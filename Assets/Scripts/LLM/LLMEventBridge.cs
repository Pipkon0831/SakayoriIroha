using UnityEngine;

public class LLMEventBridge : MonoBehaviour
{
    public static LLMEventBridge Instance { get; private set; }

    [Header("好感度（由LLM输出 delta 控制）")]
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

    public void ApplyAffinityDelta(int delta)
    {
        affinity += delta;
        affinity = Mathf.Clamp(affinity, -100, 100);
    }
}