using NUnit.Framework;
using Shitboxer.Meta;
using Shitboxer.Race;

namespace Shitboxer.Tests
{
    /// <summary>
    /// The per-circuit boss rotation (doc 08 slice 12): an 8-circuit season meets four distinct
    /// bosses twice each instead of one gate eight times. Pure data — no scene needed.
    /// </summary>
    public class BossRotationTests : TestBase
    {
        [Test]
        public void EveryBossInTheRotation_IsABoss_AndWithholdsTheRepair()
        {
            for (int circuit = 0; circuit < 4; circuit++)
            {
                RaceRuleset boss = RaceRuleset.BossForCircuit(circuit);
                Assert.IsTrue(boss.IsBoss, $"circuit {circuit}'s boss must flag itself as a boss");
                Assert.IsTrue(boss.Has(RaceModifier.NoRepairAfter),
                    $"circuit {circuit}: boss damage riding into the garage is the shared boss identity");
                Assert.IsFalse(string.IsNullOrEmpty(boss.Title),
                    $"circuit {circuit}'s boss needs a name — the HUD and summaries announce it");
                Assert.IsFalse(boss.Has(RaceModifier.ReverseGrid),
                    $"circuit {circuit}: ReverseGrid is declared but NOT wired — it must not sit on a live boss");
            }
        }

        [Test]
        public void AdjacentCircuits_MeetDifferentBosses_AndTheRotationWraps()
        {
            for (int circuit = 0; circuit < 7; circuit++)
                Assert.AreNotEqual(RaceRuleset.BossForCircuit(circuit).Title,
                    RaceRuleset.BossForCircuit(circuit + 1).Title,
                    "back-to-back circuits must never repeat a boss");

            Assert.AreEqual(RaceRuleset.BossForCircuit(0).Title, RaceRuleset.BossForCircuit(4).Title,
                "the 8-circuit season sees the 4-boss rotation twice");
            Assert.AreEqual(RaceRuleset.BossForCircuit(0).Title, RaceRuleset.BossForCircuit(-4).Title,
                "a junk negative index wraps instead of throwing");
        }

        [Test]
        public void TheRotation_CoversTheThreeDistinctLevers()
        {
            // One boss per wired lever beyond the Enforcer: dead air, the deploy tax (with the
            // double-payout carrot), and sheer length. Pinned by title so a retune that deletes a
            // lever has to say so here.
            Assert.IsTrue(RaceRuleset.BossForCircuit(1).Has(RaceModifier.DirtyAir), "DIRTY AIR kills the slipstream");
            Assert.IsTrue(RaceRuleset.BossForCircuit(2).Has(RaceModifier.ActiveTaxed), "THE TAXMAN bills the button");
            Assert.IsTrue(RaceRuleset.BossForCircuit(2).Has(RaceModifier.DoublePayout), "and pays double for a clean finish");
            Assert.Greater(RaceRuleset.BossForCircuit(3).Laps, RaceRuleset.BossForCircuit(0).Laps,
                "THE LONG HAUL is the endurance exam");
        }

        [Test]
        public void LegacyBossTemplate_IsStillTheEnforcer()
        {
            // RaceRuleset.Boss (the shape older tests and callers pin) is circuit 0's boss.
            RaceRuleset boss = RaceRuleset.Boss;
            Assert.IsTrue(boss.Has(RaceModifier.DamageAmplified));
            Assert.IsTrue(boss.Has(RaceModifier.NoRepairAfter));
            Assert.AreEqual(5, boss.Laps);
            Assert.AreEqual(RaceRuleset.BossForCircuit(0).Title, boss.Title);
        }

        [Test]
        public void RulesetForRace_HandsEachCircuitItsOwnBoss_AndStandardOtherwise()
        {
            Assert.AreEqual(RaceRuleset.BossForCircuit(2).Title,
                RunDirector.RulesetForRace(true, true, 2).Title);
            Assert.IsFalse(RunDirector.RulesetForRace(true, false, 2).IsBoss,
                "a non-boss race stays Standard whatever the circuit");
            Assert.IsFalse(RunDirector.RulesetForRace(false, true, 2).IsBoss,
                "boss rulesets disabled = Standard even on the circuit finale");
            Assert.AreEqual(RaceRuleset.BossForCircuit(0).Title,
                RunDirector.RulesetForRace(true, true).Title,
                "the circuit-agnostic overload is circuit 0 — the Enforcer — for legacy callers");
        }
    }
}
