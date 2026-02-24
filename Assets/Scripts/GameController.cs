using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class GameController : MonoBehaviour
{
    [SerializeField] public RoomFirstDungeonGenerator dungeonGenerator;
    [SerializeField] private bool autoGenerateOnStart = true;

    [Header("战斗状态配置")]
    [SerializeField] private GameObject player;
    public bool IsInCombat { get; private set; }

    [Header("Boss房重生配置")]
    [SerializeField] private float delayAfterBossClear = 2f;
    [SerializeField] private bool autoRegenAfterBoss = true;

    [Header("死亡UI")]
    [SerializeField] private GameObject deathPanel;

    private RoomData currentPlayerRoom;
    private bool isWaitingToRegen = false;

    [Header("怪物检测配置")]
    [SerializeField] private string enemyTag = "Enemy";
    private readonly List<GameObject> currentRoomEnemies = new List<GameObject>();

    [Header("NPC对话配置")]
    [SerializeField] private NPCDecisionUI_TMP npcDecisionUI;
    
    private bool isGameOver = false;

    public int CurrentFloorIndex { get; private set; } = 0;

    public FloorModifierData CurrentFloorModifier { get; private set; }

    [Header("事件系统")]
    [SerializeField] private LayerEventApplier layerEventApplier;

    private void Start()
    {
        if (autoGenerateOnStart)
        {
            if (npcDecisionUI != null) npcDecisionUI.Show();
            else StartNewFloor(); // 兜底
        }

        IsInCombat = false;
        isGameOver = false;

        if (deathPanel != null)
            deathPanel.SetActive(false);
    }

    private void Update()
    {
        if (isGameOver) return;

        if (IsInCombat && currentPlayerRoom != null)
        {
            CheckCurrentRoomEnemies();
        }

        CheckPlayerRoomChange();
    }

    public void StartNewFloor()
    {
        CurrentFloorIndex++;
        Debug.Log($"进入第 {CurrentFloorIndex} 层");

        CurrentFloorModifier = new FloorModifierData();

        // 1) 进入新层：提交 next -> current
        if (LayerEventSystem.Instance != null)
        {
            LayerEventSystem.Instance.CommitNextFloorToCurrent();
        }

        // 2) 应用本层事件（倍率/视野/房间覆盖）
        if (layerEventApplier != null)
        {
            layerEventApplier.ApplyCurrentFloorEvents();
        }
        else
        {
            Debug.LogWarning("GameController: 未找到LayerEventApplier，本层事件不会被应用。");
        }

        // 3) 如果你当前仍用模拟LLM决策：它应该在“Boss后对话确认”时调用
        if (LLMEventBridge.Instance != null && CurrentFloorIndex == 1)
        {
            // 只在第一层开始时模拟一次（相当于“开局与NPC对话”）
            LLMEventBridge.Instance.SimulateLLMDecision();

            // 模拟LLM可能会写入 instantEvents，这里立刻执行一次性事件
            if (layerEventApplier != null)
                layerEventApplier.ApplyAndConsumeInstantEvents();
        }

        GenerateGameDungeon();
    }

    public void GenerateGameDungeon()
    {
        if (dungeonGenerator == null)
        {
            Debug.LogError("GameController: 未配置地牢生成器！");
            return;
        }

        dungeonGenerator.GenerateDungeon();
        dungeonGenerator.SpawnObjectsInRooms();

        currentPlayerRoom = null;
        isWaitingToRegen = false;
        currentRoomEnemies.Clear();

        Debug.Log("地牢生成完成！");
    }

    [ContextMenu("手动重新生成地牢")]
    public void RegenerateDungeon()
    {
        GenerateGameDungeon();

        // 将玩家移回 Spawn
        MovePlayerToSpawnRoomCenterIfPossible();
    }

    private void CheckPlayerRoomChange()
    {
        if (player == null)
        {
            return;
        }

        if (dungeonGenerator == null || dungeonGenerator.allRoomData == null || dungeonGenerator.allRoomData.Count == 0)
        {
            return;
        }

        Vector2Int playerGridPos = new Vector2Int(
            Mathf.FloorToInt(player.transform.position.x),
            Mathf.FloorToInt(player.transform.position.y)
        );

        RoomData newRoom = null;

        foreach (var room in dungeonGenerator.allRoomData)
        {
            if (room == null) continue;

            if (room.floorPositions == null)
            {
                continue;
            }

            if (room.floorPositions.Contains(playerGridPos))
            {
                newRoom = room;
                break;
            }
        }

        if (newRoom != null && newRoom != currentPlayerRoom)
        {
            currentPlayerRoom = newRoom;
            OnEnterNewRoom(newRoom);
        }
    }

    private void OnEnterNewRoom(RoomData room)
    {
        Debug.Log($"进入房间：{room.roomType}");
        currentRoomEnemies.Clear();

        switch (room.roomType)
        {
            case RoomData.RoomType.Monster:
            case RoomData.RoomType.Boss:
                if (!room.isCleared)
                {
                    EnterCombatState(room.roomType);
                    CollectCurrentRoomEnemies();
                }
                break;

            case RoomData.RoomType.Spawn:
                ExitCombatState();
                break;

            // Reward房一般也退出战斗（按你需要）
            case RoomData.RoomType.Reward:
                ExitCombatState();
                break;
        }
    }

    private void CollectCurrentRoomEnemies()
    {
        if (currentPlayerRoom == null) return;

        currentRoomEnemies.Clear();
        GameObject[] allEnemies = GameObject.FindGameObjectsWithTag(enemyTag);

        foreach (var enemy in allEnemies)
        {
            Vector2Int enemyGridPos = new Vector2Int(
                Mathf.FloorToInt(enemy.transform.position.x),
                Mathf.FloorToInt(enemy.transform.position.y)
            );

            if (currentPlayerRoom.floorPositions.Contains(enemyGridPos))
            {
                currentRoomEnemies.Add(enemy);
            }
        }

        Debug.Log($"当前房间怪物：{currentRoomEnemies.Count}");
    }

    private void CheckCurrentRoomEnemies()
    {
        currentRoomEnemies.RemoveAll(enemy => enemy == null);
        if (currentRoomEnemies.Count == 0)
        {
            ExitCombatState();
        }
    }

    private void EnterCombatState(RoomData.RoomType roomType)
    {
        if (IsInCombat) return;
        IsInCombat = true;
    }

    public void ExitCombatState()
    {
        if (!IsInCombat) return;
        IsInCombat = false;

        bool isBossRoomCleared = false;

        if (currentPlayerRoom != null)
        {
            if (currentPlayerRoom.roomType == RoomData.RoomType.Monster ||
                currentPlayerRoom.roomType == RoomData.RoomType.Boss)
            {
                currentPlayerRoom.isCleared = true;

                if (currentPlayerRoom.roomType == RoomData.RoomType.Boss)
                {
                    isBossRoomCleared = true;
                }
            }
        }

        if (isBossRoomCleared && autoRegenAfterBoss && !isWaitingToRegen)
        {
            // ✅ 本层结束：清掉 currentFloorEvents（单层效果结束）
            if (LayerEventSystem.Instance != null)
                LayerEventSystem.Instance.OnFloorEnd();

            StartCoroutine(NextFloorAfterDelay());
        }
    }

    private IEnumerator NextFloorAfterDelay()
    {
        isWaitingToRegen = true;

        // 1) Boss 清完后延迟（你原本就有）
        yield return new WaitForSeconds(delayAfterBossClear);

        // 2) 进入“对话决策阶段”：弹 UI（正式接入LLM时也是这里）
        // 注意：此时不要 StartNewFloor()，由 UI 的 Confirm 按钮去触发 StartNewFloor()
        if (npcDecisionUI != null)
        {
            npcDecisionUI.Show();
        }
        else
        {
            Debug.LogWarning("GameController: 未配置 NPCDecisionUI，兜底直接进入下一层。");
            StartNewFloor();
            MovePlayerToSpawnRoomCenterIfPossible();
        }

        // 3) 等待标记重置：
        // - 如果弹UI：UI确认后会 StartNewFloor()，我们在这里先放开标记，避免卡死
        // - 如果兜底直接进下一层：也已执行完
        isWaitingToRegen = false;
    }

    private void MovePlayerToSpawnRoomCenterIfPossible()
    {
        if (player == null || dungeonGenerator == null || dungeonGenerator.allRoomData == null || dungeonGenerator.allRoomData.Count == 0) return;

        RoomData spawnRoom = dungeonGenerator.allRoomData.Find(r => r.roomType == RoomData.RoomType.Spawn);
        if (spawnRoom == null) return;

        player.transform.position = new Vector3(
            spawnRoom.center.x + 0.5f,
            spawnRoom.center.y + 0.5f,
            player.transform.position.z
        );
    }

    public bool IsPlayerLockedInRoom()
    {
        return IsInCombat && currentPlayerRoom != null;
    }

    public Bounds GetCurrentRoomWorldBounds()
    {
        if (currentPlayerRoom == null) return new Bounds();

        BoundsInt gridBounds = currentPlayerRoom.roomBounds;
        Vector3 center = new Vector3(gridBounds.x + gridBounds.size.x / 2f, gridBounds.y + gridBounds.size.y / 2f, 0);
        Vector3 size = new Vector3(gridBounds.size.x, gridBounds.size.y, 0);
        return new Bounds(center, size);
    }

    public RoomData GetCurrentPlayerRoom()
    {
        return currentPlayerRoom;
    }

    public void NotifyEnemyDeath(GameObject enemy)
    {
        if (currentRoomEnemies.Contains(enemy))
        {
            currentRoomEnemies.Remove(enemy);
        }
    }

    public void OnPlayerDeath()
    {
        if (isGameOver) return;

        isGameOver = true;
        Time.timeScale = 0f;

        if (deathPanel != null)
        {
            deathPanel.SetActive(true);
        }

        var btn = deathPanel?.GetComponentInChildren<UnityEngine.UI.Button>();
        if (btn != null)
        {
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(RestartGame);
        }
        Debug.Log("玩家死亡 → 游戏暂停，显示死亡UI");
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}