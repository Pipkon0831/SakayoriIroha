using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NPCPersonality_", menuName = "Thesis/NPC Personality Definition")]
public class NPCPersonalityDefinition : ScriptableObject
{
    [Header("Identity")]
    public string personalityId = "default";
    public string displayName = "默认";

    [Header("LLM Prompt Injection")]
    [TextArea(3, 12)]
    public string systemPromptAddon =
        "你说话要像真人，不要重复短句，不要像系统提示。";

    [TextArea(2, 8)]
    public string openingAddon =
        "开场白要有情绪、评价，并抛出一个具体问题引导玩家回应。禁止只说“继续/说吧”。";

    [TextArea(2, 10)]
    public string decisionAddon =
        "你需要更明确的态度与评价，并倾向给出可玩但不过分极端的事件。";

    [Header("Portrait (reserved)")]
    public List<AffinityPortrait> portraits = new List<AffinityPortrait>();

    [Serializable]
    public class AffinityPortrait
    {
        public int minAffinity = -999;
        public int maxAffinity = 999;
        public Sprite portrait;
    }

    public Sprite GetPortraitByAffinity(int affinity)
    {
        if (portraits == null || portraits.Count == 0) return null;
        for (int i = 0; i < portraits.Count; i++)
        {
            var p = portraits[i];
            if (p == null) continue;
            if (affinity >= p.minAffinity && affinity <= p.maxAffinity)
                return p.portrait;
        }
        return null;
    }
}