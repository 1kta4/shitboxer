using NUnit.Framework;
using Shitboxer.Meta;
using Shitboxer.Race;
using Shitboxer.Vehicle;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the per-race bot-strength ramp — the fix for bots never buying parts. SpecModApplier.Apply
    /// is only ever handed the player's car, so the rival field stayed showroom-stock while the player's
    /// build compounded at every garage; the 2026-07-17 playtest lapped all 7 rivals by lap 2 of 3.
    ///
    /// Two halves have to line up, and each is useless alone: RunDirector scales the rival's VehicleSpec,
    /// and BotLimits reads that scaled grip back off the spec so the brain plans to it. Scale the car
    /// without the second half and the bot still brakes as though it were on the old tyres.
    /// </summary>
    public class BotStrengthRampTests : TestBase
    {
        private const float StockMu = 1.32f;      // GripBox
        private const float StockTorque = 205f;

        private static VehicleSpec Stock()
        {
            var spec = new VehicleSpec();
            spec.FrontTyre.PeakMu = StockMu;
            spec.RearTyre.PeakMu = StockMu;
            spec.Engine.PeakTorqueNm = StockTorque;
            return spec;
        }

        // --- SpecModApplier.Scaled: the rivals' stand-in for a shopping trip -----------------

        [Test]
        public void Scaled_LiftsGripAndPower()
        {
            VehicleSpec scaled = SpecModApplier.Scaled(Stock(), 1.7f, 1.7f);

            Assert.That(scaled.FrontTyre.PeakMu, Is.EqualTo(StockMu * 1.7f).Within(1e-4f));
            Assert.That(scaled.RearTyre.PeakMu, Is.EqualTo(StockMu * 1.7f).Within(1e-4f));
            Assert.That(scaled.Engine.PeakTorqueNm, Is.EqualTo(StockTorque * 1.7f).Within(1e-3f));
        }

        [Test]
        public void Scaled_NeverMutatesTheAuthoredAsset()
        {
            // The bot prefab's spec is a shared asset — scaling it in place would compound race on
            // race and permanently corrupt the project's authored car.
            VehicleSpec source = Stock();
            SpecModApplier.Scaled(source, 2f, 2f);

            Assert.That(source.FrontTyre.PeakMu, Is.EqualTo(StockMu).Within(1e-4f));
            Assert.That(source.Engine.PeakTorqueNm, Is.EqualTo(StockTorque).Within(1e-3f));
        }

        [Test]
        public void Scaled_AtOne_IsIdentity()
        {
            // Race 1 asks for scale 1.0 — rivals must be exactly the authored car.
            VehicleSpec scaled = SpecModApplier.Scaled(Stock(), 1f, 1f);

            Assert.That(scaled.FrontTyre.PeakMu, Is.EqualTo(StockMu).Within(1e-4f));
            Assert.That(scaled.Engine.PeakTorqueNm, Is.EqualTo(StockTorque).Within(1e-3f));
        }

        // --- BotLimits: the half that makes the scaling visible to the brain ------------------

        [Test]
        public void BotLimits_Default_IsTheOldHardcodedPair()
        {
            // A bare race scene with no run layer must drive exactly as it did before BotLimits.
            BotLimits d = BotLimits.Default;
            Assert.That(d.MaxLatAccel, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(d.BrakeDecel, Is.EqualTo(8f).Within(1e-4f));
        }

        [Test]
        public void BotLimits_FromGrip_ReadsTheCarInsteadOfAssuming10()
        {
            // The bug in miniature: a stock GripBox has ~12.9 m/s^2 of grip and the brain assumed 10,
            // so bots under-drove the car they already had before any part ever entered the picture.
            BotLimits stock = BotLimits.FromGrip(StockMu);

            Assert.That(stock.MaxLatAccel, Is.EqualTo(StockMu * 9.81f).Within(1e-3f));
            Assert.Greater(stock.MaxLatAccel, BotLimits.Default.MaxLatAccel);
            Assert.Greater(stock.BrakeDecel, BotLimits.Default.BrakeDecel);
        }

        [Test]
        public void BotLimits_FromGrip_ScalesWithTheRamp()
        {
            // Corner speed is sqrt(MaxLatAccel/curvature), so grip must carry through proportionally
            // or the ramp buys nothing.
            BotLimits stock = BotLimits.FromGrip(StockMu);
            BotLimits ramped = BotLimits.FromGrip(StockMu * 1.7f);

            Assert.That(ramped.MaxLatAccel / stock.MaxLatAccel, Is.EqualTo(1.7f).Within(1e-3f));
        }

        [Test]
        public void BotLimits_FromGrip_StaysSaneOnJunkInput()
        {
            // A zero/negative mu would otherwise hand the plan a 0 or negative corner limit and
            // sqrt() it — bots would either stop dead or NaN out.
            Assert.Greater(BotLimits.FromGrip(0f).MaxLatAccel, 0f);
            Assert.Greater(BotLimits.FromGrip(-5f).BrakeDecel, 0f);
        }

        [Test]
        public void ScaledSpec_FeedsBotLimits_EndToEnd()
        {
            // The two halves together: scale the car, and the brain's limit moves with it.
            VehicleSpec race3 = SpecModApplier.Scaled(Stock(), 1.7f, 1.7f);
            BotLimits limits = BotLimits.FromGrip(race3.FrontTyre.PeakMu);

            Assert.That(limits.MaxLatAccel, Is.EqualTo(StockMu * 1.7f * 9.81f).Within(1e-2f));
            // ~22 m/s^2 vs the 10 the brain used to assume — the whole point of the change.
            Assert.Greater(limits.MaxLatAccel, 2f * BotLimits.Default.MaxLatAccel);
        }

        // --- RunState.RaceNumber: what the ramp keys off --------------------------------------

        [Test]
        public void RaceNumber_CountsRacesNotCircuits()
        {
            // The ramp must step every race. Keying off CircuitIndex (as DifficultyMult does) leaves
            // the field stock for a whole season — and at the shipped TotalCircuits = 1 it never fires.
            var run = new RunState { RacesPerCircuit = 3, CircuitIndex = 0, RaceIndex = 0 };
            Assert.AreEqual(0, run.RaceNumber);

            run.RaceIndex = 2;
            Assert.AreEqual(2, run.RaceNumber);

            run.CircuitIndex = 1;
            run.RaceIndex = 0;
            Assert.AreEqual(3, run.RaceNumber);
        }

        // --- BotStrengthFor: the ramp curve --------------------------------------------------

        [Test]
        public void BotStrengthFor_StartsAtBase_NotAtStock()
        {
            // Race 1 at scale 1.0 playtested far too soft — rivals drive the player's own starting
            // shitbox, and a human out-drives the bot speed plan by ~25%, so stock is never a race.
            Assert.That(RunDirector.BotStrengthFor(0, 1.4f, 0.4f, 3f), Is.EqualTo(1.4f).Within(1e-4f));
        }

        [Test]
        public void BotStrengthFor_RampsAcrossA5RaceSeason()
        {
            // 1.4 -> 3.0 over five races: roughly 23s -> 17s a lap against a 14.4s player.
            Assert.That(RunDirector.BotStrengthFor(1, 1.4f, 0.4f, 3f), Is.EqualTo(1.8f).Within(1e-4f));
            Assert.That(RunDirector.BotStrengthFor(2, 1.4f, 0.4f, 3f), Is.EqualTo(2.2f).Within(1e-4f));
            Assert.That(RunDirector.BotStrengthFor(3, 1.4f, 0.4f, 3f), Is.EqualTo(2.6f).Within(1e-4f));
            Assert.That(RunDirector.BotStrengthFor(4, 1.4f, 0.4f, 3f), Is.EqualTo(3.0f).Within(1e-4f));
        }

        [Test]
        public void BotStrengthFor_CapsSoALongSeasonCantHandTheFieldASpaceship()
        {
            Assert.That(RunDirector.BotStrengthFor(50, 1.4f, 0.4f, 3f), Is.EqualTo(3f).Within(1e-4f));
        }

        [Test]
        public void BotStrengthFor_Retuned24RaceSeason_LandsOnThePracticalCeilingAtTheFinale()
        {
            // Doc 08 decision 13: base 1.4, +0.013/race, cap 1.70 — the PRACTICAL player ceiling, not
            // the theoretical x2. The ramp must reach the cap BY THE CURVE at the final race (index 23:
            // 1.4 + 23*0.013 = 1.699), not slam into a clamp mid-season the way the old 5-race tune
            // (0.40/race, capped at race 4) did on a 24-race calendar.
            Assert.That(RunDirector.BotStrengthFor(0, 1.4f, 0.013f, 1.7f), Is.EqualTo(1.4f).Within(1e-4f),
                "race 1 starts at base");
            Assert.That(RunDirector.BotStrengthFor(11, 1.4f, 0.013f, 1.7f), Is.EqualTo(1.543f).Within(1e-4f),
                "mid-season sits above a typical build's ~1.45 reach — survive-and-farm territory");
            Assert.That(RunDirector.BotStrengthFor(23, 1.4f, 0.013f, 1.7f), Is.EqualTo(1.699f).Within(1e-4f),
                "the finale lands a hair under the cap by ramp, not by clamp");
            Assert.LessOrEqual(RunDirector.BotStrengthFor(100, 1.4f, 0.013f, 1.7f), 1.7f + 1e-4f,
                "nothing ever exceeds the practical ceiling");
        }

        [Test]
        public void BotStrengthFor_OffConfig_LeavesRivalsAsAuthored()
        {
            // Callers treat 1 as "don't touch the spec at all", so an OFF config must return exactly 1.
            Assert.That(RunDirector.BotStrengthFor(4, 1f, 0f, 3f), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void BotStrengthFor_NeverWeakensRivals_OnJunkConfig()
        {
            // A negative/sub-1 config would otherwise hand rivals a WORSE car than authored.
            Assert.GreaterOrEqual(RunDirector.BotStrengthFor(2, 0.2f, -1f, 3f), 1f);
            Assert.GreaterOrEqual(RunDirector.BotStrengthFor(-3, 1.4f, 0.4f, 0.5f), 1f);
        }

        // --- Season shape: 5 races per circuit ------------------------------------------------

        [Test]
        public void ApplySeasonShape_StampsRacesPerCircuit()
        {
            RunState run = RunDirector.ApplySeasonShape(new RunState(), 1, 5);

            Assert.AreEqual(5, run.RacesPerCircuit);
            // The boss is the circuit's last race — race 5, not race 3.
            run.RaceIndex = 3;
            Assert.IsFalse(run.IsBossRace);
            run.RaceIndex = 4;
            Assert.IsTrue(run.IsBossRace);
        }

        [Test]
        public void ApplySeasonShape_FullSeason_Is8CircuitsOf3Races()
        {
            // Doc 08 decision 12: 8 circuits x 3 races = 24, the calendar the bot ramp above is sized
            // against. RaceNumber must run 0..23 and the run only completes after the final boss.
            RunState run = RunDirector.ApplySeasonShape(new RunState(), 8, 3);

            Assert.AreEqual(8, run.TotalCircuits);
            Assert.AreEqual(3, run.RacesPerCircuit);

            run.CircuitIndex = 7;
            run.RaceIndex = 2;
            Assert.AreEqual(23, run.RaceNumber, "the finale is race number 23 — what the ramp's cap keys off");
            Assert.IsTrue(run.IsBossRace, "the finale is the last circuit's boss");
            Assert.IsTrue(run.IsFinalCircuit);
            Assert.IsFalse(run.RunComplete, "the run is not complete until the finale is CLEARED");

            run.RaceIndex = 3; // cleared the final boss
            Assert.IsTrue(run.RunComplete);
        }

        [Test]
        public void ApplySeasonShape_ZeroRacesPerCircuit_LeavesItAlone()
        {
            // The 2-arg overload's callers (and the existing tests) must keep their RunState's value.
            RunState run = RunDirector.ApplySeasonShape(new RunState { RacesPerCircuit = 3 }, 1);
            Assert.AreEqual(3, run.RacesPerCircuit);
        }

        // --- Track rotation --------------------------------------------------------------------

        [Test]
        public void SceneForRace_RotatesSoA5RaceRunIsntOneRectangleFiveTimes()
        {
            var scenes = new[] { "RaceTest", "RaceGauntlet", "RaceSpeedway" };

            Assert.AreEqual("RaceTest", RunDirector.SceneForRace(0, scenes, "Active"));
            Assert.AreEqual("RaceGauntlet", RunDirector.SceneForRace(1, scenes, "Active"));
            Assert.AreEqual("RaceSpeedway", RunDirector.SceneForRace(2, scenes, "Active"));
            Assert.AreEqual("RaceTest", RunDirector.SceneForRace(3, scenes, "Active"));
            Assert.AreEqual("RaceGauntlet", RunDirector.SceneForRace(4, scenes, "Active"));
        }

        [Test]
        public void SceneForRace_Unconfigured_FallsBackToTheActiveScene()
        {
            // Must reload the active scene (the old single-track behaviour) rather than hand
            // SceneManager.LoadScene an empty name, which throws.
            Assert.AreEqual("Active", RunDirector.SceneForRace(2, null, "Active"));
            Assert.AreEqual("Active", RunDirector.SceneForRace(2, new string[0], "Active"));
            Assert.AreEqual("Active", RunDirector.SceneForRace(1, new[] { "RaceTest", "  " }, "Active"));
        }

        [Test]
        public void SceneForRace_NeverIndexesOutOfRange()
        {
            var scenes = new[] { "A", "B" };
            Assert.AreEqual("A", RunDirector.SceneForRace(100, scenes, "Active"));
            Assert.AreEqual("B", RunDirector.SceneForRace(-1, scenes, "Active")); // negatives wrap, not crash
        }
    }
}
