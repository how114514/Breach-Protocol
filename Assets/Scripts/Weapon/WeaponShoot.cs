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

    private WeaponObstacleDetector obstacleDetector; // 枪械障碍物检测（挂在 WeaponRoot 或其子/父物体上）

    private void Awake()
    {
        // 获取枪械障碍物检测组件：先查同一 GameObject，找不到再搜子物体和父物体，
        // 保证 WeaponObstacleDetector 无论挂在 WeaponRoot 还是其子/父物体上都能被引用。
        obstacleDetector = GetComponent<WeaponObstacleDetector>();
        if (obstacleDetector == null)
            obstacleDetector = GetComponentInChildren<WeaponObstacleDetector>();
        if (obstacleDetector == null)
            obstacleDetector = GetComponentInParent<WeaponObstacleDetector>();
    }

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
        // 0. 障碍物检测：开火前读取 IsBlocked，被 Obstacle 阻挡时取消开火（不生成子弹、不触发枪口闪光）。
        bool blocked = obstacleDetector != null && obstacleDetector.IsBlocked;
        if (blocked)
        {
            return;
        }

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
