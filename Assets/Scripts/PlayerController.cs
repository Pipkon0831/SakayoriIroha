using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("基础设置")]
    [SerializeField] private CameraController cameraController;

    [Header("移动设置")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float moveSmooth = 0.1f;
    [SerializeField] private GameController gameController;

    [Header("玩家属性配置")]
    [SerializeField] private float baseAttack = 10f;
    [SerializeField] private float baseMaxHP = 100f;

    // ✅ 语义：这里是“攻速加成值”，0.2 代表 +20%
    [SerializeField] private float baseAttackSpeed = 0.0f;

    [SerializeField] private float upgradeBonusRate = 0.1f;

    [Header("受伤无敌设置")]
    [SerializeField] private float invincibilityTime = 1f;
    [SerializeField] private float flashInterval = 0.1f;
    [SerializeField] private SpriteRenderer playerSprite;

    [Header("武器系统")]
    [SerializeField] private WeaponManager weaponManager;
    [SerializeField] private Camera mainCamera;

    private Vector2 inputDirection;
    private Vector2 smoothInputVelocity;
    private Vector2 currentVelocity;

    public float CurrentAttack { get; private set; }

    /// <summary>
    /// ✅ 语义：攻速“加成值”，0.2 表示 +20%
    /// </summary>
    public float CurrentAttackSpeedBonus
    {
        get
        {
            float bonus = baseAttackSpeed;
            float mult = 1f;

            if (CombatModifierSystem.Instance != null)
                mult = Mathf.Clamp(CombatModifierSystem.Instance.playerAttackSpeedMultiplier, 0.25f, 4f);

            // bonus 仍是 bonus，只是受本层倍率影响（你也可以选择不影响bonus，仅影响最终因子）
            return bonus * mult;
        }
    }

    public float CurrentExp { get; private set; }
    public float ExpToNextLevel { get; private set; }
    public float MaxHP { get; private set; }
    public float CurrentHP { get; private set; }
    public int Level { get; private set; }
    public float CurrentMoveSpeed { get; private set; }

    private float invincibilityRemaining;
    private bool isInvincible => invincibilityRemaining > 0f;
    private float flashTimer;

    public static event System.Action OnPlayerStatsChanged;
    private Rigidbody2D rb;

    private void Awake()
    {
        if (gameController == null)
            gameController = FindObjectOfType<GameController>();

        if (cameraController == null)
            cameraController = FindObjectOfType<CameraController>();

        rb = GetComponent<Rigidbody2D>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0;
        rb.freezeRotation = true;
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
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

        if (gameController != null && gameController.IsPlayerLockedInRoom())
            currentVelocity = ValidateMovement(currentVelocity);

        AimAtMouse();
        CheckLevelUp();
        UpdateInvincibility();
    }

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
        // 你原来 SmoothDamp 写法比较怪（把 inputDirection 往 zero 拉）
        // 这里保持你的逻辑不大改，只修“finalMoveSpeed 变量没用”的问题。
        Vector2 smoothed = Vector2.SmoothDamp(
            inputDirection,
            Vector2.zero,
            ref smoothInputVelocity,
            moveSmooth
        );

        currentVelocity = smoothed * CurrentMoveSpeed;
    }

    private Vector2 ValidateMovement(Vector2 desiredVelocity)
    {
        if (gameController == null) return desiredVelocity;

        RoomData currentRoom = gameController.GetCurrentPlayerRoom();
        if (currentRoom == null || currentRoom.floorPositions == null) return desiredVelocity;

        Vector3 targetPos = transform.position + new Vector3(desiredVelocity.x, desiredVelocity.y, 0) * Time.fixedDeltaTime;
        Vector2Int targetGridPos = new Vector2Int(
            Mathf.FloorToInt(targetPos.x),
            Mathf.FloorToInt(targetPos.y)
        );

        if (currentRoom.floorPositions.Contains(targetGridPos))
            return desiredVelocity;

        Vector2 validVelocity = Vector2.zero;

        Vector3 targetPosX = transform.position + new Vector3(desiredVelocity.x, 0, 0) * Time.fixedDeltaTime;
        Vector2Int targetGridX = new Vector2Int(Mathf.FloorToInt(targetPosX.x), Mathf.FloorToInt(transform.position.y));
        if (currentRoom.floorPositions.Contains(targetGridX))
            validVelocity.x = desiredVelocity.x;

        Vector3 targetPosY = transform.position + new Vector3(0, desiredVelocity.y, 0) * Time.fixedDeltaTime;
        Vector2Int targetGridY = new Vector2Int(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(targetPosY.y));
        if (currentRoom.floorPositions.Contains(targetGridY))
            validVelocity.y = desiredVelocity.y;

        return validVelocity;
    }

    private void ApplyMovement()
    {
        rb.velocity = currentVelocity;
    }

    private void InitPlayerStats()
    {
        Level = 1;
        CurrentExp = 0;
        ExpToNextLevel = 100;

        float upgradeMultiplier = Mathf.Pow(1 + upgradeBonusRate, Level - 1);
        CurrentAttack = baseAttack * upgradeMultiplier;
        CurrentMoveSpeed = moveSpeed * upgradeMultiplier;
        MaxHP = baseMaxHP * upgradeMultiplier;
        CurrentHP = MaxHP;

        OnPlayerStatsChanged?.Invoke();
    }

    private void InitWeaponSystem()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (weaponManager != null)
            weaponManager.InitWeaponManager(this);

        if (mainCamera == null)
            mainCamera = Camera.main;
    }

    private void AimAtMouse()
    {
        if (mainCamera == null || weaponManager == null) return;

        Vector2 mouseWorldPos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector2 aimDir = (mouseWorldPos - (Vector2)transform.position).normalized;

        if (aimDir != Vector2.zero)
            weaponManager.SetAimDirection(aimDir);
    }

    public void AddExp(float exp)
    {
        CurrentExp += exp;
        OnPlayerStatsChanged?.Invoke();
    }

    private void CheckLevelUp()
    {
        if (CurrentExp >= ExpToNextLevel)
            LevelUp();
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

        float finalDamage = damage;

        if (CombatModifierSystem.Instance != null)
            finalDamage *= CombatModifierSystem.Instance.playerReceiveDamageMultiplier;

        finalDamage = Mathf.Max(0f, finalDamage);

        CurrentHP = Mathf.Max(0, CurrentHP - finalDamage);

        // ✅ 统计：承伤打点（只统计本层 current）
        NPCRunFloorStats.Instance?.RecordDamageTaken(finalDamage);

        Debug.Log($"玩家受到{finalDamage}伤害，剩余血量：{CurrentHP}");

        invincibilityRemaining = invincibilityTime;

        if (CurrentHP <= 0)
        {
            Debug.Log("玩家死亡！触发强抖动");

            if (gameController != null)
                gameController.OnPlayerDeath();
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
                playerSprite.enabled = true;
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

    public bool IsPlayerInvincible() => isInvincible;

    public void AddAttack(float value)
    {
        CurrentAttack += value;
        OnPlayerStatsChanged?.Invoke();
    }

    public void AddMaxHP(float value, bool healCurrent = true)
    {
        MaxHP += value;
        if (healCurrent)
            CurrentHP = Mathf.Min(MaxHP, CurrentHP + value);

        OnPlayerStatsChanged?.Invoke();
    }

    public void HealHP(float value)
    {
        if (value <= 0f) return;

        float before = CurrentHP;
        CurrentHP = Mathf.Min(CurrentHP + value, MaxHP);
        float actual = Mathf.Max(0f, CurrentHP - before);

        // ✅ 统计：治疗量
        NPCRunFloorStats.Instance?.RecordHealed(actual);

        OnPlayerStatsChanged?.Invoke();
    }

    public void TakeDamageDirect(float value)
    {
        TakeDamage(value);
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