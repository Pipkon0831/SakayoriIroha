using System.Collections;
using UnityEngine;

public class Enemy : MonoBehaviour
{
    [Header("敌人配置")]
    [SerializeField] private float maxHP = 50f;
    [SerializeField] private float expReward = 20f;
    [SerializeField] private float attackDamage = 10f;
    [SerializeField] private float attackCooldown = 1f;
    [SerializeField] private float attackCheckRadius = 1.8f;

    [Header("AI 移动")]
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float chaseRange = 6f;
    [SerializeField] private float stopRange = 1.3f;
    [SerializeField] private float enemySeparation = 1f;

    [Header("随机游走")]
    [SerializeField] private float idleMoveSpeed = 0.5f;
    [SerializeField] private float idleChangeDirTime = 2f;
    [SerializeField] private float idleWanderRadius = 1.5f;

    private float currentHP;
    private PlayerController player;
    private GameController gameController;
    private RoomData currentRoom;

    private float lastAttackTime;
    private Vector2 idleTargetPos;
    private float idleDirTimer;

    private enum EnemyState { Idle, Chase, Attack }
    private EnemyState currentState;

    private Rigidbody2D rb;
    private Collider2D col;
    private LayerMask playerLayer;
    private ContactFilter2D playerContactFilter;

    // 标记是否已死亡，防止重复触发死亡逻辑
    private bool isDead = false;

    private void Awake()
    {
        currentHP = maxHP;
        player = FindObjectOfType<PlayerController>();
        gameController = FindObjectOfType<GameController>();

        lastAttackTime = -attackCooldown;
        currentState = EnemyState.Idle;

        rb = GetComponent<Rigidbody2D>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        if (col == null) col = gameObject.AddComponent<CircleCollider2D>();

        rb.gravityScale = 0;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.bodyType = RigidbodyType2D.Dynamic;

        idleTargetPos = transform.position;

        playerLayer = LayerMask.GetMask("Player");
        playerContactFilter = new ContactFilter2D();
        playerContactFilter.layerMask = playerLayer;
        playerContactFilter.useTriggers = false;
    }

    private void Start()
    {
        // ✅ 防御：等地牢生成/房间数据 ready 再尝试绑定房间，避免 NRE
        StartCoroutine(InitRoomCoroutine());
    }

    private IEnumerator InitRoomCoroutine()
    {
        // 最多等待 30 帧
        for (int i = 0; i < 30; i++)
        {
            if (TryGetEnemyCurrentRoom())
                yield break;

            yield return null;
        }

        // 超时不崩：进入降级模式（后续 FixedUpdate 仍会再尝试）
        Debug.LogWarning($"[Enemy] {name} 无法定位所属房间（30帧超时）。将保持 Idle，直到后续可定位。");
    }

    private void FixedUpdate()
    {
        // 死亡后直接返回，不再执行任何逻辑
        if (isDead) return;

        // player 缺失就不跑
        if (player == null) return;

        // ✅ 防御：如果没拿到房间，尝试补一次（例如地牢刚生成/刷新）
        if (currentRoom == null)
        {
            TryGetEnemyCurrentRoom();
            if (currentRoom == null) return;
        }

        UpdateEnemyState();

        switch (currentState)
        {
            case EnemyState.Idle:
                IdleWander();
                break;
            case EnemyState.Chase:
                ChasePlayer();
                break;
            case EnemyState.Attack:
                AttackState();
                break;
        }
    }

    #region AI 状态更新
    private void UpdateEnemyState()
    {
        if (gameController == null) gameController = FindObjectOfType<GameController>();
        if (gameController == null)
        {
            currentState = EnemyState.Idle;
            return;
        }

        if (gameController.GetCurrentPlayerRoom() != currentRoom)
        {
            currentState = EnemyState.Idle;
            return;
        }

        float dist = Vector2.Distance(transform.position, player.transform.position);

        bool isInAttackRange = IsPlayerOverlapping() || dist <= stopRange;
        if (isInAttackRange && Time.fixedTime - lastAttackTime >= attackCooldown)
            currentState = EnemyState.Attack;
        else if (dist <= chaseRange)
            currentState = EnemyState.Chase;
        else
            currentState = EnemyState.Idle;
    }
    #endregion

