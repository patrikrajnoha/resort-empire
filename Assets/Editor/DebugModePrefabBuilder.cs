#if UNITY_EDITOR
using ResortEmpire.Gameplay.Integration;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

internal static class DebugModePrefabBuilder
{
    [MenuItem("Resort Empire/Debug/Create Debug Panel")]
    private static void Build()
    {
        var root = new GameObject("DebugModePanel", typeof(RectTransform));
        root.SetActive(false);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32767;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        root.AddComponent<GraphicRaycaster>();
        root.transform.localScale = Vector3.one;
        var rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        var controller = root.AddComponent<DebugModePanel>();
        var overlay = Element("ModalOverlay", root.transform, Vector2.zero, Vector2.zero);
        var overlayRect = overlay.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        var blocker = overlay.AddComponent<Image>();
        blocker.color = new Color(0, 0, 0, 0.55f);
        blocker.raycastTarget = true;
        controller.modalOverlay = overlay;
        var panel = Element("Panel", root.transform, new Vector2(800, 560), Vector2.zero);
        var image = panel.AddComponent<Image>();
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_ResortEmpire/Art/Shared/UI/Surfaces/tropical-panel.png");
        image.color = new Color(0.13f, 0.27f, 0.29f);
        controller.panel = panel;
        var title = Text("Title", panel.transform, "DEBUG MODE", new Vector2(0, 218), new Vector2(600, 50));
        title.fontSize = 34;
        var field = Element("CommandInput", panel.transform, new Vector2(544, 60), new Vector2(-80, 135));
        field.AddComponent<Image>().sprite = image.sprite;
        var viewport = Element("Text Area", field.transform, new Vector2(504, 50), Vector2.zero);
        viewport.AddComponent<RectMask2D>();
        var text = Text("Text", viewport.transform, "", Vector2.zero, new Vector2(500, 45));
        text.color = Color.black;
        text.alignment = TextAlignmentOptions.Left;
        var placeholder = Text("Placeholder", viewport.transform, "Enter a command", Vector2.zero, new Vector2(500, 45));
        placeholder.color = new Color32(80, 91, 96, 255);
        placeholder.alignment = TextAlignmentOptions.Left;
        var input = field.AddComponent<TMP_InputField>();
        input.textViewport = viewport.GetComponent<RectTransform>(); input.textComponent = text; input.placeholder = placeholder;
        controller.input = input;
        controller.executeButton = Button("Execute", panel.transform, new Vector2(282, 135), new Vector2(140, 60));
        controller.output = Text("Output", panel.transform, "Type help to list commands.", new Vector2(0, 25), new Vector2(700, 128));
        controller.output.fontSize = 22;
        controller.output.alignment = TextAlignmentOptions.Left;
        controller.coinsButton = Button("Add 100 Coins", panel.transform, new Vector2(-240,-100), new Vector2(220,60));
        controller.energyButton = Button("Add 50 Energy", panel.transform, new Vector2(0,-100), new Vector2(220,60));
        controller.statusButton = Button("Wave Status", panel.transform, new Vector2(240,-100), new Vector2(220,60));
        controller.closeButton = Button("Close", panel.transform, new Vector2(0,-210), new Vector2(200,60));
        var shortcutTint = new Color(0.78f, 0.86f, 0.88f);
        controller.coinsButton.image.color = shortcutTint;
        controller.energyButton.image.color = shortcutTint;
        controller.statusButton.image.color = shortcutTint;
        controller.closeButton.image.color = new Color(0.68f, 0.74f, 0.76f);
        panel.SetActive(false);
        overlay.SetActive(false);
        root.SetActive(true);
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        PrefabUtility.SaveAsPrefabAsset(root, "Assets/Resources/DebugModePanel.prefab");
        Object.DestroyImmediate(root);
    }
    private static GameObject Element(string name, Transform parent, Vector2 size, Vector2 pos)
    {
        var go = new GameObject(name, typeof(RectTransform)); var r = go.GetComponent<RectTransform>();
        r.SetParent(parent,false); r.sizeDelta=size; r.anchoredPosition=pos; return go;
    }
    private static TextMeshProUGUI Text(string name, Transform parent, string value, Vector2 pos, Vector2 size)
    {
        var t=Element(name,parent,size,pos).AddComponent<TextMeshProUGUI>();
        t.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_ResortEmpire/Art/Shared/Fonts/rubik/TextMeshPro/Rubik-Regular SDF.asset");
        t.text=value; t.fontSize=24; t.color=Color.white; t.alignment=TextAlignmentOptions.Center; t.raycastTarget=false; return t;
    }
    private static Button Button(string label, Transform parent, Vector2 pos, Vector2 size)
    {
        var go=Element(label,parent,size,pos); var image=go.AddComponent<Image>();
        image.sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_ResortEmpire/Art/UI/HomeIsland/Buttons/start-wave-button-v2.png");
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        Text("Label", go.transform, label, Vector2.zero, size);
        return button;
    }
}
#endif
