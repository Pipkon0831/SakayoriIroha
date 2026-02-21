using UnityEngine;

/// <summary>
/// 游戏控制器 - 管理游戏启动流程、战斗状态、房间检测
/// </summary>
public class GameController : MonoBehaviour
{
    // 引用场景中的地牢生成器组件
    [SerializeField] private RoomFirstDungeonGenerator dungeonGenerator;
    
    // 是否在游戏启动时自动生成地牢
    [SerializeField] private bool autoGenerateOnStart = true;

    // ========== 新增：战斗状态管理 ==========
    [Header("战斗状态配置")]
    [SerializeField] private GameObject player; // 拖入场景中的Player
    public bool IsInCombat { get; private set; } // 战斗状态（外部可读取，内部修改）

    // 房间检测相关
    private RoomData currentPlayerRoom; // 记录玩家当前所在房间

    // 游戏启动时执行
    private void Start()
    {
        if (autoGenerateOnStart)
        {
            // 自动执行地牢生成和物品放置
            GenerateGameDungeon();
        }

        // 初始化状态
        IsInCombat = false;
    }

    // 每一帧检测：房间切换 + Esc键脱战
    private void Update()
    {
        // 检测Esc键：脱战
        if (Input.GetKeyDown(KeyCode.Escape) && IsInCombat)
        {
            ExitCombatState();
        }

        // 检测玩家房间切换
        CheckPlayerRoomChange();
    }

    /// <summary>
    /// 生成地牢并放置物品的核心方法
    /// </summary>
    public void GenerateGameDungeon()
    {
        // 安全校验：确保引用的地牢生成器不为空
        if (dungeonGenerator == null)
        {
            Debug.LogError("GameController: 未配置地牢生成器引用！请在Inspector中指定RoomFirstDungeonGenerator组件");
            return;
        }

        // 第一步：生成地牢（房间+走廊+墙壁）
        dungeonGenerator.GenerateDungeon();
        
        // 第二步：在生成的地牢中放置物品/敌人
        dungeonGenerator.SpawnObjectsInRooms();
        
        Debug.Log("GameController: 地牢生成和物品放置完成！");
    }

    // 提供给UI按钮等手动触发的方法（可选）
    [ContextMenu("手动重新生成地牢")]
    public void RegenerateDungeon()
    {
        GenerateGameDungeon();
    }

    // ========== 新增：检测玩家房间切换 ==========
    private void CheckPlayerRoomChange()
    {
        if (player == null || dungeonGenerator.allRoomData.Count == 0)
            return;

        // 转换玩家世界坐标为网格坐标
        Vector2Int playerGridPos = new Vector2Int(
            Mathf.FloorToInt(player.transform.position.x),
            Mathf.FloorToInt(player.transform.position.y)
        );

        // 查找玩家当前所在房间
        RoomData newRoom = null;
        foreach (var room in dungeonGenerator.allRoomData)
        {
            if (room.floorPositions.Contains(playerGridPos))
            {
                newRoom = room;
                break;
            }
        }

        // 如果进入了新房间
        if (newRoom != null && newRoom != currentPlayerRoom)
        {
            currentPlayerRoom = newRoom;
            OnEnterNewRoom(newRoom);
        }
    }

    // ========== 新增：进入新房间的处理逻辑 ==========
    private void OnEnterNewRoom(RoomData room)
    {
        // 1. 打印房间类型（仅进入时显示）
        Debug.Log($"【玩家进入房间】类型：{room.roomType} | 房间中心：{room.center}");

        // 2. 判断是否需要进入战斗状态
        switch (room.roomType)
        {
            case RoomData.RoomType.Monster:
            case RoomData.RoomType.Boss:
                EnterCombatState(room.roomType);
                break;
            case RoomData.RoomType.Spawn:
            case RoomData.RoomType.Reward:
                ExitCombatState();
                break;
        }
    }

    // ========== 新增：进入战斗状态 ==========
    private void EnterCombatState(RoomData.RoomType roomType)
    {
        // 如果已经在战斗中，就不要重复进入
        if (IsInCombat)
            return;
    
        IsInCombat = true;
    
        Debug.Log($"【进入战斗状态】房间类型：{roomType} | 按Esc键脱战");
    
        // 可扩展逻辑
    }

    // ========== 新增：退出战斗状态 ==========
    public void ExitCombatState()
    {
        // ⭐ 只有在战斗中才允许退出
        if (!IsInCombat)
            return;

        IsInCombat = false;

        Debug.Log("【退出战斗状态】回到脱战模式");

        // 可扩展逻辑
    }
}