using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// LLM 业务编排层：
/// - 玩家发送消息时：请求“决策JSON”（允许等待一次）
/// - 进入新层时：后台预取“下一次NPC首句”（不阻塞玩家）
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

    private ILLMClient _client;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _client = new DeepSeekLLMProvider(modelName, ApiKeyProvider.Get);
    }

    // =========================================================
    // 玩家发送：请求决策 JSON（等待一次）
    // =========================================================
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

    // =========================================================
    // 预取：下一次对话阶段开场白（不阻塞）
    // =========================================================
    public async Task PrefetchOpeningLineAsync(
        int affinity,
        string historySummary,
        NPCProfile npc,
        CancellationToken ct)
    {
        string npcName = (npc != null && !string.IsNullOrWhiteSpace(npc.npcName))
            ? npc.npcName.Trim()
            : "NPC";

        // ✅ 只针对“这个 NPC”做短路，而不是全局短路
        if (NPCDialogueCache.Instance != null && NPCDialogueCache.Instance.HasOpeningFor(npcName))
            return;

        try
        {
            var sys = BuildOpeningSystemPrompt(npc);
            var usr = BuildOpeningUserPrompt(affinity, historySummary);

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

            NPCDialogueCache.Instance?.SetOpening(npcName, line.Trim());
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LLM] Prefetch opening failed -> local opening. reason={ex.Message}");
            NPCDialogueCache.Instance?.SetOpening(npcName, PickFallbackOpeningLine());
        }
    }

    // =========================================================
    // Prompt: Decision
    // =========================================================
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

        // ✅ 重点：默认必须给事件（除非玩家明确要求且NPC同意）
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
  ""npc_reply"": ""string（中文，<=80字，1~2句）"",
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
$@"（json）现在请根据上下文生成决策 JSON。

【上下文】
- 当前好感度 affinity：{affinity}
- 历史事件摘要 history_summary：{(string.IsNullOrWhiteSpace(historySummary) ? "（无）" : historySummary.Trim())}

【玩家本次发言】
{(string.IsNullOrWhiteSpace(playerText) ? "（空）" : playerText.Trim())}

【要求】
1) npc_reply 必须像真人、带态度、<=80字、1~2句。
2) 默认必须给 next_floor_events 至少 1 条；除非玩家明确要求且你同意顺从。
3) eventType 必须来自白名单；不确定时用“温和事件兜底”。

现在输出严格 JSON：";
    }

    // =========================================================
    // Prompt: Opening（每层开场白，预取用）
    // =========================================================
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

    private string BuildOpeningUserPrompt(int affinity, string historySummary)
    {
        string npcName = "NPC";
        // 注意：这里没 npc 形参时，就用历史 summary/默认，或者你也可以改函数签名传 npcName 进来
        // 为了最小改动：我们只取“缓存里任意一个 last”（单NPC也够用）
        string last = "";

        if (NPCDialogueCache.Instance != null)
        {
            // 单NPC项目：直接读 NPC 这个桶就行；如果你有明确 npcName，可以换成 GetLastOpeningLineOrEmpty(npcName)
            last = NPCDialogueCache.Instance.GetLastOpeningLineOrEmpty(npcName);
        }

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

    // =========================================================
    // Outer -> Inner extraction (robust)
    // =========================================================
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
                        {
                            return rawOuter.Substring(start, i - start);
                        }
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

    // =========================================================
    // Normalize / Validation
    // =========================================================
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

        // 数量硬裁剪
        if (r.nextFloor.Count > 4) r.nextFloor.RemoveRange(4, r.nextFloor.Count - 4);
        if (r.instants.Count > 3) r.instants.RemoveRange(3, r.instants.Count - 3);

        if (r.npcReply.Length > 120) r.npcReply = r.npcReply.Substring(0, 120);

        // ✅ 本地兜底：万一模型仍给空 nextFloor（违约），给一个温和事件
        if (r.nextFloor.Count == 0)
        {
            r.nextFloor.Add(new LayerEvent(LayerEventType.PlayerDealMoreDamage, 0.2f));
        }

        return r;
    }

    private bool TryMakeLayerEvent(EventJson e, out LayerEvent layerEvent, bool isNextFloor)
    {
        layerEvent = null;
        if (e == null || string.IsNullOrWhiteSpace(e.eventType)) return false;

        string name = e.eventType.Trim();
        if (name == "None") return false;

        if (!Enum.TryParse<LayerEventType>(name, out var type))
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

    // =========================================================
    // Fallback（保留原逻辑）
    // =========================================================
    private DecisionResult BuildFallbackDecision(int affinity)
    {
        bool positive = affinity >= 0;

        var res = new DecisionResult
        {
            isFallback = true,
            npcReply = PickFallbackNpcReply(positive),
            affinityDelta = positive ? 1 : -1,
            nextFloor = new List<LayerEvent>(),
            instants = new List<LayerEvent>(),
            historyDelta = ""
        };

        // fallback 仍保证至少一个 nextFloor
        res.nextFloor.Add(new LayerEvent(LayerEventType.PlayerDealMoreDamage, 0.2f));

        // 再随机补一点
        res.instants.Add(new LayerEvent(positive ? LayerEventType.Heal : LayerEventType.LoseHP, positive ? 12f : 10f));
        return res;
    }

    private static string PickFallbackNpcReply(bool positive)
    {
        string[] pos = { "行，我记下了。下一层我会照看你一点。", "可以。别拖沓，往下走。" };
        string[] neg = { "……随你。下一层你自己扛住。", "我听到了。别后悔。" };
        var pool = positive ? pos : neg;
        return pool[UnityEngine.Random.Range(0, pool.Length)];
    }

    private static string PickFallbackOpeningLine()
    {
        string[] pool = { "你这次想赌什么？说清楚。", "先别急，告诉我你的打算。", "你在犹豫？那就选一个方向。" };
        return pool[UnityEngine.Random.Range(0, pool.Length)];
    }

    // =========================================================
    // Types
    // =========================================================
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

    // =========================================================
    // Public result
    // =========================================================
    public class DecisionResult
    {
        public bool isFallback;
        public string npcReply;
        public int affinityDelta;
        public List<LayerEvent> nextFloor;
        public List<LayerEvent> instants;
        public string historyDelta;
    }
    
    // =========================================================
// 开场白：每次 UI 打开时请求（等待一次）
// =========================================================
    public async Task<string> RequestOpeningLineAsync(
        int affinity,
        string historySummary,
        NPCProfile npc,
        CancellationToken ct)
    {
        try
        {
            var sys = BuildOpeningSystemPrompt(npc);
            var usr = BuildOpeningUserPrompt(affinity, historySummary);

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
            Debug.LogWarning($"[LLM] Opening failed -> local opening. reason={ex.Message}");
            return PickFallbackOpeningLine();
        }
    }
}