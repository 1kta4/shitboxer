using System.Collections.Generic;
using Shitboxer.Meta;
using Shitboxer.UI.Elements;
using Shitboxer.UI.Model;
using UnityEngine.UIElements;

namespace Shitboxer.UI.Views
{
    /// <summary>
    /// The garage screen, "PS2 v3" layout (ported from garage-ps2-v3.html): a header, a permanent left
    /// rail carrying the car's live state (funds, lives, GRIP/POWER, owned parts) and a main shop column
    /// of selectable rows that expand to show description / stat delta / BUY. Built over a
    /// <see cref="GarageViewModel"/>; the static frame is built once and the dynamic lists rebuild on the
    /// VM's Changed signal. All styling is USS (Tokens + Shitboxer + Garage).
    ///
    /// USS has no CSS grid, clip-path or box-shadow, so the mock's diagonal cuts and glows are NOT here —
    /// they are a later baked-sprite pass. Layout is flexbox; bevels are border colours. Fonts are the
    /// in-repo pixel faces (swap the two --font vars in Tokens.uss to move onto the demo fonts).
    /// </summary>
    public sealed class GarageView
    {
        private readonly GarageViewModel _vm;

        public VisualElement Root { get; }

        // Mutable widgets, refreshed on Changed.
        private readonly ReadoutRow _funds = new ReadoutRow();
        private readonly VisualElement _lives = new VisualElement();
        private readonly Label _circuit = new Label();
        private readonly Label _next = new Label();
        private readonly StatBar _grip = new StatBar();
        private readonly StatBar _power = new StatBar();
        private readonly Label _slots = new Label();
        private readonly VisualElement _owned = new VisualElement();
        private readonly Label _offerCount = new Label();
        private readonly VisualElement _offers = new VisualElement();

        private int _sel;   // selected shop row (expands its detail)

        public GarageView(GarageViewModel vm)
        {
            _vm = vm;
            Root = Build();
            _vm.Changed += Refresh;
            Refresh();
        }

        private VisualElement Build()
        {
            var screen = new VisualElement();
            screen.AddToClassList("sb-screen");

            var stage = new VisualElement();
            stage.AddToClassList("gx-stage");
            stage.Add(BuildHead());

            var body = new VisualElement();
            body.AddToClassList("gx-body");
            body.Add(BuildRail());
            body.Add(BuildMain());
            stage.Add(body);

            stage.Add(BuildFoot());
            screen.Add(stage);
            return screen;
        }

        private VisualElement BuildHead()
        {
            var head = new VisualElement();
            head.AddToClassList("gx-head");

            var lockup = new VisualElement();
            lockup.AddToClassList("gx-lockup");
            var title = new Label { text = "GARAGE" };
            title.AddToClassList("gx-title");
            var tag = new Label { text = "BETWEEN RACES" };
            tag.AddToClassList("gx-tag");
            lockup.Add(title);
            lockup.Add(tag);

            var sys = new VisualElement();
            sys.AddToClassList("gx-sys");
            _circuit.AddToClassList("gx-circuit");
            var mem = new Label { text = "MEM SLOT 1" };
            mem.AddToClassList("gx-mem");
            sys.Add(_circuit);
            sys.Add(mem);

            head.Add(lockup);
            head.Add(sys);
            return head;
        }

        private VisualElement BuildRail()
        {
            var rail = new VisualElement();
            rail.AddToClassList("gx-rail");
            rail.Add(RailHead("VEHICLE"));

            var readouts = new VisualElement();
            readouts.AddToClassList("gx-readouts");
            _funds.AddToClassList("cash");
            readouts.Add(_funds);

            var livesRow = new VisualElement();
            livesRow.AddToClassList("gx-lives-row");
            var livesLabel = new Label { text = "LIVES" };
            livesLabel.AddToClassList("gx-lives-label");
            _lives.AddToClassList("gx-lives");
            livesRow.Add(livesLabel);
            livesRow.Add(_lives);
            readouts.Add(livesRow);
            rail.Add(readouts);

            _next.AddToClassList("gx-next");
            rail.Add(_next);

            var stats = new VisualElement();
            stats.AddToClassList("gx-stats");
            stats.Add(_grip);
            stats.Add(_power);
            rail.Add(stats);

            var ownedHead = new VisualElement();
            ownedHead.AddToClassList("gx-ownedh");
            var ownedTitle = new Label { text = "OWNED" };
            ownedTitle.AddToClassList("gx-railh-txt");
            _slots.AddToClassList("gx-slots");
            ownedHead.Add(ownedTitle);
            ownedHead.Add(_slots);
            rail.Add(ownedHead);

            _owned.AddToClassList("gx-owned");
            rail.Add(_owned);
            return rail;
        }

