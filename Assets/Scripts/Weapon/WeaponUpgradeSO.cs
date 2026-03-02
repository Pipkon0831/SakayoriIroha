using UnityEngine;

public enum WeaponUpgradeType
{
    Penetration,
    ExtraProjectiles,
    BulletSize,
    ExplosionOnHit
}

[CreateAssetMenu(menuName = "Weapons/Upgrade SO", fileName = "WeaponUpgradeSO")]
public class WeaponUpgradeSO : ScriptableObject
{
    public WeaponUpgradeType type;

    [Header("通用数值（按类型使用）")]
    public int intValue = 1;            // 穿透+1 / 额外子弹+1
    public float floatValue = 1.2f;     // 子弹大小倍率（如1.2）
    public float floatValue2 = 12f;     // 散射角覆盖（可选）或爆炸半径
    public float floatValue3 = 0.6f;    // 爆炸伤害倍率

    [TextArea]
    public string desc;
}