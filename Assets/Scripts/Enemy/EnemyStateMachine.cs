using UnityEngine;

/// <summary>
/// 敌人状态机：只负责“当前该做什么”的决策与状态切换，不实现具体移动/瞄准/射击。
/// 具体行为由各状态委托给对应行为组件（EnemyPatrolController / EnemyAimController /
/// EnemyShootController / EnemyMovement），状态机本身不做这些。
/// 同时承载进入战斗的玩家探测（距离 + 前方角度 + 视线）与战斗期间丢失玩家的判定入口。
/// </summary>
public class EnemyStateMachine : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform player;                        // 玩家 Transform（必填）
    [SerializeField] private EnemyAimController aimController;        // 瞄准控制器（已有组件）
    [SerializeField] private EnemyShootController shootController;    // 射击控制器（已有组件）
    [SerializeField] private EnemyPatrolController patrolController;  // 巡逻控制器
    [SerializeField] private EnemyMovement movement;                  // 移动组件

    [Header("Detection")]
    [SerializeField] private float detectionRange = 15f;   // 警戒范围（米）
    [SerializeField] private float detectionFov = 180f;    // 前方警戒角度（180 = 正前方整个半球）
    [SerializeField] private float eyeHeight = 1.5f;       // 视线检测起点高度（米）
    [SerializeField] private LayerMask obstacleMask;       // 视线遮挡检测层（需包含 Obstacle 等阻挡层）

    [Header("Lost Target")]
    [SerializeField] private float lostTargetTime = 10f;   // 丢失玩家多久后回到巡逻（秒）

    /// <summary>当前状态。</summary>
    public IEnemyState CurrentState { get; private set; }

    /// <summary>巡逻状态实例。</summary>
    public EnemyPatrolState PatrolState { get; private set; }

    /// <summary>战斗状态实例。</summary>
    public EnemyCombatState CombatState { get; private set; }

    // ---- 供各状态访问的行为组件 ----
    public Transform Player => player;
    public EnemyAimController AimController => aimController;
    public EnemyShootController ShootController => shootController;
    public EnemyPatrolController PatrolController => patrolController;
    public EnemyMovement Movement => movement;
    public float LostTargetTime => lostTargetTime;

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
        CombatState = new EnemyCombatState();
        SwitchState(PatrolState);
    }

    private void Update()
    {
        CurrentState?.Update(this);
    }

    /// <summary>切换到指定状态（相同状态忽略，避免重复 Enter/Exit）。</summary>
    public void SwitchState(IEnemyState next)
    {
        if (next == null || next == CurrentState) return;
        CurrentState?.Exit(this);
        CurrentState = next;
        CurrentState.Enter(this);
    }

    /// <summary>
    /// 进入战斗判定：玩家在警戒范围内 + 位于敌人前方 detectionFov 范围内 + 视线无遮挡。
    /// 全部满足才返回 true（PatrolState → CombatState）。
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

    /// <summary>玩家是否位于敌人正前方 detectionFov 范围内（忽略高度）。</summary>
    private bool IsPlayerInFrontArc()
    {
        if (player == null) return false;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.0001f) return true;

        return Vector3.Angle(transform.forward, toPlayer) <= detectionFov * 0.5f;
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
}
