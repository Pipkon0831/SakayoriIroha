using System.Collections;
using System.Collections.Generic;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NPCDecisionUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject panelRoot;

    [Header("NPC Profile (future multi NPC)")]
    [SerializeField] private NPCProfile currentNPC;

    [Header("Header (TMP)")]
    [SerializeField] private TextMeshProUGUI npcNameText;
    [SerializeField] private TextMeshProUGUI favorText;

    [Header("Portrait (reserved)")]
    [SerializeField] private Image portraitImage;

    [Header("Opening Line")]
    [TextArea(1, 3)]
    [SerializeField] private string firstGameFixedOpeningLine = "你终于来了。说吧，你想怎么做？";
    [TextArea(1, 3)]
    [SerializeField] private string localOpeningFallbackLine = "……说清楚你的打算。";

    [Header("Chat")]
    [SerializeField] private ScrollRect chatScrollRect;
    [SerializeField] private RectTransform chatContent;

    [Header("Input")]
    [SerializeField] private TMP_InputField playerInput;

    [Header("Button (single)")]
    [SerializeField] private Button actionButton;
    [SerializeField] private TextMeshProUGUI buttonLabel;

    [Header("Prefabs")]
    [SerializeField] private GameObject npcBubblePrefab;
    [SerializeField] private GameObject playerBubblePrefab;
    [SerializeField] private GameObject effectLinePrefab;

    [Header("Behavior")]
    [SerializeField] private bool disablePlayerControllerWhileUIOpen = true;

    [Header("History (placeholder)")]
    [TextArea(1, 4)]
    [SerializeField] private string historySummaryPlaceholder = "";

    private PlayerController _cachedPlayerController;
    private CancellationTokenSource _cts;

    private enum Phase
    {
        OpeningWaiting,
        Chatting,      // ✅ 同一层可多轮聊天
        WaitingLLM
    }

    private Phase _phase = Phase.Chatting;

    // ✅ 本层“事件计划”只锁定一次（第一次决策）
    private LLMOrchestrator.DecisionResult _lockedPlan;

    // ✅ 防止连点 / 重入
    private bool _isRequesting;

    private static bool s_firstOpeningUsed;

    public NPCProfile CurrentNPC => currentNPC;

    public void Show()
    {
        if (NPCRunPersonalityManager.Instance != null)
            NPCRunPersonalityManager.Instance.EnsurePicked();

        if (panelRoot != null) panelRoot.SetActive(true);
        LockInput(true);

        _lockedPlan = null;
        _isRequesting = false;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        EnsureChatLayout();
        ClearChat();
        SetHeader();
        UpdatePortrait();

        // Input
        if (playerInput != null)
        {
            playerInput.text = "";
            playerInput.interactable = false;
            playerInput.onSubmit.RemoveAllListeners();
            playerInput.onEndEdit.RemoveAllListeners();
            playerInput.lineType = TMP_InputField.LineType.MultiLineNewline;
        }

        // Button
        if (actionButton != null)
        {
            actionButton.onClick.RemoveAllListeners();
            actionButton.onClick.AddListener(OnActionButtonClicked);
        }

        EnterOpeningWaitingPhase();
        StartCoroutine(RequestOpeningLineAndRender());
    }

    public void Hide()
    {
        _cts?.Cancel();
        _cts = null;

        if (panelRoot != null) panelRoot.SetActive(false);
        LockInput(false);
    }

    private void EnterOpeningWaitingPhase()
    {
        _phase = Phase.OpeningWaiting;
        if (playerInput != null) playerInput.interactable = false;
        SetButtonVisible(false);
    }

    private void EnterChattingPhase()
    {
        _phase = Phase.Chatting;

        if (playerInput != null)
        {
            playerInput.interactable = true;
            playerInput.ActivateInputField();
        }

        SetButtonVisible(true);
        RefreshActionButtonLabel();
        SetButtonInteractable(true);
    }

    private void EnterWaitingPhase()
    {
        _phase = Phase.WaitingLLM;
        if (playerInput != null) playerInput.interactable = false;
        SetButtonVisible(false);
    }

    private void RefreshActionButtonLabel()
    {
        // ✅ 单按钮双语义：
        // - 输入框有字：发送
        // - 输入框空：下楼（如果已锁定事件计划）
        string t = playerInput != null ? playerInput.text.Trim() : "";
        bool hasText = !string.IsNullOrEmpty(t);

        if (hasText)
            SetButtonText("发送");
        else
            SetButtonText(_lockedPlan != null ? "下楼" : "发送");
    }

    private void OnActionButtonClicked()
    {
        if (_phase != Phase.Chatting) return;
        if (_isRequesting) return;

        string t = playerInput != null ? playerInput.text.Trim() : "";

        // ✅ 空输入：如果已有计划 → 直接下楼
        if (string.IsNullOrEmpty(t))
        {
            if (_lockedPlan != null)
            {
                ApplyAndEnterNextFloor();
            }
            else
            {
                // 没有计划却空输入：不做事
                RefreshActionButtonLabel();
            }
            return;
        }

        // ✅ 有输入：发送聊天
        TrySend(t);
    }

    private IEnumerator RequestOpeningLineAndRender()
    {
        AddMessage(npcBubblePrefab, "……（思考中）");

        var task = RequestOpeningTask();
        while (!task.IsCompleted) yield return null;

        string line = null;
        if (!task.IsFaulted && !task.IsCanceled)
            line = task.Result;

        if (string.IsNullOrWhiteSpace(line))
            line = PickOpeningFallbackLine();

        AddMessage(npcBubblePrefab, line);
        EnterChattingPhase();
    }

    private async System.Threading.Tasks.Task<string> RequestOpeningTask()
    {
        int affinity = (LLMEventBridge.Instance != null) ? LLMEventBridge.Instance.Affinity : 0;
        string dialogueMemory = (NPCRunDialogueMemory.Instance != null)
            ? NPCRunDialogueMemory.Instance.BuildContextForLLM()
            : historySummaryPlaceholder;

        if (LLMOrchestrator.Instance == null)
        {
            Debug.LogError("NPCDecisionUI: LLMOrchestrator.Instance is null (opening).");
            return null;
        }

        var token = (_cts != null) ? _cts.Token : CancellationToken.None;

        try
        {
            return await LLMOrchestrator.Instance.RequestOpeningLineAsync(
                affinity,
                dialogueMemory,
                currentNPC,
                token);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"NPCDecisionUI: RequestOpeningLineAsync failed:\n{ex}");
            return null;
        }
    }

    private void TrySend(string playerText)
    {
        if (string.IsNullOrEmpty(playerText)) return;

        _isRequesting = true;

        AddMessage(playerBubblePrefab, playerText);

        if (playerInput != null)
        {
            playerInput.text = "";
            playerInput.ActivateInputField();
        }

        EnterWaitingPhase();
        StartCoroutine(RequestLLMAndRender(playerText));
    }

    private IEnumerator RequestLLMAndRender(string playerText)
    {
        var task = RequestDecisionTask(playerText);
        while (!task.IsCompleted) yield return null;

        LLMOrchestrator.DecisionResult result = null;
        if (!task.IsFaulted && !task.IsCanceled) result = task.Result;

        if (result == null)
        {
            AddMessage(npcBubblePrefab, PickDecisionFallbackLine());
            _isRequesting = false;
            EnterChattingPhase();
            yield break;
        }

        // NPC回复
        AddMessage(npcBubblePrefab, result.npcReply);

        // ✅ 好感度即时生效：每轮聊天都有“反馈”
        if (LLMEventBridge.Instance != null)
            LLMEventBridge.Instance.ApplyAffinityDelta(result.affinityDelta);

        SetHeader();
        UpdatePortrait();

        // ✅ 事件计划：只在本层第一次锁定并展示
        if (_lockedPlan == null)
        {
            _lockedPlan = result;

            if (_lockedPlan.instants != null)
                foreach (var e in _lockedPlan.instants) AddSystemLine(FormatInstantEventLine(e));

            if (_lockedPlan.nextFloor != null)
                foreach (var e in _lockedPlan.nextFloor) AddSystemLine(FormatNextFloorEventLine(e));
        }
        else
        {
            // 后续聊天：不再播报/覆盖事件，保持“本层计划”稳定
            // 你也可以在这里偶尔提示：AddSystemLine("系统：本层事件计划已锁定。");
        }

        _isRequesting = false;
        EnterChattingPhase();
    }

    private async System.Threading.Tasks.Task<LLMOrchestrator.DecisionResult> RequestDecisionTask(string playerText)
    {
        int affinity = (LLMEventBridge.Instance != null) ? LLMEventBridge.Instance.Affinity : 0;

        string dialogueMemory = (NPCRunDialogueMemory.Instance != null)
            ? NPCRunDialogueMemory.Instance.BuildContextForLLM()
            : historySummaryPlaceholder;

        if (LLMOrchestrator.Instance == null)
        {
            Debug.LogError("NPCDecisionUI: LLMOrchestrator.Instance is null.");
            return null;
        }

        var token = (_cts != null) ? _cts.Token : CancellationToken.None;

        try
        {
            return await LLMOrchestrator.Instance.RequestDecisionAsync(
                playerText,
                affinity,
                dialogueMemory,
                currentNPC,
                token);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"NPCDecisionUI: RequestDecisionAsync failed:\n{ex}");
            return null;
        }
    }

    private void ApplyAndEnterNextFloor()
    {
        SetButtonInteractable(false);

        if (_lockedPlan != null)
        {
            WriteToLayerEventSystem(_lockedPlan.nextFloor ?? new List<LayerEvent>(),
                                    _lockedPlan.instants ?? new List<LayerEvent>());
        }

        var applier = FindFirstObjectByType<LayerEventApplier>();
        if (applier != null)
            applier.ApplyAndConsumeInstantEvents();

        var gc = FindFirstObjectByType<GameController>();
        if (gc != null)
        {
            Hide();
            gc.StartNewFloor();
            gc.MovePlayerToSpawnRoomCenterIfPossible_Public();
        }
        else
        {
            Debug.LogError("NPCDecisionUI: 未找到 GameController，无法进入下一层。");
            SetButtonInteractable(true);
            return;
        }
    }

    private void WriteToLayerEventSystem(List<LayerEvent> nextFloor, List<LayerEvent> instants)
    {
        if (LayerEventSystem.Instance == null)
        {
            Debug.LogWarning("NPCDecisionUI: LayerEventSystem 未就绪，无法写入事件。");
            return;
        }

        LayerEventSystem.Instance.ClearNextFloorEvents();
        LayerEventSystem.Instance.AddNextFloorEvents(nextFloor);
        LayerEventSystem.Instance.AddInstantEvents(instants);
    }

    private string GetNpcNameSafe()
    {
        return (currentNPC != null && !string.IsNullOrWhiteSpace(currentNPC.npcName))
            ? currentNPC.npcName.Trim()
            : "NPC";
    }

    private void SetHeader()
    {
        string npcName = GetNpcNameSafe();

        var p = (NPCRunPersonalityManager.Instance != null) ? NPCRunPersonalityManager.Instance.Selected : null;
        string fullName = (p != null && !string.IsNullOrWhiteSpace(p.displayName))
            ? $"{npcName} · {p.displayName}"
            : npcName;

        if (npcNameText != null) npcNameText.text = fullName;

        int affinity = (LLMEventBridge.Instance != null) ? LLMEventBridge.Instance.Affinity : 0;
        if (favorText != null) favorText.text = $"好感度  ♥  {affinity}";
    }

    private void UpdatePortrait()
    {
        if (portraitImage == null) return;

        int affinity = (LLMEventBridge.Instance != null) ? LLMEventBridge.Instance.Affinity : 0;
        var personality = (NPCRunPersonalityManager.Instance != null) ? NPCRunPersonalityManager.Instance.Selected : null;

        Sprite sp = (personality != null) ? personality.GetPortraitByAffinity(affinity) : null;

        if (sp != null)
        {
            portraitImage.sprite = sp;
            portraitImage.enabled = true;
        }
        else
        {
            portraitImage.enabled = false;
        }
    }

    private void LockInput(bool locked)
    {
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

    private void EnsureChatLayout()
    {
        if (chatContent == null) return;

        var vlg = chatContent.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = chatContent.gameObject.AddComponent<VerticalLayoutGroup>();

        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = Mathf.Max(vlg.spacing, 8f);

        var csf = chatContent.GetComponent<ContentSizeFitter>();
        if (csf == null) csf = chatContent.gameObject.AddComponent<ContentSizeFitter>();

        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private void AddMessage(GameObject prefab, string text)
    {
        AddRow(prefab, text);

        if (NPCRunDialogueMemory.Instance == null) return;

        int floor = 0;
        var gc = FindFirstObjectByType<GameController>();
        if (gc != null) floor = gc.CurrentFloorIndex;

        if (prefab == playerBubblePrefab)
            NPCRunDialogueMemory.Instance.AddPlayerLine(text, floor);
        else if (prefab == npcBubblePrefab)
            NPCRunDialogueMemory.Instance.AddNpcLine(text, floor);
    }

    private void AddSystemLine(string text) => AddRow(effectLinePrefab, text);

    private void AddRow(GameObject prefab, string text)
    {
        if (prefab == null || chatContent == null) return;

        var go = Instantiate(prefab, chatContent);

        var tmp = go.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp != null) tmp.text = text ?? "";

        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();

        Canvas.ForceUpdateCanvases();
        if (tmp != null)
            le.preferredHeight = Mathf.Max(tmp.preferredHeight + 6f, 24f);
        else
            le.preferredHeight = 24f;

        AutoScrollBottom();
    }

    private void ClearChat()
    {
        if (chatContent == null) return;
        for (int i = chatContent.childCount - 1; i >= 0; i--)
            Destroy(chatContent.GetChild(i).gameObject);
    }

    private void AutoScrollBottom()
    {
        if (chatScrollRect == null) return;
        Canvas.ForceUpdateCanvases();
        chatScrollRect.verticalNormalizedPosition = 0f;
        Canvas.ForceUpdateCanvases();
    }

    private void SetButtonVisible(bool visible)
    {
        if (actionButton == null) return;
        actionButton.gameObject.SetActive(visible);
    }

    private void SetButtonInteractable(bool interactable)
    {
        if (actionButton == null) return;
        actionButton.interactable = interactable;
    }

    private void SetButtonText(string text)
    {
        if (buttonLabel != null) buttonLabel.text = text;
    }

    // -------------------------
    // Fallbacks
    // -------------------------
    private string PickOpeningFallbackLine()
    {
        var p = (NPCRunPersonalityManager.Instance != null) ? NPCRunPersonalityManager.Instance.Selected : null;
        if (p != null)
        {
            string s = p.GetRandomOpeningFallbackOrEmpty();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }

        if (!string.IsNullOrWhiteSpace(localOpeningFallbackLine))
            return localOpeningFallbackLine.Trim();

        return "……";
    }

    private string PickDecisionFallbackLine()
    {
        var p = (NPCRunPersonalityManager.Instance != null) ? NPCRunPersonalityManager.Instance.Selected : null;
        if (p != null)
        {
            string s = p.GetRandomDecisionFallbackOrEmpty();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }

        return "……";
    }

    // -------------------------
    // Formatting
    // -------------------------
    private static string FormatNextFloorEventLine(LayerEvent e)
    {
        if (e == null) return "";

        switch (e.eventType)
        {
            case LayerEventType.LowVision:
                return $"系统：下层视野 {Mathf.RoundToInt(e.value * 100f)}%";
            case LayerEventType.EnemyMoveSpeedUp:
                return $"系统：下层怪物移速 +{Mathf.RoundToInt(e.value * 100f)}%";
            case LayerEventType.PlayerDealMoreDamage:
                return $"系统：下层玩家伤害 +{Mathf.RoundToInt(e.value * 100f)}%";
            case LayerEventType.PlayerReceiveMoreDamage:
                return $"系统：下层玩家受伤 +{Mathf.RoundToInt(e.value * 100f)}%";
            case LayerEventType.PlayerAttackSpeedUp:
                return $"系统：下层玩家攻速 +{Mathf.RoundToInt(e.value * 100f)}%";
            case LayerEventType.PlayerAttackSpeedDown:
                return $"系统：下层玩家攻速 -{Mathf.RoundToInt(e.value * 100f)}%";
            case LayerEventType.AllRoomsMonsterExceptBossAndSpawn:
                return "系统：下层除出生/Boss外全部怪物房";
            case LayerEventType.AllRoomsRewardExceptBossAndSpawn:
                return "系统：下层除出生/Boss外全部奖励房";
            default:
                return $"系统：下层效果 {e.eventType} ({e.value})";
        }
    }

    private static string FormatInstantEventLine(LayerEvent e)
    {
        if (e == null) return "";

        switch (e.eventType)
        {
            case LayerEventType.GainExp:
                return $"系统：即时 获得经验 +{Mathf.RoundToInt(e.value)}";
            case LayerEventType.Heal:
                return $"系统：即时 回复生命 +{Mathf.RoundToInt(e.value)}";
            case LayerEventType.LoseHP:
                return $"系统：即时 扣除生命 -{Mathf.RoundToInt(e.value)}";
            case LayerEventType.PlayerMaxHPUp:
                return $"系统：即时 最大生命 +{Mathf.RoundToInt(e.value)}";
            case LayerEventType.PlayerMaxHPDown:
                return $"系统：即时 最大生命 -{Mathf.RoundToInt(e.value)}";
            case LayerEventType.PlayerAttackUp:
                return $"系统：即时 攻击 +{Mathf.RoundToInt(e.value)}";
            case LayerEventType.PlayerAttackDown:
                return $"系统：即时 攻击 -{Mathf.RoundToInt(e.value)}";
            default:
                return $"系统：即时效果 {e.eventType} ({e.value})";
        }
    }
}