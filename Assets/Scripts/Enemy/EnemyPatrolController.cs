using UnityEngine;

/// <summary>
/// 敌人巡逻控制器：负责沿 patrolPoints 顺序循环巡逻（0 → 1 → 2 → 3 → 0 → 1 → …）。
/// 到达最后一个点后回到第 0 个点；到达每个巡逻点后等待 patrolWaitTime 秒，再走向下一个点。
/// 等待时原地左右摆动旋转观察周围（lookRotateSpeed / lookRange）。
/// 只决定"去哪儿"与节奏，实际位移与转向由 EnemyMovement（NavMeshAgent 驱动）完成。
/// </summary>
public class EnemyPatrolController : MonoBehaviour
{
    [Header("Patrol Points")]
    [Tooltip("巡逻点（建议 4 个），巡逻顺序 0→1→2→3→0→1→… 循环，到达最后一个点后回到第 0 个点。")]
    [SerializeField] private Transform[] patrolPoints;

    [Header("Patrol Settings")]
    [SerializeField] private float patrolWaitTime = 2f; // 到达巡逻点后等待时间（秒）

    [Header("Look Around")]
    [SerializeField] private float lookRotateSpeed = 60f; // 原地左右观察旋转速度（度/秒）
    [SerializeField] private float lookRange = 60f;       // 左右观察摆动范围（相对到达时朝向，度）

    [Header("References")]
    [SerializeField] private EnemyMovement movement; // 移动组件（留空则自动查找同物体上的 EnemyMovement）

    private int currentIndex;     // 当前目标巡逻点下标
    private bool isWaiting;       // 是否正在巡逻点等待
    private float waitTimer;      // 等待计时
    private float lookAngle;      // 相对到达时朝向的左右摆动偏角（度）
    private float lookDirection;  // 当前摆动方向：1 左 / -1 右

    /// <summary>是否正在巡逻点等待（原地观察），供状态机 Debug 显示巡逻子状态。</summary>
    public bool IsWaiting => isWaiting;

    private void Awake()
    {
        if (movement == null)
            movement = GetComponent<EnemyMovement>();
    }

    /// <summary>开始 / 恢复巡逻。不重置巡逻点下标，战斗结束后从上次位置继续。</summary>
    public void StartPatrol()
    {
        isWaiting = false;
        waitTimer = 0f;
        lookAngle = 0f;
        lookDirection = 1f;
    }

    /// <summary>每帧更新巡逻：移动 / 等待（原地观察）/ 切换巡逻点。由 EnemyPatrolState 调用。</summary>
    public void UpdatePatrol(float deltaTime)
    {
        if (movement == null) return;
        if (patrolPoints == null || patrolPoints.Length == 0) return;
        if (patrolPoints.Length == 1) return; // 只有 1 个巡逻点 → 原地停留

        // 到达巡逻点后等待：停止移动，原地左右观察
        if (isWaiting)
        {
            waitTimer -= deltaTime;
            UpdateLookAround(deltaTime);
            if (waitTimer <= 0f)
                isWaiting = false;
            return;
        }

        Transform target = patrolPoints[currentIndex];
        if (target == null) return;

        // 向目标点移动；到达后停止移动、进入等待并切换到下一个巡逻点
        if (movement.MoveTowards(target.position, movement.MoveSpeed))
        {
            movement.StopMove();
            isWaiting = true;
            waitTimer = patrolWaitTime;
            lookAngle = 0f;
            lookDirection = 1f;
            Advance();
        }
    }

    /// <summary>等待时原地左右摆动观察周围，从 -lookRange 到 +lookRange 往复。</summary>
    private void UpdateLookAround(float deltaTime)
    {
        if (movement == null || lookRotateSpeed <= 0f || lookRange <= 0f) return;

        lookAngle += lookDirection * lookRotateSpeed * deltaTime;
        if (lookAngle >= lookRange)      { lookAngle = lookRange;      lookDirection = -1f; }
        else if (lookAngle <= -lookRange) { lookAngle = -lookRange;     lookDirection = 1f; }

        movement.RotateAroundY(lookDirection * lookRotateSpeed);
    }

    /// <summary>切换到下一个巡逻点；到达最后一个点后回到第 0 个点（循环巡逻）。</summary>
    private void Advance()
    {
        currentIndex = (currentIndex + 1) % patrolPoints.Length;
    }

    /// <summary>编辑器辅助：选中敌人时显示巡逻点与路径。</summary>
    private void OnDrawGizmosSelected()
    {
        if (patrolPoints == null || patrolPoints.Length == 0) return;

        Gizmos.color = Color.cyan;
        Vector3? prev = null;
        foreach (Transform point in patrolPoints)
        {
            if (point == null) continue;
            Gizmos.DrawWireSphere(point.position, 0.3f);
            if (prev.HasValue)
                Gizmos.DrawLine(prev.Value, point.position);
            prev = point.position;
        }
    }
}
