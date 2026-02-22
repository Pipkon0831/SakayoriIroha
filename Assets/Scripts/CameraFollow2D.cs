using UnityEngine;

public class CameraFollow2D : MonoBehaviour
{
    [Header("基础跟随设置")]
    public Transform target;          // 跟随的目标对象
    public float smoothTime = 0.2f;   // 平滑跟随的时间

    [Header("鼠标偏移设置")]
    public float mouseOffsetAmount = 2f;  // 鼠标偏移的最大距离
    [Range(0f, 1f)]
    public float mouseSensitivity = 0.8f; // 鼠标偏移的灵敏度

    private Vector3 velocity = Vector3.zero;

    void LateUpdate()
    {
        if (target == null) return;

        // 1. 计算基础目标位置（跟随目标）
        Vector3 baseTargetPosition = new Vector3(
            target.position.x,
            target.position.y,
            transform.position.z
        );

        // 2. 计算鼠标偏移量
        Vector3 mouseOffset = CalculateMouseOffset();

        // 3. 合并基础位置和鼠标偏移
        Vector3 finalTargetPosition = baseTargetPosition + mouseOffset;

        // 4. 平滑移动相机到最终目标位置
        transform.position = Vector3.SmoothDamp(
            transform.position,
            finalTargetPosition,
            ref velocity,
            smoothTime
        );
    }

    /// <summary>
    /// 计算基于鼠标位置的相机偏移量
    /// </summary>
    private Vector3 CalculateMouseOffset()
    {
        // 获取鼠标在屏幕上的位置（像素坐标）
        Vector2 mouseScreenPosition = Input.mousePosition;
        
        // 将屏幕坐标转换为归一化坐标（0-1范围）
        float normalizedX = mouseScreenPosition.x / Screen.width;
        float normalizedY = mouseScreenPosition.y / Screen.height;
        
        // 将0-1范围转换为-0.5到0.5范围（中心点为0）
        float offsetX = (normalizedX - 0.5f) * 2f;
        float offsetY = (normalizedY - 0.5f) * 2f;
        
        // 应用灵敏度和最大偏移量限制
        offsetX *= mouseSensitivity * mouseOffsetAmount;
        offsetY *= mouseSensitivity * mouseOffsetAmount;

        // 返回最终的偏移向量（Z轴保持0，不影响相机深度）
        return new Vector3(offsetX, offsetY, 0f);
    }
}