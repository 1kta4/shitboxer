using Shitboxer.UI.Model;
using Shitboxer.UI.Views;
using UnityEngine;
using UnityEngine.UIElements;

namespace Shitboxer.UI
{
    /// <summary>
    /// Mounts the garage view into a UIDocument with canned data, for looking at the UI in isolation.
    /// Forces timeScale = 0 while active so the gallery also proves the two things the real garage needs
    /// but nobody has observed yet: that a Button responds and that USS transitions run WHILE PAUSED.
    /// The stylesheets are assigned by the editor builder (Shitboxer/Build UI Gallery).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GalleryDriver : MonoBehaviour
    {
        [SerializeField] private StyleSheet[] styleSheets;

        private void OnEnable()
        {
            Time.timeScale = 0f;

            var doc = GetComponent<UIDocument>();
            VisualElement root = doc.rootVisualElement;
            if (root == null) return;

            root.Clear();
            if (styleSheets != null)
                foreach (StyleSheet sheet in styleSheets)
                    if (sheet != null) root.styleSheets.Add(sheet);

            var view = new GarageView(new GarageViewModel(new GalleryHost()));
            view.Root.style.flexGrow = 1;
            root.Add(view.Root);
        }

        private void OnDisable() => Time.timeScale = 1f;
    }
}
