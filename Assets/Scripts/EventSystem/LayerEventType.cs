public enum LayerEventType
{
    None,

    // =========================
    // 单层事件（仅对下一层生效）
    // =========================
    LowVision,                           // 视野受限（缩小相机size倍率）
    EnemyMoveSpeedUp,                    // 怪物移动速度增大
    PlayerDealMoreDamage,                // 玩家造成伤害增加
    PlayerReceiveMoreDamage,             // 玩家受到伤害增加
    AllRoomsMonsterExceptBossAndSpawn,   // 下一层除Boss/出生外全怪物房
    AllRoomsRewardExceptBossAndSpawn,    // 下一层除Boss/出生外全奖励房
    PlayerAttackSpeedUp,                 // 玩家攻速提高
    PlayerAttackSpeedDown,               // 玩家攻速降低

    // =========================
    // 即时永久事件（一次性执行）
    // =========================
    GainExp,              // 获得经验（一次性）
    Heal,                 // 回复生命值（一次性）
    LoseHP,               // 扣除生命值（一次性）
    PlayerMaxHPUp,        // 提高最大生命值（一次性）
    PlayerMaxHPDown,      // 降低最大生命值（一次性）
    PlayerAttackUp,       // 提高玩家攻击力（一次性）
    PlayerAttackDown,     // 降低玩家攻击力（一次性）
}