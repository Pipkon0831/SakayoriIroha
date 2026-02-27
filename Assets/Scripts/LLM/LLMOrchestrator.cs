using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// LLM 业务编排层：
/// - 玩家发送消息时：请求“决策JSON”
/// - JSON失败/超时/空content：不重试LLM，直接本地fallback（事件+模糊回复）
/// </summary>
public class LLMOrchestrator : MonoBehaviour
{
    public static LLMOrchestrator Instance { get; private set; }

    [Header("Model")]
    [SerializeField] private string modelName = "deepseek-chat";

    [Header("Timeout (seconds)")]
    [SerializeField] private int decisionTimeoutSeconds = 8;
    [SerializeField] private int openingTimeoutSeconds = 6;

    [Header("Tokens")]
    [SerializeField] private int decisionMaxTokens = 900;
    [SerializeField] private int openingMaxTokens = 160;

    [Header("Temperature")]
    [SerializeField] private float decisionTemperature = 0.7f;
    [SerializeField] private float openingTemperature = 0.9f;

    [Header("Local fallback (Orchestrator)")]
    [TextArea(1, 3)]
    [SerializeField] private string orchestratorOpeningFallbackLine = "……说清楚你的打算。";

    private ILLMClient _client;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _client = new DeepSeekLLMProvider(modelName, ApiKeyProvider.Get);
    }

    // =========================
    // Decision
    // =========================
    public async Task<DecisionResult> RequestDecisionAsync(
        string playerText,
        int affinity,
        string historySummary,
        NPCProfile npc,
        CancellationToken ct)
    {
        try
        {
            var sys = BuildDecisionSystemPrompt(npc);
            var usr = BuildDecisionUserPrompt(playerText, affinity, historySummary);

            string rawOuter = await _client.RequestJsonAsync(
                sys, usr,
                decisionMaxTokens, decisionTemperature,
                decisionTimeoutSeconds, ct);

            string content = ExtractInnerContentFromOuter(rawOuter);
            if (string.IsNullOrWhiteSpace(content))
                throw new Exception("LLM returned empty content.");

            var inner = JsonUtility.FromJson<DecisionJson>(content);
            if (inner == null)
                throw new Exception("Decision JSON parse failed.");

            var normalized = NormalizeDecision(inner);
            normalized.isFallback = false;
            return normalized;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LLM] Decision failed -> fallback. reason={ex.Message}");
            return BuildFallbackDecision(affinity);
        }
    }

    // =========================
    // Opening (prefetch + direct request)
    // =========================
    public async Task PrefetchOpeningLineAsync(
        int affinity,
        string historySummary,
        NPCProfile npc,
        CancellationToken ct)
    {
        string npcName = GetNpcNameSafe(npc);

        if (NPCDialogueCache.Instance != null && NPCDialogueCache.Instance.HasOpeningFor(npcName))
            return;

        try
        {
            string line = await RequestOpeningLineAsync(affinity, historySummary, npc, ct);
            if (string.IsNullOrWhiteSpace(line))
                line = PickOpeningFallbackLine();

            NPCDialogueCache.Instance?.SetOpening(npcName, line.Trim());
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LLM] Prefetch opening failed -> local opening. reason={ex.Message}");
            NPCDialogueCache.Instance?.SetOpening(npcName, PickOpeningFallbackLine());
        }
    }

    public async Task<string> RequestOpeningLineAsync(
        int affinity,
        string historySummary,
        NPCProfile npc,
        CancellationToken ct)
    {
        try
        {
            var sys = BuildOpeningSystemPrompt(npc);
            var usr = BuildOpeningUserPrompt(npc, affinity, historySummary);

            string rawOuter = await _client.RequestJsonAsync(
                sys, usr,
                openingMaxTokens, openingTemperature,
                openingTimeoutSeconds, ct);

            string content = ExtractInnerContentFromOuter(rawOuter);
            if (string.IsNullOrWhiteSpace(content))
                throw new Exception("LLM returned empty content (opening).");

            var inner = JsonUtility.FromJson<OpeningJson>(content);
            string line = inner?.npc_opening_line;

            if (string.IsNullOrWhiteSpace(line))
                throw new Exception("Opening JSON missing npc_opening_line.");

            return line.Trim();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LLM] Opening failed -> fallback. reason={ex.Message}");
            return PickOpeningFallbackLine();
        }
    }

    // =========================
    // Prompts
    // =========================
    private string BuildDecisionSystemPrompt(NPCProfile npc)
    {
        string npcName = npc != null ? npc.npcName : "NPC";
        string persona = npc != null ? npc.persona : "冷静、克制、说话简短。";
        string background = npc != null ? npc.background : "你是地牢中的引导者。";
        string style = npc != null ? npc.speakingStyle : "中文；像真人；不使用表情；不要长段落。";

        var p = (NPCRunPersonalityManager.Instance != null) ? NPCRunPersonalityManager.Instance.Selected : null;
        string pBlock = p != null
            ? $@"【本局人格】
- 人格名：{p.displayName}
- 总体约束：{p.systemPromptAddon}
- 决策风格：{p.decisionAddon}
"
            : "";

        return
$@"你是游戏NPC【{npcName}】的“决策与对话引擎”。你必须使用中文输出，并且只能输出一个合法的 JSON 对象（json），禁止输出任何 JSON 之外的文字、markdown、解释、注释。

{pBlock}

【角色设定】
- 名字：{npcName}
- 性格：{persona}
- 背景：{background}
- 说话风格：{style}

【输出必须严格为 json_object，字段固定如下】
{{
  ""npc_reply"": ""string（中文，<=160字，2~4句，像真人交流，可含少量口语停顿）"",
  ""affinity_delta"": int（-5~5）,
  ""next_floor_events"": [ {{ ""eventType"": ""EnumName"", ""value"": float }} ],
  ""instant_events"": [ {{ ""eventType"": ""EnumName"", ""value"": float }} ],
  ""history_event_summary_delta"": ""string（可为空，中文短句）""
}}

【合法 eventType 白名单（必须完全匹配枚举名；禁止使用 None）】
1) 单层事件（只能出现在 next_floor_events）
- LowVision
- EnemyMoveSpeedUp
- PlayerDealMoreDamage
- PlayerReceiveMoreDamage
- AllRoomsMonsterExceptBossAndSpawn
- AllRoomsRewardExceptBossAndSpawn
- PlayerAttackSpeedUp
- PlayerAttackSpeedDown

