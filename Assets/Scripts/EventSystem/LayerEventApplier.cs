using UnityEngine;
using System.Collections.Generic;

public class LayerEventApplier : MonoBehaviour
{
    private CombatModifierSystem combatSystem;
    private PlayerController player;

    private void Awake()
    {
        combatSystem = CombatModifierSystem.Instance;
        player = FindObjectOfType<PlayerController>();
    }

    /// <summary>应用所有事件的效果（临时+长期）</summary>
    public void ApplyAllEvents()
    {
        List<LayerEvent> events = LayerEventSystem.Instance.GetAllActiveEvents();

        // 先重置倍率（临时事件每层重新应用）
        combatSystem.ResetAll();

        foreach (var e in events)
        {
            ApplyEvent(e);
        }
    }

    private void ApplyEvent(LayerEvent e)
    {
        switch (e.eventType)
        {
            // 正面临时效果
            case LayerEventType.AttackPowerUp:
                if (e.isPersistent)
                    player.AddAttack(e.value);
                else
                    combatSystem.playerDamageMultiplier += e.value;
                break;

            case LayerEventType.MaxHPUp:
                if (e.isPersistent)
                    player.AddMaxHP(e.value);
                else
                    combatSystem.playerReceiveDamageMultiplier -= e.value;
                break;

            case LayerEventType.Heal:
                player.HealHP(e.value);
                break;

            case LayerEventType.LoseHP:
                player.TakeDamageDirect(e.value);
                break;
            case LayerEventType.GainExp:
                player.AddExp(e.value);
                break;

            // 负面临时效果
            case LayerEventType.EnemySpeedUp:
                combatSystem.enemyMoveSpeedMultiplier += e.value;
                break;

            case LayerEventType.PlayerReceiveMoreDamage:
                combatSystem.playerReceiveDamageMultiplier += e.value;
                break;

            // 特殊事件可在这里扩展
        }
    }
}