#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class LLMChatPrefabBuilder_NoFont
{
    private const string PREFAB_DIR = "Assets/UI/Prefabs/LLMChat";

    [MenuItem("Tools/Thesis/Build LLM Chat Prefabs (NoFont)")]
    public static void BuildPrefabs()
    {
        EnsureDir(PREFAB_DIR);

        BuildNPCBubble(Path.Combine(PREFAB_DIR, "NPCBubble.prefab"));
        BuildPlayerBubble(Path.Combine(PREFAB_DIR, "PlayerBubble.prefab"));
        BuildEffectLine(Path.Combine(PREFAB_DIR, "EffectLine.prefab"));

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("✅ 已生成 Prefabs：NPCBubble / PlayerBubble / EffectLine（NoFont，无文字无字体设置）。");
    }

    // ---------------- Prefab 1: NPCBubble (Left) ----------------
    private static void BuildNPCBubble(string savePath)
    {
        var root = NewUI("NPCBubble", typeof(RectTransform), typeof(LayoutElement));
        var rootRT = root.GetComponent<RectTransform>();
        // This root is meant to be a child of Content (VerticalLayoutGroup)
        rootRT.anchorMin = new Vector2(0, 1);
        rootRT.anchorMax = new Vector2(1, 1);
        rootRT.pivot = new Vector2(0.5f, 1);
        rootRT.sizeDelta = new Vector2(0, 0);

        var rootLE = root.GetComponent<LayoutElement>();
        rootLE.flexibleWidth = 1;
        rootLE.minHeight = 0;

        // BubbleBG
        var bubbleBG = NewUI("BubbleBG", typeof(RectTransform), typeof(Image), typeof(ContentSizeFitter), typeof(LayoutElement));
        bubbleBG.transform.SetParent(root.transform, false);

        var bgRT = bubbleBG.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0, 1); // top-left inside row
        bgRT.anchorMax = new Vector2(0, 1);
        bgRT.pivot = new Vector2(0, 1);
        bgRT.anchoredPosition = Vector2.zero;
        bgRT.sizeDelta = new Vector2(520, 120); // initial; will expand by fitter

        var bgImg = bubbleBG.GetComponent<Image>();
        bgImg.color = new Color(1f, 1f, 1f, 0.12f); // placeholder; you will replace with pixel 9-slice
        bgImg.raycastTarget = false;

        var bgLE = bubbleBG.GetComponent<LayoutElement>();
        bgLE.preferredWidth = 520;     // wrap target width
        bgLE.flexibleWidth = 0;

        var csf = bubbleBG.GetComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Text
        var txtGO = NewUI("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        txtGO.transform.SetParent(bubbleBG.transform, false);

        var txtRT = txtGO.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = new Vector2(16, 12);
        txtRT.offsetMax = new Vector2(-16, -12);

        var tmp = txtGO.GetComponent<TextMeshProUGUI>();
        tmp.text = ""; // NO text
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.fontSize = 28;
        tmp.color = new Color(1f, 1f, 1f, 0.92f);

        SavePrefab(root, savePath);
    }

    // ---------------- Prefab 2: PlayerBubble (Right) ----------------
    private static void BuildPlayerBubble(string savePath)
    {
        var root = NewUI("PlayerBubble", typeof(RectTransform), typeof(LayoutElement));
        var rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = new Vector2(0, 1);
        rootRT.anchorMax = new Vector2(1, 1);
        rootRT.pivot = new Vector2(0.5f, 1);
        rootRT.sizeDelta = new Vector2(0, 0);

        var rootLE = root.GetComponent<LayoutElement>();
        rootLE.flexibleWidth = 1;

        // AlignRight container
        var align = NewUI("AlignRight", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        align.transform.SetParent(root.transform, false);

        var alignRT = align.GetComponent<RectTransform>();
        alignRT.anchorMin = new Vector2(0, 1);
        alignRT.anchorMax = new Vector2(1, 1);
        alignRT.pivot = new Vector2(0.5f, 1);
        alignRT.sizeDelta = new Vector2(0, 0);

        var hlg = align.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.UpperRight;
        hlg.padding = new RectOffset(0, 0, 0, 0);
        hlg.spacing = 0;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;  // make the align container take full row width
        hlg.childForceExpandHeight = false;

        // BubbleBG
        var bubbleBG = NewUI("BubbleBG", typeof(RectTransform), typeof(Image), typeof(ContentSizeFitter), typeof(LayoutElement));
        bubbleBG.transform.SetParent(align.transform, false);

        var bgRT = bubbleBG.GetComponent<RectTransform>();
        bgRT.pivot = new Vector2(1, 1);

        var bgImg = bubbleBG.GetComponent<Image>();
        bgImg.color = new Color(0.16f, 0.45f, 0.9f, 0.18f); // placeholder
        bgImg.raycastTarget = false;

        var bgLE = bubbleBG.GetComponent<LayoutElement>();
        bgLE.preferredWidth = 520; // wrap target width
        bgLE.flexibleWidth = 0;

        var csf = bubbleBG.GetComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Text
        var txtGO = NewUI("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        txtGO.transform.SetParent(bubbleBG.transform, false);

        var txtRT = txtGO.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = new Vector2(16, 12);
        txtRT.offsetMax = new Vector2(-16, -12);

        var tmp = txtGO.GetComponent<TextMeshProUGUI>();
        tmp.text = ""; // NO text
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.fontSize = 28;
        tmp.color = new Color(1f, 1f, 1f, 0.92f);

        SavePrefab(root, savePath);
    }

    // ---------------- Prefab 3: EffectLine (Center small text, no bubble) ----------------
    private static void BuildEffectLine(string savePath)
    {
        var root = NewUI("EffectLine", typeof(RectTransform), typeof(LayoutElement));
        var rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = new Vector2(0, 1);
        rootRT.anchorMax = new Vector2(1, 1);
        rootRT.pivot = new Vector2(0.5f, 1);
        rootRT.sizeDelta = new Vector2(0, 0);

        var le = root.GetComponent<LayoutElement>();
        le.flexibleWidth = 1;

        var txtGO = NewUI("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        txtGO.transform.SetParent(root.transform, false);

        var txtRT = txtGO.GetComponent<RectTransform>();
        txtRT.anchorMin = new Vector2(0, 1);
        txtRT.anchorMax = new Vector2(1, 1);
        txtRT.pivot = new Vector2(0.5f, 1);
        txtRT.offsetMin = new Vector2(12, -2);
        txtRT.offsetMax = new Vector2(-12, -2);

        var tmp = txtGO.GetComponent<TextMeshProUGUI>();
        tmp.text = ""; // NO text
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 24;
        tmp.color = new Color(1f, 1f, 1f, 0.65f);

        SavePrefab(root, savePath);
    }

    // ---------------- Utils ----------------
    private static GameObject NewUI(string name, params System.Type[] components)
    {
        var go = new GameObject(name, components);
        var rt = go.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
        }
        return go;
    }

    private static void EnsureDir(string dir)
    {
        if (AssetDatabase.IsValidFolder(dir)) return;

        // Create nested dirs safely
        var parts = dir.Split('/');
        var current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }

    private static void SavePrefab(GameObject root, string savePath)
    {
        // If exists, overwrite
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(savePath);
        if (existing != null)
        {
            // SaveAsPrefabAsset will overwrite the file at the same path
        }

        PrefabUtility.SaveAsPrefabAsset(root, savePath);
        Object.DestroyImmediate(root);
    }
}
#endif