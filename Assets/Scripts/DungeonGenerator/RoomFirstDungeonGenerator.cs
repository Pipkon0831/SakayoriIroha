using System;
using System.Collections.Generic;
using System.Linq; // 添加这一行
using UnityEngine;
using Random = UnityEngine.Random;


public class RoomFirstDungeonGenerator : AbstractDungeonGenerator
{
    [SerializeField] private int minRoomWidth = 4, minRoomHeight = 4;
    [SerializeField] private int dungeonWidth = 20, dungeonHeight = 20;
    [SerializeField] [Range(0, 10)] private int offset = 1;
    [SerializeField] private int corridorWidth = 2;

    [Header("物品/敌人生成配置")]
    [SerializeField] private List<SpawnableObject> spawnableObjects;

    [Header("房间类型配置")]
    [SerializeField] private int monsterRoomWeight = 70;
    [SerializeField] private int rewardRoomWeight = 20;
    [SerializeField] private int minRoomsForBoss = 5;

    [Header("Player配置")]
    [SerializeField] private GameObject player;

    public List<RoomData> allRoomData = new List<RoomData>();
    [Header("生成物体父节点")]
    [SerializeField] private Transform spawnedObjectsParent;

    private Dictionary<RoomData, List<RoomData>> roomConnections;

    protected override void RunProceduralGeneration()
    {
        CreateRooms();
        MovePlayerToSpawnRoomCenter();
    }

    private void CreateRooms()
    {
        allRoomData.Clear();
        ClearExistingSpawnedObjects();

        var generatedRooms = ProceduralGenerationAlgorithms.BinarySpacePartitioning(
            new BoundsInt((Vector3Int)startPosition, new Vector3Int(dungeonWidth, dungeonHeight)),
            minRoomWidth, minRoomHeight);

        HashSet<Vector2Int> floor = new HashSet<Vector2Int>();

        foreach (var roomBounds in generatedRooms)
        {
            RoomData roomData = new RoomData
            {
                roomBounds = roomBounds,
                center = (Vector2Int)Vector3Int.RoundToInt(roomBounds.center),
                floorPositions = new HashSet<Vector2Int>(),
                borderPositions = new List<Vector2Int>()
            };

            for (int col = offset; col < roomBounds.size.x - offset; col++)
            {
                for (int row = offset; row < roomBounds.size.y - offset; row++)
                {
                    Vector2Int position = (Vector2Int)roomBounds.min + new Vector2Int(col, row);
                    if (IsValidPosition(position))
                    {
                        roomData.floorPositions.Add(position);
                        floor.Add(position);
                    }
                }
            }

            CalculateRoomBorder(roomData);

            if (roomData.floorPositions.Count > 0)
                allRoomData.Add(roomData);
        }

        // ⭐ 初始化真实图结构
        roomConnections = new Dictionary<RoomData, List<RoomData>>();
        foreach (var room in allRoomData)
            roomConnections[room] = new List<RoomData>();

        // ⭐ 先生成真实走廊 + 构建图
        List<Vector2Int> roomCenters = new List<Vector2Int>(allRoomData.ConvertAll(r => r.center));
        HashSet<Vector2Int> corridors = ConnectRooms(roomCenters);
        floor.UnionWith(corridors);

        // ⭐ 再根据真实图分配房间类型
        AssignRoomTypes();

        tilemapVisualizer.PaintFloorTiles(floor);
        WallGenerator.CreateWalls(floor, tilemapVisualizer);
    }

