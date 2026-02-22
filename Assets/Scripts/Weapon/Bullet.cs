using UnityEngine;

public class Bullet : MonoBehaviour
{
    private float damage;
    private Vector2 direction;
    private float speed;
    private Rigidbody2D rb;

    [Header("子弹生命周期配置")]
    [SerializeField] private float maxLifetime = 5f; // 最大存活时间（秒）
    private float lifetimeTimer; // 生命周期计时器

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
        }
        rb.gravityScale = 0; // 2D子弹无重力

        // 初始化生命周期计时器
        lifetimeTimer = 0;
    }

    private void Update()
    {
        // 累计生命周期时间
        lifetimeTimer += Time.deltaTime;
        
        // 超过最大存活时间 → 自动销毁
        if (lifetimeTimer >= maxLifetime)
        {
            DestroyBullet();
        }
    }

    /// <summary>
    /// 初始化子弹属性
    /// </summary>
    public void InitBullet(float dmg, Vector2 dir, float spd)
    {
        damage = dmg;
        direction = dir;
        speed = spd;

        // 发射子弹
        rb.velocity = direction * speed;
    }

    /// <summary>
    /// 子弹碰撞检测
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        // 1. 检测敌人（原有逻辑保留）
        if (other.CompareTag("Enemy"))
        {
            Enemy enemy = other.GetComponent<Enemy>();
            if (enemy != null)
            {
                enemy.TakeDamage(damage);
            }
            DestroyBullet(); // 击中敌人后销毁
        }

        // 2. 新增：检测Wall标签 → 立即销毁
        if (other.CompareTag("Wall"))
        {
            DestroyBullet();
        }
    }

    /// <summary>
    /// 安全销毁子弹（避免重复销毁报错）
    /// </summary>
    private void DestroyBullet()
    {
        // 容错：如果物体已经被销毁/标记为销毁，直接返回
        if (gameObject == null || !gameObject.activeInHierarchy)
        {
            return;
        }
        
        Destroy(gameObject);
    }

    // 可选：场景切换/子弹被禁用时，强制销毁（防止内存泄漏）
    private void OnDisable()
    {
        DestroyBullet();
    }
}