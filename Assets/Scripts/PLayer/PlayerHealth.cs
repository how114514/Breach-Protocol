using UnityEngine;

/// <summary>
/// 玩家生命值管理。
/// 实现 IDamageable，作为敌人子弹等伤害来源的统一接收入口。
/// 只负责基础血量；死亡动画、游戏结束、复活等逻辑暂不实现。
/// </summary>
public class PlayerHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private float maxHealth = 100f; // 最大生命值

    private float currentHealth; // 当前生命值

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;

    private void Awake()
    {
        // 初始化为满血
        currentHealth = maxHealth;
    }

    /// <summary>
    /// 受到伤害：扣除血量，血量不低于 0（暂时不处理死亡流程）。
    /// </summary>
    public void TakeDamage(float damage)
    {
        currentHealth = Mathf.Max(0f, currentHealth - damage);
    }
}
