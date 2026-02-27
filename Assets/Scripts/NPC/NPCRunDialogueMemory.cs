using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class NPCRunDialogueMemory : MonoBehaviour
{
    public static NPCRunDialogueMemory Instance { get; private set; }

    public enum Speaker
    {
        Player,
        NPC
    }

    [Serializable]
    public class Turn
    {
        public Speaker speaker;
        public string text;
        public int floorIndex;
        public float time; // Time.time
    }

    [Header("Storage")]
    [SerializeField] private int maxStoredTurns = 200; // 一局最多记录多少条（防止无限膨胀）

    [Header("Context Build")]
    [SerializeField] private int contextMaxTurns = 18;     // 给LLM时最多带最近多少条
    [SerializeField] private int contextMaxChars = 1400;   // 给LLM时总字符上限（中文大概够用）
    [SerializeField] private bool includeFloorTag = true;  // 是否带“第X层”提示

    private readonly List<Turn> _turns = new List<Turn>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>新开一局时调用：清空对话记忆</summary>
    public void ClearRunMemory()
    {
        _turns.Clear();
    }

    public void AddPlayerLine(string text, int floorIndex)
    {
        AddInternal(Speaker.Player, text, floorIndex);
    }

    public void AddNpcLine(string text, int floorIndex)
    {
        AddInternal(Speaker.NPC, text, floorIndex);
    }

    private void AddInternal(Speaker speaker, string text, int floorIndex)
    {
        text = (text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        _turns.Add(new Turn
        {
            speaker = speaker,
            text = text,
            floorIndex = floorIndex,
            time = Time.time
        });

        // 防止爆内存：超过上限就丢最早的
        if (_turns.Count > maxStoredTurns)
            _turns.RemoveRange(0, _turns.Count - maxStoredTurns);
    }

    /// <summary>
    /// 生成给 LLM 用的上下文（最近N条 + 字符上限截断）
    /// 格式示例：
    /// [F3] 玩家：xxxx
    /// [F3] NPC：xxxx
    /// </summary>
    public string BuildContextForLLM()
    {
        if (_turns.Count == 0) return "";

        int take = Mathf.Clamp(contextMaxTurns, 1, 200);
        int start = Mathf.Max(0, _turns.Count - take);

        // 先拼最近N条
        var sb = new StringBuilder(2048);
        for (int i = start; i < _turns.Count; i++)
        {
            var t = _turns[i];
            if (t == null) continue;

            if (includeFloorTag)
                sb.Append($"[F{t.floorIndex}] ");

            sb.Append(t.speaker == Speaker.Player ? "玩家：" : "NPC：");
            sb.AppendLine(Sanitize(t.text));
        }

        // 再按字符上限裁剪（保留末尾更重要）
        string s = sb.ToString().Trim();
        if (s.Length <= contextMaxChars) return s;

        // 末尾截断：保留最后 contextMaxChars
        int cutStart = s.Length - contextMaxChars;
        if (cutStart < 0) cutStart = 0;

        string trimmed = s.Substring(cutStart).Trim();

        // 为了可读性：如果截断发生在行中间，尽量对齐到下一行
        int firstNewline = trimmed.IndexOf('\n');
        if (firstNewline > 0 && firstNewline < 40)
            trimmed = trimmed.Substring(firstNewline + 1).Trim();

        return "（仅保留最近对话片段）\n" + trimmed;
    }

    private static string Sanitize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // 防止把超多换行/空白塞爆上下文
        s = s.Replace("\r", "").Trim();
        while (s.Contains("\n\n")) s = s.Replace("\n\n", "\n");
        return s;
    }

    // 可选：调试用
    public int Count => _turns.Count;
}