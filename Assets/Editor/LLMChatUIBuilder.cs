#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public static class LLMChatUIBuilder
{
    private const string CANVAS_NAME = "LLMChatCanvas";
    private const string EVENTSYSTEM_NAME = "EventSystem";

    [MenuItem("Tools/Thesis/Build LLM Chat UI Canvas (NoFont)")]
    public static void Build()
    {
        // 0) Delete old canvas if exists
        var oldCanvas = GameObject.Find(CANVAS_NAME);
        if (oldCanvas != null)
        {
            Object.DestroyImmediate(oldCanvas);
        }

        // 1) Ensure EventSystem
        EnsureEventSystem();

        // 2) Create Canvas
        var canvasGO = new GameObject(CANVAS_NAME, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create LLM Chat Canvas (NoFont)");

        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // 3) Root
        var uiRoot = CreateUIRect("UIRoot", canvasGO.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // 4) LeftPortraitRoot (0 ~ 0.45)
        var leftRoot = CreateUIRect("LeftPortraitRoot", uiRoot.transform,
            new Vector2(0f, 0f), new Vector2(0.45f, 1f),
            Vector2.zero, Vector2.zero);

        // NPCPortrait (Image)
        var portraitGO = new GameObject("NPCPortrait", typeof(RectTransform), typeof(Image));
        portraitGO.transform.SetParent(leftRoot.transform, false);
        var portraitRT = portraitGO.GetComponent<RectTransform>();
        portraitRT.anchorMin = new Vector2(0f, 0f);
        portraitRT.anchorMax = new Vector2(1f, 1f);
        portraitRT.offsetMin = new Vector2(30, 0);
        portraitRT.offsetMax = new Vector2(-30, 0);
        portraitRT.pivot = new Vector2(0.5f, 0f);

        var portraitImg = portraitGO.GetComponent<Image>();
        portraitImg.raycastTarget = false;
        portraitImg.preserveAspect = true;
        // sprite 留空

        // 5) RightChatRoot (0.45 ~ 1)
        var rightRoot = CreateUIRect("RightChatRoot", uiRoot.transform,
            new Vector2(0.45f, 0f), new Vector2(1f, 1f),
            Vector2.zero, Vector2.zero);

        // Optional background (simple dark panel)
        var rightBg = AddImagePanel(rightRoot, "RightBG", new Color(0f, 0f, 0f, 0.35f));
        rightBg.raycastTarget = false;

        // 6) HeaderBar (top area) - only placeholders, no text/font set
        var header = CreateUIRect("HeaderBar", rightRoot.transform,
            new Vector2(0f, 0.88f), new Vector2(1f, 1f),
            new Vector2(24, 12), new Vector2(-24, -12));

        var headerImg = header.gameObject.AddComponent<Image>();
        headerImg.color = new Color(0f, 0f, 0f, 0.25f);
        headerImg.raycastTarget = false;

        // Use manual positioning (no LayoutGroup) to avoid later surprises.
        // NPCNameText (left)
        var npcName = CreateTMPText_NoFont("NPCNameText", header.transform);
        {
            var rt = npcName.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0.6f, 1f);
            rt.offsetMin = new Vector2(18, 10);
            rt.offsetMax = new Vector2(-10, -10);
            npcName.alignment = TextAlignmentOptions.Left;
            npcName.fontSize = 44;
        }

        // FavorText (right)
        var favor = CreateTMPText_NoFont("FavorText", header.transform);
        {
            var rt = favor.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.6f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(10, 10);
            rt.offsetMax = new Vector2(-18, -10);
            favor.alignment = TextAlignmentOptions.Right;
            favor.fontSize = 30;
        }

        // 7) ChatScrollView (middle)
        var scrollRoot = CreateUIRect("ChatScrollView", rightRoot.transform,
            new Vector2(0f, 0.22f), new Vector2(1f, 0.88f),
            new Vector2(24, 12), new Vector2(-24, -12));

        BuildScrollView(scrollRoot);

        // 8) InputBar (bottom, only right half) - NO HorizontalLayoutGroup
        var inputBar = CreateUIRect("InputBar", rightRoot.transform,
            new Vector2(0f, 0f), new Vector2(1f, 0.22f),
            new Vector2(24, 12), new Vector2(-24, -12));

        var inputBarImg = inputBar.gameObject.AddComponent<Image>();
        inputBarImg.color = new Color(0f, 0f, 0f, 0.25f);
        inputBarImg.raycastTarget = false;

        // InputBG fills entire InputBar
        var inputBG = CreateUIRect("InputBG", inputBar.transform,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var inputBGImg = inputBG.gameObject.AddComponent<Image>();
        inputBGImg.color = new Color(0f, 0f, 0f, 0.35f);
        inputBGImg.raycastTarget = true;

        // SendButton fixed at bottom-right corner, smaller
        var sendBtnGO = new GameObject("SendButton", typeof(RectTransform), typeof(Image), typeof(Button));
        sendBtnGO.transform.SetParent(inputBar.transform, false);
        var sendRT = sendBtnGO.GetComponent<RectTransform>();
        sendRT.anchorMin = new Vector2(1f, 0f);
        sendRT.anchorMax = new Vector2(1f, 0f);
        sendRT.pivot = new Vector2(1f, 0f);
        sendRT.anchoredPosition = new Vector2(-20f, 20f);
        sendRT.sizeDelta = new Vector2(140f, 64f);

        var sendImg = sendBtnGO.GetComponent<Image>();
        sendImg.color = new Color(0.16f, 0.45f, 0.9f, 0.85f);

        var sendBtn = sendBtnGO.GetComponent<Button>();
        sendBtn.targetGraphic = sendImg;

        // SendText placeholder (NO text, NO font assignment)
        var sendText = CreateTMPText_NoFont("SendText", sendBtnGO.transform);
        {
            var rt = sendText.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            sendText.alignment = TextAlignmentOptions.Center;
            sendText.fontSize = 30;
        }

        // TMP_InputField (multi-line) inside InputBG
        var inputField = CreateTMPInputField_NoFont(inputBG.transform, "PlayerInput");

        // Leave space for the bottom-right send button:
        // Text Area offsets: left 18, right 180, top 18, bottom 18 (right bigger to avoid overlap)
        ConfigureMultilineInput_NoFont(inputField, fontSize: 28, textAreaRightPadding: 180);

        // 9) Select canvas
        Selection.activeGameObject = canvasGO;

        Debug.Log("✅ 生成完成：按钮已缩小并固定右下角；所有Text未设置字体/文字。你可自行在Inspector里设置。");
    }

    // ---------------- Helpers ----------------

    private static void EnsureEventSystem()
    {
        var es = Object.FindObjectOfType<EventSystem>();
        if (es != null) return;

        var esGO = new GameObject(EVENTSYSTEM_NAME, typeof(EventSystem), typeof(StandaloneInputModule));
        Undo.RegisterCreatedObjectUndo(esGO, "Create EventSystem");
    }

    private static RectTransform CreateUIRect(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        rt.localScale = Vector3.one;
        return rt;
    }

    private static Image AddImagePanel(RectTransform parent, string name, Color color)
    {
        var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);

        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = panel.GetComponent<Image>();
        img.color = color;
        return img;
    }

    // TMP text with NO font assignment and NO text content
    private static TextMeshProUGUI CreateTMPText_NoFont(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = "";                 // no text
        // tmp.font = null;            // keep default (do NOT touch)
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = false;
        tmp.color = new Color(1f, 1f, 1f, 0.92f);

        return tmp;
    }

    private static void BuildScrollView(RectTransform root)
    {
        var bg = root.gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.20f);
        bg.raycastTarget = false;

        var scrollRect = root.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 20f;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
        viewport.transform.SetParent(root, false);
        var viewportRT = viewport.GetComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.offsetMin = Vector2.zero;
        viewportRT.offsetMax = Vector2.zero;

        var viewportImg = viewport.GetComponent<Image>();
        viewportImg.color = new Color(0, 0, 0, 0);
        viewportImg.raycastTarget = true;

        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);

        var contentRT = content.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1);
        contentRT.anchoredPosition = Vector2.zero;
        contentRT.sizeDelta = new Vector2(0, 0);

        var vlg = content.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.spacing = 12f;
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var csf = content.GetComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewportRT;
        scrollRect.content = contentRT;
    }

    // TMP_InputField with NO font assignment and NO placeholder text
    private static TMP_InputField CreateTMPInputField_NoFont(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(16, 16);
        rt.offsetMax = new Vector2(-16, -16);

        var bg = go.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.25f);

        // Text Area
        var textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(go.transform, false);
        var taRT = textArea.GetComponent<RectTransform>();
        taRT.anchorMin = Vector2.zero;
        taRT.anchorMax = Vector2.one;
        taRT.offsetMin = new Vector2(18, 18);
        taRT.offsetMax = new Vector2(-18, -18);

        // Placeholder
        var placeholderGO = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
        placeholderGO.transform.SetParent(textArea.transform, false);
        var phRT = placeholderGO.GetComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero;
        phRT.anchorMax = Vector2.one;
        phRT.offsetMin = Vector2.zero;
        phRT.offsetMax = Vector2.zero;

        var placeholderTMP = placeholderGO.GetComponent<TextMeshProUGUI>();
        placeholderTMP.text = ""; // no placeholder text
        placeholderTMP.raycastTarget = false;
        placeholderTMP.enableWordWrapping = true;
        placeholderTMP.color = new Color(1f, 1f, 1f, 0.35f);
        placeholderTMP.alignment = TextAlignmentOptions.TopLeft;

        // Text
        var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(textArea.transform, false);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        var textTMP = textGO.GetComponent<TextMeshProUGUI>();
        textTMP.text = "";
        textTMP.raycastTarget = false;
        textTMP.enableWordWrapping = true;
        textTMP.overflowMode = TextOverflowModes.Overflow;
        textTMP.color = new Color(1f, 1f, 1f, 0.92f);
        textTMP.alignment = TextAlignmentOptions.TopLeft;

        var input = go.GetComponent<TMP_InputField>();
        input.textViewport = taRT;
        input.textComponent = textTMP;
        input.placeholder = placeholderTMP;
        input.targetGraphic = bg;

        return input;
    }

    private static void ConfigureMultilineInput_NoFont(TMP_InputField input, int fontSize, float textAreaRightPadding)
    {
        input.lineType = TMP_InputField.LineType.MultiLineNewline;
        input.characterLimit = 0;
        input.pointSize = fontSize;

        if (input.textComponent != null)
        {
            input.textComponent.fontSize = fontSize;
            input.textComponent.enableWordWrapping = true;
            input.textComponent.alignment = TextAlignmentOptions.TopLeft;
        }

        if (input.placeholder is TextMeshProUGUI ph)
        {
            ph.fontSize = fontSize;
            ph.enableWordWrapping = true;
            ph.alignment = TextAlignmentOptions.TopLeft;
        }

        // Increase right padding so text doesn't go under the send button
        // by expanding Text Area's right inset.
        if (input.textViewport != null)
        {
            var ta = input.textViewport;
            // Keep left/top/bottom the same, enlarge right inset.
            ta.offsetMin = new Vector2(18, 18);
            ta.offsetMax = new Vector2(-textAreaRightPadding, -18);
        }
    }
}
#endif