        private VisualElement BuildMain()
        {
            var main = new VisualElement();
            main.AddToClassList("gx-main");

            var sec = new VisualElement();
            sec.AddToClassList("gx-sec");
            var secTitle = new Label { text = "SHOP" };
            secTitle.AddToClassList("gx-railh-txt");
            _offerCount.AddToClassList("gx-offercount");
            sec.Add(secTitle);
            sec.Add(_offerCount);
            main.Add(sec);

            _offers.AddToClassList("gx-offers");
            main.Add(_offers);
            return main;
        }

        private VisualElement BuildFoot()
        {
            var foot = new VisualElement();
            foot.AddToClassList("gx-foot");

            var acts = new VisualElement();
            acts.AddToClassList("gx-acts");
            var crate = new Button(() => _vm.BuyCrate()) { text = $"BUY CRATE  ${_vm.CratePrice}" };
            crate.AddToClassList("gx-btn");
            crate.AddToClassList("ghost");
            var reroll = new Button(() => _vm.Reroll()) { text = $"REROLL  ${_vm.RerollCost}" };
            reroll.AddToClassList("gx-btn");
            reroll.AddToClassList("ghost");
            acts.Add(crate);
            acts.Add(reroll);

            var cta = new Button(() => _vm.NextRace()) { text = "NEXT RACE  >" };
            cta.AddToClassList("gx-cta");

            foot.Add(acts);
            foot.Add(cta);
            return foot;
        }

        private void Refresh()
        {
            _funds.Set("FUNDS", "$" + _vm.Money);

            _lives.Clear();
            for (int i = 0; i < _vm.Lives; i++)
            {
                var dot = new VisualElement();
                dot.AddToClassList("gx-life");
                _lives.Add(dot);
            }

            _circuit.text = _vm.CircuitLine;
            _next.text = "NEXT > " + _vm.NextRaceLine;

            bool preview = _vm.HasStatPreview;
            _grip.style.display = preview ? DisplayStyle.Flex : DisplayStyle.None;
            _power.style.display = preview ? DisplayStyle.Flex : DisplayStyle.None;
            if (preview)
            {
                _grip.Set("GRIP", _vm.Current.Grip);
                _power.Set("POWER", _vm.Current.Power);
            }

            _slots.text = $"{_vm.SlotsUsed}/{_vm.SlotsTotal} SLOTS";
            RefreshOwned();
            RefreshOffers();
        }

        private void RefreshOwned()
        {
            _owned.Clear();
            foreach (OwnedPartVm p in _vm.OwnedParts)
                _owned.Add(BuildOwned(p));
        }

        private VisualElement BuildOwned(OwnedPartVm p)
        {
            var row = new VisualElement();
            row.AddToClassList("gx-owned-item");
            if (p.Equipped) row.AddToClassList("on");
            row.Add(Chip(p.Category));
            var name = new Label { text = p.Name };
            name.AddToClassList("gx-owned-name");
            row.Add(name);

            PartDef part = p.Part;
            bool equipped = p.Equipped;
            row.RegisterCallback<ClickEvent>(_ =>
            {
                if (equipped) _vm.Unequip(part);
                else _vm.Equip(part);
            });
            return row;
        }

