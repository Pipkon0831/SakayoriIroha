using UnityEngine;

// 保留你现有的SpawnableObject（无需修改）
[System.Serializable]
public class SpawnableObject
{
    // 要生成的预制体（物品/敌人）
    [Tooltip("要生成的物品/敌人预制体")]
    public GameObject prefab;
    
    // 每个房间生成的数量（范围）
    [Tooltip("每个房间生成的数量范围（最小值-最大值）")]
    public Vector2Int spawnCountPerRoom = new Vector2Int(1, 3);
    
    // 生成概率（0-1，1=100%生成）
    [Tooltip("生成概率（0=不生成，1=100%生成）")]
    [Range(0f, 1f)]
    public float spawnChance = 0.8f;
    
    // 是否只在房间中心区域生成（避免贴墙）
    [Tooltip("是否只在房间中心区域生成（勾选后不会贴墙生成）")]
    public bool spawnInCenterOnly = true;
    
    // 中心区域偏移（避免完全贴墙）
    [Tooltip("中心区域偏移量（数值越大，生成位置越靠近房间正中心）")]
    public int centerOffset = 2;
}