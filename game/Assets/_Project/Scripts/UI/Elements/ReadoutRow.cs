using UnityEngine.UIElements;

namespace Shitboxer.UI.Elements
{
    /// <summary>
    /// A service-menu readout line: LABEL ......... VALUE.
    ///
    /// USS has no ::after, so the dot leader is a real element holding a deliberately over-long dot run,
    /// clipped by overflow:hidden (in USS). It costs one extra element per row and never wraps, never
    /// miscounts, and works at any width — which a computed dot count would not. Harvested verbatim in
    /// spirit from the prototype's DiagnosticRow.
    /// </summary>
    public sealed class ReadoutRow : VisualElement
    {
        private const string Leader =
            "................................................................"
          + "................................................................";

        private readonly Label _label = new Label();
        private readonly Label _leader = new Label { text = Leader };
        private readonly Label _value = new Label();

        public ReadoutRow()
        {
            AddToClassList("readout-row");
            _label.AddToClassList("readout-row__label");
            _leader.AddToClassList("readout-row__leader");
            _value.AddToClassList("readout-row__value");
            Add(_label);
            Add(_leader);
            Add(_value);
        }

        /// <summary>Set the label and value; a fault value is drawn in blood-red.</summary>
        public void Set(string label, string value, bool fault = false)
        {
            _label.text = label;
            _value.text = value;
            _value.EnableInClassList("sb-fault", fault);
        }
    }
}
