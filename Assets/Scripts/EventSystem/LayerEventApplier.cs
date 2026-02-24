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
                // value: 0.35~1.0（倍率）
                float m = Mathf.Clamp(e.value, 0.35f, 1f);
                if (CameraController.Instance != null)
                    CameraController.Instance.SetOrthoSizeMultiplier(m);
                break;
            }

            case LayerEventType.EnemyMoveSpeedUp:
            {
                // value: 0.2 => 速度 * 1.2
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
                // value: 0.2 => 攻速 * 0.8
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

            // ========= 其余类型（一次性/不该出现在单层应用里） =========
            default:
                // 安全忽略，避免混用导致重复执行
                break;
        }
    }

    // =========================
    // 入口 2：应用并清空一次性事件（经验/回血/永久属性加减）
    // =========================
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
            // ========= 一次性永久事件 =========
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