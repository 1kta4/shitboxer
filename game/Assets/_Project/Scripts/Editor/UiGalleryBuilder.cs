using Shitboxer.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Shitboxer.Editor
{
    /// <summary>
    /// Builds a standalone scene that renders the garage UI with canned data, so the Y2K design can be
    /// looked at (and clicked, while paused) in isolation before it's wired into the real run. Reliable
    /// asset/scene creation through the real Unity API — the repo's pattern (see RaceTrackBuilder).
    /// Regenerable: run it again to rebuild the scene from scratch.
    /// </summary>
    public static class UiGalleryBuilder
    {
        private const string UiDir = "Assets/_Project/Scripts/UI";
        private const string UssDir = UiDir + "/USS";
        private const string PanelPath = UiDir + "/ShitboxerPanel.asset";
        private const string ScenePath = "Assets/_Project/Scenes/UiGallery.unity";

        [MenuItem("Shitboxer/Build UI Gallery")]
        public static void Build()
        {
            PanelSettings panel = EnsurePanel();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var rig = new GameObject("UiGalleryRig");
            UIDocument doc = rig.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            GalleryDriver driver = rig.AddComponent<GalleryDriver>();
            AssignStyleSheets(driver);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Shitboxer] Built the UI gallery at {ScenePath} — open it and press Play. " +
                      "timeScale is forced to 0 in there, so it also tests that clicks work while paused.");
        }

        private static PanelSettings EnsurePanel()
        {
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (!panel)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                panel.scaleMode = PanelScaleMode.ConstantPixelSize;
                AssetDatabase.CreateAsset(panel, PanelPath);
            }

            if (panel.themeStyleSheet == null)
            {
                string[] themes = AssetDatabase.FindAssets("t:ThemeStyleSheet");
                if (themes.Length > 0)
                {
                    panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                        AssetDatabase.GUIDToAssetPath(themes[0]));
                }
                else
                {
                    Debug.LogWarning("[Shitboxer] No ThemeStyleSheet in the project, so the panel has no theme "
                        + "and text may not render. Fix once: Assets > Create > UI Toolkit > Panel Settings Asset "
                        + "(that generates UnityDefaultRuntimeTheme.tss), then re-run Shitboxer > Build UI Gallery.");
                }
            }

            EditorUtility.SetDirty(panel);
            AssetDatabase.SaveAssets();
            return panel;
        }

        private static void AssignStyleSheets(GalleryDriver driver)
        {
            var sheets = new[]
            {
                AssetDatabase.LoadAssetAtPath<StyleSheet>(UssDir + "/Tokens.uss"),
                AssetDatabase.LoadAssetAtPath<StyleSheet>(UssDir + "/Shitboxer.uss"),
                AssetDatabase.LoadAssetAtPath<StyleSheet>(UssDir + "/Garage.uss"),
            };

            var so = new SerializedObject(driver);
            SerializedProperty arr = so.FindProperty("styleSheets");
            arr.arraySize = sheets.Length;
            for (int i = 0; i < sheets.Length; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = sheets[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
