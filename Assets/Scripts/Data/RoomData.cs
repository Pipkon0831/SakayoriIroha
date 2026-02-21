using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 存储房间的所有关键数据（范围、中心点、地板位置、边界等）
/// </summary>
[System.Serializable]
public class RoomData
{
    // 房间类型枚举（内嵌）
    public enum RoomType
    {
        Spawn,      // 出生房（唯一）
        Monster,    // 怪物房
        Reward,     // 奖励房
        Boss        // Boss房（唯一）
    }

    // 房间的边界（完整范围）
    public BoundsInt roomBounds;
    // 房间的中心点
    public Vector2Int center;
    // 房间内的地板位置（用于判断玩家是否在房间内）
    public HashSet<Vector2Int> floorPositions;
    // 房间的边界坐标（用于生成屏障）
    public List<Vector2Int> borderPositions;
    // 是否被封锁（有屏障）
    public bool isBlocked = false;
    // 存储生成的屏障物体（方便后续销毁）
    public List<GameObject> barrierObjects = new List<GameObject>();
    // 房间类型属性
    public RoomType roomType;
}