using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

// 直接继承抽象类，不再依赖SimpleRandomWalkDungeonGenerator
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
    private int corridorWidth = 2; // 保留走廊宽度配置（可根据需求改为固定值）

    protected override void RunProceduralGeneration()
    {
        CreateRooms();
    }

    private void CreateRooms()
    {
        // 1. 二进制空间分割生成房间边界
        var roomsList = ProceduralGenerationAlgorithms.BinarySpacePartitioning(
            new BoundsInt((Vector3Int)startPosition, new Vector3Int(dungeonWidth, dungeonHeight, 0)), 
            minRoomWidth, minRoomHeight);

        // 2. 生成简单房间（移除随机游走房间逻辑）
        HashSet<Vector2Int> floor = CreateSimpleRooms(roomsList);

        // 3. 收集房间中心点
        List<Vector2Int> roomCenters = new List<Vector2Int>();
        foreach (var room in roomsList)
        {
            roomCenters.Add((Vector2Int)Vector3Int.RoundToInt(room.center));
        }

        // 4. 连接房间生成走廊
        HashSet<Vector2Int> corridors = ConnectRooms(roomCenters);
        floor.UnionWith(corridors);

        // 5. 绘制地板和墙壁
        tilemapVisualizer.PaintFloorTiles(floor);
        WallGenerator.CreateWalls(floor, tilemapVisualizer);
    }

    // 连接房间生成走廊（保留宽度扩展）
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

    // 生成两点间的走廊（带宽度扩展）
    private HashSet<Vector2Int> CreateCorridor(Vector2Int currentRoomCenter, Vector2Int destination)
    {
        HashSet<Vector2Int> corridor = new HashSet<Vector2Int>();
        var position = currentRoomCenter;
        AddCorridorWidth(corridor, position, corridorWidth);

        // 先处理Y轴
        while (position.y != destination.y)
        {
            position += destination.y > position.y ? Vector2Int.up : Vector2Int.down;
            AddCorridorWidth(corridor, position, corridorWidth);
        }

        // 再处理X轴
        while (position.x != destination.x)
        {
            position += destination.x > position.x ? Vector2Int.right : Vector2Int.left;
            AddCorridorWidth(corridor, position, corridorWidth);
        }
        return corridor;
    }

    // 走廊宽度扩展辅助方法
    private void AddCorridorWidth(HashSet<Vector2Int> corridor, Vector2Int centerPos, int width)
    {
        if (width <= 1)
        {
            corridor.Add(centerPos);
            return;
        }

        // 向中心位置四周扩展宽度
        for (int x = -width / 2; x <= width / 2; x++)
        {
            for (int y = -width / 2; y <= width / 2; y++)
            {
                Vector2Int newPos = centerPos + new Vector2Int(x, y);
                corridor.Add(newPos);
            }
        }
    }

    // 找最近的房间中心点
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

    // 生成简单房间（核心保留）
    private HashSet<Vector2Int> CreateSimpleRooms(List<BoundsInt> roomsList)
    {
        HashSet<Vector2Int> floor = new HashSet<Vector2Int>();
        foreach (var room in roomsList)
        {
            for (int col = offset; col < room.size.x - offset; col++)
            {
                for (int row = offset; row < room.size.y - offset; row++)
                {
                    Vector2Int position = (Vector2Int)room.min + new Vector2Int(col, row);
                    floor.Add(position);
                }
            }
        }
        return floor;
    }
}