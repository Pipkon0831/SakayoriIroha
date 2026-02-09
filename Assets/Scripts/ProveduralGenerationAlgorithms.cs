using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class ProceduralGenerationAlgorithms
{
    public static HashSet<Vector2Int> SimpleRandomWalk(Vector2Int startPosition, int walkLength)
    {
        HashSet<Vector2Int> path = new HashSet<Vector2Int>();

        path.Add(startPosition);
        var previousition = startPosition;

        for (int i = 0; i < walkLength; i++)
        {
            var newPosition = previousition + Direction2D.GetRandomCardinalDirection();
            path.Add(newPosition);
            previousition = newPosition;
        }
        return path;
    }

}

public static class Direction2D
{
    public static List<Vector2Int> cardinalDirectionsList = new List<Vector2Int>()
    {
        new Vector2Int(0, 1), // 上
        new Vector2Int(1, 0), // 右
        new Vector2Int(0, -1), // 下
        new Vector2Int(-1, 0), // 左
    };

    public static Vector2Int GetRandomCardinalDirection()
    {
        return cardinalDirectionsList[Random.Range(0, cardinalDirectionsList.Count)];
    }
}