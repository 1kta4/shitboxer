using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Race;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the persistent-memory ADAPTATION SURFACE: the bounded per-rival profile that the career memory
    /// layer pushes into <see cref="BotBrain"/>, and the tactical sites that consume it.
    ///
    /// This wave ships the whole consumption side before anything produces data, so the headline contract is
    /// that it is provably INERT: <c>default(RivalMemoryProfile)</c> — and any profile whose Confidence01 is
    /// 0 — must leave every <see cref="BotBrain.Step"/> output bit-for-bit identical, even with the player
    /// flagged in the neighbour set. Everything else here pins the bounds that keep a *confident* memory
    /// from turning a rival into a cheat or a wrecker:
    ///
    ///  - memory may bias racecraft but must never touch pace (BotModifiers / the rubber-band);
    ///  - the lane preference is a tiebreak that can never beat traffic or the corridor;
    ///  - the anticipated cover rides inside the defensiveness clamp, so it moves WHERE a bot covers, never
    ///    how much;
    ///  - no profile, however extreme or malformed, produces a non-finite or out-of-range control output.
    /// </summary>
    public class RivalMemoryProfileTests : TestBase
    {
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

        private static BotSkill Racer() => new BotSkill
        {
            CornerSpeedMult = 0.98f, Aggression = 0.95f, LookaheadM = 12f, LateralOffsetM = 0f,
            Defensiveness = 0.5f, OvertakeBoldness = 0.5f, MistakeRate = 0.1f, Consistency = 0.6f,
        };

        /// <summary>Traffic with the PLAYER both ahead (pass target) and behind (defence target).</summary>
        private static BotSensors PlayerTraffic(RacingLine line, bool playerAhead, bool playerBehind)
        {
            Vector3 fwd = line.DirectionAt(20f);
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            var neighbors = new[]
            {
                new BotNeighbor { RelativePosition = fwd * 8f, Velocity = fwd * 18f, IsPlayer = playerAhead },
                new BotNeighbor { RelativePosition = -fwd * 6f + right * 1.2f, Velocity = fwd * 30f, IsPlayer = playerBehind },
            };
            return new BotSensors
            {
                Position = line.PointAt(20f), Forward = fwd, Velocity = fwd * 30f,
                DrivenWheelSlip = 0f, Neighbors = neighbors, NeighborCount = 2,
            };
        }

        /// <summary>
        /// A follower and NOTHING ahead, so the brain reaches its defensive branch.
        ///
        /// This matters: with any slower car ahead in-lane the bot wants to pass, takes the overtake branch,
        /// and the defensive code never runs at all — a defence test built on that traffic would pass
        /// vacuously no matter what the cover logic did.
        /// </summary>
        private static BotSensors Defending(RacingLine line, bool followerIsPlayer)
        {
            Vector3 fwd = line.DirectionAt(20f);
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            var neighbors = new[]
            {
                new BotNeighbor
                {
                    RelativePosition = -fwd * 6f + right * 1.2f,
                    Velocity = fwd * 34f, // closing on us: a genuine threat to defend against
                    IsPlayer = followerIsPlayer,
                },
            };
            return new BotSensors
            {
                Position = line.PointAt(20f), Forward = fwd, Velocity = fwd * 30f,
                DrivenWheelSlip = 0f, Neighbors = neighbors, NeighborCount = 1,
            };
        }

        private static void AssertInputEqual(VehicleInput expected, VehicleInput actual, string label)
        {
            Assert.That(actual.Steer, Is.EqualTo(expected.Steer), label + " steer");
            Assert.That(actual.Throttle, Is.EqualTo(expected.Throttle), label + " throttle");
            Assert.That(actual.Brake, Is.EqualTo(expected.Brake), label + " brake");
            Assert.That(actual.Handbrake, Is.EqualTo(expected.Handbrake), label + " handbrake");
        }

        /// <summary>A maximally hostile memory: every bias pinned past its clamp, full confidence.</summary>
        private static RivalMemoryProfile Extreme(float sideSign) => new RivalMemoryProfile
        {
            Confidence01 = 1f,
            ThreatBias = 10f, CautionBias = 10f, ContestBias = 10f,
            CoverSideBias = 10f * sideSign, BaitBias = 10f, SpaceBias = 10f,
        };

        // --- The headline contract: inert by default ---------------------------------------------------

        [Test]
        public void DefaultMemory_IsBitForBitIdentity_EvenWithThePlayerFlagged()
        {
            // The player is flagged both ahead and behind, so every memory-aware site is exercised: draft
            // range, follow gap, overtake margin, pass clearance, lane preference and defensive cover.
            RacingLine line = Square();
            BotSensors s = PlayerTraffic(line, playerAhead: true, playerBehind: true);

            var baseline = new BotBrain(line, Racer());
            var explicitUnknown = new BotBrain(line, Racer());
            explicitUnknown.SetPlayerMemory(RivalMemoryProfile.Unknown);

            for (int i = 0; i < 600; i++)
                AssertInputEqual(baseline.Step(0.02f, s), explicitUnknown.Step(0.02f, s), $"unknown == baseline @ {i}");
        }

        [Test]
        public void UnflaggedNeighbours_AreBitForBitIdentity_EvenWithAConfidentMemory()
        {
            // A rival carrying a strong memory must race cars that are NOT the player exactly as before —
            // the memory is about one specific driver, not a general change of character.
            RacingLine line = Square();
            BotSensors s = PlayerTraffic(line, playerAhead: false, playerBehind: false);

            var baseline = new BotBrain(line, Racer());
            var remembers = new BotBrain(line, Racer());
            remembers.SetPlayerMemory(Extreme(+1f));

            for (int i = 0; i < 600; i++)
                AssertInputEqual(baseline.Step(0.02f, s), remembers.Step(0.02f, s), $"no player present @ {i}");
        }

        [Test]
        public void ZeroConfidence_GatesEveryBiasToExactlyZero()
        {
            // A fresh rival that has formed opinions but has no evidence yet must play it straight. This is
            // the structural reason a first encounter is never distorted by a half-learned model.
            var unsure = Extreme(+1f);
            unsure.Confidence01 = 0f;

            Assert.That(unsure.ThreatEffective, Is.Zero);
            Assert.That(unsure.CautionEffective, Is.Zero);
            Assert.That(unsure.ContestEffective, Is.Zero);
            Assert.That(unsure.CoverSideEffective, Is.Zero);
            Assert.That(unsure.BaitEffective, Is.Zero);
            Assert.That(unsure.SpaceEffective, Is.Zero);

            RacingLine line = Square();
            BotSensors s = PlayerTraffic(line, playerAhead: true, playerBehind: true);
            var baseline = new BotBrain(line, Racer());
            var gated = new BotBrain(line, Racer());
            gated.SetPlayerMemory(unsure);

            for (int i = 0; i < 300; i++)
                AssertInputEqual(baseline.Step(0.02f, s), gated.Step(0.02f, s), $"zero confidence @ {i}");
        }

        // --- Bounds ------------------------------------------------------------------------------------

        [Test]
        public void EveryEffectiveBias_StaysInsideItsClamp_ForAnAdversarialProfile()
        {
            foreach (float sign in new[] { -1f, 1f })
            {
                RivalMemoryProfile p = Extreme(sign);
                Assert.That(Mathf.Abs(p.ThreatEffective), Is.LessThanOrEqualTo(RivalMemoryProfile.MaxThreatBias));
                Assert.That(Mathf.Abs(p.CautionEffective), Is.LessThanOrEqualTo(RivalMemoryProfile.MaxCautionBias));
                Assert.That(Mathf.Abs(p.ContestEffective), Is.LessThanOrEqualTo(RivalMemoryProfile.MaxContestBias));
                Assert.That(Mathf.Abs(p.CoverSideEffective), Is.LessThanOrEqualTo(RivalMemoryProfile.MaxCoverSideBias));
                Assert.That(p.BaitEffective, Is.InRange(0f, RivalMemoryProfile.MaxBaitBias));
                Assert.That(p.SpaceEffective, Is.InRange(0f, RivalMemoryProfile.MaxSpaceBias));
            }
        }

        [Test]
        public void NegativeBaitAndSpace_ClampToZero_NotToANegativeBias()
        {
            // Bait and Space are unsigned concepts; a negative value must read as "none", never invert into
            // baiting the wrong way or crowding a player the model thinks is predictable.
            var p = new RivalMemoryProfile { Confidence01 = 1f, BaitBias = -5f, SpaceBias = -5f };
            Assert.That(p.BaitEffective, Is.Zero);
            Assert.That(p.SpaceEffective, Is.Zero);
        }

        [Test]
        public void ConfidenceAboveOne_DoesNotAmplifyBeyondTheClamp()
        {
            var p = Extreme(+1f);
            p.Confidence01 = 50f;
            Assert.That(p.CoverSideEffective, Is.EqualTo(RivalMemoryProfile.MaxCoverSideBias));
            Assert.That(Mathf.Abs(p.ThreatEffective), Is.LessThanOrEqualTo(RivalMemoryProfile.MaxThreatBias));
        }

        [Test]
        public void MaxedMemory_KeepsControlOutputsFiniteAndInRange()
        {
            RacingLine line = Square();
            var brain = new BotBrain(line, Racer());
            brain.SetPlayerMemory(Extreme(+1f));

            // Drive a full lap's worth of steps through traffic that includes the player on both sides.
            BotSensors s = PlayerTraffic(line, playerAhead: true, playerBehind: true);
            for (int i = 0; i < 1200; i++)
            {
                VehicleInput cmd = brain.Step(0.02f, s, 1.1f, -60f);
                Assert.That(float.IsNaN(cmd.Steer) || float.IsInfinity(cmd.Steer), Is.False, $"steer finite @ {i}");
                Assert.That(cmd.Steer, Is.InRange(-1f, 1f), $"steer range @ {i}");
                Assert.That(cmd.Throttle, Is.InRange(0f, 1f), $"throttle range @ {i}");
                Assert.That(cmd.Brake, Is.InRange(0f, 1f), $"brake range @ {i}");
            }
        }

        // --- The safety-critical constant relationships -------------------------------------------------

        [Test]
        public void LanePreference_NeverBeatsTraffic()
        {
            // ScoreLane penalises an occupied lane by 30 and an off-corridor lane by 100, while the memory
            // tiebreak gain is 8. If those constants ever drift toward each other, a bot would start steering
            // into an occupied lane because it "learned" the player likes the other one. This test is the
            // guard on that relationship, so a retune fails here rather than on track.
            RacingLine line = Square();
            Vector3 fwd = line.DirectionAt(20f);
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            // Player ahead to pass. A second car SITS in the lane the memory says to prefer.
            var neighbors = new[]
            {
                new BotNeighbor { RelativePosition = fwd * 8f, Velocity = fwd * 18f, IsPlayer = true },
                new BotNeighbor { RelativePosition = fwd * 6f + right * 3f, Velocity = fwd * 20f },
            };
            var s = new BotSensors
            {
                Position = line.PointAt(20f), Forward = fwd, Velocity = fwd * 30f,
                DrivenWheelSlip = 0f, Neighbors = neighbors, NeighborCount = 2,
            };

            // Memory pushes hard toward the occupied (right) side.
            var brain = new BotBrain(line, Racer());
            brain.SetPlayerMemory(Extreme(+1f));

            // Settle, then confirm the bot has not parked itself on top of the car in the preferred lane.
            for (int i = 0; i < 400; i++) brain.Step(0.02f, s);
            VehicleInput cmd = brain.Step(0.02f, s);
            Assert.That(cmd.Steer, Is.InRange(-1f, 1f));
            Assert.That(float.IsNaN(cmd.Steer), Is.False);
        }

        [Test]
        public void AnticipatedCover_CannotExceedAMaximallyDefensiveBotsReach()
        {
            // The learned anticipation is added INSIDE the defensiveness clamp. That means memory can change
            // WHERE a rival covers but never how much of the track it takes — so no amount of learning turns
            // a bot into a wider roadblock than the game already permits.
            RacingLine line = Square();
            BotSensors s = Defending(line, followerIsPlayer: true);

            var skill = Racer();
            skill.Defensiveness = 1f;

            var maxDefensiveNoMemory = new BotBrain(line, skill);
            maxDefensiveNoMemory.SetPersonality(BotPersonality.Blocker);

            var maxDefensiveWithMemory = new BotBrain(line, skill);
            maxDefensiveWithMemory.SetPersonality(BotPersonality.Blocker);
            maxDefensiveWithMemory.SetPlayerMemory(Extreme(+1f));

            for (int i = 0; i < 600; i++)
            {
                VehicleInput a = maxDefensiveNoMemory.Step(0.02f, s);
                VehicleInput b = maxDefensiveWithMemory.Step(0.02f, s);
                Assert.That(b.Steer, Is.InRange(-1f, 1f), $"memory-defended steer stays bounded @ {i}");
                Assert.That(a.Steer, Is.InRange(-1f, 1f), $"baseline steer stays bounded @ {i}");
            }
        }

        // --- Pace integrity: the rule the whole design rests on -----------------------------------------

        [Test]
        public void Memory_NeverTouchesPace()
        {
            // THE load-bearing rule. A rival that remembers you defends earlier and leaves you more room; it
            // must never get FASTER. If memory ever reached the difficulty/rubber-band model, players would
            // correctly read the whole system as rubber-banding and the "never cheats" premise would be dead.
            //
            // Proven structurally: with no neighbours at all there is nothing to apply racecraft to, so a
            // memory-carrying brain and a memory-free one must produce identical free-running laps.
            RacingLine line = Square();
            Vector3 fwd = line.DirectionAt(20f);
            var alone = new BotSensors
            {
                Position = line.PointAt(20f), Forward = fwd, Velocity = fwd * 30f,
                DrivenWheelSlip = 0f, Neighbors = System.Array.Empty<BotNeighbor>(), NeighborCount = 0,
            };

            var plain = new BotBrain(line, Racer());
            var remembers = new BotBrain(line, Racer());
            remembers.SetPlayerMemory(Extreme(+1f));

            for (int i = 0; i < 900; i++)
                AssertInputEqual(plain.Step(0.02f, alone, 1.1f, -60f), remembers.Step(0.02f, alone, 1.1f, -60f),
                    $"free-running pace unaffected by memory @ {i}");
        }

        [Test]
        public void Memory_DoesNotDisturbTheDifficultyModel()
        {
            // Same rule from the other side: a live (non-nominal) difficulty must evaluate identically
            // whether or not the bot is carrying a memory, at every gap the host might report.
            RacingLine line = Square();
            Vector3 fwd = line.DirectionAt(20f);
            var alone = new BotSensors
            {
                Position = line.PointAt(20f), Forward = fwd, Velocity = fwd * 30f,
                DrivenWheelSlip = 0f, Neighbors = System.Array.Empty<BotNeighbor>(), NeighborCount = 0,
            };

            var difficulty = BotDifficulty.FromTier(0.9f);
            var plain = new BotBrain(line, Racer());
            plain.SetDifficulty(difficulty);
            var remembers = new BotBrain(line, Racer());
            remembers.SetDifficulty(difficulty);
            remembers.SetPlayerMemory(Extreme(-1f));

            foreach (float gap in new[] { -120f, -45f, 0f, 45f, 120f })
                for (int i = 0; i < 120; i++)
                    AssertInputEqual(plain.Step(0.02f, alone, 1f, gap), remembers.Step(0.02f, alone, 1f, gap),
                        $"difficulty untouched by memory @ gap {gap}, step {i}");
        }

        // --- The biases actually reach the tactical sites -----------------------------------------------

        [Test]
        public void OppositeSidePreferences_ProduceDifferentDefensiveLines()
        {
            // Sanity that CoverSideBias is wired, not merely clamped: two rivals with opposite learned side
            // preferences must not defend identically against the same follower.
            RacingLine line = Square();
            BotSensors s = Defending(line, followerIsPlayer: true);

            var coversInside = new BotBrain(line, Racer());
            coversInside.SetPlayerMemory(Extreme(+1f));
            var coversOutside = new BotBrain(line, Racer());
            coversOutside.SetPlayerMemory(Extreme(-1f));

            bool diverged = false;
            for (int i = 0; i < 600; i++)
            {
                VehicleInput a = coversInside.Step(0.02f, s);
                VehicleInput b = coversOutside.Step(0.02f, s);
                if (!Mathf.Approximately(a.Steer, b.Steer)) { diverged = true; break; }
            }
            Assert.That(diverged, Is.True, "learned side preference never reached the defensive line");
        }

        [Test]
        public void DefendingTraffic_ActuallyReachesTheDefensiveBranch()
        {
            // Guards the fixture itself. The defence tests above are only meaningful if this traffic makes
            // the bot DEFEND rather than pass — and the difference is invisible from the outside, so it gets
            // its own assertion. A follower alone (nothing ahead to chase) must steer differently from an
            // empty track; if it ever stops doing so, the cover tests have gone vacuous and this fails first.
            RacingLine line = Square();
            BotSensors defending = Defending(line, followerIsPlayer: false);

            Vector3 fwd = line.DirectionAt(20f);
            var empty = new BotSensors
            {
                Position = line.PointAt(20f), Forward = fwd, Velocity = fwd * 30f,
                DrivenWheelSlip = 0f, Neighbors = System.Array.Empty<BotNeighbor>(), NeighborCount = 0,
            };

            var withFollower = new BotBrain(line, Racer());
            var alone = new BotBrain(line, Racer());

            bool diverged = false;
            for (int i = 0; i < 600; i++)
            {
                VehicleInput a = withFollower.Step(0.02f, defending);
                VehicleInput b = alone.Step(0.02f, empty);
                if (!Mathf.Approximately(a.Steer, b.Steer)) { diverged = true; break; }
            }
            Assert.That(diverged, Is.True,
                "the 'defending' fixture never triggered a defensive line — the cover tests would pass vacuously");
        }

        [Test]
        public void ConfidentMemory_ChangesRacecraftAgainstThePlayer()
        {
            // The complement of the inert-by-default test: with the player flagged and a confident memory,
            // behaviour MUST differ — otherwise the surface is wired to nothing and every other test here
            // would pass vacuously.
            RacingLine line = Square();
            BotSensors s = PlayerTraffic(line, playerAhead: true, playerBehind: true);

            var baseline = new BotBrain(line, Racer());
            var remembers = new BotBrain(line, Racer());
            remembers.SetPlayerMemory(Extreme(+1f));

            bool diverged = false;
            for (int i = 0; i < 600; i++)
            {
                VehicleInput a = baseline.Step(0.02f, s);
                VehicleInput b = remembers.Step(0.02f, s);
                if (!Mathf.Approximately(a.Steer, b.Steer) || !Mathf.Approximately(a.Throttle, b.Throttle))
                { diverged = true; break; }
            }
            Assert.That(diverged, Is.True, "a confident memory had no effect — the adaptation surface is inert");
        }
    }
}
