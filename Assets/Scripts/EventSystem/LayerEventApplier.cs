using UnityEngine;
using System.Collections.Generic;

public class LayerEventApplier : MonoBehaviour
{
    private CombatModifierSystem combatSystem;
    private PlayerController player;
    private RoomFirstDungeonGenerator dungeonGenerator;

    private void Awake()
    {
        combatSystem = CombatModifierSystem.Instance;
        player = FindObjectOfType<PlayerController>();
        dungeonGenerator = FindObjectOfType<RoomFirstDungeonGenerator>();
    }

    public void ApplyCurrentFloorEvents()
    {
        if (LayerEventSystem.Instance == null) return;

        if (combatSystem == null) combatSystem = CombatModifierSystem.Instance;
        if (player == null) player = FindObjectOfType<PlayerController>();
        if (dungeonGenerator == null) dungeonGenerator = FindObjectOfType<RoomFirstDungeonGenerator>();

        combatSystem.ResetAll();

        if (CameraController.Instance != null)
            CameraController.Instance.ResetOrthoSize();

        if (dungeonGenerator != null)
            dungeonGenerator.SetRoomOverrideMode(RoomOverrideMode.None);

        List<LayerEvent> events = LayerEventSystem.Instance.GetCurrentFloorEvents();
        foreach (var e in events)
        {
            ApplySingleFloorEvent(e);
        }
    }

    private void ApplySingleFloorEvent(LayerEvent e)
    {
        switch (e.eventType)
        {
            case LayerEventType.LowVision:
            {
                float m = Mathf.Clamp(e.value, 0.35f, 1f);
                if (CameraController.Instance != null)
                    CameraController.Instance.SetOrthoSizeMultiplier(m);
                break;
            }

            case LayerEventType.EnemyMoveSpeedUp:
            {
                float ratio = Mathf.Clamp(e.value, 0f, 3f);
                combatSystem.enemySpeedMultiplier *= (1f + ratio);
                break;
            }

            case LayerEventType.PlayerDealMoreDamage:
            {
                float ratio = Mathf.Clamp(e.value, 0f, 3f);
                combatSystem.playerDamageMultiplier *= (1f + ratio);
                break;
            }

            case LayerEventType.PlayerReceiveMoreDamage:
            {
                float ratio = Mathf.Clamp(e.value, 0f, 3f);
                combatSystem.playerReceiveDamageMultiplier *= (1f + ratio);
                break;
            }

            case LayerEventType.PlayerAttackSpeedUp:
            {
                float ratio = Mathf.Clamp(e.value, 0f, 3f);
                combatSystem.playerAttackSpeedMultiplier *= (1f + ratio);
                break;
            }

            case LayerEventType.PlayerAttackSpeedDown:
            {
                float ratio = Mathf.Clamp(e.value, 0f, 0.9f);
                combatSystem.playerAttackSpeedMultiplier *= (1f - ratio);
                combatSystem.playerAttackSpeedMultiplier = Mathf.Max(0.25f, combatSystem.playerAttackSpeedMultiplier);
                break;
            }

            case LayerEventType.AllRoomsMonsterExceptBossAndSpawn:
            {
                if (dungeonGenerator != null)
                    dungeonGenerator.SetRoomOverrideMode(RoomOverrideMode.AllMonsterExceptBossAndSpawn);
                break;
            }

            case LayerEventType.AllRoomsRewardExceptBossAndSpawn:
            {
                if (dungeonGenerator != null)
                    dungeonGenerator.SetRoomOverrideMode(RoomOverrideMode.AllRewardExceptBossAndSpawn);
                break;
            }
        }
    }

    public void ApplyAndConsumeInstantEvents()
    {
        if (LayerEventSystem.Instance == null) return;

        if (player == null) player = FindObjectOfType<PlayerController>();

        List<LayerEvent> instants = LayerEventSystem.Instance.ConsumeInstantEvents();
        foreach (var e in instants)
        {
            ApplyInstantEvent(e);
        }
    }

    private void ApplyInstantEvent(LayerEvent e)
    {
        if (player == null) return;

        switch (e.eventType)
        {
            case LayerEventType.GainExp:
                player.AddExp(e.value);
                break;

            case LayerEventType.Heal:
                player.HealHP(e.value);
                break;

            case LayerEventType.LoseHP:
                player.TakeDamageDirect(e.value);
                break;

            case LayerEventType.PlayerMaxHPUp:
                player.AddMaxHP(e.value);
                break;

            case LayerEventType.PlayerMaxHPDown:
                player.AddMaxHP(-e.value);
                break;

            case LayerEventType.PlayerAttackUp:
                player.AddAttack(e.value);
                break;

            case LayerEventType.PlayerAttackDown:
                player.AddAttack(-e.value);
                break;

        }
    }
}