using NUnit.Framework;
using Shitboxer.Race;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the data-driven race ruleset that RaceManager consults instead of hard-coded constants
    /// (the mechanism behind boss / event races). The load-bearing contract: the Standard ruleset
    /// reproduces RaceManager's shipped laps/cutoff exactly, so a race left on it behaves byte-for-byte
    /// as before; boss/event templates carry their flags; and [Flags] modifiers combine and read back
    /// correctly. A default (never-tuned) RaceManager is asserted to equal Standard, and applying a
    /// ruleset is proven to be the only thing that moves the finish condition off its shipped value.
    /// </summary>
    public class RaceRulesetTests : TestBase
    {
        // The shipped RaceManager serialized defaults these tests pin against.
        private const int ShippedLaps = 3;
        private const float ShippedCutoff = 0.15f;

        [Test]
        public void Standard_MatchesShippedRaceManagerDefaults()
        {
            RaceRuleset s = RaceRuleset.Standard;
            Assert.AreEqual(ShippedLaps, s.Laps, "Standard laps must equal RaceManager.totalLaps.");
            Assert.AreEqual(ShippedCutoff, s.CutoffFraction, "Standard cutoff must equal RaceManager.cutoffFraction.");
            Assert.IsFalse(s.IsBoss, "Standard is not a boss race.");
            Assert.AreEqual(RaceModifier.None, s.Modifiers, "Standard carries no special modifiers.");
        }

        [Test]
        public void Boss_CarriesItsBossFlagAndModifiers()
        {
            RaceRuleset b = RaceRuleset.Boss;
            Assert.IsTrue(b.IsBoss, "The boss template must flag itself as a boss.");
            Assert.IsTrue(b.Has(RaceModifier.DamageAmplified), "Boss amplifies damage.");
            Assert.IsTrue(b.Has(RaceModifier.NoRepairAfter), "Boss withholds the post-race repair.");
            Assert.IsFalse(b.Has(RaceModifier.DoublePayout), "Boss does not double the payout.");
            Assert.Greater(b.Laps, RaceRuleset.Standard.Laps, "The boss duel runs longer than a standard race.");
            Assert.Less(b.CutoffFraction, RaceRuleset.Standard.CutoffFraction, "The boss survival window is tighter.");
        }

        [Test]
        public void DoubleOrNothing_IsAnEventNotABoss()
        {
            RaceRuleset e = RaceRuleset.DoubleOrNothing;
            Assert.IsFalse(e.IsBoss, "The double-or-nothing event is not a boss race.");
            Assert.IsTrue(e.Has(RaceModifier.DoublePayout), "The event doubles the payout.");
            Assert.IsTrue(e.Has(RaceModifier.ReverseGrid), "The event reverses the grid.");
            // Standard length/cutoff — only the economy/grid twist differs.
            Assert.AreEqual(ShippedLaps, e.Laps);
            Assert.AreEqual(ShippedCutoff, e.CutoffFraction);
        }

        [Test]
        public void Modifiers_CombineAndReadBackAsFlags()
        {
            RaceModifier combo = RaceModifier.NoRepairAfter | RaceModifier.DamageAmplified;
            var r = new RaceRuleset { Modifiers = combo };

            // A subset (each individual bit, and both together) is present...
            Assert.IsTrue(r.Has(RaceModifier.NoRepairAfter));
            Assert.IsTrue(r.Has(RaceModifier.DamageAmplified));
            Assert.IsTrue(r.Has(combo));
            // ...bits that were never set are absent, including a superset that adds one.
            Assert.IsFalse(r.Has(RaceModifier.DoublePayout));
            Assert.IsFalse(r.Has(combo | RaceModifier.DoublePayout));
        }

        [Test]
        public void Modifier_None_IsZeroAndAlwaysMatches()
        {
            Assert.AreEqual(0, (int)RaceModifier.None, "None must be the zero flag so it never occupies a bit.");
            // Has(None) is a vacuous truth (& 0 == 0) — a race with no modifiers still 'has' None.
            Assert.IsTrue(new RaceRuleset { Modifiers = RaceModifier.None }.Has(RaceModifier.None));
            Assert.IsTrue(RaceRuleset.Boss.Has(RaceModifier.None));
        }

        [Test]
        public void ModifierBits_AreDistinctPowersOfTwo()
        {
            // Each named modifier must own a unique bit or [Flags] combination is meaningless.
            RaceModifier[] bits =
            {
                RaceModifier.NoRepairAfter,
                RaceModifier.DoublePayout,
                RaceModifier.ReverseGrid,
                RaceModifier.DamageAmplified,
            };
            int seen = 0;
            foreach (RaceModifier bit in bits)
            {
                int v = (int)bit;
                Assert.AreNotEqual(0, v, $"{bit} must occupy a bit.");
                Assert.AreEqual(0, v & (v - 1), $"{bit} must be a single power-of-two bit.");
                Assert.AreEqual(0, seen & v, $"{bit} overlaps an earlier modifier's bit.");
                seen |= v;
            }
        }

        [Test]
        public void WithBuilders_AreClampedNonMutatingCopies()
        {
            RaceRuleset s = RaceRuleset.Standard;

            RaceRuleset laps = s.WithLaps(7);
            Assert.AreEqual(7, laps.Laps);
            Assert.AreEqual(ShippedLaps, s.Laps, "WithLaps must not mutate the original.");
            Assert.AreEqual(1, s.WithLaps(0).Laps, "Lap count clamps to at least 1.");

            RaceRuleset cut = s.WithCutoff(0.4f);
            Assert.AreEqual(0.4f, cut.CutoffFraction);
            Assert.AreEqual(ShippedCutoff, s.CutoffFraction, "WithCutoff must not mutate the original.");
            Assert.AreEqual(0.01f, s.WithCutoff(0f).CutoffFraction, "Cutoff clamps up off zero.");
            Assert.AreEqual(1f, s.WithCutoff(5f).CutoffFraction, "Cutoff clamps down to one.");

            RaceRuleset mod = s.WithModifier(RaceModifier.DoublePayout).WithModifier(RaceModifier.ReverseGrid);
            Assert.IsTrue(mod.Has(RaceModifier.DoublePayout | RaceModifier.ReverseGrid), "Modifier bits accumulate.");
            Assert.AreEqual(RaceModifier.None, s.Modifiers, "WithModifier must not mutate the original.");
        }

        // --- RaceManager, at the ruleset seam -------------------------------------------------
        // RaceManager is a MonoBehaviour; in EditMode AddComponent runs field initializers but not
        // Start/Awake, so no TrackPath is needed to inspect its ruleset state. We assert only on the
        // additive ruleset surface (never Start()) and destroy the object afterwards.

        [Test]
        public void RaceManager_DefaultRuleset_MatchesStandardFinishCondition()
        {
            var go = new GameObject(nameof(RaceManager_DefaultRuleset_MatchesStandardFinishCondition));
            try
            {
                var mgr = go.AddComponent<RaceManager>();
                // A freshly built manager (no SetRuleset call) is the standard ruleset: same finish
                // condition (laps) and cutoff as shipped, not a boss, no modifiers.
                Assert.AreEqual(RaceRuleset.Standard.Laps, mgr.TotalLaps);
                Assert.AreEqual(RaceRuleset.Standard.CutoffFraction, mgr.CutoffFraction);
                Assert.IsFalse(mgr.IsBossRace);
                Assert.AreEqual(RaceModifier.None, mgr.Modifiers);

                RaceRuleset live = mgr.Ruleset;
                Assert.AreEqual(RaceRuleset.Standard.Laps, live.Laps);
                Assert.AreEqual(RaceRuleset.Standard.CutoffFraction, live.CutoffFraction);
                Assert.IsFalse(live.IsBoss);
                Assert.AreEqual(RaceModifier.None, live.Modifiers);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RaceManager_SetStandard_IsANoOp()
        {
            var go = new GameObject(nameof(RaceManager_SetStandard_IsANoOp));
            try
            {
                var mgr = go.AddComponent<RaceManager>();
                int lapsBefore = mgr.TotalLaps;
                float cutBefore = mgr.CutoffFraction;

                mgr.SetRuleset(RaceRuleset.Standard);

                Assert.AreEqual(lapsBefore, mgr.TotalLaps, "Standard ruleset must not change the lap count.");
                Assert.AreEqual(cutBefore, mgr.CutoffFraction, "Standard ruleset must not change the cutoff.");
                Assert.IsFalse(mgr.IsBossRace);
                Assert.AreEqual(RaceModifier.None, mgr.Modifiers);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RaceManager_SetBoss_DrivesLapsCutoffAndFlags()
        {
            var go = new GameObject(nameof(RaceManager_SetBoss_DrivesLapsCutoffAndFlags));
            try
            {
                var mgr = go.AddComponent<RaceManager>();
                RaceRuleset boss = RaceRuleset.Boss;

                mgr.SetRuleset(boss);

                Assert.AreEqual(boss.Laps, mgr.TotalLaps, "Boss ruleset must drive the finish lap count.");
                Assert.AreEqual(boss.CutoffFraction, mgr.CutoffFraction, "Boss ruleset must drive the cutoff.");
                Assert.IsTrue(mgr.IsBossRace);
                Assert.IsTrue(mgr.HasModifier(RaceModifier.DamageAmplified));
                Assert.IsTrue(mgr.HasModifier(RaceModifier.NoRepairAfter));
                Assert.IsFalse(mgr.HasModifier(RaceModifier.DoublePayout));

                // The reconstructed ruleset round-trips the applied one.
                RaceRuleset live = mgr.Ruleset;
                Assert.AreEqual(boss.Laps, live.Laps);
                Assert.AreEqual(boss.CutoffFraction, live.CutoffFraction);
                Assert.IsTrue(live.IsBoss);
                Assert.AreEqual(boss.Modifiers, live.Modifiers);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RaceManager_SetRuleset_ClampsOutOfRangeValues()
        {
            var go = new GameObject(nameof(RaceManager_SetRuleset_ClampsOutOfRangeValues));
            try
            {
                var mgr = go.AddComponent<RaceManager>();
                // A hand-built ruleset with illegal values is clamped exactly like the existing setters.
                mgr.SetRuleset(new RaceRuleset { Laps = 0, CutoffFraction = 5f, IsBoss = true });
                Assert.AreEqual(1, mgr.TotalLaps, "Laps clamp to at least 1 (never a zero-lap finish).");
                Assert.AreEqual(1f, mgr.CutoffFraction, "Cutoff clamps into the sane band.");

                mgr.SetRuleset(new RaceRuleset { Laps = 3, CutoffFraction = 0f });
                Assert.AreEqual(0.01f, mgr.CutoffFraction, "Cutoff can never be zero (instant elimination).");
                Assert.IsFalse(mgr.IsBossRace, "Boss flag follows the last applied ruleset.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
