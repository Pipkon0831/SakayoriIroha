using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Reward Pool SO", fileName = "RewardPoolSO")]
public class RewardPoolSO : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public WeaponUpgradeSO upgrade;
        [Min(0f)] public float weight = 1f;
    }

    public List<Entry> entries = new List<Entry>();

    /// <summary>
    /// 随机抽取一个Upgrade（按权重）。返回null表示池子不可用。
    /// </summary>
    public WeaponUpgradeSO Roll()
    {
        if (entries == null || entries.Count == 0) return null;

        float total = 0f;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null || e.upgrade == null) continue;
            if (e.weight <= 0f) continue;
            total += e.weight;
        }

        if (total <= 0f) return null;

        float r = UnityEngine.Random.Range(0f, total);
        float acc = 0f;

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null || e.upgrade == null) continue;
            if (e.weight <= 0f) continue;

            acc += e.weight;
            if (r <= acc)
                return e.upgrade;
        }

        // 理论不会到这里，兜底返回最后一个有效项
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            if (e != null && e.upgrade != null && e.weight > 0f)
                return e.upgrade;
        }
        return null;
    }
}