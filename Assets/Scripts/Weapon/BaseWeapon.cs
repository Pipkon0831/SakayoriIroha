using UnityEngine;

public abstract class BaseWeapon : MonoBehaviour
{
    [Header("基础配置")]
    public WeaponSO weaponConfig;
    public Transform firePoint;

    [SerializeField] protected PlayerController player; // 关联玩家
    protected float attackTimer;
    protected Vector2 aimDirection;
    protected bool isActive = false;

    public bool IsActive => isActive;

    public virtual void InitWeapon(PlayerController playerRef)
    {
        if (playerRef != null) player = playerRef;
        else if (player == null) player = FindObjectOfType<PlayerController>();

        if (player == null)
        {
            Debug.LogError($"[{gameObject.name}] BaseWeapon：未找到PlayerController引用！");
            return;
        }

        attackTimer = 0f;
        isActive = false;
        gameObject.SetActive(false);
    }

    private void Update()
    {
        // 非激活/无玩家/非战斗状态 → 不攻击
        if (!isActive || player == null || !IsPlayerInCombat())
        {
            attackTimer = 0f;
            return;
        }

        attackTimer += Time.deltaTime;

        float finalInterval = GetFinalAttackInterval();
        if (attackTimer >= finalInterval)
        {
            Attack();
            attackTimer = 0f;
        }
    }

    private bool IsPlayerInCombat()
    {
        if (player == null) return false;
        return player.IsPlayerInCombat();
    }

    /// <summary>
    /// 最终攻击间隔 = baseInterval / (玩家基础攻速因子 * 本层攻速倍率)
    /// 约定：倍率 > 1 => 更快（间隔更短）
    /// </summary>
    protected virtual float GetFinalAttackInterval()
    {
        if (weaponConfig == null) return 0.5f;
        if (player == null) return weaponConfig.baseAttackInterval;

        // 1) 玩家长期属性：基础攻速因子（兼容你目前 CurrentAttackSpeed 的写法：表示“加成值”）
        float baseSpeedFactor = Mathf.Max(0.1f, 1f + player.CurrentAttackSpeed);

        // 2) 本层攻速倍率：来自 CombatModifierSystem（单层事件落点）
        float floorSpeedMultiplier = 1f;
        var cms = CombatModifierSystem.Instance;
        if (cms != null)
        {
            floorSpeedMultiplier = Mathf.Clamp(cms.playerAttackSpeedMultiplier, 0.25f, 4f);
        }

        float totalSpeedFactor = baseSpeedFactor * floorSpeedMultiplier;
        return weaponConfig.baseAttackInterval / totalSpeedFactor;
    }

    protected abstract void Attack();

    /// <summary>
    /// 设置瞄准方向（并旋转武器）
    /// 说明：你原注释写“非战斗仍可旋转”，但代码用 !isActive 直接 return，逻辑矛盾。
    /// 这里改为：允许旋转，只要方向有效且 player 不为空。
    /// </summary>
    public virtual void SetAimDirection(Vector2 dir)
    {
        if (dir == Vector2.zero || player == null) return;

        aimDirection = dir;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    /// <summary>
    /// 最终伤害 = 玩家基础攻击 * 武器倍率 * 本层伤害倍率
    /// </summary>
    protected float GetFinalDamage()
    {
        if (weaponConfig == null) return 10f;
        if (player == null) return weaponConfig.damageMultiplier * 10f;

        float floorDamageMultiplier = 1f;
        var cms = CombatModifierSystem.Instance;
        if (cms != null)
        {
            floorDamageMultiplier = Mathf.Clamp(cms.playerDamageMultiplier, 0.1f, 10f);
        }

        float baseDamage = player.CurrentAttack * weaponConfig.damageMultiplier;
        return Mathf.Max(0f, baseDamage * floorDamageMultiplier);
    }

    public virtual void SetActive(bool active)
    {
        isActive = active;
        gameObject.SetActive(active);
        if (active) attackTimer = 0f;
    }

    private void OnValidate()
    {
        if (player == null) player = FindObjectOfType<PlayerController>();
    }
}