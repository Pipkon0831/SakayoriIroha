using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

[Serializable]
public class RoomSpawnConfig
{
    public RoomData.RoomType roomType; 
    public List<SpawnableObject> spawnableObjects;
}

public enum RoomOverrideMode
{
    None,
    AllMonsterExceptBossAndSpawn,
    AllRewardExceptBossAndSpawn
}

public class RoomFirstDungeonGenerator : AbstractDungeonGenerator
{
    [SerializeField] private int minRoomWidth = 4, minRoomHeight = 4;
    [SerializeField] private int dungeonWidth = 20, dungeonHeight = 20;
    [SerializeField] [Range(0, 10)] private int offset = 1;
    [SerializeField] private int corridorWidth = 2;

    [Header("按房间类型配置生成规则")]
    [SerializeField] private List<RoomSpawnConfig> roomSpawnConfigs;

    [Header("房间类型权重（仅怪物/奖励房）")]
    [SerializeField] private int monsterRoomWeight = 70;
    [SerializeField] private int rewardRoomWeight = 20;
    [SerializeField] private int minRoomsForBoss = 5;

    [Header("Player配置")]
    [SerializeField] private GameObject player;

    public List<RoomData> allRoomData = new List<RoomData>();

    [Header("生成物体父节点")]
    [SerializeField] private Transform spawnedObjectsParent;

    [Header("楼层规则覆盖（由事件系统注入）")]
    [SerializeField] private RoomOverrideMode overrideMode = RoomOverrideMode.None;

    private Dictionary<RoomData, List<RoomData>> roomConnections;

    public void SetRoomOverrideMode(RoomOverrideMode mode)
    {
        overrideMode = mode;
    }

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

        roomConnections = new Dictionary<RoomData, List<RoomData>>();
        foreach (var room in allRoomData)
            roomConnections[room] = new List<RoomData>();

        List<Vector2Int> roomCenters = new List<Vector2Int>(allRoomData.ConvertAll(r => r.center));
        HashSet<Vector2Int> corridors = ConnectRooms(roomCenters);
        floor.UnionWith(corridors);

        AssignRoomTypes();

