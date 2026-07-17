using Shitboxer.Meta;
using Shitboxer.Race;
using Shitboxer.UI.Model;
using Shitboxer.UI.Views;
using UnityEngine;
using UnityEngine.UIElements;

namespace Shitboxer.UI
{
    /// <summary>
    /// Mounts the UI Toolkit <see cref="GarageView"/> over the live run during the Garage phase — the
    /// replacement for the IMGUI GarageScreen's garage draw. Lives on the RunRig beside
    /// <see cref="RunDirector"/> (a DontDestroyOnLoad singleton) so the overlay survives the race-scene
    /// rotation. Shows only in Garage; the view is rebuilt fresh each visit so it always reflects the
    /// current run. The panel + stylesheets are assigned by the editor builder (MetaAssetsBuilder).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GarageUiHost : MonoBehaviour
    {
        [SerializeField] private StyleSheet[] styleSheets;

        private IRunHost _host;
        private UIDocument _doc;
        private RaceHud _raceHud;
        private bool _styled;

        private void Start()
        {
            _doc = GetComponent<UIDocument>();
            _host = GetComponent<RunDirector>();
            if (_host == null) _host = RunDirector.Instance;

            if (_host == null)
            {
                Debug.LogWarning("[Shitboxer] GarageUiHost found no RunDirector — the garage UI won't show.", this);
                return;
            }

            _host.PhaseChanged += OnPhaseChanged;
            Sync(_host.Phase);   // PhaseChanged never fires for the initial Racing phase — read it once.
        }

        private void OnDestroy()
        {
            if (_host != null) _host.PhaseChanged -= OnPhaseChanged;
        }

        private void OnPhaseChanged(RunPhase phase) => Sync(phase);

        private void Sync(RunPhase phase)
        {
            // The in-race HUD (RaceRig, Race assembly) can't see the run phase, so hide it here the moment
            // we leave Racing — otherwise its readout bleeds through the garage. Re-found after each scene
            // rotation (the old HUD is destroyed, so the reference goes null and we look again).
            if (_raceHud == null) _raceHud = FindAnyObjectByType<RaceHud>();
            if (_raceHud != null) _raceHud.enabled = phase == RunPhase.Racing;

            VisualElement root = _doc != null ? _doc.rootVisualElement : null;
            if (root == null) return;

            // Stylesheets attach once — root persists across garage visits, so re-adding would duplicate.
            if (!_styled && styleSheets != null)
            {
                foreach (StyleSheet sheet in styleSheets)
                    if (sheet != null) root.styleSheets.Add(sheet);
                _styled = true;
            }

            root.Clear();
            switch (phase)
            {
                case RunPhase.Garage:
                    Mount(root, new GarageView(new GarageViewModel(_host)).Root);
                    break;
                case RunPhase.RunOver:
                    Mount(root, new EndScreenView(_host, "RUN OVER").Root);
                    break;
                case RunPhase.RunComplete:
                    Mount(root, new EndScreenView(_host, "SEASON CLEARED").Root);
                    break;
                default:
                    root.style.display = DisplayStyle.None;
                    break;
            }
        }

        private static void Mount(VisualElement root, VisualElement view)
        {
            root.style.display = DisplayStyle.Flex;
            view.style.flexGrow = 1;
            root.Add(view);
        }
    }
}
