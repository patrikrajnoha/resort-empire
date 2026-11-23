using System.Linq;
using ResortEmpire.Gameplay.Integration;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ResortEmpire.EditorTools
{
    /// <summary>
    /// Builds the Main Menu scene and the S1 MENU button from the existing UI art, and puts the Main Menu first in
    /// Build Settings. Rebuilding replaces manual edits to MainMenu.unity, so tweak the result in place afterwards.
    /// </summary>
    public static class MainMenuSceneSetup
    {
        public const string ScenePath = "Assets/_ResortEmpire/Scenes/MainMenu.unity";
        public const string GameplayScenePath = "Assets/_ResortEmpire/Scenes/Resort.unity";
        private const string HomeIslandPrefabPath = "Assets/_ResortEmpire/Prefabs/UI/HomeIslandView.prefab";
        private const string BackdropPrefabPath = "Assets/_ResortEmpire/Prefabs/UI/ResortBackdrop.prefab";
        private const string EconomyPath = "Assets/_ResortEmpire/Data/Progression/DemoEconomy.asset";
        private const string PanelSpritePath = "Assets/_ResortEmpire/Art/Shared/UI/Surfaces/tropical-panel.png";
        private const string PrimaryButtonPath = "Assets/_ResortEmpire/Art/UI/HomeIsland/Buttons/start-wave-button-v2.png";
        private const string RoundedPath = "Assets/_ResortEmpire/Art/Shared/WorldAndUI/UI/ImplementedScreens/RoundedPanel.png";
        private const string EmblemPath = "Assets/_ResortEmpire/Art/Shared/UI/Icons/resort-emblem.png";
        private const string AlertPath = "Assets/_ResortEmpire/Art/Shared/UI/Icons/alert.png";
        private const string BoldFontPath = "Assets/_ResortEmpire/Art/Shared/Fonts/rubik/TextMeshPro/Rubik-Bold SDF.asset";
        private const string RegularFontPath = "Assets/_ResortEmpire/Art/Shared/Fonts/rubik/TextMeshPro/Rubik-Regular SDF.asset";

        private static readonly Vector2 ReferenceResolution = new Vector2(2340, 1080);
        // 48 dp on a 360 dp tall 720p phone, where the canvas is scaled by 0.6.
        private const float TouchHeight = 165;
        private static readonly Color Ink = new Color(.22f, .31f, .27f);
        private static readonly Color Sand = new Color(.87f, .82f, .7f);
        private static readonly Color Green = new Color(.08f, .55f, .45f);
        private static readonly Color Warning = new Color(.68f, .2f, .12f);
        private static readonly Color HudInk = new Color(.31f, .19f, .12f);

        [MenuItem("Resort Empire/UI/Build Main Menu Scene")]
        public static void BuildAll()
        {
            BuildMainMenuScene();
            AddMenuButtonToHomeIsland();
            PutMainMenuFirstInBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("Main Menu scene, S1 MENU button and Build Settings are ready.");
        }

        public static void BuildMainMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.11f, .55f, .62f);

            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

            var canvas = new GameObject("MainMenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            var root = canvas.transform;

            var backdrop = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BackdropPrefabPath), scene);
            backdrop.transform.SetParent(root, false);
            Stretch(backdrop.transform);
            foreach (var graphic in backdrop.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;

            var shade = Node("Shade", root);
            Stretch(shade);
            var shadeImage = shade.gameObject.AddComponent<Image>();
            shadeImage.color = new Color(.02f, .12f, .14f, .35f);
            shadeImage.raycastTarget = false;

            var safeArea = Node("SafeArea", root);
            Stretch(safeArea);
            safeArea.gameObject.AddComponent<ScreenSafeAreaFitter>();

            var panel = Node("MenuPanel", safeArea);
            Center(panel, new Vector2(1140, 760), Vector2.zero);
            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.sprite = Load<Sprite>(PanelSpritePath);
            panelImage.raycastTarget = true;

            var emblem = Picture("Emblem", panel, Load<Sprite>(EmblemPath));
            Top(emblem, 0, 30, 130, 130);
            var title = Text("Title", panel, "RESORT EMPIRE", Bold, 64, Ink);
            Top(title.transform, 0, 158, 900, 82);
            var subtitle = Text("Subtitle", panel, "Build your island paradise, one wave of guests at a time.", Regular, 27, Ink);
            Top(subtitle.transform, 0, 236, 900, 38);

            var continueButton = MakeButton("ContinueButton", panel, Load<Sprite>(PrimaryButtonPath), Image.Type.Simple, Color.white);
            Top(continueButton.transform, 0, 282, 600, 170);
            var continueLabel = Text("Label", continueButton.transform, "CONTINUE", Bold, 50, Color.white);
            // The button art has transparent margins; centre the label on the orange face.
            Fill(continueLabel.transform, new Vector2(0, .25f), new Vector2(1, .78f));
            var continueDetail = Text("ContinueDetail", panel, "No saved resort yet", Regular, 27, Ink);
            Top(continueDetail.transform, 0, 452, 900, 38);

            var newGameButton = MakeButton("NewGameButton", panel, Load<Sprite>(RoundedPath), Image.Type.Sliced, Sand, new Vector2(440, 100));
            Top(newGameButton.transform, 0, 468, 520, TouchHeight);
            var newGameLabel = Text("Label", newGameButton.transform.Find("Face"), "NEW GAME", Bold, 34, Ink);
            Fill(newGameLabel.transform, Vector2.zero, Vector2.one);

            var messageRow = Node("MessageRow", panel);
            Top(messageRow, 0, 616, 940, 82);
            var alert = Picture("AlertIcon", messageRow, Load<Sprite>(AlertPath));
            Anchor(alert, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(0, 0), new Vector2(44, 44));
            var message = Text("Message", messageRow, "", Regular, 26, Warning);
            message.alignment = TextAlignmentOptions.MidlineLeft;
            Anchor(message.transform, Vector2.zero, Vector2.one, new Vector2(.5f, .5f), new Vector2(30, 0), new Vector2(-60, 0));
            message.enableAutoSizing = true;
            message.fontSizeMin = 22;
            message.fontSizeMax = 26;
            messageRow.gameObject.SetActive(false);

            var loading = Text("LoadingIndicator", panel, "LOADING YOUR RESORT...", Bold, 30, Ink);
            Top(loading.transform, 0, 632, 900, 48);
            loading.gameObject.SetActive(false);

            var dialog = Node("NewGameConfirmation", safeArea);
            Stretch(dialog);
            var dim = dialog.gameObject.AddComponent<Image>();
            dim.color = new Color(.02f, .12f, .14f, .65f);
            dim.raycastTarget = true;
            var dialogPanel = Node("Dialog", dialog);
            Center(dialogPanel, new Vector2(840, 560), Vector2.zero);
            var dialogImage = dialogPanel.gameObject.AddComponent<Image>();
            dialogImage.sprite = Load<Sprite>(PanelSpritePath);
            var dialogTitle = Text("Title", dialogPanel, "NEW GAME", Bold, 46, Ink);
            Top(dialogTitle.transform, 0, 62, 700, 60);
            var dialogText = Text("Message", dialogPanel, "Start a new game?", Regular, 30, Ink);
            Top(dialogText.transform, 0, 134, 680, 210);
            dialogText.enableAutoSizing = true;
            dialogText.fontSizeMin = 22;
            dialogText.fontSizeMax = 30;
            var cancel = MakeButton("CancelButton", dialogPanel, Load<Sprite>(RoundedPath), Image.Type.Sliced, Sand, new Vector2(280, 100));
            Bottom(cancel.transform, -180, 22, 300, TouchHeight);
            Fill(Text("Label", cancel.transform.Find("Face"), "CANCEL", Bold, 32, Ink).transform, Vector2.zero, Vector2.one);
            var confirm = MakeButton("ConfirmButton", dialogPanel, Load<Sprite>(RoundedPath), Image.Type.Sliced, Green, new Vector2(340, 100));
            Bottom(confirm.transform, 155, 22, 360, TouchHeight);
            Fill(Text("Label", confirm.transform.Find("Face"), "START NEW", Bold, 32, Color.white).transform, Vector2.zero, Vector2.one);
            dialog.gameObject.SetActive(false);

            var controller = canvas.AddComponent<MainMenuController>();
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("economy").objectReferenceValue = Load<ResortEconomyConfig>(EconomyPath);
            serialized.FindProperty("continueButton").objectReferenceValue = continueButton;
            serialized.FindProperty("continueDetail").objectReferenceValue = continueDetail;
            serialized.FindProperty("newGameButton").objectReferenceValue = newGameButton;
            serialized.FindProperty("messageRow").objectReferenceValue = messageRow.gameObject;
            serialized.FindProperty("messageText").objectReferenceValue = message;
            serialized.FindProperty("loadingIndicator").objectReferenceValue = loading.gameObject;
            serialized.FindProperty("confirmationDialog").objectReferenceValue = dialog.gameObject;
            serialized.FindProperty("confirmationText").objectReferenceValue = dialogText;
            serialized.FindProperty("confirmButton").objectReferenceValue = confirm;
            serialized.FindProperty("cancelButton").objectReferenceValue = cancel;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        /// <summary>Adds the S1 MENU button under the Resort Value card. It shows only between waves.</summary>
        public static void AddMenuButtonToHomeIsland()
        {
            var root = PrefabUtility.LoadPrefabContents(HomeIslandPrefabPath);
            try
            {
                var top = root.transform.Find("Top");
                var existing = top.Find("MainMenuButton");
                if (existing != null) Object.DestroyImmediate(existing.gameObject);

                var button = MakeButton("MainMenuButton", top, Load<Sprite>(RoundedPath), Image.Type.Sliced, new Color(.99f, .95f, .84f), new Vector2(190, 82));
                var rect = (RectTransform)button.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                // The tap area extends past the card art, like a 48 dp touch target.
                rect.anchoredPosition = new Vector2(24, -185 + TouchHeight / 2);
                rect.sizeDelta = new Vector2(230, TouchHeight);
                Fill(Text("Label", button.transform.Find("Face"), "MENU", Bold, 30, HudInk).transform, Vector2.zero, Vector2.one);
                button.gameObject.AddComponent<CanvasGroup>();
                button.gameObject.AddComponent<ReturnToMainMenuButton>();
                PrefabUtility.SaveAsPrefabAsset(root, HomeIslandPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static void PutMainMenuFirstInBuildSettings()
        {
            var others = EditorBuildSettings.scenes.Where(s => s.path != ScenePath && s.path != GameplayScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true), new EditorBuildSettingsScene(GameplayScenePath, true) }
                .Concat(others).ToArray();
        }

        private static TMP_FontAsset Bold => Load<TMP_FontAsset>(BoldFontPath);
        private static TMP_FontAsset Regular => Load<TMP_FontAsset>(RegularFontPath);

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new System.InvalidOperationException("Missing " + typeof(T).Name + ": " + path);
            return asset;
        }

        private static RectTransform Node(string name, Transform parent)
        {
            var node = new GameObject(name, typeof(RectTransform));
            node.layer = LayerMask.NameToLayer("UI");
            node.transform.SetParent(parent, false);
            return (RectTransform)node.transform;
        }

        private static Button MakeButton(string name, Transform parent, Sprite sprite, Image.Type type, Color color, Vector2? faceSize = null)
        {
            var node = Node(name, parent);
            var image = node.gameObject.AddComponent<Image>();
            image.raycastTarget = true;
            var face = image;
            if (faceSize.HasValue)
            {
                // A transparent tap area around a smaller face; a culled transparent mesh would not receive raycasts.
                image.color = Color.clear;
                node.GetComponent<CanvasRenderer>().cullTransparentMesh = false;
                face = Node("Face", node).gameObject.AddComponent<Image>();
                Center(face.transform, faceSize.Value, Vector2.zero);
                face.raycastTarget = false;
            }
            face.sprite = sprite;
            face.type = type;
            face.color = color;
            var button = node.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(.82f, .88f, .82f);
            colors.disabledColor = new Color(.6f, .6f, .6f, .7f);
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            return button;
        }

        private static TextMeshProUGUI Text(string name, Transform parent, string value, TMP_FontAsset font, float size, Color color)
        {
            var text = Node(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.text = value;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform Picture(string name, Transform parent, Sprite sprite)
        {
            var image = Node(name, parent).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image.rectTransform;
        }

        private static void Stretch(Transform target) => Anchor(target, Vector2.zero, Vector2.one, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
        private static void Center(Transform target, Vector2 size, Vector2 position) =>
            Anchor(target, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(.5f, .5f), position, size);
        private static void Top(Transform target, float x, float y, float width, float height) =>
            Anchor(target, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(x, -y), new Vector2(width, height));
        private static void Bottom(Transform target, float x, float y, float width, float height) =>
            Anchor(target, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(x, y), new Vector2(width, height));
        private static void Fill(Transform target, Vector2 min, Vector2 max) =>
            Anchor(target, min, max, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);

        private static void Anchor(Transform target, Vector2 min, Vector2 max, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)target;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
