using UnityEngine;
using System.Collections;
using System.Collections.Generic; // 新增：用于存储怪物引用

public class GameController : MonoBehaviour
{
    [SerializeField] private RoomFirstDungeonGenerator dungeonGenerator;
    [SerializeField] private bool autoGenerateOnStart = true;

    [Header("战斗状态配置")]
    [SerializeField] private GameObject player; // 拖入场景中的Player
    public bool IsInCombat { get; private set; } // 战斗状态

    [Header("Boss房重生配置")]
    [SerializeField] private float delayAfterBossClear = 2f; // 打完Boss后延迟多久重生地牢
    [SerializeField] private bool autoRegenAfterBoss = true; // 是否开启Boss房通关后自动重生地牢

    // 房间检测相关
    private RoomData currentPlayerRoom; // 记录玩家当前所在房间
    private bool isWaitingToRegen = false; // 防止重复触发重生
    
    // 新增：怪物检测相关
    [Header("怪物检测配置")]
    [SerializeField] private string enemyTag = "Enemy"; // 敌人标签（需和Enemy预制体标签一致）
    private List<GameObject> currentRoomEnemies = new List<GameObject>(); // 当前房间的怪物列表

    private void Start()
    {
        if (autoGenerateOnStart)
        {
            GenerateGameDungeon();
        }

        IsInCombat = false;
    }

    private void Update()
    {
        // 移除Esc退出战斗的代码
        // 新增：战斗中实时检测怪物数量
        if (IsInCombat && currentPlayerRoom != null)
        {
            CheckCurrentRoomEnemies();
        }

        CheckPlayerRoomChange();
    }

    public void GenerateGameDungeon()
    {
        if (dungeonGenerator == null)
        {
            Debug.LogError("GameController: 未配置地牢生成器引用！请在Inspector中指定RoomFirstDungeonGenerator组件");
            return;
        }

        dungeonGenerator.GenerateDungeon();
        dungeonGenerator.SpawnObjectsInRooms();
        
        // 重置状态
        currentPlayerRoom = null;
        isWaitingToRegen = false;
        currentRoomEnemies.Clear(); // 清空怪物列表
        
        Debug.Log("GameController: 地牢生成和物品放置完成！");
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
        Debug.Log($"【玩家进入房间】类型：{room.roomType} | 房间中心：{room.center}");

        // 清空上一个房间的怪物列表
        currentRoomEnemies.Clear();

        switch (room.roomType)
        {
            case RoomData.RoomType.Monster:
            case RoomData.RoomType.Boss:
                if (!room.isCleared)
                {
                    EnterCombatState(room.roomType);
                    // 进入房间时立即收集怪物列表
                    CollectCurrentRoomEnemies();
                }
                break;
            case RoomData.RoomType.Spawn:
                ExitCombatState();
                break;
        }
    }

    /// <summary>
    /// 收集当前房间内的所有怪物
    /// </summary>
    private void CollectCurrentRoomEnemies()
    {
        if (currentPlayerRoom == null) return;

        currentRoomEnemies.Clear();
        
        // 方式1：通过标签查找场景中所有敌人，再筛选是否在当前房间内（推荐）
        GameObject[] allEnemies = GameObject.FindGameObjectsWithTag(enemyTag);
        foreach (var enemy in allEnemies)
        {
            Vector2Int enemyGridPos = new Vector2Int(
                Mathf.FloorToInt(enemy.transform.position.x),
                Mathf.FloorToInt(enemy.transform.position.y)
            );

            // 检测敌人是否在当前房间的地板范围内
            if (currentPlayerRoom.floorPositions.Contains(enemyGridPos))
            {
                currentRoomEnemies.Add(enemy);
            }
        }

        Debug.Log($"【收集房间怪物】当前{currentPlayerRoom.roomType}房间内共有{currentRoomEnemies.Count}个怪物");
    }

    /// <summary>
    /// 实时检测当前房间的怪物数量
    /// </summary>
    private void CheckCurrentRoomEnemies()
    {
        // 清理已销毁的怪物引用
        currentRoomEnemies.RemoveAll(enemy => enemy == null);

        // 如果怪物数量为0，自动退出战斗状态
        if (currentRoomEnemies.Count == 0)
        {
            ExitCombatState();
        }
    }

    private void EnterCombatState(RoomData.RoomType roomType)
    {
        if (IsInCombat)
            return;
    
        IsInCombat = true;
    
        Debug.Log($"【进入战斗状态】房间类型：{roomType} | 需击败所有怪物才能脱战");
    }

    public void ExitCombatState()
    {
        if (!IsInCombat)
            return;

        IsInCombat = false;
        bool isBossRoomCleared = false;

        if (currentPlayerRoom != null)
        {
            // 标记房间为已攻略
            if (currentPlayerRoom.roomType == RoomData.RoomType.Monster || 
                currentPlayerRoom.roomType == RoomData.RoomType.Boss)
            {
                currentPlayerRoom.isCleared = true;
                Debug.Log($"【房间已攻略】{currentPlayerRoom.roomType}房间标记为已攻略，再次进入不会触发战斗");
                
                // 检测是否是Boss房被攻略
                if (currentPlayerRoom.roomType == RoomData.RoomType.Boss)
                {
                    isBossRoomCleared = true;
                    Debug.Log($"【Boss房已通关】恭喜击败Boss！");
                }
            }
        }

        Debug.Log("【退出战斗状态】所有怪物已击败，回到脱战模式");

        // 如果是Boss房通关且开启了自动重生，则延迟重生地牢
        if (isBossRoomCleared && autoRegenAfterBoss && !isWaitingToRegen)
        {
            StartCoroutine(RegenDungeonAfterDelay());
        }
    }

    /// <summary>
    /// 延迟指定时间后重新生成地牢
    /// </summary>
    private IEnumerator RegenDungeonAfterDelay()
    {
        isWaitingToRegen = true;
        Debug.Log($"【准备重生地牢】将在{delayAfterBossClear}秒后生成新的地牢...");
        
        yield return new WaitForSeconds(delayAfterBossClear);
        
        Debug.Log("【开始重生地牢】生成全新的地牢场景！");
        RegenerateDungeon();
        
        // 如果玩家对象存在，重置玩家到新出生房中心
        if (player != null && dungeonGenerator != null && dungeonGenerator.allRoomData.Count > 0)
        {
            // 找到出生房并移动玩家
            RoomData spawnRoom = dungeonGenerator.allRoomData.Find(r => r.roomType == RoomData.RoomType.Spawn);
            if (spawnRoom != null)
            {
                player.transform.position = new Vector3(spawnRoom.center.x + 0.5f, spawnRoom.center.y + 0.5f, player.transform.position.z);
                Debug.Log($"【玩家重置位置】移动到新出生房中心：{spawnRoom.center}");
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
        Vector3 center = new Vector3(
            gridBounds.x + gridBounds.size.x / 2f,
            gridBounds.y + gridBounds.size.y / 2f,
            0
        );
        Vector3 size = new Vector3(gridBounds.size.x, gridBounds.size.y, 0);
        
        return new Bounds(center, size);
    }

    public RoomData GetCurrentPlayerRoom()
    {
        return currentPlayerRoom;
    }

    // 新增：供Enemy脚本调用，主动通知怪物死亡（可选，优化检测效率）
    public void NotifyEnemyDeath(GameObject enemy)
    {
        if (currentRoomEnemies.Contains(enemy))
        {
            currentRoomEnemies.Remove(enemy);
            Debug.Log($"【怪物死亡】当前房间剩余怪物数量：{currentRoomEnemies.Count}");
        }
    }
}