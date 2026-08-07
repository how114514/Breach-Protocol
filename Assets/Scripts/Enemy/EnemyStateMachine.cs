using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 敌人状态机：只负责”当前该做什么”的决策与状态切换，不实现具体移动/瞄准/射击。
/// 具体行为由各状态委托给对应行为组件（EnemyPatrolController / EnemyAimController /
/// EnemyShootController / EnemyMovement），状态机本身不做这些。
/// 同时承载玩家位置记录（lastKnownPlayerPosition）与玩家探测判定。
///
/// 玩家位置记录规则（统一）：
///   任何状态下，只要玩家在警戒范围内且视线无遮挡（能看到玩家），每帧统一更新
///   lastKnownPlayerPosition = 玩家当前位置；玩家不可见时停止更新（冻结最后位置）。
///   lastKnownPlayerPosition 表示"敌人最后一次确认玩家存在的位置"。
///   同时记录玩家最后一次可见的时间与当时的移动方向，供 Alert 的"模拟推理玩家位置"使用
///   （推理时只取已记录信息，不读取玩家实时位置）。
/// </summary>
public class EnemyStateMachine : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform player;                        // 玩家 Transform（必填）
    [SerializeField] private EnemyAimController aimController;        // 瞄准控制器（已有组件）
    [SerializeField] private EnemyShootController shootController;    // 射击控制器（已有组件）
    [SerializeField] private EnemyPatrolController patrolController;  // 巡逻控制器
    [SerializeField] private EnemyMovement movement;                  // 移动组件（NavMeshAgent 驱动）

    [Header("Detection")]
    [SerializeField] private float detectionRange = 15f;   // 警戒范围（米），需大于 combatMaxDistance
    [SerializeField] private float eyeHeight = 1.5f;       // 视线检测起点高度（米）
    [SerializeField] private LayerMask obstacleMask;       // 视线遮挡检测层（需包含 Obstacle 等阻挡层）

    [Header("Per-State FOV")]
    [Tooltip("巡逻视野角度（度），PatrolState.Enter 时生效。")]
    [SerializeField] private float patrolFov = 100f;       // 巡逻视野（较小、正常警觉）
    [Tooltip("警戒视野角度（度），AlertState.Enter 时生效。")]
    [SerializeField] private float alertFov = 270f;        // 警戒视野（270°：大范围搜索异常目标）
    [Tooltip("战斗视野角度（度），CombatState.Enter 时生效。")]
    [SerializeField] private float combatFov = 180f;       // 战斗视野（保持当前战斗检测逻辑）

    [Header("Alert Search")]
    [SerializeField] private float alertSearchRotateSpeed = 60f;  // 警戒搜索旋转速度（度/秒）
    [SerializeField] private float alertSearchAngle = 360f;       // 每个搜索旋转段的角度（度，默认 360：第一段顺时针一整圈，第二段逆时针一整圈）

    [Header("Alert Confirm")]
    [SerializeField] private float confirmAngle = 10f;            // 确认玩家所需对准角度（度）：视线与玩家方向误差 ≤ 该值即视为已确认
    [SerializeField] private float reactionTime = 1f;             // 确认玩家所需持续可见时间（秒，默认 1）：转向对准后玩家需持续可见该时长才确认目标

    [Header("Combat")]
    [SerializeField] private float combatMinDistance = 3f;   // 战斗理想距离下限：过近则后退/调整位置
    [SerializeField] private float combatMaxDistance = 12f;  // 战斗理想距离上限：过远则斜向靠近
    [SerializeField] private float combatMoveSpeed = 3.5f;   // 战斗调整站位移动速度（米/秒）

    [Header("Predict (模拟推理玩家位置)")]
    [Tooltip("敌人推测的玩家移动速度（米/秒）：推理距离 = 推测速度 × 自最后一次看到玩家起流逝的时间。这是模拟推理的假设值，不实时读取玩家实际速度。")]
    [SerializeField] private float playerPredictSpeed = 4f;         // 敌人推测的玩家移动速度（米/秒）
    [Tooltip("推理距离上限（米）：限制模拟推理位置不会离玩家最后已知位置过远。")]
    [SerializeField] private float playerPredictMaxDistance = 12f;  // 推理距离上限（米）

    [Header("Debug")]
    [SerializeField] private bool debugLogState = true;      // 每帧打印敌人当前状态与子状态（Debug）

    [Header("Vision Debug")]
    [Tooltip("Scene 视图视野 Debug：视野扇面 + 玩家检测结果线 + 感知信息文本。仅可视化，不改变任何检测逻辑。")]
    [SerializeField] private bool showVisionDebug = true;    // 是否显示视野可视化
    [SerializeField] private Color visionBoundaryColor = new Color(0.2f, 0.9f, 1f, 0.7f);  // 视野左右边界与 forward 射线
    [SerializeField] private Color visionConeColor = new Color(0.2f, 0.9f, 1f, 0.12f);     // 扇形内部辅助射线（半透明）
    [SerializeField] private Color canSeeColor = Color.green;      // 玩家在视野内，且无遮挡
    [SerializeField] private Color cannotSeeColor = Color.red;     // 玩家不在视野内
    [SerializeField] private Color occludedColor = Color.yellow;   // 玩家在角度范围内，但被障碍物遮挡
    [SerializeField] private Color lastKnownColor = Color.magenta; // 玩家最后已知位置标记
    [SerializeField] private int visionFanSegments = 12;           // 扇形内部辅助射线数量
    [SerializeField] private int visionArcSegments = 36;           // 检测距离弧线分段数

    private Vector3 lastKnownPlayerPosition; // 玩家最后已知位置（跨状态保留）
    private Vector3 lastSeenPlayerMoveDirection; // 玩家最后一次可见时的移动方向（水平单位向量；可见期间逐帧记录，丢失后冻结）
    private float lastSeenPlayerTime;            // 玩家最后一次可见的时间（Time.time）

    private float currentFov;                // 当前生效视野角（度），由各状态 Enter 时通过 ApplyFov 设置

    /// <summary>当前状态。</summary>
    public IEnemyState CurrentState { get; private set; }

    /// <summary>切换前的上一个状态（供新状态 Enter 判断来源；Alert 据此区分"第一次发现"与"已确认目标后寻找"）。</summary>
    public IEnemyState PreviousState { get; private set; }

    /// <summary>巡逻状态实例。</summary>
    public EnemyPatrolState PatrolState { get; private set; }

    /// <summary>警戒（调查/搜索）状态实例。</summary>
    public EnemyAlertState AlertState { get; private set; }

    /// <summary>战斗状态实例。</summary>
    public EnemyCombatState CombatState { get; private set; }

    // ---- 供各状态访问的行为组件 ----
    public Transform Player => player;
    public EnemyAimController AimController => aimController;
    public EnemyShootController ShootController => shootController;
    public EnemyPatrolController PatrolController => patrolController;
    public EnemyMovement Movement => movement;

    // ---- 玩家位置记录（统一规则） ----
    /// <summary>玩家最后已知位置 = 敌人最后一次确认玩家存在的位置（可见时持续更新，不可见时冻结）。</summary>
    public Vector3 LastKnownPlayerPosition => lastKnownPlayerPosition;

    /// <summary>玩家最后一次可见时的移动方向（水平单位向量；可见期间逐帧记录，丢失玩家后冻结）。供 Alert 模拟推理使用。</summary>
    public Vector3 LastSeenPlayerMoveDirection => lastSeenPlayerMoveDirection;

    /// <summary>玩家最后一次可见的时间（Time.time），供 Alert 计算推理距离（已流逝时间）。</summary>
    public float LastSeenPlayerTime => lastSeenPlayerTime;

    /// <summary>敌人推测的玩家移动速度（米/秒，模拟推理假设值，不读取玩家实际速度）。</summary>
    public float PlayerPredictSpeed => playerPredictSpeed;

    /// <summary>推理距离上限（米），限制模拟推理位置不会离最后已知位置过远。</summary>
    public float PlayerPredictMaxDistance => playerPredictMaxDistance;

    /// <summary>
    /// 模拟推理玩家当前位置（供 Alert 第一次搜索失败后的二次调查使用）。
    /// 不读取玩家真实位置，只用可见时记录的已知信息：
    ///   推理距离 = 推测速度 ×（当前时间 − 玩家最后出现时间），限制在最大推理距离内；
    ///   推理位置 = 玩家最后已知位置 + 玩家最后移动方向 × 推理距离。
    /// 玩家最后可见时未移动（无记录方向）→ 推理位置即为玩家最后已知位置。
    /// </summary>
    public Vector3 CalculatePredictedPlayerPosition()
    {
        float elapsed = Time.time - lastSeenPlayerTime;
        float predictedDistance = Mathf.Min(playerPredictSpeed * elapsed, playerPredictMaxDistance);
        return lastKnownPlayerPosition + lastSeenPlayerMoveDirection * predictedDistance;
    }

    // ---- 警戒搜索 ----
    /// <summary>警戒搜索旋转速度（度/秒）。</summary>
    public float AlertSearchRotateSpeed => alertSearchRotateSpeed;

    /// <summary>每个搜索旋转段的角度（度），默认 360：第一段顺时针转一圈，第二段逆时针转一圈。</summary>
    public float AlertSearchAngle => alertSearchAngle;

    /// <summary>确认玩家所需持续可见时间（秒），默认 1：转向对准玩家后，玩家需持续可见该时长才确认目标。</summary>
    public float ReactionTime => reactionTime;

    /// <summary>确认玩家所需对准角度（度）：视线与玩家方向水平误差 ≤ 该值即视为已确认。</summary>
    public float ConfirmAngle => confirmAngle;

    // ---- 战斗距离 ----
    /// <summary>战斗理想距离下限（米），距离过近时后退。</summary>
    public float CombatMinDistance => combatMinDistance;

    /// <summary>战斗理想距离上限（米），距离过远时斜向靠近。</summary>
    public float CombatMaxDistance => combatMaxDistance;

    /// <summary>战斗调整站位移动速度（米/秒）。</summary>
    public float CombatMoveSpeed => combatMoveSpeed;

    // ---- 按状态切换视野（FOV） ----
    /// <summary>巡逻视野角度（度）。</summary>
    public float PatrolFov => patrolFov;

    /// <summary>警戒视野角度（度）。</summary>
    public float AlertFov => alertFov;

    /// <summary>战斗视野角度（度）。</summary>
    public float CombatFov => combatFov;

    /// <summary>
    /// 当前生效的视野角度（度），由各状态 Enter 时设置（Patrol → patrolFov / Alert → alertFov / Combat → combatFov）。
    /// 编辑器未进入运行（currentFov 尚未被任何状态设置）时回退为 patrolFov，保证 Scene 视图调试可视化始终有值。
    /// </summary>
    public float CurrentFov => currentFov > 0f ? currentFov : patrolFov;

    /// <summary>设置当前生效的视野角度（度）。各状态 Enter 时调用；检测与 Debug 可视化统一读取 CurrentFov。</summary>
    public void ApplyFov(float fov) => currentFov = fov;

    private void Awake()
    {
        // 组件未拖引用时自动查找（四个行为组件都挂在敌人根物体上）
        if (aimController == null) aimController = GetComponent<EnemyAimController>();
        if (shootController == null) shootController = GetComponent<EnemyShootController>();
        if (patrolController == null) patrolController = GetComponent<EnemyPatrolController>();
        if (movement == null) movement = GetComponent<EnemyMovement>();

        if (aimController == null)
            Debug.LogWarning("[EnemyStateMachine] 未找到 EnemyAimController，敌人无法瞄准。", this);
        if (shootController == null)
            Debug.LogWarning("[EnemyStateMachine] 未找到 EnemyShootController，敌人无法射击。", this);
        if (patrolController == null)
            Debug.LogWarning("[EnemyStateMachine] 未找到 EnemyPatrolController，敌人不会巡逻。", this);
        if (player == null)
            Debug.LogWarning("[EnemyStateMachine] 未设置 Player 引用，敌人无法探测玩家。", this);
        if (obstacleMask.value == 0)
            Debug.LogWarning("[EnemyStateMachine] obstacleMask 未设置，视线检测将一直视为无遮挡。", this);

        PatrolState = new EnemyPatrolState();
        AlertState = new EnemyAlertState();
        CombatState = new EnemyCombatState();
        SwitchState(PatrolState);
    }

    private void Start()
    {
        // 初始化玩家位置与最后可见时间记录，避免首帧把"原点 → 玩家当前位置"误当作玩家移动方向
        if (player != null)
        {
            lastKnownPlayerPosition = player.position;
            lastSeenPlayerTime = Time.time;
        }
    }

    private void Update()
    {
        // 统一玩家位置记录：任何状态，只要能看到玩家就持续更新；看不到则停止（冻结最后位置）
        UpdateKnownPlayerPosition();

        CurrentState?.Update(this);

        // [Debug] 打印敌人当前状态与子状态（每帧输出）
        LogStateDebug();
    }

    /// <summary>
    /// 统一玩家位置记录规则：只要玩家在警戒范围内且视线无遮挡（各状态共享的"能看到玩家"判定），
    /// 每帧更新 lastKnownPlayerPosition；玩家不可见时不更新，保留最后一次确认的位置。
    /// 同时记录玩家最后一次可见的时间与当时的移动方向（供 Alert"模拟推理玩家位置"使用），
    /// 丢失玩家后一并冻结。各状态不重复维护玩家位置，统一由这里负责。
    /// </summary>
    private void UpdateKnownPlayerPosition()
    {
        if (!IsPlayerInRangeAndVisible()) return;

        Vector3 current = player.position;
        // 记录玩家最后可见时的移动方向（水平）：上一已知位置 → 当前位置。丢失后冻结，
        // 供 Alert 推理位置使用（推理时只取已记录信息，不在推理时读取玩家实时位置）。
        Vector3 delta = current - lastKnownPlayerPosition;
        delta.y = 0f;
        if (delta.sqrMagnitude > 0.0001f)
            lastSeenPlayerMoveDirection = delta.normalized;

        lastSeenPlayerTime = Time.time;
        lastKnownPlayerPosition = current;
    }

    /// <summary>[Debug] 每帧打印敌人当前状态（Patrol / Alert / Combat）与子状态。</summary>
    private void LogStateDebug()
    {
        if (!debugLogState || CurrentState == null) return;

        string state = GetStateName(CurrentState);
        string sub = GetSubStateName(CurrentState);

        Debug.Log($"[Enemy AI] State: {state} | SubState: {sub}");
    }

    /// <summary>[Debug] 状态显示名：Patrol / Alert / Combat。</summary>
    private string GetStateName(IEnemyState state)
    {
        if (state is EnemyPatrolState) return "Patrol";
        if (state is EnemyAlertState) return "Alert";
        if (state is EnemyCombatState) return "Combat";
        return state != null ? state.GetType().Name : "-";
    }

    /// <summary>[Debug] 子状态：Patrol = 移动/等待，Alert = 警戒阶段，Combat = 战斗走位模式。</summary>
    private string GetSubStateName(IEnemyState state)
    {
        if (state is EnemyAlertState alert) return alert.CurrentPhase;
        if (state is EnemyPatrolState) return patrolController != null && patrolController.IsWaiting ? "Wait" : "Move";
        if (state is EnemyCombatState combat) return combat.CurrentSubState;
        return "-";
    }

    /// <summary>切换到指定状态（相同状态忽略，避免重复 Enter/Exit）。</summary>
    public void SwitchState(IEnemyState next)
    {
        if (next == null || next == CurrentState) return;
        PreviousState = CurrentState;
        CurrentState?.Exit(this);
        CurrentState = next;
        CurrentState.Enter(this);
    }

    /// <summary>
    /// 进入警戒判定：玩家在警戒范围内 + 位于敌人前方 CurrentFov 范围内 + 视线无遮挡。
    /// 全部满足才返回 true（Patrol 巡逻 / Alert 搜索检测玩家用）。
    /// </summary>
    public bool TryDetectPlayer()
    {
        if (!IsPlayerInRangeAndVisible()) return false;
        return IsPlayerInFrontArc();
    }

    /// <summary>
    /// 玩家是否仍在警戒范围内且视线无遮挡（不限制方向，玩家绕到身后也算可见）。
    /// 用于 CombatState 判断是否丢失玩家。
    /// </summary>
    public bool IsPlayerInRangeAndVisible()
    {
        if (player == null) return false;

        Vector3 toPlayer = player.position - transform.position;
        if (toPlayer.magnitude > detectionRange) return false;

        return HasLineOfSight();
    }

    /// <summary>玩家是否位于敌人正前方 CurrentFov 范围内（忽略高度）。</summary>
    private bool IsPlayerInFrontArc()
    {
        if (player == null) return false;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.0001f) return true;

        return Vector3.Angle(transform.forward, toPlayer) <= CurrentFov * 0.5f;
    }

    /// <summary>
    /// 视线方向是否已对准玩家（水平角度误差 ≤ confirmAngle）。
    /// 用于 ConfirmPlayer 阶段判断"确认完成"：敌人已完成转头看向玩家。
    /// </summary>
    public bool IsFacingPlayer()
    {
        if (player == null) return false;
        return IsFacingPosition(player.position);
    }

    /// <summary>
    /// 视线方向是否已对准 worldPosition（水平角度误差 ≤ confirmAngle）。
    /// 用于警戒"转向玩家最后位置"阶段判断转向是否完成（目标可为玩家最后可见位置）。
    /// </summary>
    public bool IsFacingPosition(Vector3 worldPosition)
    {
        Vector3 toTarget = worldPosition - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f) return true;

        return Vector3.Angle(transform.forward, toTarget) <= confirmAngle;
    }

    /// <summary>
    /// 从视线起点（自身 + eyeHeight）向玩家发射射线检测遮挡。
    /// 命中敌人自身碰撞体则跳过；命中玩家视为可见；命中其他物体（Obstacle 等）视为被遮挡。
    /// </summary>
    private bool HasLineOfSight()
    {
        if (player == null) return false;

        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 toPlayer = player.position - origin;
        float distance = toPlayer.magnitude;
        if (distance <= 0.0001f) return false;

        RaycastHit[] hits = Physics.RaycastAll(origin, toPlayer / distance, distance, obstacleMask);
        foreach (RaycastHit hit in hits)
        {
            if (IsSelf(hit.transform)) continue;      // 跳过敌人自身碰撞体
            if (IsPlayer(hit.transform)) return true; // 命中玩家 → 可见
            return false;                             // 命中其他物体（障碍物）→ 被遮挡
        }

        return true; // 未命中任何遮挡物 → 可见
    }

    private bool IsPlayer(Transform target)
    {
        while (target != null)
        {
            if (target == player) return true;
            target = target.parent;
        }
        return false;
    }

    private bool IsSelf(Transform target)
    {
        while (target != null)
        {
            if (target == transform) return true;
            target = target.parent;
        }
        return false;
    }

    /// <summary>编辑器辅助：选中敌人时显示警戒范围。</summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }

    // ==================== 视野 Debug 可视化（仅观察，不改检测逻辑） ====================
    // 全部复用现有检测参数（detectionRange / CurrentFov / eyeHeight / obstacleMask）与
    // 现有检测方法（TryDetectPlayer / IsPlayerInFrontArc / HasLineOfSight），不新增任何判定。
    // Scene 视图默认常显；不需要时把 Inspector 里的 showVisionDebug 关掉即可。

    /// <summary>[Debug] 视野可视化：视野扇面（左/右边界 + forward + 弧线）+ 玩家检测结果线 + 感知信息文本。</summary>
    private void OnDrawGizmos()
    {
        if (!showVisionDebug) return;

        Vector3 eye = transform.position + Vector3.up * eyeHeight; // 与视线射线 HasLineOfSight 同一起点
        float halfFov = CurrentFov * 0.5f;                         // 与 IsPlayerInFrontArc 读取同一个当前 FOV

        // 1. 视野范围：左/右边界 + 中间 forward 方向 + 检测距离弧线
        Gizmos.color = visionBoundaryColor;
        Gizmos.DrawRay(eye, transform.forward * detectionRange);                                            // 中间 forward
        Gizmos.DrawRay(eye, Quaternion.Euler(0f, halfFov, 0f) * transform.forward * detectionRange);        // 右边界（正 Y 旋转 = 向右）
        Gizmos.DrawRay(eye, Quaternion.Euler(0f, -halfFov, 0f) * transform.forward * detectionRange);       // 左边界
        DrawVisionArc(eye, detectionRange, halfFov);

        // 2. 扇形内部辅助射线（半透明，直观显示 180° 视野覆盖区域）
        Gizmos.color = visionConeColor;
        for (int i = 1; i <= visionFanSegments; i++)
        {
            float t = (float)i / (visionFanSegments + 1); // 排除两端（边界已单独画出）
            float angle = Mathf.Lerp(-halfFov, halfFov, t);
            Gizmos.DrawRay(eye, Quaternion.Euler(0f, angle, 0f) * transform.forward * detectionRange);
        }

        // 3. 视线检测起点（眼位）标记
        Gizmos.color = visionBoundaryColor;
        Gizmos.DrawWireSphere(eye, 0.1f);

        // 4. 玩家检测结果：从眼位到玩家的线 + 玩家处标记（绿 / 黄 / 红）
        if (player != null)
        {
            Color detectColor = GetDetectionColor();
            Gizmos.color = detectColor;
            Gizmos.DrawLine(eye, player.position); // 与 HasLineOfSight 的射线路径一致
            Gizmos.DrawSphere(player.position, 0.25f);
        }

        // 5. 玩家最后已知位置标记（仅运行时显示，避免编辑器默认零位置误导）
        if (Application.isPlaying)
        {
            Gizmos.color = lastKnownColor;
            Gizmos.DrawWireSphere(lastKnownPlayerPosition, 0.35f);
        }

        // 6. 感知信息文本（Scene 视图中敌人上方）
        DrawPerceptionLabel(eye);
    }

    /// <summary>[Debug] 检测距离处的扇面弧线，把左右边界连起来标出视野外缘。</summary>
    private void DrawVisionArc(Vector3 origin, float range, float halfFov)
    {
        Gizmos.color = visionBoundaryColor;
        Vector3 prev = origin + Quaternion.Euler(0f, -halfFov, 0f) * transform.forward * range;
        for (int i = 1; i <= visionArcSegments; i++)
        {
            float angle = Mathf.Lerp(-halfFov, halfFov, (float)i / visionArcSegments);
            Vector3 curr = origin + Quaternion.Euler(0f, angle, 0f) * transform.forward * range;
            Gizmos.DrawLine(prev, curr);
            prev = curr;
        }
    }

    /// <summary>[Debug] 玩家检测结果颜色，复用现有检测判定，不新增逻辑：
    /// 绿 = TryDetectPlayer() 为真（视野内 + 无遮挡）；黄 = 视野角度内但在检测范围内被遮挡；红 = 其余（不在视野内）。</summary>
    private Color GetDetectionColor()
    {
        if (TryDetectPlayer()) return canSeeColor;                      // 绿：玩家在视野内，且无遮挡
        bool inFov = IsPlayerInFrontArc();                              // 复用现有角度判定
        bool inRange = (player.position - transform.position).magnitude <= detectionRange; // 复用检测距离参数
        if (inFov && inRange && !HasLineOfSight()) return occludedColor; // 黄：在角度范围内，但被障碍物遮挡
        return cannotSeeColor;                                           // 红：玩家不在视野内
    }

    /// <summary>[Debug] 敌人上方显示当前感知信息：State / 子状态（Alert 时为 Phase）/ CanSeePlayer / lastKnownPlayerPosition。</summary>
    private void DrawPerceptionLabel(Vector3 eye)
    {
#if UNITY_EDITOR
        string text = $"State: {GetStateName(CurrentState)} | Sub: {GetSubStateName(CurrentState)} | Fov: {CurrentFov:F0}°\n" +
                      $"CanSee: {TryDetectPlayer()}\n" +
                      $"LastKnown: {lastKnownPlayerPosition.ToString("F2")}";
        Handles.Label(eye + Vector3.up * 0.8f, text, PerceptionLabelStyle);
#endif
    }

#if UNITY_EDITOR
    private static GUIStyle _perceptionLabelStyle;
    private static GUIStyle PerceptionLabelStyle
    {
        get
        {
            if (_perceptionLabelStyle == null)
            {
                _perceptionLabelStyle = new GUIStyle
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                };
                _perceptionLabelStyle.normal.textColor = Color.white;
            }
            return _perceptionLabelStyle;
        }
    }
#endif
}
