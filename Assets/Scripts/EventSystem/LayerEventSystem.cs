using System.Collections.Generic;
using UnityEngine;

public class LayerEventSystem : MonoBehaviour
{
    public static LayerEventSystem Instance { get; private set; }

    private readonly List<LayerEvent> nextFloorEvents = new List<LayerEvent>();

    private readonly List<LayerEvent> currentFloorEvents = new List<LayerEvent>();

    private readonly List<LayerEvent> instantEvents = new List<LayerEvent>();

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


    public void ClearNextFloorEvents()
    {
        nextFloorEvents.Clear();
    }

    public void AddNextFloorEvent(LayerEventType type, float value = 0f)
    {
        nextFloorEvents.Add(new LayerEvent(type, value));
    }

    public void AddNextFloorEvents(IEnumerable<LayerEvent> eventsToAdd)
    {
        if (eventsToAdd == null) return;
        nextFloorEvents.AddRange(eventsToAdd);
    }

    public List<LayerEvent> GetNextFloorEventsSnapshot()
    {
        return new List<LayerEvent>(nextFloorEvents);
    }

    public void CommitNextFloorToCurrent()
    {
        currentFloorEvents.Clear();
        currentFloorEvents.AddRange(nextFloorEvents);
        nextFloorEvents.Clear();
    }

    public List<LayerEvent> GetCurrentFloorEvents()
    {
        return new List<LayerEvent>(currentFloorEvents);
    }

    public void ClearCurrentFloorEvents()
    {
        currentFloorEvents.Clear();
    }

    public void AddInstantEvent(LayerEventType type, float value = 0f)
    {
        instantEvents.Add(new LayerEvent(type, value));
    }

    public void AddInstantEvents(IEnumerable<LayerEvent> eventsToAdd)
    {
        if (eventsToAdd == null) return;
        instantEvents.AddRange(eventsToAdd);
    }

    public List<LayerEvent> ConsumeInstantEvents()
    {
        var snapshot = new List<LayerEvent>(instantEvents);
        instantEvents.Clear();
        return snapshot;
    }

    public void OnFloorEnd()
    {
        currentFloorEvents.Clear();
    }
}