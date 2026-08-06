using UnityEngine;

/// <summary>
/// 挂在 WeaponRoot 上：从 WeaponRoot 沿自身 forward 发射一条射线，检测枪械前方是否有障碍物。
/// 射线方向使用 WeaponRoot 的当前 transform.forward：
/// WeaponRoot 是枪械旋转中心，瞄准旋转时射线方向随枪械同步改变。
/// WeaponShoot 开火前可通过 <see cref="IsBlocked"/> 判断枪口是否被 Obstacle 遮挡。
/// 本组件只负责发射射线并更新 IsBlocked，不直接控制子弹生成。
/// </summary>
public class WeaponObstacleDetector : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("检测射线长度（米），可在 Inspector 调整。")]
    [SerializeField] private float rayDistance = 5f;

    // 只检测 Obstacle 层的层掩码（按层名解析，保证始终只检测 Obstacle）。
    private int obstacleMask;

    /// <summary>枪械前方 rayDistance 范围内是否被障碍物阻挡。</summary>
    public bool IsBlocked { get; private set; }

    private void Start()
    {
        // 解析 Obstacle 层；该层不存在时警告并关闭检测（不产生误阻挡）。
        int obstacleLayer = LayerMask.NameToLayer("Obstacle");
        if (obstacleLayer < 0)
        {
            Debug.LogWarning("[WeaponObstacleDetector] 找不到 \"Obstacle\" 层，障碍物检测已禁用。", this);
            obstacleMask = 0;
        }
        else
        {
            obstacleMask = 1 << obstacleLayer;
        }
    }

    private void Update()
    {
        // 每帧从 WeaponRoot 当前位置沿当前 forward 发射射线，只检测 Obstacle 层。
        // 起点跟随 WeaponRoot（武器随玩家移动）；方向随枪械瞄准旋转同步变化。
        IsBlocked = Physics.Raycast(
            transform.position,
            transform.forward,
            rayDistance,
            obstacleMask,
            QueryTriggerInteraction.Collide);

        // 场景视图可视化（仅编辑器中可见）。
        Debug.DrawRay(transform.position, transform.forward * rayDistance, Color.yellow);
    }
}
