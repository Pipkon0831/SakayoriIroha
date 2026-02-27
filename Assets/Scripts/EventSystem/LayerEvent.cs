using System;

[Serializable]
public class LayerEvent
{
    public LayerEventType eventType;
    public float value;

    public LayerEvent(LayerEventType eventType, float value = 0f)
    {
        this.eventType = eventType;
        this.value = value;
    }

    public override string ToString()
    {
        return $"{eventType} ({value})";
    }
}