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

    public void InitBullet(float dmg, Vector2 dir, float spd, WeaponMods mods)
    {
        damage = dmg;
        direction = dir.normalized;
        speed = spd;

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
        if (other.CompareTag("Wall"))
        {
            DestroyBullet();
            return;
        }

        if (other.CompareTag("Enemy"))
        {
            Enemy enemy = other.GetComponent<Enemy>();
            if (enemy != null)
            {
                int id = enemy.GetInstanceID();
                if (!hitEnemyInstanceIds.Contains(id))
                {
                    hitEnemyInstanceIds.Add(id);

                    // ✅ 命中统计：只记首次命中同一敌人
                    NPCRunFloorStats.Instance?.RecordHitEnemy(1);

                    enemy.TakeDamage(damage);
                }
            }

            if (explodeOnHit)
            {
                Explode();
                DestroyBullet();
                return;
            }

            if (remainingPenetration > 0)
            {
                remainingPenetration--;
                return;
            }

            DestroyBullet();
        }
    }

    private void Explode()
    {
        Collider2D[] cols = Physics2D.OverlapCircleAll(transform.position, explosionRadius);
        float explodeDmg = Mathf.Max(0f, damage * explosionDamageMultiplier);

        for (int i = 0; i < cols.Length; i++)
        {
            if (!cols[i].CompareTag("Enemy")) continue;

            var e = cols[i].GetComponent<Enemy>();
            if (e != null)
            {
                // 如果你想让爆炸也计入命中，可在这里 RecordHitEnemy
                e.TakeDamage(explodeDmg);
            }
        }
    }

    private void DestroyBullet()
    {
        if (gameObject == null || !gameObject.activeInHierarchy) return;
        Destroy(gameObject);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
#endif
}