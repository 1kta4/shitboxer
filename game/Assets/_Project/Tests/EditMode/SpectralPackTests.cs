using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Meta;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// The SpectralOffer string codec (doc 08 slice 13) — pure, runs everywhere. The pack lives on
    /// RunState as these strings, so a junk-tolerant codec is what keeps a stale save from wedging
    /// the shop.
    /// </summary>
    public class SpectralOfferTests : TestBase
    {
        [Test]
        public void EncodeDecode_RoundTripsEveryTier()
        {
            foreach (PartEdition tier in SpectralOffer.Tiers)
            {
                string encoded = SpectralOffer.Encode(tier, "junkyard_turbo");
                Assert.IsTrue(SpectralOffer.TryDecode(encoded, out PartEdition edition, out string partId),
                    $"{tier} failed to decode its own encoding");
                Assert.AreEqual(tier, edition);
                Assert.AreEqual("junkyard_turbo", partId);
            }
        }

        [Test]
        public void Decode_DropsJunkInsteadOfThrowing()
        {
            Assert.IsFalse(SpectralOffer.TryDecode(null, out _, out _), "null");
            Assert.IsFalse(SpectralOffer.TryDecode("", out _, out _), "empty");
            Assert.IsFalse(SpectralOffer.TryDecode("Foil", out _, out _), "no separator");
            Assert.IsFalse(SpectralOffer.TryDecode("Foil:", out _, out _), "no target");
            Assert.IsFalse(SpectralOffer.TryDecode(":tow_cell", out _, out _), "no tier");
            Assert.IsFalse(SpectralOffer.TryDecode("None:tow_cell", out _, out _),
                "None is a non-material — a do-nothing offer must never decode as valid");
            Assert.IsFalse(SpectralOffer.TryDecode("Chrome:tow_cell", out _, out _), "unknown tier");
        }

        [Test]
        public void Tiers_AreAscending_AndAllWeighted()
        {
            for (int i = 1; i < SpectralOffer.Tiers.Length; i++)
                Assert.Greater(SpectralOffer.Tiers[i], SpectralOffer.Tiers[i - 1],
                    "tier order IS the upgrade order — RollEditionAbove walks it");
            foreach (PartEdition tier in SpectralOffer.Tiers)
                Assert.Greater(SpectralOffer.Weight(tier), 0, $"{tier} can never be drawn at weight 0");
            Assert.AreEqual(0, SpectralOffer.Weight(PartEdition.None), "None is never offered");
            Assert.Greater(SpectralOffer.Weight(PartEdition.Foil), SpectralOffer.Weight(PartEdition.Polychrome),
                "Foil is the common pull, Polychrome the jackpot");
        }
    }

    /// <summary>
    /// The Spectral pack end to end (doc 08 slice 13): eligibility, the applied edition reaching
    /// the bake and the refund, and the sold-target purge. Editor-only — the fixture builds
    /// ScriptableObject parts, so the standalone harness skips it (run in Unity's Test Runner).
    /// </summary>
    public class SpectralPackTests : TestBase
    {
        private static PartDef StatPart(string id, float gripMult, int price = 6)
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            p.Id = id;
            p.DisplayName = id;
            p.Category = PartCategory.Stat;
            p.Price = price;
            p.SpecMods = new List<SpecMod>
            {
                new SpecMod { Target = SpecModTarget.GripFront, Multiplier = gripMult },
            };
            return p;
        }

        private static PartDef PlainPart(string id)
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            p.Id = id;
            p.DisplayName = id;
            p.Category = PartCategory.Economy;
            p.Price = 5;
            return p;
        }

        private static RunState RunWith(params PartDef[] fitted)
        {
            var run = new RunState { Money = 50 };
            foreach (PartDef part in fitted)
            {
                run.OwnedParts.Add(part);
                run.Equip(part);
            }
            return run;
        }

        [Test]
        public void EditionOf_DefaultsToNone_AndUpgradesOnlyClimb()
        {
            PartDef part = StatPart("tow_cell", 1.1f);
            RunState run = RunWith(part);

            Assert.AreEqual(PartEdition.None, run.EditionOf(part), "an untouched part is plain");
            Assert.IsTrue(run.TryUpgradeEdition(part, PartEdition.Holo), "None -> Holo climbs");
            Assert.IsFalse(run.TryUpgradeEdition(part, PartEdition.Foil), "Holo -> Foil is a downgrade — refused");
            Assert.IsFalse(run.TryUpgradeEdition(part, PartEdition.Holo), "same tier again does nothing");
            Assert.IsTrue(run.TryUpgradeEdition(part, PartEdition.Polychrome), "Holo -> Polychrome climbs");
            Assert.AreEqual(PartEdition.Polychrome, run.EditionOf(part));
        }

        [Test]
        public void AppliedEdition_ReachesTheBake_ThroughTheResolver()
        {
            PartDef part = StatPart("sticky", 1.10f);
            RunState run = RunWith(part);
            var baseSpec = new VehicleSpec();
            float stockMu = baseSpec.FrontTyre.PeakMu;

            VehicleSpec plain = SpecModApplier.Apply(baseSpec, run.EquippedParts, run.EditionOf);
            Assert.AreEqual(stockMu * 1.10f, plain.FrontTyre.PeakMu, 1e-4f, "no material: the shipped bake");

            run.TryUpgradeEdition(part, PartEdition.Polychrome); // x2 on the DEVIATION: +10% -> +20%
            VehicleSpec poly = SpecModApplier.Apply(baseSpec, run.EquippedParts, run.EditionOf);
            Assert.AreEqual(stockMu * 1.20f, poly.FrontTyre.PeakMu, 1e-4f,
                "a run-applied edition must amplify the bake through the resolver");

            VehicleSpec assetOnly = SpecModApplier.Apply(baseSpec, run.EquippedParts);
            Assert.AreEqual(stockMu * 1.10f, assetOnly.FrontTyre.PeakMu, 1e-4f,
                "without the resolver the asset's authored (None) edition still rules — old callers unchanged");
        }

        [Test]
        public void BuyingASpectralPack_RequiresAnEligibleFittedPart()
        {
            // Eligible = fitted, has SpecMods, below Polychrome. A run with only a stat-less part
            // must be refused at BUY time — never sell a pack whose prize can't be taken.
            var shop = new ShopLogic(seed: 7);
            RunState run = RunWith(PlainPart("banner"));

            Assert.AreEqual(0, SpectralDrawCount(shop, run), "no stat part fitted: nothing to draw");

            PartDef maxed = StatPart("maxed", 1.1f);
            RunState run2 = RunWith(maxed);
            run2.TryUpgradeEdition(maxed, PartEdition.Polychrome);
            Assert.AreEqual(0, SpectralDrawCount(shop, run2), "a Polychrome part can climb no further");

            RunState run3 = RunWith(StatPart("live", 1.1f));
            Assert.Greater(SpectralDrawCount(shop, run3), 0, "one eligible fitted part is a sellable pack");
        }

        // Buys a Spectral pack through the public seam by stacking the shelf: TryBuyPack draws only
        // for the Spectral kind, so the drawn count IS the eligibility signal.
        private static int SpectralDrawCount(ShopLogic shop, RunState run)
        {
            run.PackSpectrals.Clear();
            // Find the Spectral pack on a rolled shelf; reroll the packs until one shows up.
            // Bounded: with weight 15/105 per slot, 200 visits without one means the table broke.
            for (int visit = 0; visit < 200; visit++)
            {
                shop.BeginVisit(new List<PartDef>(), run);
                for (int i = 0; i < shop.Packs.Count; i++)
                    if (shop.Packs[i].Kind == ShopPackKind.Spectral)
                    {
                        run.Money = 50;
                        return shop.TryBuyPack(i, new List<PartDef>(), run) ? run.PackSpectrals.Count : 0;
                    }
            }
            Assert.Fail("no Spectral pack rolled in 200 visits — the weight table is broken");
            return 0;
        }

        [Test]
        public void TakingAnOffer_StampsTheEdition_AndClosesThePack()
        {
            var shop = new ShopLogic(seed: 3);
            PartDef part = StatPart("coilovers", 1.08f);
            RunState run = RunWith(part);
            run.PackSpectrals.Add(SpectralOffer.Encode(PartEdition.Holo, "coilovers"));

            Assert.IsFalse(shop.TryTakeSpectral(part, PartEdition.Foil, run), "an offer not in the pack is refused");
            Assert.IsTrue(shop.TryTakeSpectral(part, PartEdition.Holo, run));
            Assert.AreEqual(PartEdition.Holo, run.EditionOf(part), "the pick stamps the run edition");
            Assert.IsFalse(run.SpectralPackOpen, "one pick resolves the pack");
        }

        [Test]
        public void SellingATarget_PurgesItsOffer_AndPricesTheAppliedEditionIn()
        {
            var shop = new ShopLogic(seed: 3);
            PartDef kept = StatPart("kept", 1.1f, price: 6);
            PartDef sold = StatPart("sold", 1.1f, price: 6);
            RunState run = RunWith(kept, sold);
            run.PackSpectrals.Add(SpectralOffer.Encode(PartEdition.Foil, "kept"));
            run.PackSpectrals.Add(SpectralOffer.Encode(PartEdition.Holo, "sold"));

            Assert.AreEqual(3, shop.SellValueOf(sold, run), "a plain $6 part refunds half");
            run.TryUpgradeEdition(sold, PartEdition.Holo);
            Assert.AreEqual(6, shop.SellValueOf(sold, run), "a Holo (x2 price) part refunds against the multiplied price");

            Assert.IsTrue(shop.TrySell(sold, run));
            Assert.AreEqual(1, run.PackSpectrals.Count, "the sold part's offer dies with it");
            Assert.IsTrue(run.PackSpectrals[0].EndsWith("kept"), "the surviving offer still aims at the kept part");
            Assert.AreEqual(PartEdition.None, run.EditionOf(sold), "the material left with the part");
        }

        [Test]
        public void SaveRoundTrip_KeepsEditionsAndTheOpenPack()
        {
            PartDef part = StatPart("round_trip", 1.1f);
            var pool = ScriptableObject.CreateInstance<PartPool>();
            pool.Parts = new List<PartDef> { part };

            RunState run = RunWith(part);
            run.TryUpgradeEdition(part, PartEdition.Foil);
            run.PackSpectrals.Add(SpectralOffer.Encode(PartEdition.Holo, "round_trip"));
            run.PackComponents.Add((int)CarComponent.Turbo);

            RunState restored = RunSave.From(run).ToRunState(pool);
            Assert.AreEqual(PartEdition.Foil, restored.EditionOf(part), "the applied edition survives a save");
            Assert.IsTrue(restored.SpectralPackOpen, "a paid-for open Spectral pack survives a save");
            Assert.AreEqual(run.PackSpectrals[0], restored.PackSpectrals[0]);
            Assert.IsTrue(restored.ComponentPackOpen,
                "a paid-for open components pack survives a save (the gap this slice closed)");
        }
    }
}
