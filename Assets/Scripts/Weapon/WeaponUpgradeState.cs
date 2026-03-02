using UnityEngine;

[System.Serializable]
public class WeaponMods
{
    [Header("穿透")]
    public int penetrateCount = 0; // 0=不穿透，1=穿透1个敌人后再销毁

    [Header("额外子弹数量（散射）")]
    public int extraProjectiles = 0; // 0=只发1发；1=总共2发
    public float spreadAngleDeg = 12f; // 总散射角（左右展开）

    [Header("子弹大小")]
    public float bulletSizeMultiplier = 1f; // 1=原大小

    [Header("命中爆炸")]
    public bool explodeOnHit = false;
    public float explosionRadius = 1.5f;
    public float explosionDamageMultiplier = 0.6f; // 爆炸伤害 = 子弹伤害 * 该倍率
}

public class WeaponUpgradeState : MonoBehaviour
{
    public WeaponMods Mods = new WeaponMods();

    // 你可以在UI里显示当前改造值
    public void ResetAll()
    {
        Mods = new WeaponMods();
    }

    public void AddPenetration(int amount)
    {
        Mods.penetrateCount = Mathf.Max(0, Mods.penetrateCount + amount);
    }

    public void AddExtraProjectiles(int amount, float? spreadAngleOverride = null)
    {
        Mods.extraProjectiles = Mathf.Max(0, Mods.extraProjectiles + amount);
        if (spreadAngleOverride.HasValue)
            Mods.spreadAngleDeg = Mathf.Max(0f, spreadAngleOverride.Value);
    }

    public void MultiplyBulletSize(float multiplier)
    {
        Mods.bulletSizeMultiplier = Mathf.Clamp(Mods.bulletSizeMultiplier * multiplier, 0.2f, 10f);
    }

    public void EnableExplosion(float radius, float dmgMul)
    {
        Mods.explodeOnHit = true;
        Mods.explosionRadius = Mathf.Clamp(radius, 0.1f, 20f);
        Mods.explosionDamageMultiplier = Mathf.Clamp(dmgMul, 0f, 10f);
    }
}