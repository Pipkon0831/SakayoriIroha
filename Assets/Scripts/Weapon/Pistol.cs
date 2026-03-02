using UnityEngine;

public class Pistol : BaseWeapon
{
    [Header("手枪专属")]
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private float bulletSpeed = 20f;

    private WeaponUpgradeState upgradeState;

    public override void InitWeapon(PlayerController playerRef)
    {
        base.InitWeapon(playerRef);

        if (player != null)
        {
            upgradeState = player.GetComponent<WeaponUpgradeState>();
            if (upgradeState == null)
            {
                // 自动补一个（避免你忘记挂）
                upgradeState = player.gameObject.AddComponent<WeaponUpgradeState>();
            }
        }
    }

    protected override void Attack()
    {
        if (bulletPrefab == null || firePoint == null) return;
        if (aimDirection == Vector2.zero) return;

        WeaponMods mods = (upgradeState != null) ? upgradeState.Mods : null;

        int extra = (mods != null) ? Mathf.Max(0, mods.extraProjectiles) : 0;
        int total = 1 + extra;

        float spread = (mods != null) ? Mathf.Max(0f, mods.spreadAngleDeg) : 0f;

        // total=1 时角度偏移为0；total>1 时在[-spread/2, +spread/2]均匀分布
        for (int i = 0; i < total; i++)
        {
            float t = (total == 1) ? 0.5f : (i / (float)(total - 1));
            float angleOffset = (total == 1) ? 0f : Mathf.Lerp(-spread * 0.5f, spread * 0.5f, t);

            Vector2 dir = RotateVector(aimDirection.normalized, angleOffset);

            GameObject bullet = Instantiate(bulletPrefab, firePoint.position, Quaternion.identity);

            // 让子弹朝向与速度方向一致（可选）
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            bullet.transform.rotation = Quaternion.Euler(0, 0, angle);

            Bullet bulletComp = bullet.GetComponent<Bullet>();
            if (bulletComp != null)
            {
                bulletComp.InitBullet(GetFinalDamage(), dir, bulletSpeed, mods);
            }
        }
    }

    private Vector2 RotateVector(Vector2 v, float deg)
    {
        float rad = deg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }
}