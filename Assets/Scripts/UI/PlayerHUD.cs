using UnityEngine;
using TMPro;

public class PlayerHUD : MonoBehaviour
{
    public static PlayerHUD Instance { get; private set; }

    [Header("TMP 文本")]
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private TextMeshProUGUI expText;
    [SerializeField] private TextMeshProUGUI levelText;

    [Header("玩家")]
    [SerializeField] private PlayerController player;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        if (player == null)
            player = FindObjectOfType<PlayerController>();

        UpdateAllUI();
    }

    private void Update()
    {
        UpdateAllUI();
    }

    public void UpdateAllUI()
    {
        if (player == null) return;

        hpText.text = $"HP: {player.CurrentHP:F0} / {player.MaxHP:F0}";
        expText.text = $"EXP: {player.CurrentExp:F0} / {player.ExpToNextLevel:F0}";
        levelText.text = $"Lv. {player.Level}";
    }
}