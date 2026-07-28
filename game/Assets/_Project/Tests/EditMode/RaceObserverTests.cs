using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Race;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the race observation layer: the pure core that turns per-step car state into attributed
    /// events about what the PLAYER did to each rival.
    ///
    /// Every test drives synthetic <see cref="CarFrame"/> arrays through a hand-rolled 50 Hz loop — the same
    /// pattern <c>VehicleSimStepTests</c> uses on the physics core — so the detectors are exercised with no
    /// scene, no rigidbodies and no engine loop.
    ///
    /// The traps these pin are the ones a naive observer falls straight into: lapping counted as an
    /// overtake, recovery teleports counted as overtakes, one scrape counted as six collisions with
    /// contradictory blame, and a clean inside pass counted as a divebomb.
    /// </summary>
    public class RaceObserverTests : TestBase
    {
        private const float Dt = 0.02f;
        private const int RivalKey = 1;

        /// <summary>
        /// A rounded-rectangle circuit built the same way <c>RaceTrackBuilder.BuildCenterlineWaypoints</c>
        /// builds the three shipped tracks: four arc corners of an explicit radius joined by real straights.
        ///
        /// Deliberately NOT the 4-point square other bot fixtures use. A closed Catmull-Rom through four
        /// corner points is very nearly a circle — measured, its curvature only falls from 0.041 to 0.0065
        /// between "corners", so it has no straights at all and reads as one endless bend. Corner detection
        /// tested against that shape would be tested against something the game never loads.
        /// Defaults mirror RaceTest: 110 x 70 half-extents, 20 m corners.
        /// </summary>
        private static RacingLine Circuit(float halfX = 110f, float halfZ = 70f, float radius = 20f)
        {
            const float y = 0.25f;
            float ax = halfX - radius;
            float az = halfZ - radius;
            float midX = ax * 0.5f;

            var pts = new List<Vector3>
            {
                new Vector3(0f, y, -halfZ),
                new Vector3(midX, y, -halfZ),
            };
            Arc(pts, new Vector3(ax, y, -az), radius, -90f, 0f);
            pts.Add(new Vector3(halfX, y, 0f));
            Arc(pts, new Vector3(ax, y, az), radius, 0f, 90f);
            pts.Add(new Vector3(midX, y, halfZ));
            pts.Add(new Vector3(0f, y, halfZ));
            pts.Add(new Vector3(-midX, y, halfZ));
            Arc(pts, new Vector3(-ax, y, az), radius, 90f, 180f);
            pts.Add(new Vector3(-halfX, y, 0f));
            Arc(pts, new Vector3(-ax, y, -az), radius, 180f, 270f);
            pts.Add(new Vector3(-midX, y, -halfZ));
            return new RacingLine(pts);
        }

        private static void Arc(List<Vector3> pts, Vector3 centre, float r, float fromDeg, float toDeg)
        {
            const int segments = 3;
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(fromDeg, toDeg, i / (float)segments);
                pts.Add(centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
        }

        private static CarFrame Car(int key, float dist, float lateral, float speed,
            float throttle = 1f, float brake = 0f, float progress = 0f) => new CarFrame
            {
                Key = key,
                TotalDistanceM = dist,
                ProgressM = progress,
                LateralM = lateral,
                SpeedMps = speed,
                Throttle = throttle,
                Brake = brake,
                Racing = true,
            };

        private static RaceObserver NewObserver()
        {
            var o = new RaceObserver();
            o.Reset();
            o.RegisterRival(RivalKey);
            return o;
        }

        /// <summary>
        /// Runs a straight-line overtake: the player closes from <paramref name="startGap"/> behind to
        /// clearly ahead, holding <paramref name="lateral"/> metres off the rival's line.
        /// </summary>
        private static RivalEncounterSummary RunOvertake(float startGap, float lateral,
            float closeRate = 4f, float seconds = 12f, float rivalDist = 0f)
        {
            RaceObserver o = NewObserver();
            float t = 0f;
            float gap = startGap; // negative = player behind
            var frames = new CarFrame[2];

            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                gap += closeRate * Dt;
                float rd = rivalDist + 30f * t;
                frames[0] = Car(0, rd + gap, lateral, 34f);
                frames[1] = Car(RivalKey, rd, 0f, 30f);
                o.Observe(t, Dt, frames, 2);
                t += Dt;
            }
            return o.Summarize(1, 8).Rivals[0];
        }

        // --- Pass detection ----------------------------------------------------------------------------

        [Test]
        public void CloseOvertake_IsRecordedOnce()
        {
            RivalEncounterSummary s = RunOvertake(startGap: -10f, lateral: 2.5f);
            Assert.That(s.PlayerPassesOnRival, Is.EqualTo(1), "expected exactly one pass");
            Assert.That(s.RivalPassesOnPlayer, Is.Zero);
            Assert.That(s.Engagements, Is.GreaterThan(0));
        }

        [Test]
        public void Lapping_IsNotAPass()
        {
            // The same distance sign-flip, but 40 m apart — that is lapping traffic, not an overtake.
            // Without the proximity gate every lapped car would read as a heroic pass.
            RaceObserver o = NewObserver();
            float t = 0f;
            var frames = new CarFrame[2];
            for (int i = 0; i < 600; i++)
            {
                float rd = 30f * t;
                frames[0] = Car(0, rd - 40f + 8f * t, 0f, 38f);
                frames[1] = Car(RivalKey, rd, 0f, 30f);
                o.Observe(t, Dt, frames, 2);
                t += Dt;
            }
            Assert.That(o.Summarize(1, 8).Rivals[0].PlayerPassesOnRival, Is.Zero);
        }

        [Test]
        public void SameLanePass_WithoutLateralSeparation_DoesNotCommit()
        {
            // Driving through a car rather than around it is not a pass. Requires genuine lateral
            // separation — a car is ~1.8 m wide, so 0.3 m of offset is the same piece of road.
            RivalEncounterSummary s = RunOvertake(startGap: -10f, lateral: 0.3f);
            Assert.That(s.PlayerPassesOnRival, Is.Zero);
        }

        [Test]
        public void TeleportJump_IsNotAPass()
        {
            // BotDriver's flip-recovery and reset-to-track both teleport cars, and ShuffleGrid moves the
            // whole field at the start. A jump beyond MaxPlausibleStepM must be dropped, not celebrated.
            RaceObserver o = NewObserver();
            float t = 0f;
            var frames = new CarFrame[2];
            for (int i = 0; i < 300; i++)
            {
                float rd = 30f * t;
                float playerDist = i < 150 ? rd - 8f : rd + 8f; // instantaneous 16 m jump
                frames[0] = Car(0, playerDist, 2.5f, 32f);
                frames[1] = Car(RivalKey, rd, 0f, 30f);
                o.Observe(t, Dt, frames, 2);
                t += Dt;
            }
            Assert.That(o.Summarize(1, 8).Rivals[0].PlayerPassesOnRival, Is.Zero);
        }

        [Test]
        public void RivalOvertakingPlayer_IsAttributedToTheRival()
        {
            RaceObserver o = NewObserver();
            float t = 0f;
            var frames = new CarFrame[2];
            float gap = 10f; // player ahead, being caught
            for (int i = 0; i < 600; i++)
            {
                gap -= 4f * Dt;
                float pd = 30f * t;
                frames[0] = Car(0, pd, 0f, 30f);
                frames[1] = Car(RivalKey, pd - gap, 2.5f, 34f);
                o.Observe(t, Dt, frames, 2);
                t += Dt;
            }
            RivalEncounterSummary s = o.Summarize(2, 8).Rivals[0];
            Assert.That(s.RivalPassesOnPlayer, Is.EqualTo(1));
            Assert.That(s.PlayerPassesOnRival, Is.Zero);
        }

        [Test]
        public void ParkedCars_ProduceNoEvents()
        {
            RaceObserver o = NewObserver();
            var frames = new[] { Car(0, 0f, 0f, 0f), Car(RivalKey, -5f, 0f, 0f) };
            for (int i = 0; i < 300; i++) o.Observe(i * Dt, Dt, frames, 2);
            RivalEncounterSummary s = o.Summarize(1, 8).Rivals[0];
            Assert.That(s.PlayerPassesOnRival, Is.Zero);
            Assert.That(s.Engagements, Is.Zero, "cars below racing speed are not contesting anything");
        }

        // --- Contact attribution -----------------------------------------------------------------------

        [Test]
        public void BothCarsReportingOneCollision_CountsOnce()
        {
            // PhysX fires OnCollisionEnter on BOTH cars for a single collision, each with complementary
            // blame. Without de-duplication every contact is counted twice with contradictory fault.
            RaceObserver o = NewObserver();
            o.RecordContact(5.00f, RivalKey, 0.6f, 0.9f);  // player's own callback: player at fault
            o.RecordContact(5.005f, RivalKey, 0.6f, 0.1f); // rival's callback, complement of the same hit

            RivalEncounterSummary s = o.Summarize(1, 8).Rivals[0];
            Assert.That(s.ContactsPlayerFault + s.ContactsRivalFault + s.ContactsMutual, Is.EqualTo(1));
            Assert.That(s.ContactsPlayerFault, Is.EqualTo(1), "the first (decisive) report should win");
        }

        [Test]
        public void RepeatedScrapeContacts_AreDebounced()
        {
            RaceObserver o = NewObserver();
            for (int i = 0; i < 6; i++) o.RecordContact(5f + i * 0.05f, RivalKey, 0.5f, 0.9f);
            RivalEncounterSummary s = o.Summarize(1, 8).Rivals[0];
            Assert.That(s.ContactsPlayerFault, Is.EqualTo(1), "one sidewipe is one incident, not six");
        }

        [Test]
        public void SeparateContacts_AreCountedSeparately()
        {
            RaceObserver o = NewObserver();
            o.RecordContact(5f, RivalKey, 0.5f, 0.9f);
            o.RecordContact(9f, RivalKey, 0.5f, 0.1f);
            RivalEncounterSummary s = o.Summarize(1, 8).Rivals[0];
            Assert.That(s.ContactsPlayerFault, Is.EqualTo(1));
            Assert.That(s.ContactsRivalFault, Is.EqualTo(1));
        }

        [Test]
        public void LightTaps_AreBelowTheSeverityGate()
        {
            RaceObserver o = NewObserver();
            o.RecordContact(5f, RivalKey, 0.01f, 1f);
            RivalEncounterSummary s = o.Summarize(1, 8).Rivals[0];
            Assert.That(s.ContactsPlayerFault + s.ContactsRivalFault + s.ContactsMutual, Is.Zero);
        }

        [Test]
        public void EvenBlame_ReadsAsMutual_NotAsSomeonesFault()
        {
            RaceObserver o = NewObserver();
            o.RecordContact(5f, RivalKey, 0.5f, 0.5f);
            RivalEncounterSummary s = o.Summarize(1, 8).Rivals[0];
            Assert.That(s.ContactsMutual, Is.EqualTo(1));
            Assert.That(s.ContactsPlayerFault, Is.Zero);
            Assert.That(s.ContactsRivalFault, Is.Zero);
        }

        [Test]
        public void ContactForAnUnregisteredRival_IsIgnored()
        {
            RaceObserver o = NewObserver();
            o.RecordContact(5f, 99, 0.9f, 1f);
            Assert.That(o.Summarize(1, 8).Rivals[0].ContactsPlayerFault, Is.Zero);
        }

        // --- Exposure and summary ----------------------------------------------------------------------

        [Test]
        public void ProximitySeconds_AccumulateOnlyWhenClose()
        {
            RaceObserver o = NewObserver();
            var near = new[] { Car(0, 5f, 0f, 30f), Car(RivalKey, 0f, 0f, 30f) };
            var far = new[] { Car(0, 400f, 0f, 30f), Car(RivalKey, 0f, 0f, 30f) };

            for (int i = 0; i < 100; i++) o.Observe(i * Dt, Dt, near, 2);
            float afterNear = o.Summarize(1, 8).Rivals[0].ProximitySeconds;
            for (int i = 0; i < 100; i++) o.Observe(2f + i * Dt, Dt, far, 2);
            float afterFar = o.Summarize(1, 8).Rivals[0].ProximitySeconds;

            Assert.That(afterNear, Is.EqualTo(100 * Dt).Within(1e-3f));
            Assert.That(afterFar, Is.EqualTo(afterNear).Within(1e-3f),
                "distant laps must not inflate the exposure denominator");
        }

        [Test]
        public void MeanSignedGap_TracksWhoIsAhead()
        {
            RaceObserver o = NewObserver();
            var frames = new[] { Car(0, 10f, 0f, 30f), Car(RivalKey, 0f, 0f, 30f) };
            for (int i = 0; i < 200; i++) o.Observe(i * Dt, Dt, frames, 2);
            Assert.That(o.Summarize(1, 8).Rivals[0].MeanSignedGapM, Is.EqualTo(10f).Within(0.5f));
        }

        [Test]
        public void Summarize_IsIdempotentAndSafeBeforeRaceEnd()
        {
            RaceObserver o = NewObserver();
            var frames = new[] { Car(0, 5f, 0f, 30f), Car(RivalKey, 0f, 0f, 30f) };
            for (int i = 0; i < 50; i++) o.Observe(i * Dt, Dt, frames, 2);

            RivalEncounterSummary a = o.Summarize(3, 8).Rivals[0];
            RivalEncounterSummary b = o.Summarize(3, 8).Rivals[0];
            Assert.That(b.ProximitySeconds, Is.EqualTo(a.ProximitySeconds));
            Assert.That(b.Engagements, Is.EqualTo(a.Engagements));
        }

        [Test]
        public void Reset_ClearsEverything()
        {
            RaceObserver o = NewObserver();
            o.RecordContact(5f, RivalKey, 0.9f, 1f);
            o.Reset();
            Assert.That(o.Summarize(1, 8).Rivals, Is.Empty);
        }

        [Test]
        public void MissingPlayerFrame_IsHandled()
        {
            RaceObserver o = NewObserver();
            var frames = new[] { Car(RivalKey, 0f, 0f, 30f), Car(2, 5f, 0f, 30f) };
            Assert.DoesNotThrow(() => { for (int i = 0; i < 50; i++) o.Observe(i * Dt, Dt, frames, 2); });
            Assert.That(o.Summarize(1, 8).Rivals[0].Engagements, Is.Zero);
        }

        [Test]
        public void DegenerateInputs_DoNotThrow()
        {
            RaceObserver o = NewObserver();
            Assert.DoesNotThrow(() =>
            {
                o.Observe(0f, Dt, null, 2);
                o.Observe(0f, Dt, new CarFrame[0], 0);
                o.Observe(0f, 0f, new[] { Car(0, 0f, 0f, 30f) }, 1);
                o.Observe(0f, Dt, new[] { Car(0, 0f, 0f, 30f) }, 99); // count past the array
            });
        }

        // --- Corner table (the frame every side/braking metric depends on) ------------------------------

        [Test]
        public void CornerTable_FindsTheCornersOfACircuit()
        {
            CornerTable table = CornerTable.Build(Circuit());
            Assert.That(table.Count, Is.EqualTo(4), "a rounded-rectangle circuit has exactly four corners");
        }

        [Test]
        public void CornerTable_SignsEveryCornerConsistently()
        {
            // The circuit is wound one way, so every corner must bend the same way. A sign that flipped
            // between corners would mean inside/outside was being read off curvature noise — and inside is
            // the whole basis of the side-preference metric.
            CornerTable table = CornerTable.Build(Circuit());
            float first = table.Corners[0].Sign;
            foreach (Corner c in table.Corners)
            {
                Assert.That(Mathf.Abs(c.Sign), Is.EqualTo(1f), "every corner must have a definite hand");
                Assert.That(c.Sign, Is.EqualTo(first));
            }
        }

        [Test]
        public void CornerTable_ApexLiesInsideItsCorner()
        {
            foreach (Corner c in CornerTable.Build(Circuit()).Corners)
            {
                Assert.That(c.ApexM, Is.InRange(c.EntryM, c.ExitM));
                Assert.That(c.LengthM, Is.GreaterThanOrEqualTo(CornerTable.MinLengthM));
            }
        }

        [Test]
        public void CornerTable_ReportsStraightsAsStraights()
        {
            CornerTable table = CornerTable.Build(Circuit());
            Corner first = table.Corners[0];
            // Look backwards from just after a corner exit with no lookahead: mid-straight should find
            // nothing, which is a real answer — a pass there carries no side information.
            float midStraight = first.ExitM + (table.Corners.Count > 1
                ? (table.Corners[1].EntryM - first.ExitM) * 0.5f
                : 20f);
            Assert.That(table.TryGetCornerAt(midStraight, 1f, out _), Is.False);
        }

        [Test]
        public void CornerTable_WorksOnAllThreeShippedTrackShapes()
        {
            // The detection thresholds have to hold on the geometry the game actually loads, not just on a
            // convenient fixture. These are RaceTrackBuilder's three layouts (centre half-extents = the mean
            // of its outer/inner pair) — including Speedway, whose 30 m sweepers are the shallowest corners
            // in the game and therefore the ones nearest the EnterKappa threshold.
            (string name, float halfX, float halfZ, float radius)[] layouts =
            {
                ("RaceTest", 110f, 70f, 20f),
                ("RaceGauntlet", 119f, 79f, 22f),
                ("RaceSpeedway", 160f, 55f, 30f),
            };

            foreach (var l in layouts)
            {
                CornerTable table = CornerTable.Build(Circuit(l.halfX, l.halfZ, l.radius));
                Assert.That(table.Count, Is.EqualTo(4), $"{l.name} should resolve four corners");
                foreach (Corner c in table.Corners)
                {
                    Assert.That(Mathf.Abs(c.Sign), Is.EqualTo(1f), $"{l.name} corner has no hand");
                    Assert.That(c.ApexM, Is.InRange(c.EntryM, c.ExitM), $"{l.name} apex outside its corner");
                }
            }
        }

        [Test]
        public void CornerTable_HandlesADegenerateLine()
        {
            Assert.That(CornerTable.Build(null).Count, Is.Zero);
            Assert.That(CornerTable.Empty.TryGetCornerAt(0f, 100f, out _), Is.False);
        }

        [Test]
        public void SignedCurvature_MatchesUnsignedInMagnitude()
        {
            RacingLine line = Circuit();
            for (float d = 0f; d < line.TotalLength; d += 5f)
            {
                float unsigned = line.CurvatureAt(d, CornerTable.HalfWindowM);
                float signed = line.SignedCurvatureAt(d, CornerTable.HalfWindowM);
                Assert.That(Mathf.Abs(signed), Is.EqualTo(unsigned).Within(1e-4f), $"@ {d}");
            }
        }

        // --- Fault split symmetry ----------------------------------------------------------------------

        [Test]
        public void Aggressorness_IsSymmetric()
        {
            // THE property the whole attribution model rests on: both cars must derive the same split, or
            // one collision would be recorded as two incidents that disagree about who was to blame.
            var rng = new System.Random(1234);
            for (int i = 0; i < 200; i++)
            {
                Vector3 aPos = Rand(rng, 20f), bPos = Rand(rng, 20f);
                Vector3 aFwd = Rand(rng, 1f).normalized, bFwd = Rand(rng, 1f).normalized;
                Vector3 aVel = Rand(rng, 30f), bVel = Rand(rng, 30f);
                if ((bPos - aPos).sqrMagnitude < 1e-4f) continue;

                float ab = VehicleCombat.Aggressorness(aPos, aFwd, aVel, bPos, bFwd, bVel);
                float ba = VehicleCombat.Aggressorness(bPos, bFwd, bVel, aPos, aFwd, aVel);
                Assert.That(ab + ba, Is.EqualTo(1f).Within(1e-4f), $"asymmetric split @ {i}");
            }
        }

        [Test]
        public void Aggressorness_BlamesTheCarThatDroveIn()
        {
            // A rear-ender: A behind, facing and moving forward into a stationary B.
            float blame = VehicleCombat.Aggressorness(
                Vector3.zero, Vector3.forward, Vector3.forward * 25f,
                Vector3.forward * 4f, Vector3.forward, Vector3.zero);
            Assert.That(blame, Is.GreaterThan(0.9f), "the car that drove in is the aggressor");
        }

        [Test]
        public void Aggressorness_IsNeutralForDegeneratePairs()
        {
            Assert.That(VehicleCombat.Aggressorness(
                Vector3.zero, Vector3.forward, Vector3.zero,
                Vector3.zero, Vector3.forward, Vector3.zero), Is.EqualTo(0.5f), "co-located");

            Assert.That(VehicleCombat.Aggressorness(
                Vector3.zero, Vector3.forward, Vector3.zero,
                Vector3.forward * 5f, Vector3.forward, Vector3.zero), Is.EqualTo(0.5f), "both stationary");
        }

        private static Vector3 Rand(System.Random rng, float scale) => new Vector3(
            (float)(rng.NextDouble() * 2 - 1) * scale,
            0f,
            (float)(rng.NextDouble() * 2 - 1) * scale);
    }
}
