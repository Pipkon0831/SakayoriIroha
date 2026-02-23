using UnityEngine;
using System.Collections.Generic;

public class LLMEventBridge : MonoBehaviour
{
    public static LLMEventBridge Instance;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    // 模拟生成下一层事件
    public void SimulateLLMDecision()
    {
        Debug.Log("[LLM] 模拟生成下一层事件");

        List<LayerEvent> generatedEvents = new List<LayerEvent>();

        // 临时事件示例
        generatedEvents.Add(new LayerEvent(LayerEventType.EnemySpeedUp, 1f, false));
        // 长期事件示例
        generatedEvents.Add(new LayerEvent(LayerEventType.AttackSpeedUp, 0.1f, true));

        ApplyLLMEvents(generatedEvents);
    }

    private void ApplyLLMEvents(List<LayerEvent> events)
    {
        foreach (var e in events)
        {
            LayerEventSystem.Instance.RegisterEvent(e.eventType, e.value, e.isPersistent);
        }
    }
}