    private void AssignRoomTypes()
    {
        if (allRoomData.Count == 0)
            return;

        foreach (var room in allRoomData)
            room.roomType = RoomData.RoomType.Monster;

        RoomData bestRoomA = null;
        RoomData bestRoomB = null;
        int maxDistance = -1;

        // ⭐ 对每个节点跑 BFS
        foreach (var room in allRoomData)
        {
            Dictionary<RoomData, int> distances = BFSCalculateDistances(room);

            foreach (var kvp in distances)
            {
                if (kvp.Value > maxDistance)
                {
                    maxDistance = kvp.Value;
                    bestRoomA = room;
                    bestRoomB = kvp.Key;
                }
            }
        }

        if (bestRoomA != null)
            bestRoomA.roomType = RoomData.RoomType.Spawn;

        if (bestRoomB != null && bestRoomB != bestRoomA && allRoomData.Count >= minRoomsForBoss)
            bestRoomB.roomType = RoomData.RoomType.Boss;

        // 剩余按权重分配
        int totalWeight = monsterRoomWeight + rewardRoomWeight;

        foreach (var room in allRoomData)
        {
            if (room.roomType != RoomData.RoomType.Monster)
                continue;

            room.roomType = Random.Range(0, totalWeight) < monsterRoomWeight
                ? RoomData.RoomType.Monster
                : RoomData.RoomType.Reward;
        }

        Debug.Log($"✅ Spawn: {bestRoomA.center} | Boss: {bestRoomB.center} | 最大最短距离: {maxDistance}");
    }
    
    private RoomData GetRoomByCenter(Vector2Int center)
    {
        return allRoomData.Find(r => r.center == center);
    }


// 计算两个房间之间的最短距离
private int GetShortestDistance(RoomData roomA, RoomData roomB)
{
    // 使用 BFS 计算最短路径
    Dictionary<RoomData, int> distances = BFSCalculateDistances(roomA);
    return distances.ContainsKey(roomB) ? distances[roomB] : int.MaxValue;
}

// 找到最大最短距离的房间对
    private (RoomData, RoomData) FindLongestShortestPathEnds()
    {
        if (allRoomData.Count == 0) return (null, null);

        // 从任意房间开始，找到第一个最远房间
        RoomData startRoom = allRoomData[0];
        RoomData farthestRoomA = BFSFindFarthestRoom(startRoom);

        // 从房间A开始，找到最远的房间B
        RoomData farthestRoomB = BFSFindFarthestRoom(farthestRoomA);

        Debug.Log($"✅ 最长的最短路径：{farthestRoomA.center} <-> {farthestRoomB.center}");

        return (farthestRoomA, farthestRoomB);
    }

    private RoomData BFSFindFarthestRoom(RoomData startRoom)
    {
        Dictionary<RoomData, int> distances = BFSCalculateDistances(startRoom);
        return GetFarthestRoom(distances);
    }

    private void LogRoomDistribution()
    {
        int bossCount = allRoomData.Count(r => r.roomType == RoomData.RoomType.Boss);
        int monsterCount = allRoomData.Count(r => r.roomType == RoomData.RoomType.Monster);
        int rewardCount = allRoomData.Count(r => r.roomType == RoomData.RoomType.Reward); 

        Debug.Log($"✅ 房间分配完成：出生房x1 | Boss房x{bossCount} | 怪物房x{monsterCount} | 奖励房x{rewardCount}");
    }

    private bool IsValidPosition(Vector2Int position)
    {
        return position.x >= startPosition.x && position.x < startPosition.x + dungeonWidth &&
               position.y >= startPosition.y && position.y < startPosition.y + dungeonHeight;
    }

    private (RoomData, RoomData) FindFarthestRoomPairInGraph()
    {
        if (allRoomData.Count == 0) return (null, null);
        if (allRoomData.Count == 1) return (allRoomData[0], null);

        RoomData startRoom = allRoomData[0];
        Dictionary<RoomData, int> distancesFromStart = BFSCalculateDistances(startRoom);
        RoomData farthestRoomA = GetFarthestRoom(distancesFromStart);

        Dictionary<RoomData, int> distancesFromA = BFSCalculateDistances(farthestRoomA);
        RoomData farthestRoomB = GetFarthestRoom(distancesFromA);

        int maxDistance = distancesFromA[farthestRoomB];
        Debug.Log($"✅ 最远房间对：{farthestRoomA.center} <-> {farthestRoomB.center} | 距离={maxDistance}");

        return (farthestRoomA, farthestRoomB);
    }

    private Dictionary<RoomData, int> BFSCalculateDistances(RoomData startRoom)
    {
        Dictionary<RoomData, int> distances = new Dictionary<RoomData, int>();
        Queue<RoomData> queue = new Queue<RoomData>();

        foreach (var room in allRoomData)
        {
            distances[room] = -1; // 未访问
        }

        distances[startRoom] = 0;
        queue.Enqueue(startRoom);

        while (queue.Count > 0)
        {
            RoomData currentRoom = queue.Dequeue();
            foreach (var neighbor in roomConnections[currentRoom])
            {
                if (distances[neighbor] == -1)
                {
                    distances[neighbor] = distances[currentRoom] + 1; // 距离+1
                    queue.Enqueue(neighbor);
                }
            }
        }

        return distances;
    }