        tilemapVisualizer.PaintFloorTiles(floor);
        WallGenerator.CreateWalls(floor, tilemapVisualizer);
    }

    private void AssignRoomTypes()
    {
        if (allRoomData.Count == 0) return;

        foreach (var room in allRoomData)
            room.roomType = RoomData.RoomType.Monster;

        RoomData bestRoomA = null;
        RoomData bestRoomB = null;
        int maxDistance = -1;

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

        if (bestRoomA != null) bestRoomA.roomType = RoomData.RoomType.Spawn;

        if (bestRoomB != null && bestRoomB != bestRoomA && allRoomData.Count >= minRoomsForBoss)
            bestRoomB.roomType = RoomData.RoomType.Boss;

        int totalWeight = monsterRoomWeight + rewardRoomWeight;

        foreach (var room in allRoomData)
        {
            if (room.roomType != RoomData.RoomType.Monster) continue;

            room.roomType = Random.Range(0, totalWeight) < monsterRoomWeight
                ? RoomData.RoomType.Monster
                : RoomData.RoomType.Reward;
        }

        // ✅ 覆盖规则：让事件能强制全怪物/全奖励
        ApplyOverrideModeIfNeeded();

       // Debug.Log($"✅ Spawn: {bestRoomA?.center} | Boss: {bestRoomB?.center} | 最大最短距离: {maxDistance} | override: {overrideMode}");
    }

    private void ApplyOverrideModeIfNeeded()
    {
        if (overrideMode == RoomOverrideMode.None) return;

        RoomData.RoomType targetType =
            overrideMode == RoomOverrideMode.AllMonsterExceptBossAndSpawn
                ? RoomData.RoomType.Monster
                : RoomData.RoomType.Reward;

        foreach (var room in allRoomData)
        {
            if (room.roomType == RoomData.RoomType.Spawn) continue;
            if (room.roomType == RoomData.RoomType.Boss) continue;
            room.roomType = targetType;
        }
    }

    private RoomData GetRoomByCenter(Vector2Int center)
    {
        return allRoomData.Find(r => r.center == center);
    }

    private bool IsValidPosition(Vector2Int position)
    {
        return position.x >= startPosition.x && position.x < startPosition.x + dungeonWidth &&
               position.y >= startPosition.y && position.y < startPosition.y + dungeonHeight;
    }

    private Dictionary<RoomData, int> BFSCalculateDistances(RoomData startRoom)
    {
        Dictionary<RoomData, int> distances = new Dictionary<RoomData, int>();
        Queue<RoomData> queue = new Queue<RoomData>();

        foreach (var room in allRoomData)
            distances[room] = -1;

        distances[startRoom] = 0;
        queue.Enqueue(startRoom);

        while (queue.Count > 0)
        {
            RoomData currentRoom = queue.Dequeue();
            foreach (var neighbor in roomConnections[currentRoom])
            {
                if (distances[neighbor] == -1)
                {
                    distances[neighbor] = distances[currentRoom] + 1;
                    queue.Enqueue(neighbor);
                }
            }
        }

        return distances;
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

        Vector3 centerPos = new Vector3(spawnRoom.center.x + 0.5f, spawnRoom.center.y + 0.5f, 0);
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
            RoomSpawnConfig config = roomSpawnConfigs.Find(c => c.roomType == room.roomType);
            if (config == null || config.spawnableObjects == null || config.spawnableObjects.Count == 0)
            {
               // Debug.Log($"📌 房间类型 {room.roomType} 无生成配置，跳过");
                continue;
            }

            foreach (var spawnable in config.spawnableObjects)
            {
                if (spawnable.prefab == null) continue;
                if (Random.value > spawnable.spawnChance) continue;

                int spawnCount = Random.Range(spawnable.spawnCountPerRoom.x, spawnable.spawnCountPerRoom.y + 1);
                for (int i = 0; i < spawnCount; i++)
                    SpawnSingleObject(room.floorPositions, room.roomBounds, spawnable);
            }
        }

        Debug.Log($"✅ 物品/敌人生成完成！");
    }

    private void SpawnSingleObject(HashSet<Vector2Int> roomPositions, BoundsInt roomBounds, SpawnableObject spawnable)
    {
        List<Vector2Int> availablePositions = new List<Vector2Int>(roomPositions);

        if (spawnable.spawnInCenterOnly)
            availablePositions = FilterCenterPositions(roomPositions, roomBounds, spawnable.centerOffset);

        if (availablePositions.Count == 0) return;

        Vector2Int spawnPos = availablePositions[Random.Range(0, availablePositions.Count)];
        Vector3 worldPos = new Vector3(spawnPos.x + 0.5f, spawnPos.y + 0.5f, 0);

        GameObject spawnedObj = Instantiate(spawnable.prefab, worldPos, Quaternion.identity);
        if (spawnedObjectsParent != null) spawnedObj.transform.SetParent(spawnedObjectsParent);
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
                centerPositions.Add(pos);
        }

        return centerPositions.Count > 0 ? centerPositions : new List<Vector2Int>(positions);
    }

    private void ClearExistingSpawnedObjects()
    {
        if (spawnedObjectsParent == null) return;

        for (int i = spawnedObjectsParent.childCount - 1; i >= 0; i--)
            DestroyImmediate(spawnedObjectsParent.GetChild(i).gameObject);
    }

    private HashSet<Vector2Int> ConnectRooms(List<Vector2Int> roomCenters)
    {
        HashSet<Vector2Int> corridors = new HashSet<Vector2Int>();
        if (roomCenters.Count == 0) return corridors;

        Vector2Int currentCenter = roomCenters[Random.Range(0, roomCenters.Count)];
        roomCenters.Remove(currentCenter);

        while (roomCenters.Count > 0)
        {
            Vector2Int closestCenter = FindClosestRoomCenter(currentCenter, roomCenters);
            roomCenters.Remove(closestCenter);

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
        for (int y = -width / 2; y <= width / 2; y++)
            corridor.Add(center + new Vector2Int(x, y));
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