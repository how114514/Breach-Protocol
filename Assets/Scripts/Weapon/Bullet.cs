using UnityEngine;

public class Bullet : MonoBehaviour
{
    [SerializeField] private float speed = 10f;          // 子弹飞行速度
    [SerializeField] private float lifetime = 3f;        // 子弹存活时间，超时后自动销毁
    [SerializeField] private float bulletRadius = 0.05f; // Swept 检测球体半径（与子弹碰撞截面相当）
    [SerializeField] private GameObject owner;           // 子弹归属者（未来由武器生成时设置），命中归属者时忽略

    private float damage;             // 单发伤害（从 BulletDamage 读取，单一数据来源）
    private LayerMask hitMask;        // 需要检测的层：Enemy + Obstacle
    private Vector3 previousPosition; // 子弹上一帧位置，用于 Swept 路径检测

    /// <summary>
    /// 设置/读取子弹归属者：玩家子弹设为玩家，敌人子弹设为敌人，命中归属者时忽略，避免自伤。
    /// </summary>
    public GameObject Owner
    {
        get => owner;
        set => owner = value;
    }

    private void Start()
    {
        // 从生成那一刻起计时，到期销毁（无需碰撞，纯定时）
        Destroy(gameObject, lifetime);

        // 获取 BulletDamage 的伤害数值（该组件只提供数据，不做检测），保证单一伤害来源
        if (TryGetComponent<BulletDamage>(out BulletDamage bulletDamage))
            damage = bulletDamage.Damage;

        // 构建命中层掩码：检测 Enemy、Obstacle、Player（若 Player 层存在）；
        // 子弹自身在 PlayerBullet 层，不会被自己命中；归属者判定由 Owner 处理。
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        int obstacleLayer = LayerMask.NameToLayer("Obstacle");
        int playerLayer = LayerMask.NameToLayer("Player");
        if (enemyLayer >= 0) hitMask |= 1 << enemyLayer;
        if (obstacleLayer >= 0) hitMask |= 1 << obstacleLayer;
        if (playerLayer >= 0) hitMask |= 1 << playerLayer;

        previousPosition = transform.position;
    }

    private void Update()
    {
        // 1. 计算本帧移动距离与目标位置
        float moveDistance = speed * Time.deltaTime;
        if (moveDistance <= 0f)
            return;

        Vector3 newPosition = previousPosition + transform.right * moveDistance;

        // 2. Swept 路径检测：在上一帧位置到本帧目标位置之间扫一颗球，
        //    覆盖整段移动轨迹，避免高速子弹穿过墙壁或漏掉路径上的敌人。
        Vector3 moveDirection = (newPosition - previousPosition).normalized;
        if (Physics.SphereCast(
                previousPosition, bulletRadius, moveDirection,
                out RaycastHit hit, moveDistance, hitMask,
                QueryTriggerInteraction.Collide))
        {
            // 3. 命中自己的拥有者：忽略，子弹继续飞行（避免玩家子弹伤玩家、敌人子弹伤敌人）
            if (!IsOwner(hit.collider))
            {
                // 4. 获取目标的 IDamageable：可受伤对象造成伤害；纯障碍物不受伤，仅拦截子弹
                IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
                if (damageable != null)
                    damageable.TakeDamage(damage);

                // 5. 命中后销毁子弹
                Destroy(gameObject);
                return;
            }
        }

        // 6. 路径无碰撞（或仅命中拥有者）：正常移动到目标位置
        transform.position = newPosition;
        previousPosition = transform.position;
    }

    /// <summary>判断碰撞体是否属于子弹的拥有者（拥有者自身或其子物体）。</summary>
    private bool IsOwner(Collider collider)
    {
        if (owner == null)
            return false;
        return collider.transform.IsChildOf(owner.transform);
    }
}
