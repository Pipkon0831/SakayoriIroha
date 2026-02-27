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

    // -------------------------
    // NEW: Fallback Lines (Per Personality)
    // -------------------------
    [Header("Fallback Lines (Per Personality)")]
    [Tooltip("用于：每层首句（Opening）请求失败/超时/异常时的兜底话术。留空则回退到UI本地兜底。")]
    [TextArea(1, 3)]
    public List<string> openingFallbackLines = new List<string>()
    {
        "……先别急。你想怎么走？",
        "我这里信号不太稳。你先说重点。",
        "说清楚：你打算怎么做？"
    };

    [Tooltip("用于：玩家发送后（Decision）LLM异常/JSON不合法时的兜底话术。留空则回退到UI默认兜底。")]
    [TextArea(1, 3)]
    public List<string> decisionFallbackLines = new List<string>()
    {
        "……我需要再想想。先按你说的来。",
        "行，我听到了。先继续推进。",
        "别废话了，先过这一层。"
    };

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

    // -------------------------
    // Helpers
    // -------------------------
    public string GetRandomOpeningFallbackOrEmpty()
    {
        return PickRandomOrEmpty(openingFallbackLines);
    }

    public string GetRandomDecisionFallbackOrEmpty()
    {
        return PickRandomOrEmpty(decisionFallbackLines);
    }

    private static string PickRandomOrEmpty(List<string> list)
    {
        if (list == null || list.Count == 0) return "";
        List<string> candidates = null;

        for (int i = 0; i < list.Count; i++)
        {
            var s = (list[i] ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s)) continue;
            candidates ??= new List<string>();
            candidates.Add(s);
        }

        if (candidates == null || candidates.Count == 0) return "";
        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }
}