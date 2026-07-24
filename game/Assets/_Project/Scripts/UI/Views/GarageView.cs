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
        private readonly StatBar _weight = new StatBar();
        private readonly StatBar _durability = new StatBar();
        private readonly Label _slots = new Label();
        private readonly VisualElement _owned = new VisualElement();
        private readonly Label _offerCount = new Label();
        private readonly VisualElement _offers = new VisualElement();
        private readonly VisualElement _packs = new VisualElement();
        private readonly VisualElement _components = new VisualElement();
        private readonly VisualElement _blueprints = new VisualElement();
        private readonly Label _packsTitle = new Label { text = "PACKS" };
        private readonly Label _blueprintsTitle = new Label { text = "BLUEPRINTS" };
        private readonly Label _componentsTitle = new Label { text = "COMPONENTS" };

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

            // Four bars now (doc 08 decision 2). Weight reads as LIGHTNESS so "up is good" holds on
            // every bar, which is the only way four stats stay glanceable.
            var stats = new VisualElement();
            stats.AddToClassList("gx-stats");
            stats.Add(_power);
            stats.Add(_grip);
            stats.Add(_weight);
            stats.Add(_durability);
            rail.Add(stats);

            var ownedHead = new VisualElement();
            ownedHead.AddToClassList("gx-ownedh");
            var ownedTitle = new Label { text = "FITTED" };
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

            // Packs and components sit BELOW the shelf rather than in tabs: everything you can spend on
            // should be visible at once, or the shop stops being a comparison and becomes a menu.
            _packsTitle.AddToClassList("gx-railh-txt");
            _packsTitle.AddToClassList("gx-subhead");
            main.Add(_packsTitle);
            _packs.AddToClassList("gx-packs");
            main.Add(_packs);

            // Blueprints are STOCK, so they sit up here with the other things you can spend on. The
            // component list below is the read-out of what you own — the split is deliberate, and it
            // is the whole reason a component level now has to turn up rather than be picked off a menu.
            _blueprintsTitle.AddToClassList("gx-railh-txt");
            _blueprintsTitle.AddToClassList("gx-subhead");
            main.Add(_blueprintsTitle);
            _blueprints.AddToClassList("gx-components");
            main.Add(_blueprints);

            _componentsTitle.AddToClassList("gx-railh-txt");
            _componentsTitle.AddToClassList("gx-subhead");
            main.Add(_componentsTitle);
            _components.AddToClassList("gx-components");
            main.Add(_components);
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

            _slots.text = $"{_vm.SlotsUsed}/{_vm.SlotsTotal} SLOTS";
            RefreshOwned();
            RefreshOffers();
            RefreshPacks();
            RefreshBlueprints();
            RefreshComponents();
            RefreshStatPreview();
        }

        /// <summary>
        /// The visit's two booster packs. Hidden entirely while a pack is open, because the pick
        /// replaces the shelf — buying a second pack mid-pick is not a thing.
        /// </summary>
        private void RefreshPacks()
        {
            bool show = !_vm.PackOpen && _vm.Packs.Count > 0;
            _packs.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            _packsTitle.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            _packs.Clear();
            if (!show) return;

            foreach (PackVm p in _vm.Packs)
            {
                var card = new VisualElement();
                card.AddToClassList("gx-pack");
                if (!p.Buyable) card.AddToClassList("off");

                var name = new Label { text = p.Name };
                name.AddToClassList("gx-pack-name");
                var sub = new Label { text = $"pick 1 of {p.DrawCount}" };
                sub.AddToClassList("gx-pack-sub");

                int index = p.Index;
                var buy = new Button(() => _vm.BuyPack(index)) { text = $"${p.Price}" };
                buy.AddToClassList("gx-buy");
                buy.SetEnabled(p.Buyable);

                card.Add(name);
                card.Add(sub);
                card.Add(buy);
                _packs.Add(card);
            }
        }

        /// <summary>
        /// This visit's Blueprint stock — the only place a component level can be bought outright.
        /// Hidden while a pack is open (the pick replaces the shelf) and when the roll came back
        /// empty, which happens legitimately once nearly every component is maxed; a "BLUEPRINTS"
        /// header over nothing would read as a bug rather than as a finished car.
        /// </summary>
        private void RefreshBlueprints()
        {
            bool show = !_vm.PackOpen && _vm.Blueprints.Count > 0;
            _blueprints.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            _blueprintsTitle.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            _blueprints.Clear();
            if (!show) return;

            foreach (ComponentVm c in _vm.Blueprints)
                _blueprints.Add(BuildComponentRow(c, CompRow.Blueprint));
        }

        /// <summary>
        /// The ten components — a read-out of the car, with no BUY on any row: levels are bought from
        /// the Blueprint shelf above or picked out of a pack. While a components pack is open this
        /// shows only its offered picks, so the pick reads as "these three, choose one" rather than as
        /// a modal on top of a list you can still shop from.
        /// </summary>
        private void RefreshComponents()
        {
            _components.Clear();
            bool picking = _vm.ComponentPackOpen;
            _componentsTitle.text = picking ? "PICK A COMPONENT" : "COMPONENTS";

            IReadOnlyList<ComponentVm> list = picking ? _vm.PackComponents : _vm.Components;
            foreach (ComponentVm c in list)
                _components.Add(BuildComponentRow(c, picking ? CompRow.Pick : CompRow.Status));
        }

        /// <summary>What a component row lets you DO — the only thing that differs between its three uses.</summary>
        private enum CompRow
        {
            /// <summary>A row in the ten-component read-out: level only, nothing to press.</summary>
            Status,
            /// <summary>Stock on the shelf: buy this component's next level at its price.</summary>
            Blueprint,
            /// <summary>A pick from an open components pack — already paid for, so it costs nothing.</summary>
            Pick,
        }

        private VisualElement BuildComponentRow(ComponentVm c, CompRow mode)
        {
            var row = new VisualElement();
            row.AddToClassList("gx-comp");
            if (mode == CompRow.Pick) row.AddToClassList("pick");
            if (!c.CanLevel) row.AddToClassList("maxed");

            // The family tag IS the stat bar it feeds (decision 5), so the grouping needs no explaining.
            var fam = new Label { text = c.Family.ToString().ToUpperInvariant() };
            fam.AddToClassList("gx-comp-fam");
            fam.AddToClassList("fam-" + c.Family.ToString().ToLowerInvariant());

            var name = new Label { text = c.Name };
            name.AddToClassList("gx-comp-name");

            var level = new Label { text = c.LevelLabel };
            level.AddToClassList("gx-comp-lvl");

            row.Add(fam);
            row.Add(name);
            row.Add(level);

            CarComponent component = c.Component;
            switch (mode)
            {
                case CompRow.Pick:
                {
                    var take = new Button(() => _vm.TakeComponent(component)) { text = "TAKE" };
                    take.AddToClassList("gx-buy");
                    row.Add(take);
                    break;
                }

                case CompRow.Blueprint:
                {
                    var buy = new Button(() => _vm.BuyBlueprint(component)) { text = $"+1  ${c.Price}" };
                    buy.AddToClassList("gx-buy");
                    buy.SetEnabled(c.Affordable);
                    row.Add(buy);
                    break;
                }

                // Status: nothing to press. A maxed component still says so — it is the one piece of
                // news the read-out carries beyond the level number.
                default:
                {
                    if (c.CanLevel) break;
                    var maxed = new Label { text = "MAX" };
                    maxed.AddToClassList("gx-comp-max");
                    row.Add(maxed);
                    break;
                }
            }
            return row;
        }

        /// <summary>Rail GRIP/POWER bars: the current equipped stats, or — when a Stat part is selected in
        /// the shop — its projected value ghosted on, so you see what a part does before buying.</summary>
        private void RefreshStatPreview()
        {
            bool has = _vm.HasStatPreview;
            DisplayStyle display = has ? DisplayStyle.Flex : DisplayStyle.None;
            _power.style.display = display;
            _grip.style.display = display;
            _weight.style.display = display;
            _durability.style.display = display;
            if (!has) return;

            IReadOnlyList<OfferVm> list = _vm.CrateOpen ? _vm.CrateContents : _vm.Offers;
            if (_sel >= 0 && _sel < list.Count && list[_sel].HasStatPreview)
            {
                OfferVm o = list[_sel];
                _power.SetPreview("POWER", o.Power.Before, o.Power.After);
                _grip.SetPreview("GRIP", o.Grip.Before, o.Grip.After);
                _weight.SetPreview("LIGHTNESS", o.Weight.Before, o.Weight.After);
                _durability.SetPreview("DURABILITY", o.Durability.Before, o.Durability.After);
            }
            else
            {
                _power.Set("POWER", _vm.Current.Power);
                _grip.Set("GRIP", _vm.Current.Grip);
                _weight.Set("LIGHTNESS", _vm.Current.Weight);
                _durability.Set("DURABILITY", _vm.Current.Durability);
            }
        }

        private void RefreshOwned()
        {
            _owned.Clear();
            foreach (OwnedPartVm p in _vm.OwnedParts)
                _owned.Add(BuildOwned(p));
        }

        /// <summary>
        /// A fitted part. There is no EQUIP action any more — a bought part is always fitted — so the
        /// only thing you can do here is SELL, which refunds half and frees the slot. That is also the
        /// only way to make room once the car is full, so the button carries real weight and is
        /// deliberately an explicit button rather than a click-anywhere-on-the-row (which would make
        /// selling a build-defining part an easy misclick).
        /// </summary>
        private VisualElement BuildOwned(OwnedPartVm p)
        {
            var row = new VisualElement();
            row.AddToClassList("gx-owned-item");
            if (p.Equipped) row.AddToClassList("on");
            row.Add(Chip(p.Category));
            // The edition tag rides the name ("[FOIL x1.25] Junkyard Turbo") — the fitted list is
            // where an applied Spectral material has to be visible, next to the refund that prices it.
            var name = new Label { text = p.EditionTag.Length > 0 ? $"{p.EditionTag} {p.Name}" : p.Name };
            name.AddToClassList("gx-owned-name");
            row.Add(name);

            PartDef part = p.Part;
            var sell = new Button(() => _vm.Sell(part)) { text = $"SELL ${p.SellValue}" };
            sell.AddToClassList("gx-sell");
            row.Add(sell);
            return row;
        }

        private void RefreshOffers()
        {
            _offers.Clear();

            // An open Spectral pack replaces the shelf, exactly as a parts crate does — the pick is
            // the visit's business until it is resolved. Each row is pre-aimed ("[FOIL x1.25] →
            // JUNKYARD TURBO"), so APPLY is the whole decision.
            if (_vm.SpectralPackOpen)
            {
                foreach (SpectralVm pick in _vm.PackSpectrals)
                    _offers.Add(BuildSpectral(pick));
                _offerCount.text = "SPECTRAL — APPLY ONE";
                return;
            }

            IReadOnlyList<OfferVm> list = _vm.CrateOpen ? _vm.CrateContents : _vm.Offers;
            if (_sel >= list.Count) _sel = list.Count > 0 ? list.Count - 1 : 0;

            for (int i = 0; i < list.Count; i++)
                _offers.Add(BuildOffer(list[i], i, _vm.CrateOpen));

            _offerCount.text = list.Count + (list.Count == 1 ? " OFFER" : " OFFERS");
        }

        private VisualElement BuildSpectral(SpectralVm pick)
        {
            var row = new VisualElement();
            row.AddToClassList("gx-owned-item");
            var name = new Label { text = pick.Label };
            name.AddToClassList("gx-owned-name");
            row.Add(name);

            var apply = new Button(() => _vm.TakeSpectral(pick)) { text = "APPLY" };
            apply.AddToClassList("gx-sell");
            row.Add(apply);
            return row;
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
            // "CAR FULL" vs "NO FUNDS": a bought part is always fitted, so a full car blocks the buy
            // just as surely as an empty wallet — and a button that says nothing while the player is
            // holding money reads as a bug rather than as a rule.
            string blocked = _vm.CarIsFull ? "CAR FULL — SELL ONE" : "NO FUNDS";
            Button buy = isCrate
                ? new Button(() => _vm.TakeFromCrate(part)) { text = o.Affordable ? "KEEP" : blocked }
                : new Button(() => _vm.Buy(part)) { text = o.Affordable ? "BUY" : blocked };
            buy.AddToClassList("gx-btn");
            buy.SetEnabled(o.Affordable);
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
            RefreshStatPreview();
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
            // All four, but only the ones that actually MOVE — AddDeltaPart's 0.5-point deadband keeps
            // a part that touches nothing but grip from printing three "no change" columns.
            AddDeltaPart(wrap, "POWER", o.Power);
            AddDeltaPart(wrap, "GRIP", o.Grip);
            AddDeltaPart(wrap, "LIGHT", o.Weight);
            AddDeltaPart(wrap, "DURA", o.Durability);
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
