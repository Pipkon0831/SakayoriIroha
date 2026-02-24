using UnityEngine;

/// <summary>
/// 负责把 LLM（或模拟）输出转成游戏可用的事件，并写入 LayerEventSystem：
/// - nextFloorEvents：单层事件（倍率/视野/房间覆盖）
/// - instantEvents：一次性事件（经验/回血/永久属性加减）
/// </summary>
public class LLMEventBridge : MonoBehaviour
{
    public static LLMEventBridge Instance { get; private set; }

    [Header("好感度（后续接LLM输出）")]
    [SerializeField] private int affinity = 0;
    public int Affinity => affinity;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// ✅ 模拟：生成下一层事件 + 一次性事件（用于你现在没接UI/没接LLM接口时）
    /// 你可以在 Boss 清完后调用它，或开局第一次与NPC对话时调用它。
    /// </summary>
    public void SimulateLLMDecision()
    {
        if (LayerEventSystem.Instance == null)
        {
            Debug.LogWarning("[LLM] LayerEventSystem 未就绪，无法写入事件。");
            return;
        }

        Debug.Log("[LLM] 模拟生成下一层事件 + 一次性事件");

        // -----------------------------
        // 1) 清空旧的 nextFloorEvents（避免叠加）
        // -----------------------------
        LayerEventSystem.Instance.ClearNextFloorEvents();

        // -----------------------------
        // 2) 写入 nextFloorEvents（单层事件）
        // value 约定：
        // - 倍率类用“比例”：0.2 => +20%
        // - LowVision 用“倍率”：0.6 => 视野缩到60%
        // -----------------------------

        // 示例：怪物移速 +20%
        LayerEventSystem.Instance.AddNextFloorEvent(LayerEventType.EnemyMoveSpeedUp, 0.2f);

        // 示例：玩家造成伤害 +30%
        LayerEventSystem.Instance.AddNextFloorEvent(LayerEventType.PlayerDealMoreDamage, 0.3f);

        // 示例：视野受限（缩到 60%）
        LayerEventSystem.Instance.AddNextFloorEvent(LayerEventType.LowVision, 0.6f);

        // 示例：下一层全怪物房（除Spawn/Boss）
        // LayerEventSystem.Instance.AddNextFloorEvent(LayerEventType.AllRoomsMonsterExceptBossAndSpawn, 0f);

        // 示例：玩家攻速提高 +25%
        LayerEventSystem.Instance.AddNextFloorEvent(LayerEventType.PlayerAttackSpeedUp, 0.25f);

        // -----------------------------
        // 3) 写入 instantEvents（一次性事件）
        // 这些事件应该在 Boss 对话确认后立刻执行（并被 Consume 清空）
        // -----------------------------

        // 示例：回血 15
        LayerEventSystem.Instance.AddInstantEvent(LayerEventType.Heal, 15f);

        // 示例：获得经验 30
        LayerEventSystem.Instance.AddInstantEvent(LayerEventType.GainExp, 30f);

        // 示例：永久攻击 +2
        LayerEventSystem.Instance.AddInstantEvent(LayerEventType.PlayerAttackUp, 2f);

        // 示例：永久最大生命 +10
        // LayerEventSystem.Instance.AddInstantEvent(LayerEventType.PlayerMaxHPUp, 10f);

        // -----------------------------
        // 4) 好感度变化（示例）
        // -----------------------------
        ApplyAffinityDelta(+1);

        Debug.Log($"[LLM] 模拟完成：Affinity={affinity}，nextFloorEvents={LayerEventSystem.Instance.GetNextFloorEventsSnapshot().Count}");
    }

    /// <summary>
    /// 好感度变化（后续由LLM输出 delta 控制）
    /// </summary>
    public void ApplyAffinityDelta(int delta)
    {
        affinity += delta;
        affinity = Mathf.Clamp(affinity, -100, 100);
    }
}