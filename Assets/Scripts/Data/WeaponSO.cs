using UnityEngine;

[CreateAssetMenu(fileName = "Weapon_", menuName = "Game/Weapon Config")]
public class WeaponSO : ScriptableObject
{
    public enum WeaponType { Pistol, Shotgun, SubmachineGun, Laser, Sniper }
    
    public WeaponType weaponType;
    [Header("基础属性")]
    public float damageMultiplier; // 伤害修正参数
    public float baseAttackInterval; // 基础攻击间隔（激光枪无效）
    
    [Header("散弹枪专属")]
    public int shotgunBulletCount = 8; // 每发子弹数
    [Range(0, 30)] public float shotgunSpreadAngle = 15f; // 弹道偏移角度
    
    [Header("激光枪专属")]
    public float laserCheckInterval = 0.1f; // 全局固定伤害判定间隔
    public float laserLength = 10f; // 激光长度
    public bool isLaserPenetrate = true; // 是否穿透
    
    [Header("狙击枪专属")]
    public float sniperRange = 20f; // 狙击射程
}