2) 即时永久事件（只能出现在 instant_events）
- GainExp
- Heal
- LoseHP
- PlayerMaxHPUp
- PlayerMaxHPDown
- PlayerAttackUp
- PlayerAttackDown

【事件输出规则（强制）】
- 默认必须输出事件：
  - next_floor_events 至少 1 条（1~3条最佳）
  - instant_events 0~2条（可选）
- 只有当玩家明确提出“不要事件/不要buff/debuff/只想纯聊天/不想影响游戏”，并且你的 npc_reply 明确同意顺从时，才允许 next_floor_events 输出 []。
- 若玩家没有提出该请求，禁止输出 next_floor_events = []。
- 若你不确定给什么事件：优先给温和事件（例如 PlayerDealMoreDamage 0.15~0.25 或 GainExp 20~40 或 Heal 8~15）。

【数量限制】
- next_floor_events：0~4 条（通常 1~3）
- instant_events：0~3 条（通常 0~2）
- 尽量避免同一 eventType 重复；不确定时按“温和事件兜底”。

【value 取值规则（务必遵守）】
- LowVision：0.35 ~ 1.0（倍率）
- EnemyMoveSpeedUp / PlayerDealMoreDamage / PlayerReceiveMoreDamage / PlayerAttackSpeedUp：0.0 ~ 3.0（比例；0.2 表示 +20%）
- PlayerAttackSpeedDown：0.0 ~ 0.9（比例；0.2 表示 -20%）
- Heal / LoseHP：1 ~ 50
- GainExp：10 ~ 100
- PlayerMaxHPUp / PlayerMaxHPDown：1 ~ 20
- PlayerAttackUp / PlayerAttackDown：1 ~ 5

【对话行为准则（强制）】
- 你不是“提问机”。优先用“回应→评价→延伸”的节奏聊天。
- 必须偶尔承接玩家上一句的关键词或情绪（至少每次都要做到一点）。
- 不要每次都用相似句式开头（例如总是“行/可以/随你/继续”）。
- 可以自然地表现：调侃、怀疑、欣赏、嫌弃、担心、占有欲等（受人格影响），但不要长篇说教。

