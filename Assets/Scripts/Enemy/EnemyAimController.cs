using UnityEngine;

/// <summary>
/// 敌人的平滑瞄准控制器。
/// 以武器/枪口作为主要瞄准参考：身体以固定角速度水平转向玩家，枪械直接继承身体旋转并应用模型偏移，无延迟同步跟随。
/// 仅当视线无遮挡（能看到玩家）时才进行瞄准；被障碍物阻挡则停止瞄准。
/// 不包含射击、攻击、寻路、AI状态机等逻辑。
/// </summary>
public class EnemyAimController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform player;      // 玩家 Transform
    [SerializeField] private Transform weaponRoot;  // 武器挂点（Enemy 的子物体），为空时回退为身体瞄准
    [SerializeField] private Transform eyePoint;    // 视线检测起点（优先于 weaponRoot），为空时用 weaponRoot/自身

    [Header("Aim Settings")]
    [SerializeField] private float aimRotationSpeed = 180f;                          // 每秒最大旋转角度（度/秒）
    [SerializeField] private Vector3 weaponRotationOffset = new Vector3(0, 180, 0);  // 枪械模型朝向修正

    [Header("Vision")]
    [SerializeField] private LayerMask obstacleMask; // 障碍物/玩家所在的 LayerMask（用于视线检测）

    [Header("Shooting")]
    [SerializeField] private float shootAngleThreshold = 5f; // 可开火的最大瞄准误差（角度）

    /// <summary>是否可以开火：能看到玩家 且 瞄准误差小于 shootAngleThreshold（供射击控制器读取）。</summary>
    public bool CanShootPlayer { get; private set; }

    private void Update()
    {
        // 每帧重置开火判定，满足条件时才置为 true
        CanShootPlayer = false;

        // 没有玩家时不进行任何旋转
        if (player == null) return;

        // 视线被遮挡（看不到玩家）→ 不瞄准、不旋转枪械、不旋转身体
        if (!HasLineOfSight()) return;

        // 瞄准起点：优先使用武器/枪口位置，无武器时回退到自身位置
        Transform aimPoint = weaponRoot != null ? weaponRoot : transform;

        // 1. 以枪口为起点，计算指向玩家的水平方向（忽略 Y 轴高度差）
        Vector3 direction = player.position - aimPoint.position;
        direction.y = 0f;

        // 玩家与枪口几乎重合时无法求方向，保持当前朝向
        if (direction.sqrMagnitude < 0.0001f) return;

        // 2. 目标旋转 = 瞄准方向（枪口→玩家，水平）
        Quaternion aimRotation = Quaternion.LookRotation(direction.normalized);

        // 3. 身体用固定角速度旋转向目标（限制转身速度）
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, aimRotation, aimRotationSpeed * Time.deltaTime);

        // 4. 枪械直接继承身体旋转 + 模型偏移（无延迟同步跟随，避免枪械滞后穿模）
        //    不再对枪械单独做平滑旋转，枪械旋转速度与身体完全一致
        if (weaponRoot != null)
        {
            weaponRoot.rotation = transform.rotation * Quaternion.Euler(weaponRotationOffset);
        }

        // 射击判定：身体朝向（枪械继承身体旋转）与指向玩家的水平方向夹角小于阈值 → 可开火
        CanShootPlayer = Vector3.Angle(transform.forward, direction.normalized) < shootAngleThreshold;
    }

    /// <summary>
    /// 从视线起点向玩家发射射线检测遮挡。
    /// 视线起点：eyePoint > weaponRoot > 自身。
    /// 跳过敌人自身碰撞体；命中玩家视为可见，命中其他物体视为被遮挡。
    /// </summary>
    private bool HasLineOfSight()
    {
        Transform eye = eyePoint != null ? eyePoint : (weaponRoot != null ? weaponRoot : transform);

        Vector3 toPlayer = player.position - eye.position;
        float distance = toPlayer.magnitude;

        // 与玩家重合，无法判定视线
        if (distance <= 0.0001f) return false;

        // 发射射线检测玩家与障碍物（obstacleMask 需包含玩家层与墙体等阻挡层）
        RaycastHit[] hits = Physics.RaycastAll(eye.position, toPlayer / distance, distance, obstacleMask);

        foreach (RaycastHit hit in hits)
        {
            // 跳过敌人自身（身体/武器）的碰撞体，避免起点紧贴自身碰撞体导致永远判为遮挡
            if (IsSelf(hit.transform)) continue;

            // 命中的是玩家（或其子物体）→ 可见
            if (IsPlayer(hit.transform)) return true;

            // 命中其他物体（障碍物）→ 被遮挡
            return false;
        }

        // 未命中任何非自身物体 → 视线无遮挡，可见
        return true;
    }

    /// <summary>判断 target 是否为玩家或其子物体。</summary>
    private bool IsPlayer(Transform target)
    {
        while (target != null)
        {
            if (target == player) return true;
            target = target.parent;
        }
        return false;
    }

    /// <summary>判断 target 是否为敌人自身或其子物体。</summary>
    private bool IsSelf(Transform target)
    {
        while (target != null)
        {
            if (target == transform) return true;
            target = target.parent;
        }
        return false;
    }
}
