using UnityEngine;

public class NPCRunPersonalityManager : MonoBehaviour
{
    public static NPCRunPersonalityManager Instance { get; private set; }

    [Header("Pool (assign in Inspector)")]
    [SerializeField] private NPCPersonalityDefinition[] personalityPool;

    [Header("Selected (runtime)")]
    [SerializeField] private NPCPersonalityDefinition selected;

    public NPCPersonalityDefinition Selected => selected;

    private bool _picked;

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

    public void EnsurePicked()
    {
        if (_picked) return;
        _picked = true;

        if (personalityPool == null || personalityPool.Length == 0)
        {
            Debug.LogWarning("[NPC Personality] Pool is empty. Selected = null.");
            selected = null;
            return;
        }

        selected = personalityPool[Random.Range(0, personalityPool.Length)];
        Debug.Log($"[NPC Personality] Picked: {(selected != null ? selected.displayName : "null")}");
    }
}