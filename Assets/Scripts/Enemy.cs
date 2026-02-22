using UnityEngine;

public class Enemy : MonoBehaviour
{
    [Header("敌人配置")]
    [SerializeField] private float maxHP = 50f;
    [SerializeField] private float expReward = 20f; // 击杀奖励经验
    private float currentHP;
    private PlayerController player;

    private void Awake()
    {
        currentHP = maxHP;
        // 查找玩家
        if (player == null)
        {
            player = FindObjectOfType<PlayerController>();
        }
    }

    /// <summary>
    /// 敌人受击扣血
    /// </summary>
    public void TakeDamage(float damage)
    {
        currentHP = Mathf.Max(0, currentHP - damage);
        if (currentHP <= 0)
        {
            Die();
        }
    }
    
    private void Die()
    {
        // 给玩家加经验
        if (player != null)
        {
            player.AddExp(expReward);
        
            // 新增：通知GameController怪物死亡
            GameController gameController = FindObjectOfType<GameController>();
            if (gameController != null)
            {
                gameController.NotifyEnemyDeath(gameObject);
            }
        }
    
        Destroy(gameObject);
    }

}