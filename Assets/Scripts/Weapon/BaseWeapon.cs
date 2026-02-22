using UnityEngine;

public abstract class BaseWeapon : MonoBehaviour
{
    [Header("基础配置")]
    public WeaponSO weaponConfig;
    public Transform firePoint; // 开火点（子弹/激光发射位置）
    
    [SerializeField] protected PlayerController player; // 关联玩家（可手动拖入）
    protected float attackTimer; // 攻击计时器
    protected Vector2 aimDirection; // 瞄准方向
    protected bool isActive = false; // 是否激活

    // 新增：公开只读属性，供外部访问isActive
    public bool IsActive => isActive;

    /// <summary>
    /// 初始化武器（由武器管理器调用）
    /// </summary>
    /// <param name="playerRef">玩家引用</param>
    public virtual void InitWeapon(PlayerController playerRef)
    {
        if (playerRef != null)
        {
            player = playerRef;
        }
        else if (player == null)
        {
            player = FindObjectOfType<PlayerController>(); // 兜底查找
        }
        
        if (player == null)
        {
            Debug.LogError($"[{gameObject.name}] BaseWeapon：未找到PlayerController引用！");
            return;
        }
        
        attackTimer = 0;
        isActive = false;
        gameObject.SetActive(false);
    }

    private void Update()
    {
        // ===== 核心修改1：增加战斗状态判断 =====
        // 非激活/无玩家/非战斗状态 → 直接返回，不执行攻击逻辑
        if (!isActive || player == null || !IsPlayerInCombat())
        {
            // 非战斗时重置攻击计时器，避免再次进入战斗时立即发射
            attackTimer = 0;
            return;
        }
        
        attackTimer += Time.deltaTime;
        float finalInterval = GetFinalAttackInterval();
        
        if (attackTimer >= finalInterval)
        {
            Attack();
            attackTimer = 0;
        }
    }

    /// <summary>
    /// 新增：检测玩家是否处于战斗状态
    /// </summary>
    /// <returns>true=战斗中，false=非战斗</returns>
    private bool IsPlayerInCombat()
    {
        // 容错：玩家引用为空时返回false
        if (player == null) return false;
        
        // 调用PlayerController的战斗状态检测方法（需确保PlayerController已实现该方法）
        return player.IsPlayerInCombat();
    }

    /// <summary>
    /// 计算最终攻击间隔（兼容玩家攻速）
    /// </summary>
    protected virtual float GetFinalAttackInterval()
    {
        if (player == null) return weaponConfig.baseAttackInterval;
        
        float speedBonus = Mathf.Max(0, 1 + player.CurrentAttackSpeed); // 确保不会除以0
        return weaponConfig.baseAttackInterval / speedBonus;
    }

    /// <summary>
    /// 核心攻击逻辑（子类实现）
    /// </summary>
    protected abstract void Attack();

    /// <summary>
    /// 设置瞄准方向（并旋转武器）
    /// </summary>
    public virtual void SetAimDirection(Vector2 dir)
    {
        // ===== 核心修改2：非战斗状态仍保留瞄准旋转（视觉反馈） =====
        // 仅限制isActive和方向有效性，不限制战斗状态
        if (!isActive || dir == Vector2.zero || player == null) return;
        
        aimDirection = dir;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    /// <summary>
    /// 计算最终伤害
    /// </summary>
    protected float GetFinalDamage()
    {
        if (player == null) return weaponConfig.damageMultiplier * 10; // 兜底默认伤害
        
        return player.CurrentAttack * weaponConfig.damageMultiplier;
    }

    /// <summary>
    /// 激活/禁用武器
    /// </summary>
    public virtual void SetActive(bool active)
    {
        isActive = active;
        gameObject.SetActive(active);
        if (active) attackTimer = 0; // 激活时重置计时器
    }

    // 编辑器验证（方便排查问题）
    private void OnValidate()
    {
        if (player == null)
        {
            player = FindObjectOfType<PlayerController>();
        }
    }
}