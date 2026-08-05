using UnityEngine;

/// <summary>
/// 子弹伤害数据组件：只持有伤害数值，不做碰撞检测、不主动造成伤害。
/// 命中检测与伤害流程统一由 Bullet.cs 的 SphereCast 负责，
/// 本组件仅作为伤害数值的唯一来源，避免重复伤害逻辑。
/// </summary>
public class BulletDamage : MonoBehaviour
{
    [SerializeField] private float damage = 25f; // 单发伤害

    /// <summary>单发伤害（供 Bullet.cs 读取）</summary>
    public float Damage => damage;
}
