[System.Serializable]
public class LayerEvent
{
    public LayerEventType eventType;
    public float value;
    public bool isPersistent; // ✅ 新增，标记长期事件

    // 构造函数
    public LayerEvent(LayerEventType type, float value = 0f, bool isPersistent = false)
    {
        this.eventType = type;
        this.value = value;
        this.isPersistent = isPersistent;
    }
}