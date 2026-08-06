using UnityEngine;

/// <summary>
/// 敌人简单移动组件：只提供水平位移与转向两个基础能力，不负责“去哪儿”。
/// - MoveTowards：向世界坐标移动并平滑转向（巡逻用）。
/// - Strafe：沿自身 right 轴左右平移（战斗横向走位用，因为敌人面向玩家，right 轴垂直于瞄准方向）。
/// 巡逻路径由 EnemyPatrolController 决定，战斗走位由 EnemyCombatState 决定。
/// </summary>
public class EnemyMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2f;        // 巡逻移动速度（米/秒）
    [SerializeField] private float strafeSpeed = 2f;      // 战斗横向移动速度（米/秒）
    [SerializeField] private float turnSpeed = 180f;      // 巡逻转向角速度（度/秒）
    [SerializeField] private float arrivalDistance = 0.15f; // 判定“到达目标点”的距离（米）

    public float MoveSpeed => moveSpeed;
    public float StrafeSpeed => strafeSpeed;
    public float ArrivalDistance => arrivalDistance;

    /// <summary>
    /// 朝世界坐标 target 水平移动；到达（距离 ≤ arrivalDistance）后返回 true。
    /// 只在水平面移动并平滑转向移动方向（供巡逻使用；战斗中瞄准旋转由 EnemyAimController 负责）。
    /// </summary>
    public bool MoveTowards(Vector3 target, float speed)
    {
        Vector3 pos = transform.position;
        target.y = pos.y; // 忽略高度差，只在水平面移动

        Vector3 toTarget = target - pos;
        float distance = toTarget.magnitude;
        if (distance <= arrivalDistance) return true;

        Vector3 direction = toTarget / distance;
        FaceDirection(direction);

        float step = Mathf.Min(speed * Time.deltaTime, distance); // 限制步长避免越过目标点
        transform.position = pos + direction * step;
        return false;
    }

    /// <summary>
    /// 水平横向平移：direction 为 -1（左）或 1（右），沿自身 right 轴移动。
    /// </summary>
    public void Strafe(float direction, float speed)
    {
        if (direction == 0f) return;
        transform.position += transform.right * (direction * speed * Time.deltaTime);
    }

    /// <summary>以固定角速度转向指定方向（忽略 Y 轴）。</summary>
    private void FaceDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        float step = turnSpeed * Time.deltaTime;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, step);
    }
}
