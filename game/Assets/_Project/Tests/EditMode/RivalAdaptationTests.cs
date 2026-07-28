using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Meta;
using Shitboxer.Race;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the closing of the loop: memory becoming racecraft.
    ///
    /// The headline test here is <see cref="EndToEnd_ADivebombingPlayerGetsTheInsideCovered"/>, which drives
    /// the ENTIRE chain — synthetic car frames → observer → persistent memory → adaptation → BotBrain — with
    /// no scene, no rigidbodies and no engine loop, and asserts the bot's defensive line actually moves
    /// toward the side the synthetic player kept attacking. Everything else pins the bounds that keep a
    /// confident memory from turning the field into either a cheat or a blob.
    /// </summary>
    public class RivalAdaptationTests : TestBase
    {
        private const string Kes = "vera_kestrel";

        private static RivalLearningProfile Neutral() =>
            RivalLearningProfile.For(RivalPersonality.Calculating);

        /// <summary>A style profile of a fast, dirty, committed inside-diver, with plenty of evidence.</summary>
        private static PlayerStyleProfile Hostile()
        {
            var s = new PlayerStyleProfile { racesObserved = 30 };
            s.insidePreference.Add(40f, 3f);
            s.divePropensity.Add(30f, 4f);
            s.passSuccess.Add(25f, 8f);
            s.defendsInside.Add(20f, 2f);
            s.diveSeverity.Add(0.8f, 30f);
            s.paceScore.Add(0.9f, 20f);
            s.collisionRate.Add(30f, 600f);
            s.faultShare.Add(20f, 3f);
            return s;
        }

        private static RivalMemory Experienced(int encounters = 12)
        {
            RivalMemory m = RivalMemory.Fresh(Kes);
            m.encounters = encounters;
            m.proximitySeconds = encounters * 60f;
            m.rivalry01 = 0.8f;
            m.personalFaultSeverity = 3f;
            m.lastSeenRaceOrdinal = 30;
            return m;
        }

        // --- The identity-default guarantee ------------------------------------------------------------

        [Test]
        public void AFreshRival_HasNoOpinion_ForEveryPersonality()
        {
            // A rival meeting the player for the first time must race exactly as it does today. This holds
            // structurally rather than by special case: no encounters -> no gate -> every bias zero.
            foreach (RivalPersonality personality in System.Enum.GetValues(typeof(RivalPersonality)))
            {
                RivalMemoryProfile p = RivalAdaptation.ToProfile(
                    Hostile(), RivalMemory.Fresh(Kes), RivalLearningProfile.For(personality));

                Assert.That(p.Confidence01, Is.Zero, $"{personality} formed an opinion with no history");
                Assert.That(p.ThreatEffective, Is.Zero, $"{personality}");
                Assert.That(p.CautionEffective, Is.Zero, $"{personality}");
                Assert.That(p.ContestEffective, Is.Zero, $"{personality}");
                Assert.That(p.CoverSideEffective, Is.Zero, $"{personality}");
                Assert.That(p.BaitEffective, Is.Zero, $"{personality}");
                Assert.That(p.SpaceEffective, Is.Zero, $"{personality}");
            }
        }

        [Test]
        public void OneEncounter_IsBelowTheHardFloor()
        {
            RivalMemory once = RivalMemory.Fresh(Kes);
            once.encounters = 1;
            RivalMemoryProfile p = RivalAdaptation.ToProfile(Hostile(), once, Neutral());
            Assert.That(p.Confidence01, Is.Zero, "one race must never change how the field races you");
        }

        [Test]
        public void ANullStyleProfile_YieldsIdentity()
        {
            Assert.That(RivalAdaptation.ToProfile(null, Experienced(), Neutral()).Confidence01, Is.Zero);
        }

        [Test]
        public void AnEmptyStyleProfile_YieldsNoBiases()
        {
            // Plenty of personal encounters but nothing actually observed about how the player races.
            RivalMemoryProfile p = RivalAdaptation.ToProfile(new PlayerStyleProfile(), Experienced(), Neutral());
            Assert.That(p.CoverSideEffective, Is.Zero);
            Assert.That(p.BaitEffective, Is.Zero);
        }

        // --- Bounds -------------------------------------------------------------------------------------

        [Test]
        public void EveryEmittedBias_StaysInsideItsClamp()
        {
            foreach (RivalPersonality personality in System.Enum.GetValues(typeof(RivalPersonality)))
            {
                RivalMemoryProfile p = RivalAdaptation.ToProfile(
                    Hostile(), Experienced(200), RivalLearningProfile.For(personality));

                Assert.That(Mathf.Abs(p.ThreatEffective), Is.LessThanOrEqualTo(RivalMemoryProfile.MaxThreatBias + 1e-4f), $"{personality}");
                Assert.That(Mathf.Abs(p.CautionEffective), Is.LessThanOrEqualTo(RivalMemoryProfile.MaxCautionBias + 1e-4f), $"{personality}");
                Assert.That(Mathf.Abs(p.ContestEffective), Is.LessThanOrEqualTo(RivalMemoryProfile.MaxContestBias + 1e-4f), $"{personality}");
                Assert.That(Mathf.Abs(p.CoverSideEffective), Is.LessThanOrEqualTo(RivalMemoryProfile.MaxCoverSideBias + 1e-4f), $"{personality}");
                Assert.That(p.BaitEffective, Is.InRange(0f, RivalMemoryProfile.MaxBaitBias + 1e-4f), $"{personality}");
                Assert.That(p.SpaceEffective, Is.InRange(0f, RivalMemoryProfile.MaxSpaceBias + 1e-4f), $"{personality}");
            }
        }

        [Test]
        public void ABiasCannotJumpInOneRace()
        {
            // Slewing is what stops the "cover inside -> player switches outside -> chase" oscillation.
            RivalMemory m = Experienced();
            m.lastCoverSideBias = 0.5f;

            RivalMemoryProfile p = RivalAdaptation.ToProfile(Hostile(), m, Neutral());
            Assert.That(Mathf.Abs(p.CoverSideBias - 0.5f),
                Is.LessThanOrEqualTo(RivalAdaptation.MaxBiasSlewPerRace + 1e-4f));
        }

        [Test]
        public void AMarginalSidePreference_BuysNoCoverage()
        {
            // A 55/45 player has no habit worth covering; acting as though they did is noise-chasing.
            var balanced = new PlayerStyleProfile { racesObserved = 30 };
            balanced.insidePreference.Add(21f, 19f);
            balanced.divePropensity.Add(20f, 20f);
            balanced.collisionRate.Add(5f, 600f);

            RivalMemoryProfile p = RivalAdaptation.ToProfile(balanced, Experienced(), Neutral());
            Assert.That(Mathf.Abs(p.CoverSideBias), Is.LessThan(0.05f));
        }

        // --- Personality actually differentiates --------------------------------------------------------

        [Test]
        public void PersonalitiesRespondDifferentlyToTheSameMemory()
        {
            // The same evidence must produce visibly different drivers — otherwise the whole grid converges
            // on one learned policy and the roster is decoration.
            PlayerStyleProfile style = Hostile();
            RivalMemory mem = Experienced();

            var byPersonality = new Dictionary<RivalPersonality, RivalMemoryProfile>();
            foreach (RivalPersonality p in System.Enum.GetValues(typeof(RivalPersonality)))
                byPersonality[p] = RivalAdaptation.ToProfile(style, mem, RivalLearningProfile.For(p));

            Assert.That(byPersonality[RivalPersonality.Cautious].SpaceEffective,
                Is.GreaterThan(byPersonality[RivalPersonality.Aggressive].SpaceEffective),
                "a Cautious driver should express a lesson as space more than an Aggressive one");

            Assert.That(byPersonality[RivalPersonality.Calculating].BaitEffective,
                Is.GreaterThan(byPersonality[RivalPersonality.Rookie].BaitEffective),
                "baiting is a skill — a Rookie should not out-bait a Calculating driver");

            Assert.That(byPersonality[RivalPersonality.Cautious].BaitEffective, Is.Zero,
                "a Cautious driver never baits");
        }

        [Test]
        public void ARookieOverreactsWhileUnsure()
        {
            // Overreaction is the low-confidence multiplier: it must show up when evidence is THIN and
            // wash out once everyone is sure.
            var thin = new PlayerStyleProfile { racesObserved = 3 };
            thin.insidePreference.Add(5f, 1f);
            thin.divePropensity.Add(5f, 1f);
            thin.collisionRate.Add(3f, 200f);
            thin.faultShare.Add(3f, 1f);

            RivalMemory mem = Experienced(3);

            // Compare the EFFECTIVE bias — what actually reaches the brain — not the raw one. Both raw
            // values sit on the same per-race slew cap, so they tie there by construction; the difference
            // between these two drivers is entirely in whether their confidence gate is open at all.
            float rookie = Mathf.Abs(RivalAdaptation.ToProfile(
                thin, mem, RivalLearningProfile.For(RivalPersonality.Rookie)).CoverSideEffective);
            float veteran = Mathf.Abs(RivalAdaptation.ToProfile(
                thin, mem, RivalLearningProfile.For(RivalPersonality.Veteran)).CoverSideEffective);

            Assert.That(rookie, Is.GreaterThan(veteran),
                "on thin evidence a Rookie should act where a Veteran still waits for more");
            Assert.That(veteran, Is.Zero, "a Veteran should form no opinion at all on three races");
        }

        [Test]
        public void ContactExposure_IsConvertedToSamplesNotCountedAsSeconds()
        {
            // Regression. RateStat's exposure is in SECONDS; feeding it straight to the confidence model
            // made two minutes of close racing read as 120 observations, saturating every rival's gate on
            // the first race and flattening the personality differences entirely. A single race's worth of
            // proximity must not by itself produce a confident rival.
            var style = new PlayerStyleProfile { racesObserved = 1 };
            style.collisionRate.Add(1f, 200f); // one contact across 200 s of close racing
            style.faultShare.Add(1f, 0f);

            RivalMemory mem = Experienced(2); // just past the hard floor
            RivalMemoryProfile p = RivalAdaptation.ToProfile(
                style, mem, RivalLearningProfile.For(RivalPersonality.Veteran));

            Assert.That(p.Confidence01, Is.LessThan(0.5f),
                "one race of proximity should not make a Veteran confident");
        }

        [Test]
        public void LearningProfile_EffectiveGainTapersWithConfidence()
        {
            RivalLearningProfile rookie = RivalLearningProfile.For(RivalPersonality.Rookie);
            Assert.That(rookie.EffectiveGain(0f), Is.GreaterThan(rookie.EffectiveGain(1f)));
            Assert.That(rookie.EffectiveGain(1f), Is.EqualTo(rookie.AdaptGain).Within(1e-4f));
        }

        // --- The nemesis budget (anti-blob) -------------------------------------------------------------

        [Test]
        public void NemesisBudget_LimitsHowManyRivalsCoverHard()
        {
            // Without this, a shared reputation eventually opens every rival's gate at once and the field
            // becomes one cautious blob — which makes the game EASIER, the opposite of the intent.
            var ids = new List<string>();
            var memories = new List<RivalMemory>();
            var profiles = new List<RivalMemoryProfile>();

            for (int i = 0; i < 7; i++)
            {
                ids.Add($"rival_{i}");
                RivalMemory m = Experienced();
                m.rivalId = ids[i];
                m.rivalry01 = 0.5f + i * 0.05f; // rival 6 has the most history
                memories.Add(m);
                profiles.Add(new RivalMemoryProfile
                {
                    Confidence01 = 1f,
                    CoverSideBias = RivalMemoryProfile.MaxCoverSideBias,
                    BaitBias = RivalMemoryProfile.MaxBaitBias,
                });
            }

            RivalAdaptation.ApplyNemesisBudget(ids, memories, profiles);

            int strong = 0;
            foreach (RivalMemoryProfile p in profiles)
                if (Mathf.Abs(p.CoverSideBias) > RivalAdaptation.NonNemesisCoverCap + 1e-4f) strong++;

            Assert.That(strong, Is.LessThanOrEqualTo(Mathf.CeilToInt(7 / 3f)),
                "too much of the field became a nemesis at once");
            Assert.That(strong, Is.GreaterThan(0), "somebody should still have your number");
        }

        [Test]
        public void NemesisBudget_KeepsTheRivalsWithTheMostHistory()
        {
            var ids = new List<string> { "a", "b", "c", "d", "e", "f" };
            var memories = new List<RivalMemory>();
            var profiles = new List<RivalMemoryProfile>();
            for (int i = 0; i < ids.Count; i++)
            {
                RivalMemory m = Experienced();
                m.rivalId = ids[i];
                m.rivalry01 = 0.3f + i * 0.1f; // "f" is the biggest grudge
                memories.Add(m);
                profiles.Add(new RivalMemoryProfile
                { Confidence01 = 1f, CoverSideBias = RivalMemoryProfile.MaxCoverSideBias });
            }

            RivalAdaptation.ApplyNemesisBudget(ids, memories, profiles);
            Assert.That(Mathf.Abs(profiles[5].CoverSideBias),
                Is.GreaterThan(RivalAdaptation.NonNemesisCoverCap), "the biggest grudge should survive the cut");
            Assert.That(Mathf.Abs(profiles[0].CoverSideBias),
                Is.LessThanOrEqualTo(RivalAdaptation.NonNemesisCoverCap + 1e-4f), "the least history should be capped");
        }

        [Test]
        public void NemesisBudget_IsDeterministic()
        {
            // Reproducibility matters for the headless-server promise: no random draw anywhere.
            List<RivalMemoryProfile> Run()
            {
                var ids = new List<string> { "a", "b", "c", "d" };
                var mems = new List<RivalMemory>();
                var profs = new List<RivalMemoryProfile>();
                foreach (string id in ids)
                {
                    RivalMemory m = Experienced();
                    m.rivalId = id;
                    m.rivalry01 = 0.6f; // deliberately tied, so the id tiebreak decides
                    mems.Add(m);
                    profs.Add(new RivalMemoryProfile { Confidence01 = 1f, CoverSideBias = 0.6f });
                }
                RivalAdaptation.ApplyNemesisBudget(ids, mems, profs);
                return profs;
            }

            List<RivalMemoryProfile> a = Run(), b = Run();
            for (int i = 0; i < a.Count; i++)
                Assert.That(b[i].CoverSideBias, Is.EqualTo(a[i].CoverSideBias), $"nondeterministic @ {i}");
        }

        [Test]
        public void NemesisBudget_HandlesDegenerateInput()
        {
            Assert.DoesNotThrow(() =>
            {
                RivalAdaptation.ApplyNemesisBudget(null, null, null);
                RivalAdaptation.ApplyNemesisBudget(new List<string>(), new List<RivalMemory>(),
                    new List<RivalMemoryProfile>());
            });
        }

        // --- Pace integrity -----------------------------------------------------------------------------

        [Test]
        public void NoMemory_EverProducesAPaceChange()
        {
            // Structural restatement of the rule the whole design rests on: the profile that crosses the
            // assembly boundary has no field capable of touching speed, and the brain proves it separately
            // (see RivalMemoryProfileTests.Memory_NeverTouchesPace). Here we assert the mapping never
            // manufactures a confidence above 1, which is the only lever that could amplify anything.
            foreach (RivalPersonality personality in System.Enum.GetValues(typeof(RivalPersonality)))
            {
                RivalMemoryProfile p = RivalAdaptation.ToProfile(
                    Hostile(), Experienced(500), RivalLearningProfile.For(personality));
                Assert.That(p.Confidence01, Is.InRange(0f, 1f), $"{personality}");
            }
        }

        // --- The headline: the whole loop, end to end ---------------------------------------------------

        [Test]
        public void EndToEnd_ADivebombingPlayerGetsTheInsideCovered()
        {
            // Drives the complete chain with no engine involvement:
            //   synthetic frames -> RaceObserver -> RivalMemoryStore -> RivalAdaptation -> BotBrain
            // and asserts the rival's defensive line actually MOVES relative to a rival with no memory.
            const int rivalKey = 1;
            var meta = new MetaProgress();

            // Race a career's worth of races in which the player always attacks down the same side and
            // makes contact doing it.
            for (int race = 1; race <= 12; race++)
            {
                var enc = new RivalEncounterSummary
                {
                    RivalKey = rivalKey,
                    ProximitySeconds = 90f,
                    Engagements = 6,
                    PlayerPassesOnRival = 5,
                    PlayerPassesInside = 5,
                    PlayerPassesOutside = 0,
                    PlayerPassesCompletedClean = 4,
                    PlayerDiveAttempts = 4,
                    PlayerDivesConverted = 3,
                    PlayerDiveScoreTotal = 4f * 0.8f,
                    ContactsPlayerFault = 2,
                    PlayerFaultSeverityTotal = 1.2f,
                    ContactSeverityTotal = 1.5f,
                    MeanSignedGapM = 5f,
                };

                var summary = new RaceObservationSummary
                {
                    RaceDurationS = 120f,
                    PlayerFinishPosition = 1,
                    FieldSize = 8,
                    Rivals = new[] { enc },
                };

                meta.careerRaces++;
                meta.playerStyle = RivalMemoryStore.GetStyle(meta.playerStyle, meta.careerRaces, meta.styleLastFoldedRace);
                RivalMemoryStore.FoldStyle(meta.playerStyle, summary);
                meta.styleLastFoldedRace = meta.careerRaces;
                RivalMemoryStore.Fold(meta.rivalMemories, Kes, enc, meta.careerRaces, race);

                // Slew the emitted biases forward each race, exactly as RunDirector does.
                RivalMemory m = RivalMemoryStore.Get(meta.rivalMemories, Kes, meta.careerRaces);
                RivalMemoryProfile emitted = RivalAdaptation.ToProfile(
                    meta.playerStyle, m, RivalLearningProfile.For(RivalPersonality.Veteran));
                m = RivalAdaptation.RememberBiases(m, emitted);
                for (int i = 0; i < meta.rivalMemories.Count; i++)
                    if (meta.rivalMemories[i].rivalId == Kes) meta.rivalMemories[i] = m;
            }

            // The model should now describe a committed, dirty, inside-attacking driver.
            Assert.That(meta.playerStyle.insidePreference.Signed, Is.GreaterThan(0.5f),
                "the model failed to learn the player's side preference");

            RivalMemory learned = RivalMemoryStore.Get(meta.rivalMemories, Kes, meta.careerRaces);
            RivalMemoryProfile profile = RivalAdaptation.ToProfile(
                meta.playerStyle, learned, RivalLearningProfile.For(RivalPersonality.Veteran));

            Assert.That(profile.Confidence01, Is.GreaterThan(0f), "a full career taught the rival nothing");
            Assert.That(profile.CoverSideEffective, Is.Not.Zero, "no cover bias emerged from a clear habit");

            // ...and it must reach the brain and change the defensive line.
            RacingLine line = TestCircuit();
            BotSensors defending = DefendingSensors(line);
            var skill = new BotSkill
            {
                CornerSpeedMult = 0.98f, Aggression = 0.95f, LookaheadM = 12f,
                Defensiveness = 0.5f, OvertakeBoldness = 0.5f, Consistency = 0.6f,
            };

            var naive = new BotBrain(line, skill);
            var remembers = new BotBrain(line, skill);
            remembers.SetPlayerMemory(profile);

            bool diverged = false;
            for (int i = 0; i < 600; i++)
            {
                VehicleInput a = naive.Step(0.02f, defending);
                VehicleInput b = remembers.Step(0.02f, defending);
                if (!Mathf.Approximately(a.Steer, b.Steer)) { diverged = true; break; }
            }
            Assert.That(diverged, Is.True,
                "the learned profile never reached the defensive line — the loop is not closed");
        }

        // --- Fixtures -----------------------------------------------------------------------------------

        /// <summary>Rounded-rectangle circuit matching RaceTest's shape (see RaceObserverTests).</summary>
        private static RacingLine TestCircuit()
        {
            const float y = 0.25f, halfX = 110f, halfZ = 70f, r = 20f;
            float ax = halfX - r, az = halfZ - r, midX = ax * 0.5f;
            var pts = new List<Vector3> { new Vector3(0f, y, -halfZ), new Vector3(midX, y, -halfZ) };
            void Arc(Vector3 c, float from, float to)
            {
                for (int i = 0; i <= 3; i++)
                {
                    float a = Mathf.Deg2Rad * Mathf.Lerp(from, to, i / 3f);
                    pts.Add(c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                }
            }
            Arc(new Vector3(ax, y, -az), -90f, 0f);
            pts.Add(new Vector3(halfX, y, 0f));
            Arc(new Vector3(ax, y, az), 0f, 90f);
            pts.Add(new Vector3(midX, y, halfZ));
            pts.Add(new Vector3(0f, y, halfZ));
            pts.Add(new Vector3(-midX, y, halfZ));
            Arc(new Vector3(-ax, y, az), 90f, 180f);
            pts.Add(new Vector3(-halfX, y, 0f));
            Arc(new Vector3(-ax, y, -az), 180f, 270f);
            pts.Add(new Vector3(-midX, y, -halfZ));
            return new RacingLine(pts);
        }

        /// <summary>A closing follower and nothing ahead, so the brain reaches its defensive branch.</summary>
        private static BotSensors DefendingSensors(RacingLine line)
        {
            Vector3 fwd = line.DirectionAt(20f);
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            return new BotSensors
            {
                Position = line.PointAt(20f),
                Forward = fwd,
                Velocity = fwd * 30f,
                DrivenWheelSlip = 0f,
                Neighbors = new[]
                {
                    new BotNeighbor
                    {
                        RelativePosition = -fwd * 6f + right * 1.2f,
                        Velocity = fwd * 34f,
                        IsPlayer = true,
                    },
                },
                NeighborCount = 1,
            };
        }
    }
}
