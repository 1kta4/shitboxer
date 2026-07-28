using NUnit.Framework;
using Shitboxer.Meta;
using Shitboxer.Race;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the pure trigger evaluation behind sector-scoring parts — whether a rule fires and how
    /// many times. Every part in the eventual collection routes through this one function, so its edge
    /// cases (a streak that must not re-fire, a count-scaled trigger, a pace rule with no previous
    /// sector to compare against) are worth pinning individually.
    /// </summary>
    public class SectorRuleTests : TestBase
    {
        static SectorContext Ctx(SectorStyle style = SectorStyle.None,
            SectorColour colour = SectorColour.None, float timeS = 20f, float previousTimeS = -1f,
            int contactsTaken = 0, int positionsGained = 0, bool finalSector = false,
            StyleStreaks streaks = default) =>
            new SectorContext(style, colour, timeS, previousTimeS, contactsTaken, positionsGained,
                finalSector, streaks);

        // ---------------------------------------------------------------- style

        [Test]
        public void StyleTrigger_FiresOnlyWhenTheTagIsPresent()
        {
            var rule = new SectorRule
            {
                Trigger = SectorTriggerKind.Style,
                StyleTag = SectorStyle.Aggressive,
                Effect = SectorEffectKind.Money,
                Amount = 2f,
            };
            Assert.AreEqual(1, SectorRuleMath.FireCount(rule, Ctx(SectorStyle.Aggressive)));
            Assert.AreEqual(0, SectorRuleMath.FireCount(rule, Ctx(SectorStyle.Clean)));
        }

        [Test]
        public void StyleTrigger_FiresOnAMultiTagSectorThatIncludesIt()
        {
            // Sectors are [Flags]: a forced move that went wrong is both Aggressive and Ragged, and a
            // part watching either tag must still pay.
            var rule = new SectorRule { Trigger = SectorTriggerKind.Style, StyleTag = SectorStyle.Ragged };
            Assert.AreEqual(1, SectorRuleMath.FireCount(rule,
                Ctx(SectorStyle.Aggressive | SectorStyle.Ragged)));
        }

        [Test]
        public void UnconfiguredRule_NeverFires()
        {
            // The default trigger is None, so a part with an empty/half-authored rule is inert rather
            // than firing on everything.
            Assert.AreEqual(0, SectorRuleMath.FireCount(default, Ctx(SectorStyle.Clean)));
        }

        // ---------------------------------------------------------------- streaks

        [Test]
        public void StreakTrigger_FiresOnReachingTheLength_AndNotAgainAfter()
        {
            // THE guard against a permanent multiplier compounding every sector of a clean race.
            var rule = new SectorRule
            {
                Trigger = SectorTriggerKind.StyleStreak,
                StyleTag = SectorStyle.Clean,
                StreakLength = 3,
            };

            var streaks = new StyleStreaks();
            int fires = 0;
            for (int i = 0; i < 6; i++)
            {
                streaks.Observe(SectorStyle.Clean);
                fires += SectorRuleMath.FireCount(rule, Ctx(SectorStyle.Clean, streaks: streaks));
            }
            Assert.AreEqual(1, fires, "a six-sector clean run should pay a 3-streak exactly once");
        }

        [Test]
        public void StreakTrigger_CanBeEarnedAgainAfterBreaking()
        {
            var rule = new SectorRule
            {
                Trigger = SectorTriggerKind.StyleStreak,
                StyleTag = SectorStyle.Clean,
                StreakLength = 2,
            };

            var streaks = new StyleStreaks();
            var sequence = new[]
            {
                SectorStyle.Clean, SectorStyle.Clean,   // reaches 2 -> fires
                SectorStyle.Ragged,                      // breaks
                SectorStyle.Clean, SectorStyle.Clean,   // reaches 2 again -> fires
            };
            int fires = 0;
            foreach (SectorStyle s in sequence)
            {
                streaks.Observe(s);
                fires += SectorRuleMath.FireCount(rule, Ctx(s, streaks: streaks));
            }
            Assert.AreEqual(2, fires);
        }

        [Test]
        public void Streaks_AreTrackedPerTag_NotForTheWholeStyleSet()
        {
            // A sector that is both Aggressive and Ragged extends BOTH those streaks while ending the
            // Clean one. Tracking a single "did the style set repeat" counter would get this wrong.
            var streaks = new StyleStreaks();
            streaks.Observe(SectorStyle.Clean);
            streaks.Observe(SectorStyle.Aggressive | SectorStyle.Ragged);
            streaks.Observe(SectorStyle.Aggressive);

            Assert.AreEqual(0, streaks.For(SectorStyle.Clean), "clean should have been broken");
            Assert.AreEqual(2, streaks.For(SectorStyle.Aggressive));
            Assert.AreEqual(0, streaks.For(SectorStyle.Ragged), "ragged ran for one sector then stopped");
        }

        [Test]
        public void StreakTrigger_WithNonPositiveLength_NeverFires()
        {
            var rule = new SectorRule
            {
                Trigger = SectorTriggerKind.StyleStreak,
                StyleTag = SectorStyle.Clean,
                StreakLength = 0,
            };
            var streaks = new StyleStreaks();
            streaks.Observe(SectorStyle.Clean);
            Assert.AreEqual(0, SectorRuleMath.FireCount(rule, Ctx(SectorStyle.Clean, streaks: streaks)));
        }

        // ---------------------------------------------------------------- colour / final / counts

        [Test]
        public void ColourTrigger_MatchesExactly()
        {
            var rule = new SectorRule { Trigger = SectorTriggerKind.Colour, TimingColour = SectorColour.Purple };
            Assert.AreEqual(1, SectorRuleMath.FireCount(rule, Ctx(colour: SectorColour.Purple)));
            Assert.AreEqual(0, SectorRuleMath.FireCount(rule, Ctx(colour: SectorColour.Green)));
            Assert.AreEqual(0, SectorRuleMath.FireCount(rule, Ctx(colour: SectorColour.None)));
        }

        [Test]
        public void FinalSectorTrigger_OnlyOnTheLastSectorOfTheRace()
        {
            var rule = new SectorRule { Trigger = SectorTriggerKind.FinalSector };
            Assert.AreEqual(1, SectorRuleMath.FireCount(rule, Ctx(finalSector: true)));
            Assert.AreEqual(0, SectorRuleMath.FireCount(rule, Ctx(finalSector: false)));
        }

        [Test]
        public void CountScaledTrigger_FiresOncePerOccurrence()
        {
            var scaled = new SectorRule { Trigger = SectorTriggerKind.ContactTaken, ScaleByCount = true };
            var flat = new SectorRule { Trigger = SectorTriggerKind.ContactTaken, ScaleByCount = false };

            Assert.AreEqual(3, SectorRuleMath.FireCount(scaled, Ctx(contactsTaken: 3)));
            Assert.AreEqual(1, SectorRuleMath.FireCount(flat, Ctx(contactsTaken: 3)));
            Assert.AreEqual(0, SectorRuleMath.FireCount(scaled, Ctx(contactsTaken: 0)));
            Assert.AreEqual(0, SectorRuleMath.FireCount(flat, Ctx(contactsTaken: 0)));
        }

        // ---------------------------------------------------------------- pace

        [Test]
        public void ConsistentPace_NeedsAPreviousSector()
        {
            // The race's first sector has nothing to be consistent WITH.
            var rule = new SectorRule { Trigger = SectorTriggerKind.ConsistentPace, PaceToleranceS = 0.25f };
            Assert.AreEqual(0, SectorRuleMath.FireCount(rule, Ctx(timeS: 20f, previousTimeS: -1f)));
        }

        [Test]
        public void ConsistentPace_FiresInsideTheToleranceEitherDirection()
        {
            var rule = new SectorRule { Trigger = SectorTriggerKind.ConsistentPace, PaceToleranceS = 0.25f };
            Assert.AreEqual(1, SectorRuleMath.FireCount(rule, Ctx(timeS: 20.2f, previousTimeS: 20.0f)));
            Assert.AreEqual(1, SectorRuleMath.FireCount(rule, Ctx(timeS: 19.8f, previousTimeS: 20.0f)));
            Assert.AreEqual(1, SectorRuleMath.FireCount(rule, Ctx(timeS: 20.0f, previousTimeS: 20.0f)));
        }

        [Test]
        public void ConsistentPace_DoesNotFireOutsideTheTolerance()
        {
            var rule = new SectorRule { Trigger = SectorTriggerKind.ConsistentPace, PaceToleranceS = 0.25f };
            Assert.AreEqual(0, SectorRuleMath.FireCount(rule, Ctx(timeS: 20.5f, previousTimeS: 20.0f)));
        }
    }
}
