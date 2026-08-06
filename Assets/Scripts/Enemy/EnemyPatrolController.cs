using UnityEngine;

/// <summary>
/// 敌人巡逻控制器：负责沿 patrolPoints 顺序循环巡逻（0 → 1 → 2 → 3 → 0 → 1 → …）。
/// 到达最后一个点后回到第 0 个点；到达每个巡逻点后等待 patrolWaitTime 秒，再走向下一个点。
/// 只决定”去哪儿”与节奏，实际位移与转向由 EnemyMovement 完成。
/// </summary>
public class EnemyPatrolController : MonoBehaviour
{
    [Header("Patrol Points")]
    [Tooltip("巡逻点（建议 4 个），巡逻顺序 0→1→2→3→0→1→… 循环，到达最后一个点后回到第 0 个点。")]
    [SerializeField] private Transform[] patrolPoints;

    [Header("Patrol Settings")]
    [SerializeField] private float patrolWaitTime = 2f; // 到达巡逻点后等待时间（秒）

    [Header("References")]
    [SerializeField] private EnemyMovement movement; // 移动组件（留空则自动查找同物体上的 EnemyMovement）

    private int currentIndex;   // 当前目标巡逻点下标
    private bool isWaiting;     // 是否正在巡逻点等待
    private float waitTimer;    // 等待计时

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
    }

    /// <summary>每帧更新巡逻：移动 / 等待 / 切换巡逻点。由 EnemyPatrolState 调用。</summary>
    public void UpdatePatrol(float deltaTime)
    {
        if (movement == null) return;
        if (patrolPoints == null || patrolPoints.Length == 0) return;
        if (patrolPoints.Length == 1) return; // 只有 1 个巡逻点 → 原地停留

        // 到达巡逻点后等待
        if (isWaiting)
        {
            waitTimer -= deltaTime;
            if (waitTimer <= 0f)
                isWaiting = false;
            return;
        }

        Transform target = patrolPoints[currentIndex];
        if (target == null) return;

        // 向目标点移动；到达后进入等待并切换到下一个巡逻点
        if (movement.MoveTowards(target.position, movement.MoveSpeed))
        {
            isWaiting = true;
            waitTimer = patrolWaitTime;
            Advance();
        }
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