        private void RefreshOffers()
        {
            _offers.Clear();
            IReadOnlyList<OfferVm> list = _vm.CrateOpen ? _vm.CrateContents : _vm.Offers;
            if (_sel >= list.Count) _sel = list.Count > 0 ? list.Count - 1 : 0;

            for (int i = 0; i < list.Count; i++)
                _offers.Add(BuildOffer(list[i], i, _vm.CrateOpen));

            _offerCount.text = list.Count + (list.Count == 1 ? " OFFER" : " OFFERS");
        }

        private VisualElement BuildOffer(OfferVm o, int index, bool isCrate)
        {
            bool selected = index == _sel;

            var offer = new VisualElement();
            offer.AddToClassList("gx-offer");
            if (selected) offer.AddToClassList("sel");
            if (!isCrate && !o.Affordable) offer.AddToClassList("unaffordable");

            var row = new VisualElement();
            row.AddToClassList("gx-offer-row");
            var marker = new Label { text = ">" };
            marker.AddToClassList("gx-marker");
            var name = new Label { text = o.Name };
            name.AddToClassList("gx-oname");
            var price = new Label { text = "$" + o.Price };
            price.AddToClassList("gx-price");
            row.Add(marker);
            row.Add(name);
            row.Add(Chip(o.Category));
            row.Add(price);
            offer.Add(row);

            var detail = new VisualElement();
            detail.AddToClassList("gx-offer-detail");
            detail.style.display = selected ? DisplayStyle.Flex : DisplayStyle.None;

            var info = new VisualElement();
            info.AddToClassList("gx-offer-info");
            if (!string.IsNullOrEmpty(o.Description))
            {
                var desc = new Label { text = o.Description };
                desc.AddToClassList("gx-desc");
                info.Add(desc);
            }
            info.Add(BuildDelta(o));
            detail.Add(info);

            PartDef part = o.Part;
            Button buy = isCrate
                ? new Button(() => _vm.TakeFromCrate(part)) { text = "KEEP" }
                : new Button(() => _vm.Buy(part)) { text = o.Affordable ? "BUY" : "NO FUNDS" };
            buy.AddToClassList("gx-btn");
            if (!isCrate) buy.SetEnabled(o.Affordable);
            detail.Add(buy);
            offer.Add(detail);

            // Click the row (but not the BUY button) to select + expand it.
            offer.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is Button) return;
                Select(index);
            });
            return offer;
        }

        private void Select(int index)
        {
            _sel = index;
            RefreshOffers();
        }

        private static VisualElement BuildDelta(OfferVm o)
        {
            if (!o.HasStatPreview)
            {
                var passive = new Label { text = "PASSIVE INCOME" };
                passive.AddToClassList("gx-delta");
                passive.AddToClassList("passive");
                return passive;
            }

            var wrap = new VisualElement();
            wrap.AddToClassList("gx-delta");
            AddDeltaPart(wrap, "GRIP", o.Grip);
            AddDeltaPart(wrap, "POWER", o.Power);
            return wrap;
        }

        private static void AddDeltaPart(VisualElement wrap, string stat, StatDelta d)
        {
            if (d.Sign == 0) return;
            string sign = d.Sign > 0 ? "+" : "";
            var lbl = new Label { text = $"{stat} {d.Before:0} -> {d.After:0} ({sign}{d.Delta:0})" };
            lbl.AddToClassList(d.Sign > 0 ? "up" : "dn");
            wrap.Add(lbl);
        }

        private static Label Chip(PartCategory cat)
        {
            string txt = cat == PartCategory.Stat ? "STAT"
                : cat == PartCategory.Economy ? "ECON"
                : cat.ToString().ToUpperInvariant();
            var chip = new Label { text = txt };
            chip.AddToClassList("gx-chip");
            if (cat == PartCategory.Economy) chip.AddToClassList("econ");
            return chip;
        }

        private static VisualElement RailHead(string text)
        {
            var head = new VisualElement();
            head.AddToClassList("gx-railh");
            var label = new Label { text = text };
            label.AddToClassList("gx-railh-txt");
            head.Add(label);
            return head;
        }
    }
}
