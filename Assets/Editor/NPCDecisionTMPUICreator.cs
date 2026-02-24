#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public static class NPCDecisionTMPPanelCreator
{
    private const string CANVAS_NAME = "NPCDecisionCanvas_TMP";

    [MenuItem("Tools/Thesis/Rebuild NPC Decision UI (TMP Redesigned)")]
    public static void Rebuild()
    {
        // 0) Delete old canvas
        var old = GameObject.Find(CANVAS_NAME);
        if (old != null) GameObject.DestroyImmediate(old);

        EnsureEventSystemCompatible();

        EnsureTMPDefaultFontDynamicAndFallback();

        // 1) Create Canvas
        var canvasGO = new GameObject(CANVAS_NAME, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // 2) Fullscreen blocker panel (must block clicks behind)
        var panelGO = CreateUI("NPCDecisionPanel", canvasGO.transform, typeof(Image));
        var panelImg = panelGO.GetComponent<Image>();
        panelImg.color = new Color(0, 0, 0, 0.78f);
        panelImg.raycastTarget = true; // ✅ block world clicks
        Stretch(panelGO.GetComponent<RectTransform>());

        // 3) Card (center)
        var cardGO = CreateUI("Card", panelGO.transform, typeof(Image));
        var cardRT = cardGO.GetComponent<RectTransform>();
        cardRT.anchorMin = cardRT.anchorMax = new Vector2(0.5f, 0.5f);
        cardRT.pivot = new Vector2(0.5f, 0.5f);
        cardRT.anchoredPosition = Vector2.zero;
        cardRT.sizeDelta = new Vector2(1180, 820);

        var cardImg = cardGO.GetComponent<Image>();
        cardImg.color = new Color(0.12f, 0.12f, 0.12f, 0.98f);
        cardImg.raycastTarget = false; // ✅ card background should NOT steal raycast

        var cardV = cardGO.AddComponent<VerticalLayoutGroup>();
        cardV.padding = new RectOffset(24, 24, 20, 18);
        cardV.spacing = 14;
        cardV.childAlignment = TextAnchor.UpperCenter;
        cardV.childControlWidth = true;
        cardV.childForceExpandWidth = true;
        cardV.childControlHeight = false;
        cardV.childForceExpandHeight = false;

        // ===== Header =====
        var header = CreateUI("Header", cardGO.transform);
        var headerLE = EnsureLE(header);
        headerLE.minHeight = 70;
        headerLE.preferredHeight = 70;

        var headerV = header.AddComponent<VerticalLayoutGroup>();
        headerV.padding = new RectOffset(0, 0, 0, 0);
        headerV.spacing = 6;
        headerV.childAlignment = TextAnchor.UpperCenter;
        headerV.childControlWidth = true;
        headerV.childForceExpandWidth = true;
        headerV.childControlHeight = false;
        headerV.childForceExpandHeight = false;

        var title = CreateTMPText(header.transform, "TitleText", "NPC Decision Debug Console", 30, TextAlignmentOptions.Center);
        title.raycastTarget = false;
        PrefH(title.rectTransform, 40);

        var hint = CreateTMPText(header.transform, "HintText", "粘贴 JSON → Apply JSON → Confirm（右侧可手动调）", 14, TextAlignmentOptions.Center);
        hint.color = new Color(0.9f, 0.9f, 0.9f, 0.9f);
        hint.raycastTarget = false;
        PrefH(hint.rectTransform, 22);

        // ===== Body Row (Left JSON / Right Controls) =====
        var bodyRow = CreateUI("BodyRow", cardGO.transform);
        var bodyLE = EnsureLE(bodyRow);
        bodyLE.flexibleHeight = 1;     // ✅ take remaining height
        bodyLE.minHeight = 620;

        var bodyH = bodyRow.AddComponent<HorizontalLayoutGroup>();
        bodyH.spacing = 16;
        bodyH.padding = new RectOffset(0, 0, 0, 0);
        bodyH.childAlignment = TextAnchor.UpperCenter;
        bodyH.childControlWidth = true;
        bodyH.childForceExpandWidth = true;
        bodyH.childControlHeight = true;
        bodyH.childForceExpandHeight = true;

        // ---------- Left: JSON Panel ----------
        var jsonPanel = CreateUI("JsonPanel", bodyRow.transform, typeof(Image));
        var jsonPanelImg = jsonPanel.GetComponent<Image>();
        jsonPanelImg.color = new Color(1f, 1f, 1f, 0.05f);
        jsonPanelImg.raycastTarget = false; // ✅ background no raycast
        jsonPanel.AddComponent<RectMask2D>(); // ✅ prevent overflow visuals

        var jsonPanelLE = EnsureLE(jsonPanel);
        jsonPanelLE.preferredWidth = 640;

        var jsonV = jsonPanel.AddComponent<VerticalLayoutGroup>();
        jsonV.padding = new RectOffset(14, 14, 12, 12);
        jsonV.spacing = 10;
        jsonV.childAlignment = TextAnchor.UpperLeft;
        jsonV.childControlWidth = true;
        jsonV.childForceExpandWidth = true;
        jsonV.childControlHeight = false;
        jsonV.childForceExpandHeight = false;

        var jsonLabel = CreateTMPText(jsonPanel.transform, "JsonLabel", "LLM JSON", 16, TextAlignmentOptions.Left);
        jsonLabel.raycastTarget = false;
        PrefH(jsonLabel.rectTransform, 24);

        // Toolbar (Paste / Clear / Apply)
        var toolbar = CreateUI("JsonToolbar", jsonPanel.transform);
        var toolbarLE = EnsureLE(toolbar);
        toolbarLE.minHeight = 44;
        toolbarLE.preferredHeight = 44;

        var tbH = toolbar.AddComponent<HorizontalLayoutGroup>();
        tbH.spacing = 10;
        tbH.childAlignment = TextAnchor.MiddleLeft;
        tbH.childControlWidth = false;
        tbH.childForceExpandWidth = false;
        tbH.childControlHeight = true;
        tbH.childForceExpandHeight = true;

        var pasteBtn = CreateButton_TMP(toolbar.transform, "PasteButton", "Paste", new Color(0.35f, 0.55f, 0.95f, 0.95f));
        SetBtnSize(pasteBtn, 120, 40);

        var clearBtn = CreateButton_TMP(toolbar.transform, "ClearJsonButton", "Clear", new Color(0.55f, 0.55f, 0.55f, 0.75f));
        SetBtnSize(clearBtn, 120, 40);

        var applyJsonBtn = CreateButton_TMP(toolbar.transform, "ApplyJsonButton", "Apply JSON", new Color(0.25f, 0.75f, 0.35f, 0.95f));
        SetBtnSize(applyJsonBtn, 150, 40);

        // Spacer to push small helper text to right if desired
        var spacer = CreateUI("ToolbarSpacer", toolbar.transform);
        var spacerLE = EnsureLE(spacer);
        spacerLE.flexibleWidth = 1;

        // JSON Scroll + Input (big area)
        var jsonScroll = CreateScrollInput_TMP(
            parent: jsonPanel.transform,
            scrollName: "JsonScrollView",
            inputName: "LLMJsonInput",
            placeholder: "在这里粘贴 LLM 返回的 JSON…",
            out TMP_InputField jsonInput
        );

        // lock height allocation: this takes rest
        var jsonScrollLE = EnsureLE(jsonScroll);
        jsonScrollLE.flexibleHeight = 1;
        jsonScrollLE.minHeight = 460;

        // ---------- Right: Controls Panel ----------
        var ctrlPanel = CreateUI("ControlPanel", bodyRow.transform, typeof(Image));
        var ctrlImg = ctrlPanel.GetComponent<Image>();
        ctrlImg.color = new Color(1f, 1f, 1f, 0.05f);
        ctrlImg.raycastTarget = false;
        ctrlPanel.AddComponent<RectMask2D>();

        var ctrlLE = EnsureLE(ctrlPanel);
        ctrlLE.preferredWidth = 480;

        var ctrlV = ctrlPanel.AddComponent<VerticalLayoutGroup>();
        ctrlV.padding = new RectOffset(14, 14, 12, 12);
        ctrlV.spacing = 12;
        ctrlV.childAlignment = TextAnchor.UpperLeft;
        ctrlV.childControlWidth = true;
        ctrlV.childForceExpandWidth = true;
        ctrlV.childControlHeight = false;
        ctrlV.childForceExpandHeight = false;

        // Next Floor section
        var nextSec = CreateSection(ctrlPanel.transform, "NextFloorSection", "Next Floor Events（下一层生效）", out TextMeshProUGUI nextLabel);
        var n1 = CreateSlot(ctrlPanel.transform, "NextSlot1", "NextType1", "NextValue1");
        var n2 = CreateSlot(ctrlPanel.transform, "NextSlot2", "NextType2", "NextValue2");
        var n3 = CreateSlot(ctrlPanel.transform, "NextSlot3", "NextType3", "NextValue3");

        // Instant section
        var instSec = CreateSection(ctrlPanel.transform, "InstantSection", "Instant Events（确认后立即执行）", out TextMeshProUGUI instLabel);
        var i1 = CreateSlot(ctrlPanel.transform, "InstantSlot1", "InstantType1", "InstantValue1");
        var i2 = CreateSlot(ctrlPanel.transform, "InstantSlot2", "InstantType2", "InstantValue2");

        // Affinity Row
        var affinityRow = CreateUI("AffinityRow", ctrlPanel.transform);
        var affLE = EnsureLE(affinityRow);
        affLE.minHeight = 42;
        affLE.preferredHeight = 42;

        var affH = affinityRow.AddComponent<HorizontalLayoutGroup>();
        affH.spacing = 10;
        affH.childAlignment = TextAnchor.MiddleLeft;
        affH.childControlWidth = false;
        affH.childForceExpandWidth = false;
        affH.childControlHeight = true;
        affH.childForceExpandHeight = true;

        var affinityLabel = CreateTMPText(affinityRow.transform, "AffinityLabel", "Affinity Delta（未接入）", 14, TextAlignmentOptions.Left);
        affinityLabel.raycastTarget = false;
        var affLabelLE = EnsureLE(affinityLabel.gameObject);
        affLabelLE.preferredWidth = 220;

        var affinityInput = CreateTMPInput_TMPDefaultControls(affinityRow.transform, "AffinityDeltaInput", "0");
        var affIpLE = EnsureLE(affinityInput.gameObject);
        affIpLE.preferredWidth = 180;
        affIpLE.minHeight = 36;

        // ✅ Fill dropdown options with LayerEventType names (fix mismatch)
        FillDropdownOptionsWithLayerEventType(n1.dd);
        FillDropdownOptionsWithLayerEventType(n2.dd);
        FillDropdownOptionsWithLayerEventType(n3.dd);
        FillDropdownOptionsWithLayerEventType(i1.dd);
        FillDropdownOptionsWithLayerEventType(i2.dd);

        // ===== Footer Buttons =====
        var footer = CreateUI("Footer", cardGO.transform);
        var footerLE = EnsureLE(footer);
        footerLE.minHeight = 64;
        footerLE.preferredHeight = 64;

        var footerH = footer.AddComponent<HorizontalLayoutGroup>();
        footerH.spacing = 18;
        footerH.childAlignment = TextAnchor.MiddleCenter;
        footerH.childControlWidth = false;
        footerH.childForceExpandWidth = false;
        footerH.childControlHeight = true;
        footerH.childForceExpandHeight = true;

        var confirm = CreateButton_TMP(footer.transform, "ConfirmButton", "Confirm", new Color(0.20f, 0.55f, 0.95f, 0.95f));
        SetBtnSize(confirm, 170, 44);

        var cancel = CreateButton_TMP(footer.transform, "CancelButton", "Cancel", new Color(0.60f, 0.60f, 0.60f, 0.75f));
        SetBtnSize(cancel, 170, 44);

        // 9) Force default TMP font (important for CJK fallback too)
        ForceAllTMPUseDefaultFont(canvasGO);

        // 10) Bind controller
        var ui = canvasGO.AddComponent<NPCDecisionUI_TMP>();
        BindToController(ui, panelGO, hint, n1, n2, n3, i1, i2, affinityInput,
            confirm, cancel, jsonInput, pasteBtn, clearBtn, applyJsonBtn);

        panelGO.SetActive(false);

        Selection.activeGameObject = canvasGO;
        Debug.Log("NPCDecisionCanvas rebuilt successfully (TMP Redesigned).");
    }

    // -------------------- Slot / Section helpers --------------------
    private struct SlotRef
    {
        public TMP_Dropdown dd;
        public TMP_InputField ip;
    }

    private static GameObject CreateSection(Transform parent, string secName, string title, out TextMeshProUGUI label)
    {
        var sec = CreateUI(secName, parent, typeof(Image));
        var img = sec.GetComponent<Image>();
        img.color = new Color(0, 0, 0, 0.08f);
        img.raycastTarget = false;

        var le = EnsureLE(sec);
        le.minHeight = 34;
        le.preferredHeight = 34;

        label = CreateTMPText(sec.transform, "Label", title, 16, TextAlignmentOptions.Left);
        label.raycastTarget = false;
        Stretch(label.rectTransform);
        label.margin = new Vector4(6, 0, 6, 0);

        return sec;
    }

    private static SlotRef CreateSlot(Transform parent, string slotName, string dropdownName, string inputName)
    {
        var row = CreateUI(slotName, parent);
        var le = EnsureLE(row);
        le.minHeight = 42;
        le.preferredHeight = 42;

        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 10;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true;
        h.childForceExpandWidth = true;
        h.childControlHeight = true;
        h.childForceExpandHeight = true;

        var ddGO = CreateTMPDropdown_TMPDefaultControls(dropdownName, row.transform);
        var ddLE = EnsureLE(ddGO);
        ddLE.flexibleWidth = 1;
        ddLE.minHeight = 36;
        ddLE.preferredHeight = 36;

        var ip = CreateTMPInput_TMPDefaultControls(row.transform, inputName, "0");
        var ipLE = EnsureLE(ip.gameObject);
        ipLE.preferredWidth = 140;
        ipLE.minHeight = 36;
        ipLE.preferredHeight = 36;

        return new SlotRef { dd = ddGO.GetComponent<TMP_Dropdown>(), ip = ip };
    }

    // -------------------- Binding --------------------
    private static void BindToController(
        NPCDecisionUI_TMP ui,
        GameObject panelRoot,
        TextMeshProUGUI hintText,
        SlotRef n1, SlotRef n2, SlotRef n3,
        SlotRef i1, SlotRef i2,
        TMP_InputField affinityInput,
        GameObject confirmBtn,
        GameObject cancelBtn,
        TMP_InputField jsonInput,
        GameObject pasteBtn,
        GameObject clearBtn,
        GameObject applyJsonBtn)
    {
        var so = new SerializedObject(ui);

        so.FindProperty("panelRoot").objectReferenceValue = panelRoot;
        so.FindProperty("hintText").objectReferenceValue = hintText;

        so.FindProperty("nextEventTypeDropdowns").arraySize = 3;
        so.FindProperty("nextEventTypeDropdowns").GetArrayElementAtIndex(0).objectReferenceValue = n1.dd;
        so.FindProperty("nextEventTypeDropdowns").GetArrayElementAtIndex(1).objectReferenceValue = n2.dd;
        so.FindProperty("nextEventTypeDropdowns").GetArrayElementAtIndex(2).objectReferenceValue = n3.dd;

        so.FindProperty("nextEventValueInputs").arraySize = 3;
        so.FindProperty("nextEventValueInputs").GetArrayElementAtIndex(0).objectReferenceValue = n1.ip;
        so.FindProperty("nextEventValueInputs").GetArrayElementAtIndex(1).objectReferenceValue = n2.ip;
        so.FindProperty("nextEventValueInputs").GetArrayElementAtIndex(2).objectReferenceValue = n3.ip;

        so.FindProperty("instantEventTypeDropdowns").arraySize = 2;
        so.FindProperty("instantEventTypeDropdowns").GetArrayElementAtIndex(0).objectReferenceValue = i1.dd;
        so.FindProperty("instantEventTypeDropdowns").GetArrayElementAtIndex(1).objectReferenceValue = i2.dd;

        so.FindProperty("instantEventValueInputs").arraySize = 2;
        so.FindProperty("instantEventValueInputs").GetArrayElementAtIndex(0).objectReferenceValue = i1.ip;
        so.FindProperty("instantEventValueInputs").GetArrayElementAtIndex(1).objectReferenceValue = i2.ip;

        so.FindProperty("affinityDeltaInput").objectReferenceValue = affinityInput;

        so.FindProperty("confirmButton").objectReferenceValue = confirmBtn.GetComponent<Button>();
        so.FindProperty("cancelButton").objectReferenceValue = cancelBtn.GetComponent<Button>();

        so.FindProperty("jsonInput").objectReferenceValue = jsonInput;
        so.FindProperty("pasteButton").objectReferenceValue = pasteBtn.GetComponent<Button>();
        so.FindProperty("clearJsonButton").objectReferenceValue = clearBtn.GetComponent<Button>();
        so.FindProperty("applyJsonButton").objectReferenceValue = applyJsonBtn.GetComponent<Button>();

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // -------------------- JSON Scroll + Input builder --------------------
    private static GameObject CreateScrollInput_TMP(
        Transform parent,
        string scrollName,
        string inputName,
        string placeholder,
        out TMP_InputField input)
    {
        // ScrollView root
        var scrollGO = CreateUI(scrollName, parent, typeof(Image), typeof(ScrollRect));
        var scrollImg = scrollGO.GetComponent<Image>();
        scrollImg.color = new Color(0.08f, 0.08f, 0.08f, 1f);
        scrollImg.raycastTarget = true; // ✅ scroll area should receive drag / wheel

        var scroll = scrollGO.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 35f;

        // Viewport
        var viewportGO = CreateUI("Viewport", scrollGO.transform, typeof(Image), typeof(Mask));
        var vpImg = viewportGO.GetComponent<Image>();
        vpImg.color = new Color(1f, 1f, 1f, 0.02f);
        vpImg.raycastTarget = true; // ✅ must be true for scroll
        var mask = viewportGO.GetComponent<Mask>();
        mask.showMaskGraphic = false;

        Stretch(viewportGO.GetComponent<RectTransform>());
        scroll.viewport = viewportGO.GetComponent<RectTransform>();

        // Content
        var contentGO = CreateUI("Content", viewportGO.transform);
        var contentRT = contentGO.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1);
        contentRT.anchoredPosition = Vector2.zero;
        contentRT.sizeDelta = new Vector2(0, 0);

        var fitter = contentGO.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 26, 10, 10);
        vlg.spacing = 0;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childForceExpandWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandHeight = false;

        scroll.content = contentRT;

        // InputField (TMP default)
        input = CreateTMPInput_TMPDefaultControls(contentGO.transform, inputName, "");
        input.lineType = TMP_InputField.LineType.MultiLineNewline;
        input.richText = false;
        input.caretWidth = 2;
        input.caretColor = Color.white;
        input.selectionColor = new Color(0.2f, 0.5f, 1f, 0.45f);

        // Input background should receive raycasts for caret/selection
        var ipBg = input.GetComponent<Image>();
        if (ipBg != null)
        {
            ipBg.color = new Color(0.10f, 0.10f, 0.10f, 1f);
            ipBg.raycastTarget = true;
        }

        if (input.textComponent != null)
        {
            input.textComponent.color = Color.white;
            input.textComponent.fontSize = 16;
            input.textComponent.enableWordWrapping = false;
            input.textComponent.alignment = TextAlignmentOptions.TopLeft;
            input.textComponent.raycastTarget = false; // ✅ text itself shouldn't steal clicks, input handles it
        }

        if (input.placeholder is TextMeshProUGUI ph)
        {
            ph.text = placeholder;
            ph.color = new Color(0.75f, 0.75f, 0.75f, 0.55f);
            ph.fontSize = 14;
            ph.alignment = TextAlignmentOptions.TopLeft;
            ph.raycastTarget = false;
        }

        // Set input preferred height
        var ipLE = EnsureLE(input.gameObject);
        ipLE.minHeight = 520;
        ipLE.preferredHeight = 520;

        // Scrollbar
        var sbGO = CreateUI("Scrollbar", scrollGO.transform, typeof(Image), typeof(Scrollbar));
        var sbRT = sbGO.GetComponent<RectTransform>();
        sbRT.anchorMin = new Vector2(1, 0);
        sbRT.anchorMax = new Vector2(1, 1);
        sbRT.pivot = new Vector2(1, 1);
        sbRT.sizeDelta = new Vector2(14, 0);
        sbRT.anchoredPosition = new Vector2(-2, 0);

        var sbImg = sbGO.GetComponent<Image>();
        sbImg.color = new Color(1, 1, 1, 0.08f);
        sbImg.raycastTarget = true;

        var sb = sbGO.GetComponent<Scrollbar>();
        sb.direction = Scrollbar.Direction.BottomToTop;

        var slidingArea = CreateUI("SlidingArea", sbGO.transform);
        Stretch(slidingArea.GetComponent<RectTransform>());

        var handle = CreateUI("Handle", slidingArea.transform, typeof(Image));
        Stretch(handle.GetComponent<RectTransform>());
        var handleImg = handle.GetComponent<Image>();
        handleImg.color = new Color(1, 1, 1, 0.35f);
        handleImg.raycastTarget = true;

        sb.targetGraphic = handleImg;
        sb.handleRect = handle.GetComponent<RectTransform>();

        scroll.verticalScrollbar = sb;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        scroll.verticalScrollbarSpacing = -2;

        return scrollGO;
    }

    // -------------------- TMP DefaultControls creation --------------------
    private static GameObject CreateTMPDropdown_TMPDefaultControls(string name, Transform parent)
    {
        var res = new TMP_DefaultControls.Resources();
        var go = TMP_DefaultControls.CreateDropdown(res);
        go.name = name;
        go.transform.SetParent(parent, false);

        // background image: allow raycast so it can open
        var img = go.GetComponent<Image>();
        if (img != null)
        {
            img.color = new Color(1, 1, 1, 0.14f);
            img.raycastTarget = true;
        }

        var dd = go.GetComponent<TMP_Dropdown>();
        if (dd != null && dd.captionText != null)
        {
            dd.captionText.color = Color.white;
            dd.captionText.raycastTarget = false;
        }

        // Also: template background often blocks weirdly; keep it raycast true only inside template
        return go;
    }

    private static TMP_InputField CreateTMPInput_TMPDefaultControls(Transform parent, string name, string defaultText)
    {
        var res = new TMP_DefaultControls.Resources();
        var go = TMP_DefaultControls.CreateInputField(res);
        go.name = name;
        go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        if (img != null)
        {
            img.color = new Color(1, 1, 1, 0.14f);
            img.raycastTarget = true; // ✅ must be true for input focus
        }

        var ip = go.GetComponent<TMP_InputField>();
        ip.text = defaultText;

        if (ip.textComponent != null)
        {
            ip.textComponent.color = Color.white;
            ip.textComponent.raycastTarget = false;
        }

        if (ip.placeholder is TextMeshProUGUI ph)
        {
            ph.color = new Color(1, 1, 1, 0.35f);
            ph.raycastTarget = false;
        }

        return ip;
    }

    private static GameObject CreateButton_TMP(Transform parent, string name, string label, Color bg)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        img.color = bg;
        img.raycastTarget = true;

        var t = CreateTMPText(go.transform, "Text", label, 16, TextAlignmentOptions.Center);
        t.raycastTarget = false;
        Stretch(t.rectTransform);

        return go;
    }

    private static void SetBtnSize(GameObject btn, float w, float h)
    {
        var rt = btn.GetComponent<RectTransform>();
        var le = EnsureLE(btn);
        le.preferredWidth = w;
        le.minHeight = h;
        le.preferredHeight = h;
    }

    private static TextMeshProUGUI CreateTMPText(Transform parent, string name, string text, int size, TextAlignmentOptions align)
    {
        var go = CreateUI(name, parent);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        t.raycastTarget = false;
        return t;
    }

    // -------------------- Dropdown option fill: match enum names exactly --------------------
    private static void FillDropdownOptionsWithLayerEventType(TMP_Dropdown dd)
    {
        if (dd == null) return;

        dd.options.Clear();
        var names = Enum.GetNames(typeof(LayerEventType));
        foreach (var n in names)
            dd.options.Add(new TMP_Dropdown.OptionData(n));

        dd.value = 0;
        dd.RefreshShownValue();
        EditorUtility.SetDirty(dd);
    }

    // -------------------- EventSystem: compatible with New/Old input --------------------
    private static void EnsureEventSystemCompatible()
    {
        var es = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
        if (es == null)
        {
            var go = new GameObject("EventSystem", typeof(EventSystem));
            es = go.GetComponent<EventSystem>();
        }

        if (es.GetComponent<StandaloneInputModule>() == null)
            es.gameObject.AddComponent<StandaloneInputModule>();

        // New Input System UI module (reflection, safe if package not installed)
        var t = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
        if (t != null && es.GetComponent(t) == null)
            es.gameObject.AddComponent(t);
    }

    // -------------------- TMP settings helpers --------------------
    private static void EnsureTMPDefaultFontDynamicAndFallback()
    {
        var settings = TMP_Settings.instance;

        var def = TMP_Settings.defaultFontAsset;
        if (def != null)
        {
            if (def.atlasPopulationMode != AtlasPopulationMode.Dynamic)
            {
                def.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                EditorUtility.SetDirty(def);
            }
        }
        else
        {
            Debug.LogWarning("⚠️ TMP_Settings.defaultFontAsset 为空：建议在 Project Settings → TextMeshPro 里设置 Default Font Asset。");
        }

        // fallbackFontAssets null guard
        if (TMP_Settings.fallbackFontAssets == null)
        {
            Debug.LogWarning("⚠️ TMP_Settings.fallbackFontAssets 为 null：请在 TMP Settings 里手动添加 CJK fallback。");
        }
    }

    private static void ForceAllTMPUseDefaultFont(GameObject root)
    {
        var def = TMP_Settings.defaultFontAsset;
        if (def == null) return;

        var texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var t in texts)
        {
            t.font = def;
            EditorUtility.SetDirty(t);
        }

        var dropdowns = root.GetComponentsInChildren<TMP_Dropdown>(true);
        foreach (var dd in dropdowns)
        {
            if (dd.captionText != null) dd.captionText.font = def;
            if (dd.itemText != null) dd.itemText.font = def;
            EditorUtility.SetDirty(dd);
        }

        var inputs = root.GetComponentsInChildren<TMP_InputField>(true);
        foreach (var ip in inputs)
        {
            if (ip.textComponent != null) ip.textComponent.font = def;
            if (ip.placeholder is TextMeshProUGUI ph) ph.font = def;
            EditorUtility.SetDirty(ip);
        }
    }

    // -------------------- UI helpers --------------------
    private static GameObject CreateUI(string name, Transform parent, params Type[] comps)
    {
        var go = new GameObject(name, comps);
        go.transform.SetParent(parent, false);
        if (go.GetComponent<RectTransform>() == null) go.AddComponent<RectTransform>();
        return go;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
    }

    private static void PrefH(RectTransform rt, float h)
    {
        var le = rt.GetComponent<LayoutElement>() ?? rt.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = h;
    }

    private static LayoutElement EnsureLE(GameObject go)
        => go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
}
#endif