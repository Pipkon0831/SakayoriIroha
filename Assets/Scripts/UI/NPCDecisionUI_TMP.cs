using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NPCDecisionUI_TMP : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject panelRoot;

    [Header("Hint")]
    [SerializeField] private TextMeshProUGUI hintText;

    [Header("Next Floor (3 slots)")]
    [SerializeField] private TMP_Dropdown[] nextEventTypeDropdowns;
    [SerializeField] private TMP_InputField[] nextEventValueInputs;

    [Header("Instant (2 slots)")]
    [SerializeField] private TMP_Dropdown[] instantEventTypeDropdowns;
    [SerializeField] private TMP_InputField[] instantEventValueInputs;

    [Header("Affinity")]
    [SerializeField] private TMP_InputField affinityDeltaInput;

    [Header("Buttons")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    [Header("LLM JSON")]
    [SerializeField] private TMP_InputField jsonInput;
    [SerializeField] private Button pasteButton;
    [SerializeField] private Button clearJsonButton;
    [SerializeField] private Button applyJsonButton;

    [Header("Input Lock")]
    [SerializeField] private bool pauseGameWhileUIOpen = false; // 推荐：别暂停，避免输入异常
    [SerializeField] private bool disablePlayerControllerWhileUIOpen = true;

    private float _prevTimeScale = 1f;
    private PlayerController _cachedPlayerController;

    [Serializable]
    private class LLMEventDTO
    {
        public string type;
        public float value;
    }

    [Serializable]
    private class LLMDecisionPayload
    {
        public string hint;
        public int affinityDelta;
        public LLMEventDTO[] nextFloorEvents;
        public LLMEventDTO[] instantEvents;
    }

    private void Awake()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(OnConfirm);
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(OnCancel);
        }

        if (applyJsonButton != null)
        {
            applyJsonButton.onClick.RemoveAllListeners();
            applyJsonButton.onClick.AddListener(ApplyJsonToUI);
        }

        if (pasteButton != null)
        {
            pasteButton.onClick.RemoveAllListeners();
            pasteButton.onClick.AddListener(PasteFromClipboard);
        }

        if (clearJsonButton != null)
        {
            clearJsonButton.onClick.RemoveAllListeners();
            clearJsonButton.onClick.AddListener(() =>
            {
                if (jsonInput == null) return;
                jsonInput.text = "";
                FocusJson();
            });
        }

        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void Show()
    {
        if (panelRoot != null) panelRoot.SetActive(true);

        LockInput(true);

        // 默认模板：方便你直接复制改（不强制覆盖已有内容）
        if (jsonInput != null && string.IsNullOrWhiteSpace(jsonInput.text))
        {
            jsonInput.text =
@"{
  ""hint"": ""（可选）NPC 提示文本"",
  ""affinityDelta"": 0,
  ""nextFloorEvents"": [
    { ""type"": ""None"", ""value"": 0 },
    { ""type"": ""None"", ""value"": 0 },
    { ""type"": ""None"", ""value"": 0 }
  ],
  ""instantEvents"": [
    { ""type"": ""None"", ""value"": 0 },
    { ""type"": ""None"", ""value"": 0 }
  ]
}";
        }

        // 强制给输入框焦点（减少“Ctrl+V 没进来”的情况）
        FocusJson();
    }

    public void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        LockInput(false);
    }

    private void FocusJson()
    {
        if (jsonInput == null) return;
        jsonInput.ActivateInputField();
        jsonInput.Select();
        jsonInput.caretPosition = jsonInput.text != null ? jsonInput.text.Length : 0;
    }

    private void PasteFromClipboard()
    {
        if (jsonInput == null) return;

        // 兜底：不依赖 Ctrl+V，直接读系统剪贴板
        jsonInput.text = GUIUtility.systemCopyBuffer ?? "";
        FocusJson();
        SetHint("已从剪贴板粘贴到 JSON 输入框。");
    }

    private void LockInput(bool locked)
    {
        // 1) Pause（不推荐，但留开关）
        if (pauseGameWhileUIOpen)
        {
            if (locked)
            {
                _prevTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }
            else
            {
                Time.timeScale = _prevTimeScale <= 0 ? 1f : _prevTimeScale;
            }
        }

        // 2) Disable PlayerController（推荐）
        if (!disablePlayerControllerWhileUIOpen) return;

        if (locked)
        {
            if (_cachedPlayerController == null)
                _cachedPlayerController = FindFirstObjectByType<PlayerController>();

            if (_cachedPlayerController != null)
                _cachedPlayerController.enabled = false;
        }
        else
        {
            if (_cachedPlayerController != null)
                _cachedPlayerController.enabled = true;

            _cachedPlayerController = null;
        }
    }

    private void ApplyJsonToUI()
    {
        if (jsonInput == null) return;

        var raw = jsonInput.text;
        if (string.IsNullOrWhiteSpace(raw))
        {
            SetHint("JSON 为空，无法解析。");
            return;
        }

        try
        {
            var payload = JsonUtility.FromJson<LLMDecisionPayload>(raw);
            if (payload == null)
            {
                SetHint("JSON 解析失败：payload 为 null。");
                return;
            }

            if (!string.IsNullOrWhiteSpace(payload.hint))
                SetHint(payload.hint);

            if (affinityDeltaInput != null)
                affinityDeltaInput.text = payload.affinityDelta.ToString();

            FillSlots(payload.nextFloorEvents, nextEventTypeDropdowns, nextEventValueInputs);
            FillSlots(payload.instantEvents, instantEventTypeDropdowns, instantEventValueInputs);

            if (string.IsNullOrWhiteSpace(payload.hint))
                SetHint("JSON 已解析并填充到 UI（hint 为空）。");
        }
        catch (Exception e)
        {
            SetHint($"JSON 解析异常：{e.Message}");
            Debug.LogException(e);
        }
    }

    private void FillSlots(LLMEventDTO[] events, TMP_Dropdown[] dds, TMP_InputField[] ips)
    {
        if (dds == null || ips == null) return;

        int slotCount = Mathf.Min(dds.Length, ips.Length);
        for (int i = 0; i < slotCount; i++)
        {
            string typeStr = "None";
            float value = 0;

            if (events != null && i < events.Length && events[i] != null)
            {
                if (!string.IsNullOrWhiteSpace(events[i].type))
                    typeStr = events[i].type;

                value = events[i].value;
            }

            SetDropdownByOptionText(dds[i], typeStr);

            if (ips[i] != null)
                ips[i].text = value.ToString(CultureInfo.InvariantCulture);
        }
    }

    private void SetDropdownByOptionText(TMP_Dropdown dd, string optionText)
    {
        if (dd == null) return;

        string target = (optionText ?? "").Trim();

        int idx = 0;
        bool found = false;

        for (int i = 0; i < dd.options.Count; i++)
        {
            var t = dd.options[i].text;
            if (string.Equals(t, target, StringComparison.OrdinalIgnoreCase))
            {
                idx = i;
                found = true;
                break;
            }
        }

        dd.value = idx;
        dd.RefreshShownValue();

        // ✅ 可选增强：JSON type 写错时立刻能看见原因
        if (!found && !string.IsNullOrEmpty(target) && !string.Equals(target, "None", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning($"NPCDecisionUI_TMP: Dropdown 未找到选项 '{target}'，已回退到第0项 '{dd.options[0].text}'。请检查 JSON type 是否为 LayerEventType 枚举名。");
        }
    }

    private void SetHint(string msg)
    {
        if (hintText != null) hintText.text = msg ?? "";
    }

    private void OnConfirm()
    {
        Hide();

        if (LayerEventSystem.Instance == null)
        {
            Debug.LogError("NPCDecisionUI_TMP: LayerEventSystem.Instance 为 null，无法提交事件。");
            return;
        }

        LayerEventSystem.Instance.ClearNextFloorEvents();
        WriteEventsToSystem(nextEventTypeDropdowns, nextEventValueInputs, isInstant: false);

        WriteEventsToSystem(instantEventTypeDropdowns, instantEventValueInputs, isInstant: true);

        var applier = FindFirstObjectByType<LayerEventApplier>();
        if (applier != null)
        {
            applier.ApplyAndConsumeInstantEvents();
        }
        else
        {
            Debug.LogWarning("NPCDecisionUI_TMP: 未找到 LayerEventApplier，Instant 事件已写入但不会立即执行。");
        }

        var gc = FindFirstObjectByType<GameController>();
        if (gc == null)
        {
            Debug.LogError("NPCDecisionUI_TMP: 未找到 GameController，无法 StartNewFloor()");
            return;
        }

        gc.StartNewFloor();
    }

    private void WriteEventsToSystem(TMP_Dropdown[] dds, TMP_InputField[] ips, bool isInstant)
    {
        if (dds == null || ips == null) return;

        int n = Mathf.Min(dds.Length, ips.Length);
        for (int i = 0; i < n; i++)
        {
            if (dds[i] == null || dds[i].options == null || dds[i].options.Count == 0) continue;

            string typeText = dds[i].options[dds[i].value].text?.Trim() ?? "None";
            if (string.Equals(typeText, "None", StringComparison.OrdinalIgnoreCase)) continue;

            float value = 0f;
            if (ips[i] != null)
                float.TryParse(ips[i].text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

            // Dropdown 选项文本必须是枚举名（LayerEventType）
            if (!Enum.TryParse(typeText, true, out LayerEventType parsedType))
            {
                Debug.LogWarning($"NPCDecisionUI_TMP: 无法把 '{typeText}' 解析为 LayerEventType（请确保 Dropdown 选项=枚举名）");
                continue;
            }

            if (isInstant)
                LayerEventSystem.Instance.AddInstantEvent(parsedType, value);
            else
                LayerEventSystem.Instance.AddNextFloorEvent(parsedType, value);
        }
    }

    private void OnCancel()
    {
        Hide();
    }
}