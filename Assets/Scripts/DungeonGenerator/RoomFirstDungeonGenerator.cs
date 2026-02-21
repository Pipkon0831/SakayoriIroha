using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class RoomFirstDungeonGenerator : AbstractDungeonGenerator
{
    [SerializeField]
    private int minRoomWidth = 4, minRoomHeight = 4;
    [SerializeField]
    private int dungeonWidth = 20, dungeonHeight = 20;
    [SerializeField]
    [Range(0, 10)]
    private int offset = 1;
    [SerializeField]
    private int corridorWidth = 2;

    [Header("物品/敌人生成配置")]
    [SerializeField] private List<SpawnableObject> spawnableObjects;

    // 保留RoomData列表（后续加屏障直接用）
    public List<RoomData> allRoomData = new List<RoomData>();

    // 可选：生成物体父节点（方便管理）
    [Header("生成物体父节点")]
    [SerializeField] private Transform spawnedObjectsParent;

    protected override void RunProceduralGeneration()
    {
        CreateRooms();
    }

    private void CreateRooms()
    {
        // 清空历史数据
        allRoomData.Clear();
        ClearExistingSpawnedObjects();
        
        // 1. 二进制空间分割生成房间边界
        var generatedRooms = ProceduralGenerationAlgorithms.BinarySpacePartitioning(
            new BoundsInt((Vector3Int)startPosition, new Vector3Int(dungeonWidth, dungeonHeight, 0)), 
            minRoomWidth, minRoomHeight);

        // 2. 遍历生成的房间，存储完整RoomData（保留核心数据，方便后续用）
        HashSet<Vector2Int> floor = new HashSet<Vector2Int>();
        foreach (var roomBounds in generatedRooms)
        {
            RoomData roomData = new RoomData();
            roomData.roomBounds = roomBounds;
            roomData.center = (Vector2Int)Vector3Int.RoundToInt(roomBounds.center);
            roomData.floorPositions = new HashSet<Vector2Int>();
            roomData.borderPositions = new List<Vector2Int>();

            // 存储房间内的地板位置（核心：确保物品只在房间内生成）
            for (int col = offset; col < roomBounds.size.x - offset; col++)
            {
                for (int row = offset; row < roomBounds.size.y - offset; row++)
                {
                    Vector2Int position = (Vector2Int)roomBounds.min + new Vector2Int(col, row);
                    // 校验：确保位置在地牢范围内
                    if (position.x >= startPosition.x && 
                        position.x < startPosition.x + dungeonWidth && 
                        position.y >= startPosition.y && 
                        position.y < startPosition.y + dungeonHeight)
                    {
                        roomData.floorPositions.Add(position);
                        floor.Add(position);
                    }
                }
            }

            // 计算房间边界（保留，后续加屏障直接用）
            CalculateRoomBorder(roomData);

            // 只有有效房间才加入列表
            if (roomData.floorPositions.Count > 0)
            {
                allRoomData.Add(roomData);
            }
        }

        // 3. 收集房间中心点（用于生成走廊）
        List<Vector2Int> roomCenters = new List<Vector2Int>();
        foreach (var roomData in allRoomData)
        {
            roomCenters.Add(roomData.center);
        }

        // 4. 连接房间生成走廊
        HashSet<Vector2Int> corridors = ConnectRooms(roomCenters);
        floor.UnionWith(corridors);

        // 5. 绘制地板和墙壁
        tilemapVisualizer.PaintFloorTiles(floor);
        WallGenerator.CreateWalls(floor, tilemapVisualizer);
    }

    // 保留：计算房间边界（后续加屏障直接调用）
    private void CalculateRoomBorder(RoomData roomData)
    {
        BoundsInt bounds = roomData.roomBounds;
        // 上边界（y最大）
        for (int x = bounds.min.x; x < bounds.max.x; x++)
        {
            roomData.borderPositions.Add(new Vector2Int(x, bounds.max.y - 1));
        }
        // 下边界（y最小）
        for (int x = bounds.min.x; x < bounds.max.x; x++)
        {
            roomData.borderPositions.Add(new Vector2Int(x, bounds.min.y));
        }
        // 左边界（x最小）
        for (int y = bounds.min.y; y < bounds.max.y; y++)
        {
            roomData.borderPositions.Add(new Vector2Int(bounds.min.x, y));
        }
        // 右边界（x最大）
        for (int y = bounds.min.y; y < bounds.max.y; y++)
        {
            roomData.borderPositions.Add(new Vector2Int(bounds.max.x - 1, y));
        }
    }

    // 手动触发放置物品/敌人的方法
    [ContextMenu("在房间内放置物品和敌人")]
    public void SpawnObjectsInRooms()
    {
        // 校验：如果还没生成地牢，先提示
        if (allRoomData.Count == 0)
        {
            Debug.LogWarning("请先生成地牢，再放置物品/敌人！");
            return;
        }

        // 清空场景中已生成的物品/敌人
        ClearExistingSpawnedObjects();

        // 遍历每个房间，生成物品/敌人
        foreach (var roomData in allRoomData)
        {
            foreach (var spawnable in spawnableObjects)
            {
                // 根据概率判断是否生成
                if (Random.value > spawnable.spawnChance)
                    continue;

                // 计算要生成的数量
                int spawnCount = Random.Range(spawnable.spawnCountPerRoom.x, spawnable.spawnCountPerRoom.y + 1);
                
                // 生成指定数量的物体
                for (int j = 0; j < spawnCount; j++)
                {
                    SpawnSingleObjectInRoom(roomData.floorPositions, roomData.roomBounds, spawnable);
                }
            }
        }

        Debug.Log($"已在 {allRoomData.Count} 个房间内放置物品/敌人完成！");
    }

    // 在单个房间内生成单个物体（确保只在房间内）
    private void SpawnSingleObjectInRoom(HashSet<Vector2Int> roomPositions, BoundsInt roomBounds, SpawnableObject spawnable)
    {
        if (spawnable.prefab == null)
        {
            Debug.LogWarning("生成物预制体未配置！");
            return;
        }

        Vector2Int spawnPosition = Vector2Int.zero;
        List<Vector2Int> availablePositions = new List<Vector2Int>(roomPositions);

        // 如果设置为只在中心区域生成，过滤出中心区域的位置
        if (spawnable.spawnInCenterOnly)
        {
            availablePositions = FilterCenterRoomPositions(roomPositions, roomBounds, spawnable.centerOffset);
        }

        // 随机选择一个可用位置
        if (availablePositions.Count > 0)
        {
            spawnPosition = availablePositions[Random.Range(0, availablePositions.Count)];
        }
        else
        {
            Debug.LogWarning("房间内无可用生成位置！");
            return;
        }

        // 转换为世界坐标（+0.5对齐Tile中心）
        Vector3 worldPosition = new Vector3(spawnPosition.x + 0.5f, spawnPosition.y + 0.5f, 0);
        
        // 生成物体
        GameObject spawnedObject = Instantiate(spawnable.prefab, worldPosition, Quaternion.identity);
        // 设置父节点
        if (spawnedObjectsParent != null)
        {
            spawnedObject.transform.SetParent(spawnedObjectsParent);
        }
    }

    // 过滤房间中心区域的位置（避免贴墙）
    private List<Vector2Int> FilterCenterRoomPositions(HashSet<Vector2Int> roomPositions, BoundsInt roomBounds, int centerOffset)
    {
        List<Vector2Int> centerPositions = new List<Vector2Int>();
        
        // 计算中心区域的范围
        int minX = roomBounds.min.x + centerOffset;
        int maxX = roomBounds.max.x - centerOffset;
        int minY = roomBounds.min.y + centerOffset;
        int maxY = roomBounds.max.y - centerOffset;

        // 只从房间实际位置中筛选
        foreach (var pos in roomPositions)
        {
            if (pos.x >= minX && pos.x < maxX && pos.y >= minY && pos.y < maxY)
            {
                centerPositions.Add(pos);
            }
        }

        // 容错：中心区域无位置则用整个房间
        if (centerPositions.Count == 0)
        {
            centerPositions = new List<Vector2Int>(roomPositions);
        }

        return centerPositions;
    }

    // 清空已生成的物品/敌人
    private void ClearExistingSpawnedObjects()
    {
        // 通过父节点清理（更安全，不依赖标签）
        if (spawnedObjectsParent != null)
        {
            // 反向遍历删除所有子物体（避免索引错乱）
            for (int i = spawnedObjectsParent.childCount - 1; i >= 0; i--)
            {
                Transform child = spawnedObjectsParent.GetChild(i);
                DestroyImmediate(child.gameObject);
            }
            // 如果你不想删除父节点本身，注释掉下面这行；如果需要重建父节点，保留逻辑
            // DestroyImmediate(spawnedObjectsParent.gameObject);
            return;
        }
    }

    private HashSet<Vector2Int> ConnectRooms(List<Vector2Int> roomCenters)
    {
        HashSet<Vector2Int> corridors = new HashSet<Vector2Int>();
        var currentRoomCenter = roomCenters[Random.Range(0, roomCenters.Count)];
        roomCenters.Remove(currentRoomCenter);

        while (roomCenters.Count > 0)
        {
            Vector2Int closest = FindClosestPointTo(currentRoomCenter, roomCenters);
            roomCenters.Remove(closest);
            HashSet<Vector2Int> newCorridor = CreateCorridor(currentRoomCenter, closest);
            currentRoomCenter = closest;
            corridors.UnionWith(newCorridor);
        }
        return corridors;
    }

    private HashSet<Vector2Int> CreateCorridor(Vector2Int currentRoomCenter, Vector2Int destination)
    {
        HashSet<Vector2Int> corridor = new HashSet<Vector2Int>();
        var position = currentRoomCenter;
        AddCorridorWidth(corridor, position, corridorWidth);

        while (position.y != destination.y)
        {
            position += destination.y > position.y ? Vector2Int.up : Vector2Int.down;
            AddCorridorWidth(corridor, position, corridorWidth);
        }

        while (position.x != destination.x)
        {
            position += destination.x > position.x ? Vector2Int.right : Vector2Int.left;
            AddCorridorWidth(corridor, position, corridorWidth);
        }
        return corridor;
    }

    private void AddCorridorWidth(HashSet<Vector2Int> corridor, Vector2Int centerPos, int width)
    {
        if (width <= 1)
        {
            corridor.Add(centerPos);
            return;
        }

        for (int x = -width / 2; x <= width / 2; x++)
        {
            for (int y = -width / 2; y <= width / 2; y++)
            {
                Vector2Int newPos = centerPos + new Vector2Int(x, y);
                corridor.Add(newPos);
            }
        }
    }

    private Vector2Int FindClosestPointTo(Vector2Int currentRoomCenter, List<Vector2Int> roomCenters)
    {
        Vector2Int closest = Vector2Int.zero;
        float distance = float.MaxValue;
        foreach (var position in roomCenters)
        {
            float currentDistance = Vector2.Distance(position, currentRoomCenter);
            if (currentDistance < distance)
            {
                distance = currentDistance;
                closest = position;
            }
        }
        return closest;
    }
    
}