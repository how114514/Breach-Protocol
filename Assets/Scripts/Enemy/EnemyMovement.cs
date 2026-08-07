using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 敌人移动组件：统一经由 NavMeshAgent 寻路移动（未烘焙导航网格时自动回退为直接位移）。
/// - MoveTowards：向世界坐标目标寻路移动并返回是否到达（巡逻 / 警戒 / 战斗走位通用）。
/// - StopMove：停止移动（到达巡逻点 / 保持战斗距离 / 原地搜索时调用）。
/// - RotateAroundY：原地绕 Y 轴旋转（巡逻点左右观察 / 警戒 360° 搜索用）。
/// - Strafe：沿自身 right 轴左右平移（预留，当前战斗改用距离控制）。
/// 转向：默认移动时朝移动方向平滑转向（FaceMovementDirection = true）；
/// 战斗中改为由 EnemyAimController 朝玩家转向（FaceMovementDirection = false）。
/// 本组件不决定“去哪儿”，只提供位移与转向能力。
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2f;         // 巡逻/警戒移动速度（米/秒）
    [SerializeField] private float strafeSpeed = 2f;      // 战斗横向移动速度（米/秒，预留）
    [SerializeField] private float turnSpeed = 180f;      // 转向角速度（度/秒）
    [SerializeField] private float arrivalDistance = 0.15f; // 判定“到达目标点”的距离（米）

    [Header("References")]
    [SerializeField] private NavMeshAgent navAgent;       // 寻路组件（未拖引用时自动查找）

    /// <summary>是否在移动时朝移动方向转向（战斗时为 false，改由瞄准控制器朝玩家转向）。</summary>
    public bool FaceMovementDirection { get; set; } = true;

    public float MoveSpeed => moveSpeed;
    public float StrafeSpeed => strafeSpeed;
    public float ArrivalDistance => arrivalDistance;

    private void Awake()
    {
        if (navAgent == null)
            navAgent = GetComponent<NavMeshAgent>();

        if (navAgent != null)
            navAgent.updateRotation = false; // 转向由本组件 / 瞄准控制器手动控制
    }

    /// <summary>
    /// 朝世界坐标 target 移动：优先 NavMeshAgent 寻路，不可用/失败时回退为直接位移。
    /// 到达（距离 ≤ arrivalDistance）后返回 true。只在水平面移动。
    /// </summary>
    public bool MoveTowards(Vector3 target, float speed)
    {
        // 寻路优先：agent 可用、位于导航网格上、且能寻到目标 → 由 NavMeshAgent 移动
        if (navAgent != null && navAgent.enabled && navAgent.isOnNavMesh && navAgent.SetDestination(target))
        {
            navAgent.speed = speed;
            navAgent.isStopped = false;

            // 需要朝移动方向转向时才转（战斗时关闭，由瞄准控制器朝玩家转向）
            if (FaceMovementDirection)
                FaceDirection(target - transform.position);

            return IsArrived();
        }

        // 回退：直接位移（未设置 / 未烘焙导航网格时保证仍能移动）
        return MoveTowardsTransform(target, speed);
    }

    /// <summary>停止移动（保留当前朝向）。</summary>
    public void StopMove()
    {
        // 未放置到导航网格上的代理调用 isStopped / ResetPath 会报错，需先检查 isOnNavMesh
        if (navAgent != null && navAgent.enabled && navAgent.isOnNavMesh)
        {
            navAgent.isStopped = true;
            navAgent.ResetPath();
        }
    }

    /// <summary>
    /// 原地绕 Y 轴旋转：degreesPerSecond 为每秒旋转角度（正 = 向左/逆时针，负 = 向右/顺时针）。
    /// 巡逻点等待观察与警戒 360° 搜索使用。
    /// </summary>
    public void RotateAroundY(float degreesPerSecond)
    {
        transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f);
    }

    /// <summary>
    /// 显式停止所有旋转控制（搜索旋转 / 左右观察 / 确认转头 / 移动转向）。
    /// 旋转为即时模式（RotateAroundY / RotateTowards 只在被调用的那一帧写入 rotation），
    /// 调用本方法后不再执行任何旋转写入，直到下一次显式调用旋转方法；
    /// 同时清除"朝移动方向转头"的意图（需要移动朝向的状态应在其流程入口重新置 true）。
    /// 用于阶段切换时把旋转控制权交接给下一阶段（搜索 → 确认玩家 → 反应 → 战斗），杜绝旧阶段残留旋转。
    /// </summary>
    public void StopRotation()
    {
        FaceMovementDirection = false;
    }

    /// <summary>水平平滑转向世界坐标点（忽略 Y 轴），按 turnSpeed 旋转。用于警戒确认时面向玩家。</summary>
    public void RotateTowards(Vector3 worldPosition)
    {
        FaceDirection(worldPosition - transform.position);
    }

    /// <summary>水平横向平移：direction 为 -1（左）或 1（右），沿自身 right 轴移动。</summary>
    public void Strafe(float direction, float speed)
    {
        if (direction == 0f) return;
        transform.position += transform.right * (direction * speed * Time.deltaTime);
    }

    /// <summary>NavMeshAgent 是否已到达目标点。</summary>
    private bool IsArrived()
    {
        if (navAgent.pathPending) return false;            // 寻路中
        if (!navAgent.hasPath) return true;                // 无路径：已到达或无可达路径
        return navAgent.remainingDistance <= arrivalDistance;
    }

    /// <summary>朝世界坐标 target 直接位移（无寻路时的回退实现）。</summary>
    private bool MoveTowardsTransform(Vector3 target, float speed)
    {
        Vector3 pos = transform.position;
        target.y = pos.y; // 忽略高度差，只在水平面移动

        Vector3 toTarget = target - pos;
        float distance = toTarget.magnitude;
        if (distance <= arrivalDistance) return true;

        Vector3 direction = toTarget / distance;
        if (FaceMovementDirection)
            FaceDirection(direction);

        float step = Mathf.Min(speed * Time.deltaTime, distance); // 限制步长避免越过目标点
        transform.position = pos + direction * step;
        return false;
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
