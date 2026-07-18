using System.Collections.Generic;
using System.IO;
using Shitboxer.Meta;
using Shitboxer.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Shitboxer.Editor
{
    /// <summary>
    /// Builds the MainMenu scene — the game's entry point — with a UI rig (UIDocument + MainMenuHost),
    /// a clearing camera, the ShitboxerPanel + the baked-v3 stylesheets, and the PartPool wired for the
    /// Collection screen. Registers it FIRST in Build Settings so the game boots to the menu. Reuses the
    /// saved-scene panel-ref repair the gallery/race builders need. Regenerable.
    /// </summary>
    public static class MainMenuBuilder
    {
        private const string UiDir = "Assets/_Project/Scripts/UI";
        private const string PanelPath = UiDir + "/ShitboxerPanel.asset";
        private const string ScenePath = "Assets/_Project/Scenes/MainMenu.unity";
        private const string PoolPath = "Assets/_Project/Settings/Parts/PartPool.asset";

        [MenuItem("Shitboxer/Build Main Menu")]
        public static void Build()
        {
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                Debug.LogError("[Shitboxer] ShitboxerPanel.asset missing — run 'Shitboxer/Build UI Gallery' once first.");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.039f, 0.051f, 0.078f, 1f);
            cam.orthographic = true;

            var rig = new GameObject("MenuRig");
            UIDocument doc = rig.AddComponent<UIDocument>();
            var docSo = new SerializedObject(doc);
            docSo.FindProperty("m_PanelSettings").objectReferenceValue = panel;
            docSo.ApplyModifiedPropertiesWithoutUndo();

            MainMenuHost host = rig.AddComponent<MainMenuHost>();
            var sheets = new[]
            {
                AssetDatabase.LoadAssetAtPath<StyleSheet>(UiDir + "/USS/Tokens.uss"),
                AssetDatabase.LoadAssetAtPath<StyleSheet>(UiDir + "/USS/Shitboxer.uss"),
                AssetDatabase.LoadAssetAtPath<StyleSheet>(UiDir + "/USS/Garage.uss"),
                AssetDatabase.LoadAssetAtPath<StyleSheet>(UiDir + "/USS/MainMenu.uss"),
            };
            var hostSo = new SerializedObject(host);
            SerializedProperty arr = hostSo.FindProperty("styleSheets");
            arr.arraySize = sheets.Length;
            for (int i = 0; i < sheets.Length; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = sheets[i];
            hostSo.FindProperty("partPool").objectReferenceValue = AssetDatabase.LoadAssetAtPath<PartPool>(PoolPath);
            hostSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            RepairPanelRef();
            EnsureFirstInBuildSettings();

            Debug.Log($"[Shitboxer] Built the main menu at {ScenePath} and made it the first scene in " +
                      "Build Settings. Open it and press Play to boot into the menu.");
        }

        /// <summary>The UIDocument's PanelSettings ref doesn't survive SaveScene — patch it (see the gallery).</summary>
        private static void RepairPanelRef()
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

        private static void EnsureFirstInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
