using NUnit.Framework;
using Shitboxer.Race;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the boss-only <see cref="RaceModifier.DamageAmplified"/> modifier as combat consumes it
    /// (wave 12). The load-bearing contract: with the modifier ABSENT — every normal race, and every race
    /// while bosses are disabled — the damage factor is exactly 1, so the amount VehicleCombat applies is
    /// byte-for-byte the shipped value; only a race whose ruleset carries the flag scales it, and only by
    /// the bounded factor. Both seams are pure helpers on <see cref="VehicleCombat"/> so they test without a
    /// scene: <see cref="VehicleCombat.AmplifiedDamage"/> (the arithmetic) and
    /// <see cref="VehicleCombat.IsDamageAmplified"/> (the null-guarded ruleset read). A null/absent race is
    /// proven to read as not-amplified (no NRE), leaving damage unchanged.
    /// </summary>
    public class DamageAmplifiedTests : TestBase
    {
        // Representative base amounts: zero (no-op site), a typical sap, a full sap, and a value the
        // amplified factor would push past 1 (the sim clamps that downstream — not this helper's concern).
        private static readonly float[] Bases = { 0f, 0.05f, 0.3f, 0.8f, 2.5f };

        // --- AmplifiedDamage: the pure arithmetic split -----------------------------------------------

        [Test]
        public void AmplifiedDamage_NotAmplified_ReturnsBaseUnchanged()
        {
            // The whole point of the no-op guarantee: not amplified => identity, byte-for-byte, for every
            // base amount (including a factor that would otherwise scale it). This is what keeps a normal
            // race exactly as it shipped.
            foreach (float b in Bases)
            {
                Assert.That(VehicleCombat.AmplifiedDamage(b, false, VehicleCombat.DefaultDamageAmplifiedFactor),
                    Is.EqualTo(b), "Not amplified must return the base amount unchanged.");
                // The factor value is irrelevant when not amplified — never touched.
                Assert.That(VehicleCombat.AmplifiedDamage(b, false, 3f), Is.EqualTo(b));
                Assert.That(VehicleCombat.AmplifiedDamage(b, false, 1f), Is.EqualTo(b));
            }
        }

        [Test]
        public void AmplifiedDamage_Amplified_ScalesByExactlyTheFactor()
        {
            float[] factors = { 1f, VehicleCombat.DefaultDamageAmplifiedFactor, 2f, 3f };
            foreach (float f in factors)
                foreach (float b in Bases)
                    Assert.That(VehicleCombat.AmplifiedDamage(b, true, f), Is.EqualTo(b * f),
                        $"Amplified must scale the base by exactly the factor (base {b}, factor {f}).");
        }

        [Test]
        public void AmplifiedDamage_ShippedFactor_ExactWorkedExample()
        {
            // A concrete pin on the shipped 1.5x so a change to the default is caught here.
            Assert.That(VehicleCombat.DefaultDamageAmplifiedFactor, Is.EqualTo(1.5f));
            Assert.That(VehicleCombat.AmplifiedDamage(0.2f, true, VehicleCombat.DefaultDamageAmplifiedFactor),
                Is.EqualTo(0.3f).Within(1e-6f), "0.2 amplified at 1.5x is 0.3.");
        }

        [Test]
        public void DefaultFactor_IsBoundedAndActuallyAmplifies()
        {
            // Bounded so the modifier can never runaway-scale; strictly above 1 so it is a real amplify,
            // not a silent no-op. (The serialized field mirrors these bounds via its Range attribute.)
            Assert.Greater(VehicleCombat.DefaultDamageAmplifiedFactor, 1f, "The factor must actually amplify.");
            Assert.LessOrEqual(VehicleCombat.DefaultDamageAmplifiedFactor, 3f, "The factor must stay bounded.");
        }

        // --- IsDamageAmplified: the null-guarded ruleset read -----------------------------------------

        [Test]
        public void IsDamageAmplified_NullRace_IsFalseAndLeavesDamageUnchanged()
        {
            // A car outside a race / no scene manager: the guard must read false with no NullReference,
            // and folding that through AmplifiedDamage must leave the damage identical to the base.
            Assert.IsFalse(VehicleCombat.IsDamageAmplified(null), "A null race is never amplified.");
            foreach (float b in Bases)
                Assert.That(
                    VehicleCombat.AmplifiedDamage(b, VehicleCombat.IsDamageAmplified(null),
                        VehicleCombat.DefaultDamageAmplifiedFactor),
                    Is.EqualTo(b), "A null/absent ruleset must leave damage unchanged.");
        }

        [Test]
        public void IsDamageAmplified_DefaultRaceManager_IsFalse()
        {
            // A freshly built manager (no SetRuleset) carries RaceModifier.None — the standard race — so
            // combat is not amplified. RaceManager is a MonoBehaviour; AddComponent runs field initializers
            // but not Start/Awake, matching how RaceRulesetTests inspects it.
            var go = new GameObject(nameof(IsDamageAmplified_DefaultRaceManager_IsFalse));
            try
            {
                var mgr = go.AddComponent<RaceManager>();
                Assert.IsFalse(VehicleCombat.IsDamageAmplified(mgr),
                    "A standard (no-modifier) race must not amplify combat damage.");
                foreach (float b in Bases)
                    Assert.That(
                        VehicleCombat.AmplifiedDamage(b, VehicleCombat.IsDamageAmplified(mgr),
                            VehicleCombat.DefaultDamageAmplifiedFactor),
                        Is.EqualTo(b), "The default race leaves combat damage byte-for-byte unchanged.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void IsDamageAmplified_NonBossEvent_IsFalse()
        {
            // The double-or-nothing event carries DoublePayout/ReverseGrid but NOT DamageAmplified, so its
            // combat is unamplified — the gate is the specific flag, not "any modifier".
            var go = new GameObject(nameof(IsDamageAmplified_NonBossEvent_IsFalse));
            try
            {
                var mgr = go.AddComponent<RaceManager>();
                mgr.SetRuleset(RaceRuleset.DoubleOrNothing);
                Assert.IsFalse(VehicleCombat.IsDamageAmplified(mgr),
                    "A race without the DamageAmplified flag must not amplify, even with other modifiers.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void IsDamageAmplified_BossRuleset_IsTrueAndScalesDamage()
        {
            // The boss template carries DamageAmplified: the gate reads true and folding it through
            // AmplifiedDamage scales every base amount by exactly the shipped factor.
            var go = new GameObject(nameof(IsDamageAmplified_BossRuleset_IsTrueAndScalesDamage));
            try
            {
                var mgr = go.AddComponent<RaceManager>();
                mgr.SetRuleset(RaceRuleset.Boss);
                Assert.IsTrue(VehicleCombat.IsDamageAmplified(mgr), "The boss ruleset amplifies combat damage.");

                bool amp = VehicleCombat.IsDamageAmplified(mgr);
                foreach (float b in Bases)
                    Assert.That(
                        VehicleCombat.AmplifiedDamage(b, amp, VehicleCombat.DefaultDamageAmplifiedFactor),
                        Is.EqualTo(b * VehicleCombat.DefaultDamageAmplifiedFactor),
                        "Under the boss ruleset combat damage scales by exactly the factor.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void IsDamageAmplified_FlagIsTheGate_NotTheBossBool()
        {
            // A hand-built ruleset that sets the flag WITHOUT IsBoss still amplifies: the modifier bit is
            // the gate combat consumes (item 1), and only the boss template happens to set that bit.
            var go = new GameObject(nameof(IsDamageAmplified_FlagIsTheGate_NotTheBossBool));
            try
            {
                var mgr = go.AddComponent<RaceManager>();
                mgr.SetRuleset(new RaceRuleset { Laps = 3, CutoffFraction = 0.15f, IsBoss = false,
                    Modifiers = RaceModifier.DamageAmplified });
                Assert.IsFalse(mgr.IsBossRace, "This ruleset is not flagged as a boss...");
                Assert.IsTrue(VehicleCombat.IsDamageAmplified(mgr), "...but carrying the flag still amplifies.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
