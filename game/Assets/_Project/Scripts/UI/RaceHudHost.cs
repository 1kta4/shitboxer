using Shitboxer.Meta;
using Shitboxer.UI.Views;
using UnityEngine;
using UnityEngine.UIElements;

namespace Shitboxer.UI
{
    /// <summary>
    /// Renders the UI Toolkit in-race HUD (<see cref="RaceHudView"/>) over the live race, reading
    /// everything from <see cref="RunDirector.Instance"/> (CurrentRace / PlayerCar / PayoutPreviewFor).
    /// Visible only during Racing; refreshed each frame. Per-scene — the editor builder adds it to the
    /// RaceRig and assigns the panel + stylesheets. The 3-2-1 countdown and CRUNCH flash stay in the
    /// (stripped) IMGUI RaceHud for now.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class RaceHudHost : MonoBehaviour
    {
        [SerializeField] private StyleSheet[] styleSheets;

        private UIDocument _doc;
        private RaceHudView _view;
        private System.Func<int, int> _payout;
        private bool _built;
        private bool _visible = true;

        private void OnEnable() => _doc = GetComponent<UIDocument>();

        private void Update()
        {
            if (!EnsureBuilt()) return;

            IRunHost host = RunDirector.Instance;
            bool racing = host != null && host.Phase == RunPhase.Racing
                          && host.CurrentRace != null && host.PlayerCar != null;

            if (racing != _visible)
            {
                _view.Root.style.display = racing ? DisplayStyle.Flex : DisplayStyle.None;
                _visible = racing;
            }

            if (!racing) return;
            if (_payout == null) _payout = host.PayoutPreviewFor;   // cache the delegate; don't alloc per frame
            _view.Refresh(host, _payout);
        }

        private bool EnsureBuilt()
        {
            if (_built) return true;
            VisualElement root = _doc != null ? _doc.rootVisualElement : null;
            if (root == null) return false;

            root.Clear();
            if (styleSheets != null)
                foreach (StyleSheet sheet in styleSheets)
                    if (sheet != null) root.styleSheets.Add(sheet);

            _view = new RaceHudView();
            _view.Root.style.flexGrow = 1;
            root.Add(_view.Root);
            _built = true;
            return true;
        }
    }
}
