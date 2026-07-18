using Shitboxer.Meta;
using Shitboxer.UI.Views;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Shitboxer.UI
{
    /// <summary>
    /// Boots the main menu: loads + applies settings, loads the meta profile, builds
    /// <see cref="MainMenuView"/>, and turns its actions into engine calls — START posts a
    /// <see cref="RunLaunch"/> request (so RunDirector begins a fresh run with the chosen chassis) then
    /// loads the first race scene; QUIT exits. Lives on the MainMenu scene's UI rig; the panel,
    /// stylesheets and part pool are wired by the editor builder (MainMenuBuilder).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuHost : MonoBehaviour
    {
        [SerializeField] private StyleSheet[] styleSheets;
        [SerializeField] private PartPool partPool;
        [SerializeField] private string firstRaceScene = "RaceTest";

        private void Start()
        {
            GameSettings settings = GameSettings.Load();
            settings.Apply();
            MetaProgress meta = MetaProgress.Load();

            var doc = GetComponent<UIDocument>();
            VisualElement root = doc != null ? doc.rootVisualElement : null;
            if (root == null)
            {
                Debug.LogWarning("[Shitboxer] MainMenuHost: no rootVisualElement (missing PanelSettings?).", this);
                return;
            }

            root.Clear();
            if (styleSheets != null)
                foreach (StyleSheet sheet in styleSheets)
                    if (sheet != null) root.styleSheets.Add(sheet);

            var view = new MainMenuView(meta, settings, partPool, StartRun, Quit);
            view.Root.style.flexGrow = 1;
            root.Add(view.Root);

            var scanlines = new VisualElement();
            scanlines.AddToClassList("sb-scanlines");
            scanlines.pickingMode = PickingMode.Ignore;
            root.Add(scanlines);
        }

        private void StartRun(int chassisId)
        {
            RunLaunch.RequestNewRun(chassisId, 0);
            SceneManager.LoadScene(firstRaceScene);
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
