using System.Linq;
using ResortEmpire.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class UpgradeAvailableUiSetup
{
    private static readonly Color Ink = new Color(.22f, .31f, .27f);

    [MenuItem("Resort Empire/UI/Apply Upgrade Available Style")]
    public static void Apply()
    {

        const string prefabPath = "Assets/_ResortEmpire/Prefabs/UI/UpgradeSuggestionPanel.prefab";
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Style(root);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static void Style(GameObject root)
    {
            var modal = (RectTransform)root.transform.Find("Modal");
            modal.anchorMin = modal.anchorMax = modal.pivot = new Vector2(.5f, .5f);
            modal.anchoredPosition = Vector2.zero;
            modal.sizeDelta = new Vector2(900, 630);
            modal.localScale = Vector3.one;
            var fit = modal.GetComponent<DesignStage>();
            if (!fit) fit = modal.gameObject.AddComponent<DesignStage>();
            fit.ReferenceSize = new Vector2(1280, 820);
            var panel = modal.GetComponent<Image>();
            panel.sprite = Sprite("tropical-panel"); panel.type = Image.Type.Simple; panel.color = Color.white;
            root.transform.Find("IslandDim").GetComponent<Image>().color = new Color(.02f, .12f, .14f, .65f);

            foreach (var text in modal.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                text.color = Ink;
                text.enableAutoSizing = false;
                text.fontStyle = FontStyles.Normal;
                text.verticalAlignment = VerticalAlignmentOptions.Geometry;
            }
            Place(modal.Find("Title"), 190, 55, 630, 58);
            Label(modal.Find("Title"), 34, TextAlignmentOptions.MidlineLeft);
            var emblem = ImageNode(modal, "UpgradeEmblem", Sprite("upgrade-emblem"));
            Place(emblem.transform, 65, 35, 112, 112);

            var card = modal.Find("SuggestionCard");
            Place(card, 60, 155, 780, 150);
            Skin(card, new Color(.94f, .89f, .77f));
            var iconBack = card.Find("IconBackground");
            Place(iconBack, 18, 11, 128, 128); Skin(iconBack, new Color(1, .98f, .91f));
            var service = card.Find("RestaurantIcon").GetComponent<Image>();
            Place(service.transform, 28, 21, 108, 108);
            service.sprite = Sprite("food"); service.color = Color.white; service.preserveAspect = true; service.raycastTarget = false;
            Place(card.Find("BuildingName"), 172, 23, 570, 46);
            Label(card.Find("BuildingName"), 34, TextAlignmentOptions.MidlineLeft);
            Place(card.Find("Description"), 172, 80, 570, 42);
            Label(card.Find("Description"), 25, TextAlignmentOptions.MidlineLeft);
            card.Find("Description").GetComponent<TextMeshProUGUI>().text = "More seats shorten the queue!";

            var capacity = ImageNode(modal, "CapacityBackground", Rounded());
            Place(capacity.transform, 60, 325, 780, 82);
            capacity.color = new Color(.84f, .93f, .86f); capacity.type = Image.Type.Sliced;
            capacity.transform.SetSiblingIndex(card.GetSiblingIndex() + 1);
            Place(modal.Find("CapacityLabel"), 88, 342, 440, 46);
            Label(modal.Find("CapacityLabel"), 28, TextAlignmentOptions.MidlineLeft);
            Place(modal.Find("CapacityValue"), 545, 340, 265, 50);
            Label(modal.Find("CapacityValue"), 34, TextAlignmentOptions.MidlineRight);
            modal.Find("CapacityValue").GetComponent<TextMeshProUGUI>().text = "6 -> <color=#087E68>9</color>";
            foreach (var name in new[] { "QualityLabel", "QualityValue", "ResortValueIcon", "ResortValueReward", "CostIcon" })
                modal.Find(name).gameObject.SetActive(false);

            var costLabel = modal.Find("CostLabel");
            if (!costLabel)
            {
                costLabel = new GameObject("CostLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).transform;
                costLabel.SetParent(modal, false);
            }
            var costText = costLabel.GetComponent<TextMeshProUGUI>();
            costText.font = modal.Find("CostValue").GetComponent<TextMeshProUGUI>().font;
            costText.text = "UPGRADE COST"; costText.color = Ink;
            Place(costLabel, 210, 435, 235, 45); Label(costLabel, 24, TextAlignmentOptions.MidlineLeft);
            var coin = ImageNode(modal, "CostCoin", AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_ResortEmpire/Art/Shared/UI/Icons/coins.png"));
            Place(coin.transform, 455, 425, 58, 58);
            Place(modal.Find("CostValue"), 525, 431, 190, 48);
            Label(modal.Find("CostValue"), 34, TextAlignmentOptions.MidlineLeft);
            modal.Find("CostValue").GetComponent<TextMeshProUGUI>().color = new Color(.55f, .35f, .09f);

            var later = modal.Find("LaterButton");
            Place(later, 170, 520, 180, 66); Skin(later, new Color(.87f, .82f, .7f));
            StretchLabel(later, Ink);
            var upgrade = modal.Find("UpgradeNowButton");
            Place(upgrade, 380, 520, 350, 66); Skin(upgrade, new Color(.08f, .55f, .45f));
            StretchLabel(upgrade, Color.white);
            foreach (var button in new[] { later.GetComponent<Button>(), upgrade.GetComponent<Button>() })
            {
                var colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1, 1, 1, 1);
                colors.pressedColor = new Color(.82f, .88f, .82f);
                colors.disabledColor = new Color(.6f, .6f, .6f, .7f);
                button.colors = colors;
            }
            var artwork = root.GetComponent<UpgradeSuggestionArt>();
            if (!artwork) artwork = root.AddComponent<UpgradeSuggestionArt>();
            artwork.ServiceIcons = new[] { Sprite("rooms"), Sprite("drinks"), Sprite("food"), Sprite("relax"), Sprite("ocean-view") };
    }

    private static Sprite Sprite(string name) => ResortArtCatalog.LoadSprite("UpgradeAvailable", name);
    private static Sprite Rounded() => AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_ResortEmpire/Art/Shared/WorldAndUI/UI/ImplementedScreens/RoundedPanel.png");
    private static void Skin(Transform target, Color color)
    {
        var image = target.GetComponent<Image>(); image.sprite = Rounded(); image.type = Image.Type.Sliced; image.color = color;
    }
    private static Image ImageNode(Transform parent, string name, Sprite sprite)
    {
        var child = parent.Find(name);
        if (!child) { child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).transform; child.SetParent(parent, false); }
        var image = child.GetComponent<Image>(); image.sprite = sprite; image.color = Color.white; image.preserveAspect = true; image.raycastTarget = false;
        return image;
    }
    private static void StretchLabel(Transform button, Color color)
    {
        var rect = (RectTransform)button.Find("Label");
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var label = rect.GetComponent<TextMeshProUGUI>(); label.color = color; label.fontSize = 27; label.alignment = TextAlignmentOptions.Midline;
    }
    private static void Label(Transform target, float size, TextAlignmentOptions alignment)
    {
        var text = target.GetComponent<TextMeshProUGUI>(); text.fontSize = size; text.alignment = alignment;
    }
    private static void Place(Transform target, float x, float y, float width, float height)
    {
        var rect = (RectTransform)target; rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
    }
}
