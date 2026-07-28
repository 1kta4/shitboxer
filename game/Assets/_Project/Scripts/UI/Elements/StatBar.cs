using UnityEngine;
using UnityEngine.UIElements;

namespace Shitboxer.UI.Elements
{
    /// <summary>
    /// A headline-stat bar: a label, a recessed track with a fill, and the numeric value. GRIP and POWER
    /// are told apart by label and length, NOT hue. <see cref="SetPreview"/> overlays a GHOST segment
    /// between the current and projected value (green for a gain, blood for a loss) so selecting a shop
    /// part shows, on the rail, what it would do before you buy — the v3 "ghost" preview.
    /// </summary>
    public sealed class StatBar : VisualElement
    {
        private readonly Label _name = new Label();
        private readonly VisualElement _fill = new VisualElement();
        private readonly VisualElement _ghost = new VisualElement();
        private readonly Label _value = new Label();

        public StatBar()
        {
            AddToClassList("stat-bar");
            _name.AddToClassList("stat-bar__name");

            var track = new VisualElement();
            track.AddToClassList("stat-bar__track");
            track.AddToClassList("sb-well");
            _fill.AddToClassList("stat-bar__fill");
            _ghost.AddToClassList("stat-bar__ghost");
            track.Add(_fill);
            track.Add(_ghost);

            _value.AddToClassList("stat-bar__value");

            Add(_name);
            Add(track);
            Add(_value);
        }

        /// <summary>Set the label and a 0..100 value; the fill grows to that fraction. No ghost.</summary>
        public void Set(string label, float value0To100)
        {
            _name.text = label;
            float v = Mathf.Clamp(value0To100, 0f, 100f);
            _fill.style.width = Length.Percent(v);
            _ghost.style.display = DisplayStyle.None;
            _value.text = Mathf.RoundToInt(v).ToString();
            _value.RemoveFromClassList("gain");
            _value.RemoveFromClassList("loss");
        }

        /// <summary>Show <paramref name="current"/> as the solid fill and ghost the delta up/down to
        /// <paramref name="projected"/>; the value reads the projected number, tinted by direction.</summary>
        public void SetPreview(string label, float current, float projected)
        {
            _name.text = label;
            float cur = Mathf.Clamp(current, 0f, 100f);
            float proj = Mathf.Clamp(projected, 0f, 100f);
            float lo = Mathf.Min(cur, proj);
            float hi = Mathf.Max(cur, proj);
            bool loss = proj < cur;

            _fill.style.width = Length.Percent(lo);        // the part guaranteed either way
            _ghost.style.display = DisplayStyle.Flex;
            _ghost.style.left = Length.Percent(lo);
            _ghost.style.width = Length.Percent(hi - lo);  // the delta segment
            _ghost.EnableInClassList("loss", loss);

            _value.text = Mathf.RoundToInt(proj).ToString();
            _value.EnableInClassList("gain", !loss && !Mathf.Approximately(proj, cur));
            _value.EnableInClassList("loss", loss);
        }
    }
}
