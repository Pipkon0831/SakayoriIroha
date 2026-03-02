using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class RewardPickup : MonoBehaviour
{
    [Header("奖励池（SO）")]
    public RewardPoolSO rewardPool;

    [Header("拾取后是否销毁")]
    public bool destroyOnPickup = true;

    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        var state = other.GetComponent<WeaponUpgradeState>();
        if (state == null)
        {
            // 不想自动加也行，你可以改成报错并return
            state = other.gameObject.AddComponent<WeaponUpgradeState>();
        }

        if (rewardPool == null)
        {
            Debug.LogWarning("[RewardPickup] 未设置rewardPool。");
            return;
        }

        var up = rewardPool.Roll();
        if (up == null)
        {
            Debug.LogWarning("[RewardPickup] rewardPool抽取结果为空（池子可能没配置或权重全为0）。");
            return;
        }

        ApplyUpgrade(state, up);

        if (destroyOnPickup) Destroy(gameObject);
    }

    private void ApplyUpgrade(WeaponUpgradeState state, WeaponUpgradeSO up)
    {
        // 这里复用你之前 WeaponUpgradePickup 的逻辑即可
        switch (up.type)
        {
            case WeaponUpgradeType.Penetration:
                state.AddPenetration(up.intValue);
                break;

            case WeaponUpgradeType.ExtraProjectiles:
                float? overrideAngle = (up.floatValue2 > 0f) ? up.floatValue2 : (float?)null;
                state.AddExtraProjectiles(up.intValue, overrideAngle);
                break;

            case WeaponUpgradeType.BulletSize:
                state.MultiplyBulletSize(Mathf.Max(0.01f, up.floatValue));
                break;

            case WeaponUpgradeType.ExplosionOnHit:
                state.EnableExplosion(up.floatValue2, up.floatValue3);
                break;
        }

        // 可选：打log看抽到了什么
        // Debug.Log($"[RewardPickup] 获得升级：{up.name} | {up.desc}");
    }
}