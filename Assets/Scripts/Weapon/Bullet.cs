using UnityEngine;

public class Bullet : MonoBehaviour
{
    [SerializeField] private float speed = 10f;     // 子弹飞行速度
    [SerializeField] private float lifetime = 3f;   // 子弹存活时间，超时后自动销毁

    private void Start()
    {
        // 从生成那一刻起计时，到期销毁（无需碰撞，纯定时）
        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        // 沿自身 +X（transform.right）方向移动
        transform.Translate(Vector3.right * speed * Time.deltaTime, Space.Self);
    }
}
