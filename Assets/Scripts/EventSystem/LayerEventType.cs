public enum LayerEventType
{
    None,

    // 正面
    AttackSpeedUp,       // 攻击速度提升
    Heal,                // 恢复血量
    GainExp,             // 获得经验
    AttackPowerUp,       // 攻击力提升
    MaxHPUp,             // 最大血量提升

    // 负面
    EnemySpeedUp,
    PlayerReceiveMoreDamage,
    LoseHP,

    // 特殊
    BossRush,
    LowVision
}