using System.Collections.Generic;
using UnityEngine;

public class Bullet : MonoBehaviour
{
    private float damage;
    private Vector2 direction;
    private float speed;
    private Rigidbody2D rb;

    [Header("子弹生命周期配置")]
    [SerializeField] private float maxLifetime = 5f;
    private float lifetimeTimer;

    // mods
    private int remainingPenetration = 0;
    private bool explodeOnHit = false;
    private float explosionRadius = 1.5f;
    private float explosionDamageMultiplier = 0.6f;

    // 防止同一颗子弹在同一敌人多个碰撞体/多帧触发时重复扣血
    private readonly HashSet<int> hitEnemyInstanceIds = new HashSet<int>();

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0;

        lifetimeTimer = 0f;
    }

    private void Update()
    {
        lifetimeTimer += Time.deltaTime;
        if (lifetimeTimer >= maxLifetime)
        {
            DestroyBullet();
        }
    }

    /// <summary>
    /// 初始化子弹属性（含改造参数）
    /// </summary>
    public void InitBullet(float dmg, Vector2 dir, float spd, WeaponMods mods)
    {
        damage = dmg;
        direction = dir.normalized;
        speed = spd;

        // 应用改造
        if (mods != null)
        {
            remainingPenetration = Mathf.Max(0, mods.penetrateCount);
            explodeOnHit = mods.explodeOnHit;
            explosionRadius = mods.explosionRadius;
            explosionDamageMultiplier = mods.explosionDamageMultiplier;

            float sizeMul = Mathf.Clamp(mods.bulletSizeMultiplier, 0.2f, 10f);
            transform.localScale = transform.localScale * sizeMul;
        }

        rb.velocity = direction * speed;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Wall：无条件销毁
        if (other.CompareTag("Wall"))
        {
            DestroyBullet();
            return;
        }

        // Enemy：扣血 + 可能爆炸 + 可能穿透
        if (other.CompareTag("Enemy"))
        {
            Enemy enemy = other.GetComponent<Enemy>();
            if (enemy != null)
            {
                int id = enemy.GetInstanceID();
                if (!hitEnemyInstanceIds.Contains(id))
                {
                    hitEnemyInstanceIds.Add(id);
                    enemy.TakeDamage(damage);
                }
            }

            if (explodeOnHit)
            {
                Explode();
                DestroyBullet();
                return;
            }

            // 不爆炸：看穿透次数
            if (remainingPenetration > 0)
            {
                remainingPenetration--;
                return; // 继续飞
            }

            DestroyBullet();
        }
    }

    private void Explode()
    {
        // 对半径内的敌人造成伤害
        Collider2D[] cols = Physics2D.OverlapCircleAll(transform.position, explosionRadius);
        float explodeDmg = Mathf.Max(0f, damage * explosionDamageMultiplier);

        for (int i = 0; i < cols.Length; i++)
        {
            if (!cols[i].CompareTag("Enemy")) continue;

            var e = cols[i].GetComponent<Enemy>();
            if (e != null)
            {
                e.TakeDamage(explodeDmg);
            }
        }

        // 你想要爆炸特效的话，在这里 Instantiate VFX
    }

    private void DestroyBullet()
    {
        if (gameObject == null || !gameObject.activeInHierarchy) return;
        Destroy(gameObject);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // 方便你调爆炸半径（只在选中时显示）
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
#endif
}