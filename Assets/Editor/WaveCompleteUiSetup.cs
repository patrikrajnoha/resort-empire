using System.Linq;
using ResortEmpire.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class WaveCompleteUiSetup
{
    [MenuItem("Resort Empire/UI/Apply Wave Complete Style")]
    public static void Apply()
    {

        const string pathPrefab = "Assets/_ResortEmpire/Prefabs/UI/WaveComplete.prefab";
        var root = PrefabUtility.LoadPrefabContents(pathPrefab);
        try
        {
            var content = (RectTransform)root.transform.Find("Content");
            content.sizeDelta = new Vector2(840, 450);
            content.GetComponent<DesignStage>().ReferenceSize = new Vector2(960, 540);
            content.localScale = Vector3.one;
            var rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_ResortEmpire/Art/Shared/WorldAndUI/UI/ImplementedScreens/RoundedPanel.png");
            var card = content.Find("ResultCard");
            if (!card)
            {
                card = new GameObject("ResultCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).transform;
                card.SetParent(content, false);
            }
            var cr = (RectTransform)card;
            cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.offsetMin = Vector2.zero; cr.offsetMax = Vector2.zero;
            var ci = card.GetComponent<Image>(); ci.sprite = rounded; ci.type = Image.Type.Sliced; ci.color = new Color(1,.97f,.9f); ci.raycastTarget = true;
            card.SetAsFirstSibling();
            var backdrop = root.transform.Find("Background").GetComponent<Image>();
            backdrop.color = new Color(.02f,.12f,.14f,.72f);
            var confetti = (RectTransform)content.Find("Confetti");
            foreach (Transform child in confetti.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            confetti.anchorMin = Vector2.zero; confetti.anchorMax = Vector2.one; confetti.offsetMin = confetti.offsetMax = Vector2.zero;
            confetti.SetAsLastSibling();
            Place(content.Find("Heading"), 140, 28, 560, 40);
            var heading = content.Find("Heading").GetComponent<TextMeshProUGUI>(); heading.fontSize = 29; heading.color = new Color(.22f,.32f,.29f);
            Place(content.Find("Rating"), 280, 84, 280, 48);
            var rating = content.Find("Rating").GetComponent<StarRatingView>();
            rating.Filled = Sprite("rating-star"); rating.Empty = Sprite("rating-empty");
            for (int i=0;i<rating.Stars.Length;i++) { Place(rating.Stars[i].transform, i*56, 0, 48, 48); rating.Stars[i].preserveAspect=true; }
            rating.SetRating(rating.Rating);
            Place(content.Find("PerfectStayBadge"), 300, 143, 240, 24);
            Place(content.Find("PerformancePanel"), 110, 184, 280, 173);
            Place(content.Find("RewardsPanel"), 450, 184, 280, 173);
            foreach(var name in new[]{"PerformancePanel", "RewardsPanel"})
            {
                var panel = content.Find(name); var image=panel.GetComponent<Image>(); image.sprite=rounded; image.type=Image.Type.Sliced; image.color=new Color(.9f,.85f,.73f);
                var fill=panel.Find("Fill").GetComponent<Image>(); fill.color=new Color(1,.99f,.95f); fill.rectTransform.sizeDelta=new Vector2(276,169);
            }
            foreach(var text in content.GetComponentsInChildren<TextMeshProUGUI>(true))
                if(text.transform.parent && (text.transform.parent.name=="PerformancePanel" || text.transform.parent.name=="RewardsPanel")) text.color = new Color(.25f,.32f,.29f);
            SetIcon(content,"RewardsPanel/Coins/Icon","coins");
            SetIcon(content,"RewardsPanel/Energy/Icon","energy");
            SetIcon(content,"RewardsPanel/ResortValue/Icon","resort-emblem");
            content.Find("RewardsPanel/Coins/Amount").GetComponent<TextMeshProUGUI>().color = new Color(.55f,.35f,.09f);
            content.Find("RewardsPanel/Energy/Amount").GetComponent<TextMeshProUGUI>().color = new Color(.04f,.46f,.4f);
            content.Find("RewardsPanel/ResortValue/Amount").GetComponent<TextMeshProUGUI>().color = new Color(.55f,.35f,.09f);
            var emblem = content.Find("ResortEmblem");
            if (!emblem) { emblem = new GameObject("ResortEmblem", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).transform; emblem.SetParent(content,false); }
            Place(emblem,48,20,80,80); emblem.GetComponent<Image>().sprite=Sprite("resort-emblem"); emblem.GetComponent<Image>().preserveAspect=true; emblem.GetComponent<Image>().raycastTarget=false;
            Place(content.Find("ClaimRewardsButton"), 290, 383, 260, 48);
            var button=content.Find("ClaimRewardsButton"); button.GetComponent<Image>().sprite=rounded; button.GetComponent<Image>().color=new Color(.08f,.53f,.45f);
            var buttonFill=button.Find("Fill").GetComponent<Image>(); buttonFill.color=new Color(.13f,.7f,.58f); buttonFill.rectTransform.sizeDelta=new Vector2(255,43);
            Place(button.Find("Label"), 38, 0, 210, 44); button.Find("Label").GetComponent<TextMeshProUGUI>().color=Color.white;
            var celebration=root.GetComponent<WaveCompleteCelebration>(); if(!celebration) celebration=root.AddComponent<WaveCompleteCelebration>();
            celebration.Content=content; celebration.ConfettiLayer=confetti; celebration.Rating=rating; celebration.ConfettiSprite=Sprite("confetti");
            PrefabUtility.SaveAsPrefabAsset(root,pathPrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    private static Sprite Sprite(string name) => ResortArtCatalog.LoadSprite("WaveComplete", name);
    private static void SetIcon(Transform root,string path,string asset)
    {
        var image=root.Find(path).GetComponent<Image>(); image.sprite=Sprite(asset); image.preserveAspect=true; image.raycastTarget=false;
        image.rectTransform.sizeDelta=new Vector2(24,24); image.rectTransform.anchoredPosition=new Vector2(-3,-1);
    }
    private static void Place(Transform t,float x,float y,float w,float h)
    {
        var r=(RectTransform)t; r.anchorMin=r.anchorMax=new Vector2(0,1); r.pivot=new Vector2(0,1); r.anchoredPosition=new Vector2(x,-y); r.sizeDelta=new Vector2(w,h);
    }
}
