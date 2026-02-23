using System.Collections.Generic;
using UnityEngine;

public class LayerEventSystem : MonoBehaviour
{
    public static LayerEventSystem Instance;

    private List<LayerEvent> activeEvents = new List<LayerEvent>();
    private List<LayerEvent> persistentEvents = new List<LayerEvent>(); // ✅ 长期生效事件

    private GameController gameController;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    private void Start()
    {
        gameController = FindObjectOfType<GameController>();
    }

    /// <summary>注册事件，可以选择长期生效</summary>
    public void RegisterEvent(LayerEventType type, float value = 0f, bool isPersistent = false)
    {
        LayerEvent newEvent = new LayerEvent(type, value, isPersistent);

        if (isPersistent)
        {
            persistentEvents.Add(newEvent);
            Debug.Log($"[LayerEvent] 注册长期事件: {type}");
        }
        else
        {
            activeEvents.Add(newEvent);
            Debug.Log($"[LayerEvent] 注册临时事件: {type}");
        }
    }

    /// <summary>层开始时调用，清空临时事件，保留长期事件</summary>
    public void OnNewFloorStart()
    {
        Debug.Log("[LayerEvent] 新层开始，清空临时事件，保留长期事件");
        activeEvents.Clear();
    }

    /// <summary>层结束时调用，清空临时事件，长期事件保留</summary>
    public void OnFloorEnd()
    {
        Debug.Log("[LayerEvent] 层结束，清空临时事件");
        activeEvents.Clear();
    }

    /// <summary>获取当前所有事件（临时+长期）</summary>
    public List<LayerEvent> GetAllActiveEvents()
    {
        List<LayerEvent> combined = new List<LayerEvent>();
        combined.AddRange(persistentEvents);
        combined.AddRange(activeEvents);
        return combined;
    }

    /// <summary>清空所有事件（包括长期）</summary>
    public void ClearAllEvents()
    {
        activeEvents.Clear();
        persistentEvents.Clear();
    }
}