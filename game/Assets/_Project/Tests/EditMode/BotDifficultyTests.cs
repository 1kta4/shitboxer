using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Race;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the adaptive-bot difficulty model: a bounded rubber-band (a bot AHEAD of the field eases off,
    /// one BEHIND pushes) folded with a rookie->pro skill tier that rises with the run's license stake. The
    /// contract mirrors the project's headline constraint: the DEFAULT (nominal skill, stake 0, rubber-band
    /// off) is a true no-op — every modifier is exactly 1 — and no gap/skill/stake/strength, however extreme,
    /// can carry a modifier outside its subtle clamped band. Two Step-level tests pin that the wiring into
    /// BotBrain preserves today's behaviour at nominal and stays bounded when a host turns it on.
    /// </summary>
    public class BotDifficultyTests : TestBase
    {
        // Documented output clamps (mirror BotDifficulty's private consts).
        private const float SpeedMin = 0.90f, SpeedMax = 1.10f;
        private const float ThrottleMin = 0.85f, ThrottleMax = 1.12f;
        private const float SteerMin = 0.85f, SteerMax = 1.20f;

        private static void AssertWithinClamps(BotModifiers m, string label)
        {
            Assert.That(m.TargetSpeedScale, Is.InRange(SpeedMin, SpeedMax), label + " target-speed scale");
            Assert.That(m.ThrottleScale, Is.InRange(ThrottleMin, ThrottleMax), label + " throttle scale");
            Assert.That(m.SteerSharpness, Is.InRange(SteerMin, SteerMax), label + " steer sharpness");
        }

        private static void AssertIdentity(BotModifiers m, string label)
        {
            Assert.That(m.TargetSpeedScale, Is.EqualTo(1f), label + " target-speed scale == 1");
            Assert.That(m.ThrottleScale, Is.EqualTo(1f), label + " throttle scale == 1");
            Assert.That(m.SteerSharpness, Is.EqualTo(1f), label + " steer sharpness == 1");
        }

        // ---- No-op defaults: the baseline must be reproduced exactly ----------------------------------------

        [Test]
        public void Nominal_IsExactIdentity_ForAnyGap()
        {
            // Rubber-band off + nominal skill + stake 0 => identity regardless of where the bot sits.
            foreach (float gap in new[] { -1000f, -45f, -1f, 0f, 1f, 45f, 1000f })
                AssertIdentity(BotDifficulty.Nominal.Evaluate(gap), $"nominal @ gap {gap}");
        }

        [Test]
        public void DefaultStruct_IsExactIdentity()
        {
            // default(BotDifficulty) must equal Nominal so a partially-initialised host struct can't silently
            // hand a bot a rookie handicap.
            foreach (float gap in new[] { -100f, 0f, 100f })
                AssertIdentity(default(BotDifficulty).Evaluate(gap), $"default @ gap {gap}");
        }

        [Test]
        public void FromTier_NominalSkill_IsExactIdentity()
        {
            AssertIdentity(BotDifficulty.FromTier(BotDifficulty.NominalSkill01).Evaluate(0f), "FromTier(nominal)");
        }

        [Test]
        public void ZeroGap_ContributesNoRubberBand()
        {
            // Even with the rubber-band cranked, a bot level with the field (gap 0) and nominal skill is identity.
            var d = new BotDifficulty { RubberBandStrength = 0.5f };
            AssertIdentity(d.Evaluate(0f), "gap 0, strong rubber-band, nominal skill");
        }

        // ---- Rubber-band direction (nominal skill, so only the gap moves the modifiers) ---------------------

        [Test]
        public void BotAhead_EasesOff()
        {
            var d = new BotDifficulty { RubberBandStrength = 0.1f }; // nominal skill
            BotModifiers m = d.Evaluate(+30f); // ahead of the field
            Assert.That(m.TargetSpeedScale, Is.LessThan(1f), "leader eases target speed");
            Assert.That(m.ThrottleScale, Is.LessThan(1f), "leader eases throttle");
            Assert.That(m.SteerSharpness, Is.EqualTo(1f), "rubber-band never touches steering");
            AssertWithinClamps(m, "ahead");
        }

        [Test]
        public void BotBehind_Pushes()
        {
            var d = new BotDifficulty { RubberBandStrength = 0.1f }; // nominal skill
            BotModifiers m = d.Evaluate(-30f); // behind the field
            Assert.That(m.TargetSpeedScale, Is.GreaterThan(1f), "trailer pushes target speed");
            Assert.That(m.ThrottleScale, Is.GreaterThan(1f), "trailer pushes throttle");
            Assert.That(m.SteerSharpness, Is.EqualTo(1f), "rubber-band never touches steering");
            AssertWithinClamps(m, "behind");
        }

        [Test]
        public void RubberBand_IsSymmetricAndMonotonicInGap()
        {
            var d = new BotDifficulty { RubberBandStrength = 0.1f };
            // Further ahead => progressively more ease; further behind => progressively more push (until saturation).
            Assert.That(d.Evaluate(40f).TargetSpeedScale, Is.LessThan(d.Evaluate(10f).TargetSpeedScale), "more ease further ahead");
            Assert.That(d.Evaluate(-40f).TargetSpeedScale, Is.GreaterThan(d.Evaluate(-10f).TargetSpeedScale), "more push further behind");
            // Symmetric about 0.
            Assert.That(d.Evaluate(20f).TargetSpeedScale + d.Evaluate(-20f).TargetSpeedScale,
                Is.EqualTo(2f).Within(1e-5f), "ease and push are symmetric about neutral");
        }

        // ---- Boundedness: no input can escape the clamps ----------------------------------------------------

        [Test]
        public void Modifiers_AlwaysWithinClamps_UnderExtremeInputs()
        {
            float[] strengths = { 0f, 0.1f, 0.3f, 0.5f, 2f, 100f };
            float[] biases = { -2f, -0.5f, 0f, 0.5f, 2f };
            int[] stakes = { -100, 0, 3, 50, 1000 };
            float[] gaps = { -1e9f, -100f, -45f, -1f, 0f, 1f, 45f, 100f, 1e9f };

            foreach (float st in strengths)
                foreach (float b in biases)
                    foreach (int stk in stakes)
                        foreach (float g in gaps)
                        {
                            var d = new BotDifficulty { RubberBandStrength = st, SkillBias01 = b, StakeLevel = stk };
                            AssertWithinClamps(d.Evaluate(g), $"str{st} bias{b} stake{stk} gap{g}");
                        }
        }

        // ---- Skill tier: competence rises with stake, and higher competence carries more --------------------

        [Test]
        public void HigherStake_RaisesCompetence()
        {
            float prev = BotDifficulty.FromTier(0.5f, 0).Competence01;
            for (int stake = 1; stake <= 5; stake++)
            {
                float c = BotDifficulty.FromTier(0.5f, stake).Competence01;
                Assert.That(c, Is.GreaterThan(prev), $"stake {stake} more competent than {stake - 1}");
                Assert.That(c, Is.LessThanOrEqualTo(1f), $"competence stays <= 1 @ stake {stake}");
                prev = c;
            }
        }

        [Test]
        public void NegativeStake_DoesNotReduceCompetence()
        {
            Assert.That(BotDifficulty.FromTier(0.5f, -5).Competence01,
                Is.EqualTo(BotDifficulty.FromTier(0.5f, 0).Competence01), "negative stake treated as 0");
        }

        [Test]
        public void FromTier_MapsRookieToProCompetence()
        {
            float rookie = BotDifficulty.FromTier(0f).Competence01;
            float nominal = BotDifficulty.FromTier(0.5f).Competence01;
            float pro = BotDifficulty.FromTier(1f).Competence01;
            Assert.That(rookie, Is.LessThan(nominal), "rookie below nominal");
            Assert.That(nominal, Is.LessThan(pro), "pro above nominal");
            Assert.That(nominal, Is.EqualTo(BotDifficulty.NominalSkill01), "nominal base skill == nominal competence");
            // Out-of-range base skill is clamped, not extrapolated.
            Assert.That(BotDifficulty.FromTier(5f).Competence01, Is.EqualTo(pro), "base skill clamps to pro");
            Assert.That(BotDifficulty.FromTier(-5f).Competence01, Is.EqualTo(rookie), "base skill clamps to rookie");
        }

        [Test]
        public void Pro_CarriesMoreThanRookie_WithinClamps()
        {
            BotModifiers pro = BotDifficulty.FromTier(1f).Evaluate(0f);
            BotModifiers rookie = BotDifficulty.FromTier(0f).Evaluate(0f);
            Assert.That(pro.TargetSpeedScale, Is.GreaterThan(1f), "pro carries more speed");
            Assert.That(rookie.TargetSpeedScale, Is.LessThan(1f), "rookie carries less speed");
            Assert.That(pro.ThrottleScale, Is.GreaterThan(rookie.ThrottleScale), "pro commits more throttle");
            Assert.That(pro.SteerSharpness, Is.GreaterThan(rookie.SteerSharpness), "pro steers sharper");
            AssertWithinClamps(pro, "pro");
            AssertWithinClamps(rookie, "rookie");
        }

        // ---- Step-level wiring: nominal preserves the baseline; a live config stays bounded -----------------

        // A 100 m square in XZ (matches BotBrainTests): a real loop to Step a bot through.
        private static RacingLine Square()
        {
            var pts = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(100f, 0f, 0f),
                new Vector3(100f, 0f, 100f),
                new Vector3(0f, 0f, 100f),
            };
            return new RacingLine(pts);
        }

        // Long thin rectangle: at progress 60 the bot is on a straight far from any corner, so the free speed
        // plan isn't corner-limited and throttle sits strictly inside (0,1) — lets a scale visibly move it.
        private static RacingLine LongStraight()
        {
            var pts = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(400f, 0f, 0f),
                new Vector3(400f, 0f, 12f),
                new Vector3(0f, 0f, 12f),
            };
            return new RacingLine(pts);
        }

        private static BotSensors ForwardOnLine(RacingLine line, float progress, float speed)
        {
            Vector3 fwd = line.DirectionAt(progress);
            return new BotSensors
            {
                Position = line.PointAt(progress),
                Forward = fwd,
                Velocity = fwd * speed,
                DrivenWheelSlip = 0f,
                Neighbors = null,
                NeighborCount = 0,
            };
        }

        private static void AssertInputEqual(VehicleInput expected, VehicleInput actual, string label)
        {
            Assert.That(actual.Steer, Is.EqualTo(expected.Steer), label + " steer");
            Assert.That(actual.Throttle, Is.EqualTo(expected.Throttle), label + " throttle");
            Assert.That(actual.Brake, Is.EqualTo(expected.Brake), label + " brake");
            Assert.That(actual.Handbrake, Is.EqualTo(expected.Handbrake), label + " handbrake");
        }

        [Test]
        public void Step_NominalDifficulty_MatchesBaseline()
        {
            RacingLine line = Square();
            BotSensors s = ForwardOnLine(line, 20f, 30f);

            // Baseline: a brain with no difficulty set and no gap passed.
            VehicleInput baseline = new BotBrain(line, BotSkill.Default).Step(0.02f, s);

            // Explicitly nominal difficulty + a real (nonzero) gap must still be bit-identical: the gap can
            // only matter once a non-nominal difficulty is in play.
            var brain = new BotBrain(line, BotSkill.Default);
            brain.SetDifficulty(BotDifficulty.Nominal);
            VehicleInput withNominal = brain.Step(0.02f, s, 1f, 37f);

            AssertInputEqual(baseline, withNominal, "nominal difficulty == baseline");
        }

        [Test]
        public void Step_LiveDifficulty_ChangesCommitment()
        {
            RacingLine line = LongStraight();
            BotSensors s = ForwardOnLine(line, 60f, 50f); // cruising below the straight's target -> unsaturated throttle

            VehicleInput baseline = new BotBrain(line, BotSkill.Default).Step(0.02f, s);

            var pushed = new BotBrain(line, BotSkill.Default);
            pushed.SetDifficulty(BotDifficulty.FromTier(1f, stakeLevel: 5, rubberBandStrength: 0.5f));
            VehicleInput hot = pushed.Step(0.02f, s, 1f, -45f); // pro, high stake, trailing the field -> push

            Assert.That(hot.Throttle, Is.GreaterThan(baseline.Throttle),
                "an enabled pro/pushing difficulty commits more throttle than the baseline");
        }

        [Test]
        public void Step_LiveDifficulty_KeepsInputsBounded()
        {
            RacingLine line = Square();
            var brain = new BotBrain(line, new BotSkill
            {
                CornerSpeedMult = 1.05f, Aggression = 1.1f, LookaheadM = 11f, LateralOffsetM = 2f,
                Defensiveness = 1f, OvertakeBoldness = 1f, MistakeRate = 0.5f, Consistency = 0f,
            });
            // Maximally cranked difficulty: pro, high stake, full rubber-band, trailing hard.
            brain.SetDifficulty(BotDifficulty.FromTier(1f, stakeLevel: 20, rubberBandStrength: 0.5f));

            Vector3 fwd = line.DirectionAt(20f);
            var neighbors = new[]
            {
                new BotNeighbor { RelativePosition = fwd * 8f, Velocity = fwd * 18f },
                new BotNeighbor { RelativePosition = -fwd * 6f, Velocity = fwd * 30f },
            };
            var s = new BotSensors
            {
                Position = line.PointAt(20f), Forward = fwd, Velocity = fwd * 30f,
                DrivenWheelSlip = 0.3f, Neighbors = neighbors, NeighborCount = 2,
            };

            for (int i = 0; i < 200; i++)
            {
                VehicleInput cmd = brain.Step(0.02f, s, 1.1f, -60f);
                Assert.That(cmd.Steer, Is.InRange(-1f, 1f), $"steer bounded @ {i}");
                Assert.That(cmd.Throttle, Is.InRange(0f, 1f), $"throttle bounded @ {i}");
                Assert.That(cmd.Brake, Is.InRange(0f, 1f), $"brake bounded @ {i}");
                Assert.That(cmd.Handbrake, Is.InRange(0f, 1f), $"handbrake bounded @ {i}");
            }
        }
    }
}