    private RoomData GetFarthestRoom(Dictionary<RoomData, int> distances)
    {
        RoomData farthestRoom = null;
        int maxDistance = -1;

        foreach (var kvp in distances)
        {
            if (kvp.Value > maxDistance)
            {
                maxDistance = kvp.Value;
                farthestRoom = kvp.Key;
            }
        }

        return farthestRoom;
    }

  

    private void MovePlayerToSpawnRoomCenter()
    {
        if (player == null)
        {
            Debug.LogWarning("⚠️ 请在Inspector中拖入场景内的Player对象！");
            return;
        }

        RoomData spawnRoom = allRoomData.Find(r => r.roomType == RoomData.RoomType.Spawn);
        if (spawnRoom == null) return;

        Vector3 centerPos = new Vector3(
            spawnRoom.center.x + 0.5f,
            spawnRoom.center.y + 0.5f,
            0
        );

        player.transform.position = centerPos;
    }

    private void CalculateRoomBorder(RoomData roomData)
    {
        BoundsInt bounds = roomData.roomBounds;
        for (int x = bounds.min.x; x < bounds.max.x; x++)
            roomData.borderPositions.Add(new Vector2Int(x, bounds.max.y - 1));
        for (int x = bounds.min.x; x < bounds.max.x; x++)
            roomData.borderPositions.Add(new Vector2Int(x, bounds.min.y));
        for (int y = bounds.min.y; y < bounds.max.y; y++)
            roomData.borderPositions.Add(new Vector2Int(bounds.min.x, y));
        for (int y = bounds.min.y; y < bounds.max.y; y++)
            roomData.borderPositions.Add(new Vector2Int(bounds.max.x - 1, y));
    }

    [ContextMenu("📌 手动放置物品和敌人")]
    public void SpawnObjectsInRooms()
    {
        if (allRoomData.Count == 0)
        {
            Debug.LogWarning("⚠️ 请先生成地牢，再放置物品/敌人！");
            return;
        }

        ClearExistingSpawnedObjects();

        foreach (var room in allRoomData)
        {
            foreach (var spawnable in spawnableObjects)
            {
                if (!IsSpawnAllowedForRoomType(room, spawnable)) continue;
                if (Random.value > spawnable.spawnChance) continue;

                int spawnCount = Random.Range(spawnable.spawnCountPerRoom.x, spawnable.spawnCountPerRoom.y + 1);
                for (int i = 0; i < spawnCount; i++)
                {
                    SpawnSingleObject(room.floorPositions, room.roomBounds, spawnable);
                }
            }
        }

        Debug.Log($"✅ 物品/敌人生成完成！");
    }

    private bool IsSpawnAllowedForRoomType(RoomData room, SpawnableObject spawnable)
    {
        if (spawnable.prefab == null) return false;
        
        switch (room.roomType)
        {
            case RoomData.RoomType.Spawn:
                return false; // 出生房不生成任何东西
            case RoomData.RoomType.Boss:
                return spawnable.prefab.name.Contains("Boss"); // Boss房只生成Boss
            case RoomData.RoomType.Reward:
                return spawnable.prefab.name.Contains("Item") || spawnable.prefab.name.Contains("Reward"); // 奖励房只生成道具
            case RoomData.RoomType.Monster:
                return !spawnable.prefab.name.Contains("Boss") && !spawnable.prefab.name.Contains("Item") && !spawnable.prefab.name.Contains("Reward"); // 怪物房只生成普通怪
            default:
                return true;
        }
    }

