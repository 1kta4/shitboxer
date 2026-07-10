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
    }
}
