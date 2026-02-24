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
    private float currentShakeMagnitude;

    [Header("视野缩放（LowVision事件）")]
    [SerializeField] private Camera targetCamera;      // 不填则自动用 Camera.main 或本物体上的 Camera
    [SerializeField] private float minOrthoMultiplier = 0.35f; // 防止缩得太小
    [SerializeField] private float maxOrthoMultiplier = 1.5f;  // 允许略微放大（可选）

    private float defaultOrthoSize = -1f;
    private float currentOrthoMultiplier = 1f;

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

        // ✅ 新增：初始化相机引用与默认size
        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
            if (targetCamera == null) targetCamera = Camera.main;
        }

        if (targetCamera != null)
        {
            defaultOrthoSize = targetCamera.orthographicSize;
        }
        else
        {
            Debug.LogWarning("CameraController: 未找到Camera引用，LowVision缩放将无效。");
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

        // 4. 加上抖动偏移
        finalTargetPosition += shakeOffset;

        // 5. 平滑移动相机
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

    public void SetOrthoSizeMultiplier(float multiplier)
    {
        if (targetCamera == null) return;
        if (defaultOrthoSize <= 0f) defaultOrthoSize = targetCamera.orthographicSize;

        currentOrthoMultiplier = Mathf.Clamp(multiplier, minOrthoMultiplier, maxOrthoMultiplier);
        targetCamera.orthographicSize = defaultOrthoSize * currentOrthoMultiplier;
    }

    public void ResetOrthoSize()
    {
        SetOrthoSizeMultiplier(1f);
    }

    public float GetCurrentOrthoSizeMultiplier()
    {
        return currentOrthoMultiplier;
    }
}