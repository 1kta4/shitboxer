using Shitboxer.UI.Model;
using Shitboxer.UI.Views;
using UnityEngine;
using UnityEngine.UIElements;

namespace Shitboxer.UI
{
    /// <summary>
    /// Mounts the garage view into a UIDocument with canned data, for looking at the UI in isolation.
    /// [ExecuteAlways] so the tree builds in the editor too — the garage shows in the GAME VIEW without
    /// pressing Play (a UIDocument renders its runtime panel in the Game view even when not playing).
    /// While PLAYING it also forces timeScale = 0, so the gallery proves the two things the real garage
    /// needs but nobody has observed yet: that a Button responds and that USS states run WHILE PAUSED.
    /// The stylesheets are assigned by the editor builder (Shitboxer/Build UI Gallery).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(UIDocument))]
    public sealed class GalleryDriver : MonoBehaviour
    {
        [SerializeField] private StyleSheet[] styleSheets;

        private void OnEnable()
        {
            // Freeze time only for the RUNNING gallery (the point is proving clicks work at timeScale 0).
            // Under [ExecuteAlways] OnEnable also runs in the editor, where touching timeScale would be wrong.
            if (Application.isPlaying) Time.timeScale = 0f;

            var doc = GetComponent<UIDocument>();
            VisualElement root = doc.rootVisualElement;
            if (root == null)
            {
                Debug.LogWarning("[Shitboxer] GalleryDriver: UIDocument.rootVisualElement is null — the "
                    + "UIDocument has no PanelSettings (or it wasn't ready), so nothing rendered. Re-run "
                    + "Shitboxer/Build UI Gallery.");
                return;
            }

            root.Clear();
            if (styleSheets != null)
                foreach (StyleSheet sheet in styleSheets)
                    if (sheet != null) root.styleSheets.Add(sheet);

            var view = new GarageView(new GarageViewModel(new GalleryHost()));
            view.Root.style.flexGrow = 1;
            root.Add(view.Root);
        }

        private void OnDisable()
        {
            if (Application.isPlaying) Time.timeScale = 1f;
        }
    }
}
