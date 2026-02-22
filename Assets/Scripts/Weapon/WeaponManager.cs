using UnityEngine;
using System.Collections.Generic;

public class WeaponManager : MonoBehaviour
{
    [Header("武器配置")]
    [SerializeField] private List<BaseWeapon> allWeapons; // 所有武器列表
    [SerializeField] private int defaultWeaponIndex = 0; // 默认激活的武器

    private PlayerController player;
    private int currentWeaponIndex;

    /// <summary>
    /// 初始化武器管理器（由PlayerController调用）
    /// </summary>
    /// <param name="playerRef">玩家引用</param>
    public void InitWeaponManager(PlayerController playerRef)
    {
        if (playerRef == null)
        {
            Debug.LogError("[WeaponManager] 传入的PlayerController引用为空！");
            return;
        }
        
        player = playerRef;
        currentWeaponIndex = defaultWeaponIndex;

        foreach (var weapon in allWeapons)
        {
            if (weapon != null)
            {
                weapon.InitWeapon(player); // 关键：把玩家引用传递给武器
            }
            else
            {
                Debug.LogWarning("[WeaponManager] 武器列表中有空引用！");
            }
        }

        if (allWeapons.Count > 0 && allWeapons[defaultWeaponIndex] != null)
        {
            SwitchWeapon(defaultWeaponIndex);
        }
        else
        {
            Debug.LogError("[WeaponManager] 默认武器索引无效或为空！");
        }

        InvokeRepeating(nameof(CheckWeaponSwitch), 0, 0.1f);
    }

    /// <summary>
    /// 检测武器切换输入（1-5键）
    /// </summary>
    private void CheckWeaponSwitch()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) SwitchWeapon(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SwitchWeapon(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SwitchWeapon(2);
        if (Input.GetKeyDown(KeyCode.Alpha4)) SwitchWeapon(3);
        if (Input.GetKeyDown(KeyCode.Alpha5)) SwitchWeapon(4);
    }

    /// <summary>
    /// 切换武器
    /// </summary>
    /// <param name="index">武器索引</param>
    public void SwitchWeapon(int index)
    {
        if (index < 0 || index >= allWeapons.Count || allWeapons[index] == null) 
        {
            Debug.LogWarning($"[WeaponManager] 无法切换到索引{index}的武器：索引无效或为空");
            return;
        }

        if (allWeapons[currentWeaponIndex] != null)
        {
            allWeapons[currentWeaponIndex].SetActive(false);
        }
        currentWeaponIndex = index;
        allWeapons[currentWeaponIndex].SetActive(true);

        Debug.Log($"切换武器：{allWeapons[currentWeaponIndex].weaponConfig.weaponType}");
    }

    /// <summary>
    /// 传递瞄准方向给当前武器
    /// </summary>
    public void SetAimDirection(Vector2 dir)
    {
        // 修复：改用公开属性IsActive访问
        if (allWeapons.Count == 0 || currentWeaponIndex >= allWeapons.Count || allWeapons[currentWeaponIndex] == null) return;
        
        if (allWeapons[currentWeaponIndex].IsActive) // 关键修改：从isActive → IsActive
        {
            allWeapons[currentWeaponIndex].SetAimDirection(dir);
        }
    }

    // 编辑器验证
    private void OnValidate()
    {
        if (player == null)
        {
            player = FindObjectOfType<PlayerController>();
        }
    }
}