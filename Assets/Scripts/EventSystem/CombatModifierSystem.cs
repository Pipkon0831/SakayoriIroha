using UnityEngine;

public class CombatModifierSystem : MonoBehaviour
{
    public static CombatModifierSystem Instance { get; private set; }

    [Header("Player倍率（单层事件用）")]
    public float playerDamageMultiplier = 1f;        // 玩家造成伤害倍率
    public float playerReceiveDamageMultiplier = 1f; // 玩家受伤倍率
    public float playerAttackSpeedMultiplier = 1f;   // 玩家攻速倍率
    public float playerMoveSpeedMultiplier = 1f;     // 玩家移速倍率

    [Header("Enemy倍率（单层事件用）")]
    public float enemySpeedMultiplier = 1f;          // 敌人移速倍率

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

    public void ResetAll()
    {
        playerDamageMultiplier = 1f;
        playerReceiveDamageMultiplier = 1f;
        playerAttackSpeedMultiplier = 1f;
        playerMoveSpeedMultiplier = 1f;
        enemySpeedMultiplier = 1f;
    }
}