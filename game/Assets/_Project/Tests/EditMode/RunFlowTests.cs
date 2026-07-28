using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Meta;
using Shitboxer.Race;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers RunDirector's season shape, its opt-in boss-race wiring, and the repair/economy-depth knobs.
    /// Season shape pins the roadmap's "start with 1 circuit, not 8" default and the re-stamp-on-adopt
    /// contract that keeps a resumed run tracking the CURRENT configured season rather than whatever
    /// default RunSave rebuilt it with. The boss/repair knobs all default to today's exact behaviour.
    /// The load-bearing contract there: with boss races DISABLED no race is designated a
    /// boss, the ruleset stays Standard and no boss reward/free-repair fires; with them ENABLED the circuit's
    /// final race takes RaceRuleset.Boss and a clean finish earns the boss bonus while NoRepairAfter withholds
    /// the interlude repair. Repair pricing at the shipped exponent 1 reproduces the original inline formula
    /// bit-for-bit and only reshapes off the endpoints when a designer dials the exponent. The boss flow is
    /// exercised through RunDirector's pure static helpers (no MonoBehaviour/scene needed) and the economy
    /// through RunState, mirroring how the existing Meta fixtures pin these formulas.
    /// </summary>
    public class RunFlowTests : TestBase
    {
        // The shipped RunDirector serialized default these tests price against.
        private const int ShippedFullRepairCost = 12;

        private static void AssertRuleset(in RaceRuleset expected, in RaceRuleset actual, string ctx)
        {
            Assert.AreEqual(expected.Laps, actual.Laps, $"{ctx}: laps");
            Assert.AreEqual(expected.CutoffFraction, actual.CutoffFraction, $"{ctx}: cutoff");
            Assert.AreEqual(expected.IsBoss, actual.IsBoss, $"{ctx}: boss flag");
            Assert.AreEqual(expected.Modifiers, actual.Modifiers, $"{ctx}: modifiers");
        }

        // --- Clean-finish payout: the shared preview/resolution formula (wave 16) --------------------
        //
        // RaceHud's mid-race payout preview and the real race resolution both call CleanFinishPayout, so
        // these pin the ORDER that keeps them honest. The order is balance-critical: sponsor money is added
        // last and must never be stake-scaled or DoublePayout-doubled.

        private static PartDef EconomyPart(int perPositionRate)
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            p.Category = PartCategory.Economy;
            p.MoneyPerPositionHeld = perPositionRate;
            return p;
        }

        private static readonly PayoutTable Table = new PayoutTable();

        [Test]
        public void CleanFinishPayout_IsInverted_LastPaysMoreThanFirst()
        {
            // The design's whole premise (doc 03). If this ever flips, the run has no tension left.
            int first = RunDirector.CleanFinishPayout(1, Table, null, 1f, false, default, 0);
            int last = RunDirector.CleanFinishPayout(8, Table, null, 1f, false, default, 0);
            Assert.Greater(last, first, "a worse finish must bank more — the economy is inverted");
        }

        [Test]
        public void CleanFinishPayout_ShippedDefaults_AreBasePlusPodium()
        {
            // Stake 0, no boss, no parts: exactly PayoutTable's own figure, nothing layered on.
            for (int pos = 1; pos <= 8; pos++)
                Assert.AreEqual(
                    Table.PayoutFor(pos, false),
                    RunDirector.CleanFinishPayout(pos, Table, null, 1f, false, default, 0),
                    $"P{pos} at shipped defaults must be byte-for-byte the table's payout");
        }

        [Test]
        public void CleanFinishPayout_SponsorMoney_IsNotScaledByStake()
        {
            // THE order test. Stake scales the position cash only; sponsor money rides on top untouched.
            // Folding sponsor in before the multiply would silently inflate every staked run's economy.
            var parts = new List<PartDef> { EconomyPart(4) };
            const float stakeMult = 1.15f;

            int expectedPay = Mathf.CeilToInt(Table.PayoutFor(6, false) * stakeMult);
            int expectedSponsor = Table.EconomyBonusFor(4, 6);

            Assert.AreEqual(
                expectedPay + expectedSponsor,
                RunDirector.CleanFinishPayout(6, Table, parts, stakeMult, false, default, 0));
        }

        [Test]
        public void CleanFinishPayout_SponsorMoney_IsNotDoubledByBossDoublePayout()
        {
            // Same order contract against the boss reward: DoublePayout doubles the position cash and the
            // flat bonus rides after it (ApplyBossReward), but sponsor money is added afterwards, undoubled.
            var parts = new List<PartDef> { EconomyPart(4) };
            RaceRuleset boss = RaceRuleset.Boss;

            int expectedPay = RunDirector.ApplyBossReward(Table.PayoutFor(6, false), boss, 8);
            int expectedSponsor = Table.EconomyBonusFor(4, 6);

            Assert.AreEqual(
                expectedPay + expectedSponsor,
                RunDirector.CleanFinishPayout(6, Table, parts, 1f, true, boss, 8));
        }

        [Test]
        public void CleanFinishPayout_SponsorMoney_IsCappedSoLastPlaceDoesNotCompound()
        {
            // The anti-sandbag cap: past EconomyBonusPositionCap, dropping further back adds no sponsor cash.
            var parts = new List<PartDef> { EconomyPart(4) };
            int atCap = RunDirector.CleanFinishPayout(Table.EconomyBonusPositionCap, Table, parts, 1f, false, default, 0);
            int beyondCap = RunDirector.CleanFinishPayout(8, Table, parts, 1f, false, default, 0);

            Assert.AreEqual(
                Table.EconomyBonusFor(4, Table.EconomyBonusPositionCap),
                Table.EconomyBonusFor(4, 8),
                "sponsor money must not keep growing past the cap");
            // The base payout still differs by position, so compare only the sponsor component's contribution.
            Assert.AreEqual(
                Table.PayoutFor(8, false) - Table.PayoutFor(Table.EconomyBonusPositionCap, false),
                beyondCap - atCap,
                "beyond the cap, only the base payout may move");
        }

        [Test]
        public void CleanFinishPayout_NullTable_IsZeroNotAThrow()
        {
            // The HUD preview calls this every OnGUI frame; an unconfigured director must not spam exceptions.
            Assert.AreEqual(0, RunDirector.CleanFinishPayout(3, null, null, 1f, false, default, 0));
        }

        // --- Season shape (doc 08 decision 12: the 8-circuit, 24-race full season) ------------------

        [Test]
        public void DefaultSeason_IsEightCircuits_PerDecision12()
        {
            // Doc 08 decision 12 deliberately overrides the roadmap's "start with 1 circuit, not 8":
            // the 24-race season is what team upgrades, long-horizon parts and the retuned bot ramp
            // are all sized against. Pin the default so it can't drift without someone deliberately
            // editing this assertion — the same guard the old 1-circuit pin provided.
            Assert.AreEqual(8, new RunState().TotalCircuits);
        }

        [Test]
        public void ApplySeasonShape_StampsConfiguredCircuitCount()
        {
            var run = new RunState { TotalCircuits = 1 };
            RunDirector.ApplySeasonShape(run, 8);
            Assert.AreEqual(8, run.TotalCircuits, "the director's inspector value should win");
        }

        [Test]
        public void ApplySeasonShape_ClampsToAtLeastOneCircuit()
        {
            // A zero/negative season would make IsFinalCircuit true before racing and end the run
            // instantly, so the clamp is load-bearing rather than cosmetic.
            Assert.AreEqual(1, RunDirector.ApplySeasonShape(new RunState(), 0).TotalCircuits);
            Assert.AreEqual(1, RunDirector.ApplySeasonShape(new RunState(), -5).TotalCircuits);
        }

        [Test]
        public void ApplySeasonShape_IsNullTolerant()
        {
            Assert.IsNull(RunDirector.ApplySeasonShape(null, 3));
        }

        [Test]
        public void ApplySeasonShape_ReStampsAfterSaveResume_SinceRunSaveDropsTuningFields()
        {
            // RunSave deliberately does not persist TotalCircuits (it's a run-start constant), so a
            // resumed run is rebuilt with RunState's default and would silently ignore a retune. The
            // director re-stamps on adopt; this pins that contract end-to-end through the real DTO.
            var pool = ScriptableObject.CreateInstance<PartPool>();
            pool.Parts = new List<PartDef>();

            var original = new RunState { TotalCircuits = 5, Money = 9, Lives = 2 };
            RunState resumed = RunSave.From(original).ToRunState(pool);

            // The default is 8 since decision 12 (the 24-race season) — the point stands unchanged:
            // whatever the saved run carried (5 here) is dropped, and the rebuilt value is the
            // class default until the director re-stamps its own configured length.
            Assert.AreEqual(8, resumed.TotalCircuits, "the DTO drops season length — it rebuilds at the default");
            Assert.AreEqual(9, resumed.Money, "run PROGRESS must still survive the round-trip");

            RunDirector.ApplySeasonShape(resumed, 5);
            Assert.AreEqual(5, resumed.TotalCircuits, "the director must restore the configured season on resume");
        }

        [Test]
        public void OneCircuitSeason_CompletesAfterItsFinalRace()
        {
            // The shipped default end-to-end: circuit 0 is immediately the final circuit, and the run
            // completes only once all RacesPerCircuit races of it are cleared.
            var run = RunDirector.ApplySeasonShape(new RunState { RacesPerCircuit = 3 }, 1);
            Assert.IsTrue(run.IsFinalCircuit, "a 1-circuit season is on its final circuit from the start");

            for (int race = 0; race < run.RacesPerCircuit; race++)
            {
                Assert.IsFalse(run.RunComplete, $"race {race} of a 1-circuit season must not end the run");
                run.RaceIndex++;
            }
            Assert.IsTrue(run.RunComplete, "clearing the last race of the only circuit completes the season");
        }

        // --- Boss designation + ruleset selection (default OFF == today) ----------------------------

        [Test]
        public void BossDisabled_NeverDesignatesBoss_AndKeepsStandardRuleset()
        {
            // The master switch off: neither a boss race nor an ordinary one is ever a boss, and the
            // ruleset selected for EITHER is the neutral Standard — byte-for-byte the shipped race.
            Assert.IsFalse(RunDirector.IsDesignatedBoss(false, true), "boss races off must never designate a boss");
            Assert.IsFalse(RunDirector.IsDesignatedBoss(false, false));

            AssertRuleset(RaceRuleset.Standard, RunDirector.RulesetForRace(false, true), "disabled, circuit boss");
            AssertRuleset(RaceRuleset.Standard, RunDirector.RulesetForRace(false, false), "disabled, ordinary race");
        }

        [Test]
        public void BossEnabled_DesignatesOnlyTheCircuitBoss()
        {
            // Enabled: only the circuit's boss (its final race) is a boss; ordinary races stay Standard.
            Assert.IsFalse(RunDirector.IsDesignatedBoss(true, false), "an ordinary race is never a boss");
            Assert.IsTrue(RunDirector.IsDesignatedBoss(true, true), "the circuit's final race is the boss");

            AssertRuleset(RaceRuleset.Standard, RunDirector.RulesetForRace(true, false), "enabled, ordinary race");
            AssertRuleset(RaceRuleset.Boss, RunDirector.RulesetForRace(true, true), "enabled, circuit boss");
        }

        [Test]
        public void EnabledBossRuleset_CarriesBossFlagsButNotDoublePayout()
        {
            // The designated boss gets the Boss template's flags (boss + damage-amplified + no-repair),
            // and NOT DoublePayout — the shipped Boss template does not double credits.
            RaceRuleset r = RunDirector.RulesetForRace(true, true);
            Assert.IsTrue(r.IsBoss);
            Assert.IsTrue(r.Has(RaceModifier.DamageAmplified), "boss amplifies contact damage (consumed race-side)");
            Assert.IsTrue(r.Has(RaceModifier.NoRepairAfter), "boss withholds the interlude repair");
            Assert.IsFalse(r.Has(RaceModifier.DoublePayout), "the shipped Boss template does not double payout");
        }

        [Test]
        public void RulesetForRace_TracksRunStateBossDesignationAcrossACircuit()
        {
            // Ties the designation to RunState.IsBossRace (final race of the circuit): only the last race
            // becomes a boss when enabled, and every race stays Standard when disabled.
            var run = new RunState { RacesPerCircuit = 3 };
            for (int race = 0; race < run.RacesPerCircuit; race++)
            {
                run.RaceIndex = race;
                bool isCircuitBoss = race == run.RacesPerCircuit - 1;
                Assert.AreEqual(isCircuitBoss, run.IsBossRace, $"race {race}");

                AssertRuleset(RaceRuleset.Standard, RunDirector.RulesetForRace(false, run.IsBossRace),
                    $"disabled, race {race}");
                AssertRuleset(isCircuitBoss ? RaceRuleset.Boss : RaceRuleset.Standard,
                    RunDirector.RulesetForRace(true, run.IsBossRace), $"enabled, race {race}");
            }
        }

        // --- Boss reward payout (Meta-side) ---------------------------------------------------------

        [Test]
        public void ApplyBossReward_AddsFlatBonusWithoutDoublePayout()
        {
            // The shipped Boss template lacks DoublePayout, so a clean boss finish just adds the flat bonus.
            Assert.AreEqual(18, RunDirector.ApplyBossReward(10, RaceRuleset.Boss, 8));
            Assert.AreEqual(18, RunDirector.ApplyBossReward(10, RaceRuleset.Standard, 8),
                "a ruleset without DoublePayout only adds the bonus");
        }

        [Test]
        public void ApplyBossReward_DoublesFirstThenAddsBonusWhenDoublePayoutSet()
        {
            // A DoublePayout ruleset (e.g. the double-or-nothing event) doubles the position cash, then adds
            // the flat bonus on top: 10 * 2 + 8 = 28.
            Assert.IsTrue(RaceRuleset.DoubleOrNothing.Has(RaceModifier.DoublePayout));
            Assert.AreEqual(28, RunDirector.ApplyBossReward(10, RaceRuleset.DoubleOrNothing, 8));
        }

        [Test]
        public void ApplyBossReward_ZeroBonusIsIdentityWithoutDoublePayout()
        {
            // A zero bonus with a non-doubling ruleset leaves the payout untouched (defensive: proves the
            // bonus is purely additive, so the reward's magnitude never leaks into a non-boss payout).
            Assert.AreEqual(10, RunDirector.ApplyBossReward(10, RaceRuleset.Boss, 0));
        }

        // --- NoRepairAfter gate on the interlude free-repair ----------------------------------------

        [Test]
        public void GrantsFreeRepair_HonoursNoRepairAfter()
        {
            // The shipped Boss template carries NoRepairAfter, so a clean boss finish does NOT free-repair —
            // its damage rides into the garage. A ruleset without the flag would grant the repair.
            Assert.IsFalse(RunDirector.GrantsFreeRepair(true, RaceRuleset.Boss),
                "NoRepairAfter must skip the interlude repair");
            Assert.IsTrue(RunDirector.GrantsFreeRepair(true, RaceRuleset.Standard),
                "a boss ruleset without NoRepairAfter grants the free repair");
            Assert.IsFalse(RunDirector.GrantsFreeRepair(false, RaceRuleset.Standard),
                "a non-boss race never free-repairs");
        }

        // --- Repair cost: default reproduces today; exponent reshapes off the endpoints -------------

        // The original inline RunDirector formula these tests pin the default against.
        private static int ShippedRepairCost(float carDurability, int fullRepairCost)
        {
            float wear = 1f - carDurability;
            if (wear <= 0f) return 0;
            float span = 1f - VehicleSim.MinDurability;
            float t = span > 0f ? Mathf.Clamp01(wear / span) : 1f;
            return Mathf.Max(1, Mathf.CeilToInt(fullRepairCost * t));
        }

        [Test]
        public void RepairCost_DefaultExponent_MatchesShippedFormulaBitForBit()
        {
            // Across the whole durability range (incl. below the floor), the default-exponent cost equals
            // the original inline formula exactly — the shipped repair number is untouched.
            float[] durabilities = { 1f, 0.99f, 0.95f, 0.9f, 0.8f, 0.7f, 0.6f, 0.5f, 0.45f, 0.4f, 0.3f, 0f };
            foreach (float dur in durabilities)
            {
                int expected = ShippedRepairCost(dur, ShippedFullRepairCost);
                Assert.AreEqual(expected, RunState.RepairCostFor(dur, ShippedFullRepairCost),
                    $"default-arg repair cost drifted at durability {dur}");
                Assert.AreEqual(expected, RunState.RepairCostFor(dur, ShippedFullRepairCost, 1f),
                    $"explicit exponent-1 repair cost drifted at durability {dur}");
            }
        }

        [Test]
        public void RepairCost_PristineIsFree_FloorIsFull_AnyWearCostsAtLeastOne()
        {
            Assert.AreEqual(0, RunState.RepairCostFor(1f, ShippedFullRepairCost), "a pristine car repairs for free");
            Assert.GreaterOrEqual(RunState.RepairCostFor(0.999f, ShippedFullRepairCost), 1,
                "any wear at all costs at least $1");
            Assert.AreEqual(ShippedFullRepairCost, RunState.RepairCostFor(VehicleSim.MinDurability, ShippedFullRepairCost),
                "at the durability floor (zero since decision 15 — a wreck) a repair costs the full price");
            Assert.AreEqual(ShippedFullRepairCost, RunState.RepairCostFor(-0.5f, ShippedFullRepairCost),
                "below the floor still clamps to the full price");
            // Since decision 15 the wear span is the whole 0..1 range, so a part-worn car pays
            // proportionally: durability 0.2 is 80% wear, not floor-price.
            Assert.AreEqual(Mathf.CeilToInt(ShippedFullRepairCost * 0.8f),
                RunState.RepairCostFor(0.2f, ShippedFullRepairCost),
                "part-worn repair scales linearly across the full 0..1 wear span");
        }

        [Test]
        public void RepairCost_ExponentReshapesCurveButKeepsEndpoints()
        {
            // Endpoints are exponent-invariant: pristine is free and the floor is full price for any exponent.
            foreach (float exp in new[] { 0.5f, 2f, 3f })
            {
                Assert.AreEqual(0, RunState.RepairCostFor(1f, ShippedFullRepairCost, exp), $"pristine, exp {exp}");
                Assert.AreEqual(ShippedFullRepairCost,
                    RunState.RepairCostFor(VehicleSim.MinDurability, ShippedFullRepairCost, exp), $"floor, exp {exp}");
            }

            // Mid-damage (durability 0.5 → normalized wear 0.5 on the decision-15 full-range span): a
            // convex exponent (>1) makes a partial repair strictly cheaper than the linear default, a
            // concave one (<1) strictly dearer — the "scales when configured" behaviour, off the fixed
            // endpoints.
            int linear = RunState.RepairCostFor(0.5f, ShippedFullRepairCost, 1f);       // 12 * 0.5     = 6
            int convex = RunState.RepairCostFor(0.5f, ShippedFullRepairCost, 2f);       // 12 * 0.5^2   = 3
            int concave = RunState.RepairCostFor(0.5f, ShippedFullRepairCost, 0.5f);    // 12 * 0.5^0.5 ≈ 8.49 → 9
            Assert.AreEqual(6, linear);
            Assert.Less(convex, linear, "a convex exponent makes partial damage cheaper than linear");
            Assert.Greater(concave, linear, "a concave exponent makes partial damage dearer than linear");
        }

        // --- Garage economy hooks are no-ops at the shipped defaults --------------------------------

        [Test]
        public void GarageEconomyHooks_AreNoOpsAtShippedDefaults()
        {
            // OpenGarage calls ResetRerollCounter + ApplyShopInterest each visit; at the shipped defaults
            // (interest rate 0, reroll increment 0) neither moves money or the reroll curve off today's.
            var run = new RunState { Money = 100 };
            Assert.AreEqual(0, run.ApplyShopInterest(), "default interest rate pays nothing");
            Assert.AreEqual(100, run.Money, "no-op interest must not change banked money");

            run.ResetRerollCounter();
            Assert.AreEqual(0, run.RerollsThisVisit, "a fresh visit starts at zero rerolls");
            Assert.AreEqual(ShopLogic.BaseRerollCost, run.NextRerollCost(),
                "the first reroll of a visit costs the shipped base");
        }
    }
}
