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
    private enum ExperimentMode
    {
        Prompt = 0,
        ApiJson = 1,
        SchemaValidation = 2,
        SchemaRetry = 3,
    }

    private static readonly ExperimentMode[] AllModes =
    {
        ExperimentMode.Prompt,
        ExperimentMode.ApiJson,
        ExperimentMode.SchemaValidation,
        ExperimentMode.SchemaRetry
    };

    // ====== API ======
    private const string DeepSeekUrl = "https://api.deepseek.com/chat/completions";
    private const string DefaultModelsCsv = "deepseek-chat,deepseek-reasoner";

    // ====== Version / Resume ======
    private const string ExperimentVersion = "v4_resume_by_csv";

    // Defaults (can be overridden by CLI args)
    private const int DefaultMaxTokens = 900;
    private const double DefaultTemperature = 0.7;
    private const int DefaultRunsPerMode = 20;
    private const int TimeoutSeconds = 12;

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

    // ====== Retry strategy ======
    private const int SchemaRetryMaxExtraAttempts = 1; // total attempts = 2

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
            int runsPerMode = DefaultRunsPerMode;
            if (args.Length >= 1 && int.TryParse(args[0], out int n) && n > 0)
                runsPerMode = n;

            string modelsCsv = (args.Length >= 2 && !string.IsNullOrWhiteSpace(args[1]))
                ? args[1].Trim()
                : DefaultModelsCsv;

            string[] models = ParseModelsCsv(modelsCsv);
            if (models.Length == 0)
            {
                Console.Error.WriteLine("ERROR: no valid models found.");
                return 2;
            }

            int maxTokens = DefaultMaxTokens;
            if (args.Length >= 3 && int.TryParse(args[2], out int mt) && mt > 0)
                maxTokens = mt;

            double temperature = DefaultTemperature;
            if (args.Length >= 4 && double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double t)
                && t >= 0.0 && t <= 2.0)
            {
                temperature = t;
            }

            int baseSeed = 114514;
            if (args.Length >= 5 && int.TryParse(args[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int bs))
                baseSeed = bs;

            string apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? "";
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.Error.WriteLine("ERROR: DEEPSEEK_API_KEY is missing.");
                return 2;
            }

            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            }
            catch { }

            string outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CsvName);
            EnsureCsvHeader(outPath);

            string configTag = BuildConfigTag(models, maxTokens, temperature, baseSeed);

            CsvProgressState progress = LoadCsvProgress(outPath, configTag, models, AllModes, runsPerMode);
            int nextRunId = progress.NextRunId;
            string batchId = progress.ResumeBatchId ?? DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            bool isResuming = progress.ResumeBatchId != null;

            using (var http = CreateHttpClient(apiKey))
            {
                Console.WriteLine("===== EXPERIMENT CONFIG =====");
                Console.WriteLine("ConfigTag      : " + configTag);
                Console.WriteLine("BatchId        : " + batchId + (isResuming ? " (RESUME)" : " (NEW)"));
                Console.WriteLine("Models         : " + string.Join(", ", models));
                Console.WriteLine("Modes          : " + string.Join(", ", ToModeStrings(AllModes)));
                Console.WriteLine("Runs/Mode      : " + runsPerMode);
                Console.WriteLine("max_tokens     : " + maxTokens);
                Console.WriteLine("temperature    : " + temperature.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("base_seed      : " + baseSeed);
                Console.WriteLine("CSV            : " + outPath);
                Console.WriteLine("=============================");
                Console.WriteLine();

                for (int modelIndex = 0; modelIndex < models.Length; modelIndex++)
                {
                    string model = models[modelIndex];
                    Console.WriteLine("############################################################");
                    Console.WriteLine("MODEL: " + model);
                    Console.WriteLine("############################################################");

                    for (int modeIndex = 0; modeIndex < AllModes.Length; modeIndex++)
                    {
                        ExperimentMode mode = AllModes[modeIndex];
                        Console.WriteLine();
                        Console.WriteLine("------------------------------------------------------------");
                        Console.WriteLine("MODE : " + mode);
                        Console.WriteLine("------------------------------------------------------------");

                        int existingCount = progress.GetCount(batchId, model, mode.ToString());
                        Console.WriteLine("Existing rows for this batch/model/mode: " + existingCount + "/" + runsPerMode);

                        if (existingCount >= runsPerMode)
                        {
                            Console.WriteLine("Already complete. Skip.");
                            continue;
                        }

                        var stats = new BlockStats();

                        for (int localRun = existingCount; localRun < runsPerMode; localRun++)
                        {
                            int runId = nextRunId++;
                            int inputSeed = unchecked(baseSeed + localRun * 10007);
                            var rng = new Random(inputSeed);

                            var input = BuildExperimentInput(rng);
                            var prompts = BuildPrompts(input, mode);

                            int retryCount;
                            ApiCallResult api;
                            ParsedDecision parsed;
                            EvalResult eval;

                            ExecuteOneExperiment(
                                http: http,
                                model: model,
                                mode: mode,
                                prompts: prompts,
                                maxTokens: maxTokens,
                                temperature: temperature,
                                out api,
                                out parsed,
                                out eval,
                                out retryCount);

                            stats.Add(api, eval);

                            AppendCsvRow(outPath, new CsvRow
                            {
                                RunId = runId,
                                BatchId = batchId,
                                ConfigTag = configTag,
                                TimestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                                Model = model,
                                Mode = mode.ToString(),
                                RetryCount = retryCount,
                                LocalRunIndex = localRun + 1,
                                Seed = inputSeed,
                                Affinity = input.Affinity,
                                FloorIndex = input.FloorIndex,
                                PlayerText = input.PlayerText,
                                HistorySummary = input.HistorySummary,

                                LatencyMs = api.LatencyMs,
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
                                "[" + runId.ToString(CultureInfo.InvariantCulture) + "] " +
                                "batch=" + batchId + " " +
                                "local=" + (localRun + 1).ToString(CultureInfo.InvariantCulture) + "/" + runsPerMode.ToString(CultureInfo.InvariantCulture) + " " +
                                "model=" + model + " " +
                                "mode=" + mode + " " +
                                "retry=" + retryCount.ToString(CultureInfo.InvariantCulture) + " " +
                                "ok=" + eval.AllOk + " " +
                                "parse=" + eval.ParseOk + " " +
                                "schema=" + eval.SchemaOk + " " +
                                "sem=" + eval.SemanticOk + " " +
                                "lat=" + api.LatencyMs.ToString(CultureInfo.InvariantCulture) + "ms " +
                                "tok=" + (api.TotalTokens.HasValue ? api.TotalTokens.Value.ToString(CultureInfo.InvariantCulture) : "") + " " +
                                "reason=" + (eval.FailLayer ?? "") + ":" + (eval.FailReason ?? "")
                            );
                        }

                        PrintBlockSummary(model, mode, runsPerMode - existingCount, stats);
                    }
                }

                Console.WriteLine();
                Console.WriteLine("ALL EXPERIMENTS FINISHED.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FATAL: " + ex);
            return 1;
        }
    }

    // ===================== Resume / CSV Progress =====================

    private sealed class CsvProgressState
    {
        public int NextRunId;
        public string ResumeBatchId;
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);

        public void AddCount(string batchId, string model, string mode)
        {
            string key = MakeKey(batchId, model, mode);
            if (_counts.TryGetValue(key, out int v)) _counts[key] = v + 1;
            else _counts[key] = 1;
        }

        public int GetCount(string batchId, string model, string mode)
        {
            string key = MakeKey(batchId, model, mode);
            return _counts.TryGetValue(key, out int v) ? v : 0;
        }

        private static string MakeKey(string batchId, string model, string mode)
        {
            return (batchId ?? "") + "\u001F" + (model ?? "") + "\u001F" + (mode ?? "");
        }
    }

    private static string BuildConfigTag(string[] models, int maxTokens, double temperature, int baseSeed)
    {
        string modelsNorm = string.Join("|", models ?? new string[0]);
        return
            "ver=" + ExperimentVersion +
            ";models=" + modelsNorm +
            ";max_tokens=" + maxTokens.ToString(CultureInfo.InvariantCulture) +
            ";temperature=" + temperature.ToString("0.####", CultureInfo.InvariantCulture) +
            ";base_seed=" + baseSeed.ToString(CultureInfo.InvariantCulture);
    }

    private static CsvProgressState LoadCsvProgress(
        string path,
        string configTag,
        string[] models,
        ExperimentMode[] modes,
        int targetRunsPerMode)
    {
        var result = new CsvProgressState();
        result.NextRunId = 1;

        if (!File.Exists(path))
            return result;

        List<string[]> records = ReadCsvRecords(path);
        if (records.Count <= 1)
            return result;

        string[] header = records[0];
        var headerMap = BuildHeaderMap(header);

        int idxRunId = GetHeaderIndex(headerMap, "run_id");
        int idxBatchId = GetHeaderIndex(headerMap, "batch_id");
        int idxConfigTag = GetHeaderIndex(headerMap, "config_tag");
        int idxModel = GetHeaderIndex(headerMap, "model");
        int idxMode = GetHeaderIndex(headerMap, "mode");

        var candidateBatches = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 1; i < records.Count; i++)
        {
            string[] row = records[i];
            if (row == null || row.Length == 0) continue;

            string runIdText = GetCell(row, idxRunId);
            if (int.TryParse(runIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int runId))
            {
                if (runId >= result.NextRunId) result.NextRunId = runId + 1;
            }

            string rowConfigTag = GetCell(row, idxConfigTag);
            if (!string.Equals(rowConfigTag, configTag, StringComparison.Ordinal))
                continue;

            string batchId = GetCell(row, idxBatchId);
            string model = GetCell(row, idxModel);
            string mode = GetCell(row, idxMode);

            if (string.IsNullOrWhiteSpace(batchId) ||
                string.IsNullOrWhiteSpace(model) ||
                string.IsNullOrWhiteSpace(mode))
            {
                continue;
            }

            result.AddCount(batchId, model, mode);
            candidateBatches.Add(batchId);
        }

        List<string> sortedBatches = new List<string>(candidateBatches);
        sortedBatches.Sort(StringComparer.Ordinal);
        sortedBatches.Reverse(); // latest first because yyyyMMdd_HHmmss

        for (int i = 0; i < sortedBatches.Count; i++)
        {
            string batchId = sortedBatches[i];
            bool incomplete = false;

            for (int m = 0; m < models.Length; m++)
            {
                for (int k = 0; k < modes.Length; k++)
                {
                    int cnt = result.GetCount(batchId, models[m], modes[k].ToString());
                    if (cnt < targetRunsPerMode)
                    {
                        incomplete = true;
                        break;
                    }
                }
                if (incomplete) break;
            }

            if (incomplete)
            {
                result.ResumeBatchId = batchId;
                break;
            }
        }

        return result;
    }

    private static Dictionary<string, int> BuildHeaderMap(string[] header)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < header.Length; i++)
        {
            string name = header[i] ?? "";
            if (!map.ContainsKey(name))
                map[name] = i;
        }
        return map;
    }

    private static int GetHeaderIndex(Dictionary<string, int> map, string name)
    {
        if (!map.TryGetValue(name, out int idx))
            throw new InvalidOperationException("CSV header missing required column: " + name);
        return idx;
    }

    private static string GetCell(string[] row, int index)
    {
        if (index < 0 || index >= row.Length) return "";
        return row[index] ?? "";
    }

    private static List<string[]> ReadCsvRecords(string path)
    {
        var records = new List<string[]>();

        using (var sr = new StreamReader(path, Encoding.UTF8, true))
        {
            var row = new List<string>();
            var cell = new StringBuilder();
            bool inQuotes = false;

            while (true)
            {
                int x = sr.Read();
                if (x < 0)
                {
                    row.Add(cell.ToString());
                    if (!(row.Count == 1 && row[0].Length == 0 && records.Count == 0))
                        records.Add(row.ToArray());
                    break;
                }

                char c = (char)x;

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        int next = sr.Peek();
                        if (next == '"')
                        {
                            sr.Read();
                            cell.Append('"');
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        cell.Append(c);
                    }
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                    continue;
                }

                if (c == ',')
                {
                    row.Add(cell.ToString());
                    cell.Length = 0;
                    continue;
                }

                if (c == '\r')
                {
                    int next = sr.Peek();
                    if (next == '\n') sr.Read();

                    row.Add(cell.ToString());
                    cell.Length = 0;
                    records.Add(row.ToArray());
                    row = new List<string>();
                    continue;
                }

                if (c == '\n')
                {
                    row.Add(cell.ToString());
                    cell.Length = 0;
                    records.Add(row.ToArray());
                    row = new List<string>();
                    continue;
                }

                cell.Append(c);
            }
        }

        return records;
    }

    // ===================== Execution =====================

    private static void ExecuteOneExperiment(
        HttpClient http,
        string model,
        ExperimentMode mode,
        Prompts prompts,
        int maxTokens,
        double temperature,
        out ApiCallResult finalApi,
        out ParsedDecision finalParsed,
        out EvalResult finalEval,
        out int retryCount)
    {
        retryCount = 0;
        finalApi = null;
        finalParsed = null;
        finalEval = null;

        int attempts = 0;
        int maxAttempts = 1;

        if (mode == ExperimentMode.SchemaRetry)
            maxAttempts = 1 + SchemaRetryMaxExtraAttempts;

        while (attempts < maxAttempts)
        {
            attempts++;

            ApiCallResult api = CallDeepSeekAsync(
                http,
                model,
                prompts.System,
                prompts.User,
                maxTokens,
                temperature,
                mode).GetAwaiter().GetResult();

            ParsedDecision parsed;
            EvalResult eval = EvaluateCompliance(api.ContentText, out parsed);

            finalApi = api;
            finalParsed = parsed;
            finalEval = eval;

            if (mode == ExperimentMode.SchemaRetry)
            {
                bool shouldRetry = !eval.ParseOk || !eval.SchemaOk;
                if (shouldRetry && attempts < maxAttempts)
                {
                    retryCount++;
                    continue;
                }
            }

            break;
        }
    }

    private static void PrintBlockSummary(string model, ExperimentMode mode, int runs, BlockStats s)
    {
        Console.WriteLine();
        Console.WriteLine(">>> SUMMARY");
        Console.WriteLine("Model               : " + model);
        Console.WriteLine("Mode                : " + mode);
        Console.WriteLine("NewRunsThisLaunch   : " + runs.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("AllOk               : " + s.AllOk + "/" + runs + " (" + Pct(s.AllOk, runs) + ")");
        Console.WriteLine("ParseOk             : " + s.ParseOk + "/" + runs + " (" + Pct(s.ParseOk, runs) + ")");
        Console.WriteLine("SchemaOk            : " + s.SchemaOk + "/" + runs + " (" + Pct(s.SchemaOk, runs) + ")");
        Console.WriteLine("SemanticOk          : " + s.SemanticOk + "/" + runs + " (" + Pct(s.SemanticOk, runs) + ")");
        Console.WriteLine("AvgLatency(ms)      : " + (runs > 0 ? (s.TotalLatencyMs / (double)runs) : 0).ToString("0.0", CultureInfo.InvariantCulture));

        if (s.UsageRows > 0)
        {
            Console.WriteLine("UsageRows           : " + s.UsageRows + "/" + runs);
            Console.WriteLine("AvgPromptTokens     : " + (s.TotalPromptTokens / (double)s.UsageRows).ToString("0.0", CultureInfo.InvariantCulture));
            Console.WriteLine("AvgCompletionTokens : " + (s.TotalCompletionTokens / (double)s.UsageRows).ToString("0.0", CultureInfo.InvariantCulture));
            Console.WriteLine("AvgTotalTokens      : " + (s.TotalTokens / (double)s.UsageRows).ToString("0.0", CultureInfo.InvariantCulture));
        }
        else
        {
            Console.WriteLine("UsageRows           : 0/" + runs);
        }

        Console.WriteLine();
    }

    private sealed class BlockStats
    {
        public int ParseOk;
        public int SchemaOk;
        public int SemanticOk;
        public int AllOk;

        public long TotalLatencyMs;
        public long TotalPromptTokens;
        public long TotalCompletionTokens;
        public long TotalTokens;
        public int UsageRows;

        public void Add(ApiCallResult api, EvalResult eval)
        {
            if (eval.ParseOk) ParseOk++;
            if (eval.SchemaOk) SchemaOk++;
            if (eval.SemanticOk) SemanticOk++;
            if (eval.AllOk) AllOk++;

            TotalLatencyMs += api.LatencyMs;

            if (api.TotalTokens.HasValue)
            {
                UsageRows++;
                TotalPromptTokens += api.PromptTokens ?? 0;
                TotalCompletionTokens += api.CompletionTokens ?? 0;
                TotalTokens += api.TotalTokens ?? 0;
            }
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
            "我们最近在第 " + Math.Max(1, floor - 1).ToString(CultureInfo.InvariantCulture) + " 层附近交流过，你提到风险与收益要平衡。\n" +
            "（run-mem:" + a.ToString(CultureInfo.InvariantCulture) + "-" + b.ToString(CultureInfo.InvariantCulture) + "-" + c.ToString(CultureInfo.InvariantCulture) + "）\n\n" +
            "【上一层表现】\n" +
            "- 用时：" + Math.Round(15 + rng.NextDouble() * 80, 1).ToString(CultureInfo.InvariantCulture) + "s\n" +
            "- 承伤：" + Math.Round(rng.NextDouble() * 60, 1).ToString(CultureInfo.InvariantCulture) +
            "（最大单次 " + Math.Round(5 + rng.NextDouble() * 35, 1).ToString(CultureInfo.InvariantCulture) + "）\n" +
            "- 击杀：" + rng.Next(0, 18).ToString(CultureInfo.InvariantCulture) +
            "；命中：" + rng.Next(0, 30).ToString(CultureInfo.InvariantCulture) +
            "；开火：" + rng.Next(1, 40).ToString(CultureInfo.InvariantCulture) + "\n";

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

    private static Prompts BuildPrompts(ExperimentInput input, ExperimentMode mode)
    {
        string sys;

        if (mode == ExperimentMode.Prompt)
        {
            sys =
                "你是地牢NPC【" + FixedPersona.NpcName + "】。请只输出一个JSON对象，不得输出任何额外文本。\n\n" +
                "输出结构如下：\n" +
                "{\n" +
                "  \"npc_reply\": \"...\",\n" +
                "  \"affinity_delta\": 0,\n" +
                "  \"next_floor_events\": [],\n" +
                "  \"instant_events\": [],\n" +
                "  \"history_event_summary_delta\": \"\"\n" +
                "}\n\n" +
                "要求：\n" +
                "1. 必须是合法JSON。\n" +
                "2. npc_reply为中文2~4句，尽量自然，不得提及系统、规则、JSON、提示词。\n" +
                "3. affinity_delta 为整数。\n" +
                "4. next_floor_events 和 instant_events 为数组。\n" +
                "5. history_event_summary_delta 为字符串。\n" +
                "6. 尽量让事件和玩家语气、当前状态相匹配。";
        }
        else if (mode == ExperimentMode.ApiJson)
        {
            sys =
                "你是地牢NPC【" + FixedPersona.NpcName + "】。仅输出一个JSON对象，不得输出任何额外文本。\n\n" +
                "固定结构：\n" +
                "{\n" +
                "  \"npc_reply\": \"...\",\n" +
                "  \"affinity_delta\": 0,\n" +
                "  \"next_floor_events\": [],\n" +
                "  \"instant_events\": [],\n" +
                "  \"history_event_summary_delta\": \"\"\n" +
                "}\n\n" +
                "要求：\n" +
                "npc_reply：中文2~4句，不得提及系统/规则/JSON/提示词。\n" +
                "affinity_delta：整数。\n" +
                "next_floor_events、instant_events：数组。\n" +
                "数组元素格式：{ \"eventType\": \"...\", \"value\": number }。";
        }
        else
        {
            sys =
                "你是地牢NPC【" + FixedPersona.NpcName + "】。仅输出一个JSON对象，不得输出任何额外文本。\n\n" +
                "固定结构（字段不可改）：\n" +
                "{\n" +
                "  \"npc_reply\": \"...\",\n" +
                "  \"affinity_delta\": 0,\n" +
                "  \"next_floor_events\": [],\n" +
                "  \"instant_events\": [],\n" +
                "  \"history_event_summary_delta\": \"\"\n" +
                "}\n\n" +
                "字段规则：\n" +
                "npc_reply：中文2~4句，≤160字，不得提及系统/规则/JSON/提示词。\n" +
                "affinity_delta：整数，-5~5。\n" +
                "next_floor_events：0~4项。\n" +
                "instant_events：0~3项。\n" +
                "数组元素格式：{ \"eventType\": \"...\", \"value\": number }。\n" +
                "同一数组内eventType不可重复。\n" +
                "history_event_summary_delta：字符串（可为空）。\n" +
                "所有value ≥ 0。\n\n" +
                "位置强制：\n" +
                "只能在next_floor_events：\n" +
                "LowVision, EnemyMoveSpeedUp, PlayerDealMoreDamage, PlayerReceiveMoreDamage,\n" +
                "AllRoomsMonsterExceptBossAndSpawn, AllRoomsRewardExceptBossAndSpawn,\n" +
                "PlayerAttackSpeedUp, PlayerAttackSpeedDown。\n\n" +
                "只能在instant_events：\n" +
                "GainExp, Heal, LoseHP, PlayerMaxHPUp, PlayerMaxHPDown,\n" +
                "PlayerAttackUp, PlayerAttackDown,\n" +
                "WeaponPenetrationUp, WeaponExtraProjectileUp,\n" +
                "WeaponBulletSizeUp, WeaponExplosionOnHit。\n\n" +
                "value范围：\n" +
                "LowVision 0.35~1.0\n" +
                "EnemyMoveSpeedUp 0.0~3.0\n" +
                "PlayerDealMoreDamage 0.0~3.0\n" +
                "PlayerReceiveMoreDamage 0.0~3.0\n" +
                "PlayerAttackSpeedUp 0.0~3.0\n" +
                "PlayerAttackSpeedDown 0.0~0.9\n" +
                "AllRoomsMonsterExceptBossAndSpawn 0\n" +
                "AllRoomsRewardExceptBossAndSpawn 0\n" +
                "GainExp 10~100整数\n" +
                "Heal 1~50整数\n" +
                "LoseHP 1~50整数\n" +
                "PlayerMaxHPUp 1~20整数\n" +
                "PlayerMaxHPDown 1~20整数\n" +
                "PlayerAttackUp 1~5整数\n" +
                "PlayerAttackDown 1~5整数\n" +
                "WeaponPenetrationUp 0.0~3.0\n" +
                "WeaponExtraProjectileUp 0.0~3.0\n" +
                "WeaponExplosionOnHit 0.0~3.0\n" +
                "WeaponBulletSizeUp 0.0~2.0\n\n" +
                "互斥（不可同时出现，跨数组也算）：\n" +
                "Heal+LoseHP\n" +
                "PlayerMaxHPUp+PlayerMaxHPDown\n" +
                "PlayerAttackUp+PlayerAttackDown\n" +
                "PlayerAttackSpeedUp+PlayerAttackSpeedDown\n" +
                "AllRoomsMonsterExceptBossAndSpawn+AllRoomsRewardExceptBossAndSpawn\n\n" +
                "允许叠加：\n" +
                "PlayerAttackUp 与 PlayerAttackSpeedUp 可同时存在（分别位于instant/next_floor）。";
        }

        string usr =
            "affinity=" + input.Affinity.ToString(CultureInfo.InvariantCulture) +
            "; floor=" + input.FloorIndex.ToString(CultureInfo.InvariantCulture) +
            "\n\nmemory:\n" + input.HistorySummary +
            "\nplayer:\n" + input.PlayerText;

        return new Prompts { System = sys, User = usr };
    }

    // ===================== API =====================

    private sealed class ApiCallResult
    {
        public int? HttpStatusCode;
        public string OuterRaw;
        public string ContentText;

        public long? PromptTokens;
        public long? CompletionTokens;
        public long? TotalTokens;

        public long LatencyMs;
    }

    private static HttpClient CreateHttpClient(string apiKey)
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return http;
    }

    private static async Task<ApiCallResult> CallDeepSeekAsync(
        HttpClient http,
        string model,
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        double temperature,
        ExperimentMode mode)
    {
        bool useJsonMode = mode != ExperimentMode.Prompt;

        string responseFormat = "";
        if (useJsonMode)
            responseFormat = "\"response_format\":{\"type\":\"json_object\"},";

        string body =
            "{" +
            "\"model\":\"" + JsonEscape(model) + "\"," +
            responseFormat +
            "\"max_tokens\":" + maxTokens.ToString(CultureInfo.InvariantCulture) + "," +
            "\"temperature\":" + temperature.ToString(CultureInfo.InvariantCulture) + "," +
            "\"messages\":[" +
                "{\"role\":\"system\",\"content\":\"" + JsonEscape(systemPrompt) + "\"}," +
                "{\"role\":\"user\",\"content\":\"" + JsonEscape(userPrompt) + "\"}" +
            "]" +
            "}";

        var sw = Stopwatch.StartNew();

        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds)))
        using (var req = new HttpRequestMessage(HttpMethod.Post, DeepSeekUrl))
        {
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using (var resp = await http.SendAsync(req, cts.Token))
            {
                string outer = await resp.Content.ReadAsStringAsync();
                sw.Stop();

                string content = ExtractContentField(outer);

                long? pt, ct, tt;
                ExtractUsageTokens(outer, out pt, out ct, out tt);

                return new ApiCallResult
                {
                    HttpStatusCode = (int)resp.StatusCode,
                    OuterRaw = outer,
                    ContentText = content,
                    PromptTokens = pt,
                    CompletionTokens = ct,
                    TotalTokens = tt,
                    LatencyMs = sw.ElapsedMilliseconds
                };
            }
        }
    }

    private static string ExtractContentField(string outer)
    {
        if (string.IsNullOrWhiteSpace(outer)) return null;

        int idx = outer.IndexOf("\"content\"", StringComparison.Ordinal);
        if (idx < 0) return null;

        idx = outer.IndexOf(':', idx);
        if (idx < 0) return null;
        idx++;

        while (idx < outer.Length && char.IsWhiteSpace(outer[idx])) idx++;
        if (idx >= outer.Length) return null;

        if (outer[idx] == '"')
        {
            idx++;
            var sb = new StringBuilder();
            bool esc = false;

            while (idx < outer.Length)
            {
                char c = outer[idx++];
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
                            if (idx + 4 <= outer.Length)
                            {
                                string hex = outer.Substring(idx, 4);
                                if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                    sb.Append((char)code);
                                idx += 4;
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

        if (outer[idx] == '{')
        {
            int start = idx;
            int depth = 0;
            bool inStr = false;
            bool esc2 = false;

            while (idx < outer.Length)
            {
                char c = outer[idx++];
                if (inStr)
                {
                    if (esc2) { esc2 = false; continue; }
                    if (c == '\\') { esc2 = true; continue; }
                    if (c == '"') { inStr = false; continue; }
                    continue;
                }
                if (c == '"') { inStr = true; continue; }
                if (c == '{') depth++;
                if (c == '}')
                {
                    depth--;
                    if (depth == 0) return outer.Substring(start, idx - start);
                }
            }
        }

        return null;
    }

    private static void ExtractUsageTokens(string outer, out long? prompt, out long? completion, out long? total)
    {
        prompt = null;
        completion = null;
        total = null;

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
        while (i < objJson.Length && char.IsDigit(objJson[i]))
        {
            i++;
            any = true;
        }

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

    // ===================== Compliance (Unified Evaluation) =====================

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

        try
        {
            var obj = root.AsObject();

            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "npc_reply", "affinity_delta", "next_floor_events", "instant_events", "history_event_summary_delta"
            };

            foreach (var k in obj.Keys)
            {
                if (!allowed.Contains(k))
                    return FailSchema("Extra field: " + k, eval);
            }

            string npcReply = obj.GetString("npc_reply", required: true);
            int affinityDelta = obj.GetInt("affinity_delta", required: true);
            var nextArr = obj.GetArray("next_floor_events", required: true);
            var instArr = obj.GetArray("instant_events", required: true);
            string hist = obj.GetStringAllowNull("history_event_summary_delta", required: true) ?? "";

            if (npcReply == null) return FailSchema("npc_reply missing", eval);
            if (npcReply.Length > NpcReplyMaxChars) return FailSchema("npc_reply too long", eval);

            if (affinityDelta < AffinityDeltaMin || affinityDelta > AffinityDeltaMax)
                return FailSchema("affinity_delta out of range: " + affinityDelta.ToString(CultureInfo.InvariantCulture), eval);

            if (nextArr.Count < NextFloorMin || nextArr.Count > NextFloorMax)
                return FailSchema("next_floor_events count out of range: " + nextArr.Count.ToString(CultureInfo.InvariantCulture), eval);

            if (instArr.Count < InstantMin || instArr.Count > InstantMax)
                return FailSchema("instant_events count out of range: " + instArr.Count.ToString(CultureInfo.InvariantCulture), eval);

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

        try
        {
            if (string.IsNullOrWhiteSpace(parsed.NpcReply))
                return FailSemantic("npc_reply empty", eval);

            foreach (var mk in LeakageMarkers)
            {
                if (parsed.NpcReply.IndexOf(mk, StringComparison.OrdinalIgnoreCase) >= 0)
                    return FailSemantic("npc_reply leakage marker: " + mk, eval);
            }

            foreach (var e in parsed.NextFloorEvents)
            {
                if (string.IsNullOrWhiteSpace(e.EventType)) return FailSemantic("next_floor_events empty eventType", eval);
                if (e.EventType == "None") return FailSemantic("eventType None forbidden", eval);
                if (!NextFloorWhitelist.Contains(e.EventType)) return FailSemantic("next_floor_events not whitelisted: " + e.EventType, eval);
                if (!IsValueValidForEvent(e.EventType, e.Value)) return FailSemantic("next_floor_events invalid value: " + e.EventType + "=" + e.Value.ToString(CultureInfo.InvariantCulture), eval);
            }

            foreach (var e in parsed.InstantEvents)
            {
                if (string.IsNullOrWhiteSpace(e.EventType)) return FailSemantic("instant_events empty eventType", eval);
                if (e.EventType == "None") return FailSemantic("eventType None forbidden", eval);
                if (!InstantWhitelist.Contains(e.EventType)) return FailSemantic("instant_events not whitelisted: " + e.EventType, eval);
                if (!IsValueValidForEvent(e.EventType, e.Value)) return FailSemantic("instant_events invalid value: " + e.EventType + "=" + e.Value.ToString(CultureInfo.InvariantCulture), eval);
            }

            var allTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in parsed.NextFloorEvents) allTypes.Add(e.EventType);
            foreach (var e in parsed.InstantEvents) allTypes.Add(e.EventType);

            foreach (var pair in Contradictions)
            {
                if (allTypes.Contains(pair.A) && allTypes.Contains(pair.B))
                    return FailSemantic("contradiction: " + pair.A + " with " + pair.B, eval);
            }

            if (HasDuplicates(parsed.NextFloorEvents))
                return FailSemantic("duplicate eventType in next_floor_events", eval);

            if (HasDuplicates(parsed.InstantEvents))
                return FailSemantic("duplicate eventType in instant_events", eval);

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
        {
            if (!set.Add(e.EventType)) return true;
        }
        return false;
    }

    private static List<EventItem> ParseEventArray(List<JsonValue> arr, string fieldName, EvalResult eval)
    {
        var list = new List<EventItem>();

        for (int i = 0; i < arr.Count; i++)
        {
            if (arr[i].Kind != JsonKind.Object)
                return FailSchemaEvent(fieldName, i, "item not object", eval);

            var obj = arr[i].AsObject();

            string et = obj.GetString("eventType", required: true);
            double v = obj.GetDouble("value", required: true);

            if (et == null)
                return FailSchemaEvent(fieldName, i, "eventType missing", eval);

            list.Add(new EventItem { EventType = et.Trim(), Value = v });
        }

        return list;
    }

    private static List<EventItem> FailSchemaEvent(string field, int idx, string reason, EvalResult eval)
    {
        eval.SchemaOk = false;
        eval.SemanticOk = false;
        eval.FailLayer = "Schema";
        eval.FailReason = field + "[" + idx.ToString(CultureInfo.InvariantCulture) + "]: " + reason;
        return null;
    }

    private static bool IsValueValidForEvent(string eventType, double value)
    {
        switch (eventType)
        {
            case "LowVision":
                return InRange(value, 0.35, 1.0);

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

    private static bool InRange(double v, double min, double max)
    {
        return v >= min - 1e-9 && v <= max + 1e-9;
    }

    private static bool IsIntegerish(double v)
    {
        return Math.Abs(v - Math.Round(v)) < 1e-9;
    }

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

    // ===================== CSV =====================

    private sealed class CsvRow
    {
        public int RunId;
        public string BatchId;
        public string ConfigTag;
        public string TimestampUtc;
        public string Model;
        public string Mode;
        public int RetryCount;
        public int LocalRunIndex;
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
        string header = string.Join(",",
            "run_id",
            "batch_id",
            "config_tag",
            "timestamp_utc",
            "model",
            "mode",
            "retry_count",
            "local_run_index",
            "seed",
            "affinity",
            "floor_index",
            "player_text",
            "history_summary",
            "latency_ms",
            "http_status",
            "prompt_tokens",
            "completion_tokens",
            "total_tokens",
            "parse_ok",
            "schema_ok",
            "semantic_ok",
            "all_ok",
            "fail_layer",
            "fail_reason",
            "npc_reply",
            "affinity_delta",
            "next_floor_count",
            "instant_count",
            "content_raw",
            "outer_raw"
        );

        if (!File.Exists(path))
        {
            File.WriteAllText(path, header + Environment.NewLine, new UTF8Encoding(true));
            return;
        }

        List<string[]> records = ReadCsvRecords(path);
        if (records.Count == 0)
        {
            File.WriteAllText(path, header + Environment.NewLine, new UTF8Encoding(true));
            return;
        }

        string existingHeader = string.Join(",", records[0]);
        if (!string.Equals(existingHeader, header, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Existing CSV header does not match current program version. " +
                "Please rename or delete the old llm_experiment_output.csv before running this version.");
        }
    }

    private static void AppendCsvRow(string path, CsvRow r)
    {
        string[] cols =
        {
            r.RunId.ToString(CultureInfo.InvariantCulture),
            r.BatchId ?? "",
            r.ConfigTag ?? "",
            r.TimestampUtc ?? "",
            r.Model ?? "",
            r.Mode ?? "",
            r.RetryCount.ToString(CultureInfo.InvariantCulture),
            r.LocalRunIndex.ToString(CultureInfo.InvariantCulture),
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

        public MiniJsonParser(string s)
        {
            _s = s;
            _i = 0;
        }

        public JsonValue ParseRootStrictObject()
        {
            SkipWs();
            var v = ParseValue();
            SkipWs();

            if (_i != _s.Length)
                throw new Exception("Trailing characters");

            if (v.Kind != JsonKind.Object)
                throw new Exception("Root not object");

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

            if (Peek('}'))
            {
                _i++;
                return new JsonValue { Kind = JsonKind.Object, Value = dict };
            }

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

                if (Peek('}'))
                {
                    _i++;
                    break;
                }

                Expect(',');
            }

            return new JsonValue { Kind = JsonKind.Object, Value = dict };
        }

        private JsonValue ParseArray()
        {
            Expect('[');
            SkipWs();

            var list = new List<JsonValue>();

            if (Peek(']'))
            {
                _i++;
                return new JsonValue { Kind = JsonKind.Array, Value = list };
            }

            while (true)
            {
                SkipWs();
                list.Add(ParseValue());
                SkipWs();

                if (Peek(']'))
                {
                    _i++;
                    break;
                }

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
            while (_i < _s.Length && char.IsDigit(_s[_i]))
            {
                _i++;
                hasDigits = true;
            }

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
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                {
                    _i++;
                    continue;
                }
                break;
            }
        }

        private void Expect(char c)
        {
            if (_i >= _s.Length || _s[_i] != c)
                throw new Exception("Expected: " + c);
            _i++;
        }

        private bool Peek(char c)
        {
            return _i < _s.Length && _s[_i] == c;
        }

        private bool Match(string lit)
        {
            if (_i + lit.Length > _s.Length) return false;

            for (int k = 0; k < lit.Length; k++)
            {
                if (_s[_i + k] != lit[k]) return false;
            }

            _i += lit.Length;
            return true;
        }
    }

    // ===================== Dictionary helpers =====================

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

    // ===================== Misc =====================

    private static string[] ParseModelsCsv(string csv)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(csv)) return list.ToArray();

        string[] parts = csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string m = (parts[i] ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(m))
                list.Add(m);
        }

        return list.ToArray();
    }

    private static string[] ToModeStrings(ExperimentMode[] modes)
    {
        var arr = new string[modes.Length];
        for (int i = 0; i < modes.Length; i++)
            arr[i] = modes[i].ToString();
        return arr;
    }
}