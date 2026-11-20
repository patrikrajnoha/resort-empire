using System.Linq;
using ResortEmpire.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class AreaUnlockUiSetup
{
    private static readonly Color Ink = new Color(.22f, .31f, .27f);

    [MenuItem("Resort Empire/UI/Apply Area Unlock Style")]
    public static void Apply()
    {

        const string pathPrefab = "Assets/_ResortEmpire/Prefabs/UI/AreaUnlockPanel.prefab";
        var root = PrefabUtility.LoadPrefabContents(pathPrefab);
        try { Style(root); PrefabUtility.SaveAsPrefabAsset(root, pathPrefab); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static void Style(GameObject root)
    {
        var card = (RectTransform)Find(root, "AreaCard");
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, .5f);
        card.anchoredPosition = Vector2.zero; card.sizeDelta = new Vector2(1020, 650); card.localScale = Vector3.one;
        var fit = card.GetComponent<DesignStage>();
        if (!fit) fit = card.gameObject.AddComponent<DesignStage>();
        fit.ReferenceSize = new Vector2(1280, 820);
        var frame = card.GetComponent<Image>();
        frame.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_ResortEmpire/Art/Shared/UI/Surfaces/tropical-panel.png");
        frame.type = Image.Type.Simple; frame.color = Color.white;
        Find(root, "IslandDim").GetComponent<Image>().color = new Color(.02f, .12f, .14f, .65f);
        foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            text.color = Ink; text.enableAutoSizing = false; text.fontStyle = FontStyles.Normal;
        }

        var badge = Find(root, "NewAreaBadge"); badge.SetParent(card, false);
        Place(badge, 195, 50, 750, 65);
        var badgeImage = badge.GetComponent<Image>(); badgeImage.color = Color.clear; badgeImage.raycastTarget = false;
        var heading = badge.Find("Label").GetComponent<TextMeshProUGUI>();
        Stretch(heading.rectTransform); heading.text = "NEW AREA AVAILABLE!"; heading.fontSize = 34; heading.alignment = TextAlignmentOptions.MidlineLeft;
        var emblem = ImageNode(card, "AreaEmblem", Sprite("compass")); Place(emblem.transform, 65, 30, 112, 112);

        var artBack = ImageNode(card, "IllustrationBackground", Rounded());
        Place(artBack.transform, 60, 174, 350, 310); artBack.type = Image.Type.Sliced; artBack.preserveAspect = false;
        artBack.color = new Color(.82f, .93f, .86f); artBack.transform.SetAsFirstSibling();
        var illustration = Find(root, "AreaIllustration").GetComponent<Image>();
        illustration.sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/_ResortEmpire/Art/Shared/WorldAndUI/Buildings/Unlockable/pool-level-01.png").OfType<Sprite>().First(x => x.name == "pool-level-01_0");
        Place(illustration.transform, 70, 186, 330, 285); illustration.preserveAspect = true; illustration.color = Color.white; illustration.raycastTarget = false;
        Place(Find(root, "Title"), 450, 175, 500, 58); Label(Find(root, "Title"), 33);
        var checks = card.GetComponentsInChildren<Image>(true).Where(x => x.name == "CheckIcon").ToArray();
        var lines = card.GetComponentsInChildren<TextMeshProUGUI>(true).Where(x => x.name == "Text").ToArray();
        if (checks.Length >= 2 && lines.Length >= 2)
        {
            checks[0].sprite = Sprite("check"); checks[1].sprite = Sprite("worker");
            for (int i = 0; i < 2; i++)
            {
                checks[i].color = Color.white; checks[i].preserveAspect = true; checks[i].raycastTarget = false;
                Place(checks[i].transform, 450, 252 + i * 70, 52, 52);
                Place(lines[i].transform, 515, 256 + i * 70, 435, 48); Label(lines[i].transform, 25);
            }
            lines[1].text = "Includes 1 extra Worker";
        }
        var costBack = ImageNode(card, "CostBackground", Rounded());
        Place(costBack.transform, 450, 410, 485, 76); costBack.type = Image.Type.Sliced; costBack.preserveAspect = false;
        costBack.color = new Color(.94f, .88f, .73f); costBack.transform.SetSiblingIndex(1);
        var coin = Find(root, "CoinIcon").GetComponent<Image>(); coin.sprite = Sprite("coins"); coin.color = Color.white; coin.preserveAspect = true;
        Place(coin.transform, 466, 420, 57, 57);
        Place(Find(root, "CoinCost"), 545, 422, 365, 52); Label(Find(root, "CoinCost"), 31);
        Find(root, "CoinCost").GetComponent<TextMeshProUGUI>().color = new Color(.55f, .35f, .09f);

        var later = Find(root, "LaterButton");
        if (!later)
        {
            later = new GameObject("LaterButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button)).transform;
            later.SetParent(card, false);
            var label = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).transform;
            label.SetParent(later, false); var tmp = label.GetComponent<TextMeshProUGUI>(); tmp.font = heading.font; tmp.text = "LATER";
        }
        later.SetParent(card, false); Place(later, 240, 532, 170, 66); Skin(later, new Color(.87f, .82f, .7f));
        var laterLabel = later.Find("Label").GetComponent<TextMeshProUGUI>(); Stretch(laterLabel.rectTransform); laterLabel.fontSize = 26; laterLabel.alignment = TextAlignmentOptions.Midline; laterLabel.color = Ink;
        var unlock = Find(root, "UnlockExpandButton"); unlock.SetParent(card, false);
        Place(unlock, 440, 532, 390, 66); Skin(unlock, new Color(.08f, .55f, .45f));
        var unlockIcon = unlock.Find("UnlockIcon").GetComponent<Image>(); unlockIcon.sprite = Sprite("unlock"); unlockIcon.color = Color.white; unlockIcon.preserveAspect = true;
        Place(unlockIcon.transform, 15, 10, 46, 46); unlockIcon.raycastTarget = false;
        var unlockLabel = unlock.Find("Label").GetComponent<TextMeshProUGUI>();
        Place(unlockLabel.transform, 66, 0, 310, 66); unlockLabel.fontSize = 26; unlockLabel.alignment = TextAlignmentOptions.Midline; unlockLabel.color = Color.white;
        foreach (var button in new[] { later.GetComponent<Button>(), unlock.GetComponent<Button>() })
        {
            button.targetGraphic = button.GetComponent<Image>();
            var colors = button.colors; colors.normalColor = colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(.82f, .88f, .82f); colors.disabledColor = new Color(.6f, .6f, .6f, .7f); button.colors = colors;
        }
    }

    private static Transform Find(GameObject root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
    private static Sprite Sprite(string name) => ResortArtCatalog.LoadSprite("AreaUnlock", name);
    private static Sprite Rounded() => AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_ResortEmpire/Art/Shared/WorldAndUI/UI/ImplementedScreens/RoundedPanel.png");
    private static Image ImageNode(Transform parent, string name, Sprite sprite)
    {
        var child = parent.Find(name);
        if (!child) { child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).transform; child.SetParent(parent, false); }
        var image = child.GetComponent<Image>(); image.sprite = sprite; image.color = Color.white; image.preserveAspect = true; image.raycastTarget = false; return image;
    }
    private static void Skin(Transform target, Color color) { var image = target.GetComponent<Image>(); image.sprite = Rounded(); image.type = Image.Type.Sliced; image.color = color; }
    private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private static void Label(Transform target, float size) { var text = target.GetComponent<TextMeshProUGUI>(); text.fontSize = size; text.alignment = TextAlignmentOptions.MidlineLeft; }
    private static void Place(Transform target, float x, float y, float width, float height)
    {
        var rect = (RectTransform)target; rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
    }
}
