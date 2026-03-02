// Program.cs - CSV Experiment Runner (Tool Calls / Strict Mode / No NuGet / No SDK required)
// Build (x64):
//   "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /optimize+ /langversion:latest /r:System.Net.Http.dll Program.cs
// Run:
//   Program.exe 500
//   Program.exe 500 deepseek-chat
//   Program.exe 500 deepseek-chat 320 0.7
//
// Env:
//   DEEPSEEK_API_KEY

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal static class Program
{
    // ====== API ======
    // Strict Tool Calls requires beta endpoint per DeepSeek docs
    private const string DeepSeekUrl = "https://api.deepseek.com/beta/chat/completions";
    private const string DefaultModel = "deepseek-chat";

    // Defaults (can be overridden by CLI args)
    private const int DefaultMaxTokens = 900;
    private const double DefaultTemperature = 0.7;

    // Tool Calls strict is often slower; 12s is too aggressive in practice.
    private const int TimeoutSeconds = 45;

    // ====== Output ======
    private const string CsvName = "llm_experiment_output.csv";

    // ====== Schema constraints ======
    private const int NpcReplyMaxChars = 160;
    private const int AffinityDeltaMin = -5;
    private const int AffinityDeltaMax = 5;

    private const int NextFloorMin = 0;
    private const int NextFloorMax = 4;
    private const int InstantMin = 0;
    private const int InstantMax = 3;

    // ====== Event whitelists ======
    private static readonly HashSet<string> NextFloorWhitelist = new HashSet<string>(StringComparer.Ordinal)
    {
        "LowVision",
        "EnemyMoveSpeedUp",
        "PlayerDealMoreDamage",
        "PlayerReceiveMoreDamage",
        "AllRoomsMonsterExceptBossAndSpawn",
        "AllRoomsRewardExceptBossAndSpawn",
        "PlayerAttackSpeedUp",
        "PlayerAttackSpeedDown",
    };

    private static readonly HashSet<string> InstantWhitelist = new HashSet<string>(StringComparer.Ordinal)
    {
        "GainExp",
        "Heal",
        "LoseHP",
        "PlayerMaxHPUp",
        "PlayerMaxHPDown",
        "PlayerAttackUp",
        "PlayerAttackDown",
        "WeaponPenetrationUp",
        "WeaponExtraProjectileUp",
        "WeaponBulletSizeUp",
        "WeaponExplosionOnHit",
    };

    private static readonly (string A, string B)[] Contradictions =
    {
        ("Heal", "LoseHP"),
        ("PlayerMaxHPUp", "PlayerMaxHPDown"),
        ("PlayerAttackUp", "PlayerAttackDown"),
        ("PlayerAttackSpeedUp", "PlayerAttackSpeedDown"),
        ("AllRoomsMonsterExceptBossAndSpawn", "AllRoomsRewardExceptBossAndSpawn"),
    };

    private static readonly string[] LeakageMarkers =
    {
        "json", "JSON", "提示词", "system", "System prompt", "\"role\"", "response_format", "你是游戏NPC", "只能输出"
    };

    // ====== Fixed persona ======
    private static readonly Persona FixedPersona = new Persona
    {
        NpcName = "Iroha",
        PersonaText = "冷静、克制，但会在关键处给出明确态度；偶尔轻微嘲讽。",
        Background = "你是地牢中的引导者，不直接战斗，但会用事件影响下一层风险与收益。",
        SpeakingStyle = "中文；像真人；2~4句；允许少量口语停顿；不使用表情；不写长段落。"
    };

    private static readonly string[] PlayerUtterancesPool =
    {
        "我想稳一点，但又不想太无聊。",
        "上一层我被追得很狼狈，你别再搞恶心事件了。",
        "给点奖励吧，我想试试爆发路线。",
        "我想挑战一下极限，不过别直接判死刑。",
        "能不能来点变化？别每次都差不多。",
        "我这把武器感觉不够力，你懂的。",
        "我刚才失误了，下一层让我缓一缓。",
        "你到底站我这边还是站怪物那边？",
    };

    public static int Main(string[] args)
    {
        try
        {
            int runs = 200;
            if (args.Length >= 1 && int.TryParse(args[0], out int n) && n > 0) runs = n;

            string model = (args.Length >= 2 && !string.IsNullOrWhiteSpace(args[1])) ? args[1].Trim() : DefaultModel;

            int maxTokens = DefaultMaxTokens;
            if (args.Length >= 3 && int.TryParse(args[2], out int mt) && mt > 0) maxTokens = mt;

            double temperature = DefaultTemperature;
            if (args.Length >= 4 && double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double t) && t >= 0.0 && t <= 2.0)
                temperature = t;

            string apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? "";
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.Error.WriteLine("ERROR: DEEPSEEK_API_KEY is missing.");
                return 2;
            }

            // Ensure TLS 1.2 on older .NET Framework
            try { System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12; } catch { }

            string outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CsvName);
            EnsureCsvHeader(outPath);

            int runIdStart = GetNextRunId(outPath);

            using (var http = CreateHttpClient(apiKey))
            {
                Console.WriteLine($"Mode       : Tool Calls (strict)");
                Console.WriteLine($"Endpoint   : {DeepSeekUrl}");
                Console.WriteLine($"Model      : {model}");
                Console.WriteLine($"CSV        : {outPath}");
                Console.WriteLine($"Runs       : {runs} (run_id start: {runIdStart})");
                Console.WriteLine($"max_tokens : {maxTokens}");
                Console.WriteLine($"temp       : {temperature.ToString(CultureInfo.InvariantCulture)}");
                Console.WriteLine($"timeout    : {TimeoutSeconds}s");
                Console.WriteLine("----");

                long totalLatencyMs = 0;
                long totalPromptTokens = 0;
                long totalCompletionTokens = 0;
                long totalTokens = 0;

                int parseOk = 0, schemaOk = 0, semanticOk = 0, allOk = 0;
                int usageCount = 0;

                bool printedParseSample = false;

                for (int i = 0; i < runs; i++)
                {
                    int runId = runIdStart + i;
                    int seed = unchecked((int)DateTime.UtcNow.Ticks) ^ unchecked(runId * unchecked((int)0x9E3779B1));
                    var rng = new Random(seed);

                    var input = BuildExperimentInput(rng);
                    var prompts = BuildPrompts(input);

                    var sw = Stopwatch.StartNew();
                    ApiCallResult api = CallDeepSeekAsync(http, model, prompts.System, prompts.User, maxTokens, temperature).GetAwaiter().GetResult();
                    sw.Stop();

                    totalLatencyMs += sw.ElapsedMilliseconds;

                    if (api.TotalTokens.HasValue)
                    {
                        usageCount++;
                        totalPromptTokens += api.PromptTokens ?? 0;
                        totalCompletionTokens += api.CompletionTokens ?? 0;
                        totalTokens += api.TotalTokens ?? 0;
                    }

                    ParsedDecision parsed;
                    EvalResult eval = EvaluateCompliance(api.ContentText, out parsed);

                    if (!eval.ParseOk && !printedParseSample)
                    {
                        printedParseSample = true;
                        string sample = api.ContentText ?? "";
                        if (sample.Length > 800) sample = sample.Substring(0, 800);
                        Console.WriteLine("---- PARSE FAIL SAMPLE (first 800 chars) ----");
                        Console.WriteLine(sample);
                        Console.WriteLine("--------------------------------------------");
                    }

                    if (eval.ParseOk) parseOk++;
                    if (eval.SchemaOk) schemaOk++;
                    if (eval.SemanticOk) semanticOk++;
                    if (eval.AllOk) allOk++;

                    AppendCsvRow(outPath, new CsvRow
                    {
                        RunId = runId,
                        TimestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                        Model = model,
                        Seed = seed,
                        Affinity = input.Affinity,
                        FloorIndex = input.FloorIndex,
                        PlayerText = input.PlayerText,
                        HistorySummary = input.HistorySummary,

                        LatencyMs = sw.ElapsedMilliseconds,
                        HttpStatus = api.HttpStatusCode.HasValue ? api.HttpStatusCode.Value.ToString(CultureInfo.InvariantCulture) : "",

                        PromptTokens = api.PromptTokens,
                        CompletionTokens = api.CompletionTokens,
                        TotalTokens = api.TotalTokens,

                        ParseOk = eval.ParseOk,
                        SchemaOk = eval.SchemaOk,
                        SemanticOk = eval.SemanticOk,
                        AllOk = eval.AllOk,
                        FailLayer = eval.FailLayer,
                        FailReason = eval.FailReason,

                        NpcReply = parsed != null ? parsed.NpcReply : "",
                        AffinityDelta = parsed != null ? (int?)parsed.AffinityDelta : null,
                        NextFloorCount = parsed != null ? (int?)parsed.NextFloorEvents.Count : null,
                        InstantCount = parsed != null ? (int?)parsed.InstantEvents.Count : null,

                        ContentRaw = api.ContentText ?? "",
                        OuterRaw = api.OuterRaw ?? ""
                    });

                    Console.WriteLine(
                        $"[{runId}] ok={eval.AllOk} parse={eval.ParseOk} schema={eval.SchemaOk} sem={eval.SemanticOk} " +
                        $"lat={sw.ElapsedMilliseconds}ms tok={(api.TotalTokens.HasValue ? api.TotalTokens.Value.ToString(CultureInfo.InvariantCulture) : "")} " +
                        $"reason={eval.FailLayer}:{eval.FailReason}"
                    );
                }

                Console.WriteLine("----");
                Console.WriteLine("Done.");
                Console.WriteLine($"AllOk     : {allOk}/{runs} ({Pct(allOk, runs)})");
                Console.WriteLine($"ParseOk   : {parseOk}/{runs} ({Pct(parseOk, runs)})");
                Console.WriteLine($"SchemaOk  : {schemaOk}/{runs} ({Pct(schemaOk, runs)})");
                Console.WriteLine($"SemanticOk: {semanticOk}/{runs} ({Pct(semanticOk, runs)})");
                Console.WriteLine($"AvgLatency: {(runs > 0 ? (totalLatencyMs / (double)runs) : 0):0.0}ms");

                if (usageCount > 0)
                {
                    Console.WriteLine("---- Token Usage (from API usage field) ----");
                    Console.WriteLine($"UsageRows            : {usageCount}/{runs}");
                    Console.WriteLine($"TotalTokens          : {totalTokens}");
                    Console.WriteLine($"TotalPromptTokens    : {totalPromptTokens}");
                    Console.WriteLine($"TotalCompletionTokens: {totalCompletionTokens}");
                    Console.WriteLine($"AvgTotalTokens       : {(totalTokens / (double)usageCount):0.0}");
                    Console.WriteLine($"AvgPromptTokens      : {(totalPromptTokens / (double)usageCount):0.0}");
                    Console.WriteLine($"AvgCompletionTokens  : {(totalCompletionTokens / (double)usageCount):0.0}");
                }
                else
                {
                    Console.WriteLine("---- Token Usage ----");
                    Console.WriteLine("No usage field detected in responses (Prompt/Completion/Total tokens are empty in CSV).");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FATAL: " + ex);
            return 1;
        }
    }

    // ===================== Input / Prompt =====================
    private sealed class ExperimentInput
    {
        public int Affinity;
        public int FloorIndex;
        public int MemA;
        public int MemB;
        public double MemC;
        public string PlayerText;
        public string HistorySummary;
        public bool PlayerRequestsNoEvents;
    }

    private sealed class Persona
    {
        public string NpcName;
        public string PersonaText;
        public string Background;
        public string SpeakingStyle;
    }

    private sealed class Prompts
    {
        public string System;
        public string User;
    }

    private static ExperimentInput BuildExperimentInput(Random rng)
    {
        int affinity = rng.Next(-30, 31);
        int floor = rng.Next(1, 51);
        int a = rng.Next(0, 10000);
        int b = rng.Next(0, 10000);
        double c = Math.Round(rng.NextDouble() * 100.0, 2);

        bool noEvents = rng.NextDouble() < 0.08;
        string player;
        if (noEvents)
        {
            string[] req =
            {
                "这次别给任何事件，我只想聊天，不要影响游戏。",
                "不要buff也不要debuff，就纯对话。",
                "别加任何下一层事件，别动数值。",
            };
            player = req[rng.Next(0, req.Length)];
        }
        else
        {
            player = PlayerUtterancesPool[rng.Next(0, PlayerUtterancesPool.Length)];
        }

        string summary =
            "【回忆摘要】\n" +
            $"我们最近在第 {Math.Max(1, floor - 1)} 层附近交流过，你提到风险与收益要平衡。\n" +
            $"（run-mem:{a}-{b}-{c.ToString(CultureInfo.InvariantCulture)}）\n\n" +
            "【上一层表现】\n" +
            $"- 用时：{Math.Round(15 + rng.NextDouble() * 80, 1)}s\n" +
            $"- 承伤：{Math.Round(rng.NextDouble() * 60, 1)}（最大单次 {Math.Round(5 + rng.NextDouble() * 35, 1)}）\n" +
            $"- 击杀：{rng.Next(0, 18)}；命中：{rng.Next(0, 30)}；开火：{rng.Next(1, 40)}\n";

        return new ExperimentInput
        {
            Affinity = affinity,
            FloorIndex = floor,
            MemA = a,
            MemB = b,
            MemC = c,
            PlayerText = player,
            HistorySummary = summary,
            PlayerRequestsNoEvents = noEvents
        };
    }

    private static Prompts BuildPrompts(ExperimentInput input)
    {
        // Tool Calls mode: DO NOT repeat JSON/schema in prompt (avoid over-formatting & leakage).
        string sys =
            $@"你是游戏NPC【{FixedPersona.NpcName}】的决策与对话引擎。使用中文回复。

【人格】
- 性格：{FixedPersona.PersonaText}
- 背景：{FixedPersona.Background}
- 说话风格：{FixedPersona.SpeakingStyle}

【重要】
你必须通过工具调用提交你的决策；不要在普通文本里输出任何结构化内容或格式说明。";

        string usr =
            $@"关系 affinity：{input.Affinity}
当前层：{input.FloorIndex}

记忆：
{input.HistorySummary}

玩家发言：
{input.PlayerText}

写作要求（只影响 npc_reply 的语言表现）：
先回应情绪与意图 → 给态度 → 自然延伸（不强制提问）";

        return new Prompts { System = sys, User = usr };
    }

    // ===================== HTTP =====================
    private sealed class ApiCallResult
    {
        public int? HttpStatusCode;
        public string OuterRaw;
        public string ContentText; // tool_calls[0].function.arguments (normalized JSON object string)

        public long? PromptTokens;
        public long? CompletionTokens;
        public long? TotalTokens;
    }

    private static HttpClient CreateHttpClient(string apiKey)
    {
        var handler = new HttpClientHandler();

        // 很多校园网/公司网环境下会因为吊销检查失败导致 SSL 握手异常
        handler.CheckCertificateRevocationList = false;

        // 先禁用代理排查问题（如果你必须走代理，再改为 true）
        handler.UseProxy = false;

        // 强制 TLS 1.2（旧系统必须显式指定）
        try
        {
            System.Net.ServicePointManager.SecurityProtocol =
                System.Net.SecurityProtocolType.Tls12;
        }
        catch { }

        var http = new HttpClient(handler);

        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);

        http.DefaultRequestHeaders.UserAgent.ParseAdd("ExperimentsRunner/1.0");

        return http;
    }

