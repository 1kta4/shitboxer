using Shitboxer.Meta;
using Shitboxer.UI.Elements;
using Shitboxer.UI.Model;
using UnityEngine.UIElements;

namespace Shitboxer.UI.Views
{
    /// <summary>
    /// The garage screen, built as a VisualElement tree over a <see cref="GarageViewModel"/>. Knows
    /// nothing about scenes, cameras or the run loop — hand it a VM and it works, which is what makes it
    /// portable and what let its logic be fully tested before a pixel existed. USS does all the styling
    /// (Tokens + Shitboxer + Garage stylesheets, attached to the panel root by the host).
    ///
    /// It rebuilds its dynamic content on the VM's Changed signal rather than polling — a buy/equip/reroll
    /// mutates the VM, the VM raises Changed, this re-reads.
    /// </summary>
    public sealed class GarageView
    {
        private readonly GarageViewModel _vm;

        public VisualElement Root { get; }

        private readonly ReadoutRow _statusMoney = new ReadoutRow();
        private readonly ReadoutRow _statusLives = new ReadoutRow();
        private readonly Label _circuitLabel = new Label();
        private readonly Label _nextLabel = new Label();
        private readonly StatBar _grip = new StatBar();
        private readonly StatBar _power = new StatBar();
        private readonly VisualElement _dynamic = new VisualElement();

        public GarageView(GarageViewModel vm)
        {
            _vm = vm;
            Root = Build();
            _vm.Changed += Refresh;
            Refresh();
        }

        private VisualElement Build()
        {
            var root = new VisualElement();
            root.AddToClassList("sb-root");
            root.AddToClassList("garage");

            var panel = new VisualElement();
            panel.AddToClassList("sb-panel");
            panel.AddToClassList("garage__panel");

            // Titlebar
            var titlebar = new VisualElement();
            titlebar.AddToClassList("sb-titlebar");
            titlebar.Add(new Label { text = "GARAGE" });
            _circuitLabel.AddToClassList("garage__circuit");
            titlebar.Add(_circuitLabel);
            panel.Add(titlebar);

            // Status well
            var status = new VisualElement();
            status.AddToClassList("sb-well");
            status.AddToClassList("garage__status");
            status.Add(_statusMoney);
            status.Add(_statusLives);
            _nextLabel.AddToClassList("garage__next");
            status.Add(_nextLabel);
            panel.Add(status);

            // Stat bars
            var stats = new VisualElement();
            stats.AddToClassList("garage__stats");
            stats.Add(_grip);
            stats.Add(_power);
            panel.Add(stats);

            // Everything below the fold is rebuilt on Changed.
            _dynamic.AddToClassList("garage__dynamic");
            panel.Add(_dynamic);

            // Footer
            var footer = new VisualElement();
            footer.AddToClassList("garage__footer");
            var next = new Button(() => _vm.NextRace()) { text = "NEXT RACE" };
            next.AddToClassList("sb-button");
            next.AddToClassList("garage__next-race");
            footer.Add(next);
            panel.Add(footer);

            root.Add(panel);
            return root;
        }

        private void Refresh()
        {
            _statusMoney.Set("$", _vm.Money.ToString());
            _statusLives.Set("LIVES", _vm.Lives.ToString(), fault: _vm.Lives <= 1);
            _circuitLabel.text = _vm.CircuitLine;
            _nextLabel.text = "NEXT: " + _vm.NextRaceLine;

            _grip.style.display = _vm.HasStatPreview ? DisplayStyle.Flex : DisplayStyle.None;
            _power.style.display = _vm.HasStatPreview ? DisplayStyle.Flex : DisplayStyle.None;
            if (_vm.HasStatPreview)
            {
                _grip.Set("GRIP", _vm.Current.Grip);
                _power.Set("POWER", _vm.Current.Power);
            }

            _dynamic.Clear();

            if (_vm.RepairAvailable)
            {
                var repair = new Button(() => _vm.Repair()) { text = _vm.RepairLabel };
                repair.AddToClassList("sb-button");
                repair.SetEnabled(_vm.CanAffordRepair);
                _dynamic.Add(repair);
            }

            _dynamic.Add(SectionLabel(_vm.CrateOpen ? "PARTS CRATE — KEEP ONE" : "SHOP"));
            if (_vm.CrateOpen)
            {
                foreach (OfferVm item in _vm.CrateContents)
                    _dynamic.Add(BuildPartOffer(item, isCrate: true));
            }
            else
            {
                foreach (OfferVm offer in _vm.Offers)
                    _dynamic.Add(BuildPartOffer(offer, isCrate: false));

                var buyCrate = new Button(() => _vm.BuyCrate())
                    { text = $"BUY PARTS CRATE (${_vm.CratePrice}) — open {_vm.CrateDrawCount}, keep 1" };
                buyCrate.AddToClassList("sb-button");
                buyCrate.SetEnabled(_vm.CanAffordCrate);
                _dynamic.Add(buyCrate);

                var reroll = new Button(() => _vm.Reroll()) { text = $"REROLL (${_vm.RerollCost})" };
                reroll.AddToClassList("sb-button");
                reroll.SetEnabled(_vm.CanAffordReroll);
                _dynamic.Add(reroll);
            }

            if (_vm.AvailableUpgrades.Count > 0)
            {
                _dynamic.Add(SectionLabel("TEAM UPGRADES (permanent)"));
                foreach (UpgradeVm upgrade in _vm.AvailableUpgrades)
                    _dynamic.Add(BuildUpgrade(upgrade));
            }

            _dynamic.Add(SectionLabel($"OWNED PARTS ({_vm.SlotLine})"));
            if (_vm.OwnedParts.Count == 0)
            {
                _dynamic.Add(Dim("(none yet — buy something)"));
            }
            else
            {
                foreach (OwnedPartVm part in _vm.OwnedParts)
                    _dynamic.Add(BuildOwned(part));
            }
        }

