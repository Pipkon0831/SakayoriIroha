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
    [SerializeField] private int maxStoredTurns = 200;

    [Header("Context Build")]
    [SerializeField] private int contextMaxTurns = 18;
    [SerializeField] private int contextMaxChars = 1400;
    [SerializeField] private bool includeFloorTag = true;

    [Header("Narrative Summary")]
    [SerializeField] private bool enableNarrativeSummary = true;

    [Tooltip("摘要会优先保留的最大字符数（剩余给对话摘录）")]
    [SerializeField] private int summaryMaxChars = 520;

    [Tooltip("摘要用于判断“最近”窗口的轮数（从末尾往前数）")]
    [SerializeField] private int summaryRecentWindowTurns = 10;

    private readonly List<Turn> _turns = new List<Turn>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

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

        if (_turns.Count > maxStoredTurns)
            _turns.RemoveRange(0, _turns.Count - maxStoredTurns);
    }

    /// <summary>
    /// 生成给 LLM 用的上下文：
    /// 1) 叙事摘要（更像共同经历）
    /// 2) 关键对话摘录（最近N条，保留原话）
    /// </summary>
    public string BuildContextForLLM()
    {
        if (_turns.Count == 0) return "";

        // 1) 摘要（优先保留）
        string summary = "";
        if (enableNarrativeSummary)
        {
            summary = BuildNarrativeSummary();
            summary = HardTrim(summary, summaryMaxChars);
        }

        // 2) 对话摘录（证据）
        string excerpt = BuildRecentExcerpt();

        // 3) 合并并控制总长度：超了就从“摘录头部”裁掉，尽量保留末尾
        string combined;
        if (!string.IsNullOrWhiteSpace(summary))
        {
            combined = $"【回忆摘要】\n{summary.Trim()}\n\n【对话摘录（最近）】\n{excerpt.Trim()}";
        }
        else
        {
            combined = excerpt.Trim();
        }

        combined = combined.Trim();
        if (combined.Length <= contextMaxChars) return combined;

        // 超总长：优先保摘要；对摘录做末尾保留
        if (!string.IsNullOrWhiteSpace(summary))
        {
            int remaining = Mathf.Max(120, contextMaxChars - ("【回忆摘要】\n".Length + summary.Length + "\n\n【对话摘录（最近）】\n".Length));
            string excerptTrimmed = KeepTail(excerpt, remaining);
            return $"【回忆摘要】\n{summary.Trim()}\n\n【对话摘录（最近）】\n{excerptTrimmed.Trim()}".Trim();
        }

        // 没摘要就直接裁剪整体末尾
        return "（仅保留最近对话片段）\n" + KeepTail(combined, contextMaxChars).Trim();
    }

    /// <summary>
    /// 最近N条对话原文（用于证据）
    /// </summary>
    private string BuildRecentExcerpt()
    {
        int take = Mathf.Clamp(contextMaxTurns, 1, 200);
        int start = Mathf.Max(0, _turns.Count - take);

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

        return sb.ToString().Trim();
    }

    /// <summary>
    /// 更自然的“共同经历”摘要：
    /// - 最近主要发生在第几层
    /// - 玩家大致表达了什么（抽取关键词/意图）
    /// - NPC的语气/互动氛围（轻量估计）
    /// </summary>
    private string BuildNarrativeSummary()
    {
        int window = Mathf.Clamp(summaryRecentWindowTurns, 3, 50);
        int start = Mathf.Max(0, _turns.Count - window);

        // 统计最近主要楼层
        int mainFloor = -1;
        var floorCount = new Dictionary<int, int>();
        for (int i = start; i < _turns.Count; i++)
        {
            var t = _turns[i];
            if (t == null) continue;
            if (!floorCount.ContainsKey(t.floorIndex)) floorCount[t.floorIndex] = 0;
            floorCount[t.floorIndex]++;
        }
        int best = -1, bestC = -1;
        foreach (var kv in floorCount)
        {
            if (kv.Value > bestC) { bestC = kv.Value; best = kv.Key; }
        }
        mainFloor = best;

        // 提取最近一条玩家句子 / NPC句子作为“最新状态”
        string lastPlayer = FindLastText(Speaker.Player);
        string lastNpc = FindLastText(Speaker.NPC);

        // 估计氛围
        var mood = EstimateMood(start);

        // 抽取玩家“主题/意图”（很粗，但够用）
        string playerTopic = ExtractTopicHint(lastPlayer);

        var sb = new StringBuilder(512);

        if (mainFloor >= 0)
            sb.AppendLine($"我们最近主要在第 {mainFloor} 层附近交流。");

        if (!string.IsNullOrWhiteSpace(playerTopic))
            sb.AppendLine($"玩家这段时间主要在谈：{playerTopic}。");

        if (!string.IsNullOrWhiteSpace(mood))
            sb.AppendLine($"整体氛围：{mood}。");

        if (!string.IsNullOrWhiteSpace(lastPlayer))
            sb.AppendLine($"玩家刚才最后一句大意是：「{SoftQuote(lastPlayer)}」");

        if (!string.IsNullOrWhiteSpace(lastNpc))
            sb.AppendLine($"NPC最近的回应口吻大概是：「{SoftQuote(lastNpc)}」");

        // 防止摘要太空/太机械
        string result = sb.ToString().Trim();
        if (string.IsNullOrWhiteSpace(result))
            return "我们刚进行了一段对话。";

        return result;
    }

    private string FindLastText(Speaker who)
    {
        for (int i = _turns.Count - 1; i >= 0; i--)
        {
            var t = _turns[i];
            if (t == null) continue;
            if (t.speaker != who) continue;
            if (string.IsNullOrWhiteSpace(t.text)) continue;
            return Sanitize(t.text);
        }
        return "";
    }

    /// <summary>
    /// 用极轻量的规则估计最近气氛（不“算命”，只是做个味道标签）
    /// </summary>
    private string EstimateMood(int windowStartIndex)
    {
        int exclaim = 0, question = 0, neg = 0, pos = 0, pressure = 0;

        for (int i = windowStartIndex; i < _turns.Count; i++)
        {
            var t = _turns[i];
            if (t == null || string.IsNullOrWhiteSpace(t.text)) continue;
            string s = t.text;

            if (s.Contains("!")) exclaim++;
            if (s.Contains("！")) exclaim++;
            if (s.Contains("?")) question++;
            if (s.Contains("？")) question++;

            // 简单关键词：偏情绪/偏压力
            if (ContainsAny(s, "不行", "不对", "烦", "卡", "崩", "错", "失败", "糟", "痛", "难受")) neg++;
            if (ContainsAny(s, "可以", "行", "好", "正常", "解决", "成功", "舒服", "不错")) pos++;
            if (ContainsAny(s, "必须", "立刻", "马上", "急", "来不及", "赶", "快")) pressure++;
        }

        // 粗规则：优先压力/负面，其次疑问，再次兴奋
        if (pressure >= 2 && neg >= 2) return "偏焦急、带点不耐烦（在赶进度/排错）";
        if (pressure >= 2) return "偏赶进度、节奏较紧";
        if (neg >= 3) return "偏沮丧/不满意（问题反复出现）";
        if (question >= 4) return "偏探询、不断追问细节";
        if (exclaim >= 3 && pos >= 2) return "偏兴奋/推进顺利";
        if (pos > neg) return "总体偏积极、在推进";
        if (neg > pos) return "总体偏消极、在排错";
        return "中性偏务实";
    }

    private static string ExtractTopicHint(string lastPlayer)
    {
        if (string.IsNullOrWhiteSpace(lastPlayer)) return "";

        // 你是做 roguelike + LLM NPC 的，这里给一些非常贴近你项目的“topic hint”
        if (ContainsAny(lastPlayer, "命中率", "子弹", "伤害", "受伤", "统计", "上一层", "表现"))
            return "上一层战斗表现统计（承伤/命中/击杀等）";
        if (ContainsAny(lastPlayer, "JSON", "解析", "结构化", "提示词", "合规"))
            return "LLM结构化输出与稳定性（JSON合规/兜底）";
        if (ContainsAny(lastPlayer, "人格", "伊蕾娜", "白", "由乃", "说话", "灵性"))
            return "NPC人格与对话风格（更连续/更有趣）";
        if (ContainsAny(lastPlayer, "Unity", "报错", "NullReference", "卡", "性能", "UI"))
            return "Unity实现细节（UI/报错/性能优化）";
        if (ContainsAny(lastPlayer, "事件", "下一层", "buff", "debuff", "好感"))
            return "事件系统与好感度联动（下层事件/即时事件）";

        // 没命中就给一个“抽象但不尬”的概括
        // 取前 20 字作为主题影子
        string s = lastPlayer.Trim();
        if (s.Length > 20) s = s.Substring(0, 20) + "…";
        return s;
    }

    private static string Sanitize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\r", "").Trim();
        while (s.Contains("\n\n")) s = s.Replace("\n\n", "\n");
        return s;
    }

    private static bool ContainsAny(string s, params string[] keys)
    {
        if (string.IsNullOrEmpty(s)) return false;
        for (int i = 0; i < keys.Length; i++)
        {
            if (!string.IsNullOrEmpty(keys[i]) && s.Contains(keys[i]))
                return true;
        }
        return false;
    }

    private static string SoftQuote(string s)
    {
        s = Sanitize(s);
        // 过长就截断，避免摘要爆炸
        if (s.Length > 40) s = s.Substring(0, 40) + "…";
        // 避免引号里出现换行
        s = s.Replace("\n", " ");
        return s;
    }

    private static string HardTrim(string s, int maxChars)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (maxChars <= 0) return "";
        s = s.Trim();
        if (s.Length <= maxChars) return s;
        return s.Substring(0, maxChars).Trim() + "…";
    }

    private static string KeepTail(string s, int maxChars)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Trim();
        if (s.Length <= maxChars) return s;

        int cutStart = s.Length - maxChars;
        if (cutStart < 0) cutStart = 0;
        string trimmed = s.Substring(cutStart).Trim();

        // 尝试对齐到下一行，提升可读性
        int firstNewline = trimmed.IndexOf('\n');
        if (firstNewline > 0 && firstNewline < 40)
            trimmed = trimmed.Substring(firstNewline + 1).Trim();

        return "（仅保留最近对话片段）\n" + trimmed;
    }

    // Debug
    public int Count => _turns.Count;
}