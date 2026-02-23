using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("基础设置")]
    [SerializeField] private CameraController cameraController; // 相机控制器引用
    
    [Header("移动设置")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float moveSmooth = 0.1f;
    [SerializeField] private GameController gameController;

    [Header("玩家属性配置")]
    [SerializeField] private float baseAttack = 10f;
    [SerializeField] private float baseMaxHP = 100f;
    [SerializeField] private float baseAttackSpeed = 0.5f;
    [SerializeField] private float upgradeBonusRate = 0.1f;

    [Header("受伤无敌设置")]
    [SerializeField] private float invincibilityTime = 1f;
    [SerializeField] private float flashInterval = 0.1f;
    [SerializeField] private SpriteRenderer playerSprite;

    [Header("武器系统")]
    [SerializeField] private WeaponManager weaponManager;
    [SerializeField] private Camera mainCamera;

    // 原有变量
    private Vector2 inputDirection;
    private Vector2 smoothInputVelocity;
    private Vector2 currentVelocity;
    public float CurrentAttack { get; private set; }
    public float CurrentAttackSpeed { get; private set; }
    public float CurrentExp { get; private set; }
    public float ExpToNextLevel { get; private set; }
    public float MaxHP { get; private set; }
    public float CurrentHP { get; private set; }
    public int Level { get; private set; }
    public float CurrentMoveSpeed { get; private set; }

    // 无敌相关
    private float invincibilityRemaining;
    private bool isInvincible => invincibilityRemaining > 0f;
    private float flashTimer;

    public static event System.Action OnPlayerStatsChanged;
    private Rigidbody2D rb;

    private void Awake()
    {
        if (gameController == null)
        {
            gameController = FindObjectOfType<GameController>();
        }

        // 自动获取相机控制器
        if (cameraController == null)
        {
            cameraController = FindObjectOfType<CameraController>();
        }

        rb = GetComponent<Rigidbody2D>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0;
        rb.freezeRotation = true;
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        // 新增：防止角色旋转（可选）
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        InitPlayerStats();
        InitWeaponSystem();

        invincibilityRemaining = 0f;
        flashTimer = 0f;
    }

    private void Update()
    {
        GetPlayerInput();
        CalculateSmoothMovement();

        if (gameController.IsPlayerLockedInRoom())
        {
            currentVelocity = ValidateMovement(currentVelocity);
        }

        AimAtMouse();
        CheckLevelUp();
        UpdateInvincibility();
    }

    // 关键修改：移动逻辑移到FixedUpdate（物理更新帧）
    private void FixedUpdate()
    {
        ApplyMovement();
    }

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
        currentVelocity = inputDirection * CurrentMoveSpeed;
    }

    private Vector2 ValidateMovement(Vector2 desiredVelocity)
    {
        RoomData currentRoom = gameController.GetCurrentPlayerRoom();
        if (currentRoom == null || currentRoom.floorPositions == null) return desiredVelocity;

        Vector3 targetPos = transform.position + new Vector3(desiredVelocity.x, desiredVelocity.y, 0) * Time.fixedDeltaTime;
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
            Vector3 targetPosX = transform.position + new Vector3(desiredVelocity.x, 0, 0) * Time.fixedDeltaTime;
            Vector2Int targetGridX = new Vector2Int(Mathf.FloorToInt(targetPosX.x), Mathf.FloorToInt(transform.position.y));
            if (currentRoom.floorPositions.Contains(targetGridX))
            {
                validVelocity.x = desiredVelocity.x;
            }

            Vector3 targetPosY = transform.position + new Vector3(0, desiredVelocity.y, 0) * Time.fixedDeltaTime;
            Vector2Int targetGridY = new Vector2Int(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(targetPosY.y));
            if (currentRoom.floorPositions.Contains(targetGridY))
            {
                validVelocity.y = desiredVelocity.y;
            }
            return validVelocity;
        }
    }

    // 核心修复：改用Rigidbody2D控制移动
    private void ApplyMovement()
    {
        // 设置刚体速度，让物理引擎处理碰撞
        rb.velocity = currentVelocity;
        // 替代方案（如果需要更平滑）：
        // rb.MovePosition(rb.position + currentVelocity * Time.fixedDeltaTime);
    }

    private void InitPlayerStats()
    {
        Level = 1;
        CurrentExp = 0;
        ExpToNextLevel = 100;
        
        float upgradeMultiplier = Mathf.Pow(1 + upgradeBonusRate, Level - 1);
        CurrentAttack = baseAttack * upgradeMultiplier;
        CurrentAttackSpeed = baseAttackSpeed;
        CurrentMoveSpeed = moveSpeed * upgradeMultiplier;
        MaxHP = baseMaxHP * upgradeMultiplier;
        CurrentHP = MaxHP;

        OnPlayerStatsChanged?.Invoke();
    }

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

        // 自动获取主相机的备用逻辑
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
    }

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

    public void AddExp(float exp)
    {
        CurrentExp += exp;
        Debug.Log($"获得{exp}经验，当前：{CurrentExp:F0}/{ExpToNextLevel:F0}");
        OnPlayerStatsChanged?.Invoke();
    }

    private void CheckLevelUp()
    {
        if (CurrentExp >= ExpToNextLevel)
        {
            LevelUp();
        }
    }

    public void LevelUp()
    {
        Level++;
        CurrentExp -= ExpToNextLevel;
        ExpToNextLevel *= 1.5f;
        
        float upgradeMultiplier = Mathf.Pow(1 + upgradeBonusRate, Level - 1);
        CurrentAttack = baseAttack * upgradeMultiplier;
        CurrentMoveSpeed = moveSpeed * upgradeMultiplier;
        MaxHP = baseMaxHP * upgradeMultiplier;
        CurrentHP = MaxHP;

        Debug.Log($"升级到{Level}级！攻击：{CurrentAttack:F1} | 血量：{MaxHP:F1} | 移速：{CurrentMoveSpeed:F1}");
        OnPlayerStatsChanged?.Invoke();
    }

    public void TakeDamage(float damage)
    {
        if (isInvincible)
        {
            Debug.Log("玩家处于无敌状态，免伤！");
            return;
        }

        CurrentHP = Mathf.Max(0, CurrentHP - damage);
        Debug.Log($"玩家受到{damage}伤害，剩余血量：{CurrentHP}");

        invincibilityRemaining = invincibilityTime;

        if (CurrentHP <= 0)
        {
            Debug.Log("玩家死亡！触发强抖动");

            // ✅ 关键：通知游戏结束
            if (gameController != null)
            {
                gameController.OnPlayerDeath();
            }
        }

        OnPlayerStatsChanged?.Invoke();
    }

    private void UpdateInvincibility()
    {
        if (isInvincible)
        {
            invincibilityRemaining -= Time.deltaTime;
            invincibilityRemaining = Mathf.Max(0f, invincibilityRemaining);

            if (playerSprite != null)
            {
                flashTimer += Time.deltaTime;
                if (flashTimer >= flashInterval)
                {
                    flashTimer = 0f;
                    playerSprite.enabled = !playerSprite.enabled;
                }
            }
        }
        else
        {
            if (playerSprite != null && !playerSprite.enabled)
            {
                playerSprite.enabled = true;
            }
        }
    }

    public bool IsPlayerInCombat()
    {
        if (gameController == null)
        {
            Debug.LogWarning("[PlayerController] GameController引用为空，默认返回非战斗状态");
            return false;
        }
        return gameController.IsInCombat;
    }

    public bool IsPlayerInvincible()
    {
        return isInvincible;
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
}