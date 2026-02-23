using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement; // 重启场景需要

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
    [SerializeField] private GameObject deathPanel;       // 拖死亡面板

    private RoomData currentPlayerRoom;
    private bool isWaitingToRegen = false;

    [Header("怪物检测配置")]
    [SerializeField] private string enemyTag = "Enemy";
    private List<GameObject> currentRoomEnemies = new List<GameObject>();

    private bool isGameOver = false;
    
    public int CurrentFloorIndex { get; private set; } = 0;
    
    public FloorModifierData CurrentFloorModifier { get; private set; }

    private void Start()
    {
        if (autoGenerateOnStart)
        {
            StartNewFloor();
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

        if (LayerEventSystem.Instance != null)
            LayerEventSystem.Instance.OnNewFloorStart();
        
        if (LLMEventBridge.Instance != null)
            LLMEventBridge.Instance.SimulateLLMDecision();

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
    
    private void ApplyFloorModifier()
    {
        if (CurrentFloorModifier == null) return;

        Debug.Log("应用本层规则数据");

        // 未来可扩展：
        // - 视野限制
        // - 怪物速度
        // - 玩家伤害倍率
        // - Boss连战
    }

    [ContextMenu("手动重新生成地牢")]
    public void RegenerateDungeon()
    {
        GenerateGameDungeon();
    }

    private void CheckPlayerRoomChange()
    {
        if (player == null || dungeonGenerator.allRoomData.Count == 0)
            return;

        Vector2Int playerGridPos = new Vector2Int(
            Mathf.FloorToInt(player.transform.position.x),
            Mathf.FloorToInt(player.transform.position.y)
        );

        RoomData newRoom = null;
        foreach (var room in dungeonGenerator.allRoomData)
        {
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
            // 先清空本层事件，再生成下一层
            LayerEventSystem.Instance.OnFloorEnd();
            StartCoroutine(RegenDungeonAfterDelay());
        }
    }

    private IEnumerator RegenDungeonAfterDelay()
    {
        isWaitingToRegen = true;
        yield return new WaitForSeconds(delayAfterBossClear);
        RegenerateDungeon();

        if (player != null && dungeonGenerator != null && dungeonGenerator.allRoomData.Count > 0)
        {
            RoomData spawnRoom = dungeonGenerator.allRoomData.Find(r => r.roomType == RoomData.RoomType.Spawn);
            if (spawnRoom != null)
            {
                player.transform.position = new Vector3(spawnRoom.center.x + 0.5f, spawnRoom.center.y + 0.5f, player.transform.position.z);
            }
        }
        isWaitingToRegen = false;
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
        Time.timeScale = 0f; // 游戏暂停

        if (deathPanel != null)
        {
            deathPanel.SetActive(true);
        }

        // 修复：先移除原有监听，再添加，防止重复绑定按钮
        var btn = deathPanel?.GetComponentInChildren<UnityEngine.UI.Button>();
        if (btn != null)
        {
            btn.onClick.RemoveAllListeners(); // 清空原有监听
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