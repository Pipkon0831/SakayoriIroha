using UnityEngine;

[System.Serializable]
public class FloorModifierData
{
    public bool isBossRush = false;
    public bool limitedVision = false;

    public float playerDamageMultiplier = 1f;
    public float playerAttackSpeedMultiplier = 1f;

    public float enemySpeedMultiplier = 1f;
    public float damageTakenMultiplier = 1f;

    public int bonusExp = 0;
}