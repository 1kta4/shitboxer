using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Meta;
using Shitboxer.Race;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the persistent memory model: the decayed estimators, the career-long store, and the fold
    /// from one race's observations into both tiers.
    ///
    /// The properties pinned here are the ones the whole design rests on, and each fails silently and
    /// invisibly in play if it breaks:
    ///
    ///  - decay preserves the point estimate EXACTLY and only lowers confidence, so forgetting means
    ///    becoming less sure rather than becoming wrong;
    ///  - the accumulator cap — not the half-life — is what actually bounds how fast the model can adapt;
    ///  - a memory below the minimum sample count has no vote at all;
    ///  - the profile round-trips through JsonUtility, including a save written before any of this existed.
    /// </summary>
    public class RivalMemoryStoreTests : TestBase
    {
        private const string Kes = "vera_kestrel";
        private const string Dex = "dex_karro";

        private static RivalEncounterSummary Encounter(int key = 1, float proximity = 60f) =>
            new RivalEncounterSummary
            {
                RivalKey = key,
                ProximitySeconds = proximity,
                Engagements = 4,
                PlayerPassesOnRival = 2,
                PlayerPassesInside = 2,
                PlayerPassesCompletedClean = 2,
                MeanSignedGapM = 10f,
                ClosestApproachM = 1.5f,
            };

        // --- Decay: the load-bearing properties ---------------------------------------------------------

        [Test]
        public void Decay_PreservesTheRawEvidenceRatio()
        {
            // THE property. Decay must never reinterpret what was actually observed — it only reduces how
            // much of it is left. If this broke, a rival that hadn't raced you in a while would not merely
            // be unsure about you, it would be confidently WRONG about you.
            var s = new BetaStat();
            s.Add(9f, 3f);
            float rawBefore = s.a / (s.a + s.b);

            foreach (int races in new[] { 1, 3, 8, 20, 100 })
            {
                BetaStat d = RivalMemoryMath.Decay(s, RivalMemoryMath.StyleHalfLifeRaces, races, 1e9f);
                Assert.That(d.a / (d.a + d.b), Is.EqualTo(rawBefore).Within(1e-4f),
                    $"raw evidence ratio moved after {races} races");
                Assert.That(d.N, Is.LessThan(s.N), $"confidence did not fall after {races} races");
            }
        }

        [Test]
        public void Decay_RelaxesTheReadEstimateTowardNeutral()
        {
            // The fixed prior deliberately does NOT decay with the evidence, so as evidence thins it counts
            // for proportionally more and the read estimate drifts back toward 0.5. That is the desired
            // behaviour, not a leak: a rival who hasn't seen you in thirty races should be treating you as
            // an unknown quantity again, not still acting on a season-old read.
            var s = new BetaStat();
            s.Add(9f, 3f);
            float prev = s.P;
            Assert.That(prev, Is.GreaterThan(BetaStat.Neutral));

            foreach (int races in new[] { 4, 8, 16, 40, 120 })
            {
                float p = RivalMemoryMath.Decay(s, RivalMemoryMath.StyleHalfLifeRaces, races, 1e9f).P;
                Assert.That(p, Is.LessThanOrEqualTo(prev + 1e-4f), $"estimate moved AWAY from neutral @ {races}");
                Assert.That(p, Is.GreaterThanOrEqualTo(BetaStat.Neutral - 1e-4f), $"overshot neutral @ {races}");
                prev = p;
            }
            Assert.That(prev, Is.EqualTo(BetaStat.Neutral).Within(0.02f),
                "a long-unseen memory should have faded back to no opinion");
        }

        [Test]
        public void Decay_PreservesMeansAndRatesExactly()
        {
            // MeanStat and RateStat carry no prior, so for them decay genuinely is estimate-preserving.
            var m = new MeanStat();
            m.Add(0.8f, 4f);
            m.Add(0.4f, 6f);
            float meanBefore = m.Mean;

            var r = new RateStat();
            r.Add(5f, 120f);
            float rateBefore = r.Rate;

            foreach (int races in new[] { 1, 5, 25 })
            {
                Assert.That(RivalMemoryMath.Decay(m, 8f, races, 1e9f).Mean,
                    Is.EqualTo(meanBefore).Within(1e-4f), $"mean moved @ {races}");
                Assert.That(RivalMemoryMath.Decay(r, 8f, races, 1e9f).Rate,
                    Is.EqualTo(rateBefore).Within(1e-4f), $"rate moved @ {races}");
            }
        }

        [Test]
        public void Decay_HalvesEvidenceOverOneHalfLife()
        {
            var s = new BetaStat();
            s.Add(6f, 4f);
            BetaStat d = RivalMemoryMath.Decay(s, 8f, 8, 1e9f);
            Assert.That(d.N, Is.EqualTo(5f).Within(1e-3f));
        }

        [Test]
        public void Decay_IsExactIdentityAtZeroRacesElapsed()
        {
            var s = new BetaStat();
            s.Add(7f, 2f);
            BetaStat d = RivalMemoryMath.Decay(s, 8f, 0, 1e9f);
            Assert.That(d.a, Is.EqualTo(s.a));
            Assert.That(d.b, Is.EqualTo(s.b));
        }

        [Test]
        public void AccumulatorCap_BoundsTheAdaptationWindow()
        {
            // Without a cap, equilibrium is samplesPerRace/(1-lambda) — about 120 samples at 10/race on an
            // 8-race half-life — and a model holding 120 samples barely moves however fresh the evidence.
            // The cap is what keeps the model responsive, so it is the thing worth testing, not the
            // half-life on its own.
            var s = new BetaStat();
            for (int race = 0; race < 60; race++)
            {
                s = RivalMemoryMath.Decay(s, RivalMemoryMath.StyleHalfLifeRaces, 1, RivalMemoryMath.StyleCap);
                s.Add(8f, 2f); // 10 observations every race, forever
            }
            Assert.That(s.N, Is.LessThanOrEqualTo(RivalMemoryMath.StyleCap * 1.5f),
                "the accumulator ran away, so the model can no longer adapt to a change of style");
        }

        [Test]
        public void CappedDecay_StillPreservesTheRawEvidenceRatio()
        {
            // The cap rescales a and b proportionally, so trimming an over-full accumulator must not
            // change what the evidence says either — only how much of it is retained.
            var s = new BetaStat();
            s.Add(80f, 20f); // well past the cap
            float rawBefore = s.a / (s.a + s.b);

            BetaStat d = RivalMemoryMath.Decay(s, 8f, 1, RivalMemoryMath.StyleCap);
            Assert.That(d.a / (d.a + d.b), Is.EqualTo(rawBefore).Within(1e-3f));
            Assert.That(d.N, Is.LessThanOrEqualTo(RivalMemoryMath.StyleCap + 1e-3f));
        }

        [Test]
        public void AStyleFlip_IsPickedUpWithinAFewRaces()
        {
            // The point of bounding accumulation: a player who genuinely changes style must be noticed
            // within a season, not eventually. Twenty races of inside, then all outside.
            var s = new BetaStat();
            for (int i = 0; i < 20; i++)
            {
                s = RivalMemoryMath.Decay(s, RivalMemoryMath.StyleHalfLifeRaces, 1, RivalMemoryMath.StyleCap);
                s.Add(6f, 0f);
            }
            Assert.That(s.Signed, Is.GreaterThan(0.5f), "should read as a committed inside-goer");

            int racesToFlip = 0;
            while (s.Signed > 0f && racesToFlip < 30)
            {
                s = RivalMemoryMath.Decay(s, RivalMemoryMath.StyleHalfLifeRaces, 1, RivalMemoryMath.StyleCap);
                s.Add(0f, 6f);
                racesToFlip++;
            }
            Assert.That(racesToFlip, Is.LessThanOrEqualTo(8),
                $"took {racesToFlip} races to notice a total style change — too slow to feel like learning");
        }

        // --- Confidence ---------------------------------------------------------------------------------

        [Test]
        public void Confidence_IsZeroBelowTheMinimumSampleCount()
        {
            // One fluke race must never change how the field races you.
            for (int n = 0; n < RivalMemoryMath.MinSamples; n++)
                Assert.That(RivalMemoryMath.Confidence(n), Is.Zero, $"n={n}");
            Assert.That(RivalMemoryMath.Confidence(RivalMemoryMath.MinSamples), Is.GreaterThan(0f));
        }

        [Test]
        public void Confidence_RisesAndSaturates()
        {
            float prev = 0f;
            foreach (float n in new[] { 3f, 6f, 12f, 40f, 200f })
            {
                float c = RivalMemoryMath.Confidence(n);
                Assert.That(c, Is.GreaterThan(prev));
                Assert.That(c, Is.InRange(0f, 1f));
                prev = c;
            }
            Assert.That(RivalMemoryMath.Confidence(10000f), Is.EqualTo(1f).Within(0.01f));
        }

        [Test]
        public void Gate_IsClosedBelowLoAndOpenAboveHi()
        {
            Assert.That(RivalMemoryMath.Gate(0.1f, 0.3f, 0.75f), Is.Zero);
            Assert.That(RivalMemoryMath.Gate(0.9f, 0.3f, 0.75f), Is.EqualTo(1f));
            Assert.That(RivalMemoryMath.Gate(0.5f, 0.3f, 0.75f), Is.InRange(0f, 1f));
        }

        [Test]
        public void Deadband_IgnoresAMarginalPreference()
        {
            // A 55/45 player has no preference worth covering; treating them as though they did is how a
            // model ends up chasing noise and oscillating.
            Assert.That(RivalMemoryMath.Deadband(0.1f, 0.25f), Is.Zero);
            Assert.That(RivalMemoryMath.Deadband(-0.2f, 0.25f), Is.Zero);
            Assert.That(RivalMemoryMath.Deadband(0.8f, 0.25f), Is.GreaterThan(0f));
            Assert.That(RivalMemoryMath.Deadband(-0.8f, 0.25f), Is.LessThan(0f));
        }

        [Test]
        public void SlewBias_CannotFlipSignInOneStep()
        {
            float bias = 0.6f;
            float flipped = RivalMemoryMath.SlewBias(bias, -0.6f, 0.12f);
            Assert.That(flipped, Is.GreaterThan(0f), "a bias must not invert in a single race");
            Assert.That(flipped, Is.EqualTo(0.48f).Within(1e-4f));
        }

        [Test]
        public void SlewBias_TakesSeveralRacesToCrossZero()
        {
            float bias = 0.6f;
            int races = 0;
            while (bias > 0f && races < 50) { bias = RivalMemoryMath.SlewBias(bias, -0.6f, 0.12f); races++; }
            Assert.That(races, Is.GreaterThanOrEqualTo(5), "behaviour flipped too fast — invites oscillation");
        }

        // --- Store --------------------------------------------------------------------------------------

        [Test]
        public void UnknownRival_ReadsAsFreshAndNeutral()
        {
            var list = new List<RivalMemory>();
            RivalMemory m = RivalMemoryStore.Get(list, Kes, careerRaces: 10);
            Assert.That(m.encounters, Is.Zero);
            Assert.That(m.rivalry01, Is.EqualTo(RivalMemory.NeutralRivalry));
            Assert.That(m.HasMetPlayer, Is.False);
            Assert.That(m.PersonalGate, Is.Zero);
        }

        [Test]
        public void FoldThenGet_RoundTrips()
        {
            var list = new List<RivalMemory>();
            RivalMemoryStore.Fold(list, Kes, Encounter(), careerRaces: 1, timestamp: 1000L);

            RivalMemory m = RivalMemoryStore.Get(list, Kes, careerRaces: 1);
            Assert.That(m.rivalId, Is.EqualTo(Kes));
            Assert.That(m.encounters, Is.EqualTo(1));
            Assert.That(m.proximitySeconds, Is.EqualTo(60f).Within(1e-3f));
            Assert.That(m.lastSeenTimestamp, Is.EqualTo(1000L));
        }

        [Test]
        public void ARivalNotSeenForManyRaces_ForgetsTheGrudge()
        {
            var list = new List<RivalMemory>();
            var hostile = Encounter();
            hostile.ContactsPlayerFault = 3;
            hostile.PlayerFaultSeverityTotal = 2.5f;
            RivalMemoryStore.Fold(list, Kes, hostile, careerRaces: 1, timestamp: 0L);

            float fresh = RivalMemoryStore.Get(list, Kes, careerRaces: 1).rivalry01;
            float later = RivalMemoryStore.Get(list, Kes, careerRaces: 31).rivalry01;

            Assert.That(fresh, Is.GreaterThan(RivalMemory.NeutralRivalry), "the incident should register");
            Assert.That(later, Is.LessThan(fresh), "30 races later it should have cooled");
            Assert.That(later, Is.EqualTo(RivalMemory.NeutralRivalry).Within(0.05f),
                "a grudge must relax toward indifference, not persist forever");
        }

        [Test]
        public void CleanRacingAndYielding_CoolARival()
        {
            // The negative term: a player who races hard but gives room can defuse a rivalry. Without it
            // every rival saturates hostile over a long career and the scalar stops discriminating.
            var hostile = Encounter();
            hostile.ContactsPlayerFault = 3;
            hostile.PlayerFaultSeverityTotal = 2f;

            var respectful = Encounter();
            respectful.PlayerYields = 3;
            respectful.ContactsRivalFault = 1;

            Assert.That(RivalMemoryStore.RivalryDelta(hostile), Is.GreaterThan(0f));
            Assert.That(RivalMemoryStore.RivalryDelta(respectful), Is.LessThan(0f));
        }

        [Test]
        public void OneRace_CannotMaxOutAGrudge()
        {
            var brutal = Encounter();
            brutal.ContactsPlayerFault = 50;
            brutal.PlayerFaultSeverityTotal = 100f;
            Assert.That(RivalMemoryStore.RivalryDelta(brutal),
                Is.LessThanOrEqualTo(RivalMemoryStore.MaxRivalryStep + 1e-4f));
        }

        [Test]
        public void Store_IsBoundedAndEvictsTheLeastRecentlySeen()
        {
            var list = new List<RivalMemory>();
            for (int i = 0; i < RivalMemoryStore.MaxRivalMemories + 12; i++)
                RivalMemoryStore.Fold(list, $"rival_{i:000}", Encounter(), careerRaces: i + 1, timestamp: i);

            Assert.That(list.Count, Is.LessThanOrEqualTo(RivalMemoryStore.MaxRivalMemories));
            // The most recent entries must have survived.
            string newest = $"rival_{RivalMemoryStore.MaxRivalMemories + 11:000}";
            Assert.That(RivalMemoryStore.Get(list, newest, careerRaces: 999).HasMetPlayer, Is.True);
        }

        [Test]
        public void AGlancingEncounter_TeachesNothing()
        {
            // Zero proximity means the two never actually raced each other; recording an encounter would
            // inflate the personal gate on evidence that does not exist.
            var list = new List<RivalMemory>();
            RivalMemoryStore.Fold(list, Kes, Encounter(proximity: 0f), careerRaces: 1, timestamp: 0L);
            Assert.That(RivalMemoryStore.Get(list, Kes, careerRaces: 1).encounters, Is.Zero);
        }

        [Test]
        public void PersonalGate_RisesWithEncountersOnly()
        {
            var list = new List<RivalMemory>();
            float prev = 0f;
            for (int i = 1; i <= 10; i++)
            {
                RivalMemoryStore.Fold(list, Kes, Encounter(), careerRaces: i, timestamp: i);
                float gate = RivalMemoryStore.Get(list, Kes, careerRaces: i).PersonalGate;
                Assert.That(gate, Is.GreaterThan(prev), $"gate did not rise at encounter {i}");
                Assert.That(gate, Is.InRange(0f, 1f));
                prev = gate;
            }
        }

        [Test]
        public void DegenerateArguments_DoNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                RivalMemoryStore.Fold(null, Kes, Encounter(), 1, 0L);
                RivalMemoryStore.Fold(new List<RivalMemory>(), null, Encounter(), 1, 0L);
                RivalMemoryStore.Fold(new List<RivalMemory>(), "", Encounter(), 1, 0L);
                RivalMemoryStore.Get(null, Kes, 1);
                RivalMemoryStore.GetStyle(null, 5, 0);
                RivalMemoryStore.FoldStyle(null, default);
                RivalMemoryStore.FoldStyle(new PlayerStyleProfile(), default);
            });
        }

        // --- Style folding ------------------------------------------------------------------------------

        [Test]
        public void StyleFold_LearnsASidePreference()
        {
            var style = new PlayerStyleProfile();
            var enc = Encounter();
            enc.PlayerPassesInside = 8;
            enc.PlayerPassesOutside = 1;

            RivalMemoryStore.FoldStyle(style, new RaceObservationSummary
            {
                Rivals = new[] { enc }, FieldSize = 8, PlayerFinishPosition = 1,
            });

            Assert.That(style.insidePreference.Signed, Is.GreaterThan(0f), "should read as an inside-goer");
        }

        [Test]
        public void StraightLinePasses_TeachNoSidePreference()
        {
            // On tracks with long straights the slipstream pass happens on whichever side the layout
            // dictates. Folding those in would teach rivals the shape of the circuit, not the player.
            var style = new PlayerStyleProfile();
            var enc = Encounter();
            enc.PlayerPassesInside = 0;
            enc.PlayerPassesOutside = 0;
            enc.PlayerPassesStraight = 12;

            RivalMemoryStore.FoldStyle(style, new RaceObservationSummary
            {
                Rivals = new[] { enc }, FieldSize = 8, PlayerFinishPosition = 1,
            });

            Assert.That(style.insidePreference.N, Is.Zero, "straight passes must not enter the side tally");
            Assert.That(style.insidePreference.Signed, Is.Zero);
        }

        [Test]
        public void ADistantRival_TeachesAlmostNothing()
        {
            var near = new PlayerStyleProfile();
            var far = new PlayerStyleProfile();
            var summary = new RaceObservationSummary { FieldSize = 8, PlayerFinishPosition = 1 };

            var nearEnc = Encounter(proximity: 60f);
            nearEnc.PlayerPassesInside = 5;
            var farEnc = Encounter(proximity: 1f);
            farEnc.PlayerPassesInside = 5;

            summary.Rivals = new[] { nearEnc };
            RivalMemoryStore.FoldStyle(near, summary);
            summary.Rivals = new[] { farEnc };
            RivalMemoryStore.FoldStyle(far, summary);

            Assert.That(far.insidePreference.N, Is.LessThan(near.insidePreference.N * 0.2f));
        }

        [Test]
        public void CleanRacing_IsNotRuinedByOneHighFaultCrash()
        {
            // A driver with one crash in a long clean career that happened to be entirely their fault is
            // not a dirty driver. A fault-share-only score would call them one; the frequency PRODUCT is
            // what stops that.
            var style = new PlayerStyleProfile();
            style.collisionRate.Add(1f, 3600f); // one fault contact per hour of close racing
            style.faultShare.Add(1f, 0f);       // ...and it was entirely theirs
            Assert.That(style.CleanRacing, Is.GreaterThan(0.9f));

            var dirty = new PlayerStyleProfile();
            dirty.collisionRate.Add(60f, 3600f); // one a minute
            dirty.faultShare.Add(1f, 0f);
            Assert.That(dirty.CleanRacing, Is.LessThan(0.5f));
        }

        [Test]
        public void PaceScore_TracksFinishPosition()
        {
            var winner = new PlayerStyleProfile();
            RivalMemoryStore.FoldStyle(winner, new RaceObservationSummary
            { Rivals = new[] { Encounter() }, FieldSize = 8, PlayerFinishPosition = 1 });

            var last = new PlayerStyleProfile();
            RivalMemoryStore.FoldStyle(last, new RaceObservationSummary
            { Rivals = new[] { Encounter() }, FieldSize = 8, PlayerFinishPosition = 8 });

            Assert.That(winner.paceScore.Mean, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(last.paceScore.Mean, Is.EqualTo(0f).Within(1e-3f));
        }

        // --- Persistence --------------------------------------------------------------------------------

        [Test]
        public void MetaProgress_RoundTripsThroughJsonUtility()
        {
            var meta = new MetaProgress { careerRaces = 12, styleLastFoldedRace = 12 };
            RivalMemoryStore.Fold(meta.rivalMemories, Kes, Encounter(), 12, 5555L);
            RivalMemoryStore.Fold(meta.rivalMemories, Dex, Encounter(key: 2), 12, 5555L);
            meta.playerStyle.insidePreference.Add(7f, 2f);
            meta.playerStyle.racesObserved = 12;

            string json = JsonUtility.ToJson(meta);
            var back = JsonUtility.FromJson<MetaProgress>(json);

            Assert.That(back.careerRaces, Is.EqualTo(12));
            Assert.That(back.rivalMemories, Has.Count.EqualTo(2));
            Assert.That(back.playerStyle.insidePreference.P,
                Is.EqualTo(meta.playerStyle.insidePreference.P).Within(1e-4f));
            Assert.That(RivalMemoryStore.Get(back.rivalMemories, Kes, 12).encounters, Is.EqualTo(1));
        }

        [Test]
        public void ALegacyProfile_LoadsWithEmptyMemories()
        {
            // A profile written before any of this existed simply lacks the fields. It must load clean
            // rather than null-referencing on the first race of an existing career.
            const string legacy = "{\"totalRuns\":7,\"bestCircuitReached\":2,\"seasonsCleared\":1," +
                                  "\"lifetimeMoney\":250,\"unlocks\":[\"stake2\"]}";
            var back = JsonUtility.FromJson<MetaProgress>(legacy);
            back.rivalMemories ??= new List<RivalMemory>();
            back.playerStyle ??= new PlayerStyleProfile();

            Assert.That(back.totalRuns, Is.EqualTo(7));
            Assert.That(back.careerRaces, Is.Zero);
            Assert.That(back.rivalMemories, Is.Empty);
            Assert.That(RivalMemoryStore.Get(back.rivalMemories, Kes, 0).HasMetPlayer, Is.False);
        }

        [Test]
        public void SavedFloats_AreQuantizedSoSaveLoadIsIdempotent()
        {
            var list = new List<RivalMemory>();
            RivalMemoryStore.Fold(list, Kes, Encounter(), 1, 0L);

            string json = JsonUtility.ToJson(new MetaProgress { rivalMemories = list });
            var back = JsonUtility.FromJson<MetaProgress>(json);
            string json2 = JsonUtility.ToJson(back);

            Assert.That(json2, Is.EqualTo(json), "a save/load cycle must not drift the model");
        }
    }
}