    private void SpawnSingleObject(HashSet<Vector2Int> roomPositions, BoundsInt roomBounds, SpawnableObject spawnable)
    {
        List<Vector2Int> availablePositions = new List<Vector2Int>(roomPositions);

        if (spawnable.spawnInCenterOnly)
        {
            availablePositions = FilterCenterPositions(roomPositions, roomBounds, spawnable.centerOffset);
        }

        if (availablePositions.Count == 0) return;

        Vector2Int spawnPos = availablePositions[Random.Range(0, availablePositions.Count)];
        Vector3 worldPos = new Vector3(spawnPos.x + 0.5f, spawnPos.y + 0.5f, 0);

        GameObject spawnedObj = Instantiate(spawnable.prefab, worldPos, Quaternion.identity);
        if (spawnedObjectsParent != null)
        {
            spawnedObj.transform.SetParent(spawnedObjectsParent);
        }
    }

    private List<Vector2Int> FilterCenterPositions(HashSet<Vector2Int> positions, BoundsInt bounds, int offset)
    {
        List<Vector2Int> centerPositions = new List<Vector2Int>();
        int minX = bounds.min.x + offset;
        int maxX = bounds.max.x - offset;
        int minY = bounds.min.y + offset;
        int maxY = bounds.max.y - offset;

        foreach (var pos in positions)
        {
            if (pos.x >= minX && pos.x < maxX && pos.y >= minY && pos.y < maxY)
            {
                centerPositions.Add(pos);
            }
        }

        return centerPositions.Count > 0 ? centerPositions : new List<Vector2Int>(positions);
    }

    private void ClearExistingSpawnedObjects()
    {
        if (spawnedObjectsParent != null)
        {
            for (int i = spawnedObjectsParent.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(spawnedObjectsParent.GetChild(i).gameObject);
            }
        }
    }

    private HashSet<Vector2Int> ConnectRooms(List<Vector2Int> roomCenters)
    {
        HashSet<Vector2Int> corridors = new HashSet<Vector2Int>();

        if (roomCenters.Count == 0)
            return corridors;

        Vector2Int currentCenter = roomCenters[Random.Range(0, roomCenters.Count)];
        roomCenters.Remove(currentCenter);

        while (roomCenters.Count > 0)
        {
            Vector2Int closestCenter = FindClosestRoomCenter(currentCenter, roomCenters);
            roomCenters.Remove(closestCenter);

            // ⭐ 在这里构建真实图连接关系
            RoomData roomA = GetRoomByCenter(currentCenter);
            RoomData roomB = GetRoomByCenter(closestCenter);

            if (roomA != null && roomB != null)
            {
                roomConnections[roomA].Add(roomB);
                roomConnections[roomB].Add(roomA);
            }

            HashSet<Vector2Int> newCorridor = CreateCorridor(currentCenter, closestCenter);
            corridors.UnionWith(newCorridor);

            currentCenter = closestCenter;
        }

        return corridors;
    }

    private HashSet<Vector2Int> CreateCorridor(Vector2Int start, Vector2Int end)
    {
        HashSet<Vector2Int> corridor = new HashSet<Vector2Int>();
        Vector2Int currentPos = start;
        AddCorridorWidth(corridor, currentPos, corridorWidth);

        while (currentPos.y != end.y)
        {
            currentPos += currentPos.y < end.y ? Vector2Int.up : Vector2Int.down;
            AddCorridorWidth(corridor, currentPos, corridorWidth);
        }

        while (currentPos.x != end.x)
        {
            currentPos += currentPos.x < end.x ? Vector2Int.right : Vector2Int.left;
            AddCorridorWidth(corridor, currentPos, corridorWidth);
        }

        return corridor;
    }

    private void AddCorridorWidth(HashSet<Vector2Int> corridor, Vector2Int center, int width)
    {
        if (width <= 1)
        {
            corridor.Add(center);
            return;
        }

        for (int x = -width / 2; x <= width / 2; x++)
        {
            for (int y = -width / 2; y <= width / 2; y++)
            {
                corridor.Add(center + new Vector2Int(x, y));
            }
        }
    }

    private Vector2Int FindClosestRoomCenter(Vector2Int from, List<Vector2Int> centers)
    {
        Vector2Int closest = Vector2Int.zero;
        float minDistance = float.MaxValue;

        foreach (var center in centers)
        {
            float distance = Vector2.Distance(from, center);
            if (distance < minDistance)
            {
                minDistance = distance;
                closest = center;
            }
        }

        return closest;
    }

    public RoomData GetRoomByType(RoomData.RoomType type)
    {
        return allRoomData.Find(r => r.roomType == type);
    }
}