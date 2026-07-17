using UnityEngine;
using UnityEngine.UIElements;

namespace Shitboxer.UI.Elements
{
    /// <summary>
    /// A headline-stat bar: a label, a recessed steel track with a cobalt fill, and the numeric value.
    /// GRIP and POWER are told apart by label and length, NOT hue — the arcade palette keeps only two
    /// saturated colours (cobalt = the system working, blood = a fault), so a coloured bar per stat would
    /// spend accents the design can't afford.
    /// </summary>
    public sealed class StatBar : VisualElement
    {
        private readonly Label _name = new Label();
        private readonly VisualElement _fill = new VisualElement();
        private readonly Label _value = new Label();

        public StatBar()
        {
            AddToClassList("stat-bar");

            _name.AddToClassList("stat-bar__name");

            var track = new VisualElement();
            track.AddToClassList("stat-bar__track");
            track.AddToClassList("sb-well");
            _fill.AddToClassList("stat-bar__fill");
            track.Add(_fill);

            _value.AddToClassList("stat-bar__value");

            Add(_name);
            Add(track);
            Add(_value);
        }

        /// <summary>Set the label and a 0..100 value; the fill grows to that fraction of the track.</summary>
        public void Set(string label, float value0To100)
        {
            _name.text = label;
            float v = Mathf.Clamp(value0To100, 0f, 100f);
            _fill.style.width = Length.Percent(v);
            _value.text = Mathf.RoundToInt(v).ToString();
        }
    }
}
