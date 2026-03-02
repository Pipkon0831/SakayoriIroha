using System.Text;
using UnityEngine;

public class NPCRunFloorStats : MonoBehaviour
{
    public static NPCRunFloorStats Instance { get; private set; }

    [System.Serializable]
    public class FloorStats
    {
        public int floorIndex = -1;
        public float floorStartTime = 0f;

        // 生存/承伤
        public float damageTakenTotal = 0f;
        public int damageInstances = 0;
        public float maxSingleHit = 0f;

        // 治疗
        public float healedTotal = 0f;

        // 输出/命中
        public int shotsFired = 0;            // Attack 次数
        public int projectilesSpawned = 0;    // 子弹数量
        public int hitsOnEnemy = 0;           // 命中次数（按首次命中某敌人计）
        public int kills = 0;                 // 击杀数

        // ✅ 修复：Time.time 可能从 0.x 开始，不能用 floorStartTime<=0 判断
        public float DurationSeconds(float now) => Mathf.Max(0f, now - floorStartTime);
    }

    [Header("Snapshot")]
    [SerializeField] private FloorStats last = new FloorStats();
    [SerializeField] private FloorStats current = new FloorStats();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// 进入新层时调用：把 current 归档进 last，然后清空 current 并开始计时
    /// </summary>
    public void StartFloor(int floorIndex)
    {
        if (current.floorIndex >= 0)
            last = Clone(current);

        current = new FloorStats
        {
            floorIndex = floorIndex,
            floorStartTime = Time.time
        };
    }

    /// <summary>
    /// 在“下楼按钮点击那刻”强制把 current 冻结为 last
    /// </summary>
    public void FreezeCurrentAsLast()
    {
        if (current.floorIndex >= 0)
            last = Clone(current);
    }

    public void ResetAll()
    {
        last = new FloorStats();
        current = new FloorStats();
    }

    private static FloorStats Clone(FloorStats s) => new FloorStats
    {
        floorIndex = s.floorIndex,
        floorStartTime = s.floorStartTime,
        damageTakenTotal = s.damageTakenTotal,
        damageInstances = s.damageInstances,
        maxSingleHit = s.maxSingleHit,
        healedTotal = s.healedTotal,
        shotsFired = s.shotsFired,
        projectilesSpawned = s.projectilesSpawned,
        hitsOnEnemy = s.hitsOnEnemy,
        kills = s.kills
    };

    // -------------------------
    // Record APIs
    // -------------------------
    public void RecordDamageTaken(float amount)
    {
        if (amount <= 0f) return;
        current.damageTakenTotal += amount;
        current.damageInstances += 1;
        if (amount > current.maxSingleHit) current.maxSingleHit = amount;
    }

    public void RecordHealed(float amount)
    {
        if (amount <= 0f) return;
        current.healedTotal += amount;
    }

    /// <summary>
    /// 本次开火产生的子弹数量（霰弹/额外子弹会>1）
    /// </summary>
    public void RecordShotFired(int projectiles)
    {
        current.shotsFired += 1;
        current.projectilesSpawned += Mathf.Max(1, projectiles);
    }

    public void RecordHitEnemy(int hitCount = 1)
    {
        current.hitsOnEnemy += Mathf.Max(1, hitCount);
    }

    public void RecordKill(int killCount = 1)
    {
        current.kills += Mathf.Max(1, killCount);
    }

    // -------------------------
    // LLM Summary
    // -------------------------
    public string BuildLastFloorSummaryForLLM()
    {
        if (last.floorIndex < 0)
            return "【上一层表现】（暂无：可能还没结算过上一层）";

        float now = Time.time;
        float dur = last.DurationSeconds(now);

        float accByShots = (last.shotsFired <= 0) ? 0f : (float)last.hitsOnEnemy / last.shotsFired;
        float accByProj = (last.projectilesSpawned <= 0) ? 0f : (float)last.hitsOnEnemy / last.projectilesSpawned;

        var sb = new StringBuilder(256);
        sb.AppendLine($"【上一层表现（Floor {last.floorIndex}）】");
        sb.AppendLine($"- 用时：{dur:0.#}s");
        sb.AppendLine($"- 承伤：{last.damageTakenTotal:0.#}（{last.damageInstances}次，最大单次 {last.maxSingleHit:0.#}）");
        if (last.healedTotal > 0f) sb.AppendLine($"- 治疗：{last.healedTotal:0.#}");
        sb.AppendLine($"- 击杀：{last.kills}；命中：{last.hitsOnEnemy}");
        sb.AppendLine($"- 开火：{last.shotsFired}次；子弹：{last.projectilesSpawned}");
        sb.AppendLine($"- 命中率：按开火 {accByShots:P0} / 按子弹 {accByProj:P0}");
        sb.AppendLine("口径：把它当作我们刚经历完上一层的结果，你可以自然吐槽/夸奖/担心地引用它，但别像播报系统报表。");
        return sb.ToString();
    }
}