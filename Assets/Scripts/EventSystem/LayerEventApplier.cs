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

        // =========================
        // 武器提升（即时永久）
        // =========================
        case LayerEventType.WeaponPenetrationUp:
        {
            var ws = player.GetComponent<WeaponUpgradeState>();
            if (ws == null) ws = player.gameObject.AddComponent<WeaponUpgradeState>();

            // e.value：如果LLM给0，就按+1；如果给>=1，就按该数值取整
            int add = (e.value <= 0f) ? 1 : Mathf.RoundToInt(e.value);
            ws.AddPenetration(add);

            // 可选上限（防炸）
            ws.Mods.penetrateCount = Mathf.Min(ws.Mods.penetrateCount, 5);
            break;
        }

        case LayerEventType.WeaponExtraProjectileUp:
        {
            var ws = player.GetComponent<WeaponUpgradeState>();
            if (ws == null) ws = player.gameObject.AddComponent<WeaponUpgradeState>();

            int add = (e.value <= 0f) ? 1 : Mathf.RoundToInt(e.value);
            ws.AddExtraProjectiles(add);

            ws.Mods.extraProjectiles = Mathf.Min(ws.Mods.extraProjectiles, 6);
            break;
        }

        case LayerEventType.WeaponBulletSizeUp:
        {
            var ws = player.GetComponent<WeaponUpgradeState>();
            if (ws == null) ws = player.gameObject.AddComponent<WeaponUpgradeState>();

            // e.value：倍率。若没给/给0，则默认1.2
            float mul = (e.value <= 0.01f) ? 1.2f : Mathf.Clamp(e.value, 1.05f, 2.0f);
            ws.MultiplyBulletSize(mul);

            ws.Mods.bulletSizeMultiplier = Mathf.Min(ws.Mods.bulletSizeMultiplier, 3f);
            break;
        }

        case LayerEventType.WeaponExplosionOnHit:
        {
            var ws = player.GetComponent<WeaponUpgradeState>();
            if (ws == null) ws = player.gameObject.AddComponent<WeaponUpgradeState>();

            // e.value：这里用作半径；如果没给就默认1.5
            float radius = (e.value <= 0.01f) ? 1.5f : Mathf.Clamp(e.value, 0.8f, 3.0f);

            // 爆炸开关型：重复抽到不叠加（避免数值失控）
            if (!ws.Mods.explodeOnHit)
                ws.EnableExplosion(radius, 0.6f);

            break;
        }
    }
}
}