using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Race;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the on-track personality/archetype layer: a bounded bias set (Blocker / Diver / Cruiser /
    /// Neutral) that nudges the bot's EXISTING tactical knobs — line cover, pass commitment, follow gap —
    /// orthogonally to the skill-tier difficulty. The contract mirrors the project's headline constraint:
    /// the DEFAULT (Neutral, == default(struct)) is a true no-op — every bias is identity and a bot's Step
    /// output is unchanged bit-for-bit — and no hand-built extreme config can carry a bias outside its
    /// subtle clamped band. Step-level tests pin that the biases actually reach the tactical sites, that a
    /// neutral personality never disturbs today's behaviour (even alongside a live difficulty), and that
    /// personality composes with a non-nominal difficulty without either overriding the other's bounds.
    /// </summary>
    public class BotPersonalityTests : TestBase
    {
        // A 100 m square in XZ (matches BotBrainTests / BotDifficultyTests): a real loop to Step a bot through.
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

        // Traffic on the first straight: a slower car ahead in-lane (rear-end / overtake trigger) and a faster
        // car drafting behind (defence trigger) — exercises all three tactical sites the personality biases.
        private static BotSensors Traffic(RacingLine line, float slip = 0f)
        {
            Vector3 fwd = line.DirectionAt(20f);
            var neighbors = new[]
            {
                new BotNeighbor { RelativePosition = fwd * 8f, Velocity = fwd * 18f },   // slower car ahead in-lane
                new BotNeighbor { RelativePosition = -fwd * 6f, Velocity = fwd * 30f },  // faster car drafting behind
            };
            return new BotSensors
            {
                Position = line.PointAt(20f), Forward = fwd, Velocity = fwd * 30f,
                DrivenWheelSlip = slip, Neighbors = neighbors, NeighborCount = 2,
            };
        }

        private static void AssertInputEqual(VehicleInput expected, VehicleInput actual, string label)
        {
            Assert.That(actual.Steer, Is.EqualTo(expected.Steer), label + " steer");
            Assert.That(actual.Throttle, Is.EqualTo(expected.Throttle), label + " throttle");
            Assert.That(actual.Brake, Is.EqualTo(expected.Brake), label + " brake");
            Assert.That(actual.Handbrake, Is.EqualTo(expected.Handbrake), label + " handbrake");
        }

        // ---- Bias math: Neutral is identity, archetypes bias the right way, everything stays clamped --------

        [Test]
        public void Neutral_IsExactIdentity()
        {
            BotPersonality n = BotPersonality.Neutral;
            Assert.That(n.BlockBiasClamped, Is.EqualTo(0f), "no block bias");
            Assert.That(n.DiveAggressionClamped, Is.EqualTo(0f), "no dive bias");
            Assert.That(n.FollowGapScale, Is.EqualTo(1f), "identity follow gap");
        }

        [Test]
        public void DefaultStruct_EqualsNeutral()
        {
            // default(BotPersonality) must be identity so a partially-initialised host struct can't silently
            // hand a bot a tactical bias.
            BotPersonality d = default;
            Assert.That(d.BlockBiasClamped, Is.EqualTo(0f), "default block bias == 0");
            Assert.That(d.DiveAggressionClamped, Is.EqualTo(0f), "default dive bias == 0");
            Assert.That(d.FollowGapScale, Is.EqualTo(1f), "default follow gap == 1");
        }

        [Test]
        public void FromKind_MapsEachArchetype()
        {
            Assert.That(BotPersonality.FromKind(BotPersonalityKind.Neutral).FollowGapScale, Is.EqualTo(1f), "neutral identity");
            // Diver tucks in (< 1), Cruiser gives room (> 1), Blocker covers the line (block bias > 0).
            Assert.That(BotPersonality.FromKind(BotPersonalityKind.Diver).FollowGapScale, Is.LessThan(1f), "diver tucks in");
            Assert.That(BotPersonality.FromKind(BotPersonalityKind.Cruiser).FollowGapScale, Is.GreaterThan(1f), "cruiser gives room");
            Assert.That(BotPersonality.FromKind(BotPersonalityKind.Blocker).BlockBiasClamped, Is.GreaterThan(0f), "blocker defends");
        }

        [Test]
        public void Blocker_DefendsMore_DivesLess()
        {
            BotPersonality b = BotPersonality.Blocker;
            Assert.That(b.BlockBiasClamped, Is.GreaterThan(0f), "blocker covers the line harder");
            Assert.That(b.DiveAggressionClamped, Is.LessThanOrEqualTo(0f), "blocker doesn't go hunting for dives");
        }

        [Test]
        public void Diver_PassesEarlierAndCloser()
        {
            BotPersonality d = BotPersonality.Diver;
            Assert.That(d.DiveAggressionClamped, Is.GreaterThan(0f), "diver commits earlier / slices closer");
            Assert.That(d.FollowGapScale, Is.LessThan(1f), "diver tucks in behind a car");
        }

        [Test]
        public void Cruiser_GivesRoom()
        {
            BotPersonality c = BotPersonality.Cruiser;
            Assert.That(c.BlockBiasClamped, Is.LessThan(0f), "cruiser cedes the line");
            Assert.That(c.DiveAggressionClamped, Is.LessThan(0f), "cruiser doesn't attack");
            Assert.That(c.FollowGapScale, Is.GreaterThan(1f), "cruiser leaves more room");
        }

        [Test]
        public void Biases_AlwaysWithinClamps_UnderExtremeConfigs()
        {
            // However extreme the host-set (or corrupt) fields, every applied bias stays inside its subtle band,
            // so a personality can never make a bot cheat or drive dangerously.
            float[] vals = { -1e9f, -3f, -0.5f, 0f, 0.5f, 3f, 1e9f };
            foreach (float bb in vals)
                foreach (float da in vals)
                    foreach (float fg in vals)
                    {
                        var p = new BotPersonality { BlockBias = bb, DiveAggression = da, FollowGapBias = fg };
                        Assert.That(p.BlockBiasClamped,
                            Is.InRange(-BotPersonality.MaxBlockBias, BotPersonality.MaxBlockBias), $"block {bb}");
                        Assert.That(p.DiveAggressionClamped,
                            Is.InRange(-BotPersonality.MaxDiveAggression, BotPersonality.MaxDiveAggression), $"dive {da}");
                        Assert.That(p.FollowGapScale,
                            Is.InRange(1f - BotPersonality.MaxFollowGapBias, 1f + BotPersonality.MaxFollowGapBias), $"gap {fg}");
                    }
        }

        // ---- Step-level wiring: Neutral no-op, archetypes bias behaviour, composes with difficulty ----------

        [Test]
        public void Step_NeutralPersonality_MatchesBaseline_InTraffic()
        {
            // Setting the Neutral personality explicitly must leave a bot driving bit-for-bit as one with none
            // set — proven with rivals present so all three biased tactical sites are actually exercised.
            RacingLine line = Square();
            BotSensors s = Traffic(line);

            var baseline = new BotBrain(line, BotSkill.Default);
            var neutral = new BotBrain(line, BotSkill.Default);
            neutral.SetPersonality(BotPersonality.Neutral);

            for (int i = 0; i < 100; i++)
                AssertInputEqual(baseline.Step(0.02f, s), neutral.Step(0.02f, s), $"neutral == baseline @ {i}");
        }

        [Test]
        public void Step_DiverClosesGapHarderThanCruiser()
        {
            // A Diver trims its follow buffer and a Cruiser widens it, so with a slower car just ahead the
            // Diver's following speed-cap sits higher and it commits visibly more throttle to close the gap.
            RacingLine line = Square();
            Vector3 fwd = line.DirectionAt(20f);
            var neighbors = new[] { new BotNeighbor { RelativePosition = fwd * 10f, Velocity = fwd * 18f } };
            var s = new BotSensors
            {
                Position = line.PointAt(20f), Forward = fwd, Velocity = fwd * 19f, // near the follow cap, not the free plan
                DrivenWheelSlip = 0f, Neighbors = neighbors, NeighborCount = 1,
            };

            var diver = new BotBrain(line, BotSkill.Default);
            diver.SetPersonality(BotPersonality.Diver);
            var cruiser = new BotBrain(line, BotSkill.Default);
            cruiser.SetPersonality(BotPersonality.Cruiser);

            // First step: the lateral tactic hasn't slewed in yet, so throttle reflects only the follow-gap cap.
            VehicleInput d = diver.Step(0.02f, s);
            VehicleInput c = cruiser.Step(0.02f, s);
            Assert.That(d.Throttle, Is.GreaterThan(c.Throttle),
                "a Diver tucks in and carries more throttle toward the car ahead than a Cruiser");
        }

        [Test]
        public void Step_BlockerCoversLineMoreThanNeutral()
        {
            // With a rival drafting off to one side and no car ahead, a Blocker's larger defend cap lets it cover
            // more of the line, so once the smoothed tactic settles it steers harder toward the follower's side.
            RacingLine line = Square();
            Vector3 fwd = line.DirectionAt(20f);
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            var neighbors = new[]
            {
                // 6 m behind, 3 m to our right: inside the draft window, beyond the neutral defend cap (2.5 m).
                new BotNeighbor { RelativePosition = -fwd * 6f + right * 3f, Velocity = fwd * 12f },
            };
            var s = new BotSensors
            {
                Position = line.PointAt(20f), Forward = fwd, Velocity = fwd * 10f,
                DrivenWheelSlip = 0f, Neighbors = neighbors, NeighborCount = 1,
            };

            var neutral = new BotBrain(line, BotSkill.Default);
            neutral.SetPersonality(BotPersonality.Neutral);
            var blocker = new BotBrain(line, BotSkill.Default);
            blocker.SetPersonality(BotPersonality.Blocker);

            // Step both long enough for the slewed tactical offset to converge, then compare the cover.
            VehicleInput nb = default, bb = default;
            for (int i = 0; i < 120; i++)
            {
                nb = neutral.Step(0.02f, s);
                bb = blocker.Step(0.02f, s);
            }
            Assert.That(Mathf.Abs(bb.Steer), Is.GreaterThan(Mathf.Abs(nb.Steer)),
                "a Blocker covers a drafting rival's line harder than a neutral bot");
        }

        [Test]
        public void Step_NeutralPersonality_WithLiveDifficulty_MatchesDifficultyOnly()
        {
            // Composition, one direction: a neutral personality must not disturb a live (non-nominal) difficulty
            // — the difficulty layer's output is preserved bit-for-bit whether or not Neutral is set.
            RacingLine line = Square();
            BotSensors s = Traffic(line, slip: 0.2f);
            BotDifficulty live = BotDifficulty.FromTier(1f, stakeLevel: 5, rubberBandStrength: 0.5f);

            var diffOnly = new BotBrain(line, BotSkill.Default);
            diffOnly.SetDifficulty(live);
            var both = new BotBrain(line, BotSkill.Default);
            both.SetDifficulty(live);
            both.SetPersonality(BotPersonality.Neutral);

            for (int i = 0; i < 100; i++)
                AssertInputEqual(diffOnly.Step(0.02f, s, 1f, -45f), both.Step(0.02f, s, 1f, -45f),
                    $"neutral personality doesn't disturb a live difficulty @ {i}");
        }

        [Test]
        public void Step_ArchetypeWithLiveDifficulty_StaysBounded()
        {
            // Composition, the other direction: a maximally opinionated archetype on top of a cranked difficulty,
            // at max skill, in traffic, must still only ever emit valid, bounded inputs — neither layer can push
            // the other past the control ranges.
            RacingLine line = Square();
            BotSensors s = Traffic(line, slip: 0.3f);

            foreach (BotPersonalityKind kind in new[]
                { BotPersonalityKind.Blocker, BotPersonalityKind.Diver, BotPersonalityKind.Cruiser })
            {
                var brain = new BotBrain(line, new BotSkill
                {
                    CornerSpeedMult = 1.05f, Aggression = 1.1f, LookaheadM = 11f, LateralOffsetM = 2f,
                    Defensiveness = 1f, OvertakeBoldness = 1f, MistakeRate = 0.5f, Consistency = 0f,
                });
                brain.SetDifficulty(BotDifficulty.FromTier(1f, stakeLevel: 20, rubberBandStrength: 0.5f));
                brain.SetPersonality(BotPersonality.FromKind(kind));

                for (int i = 0; i < 200; i++)
                {
                    VehicleInput cmd = brain.Step(0.02f, s, 1.1f, -60f);
                    Assert.That(cmd.Steer, Is.InRange(-1f, 1f), $"{kind} steer bounded @ {i}");
                    Assert.That(cmd.Throttle, Is.InRange(0f, 1f), $"{kind} throttle bounded @ {i}");
                    Assert.That(cmd.Brake, Is.InRange(0f, 1f), $"{kind} brake bounded @ {i}");
                    Assert.That(cmd.Handbrake, Is.InRange(0f, 1f), $"{kind} handbrake bounded @ {i}");
                }
            }
        }
    }
}
