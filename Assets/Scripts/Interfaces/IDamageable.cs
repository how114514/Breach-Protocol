/// <summary>
/// 可受伤接口：所有能受到伤害的对象实现此接口。
/// 子弹等伤害来源只通过该接口造成伤害，不依赖具体类型（EnemyHealth / PlayerHealth 等）。
/// </summary>
public interface IDamageable
{
    /// <summary>接收一次伤害。</summary>
    void TakeDamage(float damage);
}
