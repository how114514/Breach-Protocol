using UnityEngine;

/// <summary>
/// 敌人的生命值管理。
/// 只负责血量，不包含动画、掉落、击退等逻辑。
/// 实现 IDamageable，作为子弹等伤害来源的统一接收入口。
/// </summary>
public class EnemyHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private float maxHealth = 100f; // 最大生命值

    private float currentHealth; // 当前生命值

    private void Awake()
    {
        // 初始化为满血
        currentHealth = maxHealth;
    }

    /// <summary>
    /// 受到伤害：扣除血量，血量归零后死亡。
    /// </summary>
    public void TakeDamage(float damage)
    {
        currentHealth -= damage;

        if (currentHealth <= 0f)
            Die();
    }

    private void Die()
    {
        Destroy(gameObject);
    }
}
