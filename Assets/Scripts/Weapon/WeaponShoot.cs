using UnityEngine;
using UnityEngine.InputSystem;

public class WeaponShoot : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject bulletPrefab;   // 子弹预制体
    [SerializeField] private Transform firePoint;       // 枪口发射点（其 -Z 方向为枪口朝向）
    [SerializeField] private InputActionReference shootAction; // Input System 的开火输入（按住持续射击）

    [Header("Muzzle Flash")]
    [SerializeField] private MuzzleFlashController muzzleFlashController; // 枪口闪光控制器（粒子 + 灯光，替代自带 WFX_LightFlicker）

    [Header("Fire Rate")]
    [SerializeField] private float fireRate = 0.1f;     // 射击间隔，每隔多少秒发射一发子弹

    private float nextFireTime; // 下一次允许射击的时间（Time.time）

    private void OnEnable()
    {
        if (shootAction != null)
            shootAction.action.Enable();
    }

    private void OnDisable()
    {
        if (shootAction != null)
            shootAction.action.Disable();
    }

    private void Update()
    {
        // 按住 Shoot 且已到达下一次射击时间，则开火（每帧轮询持续输入状态）
        if (shootAction != null && shootAction.action.IsPressed() && Time.time >= nextFireTime)
        {
            Fire();
            nextFireTime = Time.time + fireRate;
        }
    }

    private void Fire()
    {
        if (firePoint == null) return;

        // 1. 触发枪口闪光：由 MuzzleFlashController 控制粒子重播 + 灯光短暂亮起并自动关闭。
        if (muzzleFlashController != null)
            muzzleFlashController.PlayFlash();

        // 2. 生成子弹
        if (bulletPrefab == null) return;

        // 子弹移动逻辑固定使用自身 +X（transform.right）方向，
        // 而枪口朝向是 firePoint 的 -Z 方向，因此需要把子弹旋转到 +X 与枪口对齐。
        // Quaternion.Euler(0, 90, 0) 在本地空间把 +X 映射到 -Z，
        // 再左乘 firePoint.rotation 继承枪口的完整朝向。
        Quaternion bulletRotation = firePoint.rotation * Quaternion.Euler(0f, 90f, 0f);

        Instantiate(bulletPrefab, firePoint.position, bulletRotation);
    }
}
