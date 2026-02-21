using UnityEngine;

public class GameController : MonoBehaviour
{
    [SerializeField] private RoomFirstDungeonGenerator dungeonGenerator;
    
    [SerializeField] private bool autoGenerateOnStart = true;

    [Header("战斗状态配置")]
    [SerializeField] private GameObject player; // 拖入场景中的Player
    public bool IsInCombat { get; private set; } // 战斗状态

    // 房间检测相关
    private RoomData currentPlayerRoom; // 记录玩家当前所在房间

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
            case RoomData.RoomType.Reward:
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

        if (currentPlayerRoom != null && 
            (currentPlayerRoom.roomType == RoomData.RoomType.Monster || 
             currentPlayerRoom.roomType == RoomData.RoomType.Boss))
        {
            currentPlayerRoom.isCleared = true;
            Debug.Log($"【房间已攻略】{currentPlayerRoom.roomType}房间标记为已攻略，再次进入不会触发战斗");
        }

        Debug.Log("【退出战斗状态】回到脱战模式");

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