【硬性禁止】
- 禁止输出 None
- 禁止输出白名单之外的 eventType
- 禁止把即时事件放进 next_floor_events
- 禁止把单层事件放进 instant_events
- 禁止输出多余字段
- 禁止输出 JSON 之外的任何字符";
    }

    private string BuildDecisionUserPrompt(string playerText, int affinity, string historySummary)
    {
        return
            $@"（json）请基于“连续对话”生成决策 JSON。

【当前关系】
- 好感度 affinity：{affinity}

【对话记忆 dialogue_memory（最近片段，可能被截断）】
{(string.IsNullOrWhiteSpace(historySummary) ? "（无）" : historySummary.Trim())}

【玩家本次发言】
{(string.IsNullOrWhiteSpace(playerText) ? "（空）" : playerText.Trim())}

【回复写作规则（很重要）】
1) npc_reply 必须像真人交流：先回应玩家话题/情绪，再补充你的态度/评价，最后可自然延伸（不强制提问）。
2) 必须“呼应/引用”对话记忆里最近的一个点（情绪、用词、承诺、玩家偏好、上一句的某个关键词），让玩家感觉你记得。
3) 避免NPC像面试官连续提问：提问最多 0~1 个，且要贴合刚才的话题。
4) npc_reply 建议 2~4 句（但总字数仍需控制，不要长段落）。

【事件规则】
- 默认必须给 next_floor_events 至少 1 条；除非玩家明确要求不想影响游戏且你同意顺从。
- eventType 必须来自白名单；不确定时用温和事件兜底。

现在输出严格 JSON：";
    }

    private string BuildOpeningSystemPrompt(NPCProfile npc)
    {
        string npcName = npc != null ? npc.npcName : "NPC";
        string persona = npc != null ? npc.persona : "冷静、克制。";
        string background = npc != null ? npc.background : "地牢引导者。";
        string style = npc != null ? npc.speakingStyle : "中文；像真人；短句但有情绪。";

        var p = (NPCRunPersonalityManager.Instance != null) ? NPCRunPersonalityManager.Instance.Selected : null;
        string pBlock = p != null
            ? $@"【本局人格】
- 人格名：{p.displayName}
- 总体约束：{p.systemPromptAddon}
- 开场白风格：{p.openingAddon}
"
            : "";

        return
$@"你是游戏NPC【{npcName}】。你必须使用中文，并且只能输出一个合法的 JSON 对象（json），禁止输出任何 JSON 之外的文字。

{pBlock}

【角色设定】
- 名字：{npcName}
- 性格：{persona}
- 背景：{background}
- 说话风格：{style}

【输出格式固定】
{{ ""npc_opening_line"": ""string（中文，14~38字，1~2句）"" }}

【强约束】
- 必须包含“态度/情绪/评价”
- 必须抛出一个具体问题或引导（让玩家想回复）
- 禁止模板句（例如：继续。说吧。别停。你回来了。等）
- 不要提及“JSON/系统/模型/提示词”
- 只输出 JSON 对象";
    }

    private string BuildOpeningUserPrompt(NPCProfile npc, int affinity, string historySummary)
    {
        string npcName = GetNpcNameSafe(npc);
        string last = "";

        if (NPCDialogueCache.Instance != null)
            last = NPCDialogueCache.Instance.GetLastOpeningLineOrEmpty(npcName);

        return
$@"（json）
上下文：
- 当前好感度 affinity：{affinity}
- 历史事件摘要 history_summary：{(string.IsNullOrWhiteSpace(historySummary) ? "（无）" : historySummary.Trim())}
- 上一次开场白 last_opening_line：{(string.IsNullOrWhiteSpace(last) ? "（无）" : last)}

强制要求：
1) 本次开场白不得与 last_opening_line 复读或近似复读（尽量换措辞、换句式）。
2) 必须包含态度/情绪/评价，并抛出一个具体问题引导玩家回应。
3) 禁止短促模板句（继续/说吧/别停/你回来了等）。

