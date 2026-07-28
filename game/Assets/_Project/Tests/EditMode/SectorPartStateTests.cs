using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Meta;
using Shitboxer.Race;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the race-scoped scoring accumulator: money totals, timed bonus expiry, retriggers, and the
    /// clamps. Drives <see cref="SectorPartState.ResolveRules"/> directly rather than through PartDef
    /// assets, so the whole scoring pass is exercised without a live editor.
    ///
    /// The load-bearing tests here are the no-op one (a loadout with no sector rules must leave the
    /// economy and the car byte-for-byte untouched — doc 08 decision 9) and the expiry-window one (a
    /// 1-sector bonus must last exactly one sector, not two).
    /// </summary>
    public class SectorPartStateTests : TestBase
    {
        static List<SectorRule> Rules(params SectorRule[] rules) => new List<SectorRule>(rules);

        static SectorRule Money(SectorStyle tag, float amount) => new SectorRule
        {
            Trigger = SectorTriggerKind.Style,
            StyleTag = tag,
            Effect = SectorEffectKind.Money,
            Amount = amount,
        };

        static SectorRule Grip(SectorStyle tag, float amount, int duration = 0,
            SpecModOp op = SpecModOp.Add) => new SectorRule
        {
            Trigger = SectorTriggerKind.Style,
            StyleTag = tag,
            Effect = SectorEffectKind.Grip,
            Op = op,
            Amount = amount,
            DurationSectors = duration,
        };

        static SectorPartState.Totals Drive(SectorPartState state, IReadOnlyList<SectorRule> rules,
            SectorStyle style, SectorColour colour = SectorColour.None, float timeS = 20f,
            int contactsTaken = 0, int positionsGained = 0, bool finalSector = false,
            int sectorIndex = 0) =>
            state.ResolveRules(rules, sectorIndex, style, colour, timeS, contactsTaken, positionsGained,
                finalSector);

        // ---------------------------------------------------------------- the no-op guarantee

        [Test]
        public void NoSectorRules_PaysNothingAndLeavesTheCarUntouched()
        {
            // doc 08 decision 9: sectors pay nothing on their own. A run that owns no sector parts must
            // see the shipped position-only economy and the shipped driving feel, exactly.
            var state = new SectorPartState();
            for (int i = 0; i < 9; i++)
            {
                SectorPartState.Totals t = Drive(state, null, SectorStyle.Clean | SectorStyle.Aggressive);
                Assert.AreEqual(0, t.Money);
                Assert.AreEqual(0f, t.DurabilityDelta);
                Assert.AreEqual(1f, t.GripMult);
                Assert.AreEqual(1f, t.PowerMult);
            }
            Assert.AreEqual(0, state.MoneyEarned);
        }

        [Test]
        public void EmptyRuleList_IsAlsoANoOp()
        {
            var state = new SectorPartState();
            SectorPartState.Totals t = Drive(state, Rules(), SectorStyle.Clean);
            Assert.AreEqual(0, t.Money);
            Assert.AreEqual(1f, t.GripMult);
        }

        // ---------------------------------------------------------------- money

        [Test]
        public void MoneyAccumulatesAcrossAWholeRace()
        {
            // Bruiser's Ledger over a nine-sector race with four aggressive sectors.
            var state = new SectorPartState();
            var rules = Rules(Money(SectorStyle.Aggressive, 2f));
            var race = new[]
            {
                SectorStyle.Aggressive, SectorStyle.Clean, SectorStyle.Clean,
                SectorStyle.Aggressive, SectorStyle.Clean, SectorStyle.Aggressive,
                SectorStyle.Clean, SectorStyle.Clean, SectorStyle.Aggressive,
            };
            foreach (SectorStyle s in race) Drive(state, rules, s);
            Assert.AreEqual(8, state.MoneyEarned);
        }

        [Test]
        public void CountScaledMoneyPaysPerOccurrence()
        {
            // Tithe Collector: $1 per contact taken.
            var state = new SectorPartState();
            var rules = Rules(new SectorRule
            {
                Trigger = SectorTriggerKind.ContactTaken,
                Effect = SectorEffectKind.Money,
                Amount = 1f,
                ScaleByCount = true,
            });
            SectorPartState.Totals t = Drive(state, rules, SectorStyle.None, contactsTaken: 3);
            Assert.AreEqual(3, t.Money);
        }

        // ---------------------------------------------------------------- timed effects

        [Test]
        public void AOneSectorBonusLastsExactlyOneSector()
        {
            // The expiry-ordering test. Granted at the end of sector 1, it must be in force through
            // sector 2 and gone by sector 3 — off-by-one here would silently double every timed part.
            var state = new SectorPartState();
            var rules = Rules(Grip(SectorStyle.Aggressive, 0.08f, duration: 1));

            SectorPartState.Totals s1 = Drive(state, rules, SectorStyle.Aggressive);
            Assert.AreEqual(1.08f, s1.GripMult, 1e-4f, "granted at the end of sector 1");

            SectorPartState.Totals s2 = Drive(state, rules, SectorStyle.Clean);
            Assert.AreEqual(1f, s2.GripMult, 1e-4f, "expired once sector 2 closed");

            SectorPartState.Totals s3 = Drive(state, rules, SectorStyle.Clean);
            Assert.AreEqual(1f, s3.GripMult, 1e-4f);
        }

        [Test]
        public void ATwoSectorBonusSurvivesOneMoreSector()
        {
            var state = new SectorPartState();
            var rules = Rules(Grip(SectorStyle.Aggressive, 0.06f, duration: 2));

            Assert.AreEqual(1.06f, Drive(state, rules, SectorStyle.Aggressive).GripMult, 1e-4f);
            Assert.AreEqual(1.06f, Drive(state, rules, SectorStyle.Clean).GripMult, 1e-4f);
            Assert.AreEqual(1f, Drive(state, rules, SectorStyle.Clean).GripMult, 1e-4f);
        }

        [Test]
        public void APermanentBonusStacksForTheRestOfTheRace()
        {
            // Rear Guard: +3% grip per defensive sector, cumulative.
            var state = new SectorPartState();
            var rules = Rules(Grip(SectorStyle.Defensive, 0.03f));

            Assert.AreEqual(1.03f, Drive(state, rules, SectorStyle.Defensive).GripMult, 1e-4f);
            Assert.AreEqual(1.06f, Drive(state, rules, SectorStyle.Defensive).GripMult, 1e-4f);
            Assert.AreEqual(1.06f, Drive(state, rules, SectorStyle.Clean).GripMult, 1e-4f);
            Assert.AreEqual(1.09f, Drive(state, rules, SectorStyle.Defensive).GripMult, 1e-4f);
        }

        // ---------------------------------------------------------------- retrigger

        [Test]
        public void ARetriggerDoublesEveryOtherRule()
        {
            // Consistency Bonus + Bruiser's Ledger: a CLEAN aggressive sector should pay twice.
            var state = new SectorPartState();
            var rules = Rules(
                Money(SectorStyle.Aggressive, 2f),
                new SectorRule
                {
                    Trigger = SectorTriggerKind.Style,
                    StyleTag = SectorStyle.Clean,
                    Effect = SectorEffectKind.Retrigger,
                    Amount = 1f,
                });

            // Aggressive but NOT clean — no retrigger.
            Assert.AreEqual(2, Drive(state, rules, SectorStyle.Aggressive).Money);
            // A clean overtake is both, so the money rule fires twice.
            SectorPartState.Totals both = Drive(state, rules, SectorStyle.Aggressive | SectorStyle.Clean);
            Assert.AreEqual(4, both.Money);
            Assert.AreEqual(1, both.Retriggers);
        }

        [Test]
        public void RetriggerAppliesToRulesSlottedBeforeIt()
        {
            // Slot order must not decide whether a retrigger reaches a part — nothing in the UI tells
            // the player it would.
            var before = Rules(
                Money(SectorStyle.Clean, 3f),
                new SectorRule { Trigger = SectorTriggerKind.Style, StyleTag = SectorStyle.Clean, Effect = SectorEffectKind.Retrigger, Amount = 1f });
            var after = Rules(
                new SectorRule { Trigger = SectorTriggerKind.Style, StyleTag = SectorStyle.Clean, Effect = SectorEffectKind.Retrigger, Amount = 1f },
                Money(SectorStyle.Clean, 3f));

            Assert.AreEqual(6, Drive(new SectorPartState(), before, SectorStyle.Clean).Money);
            Assert.AreEqual(6, Drive(new SectorPartState(), after, SectorStyle.Clean).Money);
        }

        [Test]
        public void RetriggersAreBounded()
        {
            // Six stacked retrigger rules must not produce a 64x sector.
            var list = new List<SectorRule> { Money(SectorStyle.Clean, 1f) };
            for (int i = 0; i < 6; i++)
                list.Add(new SectorRule { Trigger = SectorTriggerKind.Style, StyleTag = SectorStyle.Clean, Effect = SectorEffectKind.Retrigger, Amount = 1f });

            SectorPartState.Totals t = Drive(new SectorPartState(), list, SectorStyle.Clean);
            Assert.LessOrEqual(t.Retriggers, 4);
            Assert.AreEqual(5, t.Money, "1 base + at most 4 repeats");
        }

        // ---------------------------------------------------------------- safety

        [Test]
        public void MultipliersAreClampedToASaneBand()
        {
            // A mis-authored asset must never hand the sim a multiplier that makes the car undriveable.
            var state = new SectorPartState();
            var rules = Rules(Grip(SectorStyle.Clean, 5f, op: SpecModOp.Multiply));
            for (int i = 0; i < 6; i++) Drive(state, rules, SectorStyle.Clean);
            Assert.AreEqual(SectorPartState.MaxBonusMult, state.GripMult, 1e-4f);
        }

        [Test]
        public void ANonPositiveMultiplyFactorIsRejected()
        {
            // x0 would zero the car's grip outright; a negative would invert it.
            var state = new SectorPartState();
            Assert.AreEqual(1f, Drive(state, Rules(Grip(SectorStyle.Clean, 0f, op: SpecModOp.Multiply)),
                SectorStyle.Clean).GripMult, 1e-4f);
            Assert.AreEqual(1f, Drive(state, Rules(Grip(SectorStyle.Clean, -2f, op: SpecModOp.Multiply)),
                SectorStyle.Clean).GripMult, 1e-4f);
        }

        [Test]
        public void ResetClearsEverythingForTheNextRace()
        {
            var state = new SectorPartState();
            var rules = Rules(Money(SectorStyle.Clean, 5f), Grip(SectorStyle.Clean, 0.1f));
            Drive(state, rules, SectorStyle.Clean);
            Assert.AreNotEqual(0, state.MoneyEarned);
            Assert.AreNotEqual(1f, state.GripMult);

            state.Reset();
            Assert.AreEqual(0, state.MoneyEarned);
            Assert.AreEqual(1f, state.GripMult);
            Assert.AreEqual(1f, state.PowerMult);
        }

        [Test]
        public void ResetClearsTheStreaksToo()
        {
            // A stale streak carried into the next race would pay a 3-streak part on its first sector.
            var state = new SectorPartState();
            var rules = Rules(new SectorRule
            {
                Trigger = SectorTriggerKind.StyleStreak,
                StyleTag = SectorStyle.Clean,
                StreakLength = 2,
                Effect = SectorEffectKind.Money,
                Amount = 10f,
            });

            Drive(state, rules, SectorStyle.Clean);   // streak 1, no pay
            state.Reset();
            SectorPartState.Totals first = Drive(state, rules, SectorStyle.Clean);
            Assert.AreEqual(0, first.Money, "a fresh race starts every streak at zero");
        }

        [Test]
        public void PreviousSectorTimeResetsSoPaceRulesDontCarryAcrossRaces()
        {
            var state = new SectorPartState();
            var rules = PaceRules();

            Drive(state, rules, SectorStyle.None, timeS: 20f, sectorIndex: 0);
            state.Reset();
            Assert.AreEqual(0, Drive(state, rules, SectorStyle.None, timeS: 20f, sectorIndex: 0).Money,
                "the first sector of a new race has nothing to be consistent with");
        }

        // ---------------------------------------------------------------- pace is per sector index

        static List<SectorRule> PaceRules() => Rules(new SectorRule
        {
            Trigger = SectorTriggerKind.ConsistentPace,
            PaceToleranceS = 0.25f,
            Effect = SectorEffectKind.Money,
            Amount = 2f,
        });

        [Test]
        public void ConsistencyComparesTheSameSectorAcrossLaps_NotAdjacentSectors()
        {
            // Sectors are equal by DISTANCE, so a corner-heavy third takes seconds longer than a fast
            // one. Comparing S1 against S2 would make any meaningful tolerance unreachable; a driver
            // means "this sector versus my last run through THIS sector".
            var state = new SectorPartState();
            var rules = PaceRules();

            // Lap 1 — nothing to compare against yet, whatever the spread between sectors.
            Assert.AreEqual(0, Drive(state, rules, SectorStyle.None, timeS: 20f, sectorIndex: 0).Money);
            Assert.AreEqual(0, Drive(state, rules, SectorStyle.None, timeS: 31f, sectorIndex: 1).Money);
            Assert.AreEqual(0, Drive(state, rules, SectorStyle.None, timeS: 24f, sectorIndex: 2).Money);

            // Lap 2 — each sector repeats its own lap-1 time closely, so all three pay, even though
            // consecutive sectors differ by many seconds.
            Assert.AreEqual(2, Drive(state, rules, SectorStyle.None, timeS: 20.1f, sectorIndex: 0).Money);
            Assert.AreEqual(2, Drive(state, rules, SectorStyle.None, timeS: 30.9f, sectorIndex: 1).Money);
            Assert.AreEqual(2, Drive(state, rules, SectorStyle.None, timeS: 24.2f, sectorIndex: 2).Money);
        }

        [Test]
        public void AnInconsistentLapOfTheSameSectorDoesNotPay()
        {
            var state = new SectorPartState();
            var rules = PaceRules();
            Drive(state, rules, SectorStyle.None, timeS: 20f, sectorIndex: 0);
            Assert.AreEqual(0, Drive(state, rules, SectorStyle.None, timeS: 21.5f, sectorIndex: 0).Money);
        }
    }
}
