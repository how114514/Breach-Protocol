using UnityEngine;

/// <summary>
/// 敌人射击控制器：由 EnemyAimController 判断是否可以开火，本组件只负责射击间隔与生成子弹。
/// 不监听玩家输入、不读取鼠标；复用 Bullet 预制体与 BulletDamage 数据。
/// </summary>
public class EnemyShootController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyAimController aimController; // 瞄准控制器，提供可开火判定
    [SerializeField] private Transform firePoint;              // 枪口发射点（其 -Z 方向为枪口朝向）
    [SerializeField] private GameObject bulletPrefab;          // 子弹预制体（复用玩家同款）
    [SerializeField] private Transform owner;                  // 子弹归属者（敌人自身），未设置时默认为本物体，避免自伤

    [Header("Fire Rate")]
    [SerializeField] private float fireRate = 1f;              // 射击间隔（秒）

    private float nextFireTime; // 下一次允许射击的时间（Time.time）

    private void Update()
    {
        // 瞄准完成且已到达下一次射击时间 → 开火
        if (aimController != null && aimController.CanShootPlayer && Time.time >= nextFireTime)
        {
            Shoot();
            nextFireTime = Time.time + fireRate;
        }
    }

    /// <summary>生成一发子弹，生成逻辑与玩家 WeaponShoot 保持一致。</summary>
    private void Shoot()
    {
        if (firePoint == null || bulletPrefab == null) return;

        // 子弹移动逻辑固定使用自身 +X（transform.right）方向，
        // 而枪口朝向是 firePoint 的 -Z 方向，因此需要把子弹旋转到 +X 与枪口对齐。
        Quaternion bulletRotation = firePoint.rotation * Quaternion.Euler(0f, 90f, 0f);

        Bullet bullet = Instantiate(bulletPrefab, firePoint.position, bulletRotation).GetComponent<Bullet>();

        // 设置归属者：命中敌人自身时忽略，避免敌人子弹伤害自己
        if (bullet != null)
            bullet.Owner = (owner != null ? owner : transform).gameObject;
    }
}