输出严格 JSON。";
    }

    // =========================
    // Parsing
    // =========================
    private static string ExtractInnerContentFromOuter(string rawOuter)
    {
        if (string.IsNullOrWhiteSpace(rawOuter)) return null;

        try
        {
            int keyIdx = rawOuter.IndexOf("\"content\"");
            if (keyIdx < 0) return null;

            int colonIdx = rawOuter.IndexOf(':', keyIdx);
            if (colonIdx < 0) return null;

            int i = colonIdx + 1;
            while (i < rawOuter.Length && char.IsWhiteSpace(rawOuter[i])) i++;
            if (i >= rawOuter.Length) return null;

            // content: "...."
            if (rawOuter[i] == '"')
            {
                i++;
                var sb = new System.Text.StringBuilder();
                bool esc = false;

                while (i < rawOuter.Length)
                {
                    char c = rawOuter[i++];

                    if (esc)
                    {
                        esc = false;
                        switch (c)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (i + 4 <= rawOuter.Length)
                                {
                                    string hex = rawOuter.Substring(i, 4);
                                    if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int code))
                                        sb.Append((char)code);
                                    i += 4;
                                }
                                break;
                            default:
                                sb.Append(c);
                                break;
                        }
                        continue;
                    }

                    if (c == '\\') { esc = true; continue; }
                    if (c == '"') return sb.ToString();
                    sb.Append(c);
                }

                return null;
            }

            // content: { ... }
            if (rawOuter[i] == '{')
            {
                int start = i;
                int depth = 0;
                bool inString = false;
                bool esc = false;

                while (i < rawOuter.Length)
                {
                    char c = rawOuter[i++];

                    if (inString)
                    {
                        if (esc) { esc = false; continue; }
                        if (c == '\\') { esc = true; continue; }
                        if (c == '"') { inString = false; continue; }
                        continue;
                    }

                    if (c == '"') { inString = true; continue; }
                    if (c == '{') depth++;
                    if (c == '}')
                    {
                        depth--;
                        if (depth == 0)
                            return rawOuter.Substring(start, i - start);
                    }
                }
                return null;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    // =========================
    // Normalize + Validation
    // =========================
    private DecisionResult NormalizeDecision(DecisionJson j)
    {
        var r = new DecisionResult
        {
            npcReply = string.IsNullOrWhiteSpace(j.npc_reply) ? "……" : j.npc_reply.Trim(),
            affinityDelta = Mathf.Clamp(j.affinity_delta, -5, 5),
            nextFloor = new List<LayerEvent>(),
            instants = new List<LayerEvent>(),
            historyDelta = j.history_event_summary_delta ?? "",
        };

        if (j.next_floor_events != null)
        {
            foreach (var e in j.next_floor_events)
                if (TryMakeLayerEvent(e, out var le, isNextFloor: true))
                    r.nextFloor.Add(le);
        }

        if (j.instant_events != null)
        {
            foreach (var e in j.instant_events)
                if (TryMakeLayerEvent(e, out var le, isNextFloor: false))
                    r.instants.Add(le);
        }

        if (r.nextFloor.Count > 4) r.nextFloor.RemoveRange(4, r.nextFloor.Count - 4);
        if (r.instants.Count > 3) r.instants.RemoveRange(3, r.instants.Count - 3);

        if (r.npcReply.Length > 120) r.npcReply = r.npcReply.Substring(0, 120);

        // 若模型违约（nextFloor为空），给一个温和兜底事件，保证循环
        if (r.nextFloor.Count == 0)
            r.nextFloor.Add(new LayerEvent(LayerEventType.PlayerDealMoreDamage, 0.2f));

        return r;
    }

    private bool TryMakeLayerEvent(EventJson e, out LayerEvent layerEvent, bool isNextFloor)
    {
        layerEvent = null;
        if (e == null || string.IsNullOrWhiteSpace(e.eventType)) return false;

        string name = e.eventType.Trim();
        if (name == "None") return false;

        if (!Enum.TryParse(name, out LayerEventType type))
            return false;

        float v = e.value;

        bool isInstantType =
            type == LayerEventType.GainExp ||
            type == LayerEventType.Heal ||
            type == LayerEventType.LoseHP ||
            type == LayerEventType.PlayerMaxHPUp ||
            type == LayerEventType.PlayerMaxHPDown ||
            type == LayerEventType.PlayerAttackUp ||
            type == LayerEventType.PlayerAttackDown;

        if (isNextFloor && isInstantType) return false;
        if (!isNextFloor && !isInstantType) return false;

        switch (type)
        {
            case LayerEventType.LowVision:
                v = Mathf.Clamp(v, 0.35f, 1f);
                break;

            case LayerEventType.EnemyMoveSpeedUp:
            case LayerEventType.PlayerDealMoreDamage:
            case LayerEventType.PlayerReceiveMoreDamage:
            case LayerEventType.PlayerAttackSpeedUp:
                v = Mathf.Clamp(v, 0f, 3f);
                break;

            case LayerEventType.PlayerAttackSpeedDown:
                v = Mathf.Clamp(v, 0f, 0.9f);
                break;

            case LayerEventType.Heal:
            case LayerEventType.LoseHP:
                v = Mathf.Clamp(v, 1f, 50f);
                break;

            case LayerEventType.GainExp:
                v = Mathf.Clamp(v, 10f, 100f);
                break;

            case LayerEventType.PlayerMaxHPUp:
            case LayerEventType.PlayerMaxHPDown:
                v = Mathf.Clamp(v, 1f, 20f);
                break;

            case LayerEventType.PlayerAttackUp:
            case LayerEventType.PlayerAttackDown:
                v = Mathf.Clamp(v, 1f, 5f);
                break;

            case LayerEventType.AllRoomsMonsterExceptBossAndSpawn:
            case LayerEventType.AllRoomsRewardExceptBossAndSpawn:
                v = 0f;
                break;
        }

        layerEvent = new LayerEvent(type, v);
        return true;
    }

    // =========================
    // Fallback builders
    // =========================
    private DecisionResult BuildFallbackDecision(int affinity)
    {
        bool positive = affinity >= 0;

        var res = new DecisionResult
        {
            isFallback = true,
            npcReply = PickDecisionFallbackReply(positive),
            affinityDelta = positive ? 1 : -1,
            nextFloor = new List<LayerEvent>(),
            instants = new List<LayerEvent>(),
            historyDelta = ""
        };

        // 保证至少有 nextFloor 事件（维持循环）
        res.nextFloor.Add(new LayerEvent(LayerEventType.PlayerDealMoreDamage, 0.2f));

        // 可选即时事件（温和一点）
        res.instants.Add(new LayerEvent(
            positive ? LayerEventType.Heal : LayerEventType.LoseHP,
            positive ? 12f : 10f));

        return res;
    }

    private string PickDecisionFallbackReply(bool positive)
    {
        // 1) 人格兜底（优先）
        var p = (NPCRunPersonalityManager.Instance != null) ? NPCRunPersonalityManager.Instance.Selected : null;
        if (p != null)
        {
            string s = p.GetRandomDecisionFallbackOrEmpty();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }

        // 2) 默认兜底
        string[] pos = { "行，我记下了。下一层我会照看你一点。", "可以。别拖沓，往下走。" };
        string[] neg = { "……随你。下一层你自己扛住。", "我听到了。别后悔。" };
        var pool = positive ? pos : neg;
        return pool[UnityEngine.Random.Range(0, pool.Length)];
    }

    private string PickOpeningFallbackLine()
    {
        // 1) 人格兜底（优先）
        var p = (NPCRunPersonalityManager.Instance != null) ? NPCRunPersonalityManager.Instance.Selected : null;
        if (p != null)
        {
            string s = p.GetRandomOpeningFallbackOrEmpty();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }

        // 2) Orchestrator本地兜底（注意：不要引用 UI 的 localOpeningFallbackLine）
        if (!string.IsNullOrWhiteSpace(orchestratorOpeningFallbackLine))
            return orchestratorOpeningFallbackLine.Trim();

        // 3) 最终兜底
        return "……";
    }

    private static string GetNpcNameSafe(NPCProfile npc)
    {
        return (npc != null && !string.IsNullOrWhiteSpace(npc.npcName))
            ? npc.npcName.Trim()
            : "NPC";
    }

    // =========================
    // JSON shapes
    // =========================
    [Serializable]
    private class DecisionJson
    {
        public string npc_reply;
        public int affinity_delta;
        public EventJson[] next_floor_events;
        public EventJson[] instant_events;
        public string history_event_summary_delta;
    }

    [Serializable]
    private class OpeningJson
    {
        public string npc_opening_line;
    }

    [Serializable]
    private class EventJson
    {
        public string eventType;
        public float value;
    }

    public class DecisionResult
    {
        public bool isFallback;
        public string npcReply;
        public int affinityDelta;
        public List<LayerEvent> nextFloor;
        public List<LayerEvent> instants;
        public string historyDelta;
    }
}