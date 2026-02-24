using UnityEngine;
using System.Collections.Generic;

public class WeaponManager : MonoBehaviour
{
    [Header("武器配置")]
    [SerializeField] private List<BaseWeapon> allWeapons;
    [SerializeField] private int defaultWeaponIndex = 0;

    [Header("输入开关（Boss对话UI时可关闭）")]
    public bool inputEnabled = true;

    private PlayerController player;
    private int currentWeaponIndex;

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
            if (weapon != null) weapon.InitWeapon(player);
            else Debug.LogWarning("[WeaponManager] 武器列表中有空引用！");
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

    private void CheckWeaponSwitch()
    {
        if (!inputEnabled) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) SwitchWeapon(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SwitchWeapon(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SwitchWeapon(2);
        if (Input.GetKeyDown(KeyCode.Alpha4)) SwitchWeapon(3);
        if (Input.GetKeyDown(KeyCode.Alpha5)) SwitchWeapon(4);
    }

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

        //Debug.Log($"切换武器：{allWeapons[currentWeaponIndex].weaponConfig.weaponType}");
    }

    public void SetAimDirection(Vector2 dir)
    {
        if (allWeapons.Count == 0 || currentWeaponIndex >= allWeapons.Count || allWeapons[currentWeaponIndex] == null) return;

        if (allWeapons[currentWeaponIndex].IsActive)
        {
            allWeapons[currentWeaponIndex].SetAimDirection(dir);
        }
    }

    private void OnValidate()
    {
        if (player == null)
        {
            player = FindObjectOfType<PlayerController>();
        }
    }
}