    #region 闲置游走
    private void IdleWander()
    {
        idleDirTimer += Time.fixedDeltaTime;

        if (idleDirTimer >= idleChangeDirTime)
        {
            idleDirTimer = 0;
            Vector2 randomOffset = Random.insideUnitCircle * idleWanderRadius;
            idleTargetPos = (Vector2)transform.position + randomOffset;
        }

        Vector2 dir = (idleTargetPos - (Vector2)transform.position).normalized;
        Vector2 moveDir = dir * idleMoveSpeed;

        moveDir += GetSeparationDirection();
        Move(moveDir);
    }
    #endregion

    #region 追击玩家
    private void ChasePlayer()
    {
        Vector2 dir = (player.transform.position - transform.position).normalized;
        float finalSpeed = moveSpeed;

        if (CombatModifierSystem.Instance != null)
        {
            finalSpeed *= CombatModifierSystem.Instance.enemySpeedMultiplier;
        }

        Vector2 moveDir = dir * finalSpeed;

        moveDir += GetSeparationDirection();
        Move(moveDir);
    }
    #endregion

    #region 攻击状态（核心：重叠检测+扣血）
    private void AttackState()
    {
        rb.velocity = rb.velocity * 0.5f;

        if (IsPlayerOverlapping())
        {
            TryAttackPlayer();
        }
    }

    private bool IsPlayerOverlapping()
    {
        Collider2D[] hitColliders = new Collider2D[1];
        int hitCount = Physics2D.OverlapCircle(transform.position, attackCheckRadius, playerContactFilter, hitColliders);

        return hitCount > 0 && hitColliders[0] != null && hitColliders[0].GetComponent<PlayerController>() == player;
    }
    #endregion

    #region 移动（保留物理，能撞墙）
    private void Move(Vector2 moveDir)
    {
        Vector2 nextPos = (Vector2)transform.position + moveDir * Time.fixedDeltaTime;
        if (!IsInRoom(nextPos))
        {
            rb.velocity = Vector2.zero;
            return;
        }

        rb.velocity = moveDir;
    }

    private bool IsInRoom(Vector2 pos)
    {
        // ✅ 防御：拿不到 room 时降级处理（不阻塞移动）
        if (currentRoom == null || currentRoom.floorPositions == null) return true;

        Vector2Int grid = new Vector2Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y));
        return currentRoom.floorPositions.Contains(grid);
    }
    #endregion

    #region 怪物分离
    private Vector2 GetSeparationDirection()
    {
        Vector2 separation = Vector2.zero;
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");

        foreach (var e in enemies)
        {
            if (e == gameObject) continue;

            float dist = Vector2.Distance(transform.position, e.transform.position);
            if (dist < enemySeparation && dist > 0)
            {
                separation += (Vector2)(transform.position - e.transform.position).normalized / dist;
            }
        }
        return separation;
    }
    #endregion

    #region 攻击、受击、死亡
    private void TryAttackPlayer()
    {
        if (player.IsPlayerInvincible())
            return;

        if (Time.fixedTime - lastAttackTime >= attackCooldown)
        {
            Debug.Log($"[怪物攻击] 扣血{attackDamage}，玩家当前血量：{player.CurrentHP}");
            float finalDamage = attackDamage;

            if (CombatModifierSystem.Instance != null)
            {
                finalDamage *= CombatModifierSystem.Instance.playerReceiveDamageMultiplier;
            }

            player.TakeDamage(finalDamage);
            lastAttackTime = Time.fixedTime;
        }
    }

    public void TakeDamage(float damage)
    {
        if (isDead) return;

        currentHP = Mathf.Max(0, currentHP - damage);

        if (currentHP <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        isDead = true;

        if (player != null)
        {
            player.AddExp(expReward);
        }

        if (gameController != null)
            gameController.NotifyEnemyDeath(gameObject);

        Destroy(gameObject);
    }
    #endregion

    #region 房间检测（防御式）
    private bool TryGetEnemyCurrentRoom()
    {
        if (gameController == null) gameController = FindObjectOfType<GameController>();
        if (gameController == null) return false;

        if (gameController.dungeonGenerator == null) return false;
        if (gameController.dungeonGenerator.allRoomData == null || gameController.dungeonGenerator.allRoomData.Count == 0)
            return false;

        Vector2Int g = new Vector2Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.y)
        );

        foreach (var r in gameController.dungeonGenerator.allRoomData)
        {
            if (r == null) continue;
            if (r.floorPositions == null) continue;

            if (r.floorPositions.Contains(g))
            {
                currentRoom = r;
                return true;
            }
        }

        return false;
    }
    #endregion

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackCheckRadius);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, chaseRange);
    }
}