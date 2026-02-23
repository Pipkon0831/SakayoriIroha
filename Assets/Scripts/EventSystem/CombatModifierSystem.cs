using UnityEngine;

public class CombatModifierSystem : MonoBehaviour
{
    public static CombatModifierSystem Instance;

    [Header("玩家倍率")]
    public float playerDamageMultiplier = 1f;
    public float playerAttackSpeedMultiplier = 1f;
    public float playerReceiveDamageMultiplier = 1f;

    [Header("敌人倍率")]
    public float enemyMoveSpeedMultiplier = 1f;
    public float enemyDamageMultiplier = 1f;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    public void ResetAll()
    {
        playerDamageMultiplier = 1f;
        playerAttackSpeedMultiplier = 1f;
        playerReceiveDamageMultiplier = 1f;
        enemyMoveSpeedMultiplier = 1f;
        enemyDamageMultiplier = 1f;
    }
}