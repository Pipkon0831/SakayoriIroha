using UnityEngine;

public class CameraController : MonoBehaviour
{
    // 全局单例（确保任何地方都能调用）
    public static CameraController Instance;

    [Header("基础跟随设置")]
    public Transform target;          // 跟随的目标对象
    public float smoothTime = 0.1f;   // 降低平滑时间，避免抖动被覆盖（关键！）

    [Header("鼠标偏移设置")]
    public float mouseOffsetAmount = 2f;  // 鼠标偏移的最大距离
    [Range(0f, 1f)]
    public float mouseSensitivity = 0.8f; // 鼠标偏移的灵敏度

    [Header("相机抖动设置（全局统一）")]
    public float defaultShakeDuration = 0.5f;   // 默认抖动时长（C键/受伤共用）
    public float defaultShakeMagnitude = 1.5f;  // 默认抖动幅度（调大，确保明显）
    public float shakeSmoothFactor = 0.1f;      // 抖动平滑因子

    // 抖动核心变量
    private Vector3 velocity = Vector3.zero;
    private float currentShakeTime;      
    private Vector3 shakeOffset; 
    private float currentShakeMagnitude; // 当前单次抖动的幅度（统一用这个）

    private void Awake()
    {
        // 单例初始化（确保全局唯一）
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void LateUpdate()
    {
        if (target == null) 
        {
            Debug.LogWarning("CameraController: 未设置跟随目标target！");
            return;
        }

        // 1. 计算基础跟随位置
        Vector3 baseTargetPosition = new Vector3(
            target.position.x,
            target.position.y,
            transform.position.z
        );

        // 2. 计算鼠标偏移
        Vector3 mouseOffset = CalculateMouseOffset();

        // 3. 合并基础位置 + 鼠标偏移
        Vector3 finalTargetPosition = baseTargetPosition + mouseOffset;

        finalTargetPosition += shakeOffset;

        // 5. 平滑移动相机（smoothTime调小，避免抖动被平滑掉）
        transform.position = Vector3.SmoothDamp(
            transform.position,
            finalTargetPosition,
            ref velocity,
            smoothTime
        );
    }

    private Vector3 CalculateMouseOffset()
    {
        Vector2 mouseScreenPosition = Input.mousePosition;
        float normalizedX = mouseScreenPosition.x / Screen.width;
        float normalizedY = mouseScreenPosition.y / Screen.height;
        
        float offsetX = (normalizedX - 0.5f) * 2f;
        float offsetY = (normalizedY - 0.5f) * 2f;
        
        offsetX *= mouseSensitivity * mouseOffsetAmount;
        offsetY *= mouseSensitivity * mouseOffsetAmount;

        return new Vector3(offsetX, offsetY, 0f);
    }
} 