        private VisualElement BuildPartOffer(OfferVm offer, bool isCrate)
        {
            var row = new VisualElement();
            row.AddToClassList("offer");

            var info = new VisualElement();
            info.AddToClassList("offer__info");
            info.Add(new Label { text = $"{offer.Name}  [{offer.Category}]  ${offer.Price}" });
            if (!string.IsNullOrEmpty(offer.EditionTag))
                info.Add(new Label { text = offer.EditionTag });
            if (!string.IsNullOrEmpty(offer.Description))
            {
                var desc = new Label { text = offer.Description };
                desc.AddToClassList("offer__desc");
                info.Add(desc);
            }
            if (offer.HasStatPreview)
            {
                info.Add(DeltaLabel("GRIP", offer.Grip));
                info.Add(DeltaLabel("POWER", offer.Power));
            }
            row.Add(info);

            PartDef part = offer.Part;
            Button action = isCrate
                ? new Button(() => _vm.TakeFromCrate(part)) { text = "KEEP" }
                : new Button(() => _vm.Buy(part)) { text = "BUY" };
            action.AddToClassList("sb-button");
            action.AddToClassList("offer__action");
            if (!isCrate) action.SetEnabled(offer.Affordable);
            row.Add(action);

            return row;
        }

        private VisualElement BuildUpgrade(UpgradeVm upgrade)
        {
            var row = new VisualElement();
            row.AddToClassList("offer");

            var info = new VisualElement();
            info.AddToClassList("offer__info");
            info.Add(new Label { text = $"{upgrade.Name}  ${upgrade.Price}" });
            var desc = new Label { text = upgrade.Description };
            desc.AddToClassList("offer__desc");
            info.Add(desc);
            row.Add(info);

            TeamUpgrade u = upgrade.Upgrade;
            var buy = new Button(() => _vm.BuyUpgrade(u)) { text = "BUY" };
            buy.AddToClassList("sb-button");
            buy.AddToClassList("offer__action");
            buy.SetEnabled(upgrade.Affordable);
            row.Add(buy);

            return row;
        }

        private VisualElement BuildOwned(OwnedPartVm part)
        {
            var row = new VisualElement();
            row.AddToClassList("sb-row");
            row.AddToClassList("owned");

            string tag = string.IsNullOrEmpty(part.EditionTag) ? "" : "  " + part.EditionTag;
            string suffix = part.Equipped ? "  — EQUIPPED" : "";
            var label = new Label { text = $"{part.Name}  [{part.Category}]{tag}{suffix}" };
            label.AddToClassList("owned__label");
            row.Add(label);

            var spacer = new VisualElement();
            spacer.AddToClassList("owned__spacer");
            row.Add(spacer);

            PartDef p = part.Part;
            Button action = part.Equipped
                ? new Button(() => _vm.Unequip(p)) { text = "UNEQUIP" }
                : new Button(() => _vm.Equip(p)) { text = "EQUIP" };
            action.AddToClassList("sb-button");
            if (!part.Equipped) action.SetEnabled(part.CanEquip);
            row.Add(action);

            return row;
        }

        private static Label DeltaLabel(string stat, StatDelta d)
        {
            string delta = d.Sign == 0 ? "" : $"  ({(d.Sign > 0 ? "+" : "")}{d.Delta:0})";
            var label = new Label { text = $"{stat} {d.Before:0} -> {d.After:0}{delta}" };
            label.AddToClassList("offer__delta");
            // Palette rule: a downgrade is a FAULT (blood); a gain reads as normal (the system working).
            if (d.Sign < 0) label.AddToClassList("sb-fault");
            return label;
        }

        private static Label SectionLabel(string text)
        {
            var label = new Label { text = "-- " + text + " --" };
            label.AddToClassList("garage__section");
            return label;
        }

        private static Label Dim(string text)
        {
            var label = new Label { text = text };
            label.AddToClassList("sb-dim");
            return label;
        }
    }
}
