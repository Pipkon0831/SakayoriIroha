using UnityEngine;
using System.Collections; // 仅新增这一行，解决协程/IEnumerator报错

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
        if (IsInCombat && Input.GetKeyDown(KeyCode.Escape) && !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            GUIUtility.hotControl = 0;
            ExitCombatState();
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

        // 仅移除Reward引用，不修改外部枚举，保留原有已定义的类型逻辑
        switch (room.roomType)
        {
            case RoomData.RoomType.Monster:
            case RoomData.RoomType.Boss:
                if (!room.isCleared)
                {
                    EnterCombatState(room.roomType);
                }
                break;
            case RoomData.RoomType.Spawn:
                // 完全移除Reward分支，避免引用未定义的枚举
                ExitCombatState();
                break;
        }
    }

    private void EnterCombatState(RoomData.RoomType roomType)
    {
        if (IsInCombat)
            return;
    
        IsInCombat = true;
    
        Debug.Log($"【进入战斗状态】房间类型：{roomType} | 按Esc键脱战");
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

        Debug.Log("【退出战斗状态】回到脱战模式");

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
                player.transform.position = new Vector3(spawnRoom.center.x, spawnRoom.center.y, player.transform.position.z);
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
}