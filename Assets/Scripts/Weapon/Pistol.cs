using UnityEngine;

public class Pistol : BaseWeapon
{
    [Header("手枪专属")]
    [SerializeField] private GameObject bulletPrefab; // 子弹预制体
    [SerializeField] private float bulletSpeed = 20f; // 子弹速度

    protected override void Attack()
    {
        if (bulletPrefab == null || firePoint == null) return;

        // 生成子弹
        GameObject bullet = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
        Bullet bulletComp = bullet.GetComponent<Bullet>();
        
        if (bulletComp != null)
        {
            // 传递伤害和方向
            bulletComp.InitBullet(GetFinalDamage(), aimDirection, bulletSpeed);
        }
    }
}