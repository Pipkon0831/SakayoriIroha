using UnityEngine;

[CreateAssetMenu(fileName = "NPCProfile", menuName = "Thesis/NPC Profile")]
public class NPCProfile : ScriptableObject
{
    public string npcName = "NPC";

    [TextArea(2, 6)]
    public string persona = "冷静、克制、说话简短。";

    [TextArea(2, 6)]
    public string background = "你是地牢中的引导者。";

    [TextArea(2, 6)]
    public string speakingStyle = "中文；少废话；不使用表情；避免长段落。";
}