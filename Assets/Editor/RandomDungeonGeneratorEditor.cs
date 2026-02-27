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
        
        if(GUILayout.Button("创建地牢"))
        {
            generator.GenerateDungeon();
        }

        if (roomGenerator != null && GUILayout.Button("在房间内放置物品和敌人"))
        {
            roomGenerator.SpawnObjectsInRooms();
        }
    }
}