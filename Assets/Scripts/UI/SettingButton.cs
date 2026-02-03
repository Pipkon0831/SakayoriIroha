using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))] // 确保必须有Button组件
public class SettingButton : MonoBehaviour
{
    private Button closeButton;
    private Canvas targetCanvas;

    private void Start()
    {
        closeButton = GetComponent<Button>();
        
        targetCanvas = GetComponentInParent<Canvas>();

        if (closeButton != null && targetCanvas != null)
        {
            closeButton.onClick.AddListener(CloseCanvas);
        }
        else
        {
            Debug.LogError("未找到按钮或Canvas组件！");
        }
    }

    private void CloseCanvas()
    {
        if (targetCanvas != null)
        {
            targetCanvas.gameObject.SetActive(false);
        }
    }
}