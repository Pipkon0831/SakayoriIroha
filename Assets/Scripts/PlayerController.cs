using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("移动设置")]
    [SerializeField] private float moveSpeed = 5f; // 移动速度
    [SerializeField] private float moveSmooth = 0.1f; // 移动平滑系数（越小越灵敏）
    [SerializeField] private GameController gameController;

    [Header("玩家属性配置")] // 新增：属性配置面板
    [SerializeField] private float baseAttack = 10f; // 初始攻击
    [SerializeField] private float baseMaxHP = 100f; // 初始最大血量
    [SerializeField] private float baseAttackSpeed = 0.5f; // 初始攻速加成（0.5=50%）
    [SerializeField] private float upgradeBonusRate = 0.1f; // 升级加成比例（10%）

    [Header("武器系统")] // 新增：武器相关引用
    [SerializeField] private WeaponManager weaponManager;
    [SerializeField] private Camera mainCamera;

    // 原有移动相关变量
    private Vector2 inputDirection;
    private Vector2 smoothInputVelocity;
    private Vector2 currentVelocity;

    // 原有属性定义（保留）
    public float CurrentAttack { get; private set; }
    public float CurrentAttackSpeed { get; private set; }

    // 新增：缺失的核心属性（经验、等级、血量）
    public float CurrentExp { get; private set; }
    public float ExpToNextLevel { get; private set; }
    public float MaxHP { get; private set; }
    public float CurrentHP { get; private set; }
    public int Level { get; private set; }
    public float CurrentMoveSpeed { get; private set; } // 升级后变化的移动速度

    private void Awake()
    {
        if (gameController == null)
        {
            gameController = FindObjectOfType<GameController>();
        }

        // 新增：初始化玩家属性（等级、经验、血量、攻击等）
        InitPlayerStats();

        // 新增：初始化相机和武器管理器
        InitWeaponSystem();
    }

    private void Update()
    {
        GetPlayerInput();
        CalculateSmoothMovement();

        if (gameController.IsPlayerLockedInRoom())
        {
            currentVelocity = ValidateMovement(currentVelocity);
        }

        ApplyMovement();

        // 新增：鼠标瞄准 + 升级检测（不影响原有移动逻辑）
        AimAtMouse();
        CheckLevelUp();
    }

    // ===================== 原有移动逻辑（完全保留，未修改） =====================
    private void GetPlayerInput()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        inputDirection = new Vector2(horizontal, vertical).normalized;
    }

    private void CalculateSmoothMovement()
    {
        inputDirection = Vector2.SmoothDamp(
            inputDirection,
            Vector2.zero,
            ref smoothInputVelocity,
            moveSmooth
        );
        currentVelocity = inputDirection * moveSpeed;
    }

    private Vector2 ValidateMovement(Vector2 desiredVelocity)
    {
        RoomData currentRoom = gameController.GetCurrentPlayerRoom();
        if (currentRoom == null || currentRoom.floorPositions == null) return desiredVelocity;

        Vector3 targetPos = transform.position + new Vector3(desiredVelocity.x, desiredVelocity.y, 0) * Time.deltaTime;
        Vector2Int targetGridPos = new Vector2Int(
            Mathf.FloorToInt(targetPos.x),
            Mathf.FloorToInt(targetPos.y)
        );

        bool isTargetValid = currentRoom.floorPositions.Contains(targetGridPos);
        if (isTargetValid)
        {
            return desiredVelocity;
        }
        else
        {
            Vector2 validVelocity = Vector2.zero;
            Vector3 targetPosX = transform.position + new Vector3(desiredVelocity.x, 0, 0) * Time.deltaTime;
            Vector2Int targetGridX = new Vector2Int(Mathf.FloorToInt(targetPosX.x), Mathf.FloorToInt(transform.position.y));
            if (currentRoom.floorPositions.Contains(targetGridX))
            {
                validVelocity.x = desiredVelocity.x;
            }

            Vector3 targetPosY = transform.position + new Vector3(0, desiredVelocity.y, 0) * Time.deltaTime;
            Vector2Int targetGridY = new Vector2Int(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(targetPosY.y));
            if (currentRoom.floorPositions.Contains(targetGridY))
            {
                validVelocity.y = desiredVelocity.y;
            }
            return validVelocity;
        }
    }

    private void ApplyMovement()
    {
        transform.Translate(currentVelocity * Time.deltaTime);
    }

    private void OnDrawGizmos()
    {
        if (gameController != null && gameController.IsPlayerLockedInRoom())
        {
            RoomData currentRoom = gameController.GetCurrentPlayerRoom();
            if (currentRoom != null && currentRoom.floorPositions != null)
            {
                Gizmos.color = Color.red;
                foreach (var pos in currentRoom.floorPositions)
                {
                    Gizmos.DrawWireCube(new Vector3(pos.x + 0.5f, pos.y + 0.5f, 0), Vector3.one * 0.9f);
                }
            }
        }
    }

    // ===================== 新增：缺失的核心方法和逻辑 =====================
    /// <summary>
    /// 初始化玩家基础属性（解决AddExp、等级相关报错）
    /// </summary>
    private void InitPlayerStats()
    {
        Level = 1;
        CurrentExp = 0;
        ExpToNextLevel = 100; // 1级升级所需经验
        
        // 初始化基础属性（升级加成：每级提升10%）
        float upgradeMultiplier = Mathf.Pow(1 + upgradeBonusRate, Level - 1);
        CurrentAttack = baseAttack * upgradeMultiplier;
        CurrentAttackSpeed = baseAttackSpeed; // 攻速不升级
        CurrentMoveSpeed = moveSpeed * upgradeMultiplier;
        MaxHP = baseMaxHP * upgradeMultiplier;
        CurrentHP = MaxHP; // 初始满血
    }

    /// <summary>
    /// 初始化武器系统（相机+武器管理器）
    /// </summary>
    private void InitWeaponSystem()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
        if (weaponManager != null)
        {
            weaponManager.InitWeaponManager(this);
        }
    }

    /// <summary>
    /// 鼠标瞄准（传递方向给武器管理器）
    /// </summary>
    private void AimAtMouse()
    {
        if (mainCamera == null || weaponManager == null) return;
        
        Vector2 mouseWorldPos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector2 aimDirection = (mouseWorldPos - (Vector2)transform.position).normalized;
        
        if (aimDirection != Vector2.zero)
        {
            weaponManager.SetAimDirection(aimDirection);
        }
    }

    /// <summary>
    /// 敌人调用的加经验方法（解决“无法解析AddExp”报错）
    /// </summary>
    public void AddExp(float exp)
    {
        CurrentExp += exp;
        Debug.Log($"获得{exp}经验，当前：{CurrentExp:F0}/{ExpToNextLevel:F0}");
    }

    /// <summary>
    /// 检测升级条件
    /// </summary>
    private void CheckLevelUp()
    {
        if (CurrentExp >= ExpToNextLevel)
        {
            LevelUp();
        }
    }

    /// <summary>
    /// 玩家升级逻辑
    /// </summary>
    public void LevelUp()
    {
        Level++;
        CurrentExp -= ExpToNextLevel;
        ExpToNextLevel *= 1.5f; // 升级所需经验递增
        
        // 升级后属性提升10%（除攻速外）
        float upgradeMultiplier = Mathf.Pow(1 + upgradeBonusRate, Level - 1);
        CurrentAttack = baseAttack * upgradeMultiplier;
        CurrentMoveSpeed = moveSpeed * upgradeMultiplier;
        MaxHP = baseMaxHP * upgradeMultiplier;
        CurrentHP = MaxHP; // 升级补满血量

        Debug.Log($"升级到{Level}级！攻击：{CurrentAttack:F1} | 血量：{MaxHP:F1} | 移速：{CurrentMoveSpeed:F1}");
    }

    /// <summary>
    /// 玩家受击扣血（供敌人攻击调用）
    /// </summary>
    public void TakeDamage(float damage)
    {
        CurrentHP = Mathf.Max(0, CurrentHP - damage);
        if (CurrentHP <= 0)
        {
            Debug.Log("玩家死亡！");
            // 可扩展死亡逻辑：复活/游戏结束
        }
    }
    
    // ===================== 新增：战斗状态检测（供武器系统调用） =====================
    /// <summary>
    /// 检测玩家是否处于战斗状态（供武器管理器调用）
    /// </summary>
    /// <returns>true=战斗中，false=非战斗</returns>
    public bool IsPlayerInCombat()
    {
        if (gameController == null)
        {
            Debug.LogWarning("[PlayerController] GameController引用为空，默认返回非战斗状态");
            return false;
        }
        return gameController.IsInCombat;
    }
}