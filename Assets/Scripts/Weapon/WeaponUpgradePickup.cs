using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class WeaponUpgradePickup : MonoBehaviour
{
    public WeaponUpgradeSO upgrade;

    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        var state = other.GetComponent<WeaponUpgradeState>();
        if (state == null)
        {
            Debug.LogWarning("[WeaponUpgradePickup] Player没有WeaponUpgradeState，无法应用升级。");
            return;
        }

        ApplyUpgrade(state, upgrade);
        Destroy(gameObject);
    }

    private void ApplyUpgrade(WeaponUpgradeState state, WeaponUpgradeSO up)
    {
        if (up == null) return;

        switch (up.type)
        {
            case WeaponUpgradeType.Penetration:
                state.AddPenetration(up.intValue);
                break;

            case WeaponUpgradeType.ExtraProjectiles:
                // intValue=额外子弹数；floatValue2=散射角覆盖(可选)
                // 如果你不想覆盖，就把floatValue2写成负数
                float? overrideAngle = (up.floatValue2 > 0f) ? up.floatValue2 : (float?)null;
                state.AddExtraProjectiles(up.intValue, overrideAngle);
                break;

            case WeaponUpgradeType.BulletSize:
                state.MultiplyBulletSize(Mathf.Max(0.01f, up.floatValue));
                break;

            case WeaponUpgradeType.ExplosionOnHit:
                // floatValue2=半径，floatValue3=伤害倍率
                state.EnableExplosion(up.floatValue2, up.floatValue3);
                break;
        }
    }
}