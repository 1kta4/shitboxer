using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Meta;
using Shitboxer.Vehicle;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the ten car components (doc 08 decisions 4, 5, 11): the level-1 identity that keeps a
    /// fresh run's car untouched, the family assignment the garage and family-jokers both read, the
    /// points a level is worth, and the Blueprint economy.
    /// </summary>
    public class CarComponentTests : TestBase
    {
        static int[] AllAt(int level)
        {
            var levels = new int[CarComponentCatalog.Count];
            for (int i = 0; i < levels.Length; i++) levels[i] = level;
            return levels;
        }

        static int[] Baseline() => AllAt(CarComponentCatalog.MinLevel);

        // ---------------------------------------------------------------- the identity

        [Test]
        public void EveryComponentAtBaseline_ContributesNothing()
        {
            // THE property that lets the director bake unconditionally: a fresh run must produce a spec
            // byte-for-byte identical to the authored chassis.
            BuildLedger ledger = CarComponentCatalog.Accumulate(Baseline());
            Assert.AreEqual(0f, ledger.Power, 1e-6f);
            Assert.AreEqual(0f, ledger.Grip, 1e-6f);
            Assert.AreEqual(0f, ledger.Weight, 1e-6f);
            Assert.AreEqual(0f, ledger.Durability, 1e-6f);
        }

        [Test]
        public void NullOrShortLevelArray_ReadsAsBaseline()
        {
            // A save written before a component existed must load as "that one is at level 1", not crash.
            Assert.AreEqual(0f, CarComponentCatalog.Accumulate(null).Power, 1e-6f);
            Assert.AreEqual(0f, CarComponentCatalog.Accumulate(new List<int> { 1, 1 }).Power, 1e-6f);
        }

        [Test]
        public void CorruptLevelsAreClampedRatherThanTrusted()
        {
            var wild = new int[CarComponentCatalog.Count];
            for (int i = 0; i < wild.Length; i++) wild[i] = i % 2 == 0 ? -500 : 9999;

            BuildLedger ledger = CarComponentCatalog.Accumulate(wild);
            BuildLedger maxed = CarComponentCatalog.Accumulate(AllAt(CarComponentCatalog.MaxLevel));
            Assert.LessOrEqual(ledger.Power, maxed.Power + 1e-4f);
            Assert.GreaterOrEqual(ledger.Power, 0f);
        }

        // ---------------------------------------------------------------- catalogue shape

        [Test]
        public void ThereAreTenComponents_AndTheEnumAgrees()
        {
            Assert.AreEqual(10, CarComponentCatalog.Count);
            Assert.AreEqual(System.Enum.GetValues(typeof(CarComponent)).Length, CarComponentCatalog.Count);
        }

        [Test]
        public void EveryEnumMemberHasACatalogueEntryAtItsOwnOrdinal()
        {
            // Accumulate indexes the catalogue by enum ordinal, so a mismatch would silently attribute
            // one component's levels to another.
            foreach (CarComponent c in System.Enum.GetValues(typeof(CarComponent)))
                Assert.AreEqual(c, CarComponentCatalog.Info(c).Component, $"{c} is out of order");
        }

        [Test]
        public void FamiliesMatchDecisionFive()
        {
            // Locked assignment — the ESC-menu stat display doubles as the family display, so these are
            // player-visible groupings, not an implementation detail.
            Assert.AreEqual(BuildStat.Power, CarComponentCatalog.Info(CarComponent.Engine).Family);
            Assert.AreEqual(BuildStat.Power, CarComponentCatalog.Info(CarComponent.Turbo).Family);
            Assert.AreEqual(BuildStat.Power, CarComponentCatalog.Info(CarComponent.Exhaust).Family);
            Assert.AreEqual(BuildStat.Power, CarComponentCatalog.Info(CarComponent.Ecu).Family);
            Assert.AreEqual(BuildStat.Grip, CarComponentCatalog.Info(CarComponent.Tyres).Family);
            Assert.AreEqual(BuildStat.Grip, CarComponentCatalog.Info(CarComponent.Suspension).Family);
            Assert.AreEqual(BuildStat.Weight, CarComponentCatalog.Info(CarComponent.Interior).Family);
            Assert.AreEqual(BuildStat.Weight, CarComponentCatalog.Info(CarComponent.Chassis).Family);
            Assert.AreEqual(BuildStat.Durability, CarComponentCatalog.Info(CarComponent.Cooling).Family);
            Assert.AreEqual(BuildStat.Durability, CarComponentCatalog.Info(CarComponent.Transmission).Family);
        }

        [Test]
        public void EveryFamilyHasAtLeastTwoComponents()
        {
            // A one-component family would make its family-jokers a flat constant rather than something
            // a build can lean into.
            var counts = new Dictionary<BuildStat, int>();
            foreach (CarComponentInfo info in CarComponentCatalog.All)
            {
                counts.TryGetValue(info.Family, out int n);
                counts[info.Family] = n + 1;
            }
            foreach (BuildStat stat in System.Enum.GetValues(typeof(BuildStat)))
                Assert.GreaterOrEqual(counts.TryGetValue(stat, out int c) ? c : 0, 2, $"{stat} family is too thin");
        }

        // ---------------------------------------------------------------- levelling

        [Test]
        public void LevelsScaleLinearlyAboveTheBaseline()
        {
            var l2 = Baseline(); l2[(int)CarComponent.Tyres] = 2;
            var l11 = Baseline(); l11[(int)CarComponent.Tyres] = 11;

            float one = CarComponentCatalog.Accumulate(l2).Grip;
            float ten = CarComponentCatalog.Accumulate(l11).Grip;
            Assert.AreEqual(one * 10f, ten, 1e-4f);
        }

        [Test]
        public void SecondaryContributionsCanFallOutsideTheFamily()
        {
            // The chassis is a WEIGHT component that also stiffens grip. The family is a grouping tag,
            // not a claim that nothing else moves.
            var levels = Baseline();
            levels[(int)CarComponent.Chassis] = CarComponentCatalog.MaxLevel;
            BuildLedger ledger = CarComponentCatalog.Accumulate(levels);
            Assert.Greater(ledger.Weight, 0f);
            Assert.Greater(ledger.Grip, 0f);
        }

        [Test]
        public void PowerComponentsCarryTheirDrawbacks()
        {
            // The engine adds mass and the turbo costs reliability — a pure "+everything" component
            // would be a boring component.
            var engine = Baseline();
            engine[(int)CarComponent.Engine] = CarComponentCatalog.MaxLevel;
            Assert.Less(CarComponentCatalog.Accumulate(engine).Weight, 0f, "a bigger engine should weigh more");

            var turbo = Baseline();
            turbo[(int)CarComponent.Turbo] = CarComponentCatalog.MaxLevel;
            Assert.Less(CarComponentCatalog.Accumulate(turbo).Durability, 0f, "boost should cost reliability");
        }

        [Test]
        public void AFullyMaxedCarLandsNearTheStatSpans_NotThroughThem()
        {
            // 190 buyable levels is the theoretical ceiling nobody reaches. Even so it must map to
            // multipliers just under the spans rather than being clipped by the physics clamp.
            BuildLedger maxed = CarComponentCatalog.Accumulate(AllAt(CarComponentCatalog.MaxLevel));

            float grip = StatLedger.GripMult(maxed.Grip);
            float power = StatLedger.PowerMult(maxed.Power);
            Assert.Greater(grip, 1.3f, "maxing every grip component should be transformative");
            Assert.LessOrEqual(grip, 1f + StatLedger.GripSpan + 1e-4f);
            Assert.Greater(power, 1.5f);
            Assert.LessOrEqual(power, 1f + StatLedger.PowerSpan + 1e-4f);
        }

        // ---------------------------------------------------------------- the economy

        [Test]
        public void BlueprintPriceEscalatesWithLevel()
        {
            int early = CarComponentCatalog.BlueprintPrice(1);
            int late = CarComponentCatalog.BlueprintPrice(CarComponentCatalog.MaxLevel - 1);
            Assert.Greater(late, early, "deep investment in one component should cost more per level");
            Assert.GreaterOrEqual(early, 2);
        }

        [Test]
        public void MaxingOneComponentCostsAMeaningfulShareOfASeason()
        {
            int total = 0;
            for (int level = CarComponentCatalog.MinLevel; level < CarComponentCatalog.MaxLevel; level++)
                total += CarComponentCatalog.BlueprintPrice(level);

            // A 24-race season pays roughly $250 of position money. Maxing a single component should be
            // a real commitment but not the whole run — if this drifts outside the band, the economy
            // has silently become either trivial or impossible.
            Assert.Greater(total, 50, "too cheap — maxing everything would be automatic");
            Assert.Less(total, 150, "too dear — no component would ever reach max");
        }

        [Test]
        public void CanLevelStopsAtTheCeiling()
        {
            Assert.IsTrue(CarComponentCatalog.CanLevel(CarComponentCatalog.MaxLevel - 1));
            Assert.IsFalse(CarComponentCatalog.CanLevel(CarComponentCatalog.MaxLevel));
        }

        // ---------------------------------------------------------------- run state

        [Test]
        public void AFreshRunStartsEveryComponentAtBaseline()
        {
            var run = new RunState();
            foreach (CarComponent c in System.Enum.GetValues(typeof(CarComponent)))
                Assert.AreEqual(CarComponentCatalog.MinLevel, run.LevelOf(c));
        }

        [Test]
        public void BuyingABlueprintChargesAndLevels()
        {
            var run = new RunState { Money = 20 };
            int price = run.BlueprintPriceFor(CarComponent.Tyres);

            Assert.IsTrue(run.BuyBlueprint(CarComponent.Tyres));
            Assert.AreEqual(20 - price, run.Money);
            Assert.AreEqual(2, run.LevelOf(CarComponent.Tyres));
        }

        [Test]
        public void AnUnaffordableBlueprintChargesNothing()
        {
            var run = new RunState { Money = 0 };
            Assert.IsFalse(run.BuyBlueprint(CarComponent.Tyres));
            Assert.AreEqual(0, run.Money);
            Assert.AreEqual(CarComponentCatalog.MinLevel, run.LevelOf(CarComponent.Tyres));
        }

        [Test]
        public void AMaxedComponentCannotBeBoughtFurther()
        {
            var run = new RunState { Money = 9999 };
            run.ComponentLevels[(int)CarComponent.Tyres] = CarComponentCatalog.MaxLevel;

            Assert.IsFalse(run.BuyBlueprint(CarComponent.Tyres));
            Assert.AreEqual(9999, run.Money, "a rejected purchase must not charge");
        }

        [Test]
        public void ComponentsReachTheCarThroughTheLedger()
        {
            // End to end: levels -> points -> a baked spec that is genuinely faster.
            var spec = new VehicleSpec { MassKg = 1050f };
            spec.FrontTyre.PeakMu = 1.32f;
            spec.RearTyre.PeakMu = 1.32f;
            spec.Engine.PeakTorqueNm = 205f;

            var run = new RunState();
            run.ComponentLevels[(int)CarComponent.Tyres] = 10;
            run.ComponentLevels[(int)CarComponent.Engine] = 10;

            VehicleSpec built = StatLedger.Bake(spec, run.ComponentLedger());
            Assert.Greater(built.FrontTyre.PeakMu, 1.32f);
            Assert.Greater(built.Engine.PeakTorqueNm, 205f);
            Assert.LessOrEqual(built.FrontTyre.PeakMu, PhysicsCeilings.MaxPeakMu);
        }

        // ---------------------------------------------------------------- persistence

        [Test]
        public void ComponentLevelsSurviveASaveRoundTrip()
        {
            var run = new RunState { Money = 40 };
            run.ComponentLevels[(int)CarComponent.Turbo] = 7;
            run.ComponentLevels[(int)CarComponent.Cooling] = 13;

            RunState restored = RunSave.From(run).ToRunState(null);

            Assert.AreEqual(7, restored.LevelOf(CarComponent.Turbo));
            Assert.AreEqual(13, restored.LevelOf(CarComponent.Cooling));
            Assert.AreEqual(CarComponentCatalog.MinLevel, restored.LevelOf(CarComponent.Tyres));
        }

        [Test]
        public void ASaveWithNoComponentDataRestoresToBaseline()
        {
            // An older save predating components has no such field; it must resume as the pre-component
            // car rather than as an empty or corrupt level set.
            var legacy = new RunSave { money = 10, lives = 3 };
            RunState restored = legacy.ToRunState(null);

            Assert.AreEqual(CarComponentCatalog.Count, restored.ComponentLevels.Length);
            foreach (CarComponent c in System.Enum.GetValues(typeof(CarComponent)))
                Assert.AreEqual(CarComponentCatalog.MinLevel, restored.LevelOf(c));
        }

        [Test]
        public void JunkInTheSaveIsDiscardedRatherThanThrowing()
        {
            var save = new RunSave { money = 5, lives = 3 };
            save.componentLevels.Add("NotAComponent:4");
            save.componentLevels.Add("Tyres:notanumber");
            save.componentLevels.Add("malformed");
            save.componentLevels.Add(string.Empty);
            save.componentLevels.Add("Tyres:6");        // the one good entry

            RunState restored = null;
            Assert.DoesNotThrow(() => restored = save.ToRunState(null));
            Assert.AreEqual(6, restored.LevelOf(CarComponent.Tyres));
        }

        [Test]
        public void OnlyLevelledComponentsAreWritten()
        {
            // Level 1 is the restore default, so writing it into every save would be pure noise.
            var run = new RunState();
            Assert.AreEqual(0, RunSave.From(run).componentLevels.Count);

            run.ComponentLevels[(int)CarComponent.Ecu] = 3;
            Assert.AreEqual(1, RunSave.From(run).componentLevels.Count);
        }

        [Test]
        public void AFreshRunBakesTheAuthoredChassisUnchanged()
        {
            var spec = new VehicleSpec { MassKg = 1050f };
            spec.FrontTyre.PeakMu = 1.32f;
            spec.Engine.PeakTorqueNm = 205f;

            VehicleSpec built = StatLedger.Bake(spec, new RunState().ComponentLedger());
            Assert.AreEqual(1.32f, built.FrontTyre.PeakMu, 1e-4f);
            Assert.AreEqual(205f, built.Engine.PeakTorqueNm, 1e-4f);
            Assert.AreEqual(1050f, built.MassKg, 1e-3f);
        }
    }
}
