using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class NPCRunEventMemory : MonoBehaviour
{
    public static NPCRunEventMemory Instance { get; private set; }

    [Header("Avoid repetition window")]
    [SerializeField] private int recentWindow = 8;

    private readonly HashSet<LayerEventType> _ever = new HashSet<LayerEventType>();
    private readonly Queue<LayerEventType> _recent = new Queue<LayerEventType>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void ClearRunMemory()
    {
        _ever.Clear();
        _recent.Clear();
    }

    public void RecordEvents(IEnumerable<LayerEvent> events)
    {
        if (events == null) return;
        foreach (var e in events)
        {
            if (e == null) continue;
            RecordType(e.eventType);
        }
    }

    public void RecordType(LayerEventType type)
    {
        if (type == LayerEventType.None) return;

        _ever.Add(type);
        _recent.Enqueue(type);
        while (_recent.Count > recentWindow) _recent.Dequeue();
    }

    public bool IsRecent(LayerEventType type)
    {
        foreach (var t in _recent)
            if (t == type) return true;
        return false;
    }

    public string BuildForLLM()
    {
        var sb = new StringBuilder(512);
        sb.AppendLine("【本局事件记忆（用于避免重复）】");

        if (_ever.Count == 0)
        {
            sb.AppendLine("- 尚未结算任何事件。");
            return sb.ToString();
        }

        sb.Append("- 最近事件：");
        if (_recent.Count == 0) sb.AppendLine("（无）");
        else
        {
            bool first = true;
            foreach (var t in _recent)
            {
                if (!first) sb.Append("、");
                sb.Append(t);
                first = false;
            }
            sb.AppendLine();
        }

        sb.Append("- 本局已出现（去重）：");
        int c = 0;
        foreach (var t in _ever)
        {
            if (c++ > 0) sb.Append("、");
            sb.Append(t);
        }
        sb.AppendLine();

        sb.AppendLine("规则：尽量避免选择“最近事件”中的同类事件；若必须重复，必须换数值/换方向。");
        return sb.ToString();
    }
}