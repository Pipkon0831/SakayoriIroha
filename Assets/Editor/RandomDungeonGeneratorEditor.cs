using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AbstractDungeonGenerator), true)]
public class RandomDungeonGeneratorEditor : Editor
{
    AbstractDungeonGenerator generator;
    RoomFirstDungeonGenerator roomGenerator;

    private void Awake()
    {
        generator = (AbstractDungeonGenerator)target;
        roomGenerator = generator as RoomFirstDungeonGenerator;
    }

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        
        // 生成地牢按钮
        if(GUILayout.Button("创建地牢"))
        {
            generator.GenerateDungeon();
        }

        // 新增：放置物品/敌人按钮（仅当是RoomFirstDungeonGenerator时显示）
        if (roomGenerator != null && GUILayout.Button("在房间内放置物品和敌人"))
        {
            roomGenerator.SpawnObjectsInRooms();
        }
    }
}