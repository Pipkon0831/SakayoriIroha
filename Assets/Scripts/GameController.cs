using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using System.Threading;

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
    [SerializeField] private NPCDecisionUI npcDecisionUI;

    private bool isGameOver = false;

    public int CurrentFloorIndex { get; private set; } = 0;

    public FloorModifierData CurrentFloorModifier { get; private set; }

    [Header("事件系统")]
    [SerializeField] private LayerEventApplier layerEventApplier;

    private void Start()
    {
        IsInCombat = false;
        isGameOver = false;

        // ✅ 开局确保抽取“本局人格”（只抽一次，整局固定）
        if (NPCRunPersonalityManager.Instance != null)
            NPCRunPersonalityManager.Instance.EnsurePicked();

        if (deathPanel != null)
            deathPanel.SetActive(false);

        if (!autoGenerateOnStart)
            return;

        // ✅ 开局保险：清理事件残留（Editor 多次运行时很常见）
        if (LayerEventSystem.Instance != null)
        {
            LayerEventSystem.Instance.ClearNextFloorEvents();
            LayerEventSystem.Instance.ClearCurrentFloorEvents();
            LayerEventSystem.Instance.ConsumeInstantEvents();
        }

        // ✅ 正常流程：开局先进入 NPC 对话阶段（NPC 首句固定，不等 LLM）
        if (npcDecisionUI != null)
        {
            npcDecisionUI.Show();
        }
        else
        {
            Debug.LogWarning("GameController: 未配置 NPCDecisionUI，兜底直接进入第一层。");
            StartNewFloor();
            MovePlayerToSpawnRoomCenterIfPossible();
        }
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

    /// <summary>
    /// 进入新层：
    /// 1) next -> current
    /// 2) 应用本层事件
    /// 3) 生成地牢
    /// 4) ✅ 后台预取“下一次对话阶段开场白”（用于下一层结束后的对话，不阻塞）
    /// </summary>
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
        MovePlayerToSpawnRoomCenterIfPossible();
    }

    private void CheckPlayerRoomChange()
    {
        if (player == null) return;

        if (dungeonGenerator == null || dungeonGenerator.allRoomData == null || dungeonGenerator.allRoomData.Count == 0)
            return;

        Vector2Int playerGridPos = new Vector2Int(
            Mathf.FloorToInt(player.transform.position.x),
            Mathf.FloorToInt(player.transform.position.y)
        );

        RoomData newRoom = null;

        foreach (var room in dungeonGenerator.allRoomData)
        {
            if (room == null) continue;
            if (room.floorPositions == null) continue;

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
                    isBossRoomCleared = true;
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

        // 1) Boss 清完后延迟
        yield return new WaitForSeconds(delayAfterBossClear);

        // 2) 弹对话UI：此处不 StartNewFloor，由 UI 的 Continue/Confirm 触发 StartNewFloor
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

        isWaitingToRegen = false;
    }

    private void MovePlayerToSpawnRoomCenterIfPossible()
    {
        if (player == null || dungeonGenerator == null || dungeonGenerator.allRoomData == null || dungeonGenerator.allRoomData.Count == 0)
            return;

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

    // 给 UI 调用
    public void MovePlayerToSpawnRoomCenterIfPossible_Public()
    {
        MovePlayerToSpawnRoomCenterIfPossible();
    }


}