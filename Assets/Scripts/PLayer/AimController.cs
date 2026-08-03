using UnityEngine;
using UnityEngine.InputSystem;

public class AimController : MonoBehaviour
{
    [Header("References")]
    public Camera cam;            // 玩家使用的相机
    public Transform player;      // 玩家身体根节点
    public Transform weaponRoot;  // 枪械挂点（必须是 Player 的子物体）

    [Header("Aim Settings")]
    public float aimHeight = 1f;                     // 瞄准平面高度（角色胸口/枪口高度）
    public float aimDeadZone = 0.5f;                 // 死区半径：鼠标距 Player 小于该值进入死区模式
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

        // 5. 鼠标到 Player 的水平距离，决定进入哪种瞄准模式
        Vector3 toPlayer = targetPosition - player.position;
        toPlayer.y = 0f;
        float distToPlayer = toPlayer.magnitude;

        // 6. 帧率无关的平滑系数
        float smooth = 1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime);
        Vector3 direction;

        if (distToPlayer < aimDeadZone)
        {
            // ===== 模式一：死区内 =====
            // 以 Player 位置为瞄准起点，枪的位置偏移不参与计算
            direction = targetPosition - player.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;

            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
            player.rotation = Quaternion.Slerp(player.rotation, targetRotation, smooth);
            weaponRoot.rotation = Quaternion.Slerp(weaponRoot.rotation,
                targetRotation * Quaternion.Euler(weaponRotationOffset), smooth);
        }
        else
        {
            // ===== 模式二：死区外 =====
            // 以 WeaponRoot 位置为瞄准起点，枪械精确朝向鼠标
            direction = targetPosition - weaponRoot.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;

            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
            weaponRoot.rotation = Quaternion.Slerp(weaponRoot.rotation,
                targetRotation * Quaternion.Euler(weaponRotationOffset), smooth);
            player.rotation = Quaternion.Slerp(player.rotation, targetRotation, smooth);
        }
    }
}
