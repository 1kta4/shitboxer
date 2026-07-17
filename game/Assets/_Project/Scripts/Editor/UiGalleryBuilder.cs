using System.IO;
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
            AssignPanel(doc, panel);
            GalleryDriver driver = rig.AddComponent<GalleryDriver>();
            AssignStyleSheets(driver);

            // A camera so the Game view has something to render — without one Unity draws "No cameras
            // rendering" over an uncleared framebuffer (the yellow garbage). The UI is a screen-space
            // overlay drawn on top; the camera just clears the backdrop to the screen navy.
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.039f, 0.051f, 0.078f, 1f);
            cam.orthographic = true;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            WirePanelIntoScene();
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

        private static void AssignPanel(UIDocument doc, PanelSettings panel)
        {
            // Best-effort: try the API path first. In this Unity, neither the C# setter nor a
            // SerializedObject write to m_PanelSettings survives SaveScene on a freshly-built UIDocument
            // — the reference serialises back as {fileID: 0} and the panel renders blank. So this is only
            // a first attempt; WirePanelIntoScene() below guarantees the reference on disk afterwards.
            var so = new SerializedObject(doc);
            so.FindProperty("m_PanelSettings").objectReferenceValue = panel;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Guarantee the UIDocument's PanelSettings reference by repairing the saved scene file directly.
        /// The API assignment (see AssignPanel) does not persist here, so we patch the null reference the
        /// serializer wrote. Deterministic: a PanelSettings .asset is referenced as
        /// {fileID: 11400000, guid, type: 2} — the same shape this project's URP settings assets use.
        /// Idempotent: a no-op if the reference is ever non-null (nothing to replace).
        /// </summary>
        private static void WirePanelIntoScene()
        {
            string guid = AssetDatabase.AssetPathToGUID(PanelPath);
            if (string.IsNullOrEmpty(guid)) return;

            string text = File.ReadAllText(ScenePath);
            const string nullRef = "m_PanelSettings: {fileID: 0}";
            if (!text.Contains(nullRef)) return;

            text = text.Replace(nullRef, $"m_PanelSettings: {{fileID: 11400000, guid: {guid}, type: 2}}");
            File.WriteAllText(ScenePath, text);
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
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
