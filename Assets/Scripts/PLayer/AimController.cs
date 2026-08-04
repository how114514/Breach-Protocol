using UnityEngine;
using UnityEngine.InputSystem;

public class AimController : MonoBehaviour
{
    [Header("References")]
    public Camera cam;            // 玩家使用的相机
    public Transform player;      // 玩家身体根节点
    public Transform weaponRoot;  // 枪械挂点（Player 的子物体）

    [Header("Aim Settings")]
    public float aimHeight = 1f;                     // 瞄准平面高度（角色胸口/枪口高度）
    public float aimDeadZone = 0.5f;                 // 完全由玩家方向控制的范围（鼠标距玩家小于此值）
    public float aimTransitionRange = 1.0f;          // 两个方向融合的过渡范围（超过 deadZone+range 后武器方向 100%）
    public Vector3 weaponRotationOffset = new Vector3(0, 180, 0); // 枪械模型朝向修正

    [Header("Smooth")]
    public float rotationSmoothSpeed = 10f;          // 旋转平滑速度，越大越快

    private void Update()
    {
        if (cam == null || player == null || weaponRoot == null) return;

        // 1. 获取鼠标屏幕坐标（新输入系统）
        Vector2 mouseScreenPos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

        // 2. 屏幕坐标 -> 世界射线
        Ray ray = cam.ScreenPointToRay(mouseScreenPos);

        // 3. 用水平平面模拟角色胸口高度的瞄准面
        Plane aimPlane = new Plane(Vector3.up, new Vector3(0, aimHeight, 0));

        // 4. 射线与平面求交，得到鼠标在 Y = aimHeight 高度上的世界位置
        if (!aimPlane.Raycast(ray, out float enter)) return;
        Vector3 targetPosition = ray.GetPoint(enter);

        // 5. 同时计算两个瞄准方向（都忽略 Y 轴）
        Vector3 playerAim = targetPosition - player.position;
        playerAim.y = 0f;
        Vector3 weaponAim = targetPosition - weaponRoot.position;
        weaponAim.y = 0f;

        // 目标点与玩家/枪口重合时无法求方向，保持当前朝向
        if (playerAim.sqrMagnitude < 0.0001f && weaponAim.sqrMagnitude < 0.0001f) return;

        // 归一化（若其中一个退化，用另一个兜底）
        Vector3 playerDir = playerAim.sqrMagnitude > 0.0001f ? playerAim.normalized : weaponAim.normalized;
        Vector3 weaponDir = weaponAim.sqrMagnitude > 0.0001f ? weaponAim.normalized : playerAim.normalized;

        // 6. 根据鼠标距 Player 的距离计算武器方向的权重（连续过渡，无硬切换）
        float distToPlayer = playerAim.magnitude;
        float weaponWeight;
        if (aimTransitionRange <= 0.0001f)
            weaponWeight = distToPlayer >= aimDeadZone ? 1f : 0f;  // 兼容 transitionRange=0 的极端情况
        else
            weaponWeight = Mathf.Clamp01((distToPlayer - aimDeadZone) / aimTransitionRange);
        // dist <= deadZone            -> weaponWeight = 0  （100% 玩家方向）
        // dist >= deadZone + range    -> weaponWeight = 1  （100% 武器方向）
        // 中间区域                     -> 线性平滑过渡

        // 7. 融合两个方向，得到最终瞄准方向
        Vector3 finalAim = Vector3.Slerp(playerDir, weaponDir, weaponWeight);

        // 8. 基础瞄准旋转（纯 Y 轴）再叠加武器模型偏移
        Quaternion targetRotation = Quaternion.LookRotation(finalAim);
        Quaternion targetWeaponRotation = targetRotation * Quaternion.Euler(weaponRotationOffset);

        // 9. 帧率无关的平滑系数
        float smooth = 1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime);

        // 10. 平滑插值（Player 与 WeaponRoot 同步到同一融合方向）
        player.rotation = Quaternion.Slerp(player.rotation, targetRotation, smooth);
        weaponRoot.rotation = Quaternion.Slerp(weaponRoot.rotation, targetWeaponRotation, smooth);
    }
}
