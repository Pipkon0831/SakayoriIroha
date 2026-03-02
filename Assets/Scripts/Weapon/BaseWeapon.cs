using UnityEngine;

public abstract class BaseWeapon : MonoBehaviour
{
    [Header("基础配置")]
    public WeaponSO weaponConfig;
    public Transform firePoint;

    [SerializeField] protected PlayerController player;
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
    /// 最终攻击间隔 = baseInterval / (玩家攻速因子)
    /// 约定：因子 > 1 => 更快（间隔更短）
    /// </summary>
    protected virtual float GetFinalAttackInterval()
    {
        if (weaponConfig == null) return 0.5f;
        if (player == null) return weaponConfig.baseAttackInterval;

        // ✅ 统一语义：CurrentAttackSpeedBonus 是加成值（0.2=+20%）
        float totalSpeedFactor = Mathf.Max(0.1f, 1f + player.CurrentAttackSpeedBonus);
        return weaponConfig.baseAttackInterval / totalSpeedFactor;
    }

    protected abstract void Attack();

    public virtual void SetAimDirection(Vector2 dir)
    {
        if (dir == Vector2.zero || player == null) return;

        aimDirection = dir;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }

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