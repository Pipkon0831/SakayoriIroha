using System.Collections.Generic;
using UnityEngine;

public class NPCDialogueCache : MonoBehaviour
{
    public static NPCDialogueCache Instance { get; private set; }

    // npcName -> opening line（会被 Consume 消费掉）
    private readonly Dictionary<string, string> _openingByNpc = new Dictionary<string, string>();

    // npcName -> last opening（用于“避免复读”）
    private readonly Dictionary<string, string> _lastOpeningByNpc = new Dictionary<string, string>();

    private readonly Dictionary<string, string> _latestPrefetchedByNpc = new Dictionary<string, string>();

    public bool HasPrefetchedOpening => _openingByNpc.Count > 0;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public bool HasOpeningFor(string npcName)
    {
        npcName = NormalizeName(npcName);
        return _openingByNpc.ContainsKey(npcName);
    }

    public string PeekLatestPrefetchedOrEmpty(string npcName)
    {
        npcName = NormalizeName(npcName);
        return _latestPrefetchedByNpc.TryGetValue(npcName, out var v) ? (v ?? "") : "";
    }

    public void SetOpening(string npcName, string line)
    {
        npcName = NormalizeName(npcName);
        line = (line ?? "").Trim();
        if (string.IsNullOrWhiteSpace(line)) return;

        _openingByNpc[npcName] = line;

        _latestPrefetchedByNpc[npcName] = line;

        Debug.Log($"[NPCDialogueCache] SetOpening npc={npcName}, line={line}");
    }

    /// <summary>
    /// 优先按 npcName 精确取；取不到时若只有一条缓存，允许“兜底取那条”
    /// </summary>
    public string ConsumeOpeningOrEmpty(string npcName)
    {
        npcName = NormalizeName(npcName);

        if (_openingByNpc.TryGetValue(npcName, out var line))
        {
            _openingByNpc.Remove(npcName);
            RememberLast(npcName, line);
            Debug.Log($"[NPCDialogueCache] ConsumeOpening npc={npcName}, line={line}");
            return line;
        }

        if (_openingByNpc.Count == 1)
        {
            foreach (var kv in _openingByNpc)
            {
                _openingByNpc.Clear();
                RememberLast(kv.Key, kv.Value);
                Debug.LogWarning($"[NPCDialogueCache] ConsumeOpening mismatch. want={npcName}, use cached npc={kv.Key}, line={kv.Value}");
                return kv.Value;
            }
        }

        return "";
    }

    public string GetLastOpeningLineOrEmpty(string npcName)
    {
        npcName = NormalizeName(npcName);
        return _lastOpeningByNpc.TryGetValue(npcName, out var v) ? (v ?? "") : "";
    }

    private void RememberLast(string npcName, string line)
    {
        npcName = NormalizeName(npcName);
        if (string.IsNullOrWhiteSpace(line)) return;
        _lastOpeningByNpc[npcName] = line.Trim();
    }

    public void ClearAll()
    {
        _openingByNpc.Clear();
        _lastOpeningByNpc.Clear();
        _latestPrefetchedByNpc.Clear();
    }

    private static string NormalizeName(string s)
    {
        s = (s ?? "").Trim();
        return string.IsNullOrWhiteSpace(s) ? "NPC" : s;
    }
}