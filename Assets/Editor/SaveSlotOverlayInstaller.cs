using ResortEmpire.Gameplay.Integration;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace ResortEmpire.EditorTools
{
    internal static class SaveSlotOverlayInstaller
    {
        private const string PrefabPath = "Assets/_ResortEmpire/Prefabs/UI/SaveManagerPopup.prefab";
        private const string PanelPath = "Assets/_ResortEmpire/Art/Shared/UI/Surfaces/tropical-panel.png";
        private const string ButtonPath = "Assets/_ResortEmpire/Art/UI/HomeIsland/Buttons/start-wave-button-v2.png";
        private const string FontPath = "Assets/_ResortEmpire/Art/Shared/Fonts/rubik/TextMeshPro/Rubik-Regular SDF.asset";

        [MenuItem("Resort Empire/Save Slots/Create Overlay Prefab")]
        private static void CreateOverlayPrefab()
        {
            var root = new GameObject("SaveManagerPopup", typeof(RectTransform));
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var controller = root.AddComponent<SaveSlotOverlayController>();
            controller.Configure(
                AssetDatabase.LoadAssetAtPath<Sprite>(PanelPath),
                AssetDatabase.LoadAssetAtPath<Sprite>(ButtonPath),
                AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath));
            controller.BuildPrefabContents();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Debug.Log("Created the Save Manager popup prefab. It is ready for later menu integration.", Selection.activeObject);
        }
    }
}
