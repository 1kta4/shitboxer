using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Race;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the bounded rubber-band the bots use to keep the pack tense. The clamp is a pure static
    /// helper, so it (and the fact that BotBrain.Step actually routes the host's raw factor through it)
    /// is exercised here with no scene: the contract is that the assist stays subtle (+/-10%), that
    /// 1 leaves the base plan untouched, and that no gap the host feeds in can push a bot past the band.
    /// </summary>
    public class BotBrainTests : TestBase
    {
        private const float BandMin = 0.90f; // documented ease-off floor (leader)
        private const float BandMax = 1.10f; // documented push ceiling (trailer)
        private const float MistakeMaxLift = 0.5f; // mirrors BotBrain.MistakeMaxLift: a bobble eases at most half throttle

        // A 100 m square in XZ, matching RacingLineTests — enough of a loop to run a real Step through.
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

        // A car sitting on the line at progress 20 m, cruising forward at 30 m/s, no rivals in sight.
        private static BotSensors ForwardOnLine(RacingLine line)
        {
            Vector3 fwd = line.DirectionAt(20f);
            return new BotSensors
            {
                Position = line.PointAt(20f),
                Forward = fwd,
                Velocity = fwd * 30f,
                DrivenWheelSlip = 0f,
                Neighbors = null,
                NeighborCount = 0,
            };
        }

        private static void AssertInputEqual(VehicleInput expected, VehicleInput actual, string label)
        {
            // Two deterministic Steps over identical (dt, sensors, clamped factor) must be bit-identical.
            Assert.That(actual.Steer, Is.EqualTo(expected.Steer), label + " steer");
            Assert.That(actual.Throttle, Is.EqualTo(expected.Throttle), label + " throttle");
            Assert.That(actual.Brake, Is.EqualTo(expected.Brake), label + " brake");
            Assert.That(actual.Handbrake, Is.EqualTo(expected.Handbrake), label + " handbrake");
        }

        [Test]
        public void ClampRubberband_LeavesNeutralUntouched()
        {
            Assert.That(BotBrain.ClampRubberband(1f), Is.EqualTo(1f));
        }

        [Test]
        public void ClampRubberband_PassesValuesInsideTheBand()
        {
            Assert.That(BotBrain.ClampRubberband(1.05f), Is.EqualTo(1.05f), "small boost passes through");
            Assert.That(BotBrain.ClampRubberband(0.95f), Is.EqualTo(0.95f), "small ease passes through");
        }

        [Test]
        public void ClampRubberband_NeverExceedsTheSubtleBand()
        {
            // However extreme the host's raw factor, the assist can never read as cheating.
            foreach (float raw in new[] { 2f, 5f, 100f, float.MaxValue })
                Assert.That(BotBrain.ClampRubberband(raw), Is.LessThanOrEqualTo(BandMax), $"boost capped @ {raw}");
            foreach (float raw in new[] { 0.5f, 0f, -3f, float.MinValue })
                Assert.That(BotBrain.ClampRubberband(raw), Is.GreaterThanOrEqualTo(BandMin), $"ease floored @ {raw}");
        }

        [Test]
        public void ClampRubberband_IsIdempotent()
        {
            foreach (float raw in new[] { -2f, 0.8f, 1f, 1.3f, 9f })
            {
                float once = BotBrain.ClampRubberband(raw);
                Assert.That(BotBrain.ClampRubberband(once), Is.EqualTo(once), $"idempotent @ {raw}");
            }
        }

        [Test]
        public void Step_DefaultFactorMatchesExplicitNeutral()
        {
            RacingLine line = Square();
            BotSensors s = ForwardOnLine(line);

            VehicleInput baseline = new BotBrain(line, BotSkill.Default).Step(0.02f, s);
            VehicleInput neutral = new BotBrain(line, BotSkill.Default).Step(0.02f, s, 1f);

            AssertInputEqual(baseline, neutral, "default == neutral");
        }

        [Test]
        public void Step_ClampsExtremeFactorsToTheBandEdges()
        {
            RacingLine line = Square();
            BotSensors s = ForwardOnLine(line);

            // A runaway host factor must drive the plan identically to the ceiling — proving Step routes
            // the raw factor through ClampRubberband rather than applying it raw.
            VehicleInput runaway = new BotBrain(line, BotSkill.Default).Step(0.02f, s, 100f);
            VehicleInput ceiling = new BotBrain(line, BotSkill.Default).Step(0.02f, s, BandMax);
            AssertInputEqual(ceiling, runaway, "huge boost clamps to ceiling");

            VehicleInput crawl = new BotBrain(line, BotSkill.Default).Step(0.02f, s, -5f);
            VehicleInput floor = new BotBrain(line, BotSkill.Default).Step(0.02f, s, BandMin);
            AssertInputEqual(floor, crawl, "negative factor clamps to floor");
        }

        // ---- Personality: the per-bot deterministic "mistake" bobble ---------------------------------------

        [Test]
        public void MistakeFactor_ZeroRateNeverBobbles()
        {
            // The neutral contract: a flawless (rate 0) bot never eases off, whatever its progress/seed.
            for (float p = -40f; p < 500f; p += 3.7f)
                Assert.That(BotBrain.MistakeFactor(p, 0f, 0f, 12345), Is.EqualTo(0f), $"rate 0 clean @ {p}");
        }

        [Test]
        public void MistakeFactor_StaysWithinBounds()
        {
            // However the host varies rate/consistency/seed/position, a bobble can only ever be a bounded lift:
            // never negative (that would be a phantom throttle add) and never past MistakeMaxLift (never a stop).
            foreach (int seed in new[] { 0, 7, -13, 99991 })
                foreach (float rate in new[] { 0.05f, 0.2f, 0.5f, 1f, 2f })
                    foreach (float cons in new[] { 0f, 0.5f, 1f })
                        for (float p = -40f; p < 600f; p += 2.3f)
                        {
                            float m = BotBrain.MistakeFactor(p, rate, cons, seed);
                            Assert.That(m, Is.GreaterThanOrEqualTo(0f), $"never negative (r{rate} c{cons} s{seed} @ {p})");
                            Assert.That(m, Is.LessThanOrEqualTo(MistakeMaxLift), $"never past the bounded lift (r{rate} c{cons} s{seed} @ {p})");
                        }
        }

        [Test]
        public void MistakeFactor_IsDeterministic()
        {
            // Same inputs must give the same bobble every call — the whole point of hashing progress instead of
            // sampling Random, so a headless server reproduces it and it repeats lap-to-lap.
            for (float p = 0f; p < 300f; p += 5f)
                Assert.That(BotBrain.MistakeFactor(p, 0.6f, 0.3f, 42),
                    Is.EqualTo(BotBrain.MistakeFactor(p, 0.6f, 0.3f, 42)), $"repeatable @ {p}");
        }

        [Test]
        public void MistakeFactor_BothBobblesAndRests()
        {
            // Over a lap's worth of bins the signal must sometimes fire and sometimes stay clean — proving it's
            // an occasional bobble that strings the field out, not a constant handicap. Aggregated across a few
            // seeds so the assertion can't hinge on one bot's particular hash draws.
            bool sawBobble = false, sawClean = false;
            foreach (int seed in new[] { 1, 5, 17, -23 })
                for (float p = 0f; p < 800f; p += 4f)
                {
                    float m = BotBrain.MistakeFactor(p, 1f, 0f, seed);
                    sawBobble |= m > 0f;
                    sawClean |= m == 0f;
                }
            Assert.That(sawBobble, Is.True, "fires sometimes");
            Assert.That(sawClean, Is.True, "rests sometimes");
        }

        [Test]
        public void MistakeFactor_ConsistencyDampsMagnitude()
        {
            // On any given bin, a steadier bot's bobble is never larger than an erratic one's.
            for (float p = 0f; p < 800f; p += 4f)
            {
                float erratic = BotBrain.MistakeFactor(p, 1f, 0f, 9);
                float steady = BotBrain.MistakeFactor(p, 1f, 1f, 9);
                Assert.That(steady, Is.LessThanOrEqualTo(erratic + 1e-6f), $"steady <= erratic @ {p}");
            }
        }

        [Test]
        public void Default_IsNeutralAndMistakeFree()
        {
            // "Sensible neutral" default: the reference bot never bobbles, so BotSkill.Default reproduces the
            // pre-personality behaviour exactly (see Step_DefaultFactorMatchesExplicitNeutral for the Step-level
            // proof; this pins the knobs themselves).
            BotSkill d = BotSkill.Default;
            Assert.That(d.MistakeRate, Is.EqualTo(0f), "default makes no mistakes");
            Assert.That(d.Defensiveness, Is.EqualTo(0f), "default defends exactly as the old code");
            Assert.That(d.OvertakeBoldness, Is.EqualTo(0f), "default passes exactly as the old code");
            for (float p = 0f; p < 400f; p += 6f)
                Assert.That(BotBrain.MistakeFactor(p, d.MistakeRate, d.Consistency, 123), Is.EqualTo(0f), $"default clean @ {p}");
        }

        [Test]
        public void Step_WithPersonalityAndTraffic_KeepsInputsBounded()
        {
            // A maximally opinionated bot (defends hard, passes bold, bobbles often, erratic) running with a
            // rival to overtake ahead and one drafting behind must still only ever emit valid, bounded inputs —
            // a guard that the defensive/overtake nudges and the bobble never blow past the control ranges.
            RacingLine line = Square();
            var skill = new BotSkill
            {
                CornerSpeedMult = 1.05f, Aggression = 1.1f, LookaheadM = 11f, LateralOffsetM = 2f,
                Defensiveness = 1f, OvertakeBoldness = 1f, MistakeRate = 0.5f, Consistency = 0f,
            };
            var brain = new BotBrain(line, skill);

            Vector3 fwd = line.DirectionAt(20f);
            var neighbors = new[]
            {
                new BotNeighbor { RelativePosition = fwd * 8f, Velocity = fwd * 18f },   // slower car dead ahead in-lane
                new BotNeighbor { RelativePosition = -fwd * 6f, Velocity = fwd * 30f },  // faster car drafting behind
            };
            var s = new BotSensors
            {
                Position = line.PointAt(20f), Forward = fwd, Velocity = fwd * 30f,
                DrivenWheelSlip = 0f, Neighbors = neighbors, NeighborCount = 2,
            };

            for (int i = 0; i < 200; i++)
            {
                VehicleInput cmd = brain.Step(0.02f, s, 1.1f);
                Assert.That(cmd.Steer, Is.InRange(-1f, 1f), $"steer bounded @ {i}");
                Assert.That(cmd.Throttle, Is.InRange(0f, 1f), $"throttle bounded @ {i}");
                Assert.That(cmd.Brake, Is.InRange(0f, 1f), $"brake bounded @ {i}");
                Assert.That(cmd.Handbrake, Is.InRange(0f, 1f), $"handbrake bounded @ {i}");
            }
        }
    }
}