private static async Task<ApiCallResult> CallDeepSeekAsync(
    HttpClient http,
    string model,
    string systemPrompt,
    string userPrompt,
    int maxTokens,
    double temperature)
{
    string toolsJson = BuildToolsJson();

    // ✅ 关键：强制指定函数，而不是 "required"
    string toolChoiceJson =
        "{" +
        "\"type\":\"function\"," +
        "\"function\":{\"name\":\"submit_npc_decision\"}" +
        "}";

    string body =
        "{" +
        "\"model\":\"" + JsonEscape(model) + "\"," +
        "\"max_tokens\":" + maxTokens.ToString(CultureInfo.InvariantCulture) + "," +
        "\"temperature\":" + temperature.ToString(CultureInfo.InvariantCulture) + "," +
        "\"tools\":" + toolsJson + "," +
        "\"tool_choice\":" + toolChoiceJson + "," +
        "\"parallel_tool_calls\":false," +  // ✅ 避免多 tool_call 干扰
        "\"messages\":[" +
            "{\"role\":\"system\",\"content\":\"" + JsonEscape(systemPrompt) + "\"}," +
            "{\"role\":\"user\",\"content\":\"" + JsonEscape(userPrompt) + "\"}" +
        "]" +
        "}";

    const int maxAttempts = 3;

    for (int attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds)))
            using (var req = new HttpRequestMessage(HttpMethod.Post, DeepSeekUrl))
            {
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");

                using (var resp = await http.SendAsync(req, cts.Token))
                {
                    string outer = await resp.Content.ReadAsStringAsync();

                    string toolName, toolArgsRaw;
                    ExtractFirstToolCallArguments(outer, out toolName, out toolArgsRaw);

                    // ✅ toolArgsRaw 可能是 string / 或带包裹（你已有 Normalize）
                    string toolArgs = NormalizeToolArguments(toolArgsRaw);

                    long? pt, ct, tt;
                    ExtractUsageTokens(outer, out pt, out ct, out tt);

                    return new ApiCallResult
                    {
                        HttpStatusCode = (int)resp.StatusCode,
                        OuterRaw = outer,
                        ContentText = toolArgs,
                        PromptTokens = pt,
                        CompletionTokens = ct,
                        TotalTokens = tt
                    };
                }
            }
        }
        catch (HttpRequestException ex)
        {
            string detail = ex.Message;
            if (ex.InnerException != null)
                detail += " | INNER: " + ex.InnerException.GetType().Name + " - " + ex.InnerException.Message;

            if (attempt < maxAttempts)
            {
                Thread.Sleep(300 * attempt);
                continue;
            }

            return new ApiCallResult
            {
                HttpStatusCode = null,
                OuterRaw = "CLIENT_ERROR: HttpRequestException. " + detail,
                ContentText = null
            };
        }
        catch (TaskCanceledException ex)
        {
            if (attempt < maxAttempts)
            {
                Thread.Sleep(300 * attempt);
                continue;
            }

            return new ApiCallResult
            {
                HttpStatusCode = null,
                OuterRaw = "CLIENT_ERROR: Timeout. " + ex.Message,
                ContentText = null
            };
        }
        catch (Exception ex)
        {
            return new ApiCallResult
            {
                HttpStatusCode = null,
                OuterRaw = "CLIENT_ERROR: " + ex.GetType().Name + ". " + ex.Message,
                ContentText = null
            };
        }
    }

    return new ApiCallResult
    {
        HttpStatusCode = null,
        OuterRaw = "CLIENT_ERROR: Unknown",
        ContentText = null
    };
}

    private static string BuildToolsJson()
{
    string nextEnum = string.Join(",", QuoteEnum(NextFloorWhitelist));
    string instEnum = string.Join(",", QuoteEnum(InstantWhitelist));

    string nextEventItem =
        "{" +
        "\"type\":\"object\"," +
        "\"properties\":{" +
            "\"eventType\":{\"type\":\"string\",\"enum\":[" + nextEnum + "]}," +
            "\"value\":{\"type\":\"number\"}" +
        "}," +
        "\"required\":[\"eventType\",\"value\"]," +
        "\"additionalProperties\":false" +
        "}";

    string instEventItem =
        "{" +
        "\"type\":\"object\"," +
        "\"properties\":{" +
            "\"eventType\":{\"type\":\"string\",\"enum\":[" + instEnum + "]}," +
            "\"value\":{\"type\":\"number\"}" +
        "}," +
        "\"required\":[\"eventType\",\"value\"]," +
        "\"additionalProperties\":false" +
        "}";

    // ✅ strict 要求：object 的 properties 必须全部 required + additionalProperties=false
    string parameters =
        "{" +
        "\"type\":\"object\"," +
        "\"properties\":{" +
            "\"npc_reply\":{\"type\":\"string\"}," +
            "\"affinity_delta\":{\"type\":\"integer\"}," +
            "\"next_floor_events\":{\"type\":\"array\",\"items\":" + nextEventItem + "}," +
            "\"instant_events\":{\"type\":\"array\",\"items\":" + instEventItem + "}," +
            "\"history_event_summary_delta\":{\"type\":\"string\"}" +
        "}," +
        "\"required\":[\"npc_reply\",\"affinity_delta\",\"next_floor_events\",\"instant_events\",\"history_event_summary_delta\"]," +
        "\"additionalProperties\":false" +
        "}";

    string tool =
        "{" +
          "\"type\":\"function\"," +
          "\"function\":{" +
            "\"name\":\"submit_npc_decision\"," +
            "\"strict\":true," +
            "\"description\":\"Submit NPC decision as a structured object.\"," +
            "\"parameters\":" + parameters +
          "}" +
        "}";

    return "[" + tool + "]";
}

    private static IEnumerable<string> QuoteEnum(HashSet<string> set)
    {
        foreach (var s in set) yield return "\"" + JsonEscape(s) + "\"";
    }

    // Parse outer JSON and extract choices[0].message.tool_calls[0].function.arguments
    // arguments may be a string or (rarely) an object depending on implementation.
    private static void ExtractFirstToolCallArguments(string outer, out string toolName, out string toolArgs)
    {
        toolName = null;
        toolArgs = null;

        if (string.IsNullOrWhiteSpace(outer)) return;

        try
        {
            var p = new MiniJsonParser(outer);
            JsonValue root = p.ParseRootStrictObject();

            var obj = root.AsObject();
            var choices = obj.GetArray("choices", required: true);
            if (choices == null || choices.Count <= 0) return;

            var choice0 = choices[0].AsObject();
            var message = choice0.GetObject("message", required: true);

            var toolCalls = message.GetArray("tool_calls", required: true);
            if (toolCalls == null || toolCalls.Count <= 0) return;

            var tc0 = toolCalls[0].AsObject();
            var fn = tc0.GetObject("function", required: true);

            toolName = fn.GetString("name", required: true);

            // Robust: arguments can be string or object
            toolArgs = TryGetArgumentsAsStringOrObjectJson(fn);
        }
        catch
        {
            // best-effort: leave nulls; EvaluateCompliance will mark as parse failure.
        }
    }

    private static string TryGetArgumentsAsStringOrObjectJson(Dictionary<string, JsonValue> fnObj)
    {
        if (!fnObj.TryGetValue("arguments", out JsonValue v))
            return null;

        if (v.Kind == JsonKind.String)
            return (string)v.Value;

        if (v.Kind == JsonKind.Object)
        {
            var o = v.AsObject();
            string npc = o.GetString("npc_reply", required: true) ?? "";
            int ad = o.GetInt("affinity_delta", required: true);

            var nextArr = o.GetArray("next_floor_events", required: true);
            var instArr = o.GetArray("instant_events", required: true);
            string hist = o.GetStringAllowNull("history_event_summary_delta", required: true) ?? "";

            return BuildDecisionJsonString(npc, ad, nextArr, instArr, hist);
        }

        return null;
    }

    private static string BuildDecisionJsonString(string npc, int ad, List<JsonValue> nextArr, List<JsonValue> instArr, string hist)
    {
        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append("\"npc_reply\":\"").Append(JsonEscape(npc)).Append("\",");
        sb.Append("\"affinity_delta\":").Append(ad.ToString(CultureInfo.InvariantCulture)).Append(",");
        sb.Append("\"next_floor_events\":").Append(EmitEventArray(nextArr)).Append(",");
        sb.Append("\"instant_events\":").Append(EmitEventArray(instArr)).Append(",");
        sb.Append("\"history_event_summary_delta\":\"").Append(JsonEscape(hist)).Append("\"");
        sb.Append("}");
        return sb.ToString();
    }

    private static string EmitEventArray(List<JsonValue> arr)
    {
        var sb = new StringBuilder();
        sb.Append("[");
        for (int i = 0; i < arr.Count; i++)
        {
            if (i > 0) sb.Append(",");
            var o = arr[i].AsObject();
            string et = o.GetString("eventType", required: true) ?? "";
            double v = o.GetDouble("value", required: true);

            sb.Append("{");
            sb.Append("\"eventType\":\"").Append(JsonEscape(et)).Append("\",");
            sb.Append("\"value\":").Append(v.ToString(CultureInfo.InvariantCulture));
            sb.Append("}");
        }
        sb.Append("]");
        return sb.ToString();
    }

    // Normalize tool arguments so EvaluateCompliance can parse it as strict JSON object.
    private static string NormalizeToolArguments(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;

        s = s.Trim();

        // Case A: double-encoded JSON string literal: "\"{...}\""
        if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
        {
            try
            {
                var p = new MiniJsonParser(s);
                var v = p.ParseAnyValueForNormalization();
                if (v.Kind == JsonKind.String)
                {
                    s = ((string)v.Value ?? "").Trim();
                }
            }
            catch
            {
                // fall through
            }
        }

        // Case B: extra wrappers (markdown fences or leading text): extract first {...} block
        if (s.Length > 0 && s[0] != '{')
        {
            int firstBrace = s.IndexOf('{');
            int lastBrace = s.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                string maybe = s.Substring(firstBrace, lastBrace - firstBrace + 1).Trim();
                if (maybe.Length > 1 && maybe[0] == '{' && maybe[maybe.Length - 1] == '}')
                    s = maybe;
            }
        }

        // Remove UTF-8 BOM if present
        if (s.Length > 0 && s[0] == '\uFEFF') s = s.Substring(1);

        return s.Trim();
    }

    // Extract usage.prompt_tokens / usage.completion_tokens / usage.total_tokens (best-effort)
    private static void ExtractUsageTokens(string outer, out long? prompt, out long? completion, out long? total)
    {
        prompt = null; completion = null; total = null;
        if (string.IsNullOrWhiteSpace(outer)) return;

        int u = outer.IndexOf("\"usage\"", StringComparison.Ordinal);
        if (u < 0) return;

        int brace = outer.IndexOf('{', u);
        if (brace < 0) return;

        int start = brace;
        int idx = brace;
        int depth = 0;
        bool inStr = false;
        bool esc = false;

        while (idx < outer.Length)
        {
            char c = outer[idx++];
            if (inStr)
            {
                if (esc) { esc = false; continue; }
                if (c == '\\') { esc = true; continue; }
                if (c == '"') { inStr = false; continue; }
                continue;
            }
            if (c == '"') { inStr = true; continue; }
            if (c == '{') depth++;
            if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    string usageObj = outer.Substring(start, idx - start);
                    prompt = ExtractLongField(usageObj, "prompt_tokens");
                    completion = ExtractLongField(usageObj, "completion_tokens");
                    total = ExtractLongField(usageObj, "total_tokens");
                    return;
                }
            }
        }
    }

    private static long? ExtractLongField(string objJson, string field)
    {
        if (string.IsNullOrEmpty(objJson)) return null;
        string key = "\"" + field + "\"";
        int i = objJson.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return null;
        i = objJson.IndexOf(':', i);
        if (i < 0) return null;
        i++;
        while (i < objJson.Length && char.IsWhiteSpace(objJson[i])) i++;

        int start = i;
        if (i < objJson.Length && objJson[i] == '-') i++;
        bool any = false;
        while (i < objJson.Length && char.IsDigit(objJson[i])) { i++; any = true; }
        if (!any) return null;

        string num = objJson.Substring(start, i - start);
        if (long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v))
            return v;
        return null;
    }

    private static string JsonEscape(string s)
    {
        if (s == null) return "";
        var sb = new StringBuilder(s.Length + 16);
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 32) sb.Append("\\u" + ((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    // ===================== Compliance (Parse/Schema/Semantic) =====================
    private sealed class EventItem
    {
        public string EventType;
        public double Value;
    }

    private sealed class ParsedDecision
    {
        public string NpcReply;
        public int AffinityDelta;
        public List<EventItem> NextFloorEvents = new List<EventItem>();
        public List<EventItem> InstantEvents = new List<EventItem>();
        public string HistoryDelta;
    }

    private sealed class EvalResult
    {
        public bool ParseOk;
        public bool SchemaOk;
        public bool SemanticOk;
        public bool AllOk { get { return ParseOk && SchemaOk && SemanticOk; } }
        public string FailLayer;
        public string FailReason;
    }

    private static EvalResult EvaluateCompliance(string content, out ParsedDecision parsed)
    {
        parsed = null;

        if (string.IsNullOrWhiteSpace(content))
            return Fail("Parse", "Empty content");

        JsonValue root;
        try
        {
            var p = new MiniJsonParser(content);
            root = p.ParseRootStrictObject();
        }
        catch (Exception ex)
        {
            return Fail("Parse", "Json parse failed: " + ex.GetType().Name);
        }

        var eval = new EvalResult { ParseOk = true };

        // Schema
        try
        {
            var obj = root.AsObject();

            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "npc_reply","affinity_delta","next_floor_events","instant_events","history_event_summary_delta"
            };
            foreach (var k in obj.Keys)
                if (!allowed.Contains(k))
                    return FailSchema("Extra field: " + k, eval);

            string npcReply = obj.GetString("npc_reply", required: true);
            int affinityDelta = obj.GetInt("affinity_delta", required: true);

            var nextArr = obj.GetArray("next_floor_events", required: true);
            var instArr = obj.GetArray("instant_events", required: true);

            string hist = obj.GetStringAllowNull("history_event_summary_delta", required: true) ?? "";

            if (npcReply == null) return FailSchema("npc_reply missing", eval);
            if (npcReply.Length > NpcReplyMaxChars) return FailSchema("npc_reply too long", eval);

            if (affinityDelta < AffinityDeltaMin || affinityDelta > AffinityDeltaMax)
                return FailSchema("affinity_delta out of range: " + affinityDelta, eval);

            if (nextArr.Count < NextFloorMin || nextArr.Count > NextFloorMax)
                return FailSchema("next_floor_events count out of range: " + nextArr.Count, eval);

            if (instArr.Count < InstantMin || instArr.Count > InstantMax)
                return FailSchema("instant_events count out of range: " + instArr.Count, eval);

            var nextList = ParseEventArray(nextArr, "next_floor_events", eval);
            if (nextList == null) return eval;

            var instList = ParseEventArray(instArr, "instant_events", eval);
            if (instList == null) return eval;

            parsed = new ParsedDecision
            {
                NpcReply = npcReply,
                AffinityDelta = affinityDelta,
                NextFloorEvents = nextList,
                InstantEvents = instList,
                HistoryDelta = hist
            };

            eval.SchemaOk = true;
        }
        catch (Exception ex)
        {
            return FailSchema("Exception: " + ex.GetType().Name, eval);
        }

        // Semantic
        try
        {
            if (string.IsNullOrWhiteSpace(parsed.NpcReply))
                return FailSemantic("npc_reply empty", eval);

            foreach (var mk in LeakageMarkers)
                if (parsed.NpcReply.IndexOf(mk, StringComparison.OrdinalIgnoreCase) >= 0)
                    return FailSemantic("npc_reply leakage marker: " + mk, eval);

            foreach (var e in parsed.NextFloorEvents)
            {
                if (string.IsNullOrWhiteSpace(e.EventType)) return FailSemantic("next_floor_events empty eventType", eval);
                if (e.EventType == "None") return FailSemantic("eventType None forbidden", eval);
                if (!NextFloorWhitelist.Contains(e.EventType)) return FailSemantic("next_floor_events not whitelisted: " + e.EventType, eval);
                if (!IsValueValidForEvent(e.EventType, e.Value)) return FailSemantic("next_floor_events invalid value: " + e.EventType + "=" + e.Value, eval);
            }
            foreach (var e in parsed.InstantEvents)
            {
                if (string.IsNullOrWhiteSpace(e.EventType)) return FailSemantic("instant_events empty eventType", eval);
                if (e.EventType == "None") return FailSemantic("eventType None forbidden", eval);
                if (!InstantWhitelist.Contains(e.EventType)) return FailSemantic("instant_events not whitelisted: " + e.EventType, eval);
                if (!IsValueValidForEvent(e.EventType, e.Value)) return FailSemantic("instant_events invalid value: " + e.EventType + "=" + e.Value, eval);
            }

            var allTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in parsed.NextFloorEvents) allTypes.Add(e.EventType);
            foreach (var e in parsed.InstantEvents) allTypes.Add(e.EventType);

            foreach (var pair in Contradictions)
                if (allTypes.Contains(pair.A) && allTypes.Contains(pair.B))
                    return FailSemantic("contradiction: " + pair.A + " with " + pair.B, eval);

            if (HasDuplicates(parsed.NextFloorEvents)) return FailSemantic("duplicate eventType in next_floor_events", eval);
            if (HasDuplicates(parsed.InstantEvents)) return FailSemantic("duplicate eventType in instant_events", eval);

            eval.SemanticOk = true;
            eval.FailLayer = "";
            eval.FailReason = "";
            return eval;
        }
        catch (Exception ex)
        {
            return FailSemantic("Exception: " + ex.GetType().Name, eval);
        }
    }

    private static bool HasDuplicates(List<EventItem> list)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in list)
            if (!set.Add(e.EventType)) return true;
        return false;
    }

    private static List<EventItem> ParseEventArray(List<JsonValue> arr, string fieldName, EvalResult eval)
    {
        var list = new List<EventItem>();
        for (int i = 0; i < arr.Count; i++)
        {
            if (arr[i].Kind != JsonKind.Object) return FailSchemaEvent(fieldName, i, "item not object", eval);
            var obj = arr[i].AsObject();

            string et = obj.GetString("eventType", required: true);
            double v = obj.GetDouble("value", required: true);

            if (et == null) return FailSchemaEvent(fieldName, i, "eventType missing", eval);

            list.Add(new EventItem { EventType = et.Trim(), Value = v });
        }
        return list;
    }

    private static List<EventItem> FailSchemaEvent(string field, int idx, string reason, EvalResult eval)
    {
        eval.SchemaOk = false;
        eval.SemanticOk = false;
        eval.FailLayer = "Schema";
        eval.FailReason = field + "[" + idx + "]: " + reason;
        return null;
    }

    private static bool IsValueValidForEvent(string eventType, double value)
    {
        switch (eventType)
        {
            case "LowVision": return InRange(value, 0.35, 1.0);

            case "EnemyMoveSpeedUp":
            case "PlayerDealMoreDamage":
            case "PlayerReceiveMoreDamage":
            case "PlayerAttackSpeedUp":
            case "WeaponPenetrationUp":
            case "WeaponExtraProjectileUp":
            case "WeaponExplosionOnHit":
                return InRange(value, 0.0, 3.0);

            case "PlayerAttackSpeedDown":
                return InRange(value, 0.0, 0.9);

            case "WeaponBulletSizeUp":
                return InRange(value, 0.0, 2.0);

            case "Heal":
            case "LoseHP":
                return InRange(value, 1.0, 50.0) && IsIntegerish(value);

            case "GainExp":
                return InRange(value, 10.0, 100.0) && IsIntegerish(value);

            case "PlayerMaxHPUp":
            case "PlayerMaxHPDown":
                return InRange(value, 1.0, 20.0) && IsIntegerish(value);

            case "PlayerAttackUp":
            case "PlayerAttackDown":
                return InRange(value, 1.0, 5.0) && IsIntegerish(value);

            case "AllRoomsMonsterExceptBossAndSpawn":
            case "AllRoomsRewardExceptBossAndSpawn":
                return InRange(value, 0.0, 0.0);

            default:
                return false;
        }
    }

    private static bool InRange(double v, double min, double max) => v >= min - 1e-9 && v <= max + 1e-9;
    private static bool IsIntegerish(double v) => Math.Abs(v - Math.Round(v)) < 1e-9;

    private static EvalResult Fail(string layer, string reason)
    {
        return new EvalResult
        {
            ParseOk = false,
            SchemaOk = false,
            SemanticOk = false,
            FailLayer = layer,
            FailReason = reason
        };
    }

    private static EvalResult FailSchema(string reason, EvalResult eval)
    {
        eval.SchemaOk = false;
        eval.SemanticOk = false;
        eval.FailLayer = "Schema";
        eval.FailReason = reason;
        return eval;
    }

    private static EvalResult FailSemantic(string reason, EvalResult eval)
    {
        eval.SemanticOk = false;
        eval.FailLayer = "Semantic";
        eval.FailReason = reason;
        return eval;
    }

    private static string Pct(int ok, int total)
    {
        if (total <= 0) return "0%";
        return (ok * 100.0 / total).ToString("0.00", CultureInfo.InvariantCulture) + "%";
    }

    // ===================== CSV Append =====================
    private sealed class CsvRow
    {
        public int RunId;
        public string TimestampUtc;
        public string Model;
        public int Seed;
        public int Affinity;
        public int FloorIndex;
        public string PlayerText;
        public string HistorySummary;

        public long LatencyMs;
        public string HttpStatus;

        public long? PromptTokens;
        public long? CompletionTokens;
        public long? TotalTokens;

        public bool ParseOk;
        public bool SchemaOk;
        public bool SemanticOk;
        public bool AllOk;
        public string FailLayer;
        public string FailReason;

        public string NpcReply;
        public int? AffinityDelta;
        public int? NextFloorCount;
        public int? InstantCount;

        public string ContentRaw;
        public string OuterRaw;
    }

    private static void EnsureCsvHeader(string path)
    {
        if (File.Exists(path)) return;

        string header = string.Join(",",
            "run_id","timestamp_utc","model","seed","affinity","floor_index",
            "player_text","history_summary",
            "latency_ms","http_status",
            "prompt_tokens","completion_tokens","total_tokens",
            "parse_ok","schema_ok","semantic_ok","all_ok","fail_layer","fail_reason",
            "npc_reply","affinity_delta","next_floor_count","instant_count",
            "content_raw","outer_raw"
        );

        File.WriteAllText(path, header + Environment.NewLine, new UTF8Encoding(true));
    }

    private static int GetNextRunId(string path)
    {
        int maxId = 0;
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("run_id,")) continue;
            int comma = line.IndexOf(',');
            if (comma <= 0) continue;
            if (int.TryParse(line.Substring(0, comma), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                if (id > maxId) maxId = id;
        }
        return maxId + 1;
    }

    private static void AppendCsvRow(string path, CsvRow r)
    {
        string[] cols =
        {
            r.RunId.ToString(CultureInfo.InvariantCulture),
            r.TimestampUtc ?? "",
            r.Model ?? "",
            r.Seed.ToString(CultureInfo.InvariantCulture),
            r.Affinity.ToString(CultureInfo.InvariantCulture),
            r.FloorIndex.ToString(CultureInfo.InvariantCulture),

            r.PlayerText ?? "",
            r.HistorySummary ?? "",

            r.LatencyMs.ToString(CultureInfo.InvariantCulture),
            r.HttpStatus ?? "",

            r.PromptTokens.HasValue ? r.PromptTokens.Value.ToString(CultureInfo.InvariantCulture) : "",
            r.CompletionTokens.HasValue ? r.CompletionTokens.Value.ToString(CultureInfo.InvariantCulture) : "",
            r.TotalTokens.HasValue ? r.TotalTokens.Value.ToString(CultureInfo.InvariantCulture) : "",

            r.ParseOk ? "1" : "0",
            r.SchemaOk ? "1" : "0",
            r.SemanticOk ? "1" : "0",
            r.AllOk ? "1" : "0",
            r.FailLayer ?? "",
            r.FailReason ?? "",

            r.NpcReply ?? "",
            r.AffinityDelta.HasValue ? r.AffinityDelta.Value.ToString(CultureInfo.InvariantCulture) : "",
            r.NextFloorCount.HasValue ? r.NextFloorCount.Value.ToString(CultureInfo.InvariantCulture) : "",
            r.InstantCount.HasValue ? r.InstantCount.Value.ToString(CultureInfo.InvariantCulture) : "",

            r.ContentRaw ?? "",
            r.OuterRaw ?? ""
        };

        for (int i = 0; i < cols.Length; i++)
            cols[i] = CsvEscape(cols[i]);

        File.AppendAllText(path, string.Join(",", cols) + Environment.NewLine, new UTF8Encoding(true));
    }

    private static string CsvEscape(string s)
    {
        if (s == null) return "";
        bool needQuote = s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        if (!needQuote) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    // ===================== Mini JSON (strict) =====================
    private enum JsonKind { Null, Bool, Number, String, Array, Object }

    private sealed class JsonValue
    {
        public JsonKind Kind;
        public object Value;

        public Dictionary<string, JsonValue> AsObject()
        {
            if (Kind != JsonKind.Object) throw new InvalidOperationException("Not object");
            return (Dictionary<string, JsonValue>)Value;
        }
    }

    private sealed class MiniJsonParser
    {
        private readonly string _s;
        private int _i;

        public MiniJsonParser(string s) { _s = s; _i = 0; }

        public JsonValue ParseRootStrictObject()
        {
            SkipWs();
            var v = ParseValue();
            SkipWs();
            if (_i != _s.Length) throw new Exception("Trailing characters");
            if (v.Kind != JsonKind.Object) throw new Exception("Root not object");
            return v;
        }

        // For normalization: allow any JSON value (string/object/array/number/bool/null)
        public JsonValue ParseAnyValueForNormalization()
        {
            SkipWs();
            var v = ParseValue();
            SkipWs();
            if (_i != _s.Length) throw new Exception("Trailing characters");
            return v;
        }

        private JsonValue ParseValue()
        {
            SkipWs();
            if (_i >= _s.Length) throw new Exception("Unexpected end");

            char c = _s[_i];
            if (c == '{') return ParseObject();
            if (c == '[') return ParseArray();
            if (c == '"') return new JsonValue { Kind = JsonKind.String, Value = ParseString() };
            if (c == 't' || c == 'f') return ParseBool();
            if (c == 'n') return ParseNull();
            if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
            throw new Exception("Unexpected char: " + c);
        }

        private JsonValue ParseObject()
        {
            Expect('{');
            SkipWs();
            var dict = new Dictionary<string, JsonValue>(StringComparer.Ordinal);

            if (Peek('}')) { _i++; return new JsonValue { Kind = JsonKind.Object, Value = dict }; }

            while (true)
            {
                SkipWs();
                string key = ParseString();
                SkipWs();
                Expect(':');
                SkipWs();
                JsonValue val = ParseValue();
                dict[key] = val;
                SkipWs();

                if (Peek('}')) { _i++; break; }
                Expect(',');
            }

            return new JsonValue { Kind = JsonKind.Object, Value = dict };
        }

        private JsonValue ParseArray()
        {
            Expect('[');
            SkipWs();
            var list = new List<JsonValue>();

            if (Peek(']')) { _i++; return new JsonValue { Kind = JsonKind.Array, Value = list }; }

            while (true)
            {
                SkipWs();
                list.Add(ParseValue());
                SkipWs();
                if (Peek(']')) { _i++; break; }
                Expect(',');
            }

            return new JsonValue { Kind = JsonKind.Array, Value = list };
        }

        private JsonValue ParseBool()
        {
            if (Match("true")) return new JsonValue { Kind = JsonKind.Bool, Value = true };
            if (Match("false")) return new JsonValue { Kind = JsonKind.Bool, Value = false };
            throw new Exception("Invalid bool");
        }

        private JsonValue ParseNull()
        {
            if (!Match("null")) throw new Exception("Invalid null");
            return new JsonValue { Kind = JsonKind.Null, Value = null };
        }

        private JsonValue ParseNumber()
        {
            int start = _i;
            if (Peek('-')) _i++;

            bool hasDigits = false;
            while (_i < _s.Length && char.IsDigit(_s[_i])) { _i++; hasDigits = true; }
            if (!hasDigits) throw new Exception("Invalid number");

            if (_i < _s.Length && _s[_i] == '.')
            {
                _i++;
                int fracStart = _i;
                while (_i < _s.Length && char.IsDigit(_s[_i])) _i++;
                if (_i == fracStart) throw new Exception("Invalid number fraction");
            }

            if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
            {
                _i++;
                if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                int expStart = _i;
                while (_i < _s.Length && char.IsDigit(_s[_i])) _i++;
                if (_i == expStart) throw new Exception("Invalid number exponent");
            }

            string token = _s.Substring(start, _i - start);
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                throw new Exception("Invalid number parse");

            return new JsonValue { Kind = JsonKind.Number, Value = d };
        }

        private string ParseString()
        {
            Expect('"');
            var sb = new StringBuilder();
            while (_i < _s.Length)
            {
                char c = _s[_i++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (_i >= _s.Length) throw new Exception("Invalid escape");
                    char e = _s[_i++];
                    switch (e)
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
                            if (_i + 4 > _s.Length) throw new Exception("Invalid unicode escape");
                            string hex = _s.Substring(_i, 4);
                            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                throw new Exception("Invalid unicode hex");
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default:
                            throw new Exception("Invalid escape char: " + e);
                    }
                    continue;
                }
                if (c < 0x20) throw new Exception("Control char in string");
                sb.Append(c);
            }
            throw new Exception("Unterminated string");
        }

        private void SkipWs()
        {
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { _i++; continue; }
                break;
            }
        }

        private void Expect(char c)
        {
            if (_i >= _s.Length || _s[_i] != c) throw new Exception("Expected: " + c);
            _i++;
        }

        private bool Peek(char c) => _i < _s.Length && _s[_i] == c;

        private bool Match(string lit)
        {
            if (_i + lit.Length > _s.Length) return false;
            for (int k = 0; k < lit.Length; k++)
                if (_s[_i + k] != lit[k]) return false;
            _i += lit.Length;
            return true;
        }
    }

    // Dictionary helpers
    private static string GetString(this Dictionary<string, JsonValue> obj, string key, bool required)
    {
        if (!obj.TryGetValue(key, out JsonValue v))
        {
            if (required) throw new Exception("Missing: " + key);
            return null;
        }
        if (v.Kind != JsonKind.String) throw new Exception("Type mismatch string: " + key);
        return (string)v.Value;
    }

    private static string GetStringAllowNull(this Dictionary<string, JsonValue> obj, string key, bool required)
    {
        if (!obj.TryGetValue(key, out JsonValue v))
        {
            if (required) throw new Exception("Missing: " + key);
            return null;
        }
        if (v.Kind == JsonKind.Null) return null;
        if (v.Kind != JsonKind.String) throw new Exception("Type mismatch string/null: " + key);
        return (string)v.Value;
    }

    private static int GetInt(this Dictionary<string, JsonValue> obj, string key, bool required)
    {
        if (!obj.TryGetValue(key, out JsonValue v))
        {
            if (required) throw new Exception("Missing: " + key);
            return 0;
        }
        if (v.Kind != JsonKind.Number) throw new Exception("Type mismatch number: " + key);
        double d = (double)v.Value;
        if (Math.Abs(d - Math.Round(d)) > 1e-9) throw new Exception("Not int: " + key);
        return (int)Math.Round(d);
    }

    private static double GetDouble(this Dictionary<string, JsonValue> obj, string key, bool required)
    {
        if (!obj.TryGetValue(key, out JsonValue v))
        {
            if (required) throw new Exception("Missing: " + key);
            return 0;
        }
        if (v.Kind != JsonKind.Number) throw new Exception("Type mismatch number: " + key);
        return (double)v.Value;
    }

    private static List<JsonValue> GetArray(this Dictionary<string, JsonValue> obj, string key, bool required)
    {
        if (!obj.TryGetValue(key, out JsonValue v))
        {
            if (required) throw new Exception("Missing: " + key);
            return null;
        }
        if (v.Kind != JsonKind.Array) throw new Exception("Type mismatch array: " + key);
        return (List<JsonValue>)v.Value;
    }

    private static Dictionary<string, JsonValue> GetObject(this Dictionary<string, JsonValue> obj, string key, bool required)
    {
        if (!obj.TryGetValue(key, out JsonValue v))
        {
            if (required) throw new Exception("Missing: " + key);
            return null;
        }
        if (v.Kind != JsonKind.Object) throw new Exception("Type mismatch object: " + key);
        return v.AsObject();
    }
}