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
        Chatting,
        WaitingLLM
    }

    private Phase _phase = Phase.Chatting;

    private LLMOrchestrator.DecisionResult _lockedPlan;
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

        if (playerInput != null)
        {
            playerInput.text = "";
            playerInput.interactable = false;
            playerInput.onSubmit.RemoveAllListeners();
            playerInput.onEndEdit.RemoveAllListeners();
            playerInput.lineType = TMP_InputField.LineType.MultiLineNewline;

            playerInput.onValueChanged.RemoveAllListeners();
            playerInput.onValueChanged.AddListener(_ => RefreshActionButtonLabel());
        }

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

        if (string.IsNullOrEmpty(t))
        {
            if (_lockedPlan != null)
                ApplyAndEnterNextFloor();
            else
                RefreshActionButtonLabel();
            return;
        }

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

        string context = BuildLLMContextString();

        if (LLMOrchestrator.Instance == null)
        {
            Debug.LogError("NPCDecisionUI: LLMOrchestrator.Instance is null (opening).");
            return null;
        }

        var token = (_cts != null) ? _cts.Token : CancellationToken.None;

        // ✅ 本局第一次 Show() -> 首句自我介绍模式
        bool isFirstOpening = !s_firstOpeningUsed;

        try
        {
            string line = await LLMOrchestrator.Instance.RequestOpeningLineAsync(
                affinity,
                context,
                currentNPC,
                isFirstOpening,
                token);

            // ✅ 无论成功失败（异常会走 catch），只要触发过“首句模式”，就标记用过
            if (isFirstOpening) s_firstOpeningUsed = true;

            return line;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"NPCDecisionUI: RequestOpeningLineAsync failed:\n{ex}");

            if (isFirstOpening) s_firstOpeningUsed = true;

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

        AddMessage(npcBubblePrefab, result.npcReply);

        if (LLMEventBridge.Instance != null)
            LLMEventBridge.Instance.ApplyAffinityDelta(result.affinityDelta);

        SetHeader();
        UpdatePortrait();

        if (_lockedPlan == null)
        {
            _lockedPlan = result;

            if (_lockedPlan.instants != null)
                foreach (var e in _lockedPlan.instants) AddSystemLine(FormatInstantEventLine(e));

            if (_lockedPlan.nextFloor != null)
                foreach (var e in _lockedPlan.nextFloor) AddSystemLine(FormatNextFloorEventLine(e));
        }

        _isRequesting = false;
        EnterChattingPhase();
    }

    private async System.Threading.Tasks.Task<LLMOrchestrator.DecisionResult> RequestDecisionTask(string playerText)
    {
        int affinity = (LLMEventBridge.Instance != null) ? LLMEventBridge.Instance.Affinity : 0;

        string context = BuildLLMContextString();

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
                context,
                currentNPC,
                token);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"NPCDecisionUI: RequestDecisionAsync failed:\n{ex}");
            return null;
        }
    }

    private string BuildLLMContextString()
    {
        // 1) 对话记忆
        string dialogue = (NPCRunDialogueMemory.Instance != null)
            ? NPCRunDialogueMemory.Instance.BuildContextForLLM()
            : historySummaryPlaceholder;

        // 2) 上一层统计
        string lastFloor = (NPCRunFloorStats.Instance != null)
            ? NPCRunFloorStats.Instance.BuildLastFloorSummaryForLLM()
            : "";

        // 3) 本局事件记忆（避免重复）
        string eventMem = (NPCRunEventMemory.Instance != null)
            ? NPCRunEventMemory.Instance.BuildForLLM()
            : "";

        return $"{dialogue}\n\n{lastFloor}\n\n{eventMem}".Trim();
    }

    private void ApplyAndEnterNextFloor()
    {
        SetButtonInteractable(false);

        if (_lockedPlan == null)
        {
            Debug.LogWarning("NPCDecisionUI: _lockedPlan is null, cannot enter next floor.");
            SetButtonInteractable(true);
            RefreshActionButtonLabel();
            return;
        }

        // ✅ 冻结上一层统计：下楼那一刻，把 current 固定为 last
        NPCRunFloorStats.Instance?.FreezeCurrentAsLast();

        WriteToLayerEventSystem(
            _lockedPlan.nextFloor ?? new List<LayerEvent>(),
            _lockedPlan.instants ?? new List<LayerEvent>());

        if (NPCRunEventMemory.Instance != null)
        {
            NPCRunEventMemory.Instance.RecordEvents(_lockedPlan.nextFloor);
            NPCRunEventMemory.Instance.RecordEvents(_lockedPlan.instants);
        }

        var applier = FindFirstObjectByType<LayerEventApplier>();
        if (applier != null)
            applier.ApplyAndConsumeInstantEvents();
        else
            Debug.LogWarning("NPCDecisionUI: 未找到 LayerEventApplier，即时事件未被应用（但已写入LayerEventSystem）。");

        var gc = FindFirstObjectByType<GameController>();
        if (gc == null)
        {
            Debug.LogError("NPCDecisionUI: 未找到 GameController，无法进入下一层。");
            SetButtonInteractable(true);
            RefreshActionButtonLabel();
            return;
        }

        Hide();

        gc.StartNewFloor();
        gc.MovePlayerToSpawnRoomCenterIfPossible_Public();
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

            case LayerEventType.WeaponPenetrationUp:
            {
                int add = (e.value <= 0f) ? 1 : Mathf.RoundToInt(e.value);
                return $"系统：即时 武器强化：穿透 +{add}";
            }
            case LayerEventType.WeaponExtraProjectileUp:
            {
                int add = (e.value <= 0f) ? 1 : Mathf.RoundToInt(e.value);
                return $"系统：即时 武器强化：额外子弹 +{add}";
            }
            case LayerEventType.WeaponBulletSizeUp:
            {
                float mul = (e.value <= 0.01f) ? 1.2f : e.value;
                return $"系统：即时 武器强化：子弹体积 ×{mul:0.##}";
            }
            case LayerEventType.WeaponExplosionOnHit:
            {
                float radius = (e.value <= 0.01f) ? 1.5f : e.value;
                return $"系统：即时 武器强化：命中爆炸（半径 {radius:0.##}）";
            }

            default:
                return $"系统：即时效果 {e.eventType} ({e.value})";
        }
    }
}