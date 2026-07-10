using NUnit.Framework;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the tyre heat/wear model: a cold tyre is slippy and warms into an optimal-grip band under
    /// slip, sustained abuse overheats it and sheds grip, idle time cools it back toward ambient, and
    /// wear accumulates until Reset() clears it. Also verifies the VehicleSim wiring is a true no-op while
    /// disabled (the user's driving feel must not change) and that it actually bites once enabled.
    /// </summary>
    public class TyreWearTests : TestBase
    {
        private const float Dt = 0.02f; // one 50 Hz FixedUpdate step

        // Steps a fresh model N times with a fixed slip/load and returns it.
        private static TyreWear Soak(float slip01, float load01, int steps)
        {
            var t = new TyreWear();
            for (int i = 0; i < steps; i++) t.Step(Dt, slip01, load01);
            return t;
        }

        // ---------------------------------------------------------------- TyreWear model, direct

        [Test]
        public void FreshModel_IsNoOp_GripMultOne()
        {
            var t = new TyreWear();
            Assert.That(t.GripMult, Is.EqualTo(1f).Within(1e-6f), "an un-stepped model must be a perfect no-op");
            Assert.That(t.Wear, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(t.TempC, Is.EqualTo(t.AmbientC).Within(1e-6f));
        }

        [Test]
        public void ColdTyre_HasReducedGrip()
        {
            var t = new TyreWear();
            // A single step at (near) zero slip barely moves temperature, so the tyre stays cold.
            t.Step(Dt, 0f, 1f);
            Assert.That(t.TempC, Is.EqualTo(t.AmbientC).Within(0.5f), "no slip should not warm the tyre");
            Assert.Less(t.GripMult, 1f, "a cold tyre must be slippier than a warmed one");
            Assert.That(t.GripMult, Is.EqualTo(t.ColdGrip).Within(1e-3f));
        }

        [Test]
        public void ModerateSlip_WarmsIntoOptimalBand_GripNearFull()
        {
            // Moderate sustained slip should settle the tyre inside the full-grip band and leave grip ~1.
            var t = Soak(0.3f, 1f, 500); // ~10 s
            Assert.That(t.TempC, Is.InRange(t.OptimalLowC, t.OptimalHighC),
                "moderate slip should warm the tyre into its optimal band, temp=" + t.TempC);
            Assert.Greater(t.GripMult, 0.98f, "grip in the optimal band should be ~1, was " + t.GripMult);
            Assert.Greater(t.GripMult, new TyreWear().GripMult, "warm grip must beat cold grip");
        }

        [Test]
        public void SustainedHighSlip_Overheats_DropsGrip()
        {
            var t = Soak(1f, 1f, 400); // ~8 s of full slip
            Assert.Greater(t.TempC, t.OptimalHighC, "sustained full slip should push past the band, temp=" + t.TempC);
            Assert.Less(t.GripMult, t.ColdGrip, "an overheated tyre should be worse than even a cold one, grip=" + t.GripMult);
        }

        [Test]
        public void IdleTime_CoolsBackTowardAmbient()
        {
            var t = Soak(1f, 1f, 200); // heat it right up
            float hot = t.TempC;
            Assert.Greater(hot, t.OptimalHighC, "precondition: tyre is hot before cooling");

            for (int i = 0; i < 600; i++) t.Step(Dt, 0f, 0f); // ~12 s idle
            Assert.Less(t.TempC, hot, "idle time must cool the tyre");
            Assert.That(t.TempC, Is.EqualTo(t.AmbientC).Within(2f), "cooling should approach ambient, temp=" + t.TempC);
        }

        [Test]
        public void Wear_AccumulatesUnderAbuse_ThenResetClearsEverything()
        {
            var t = Soak(1f, 1f, 400); // sustained overheating accrues wear
            Assert.Greater(t.Wear, 0f, "sustained abuse must accumulate wear");
            float wornGrip = t.GripMult;

            t.Reset();
            Assert.That(t.Wear, Is.EqualTo(0f).Within(1e-6f), "Reset must clear accumulated wear");
            Assert.That(t.TempC, Is.EqualTo(t.AmbientC).Within(1e-6f), "Reset must return temperature to ambient");
            Assert.That(t.GripMult, Is.EqualTo(1f).Within(1e-6f), "Reset must restore the nominal grip multiplier");
            Assert.Less(wornGrip, 1f, "sanity: the worn tyre had actually lost grip before the reset");
        }

        [Test]
        public void Wear_PermanentlyLowersPeakGrip_UntilReset()
        {
            // Overheat + wear a tyre, then bring it back into the optimal band: grip should recover but
            // stay BELOW 1 because wear has permanently trimmed the peak (thermal is 1 in the band).
            var t = Soak(1f, 1f, 400);
            Assert.Greater(t.Wear, 0f);
            for (int i = 0; i < 800; i++) t.Step(Dt, 0.3f, 1f); // ease back into the band
            Assert.That(t.TempC, Is.InRange(t.OptimalLowC, t.OptimalHighC), "precondition: back in the band");
            Assert.Less(t.GripMult, 1f, "wear should hold peak grip below 1 even at optimal temperature");
            Assert.GreaterOrEqual(t.GripMult, t.MinGrip, "grip must never fall below the floor");
        }

        [Test]
        public void GripMult_AlwaysWithinFloorAndOne()
        {
            var t = new TyreWear();
            var rng = new System.Random(1234);
            for (int i = 0; i < 5000; i++)
            {
                t.Step(Dt, (float)rng.NextDouble(), (float)rng.NextDouble());
                Assert.GreaterOrEqual(t.GripMult, t.MinGrip, "grip fell below the floor");
                Assert.LessOrEqual(t.GripMult, 1f, "grip rose above nominal");
            }
        }

        // ---------------------------------------------------------------- VehicleSim wiring

        // A flat, level, grounded contact for wheel i (mirrors VehicleSimStepTests.FlatContact).
        private static GroundContact FlatContact(VehicleSim sim, int i, float chassisHeight, Vector3 pointVel)
        {
            Vector3 local = sim.WheelLocalPosition(i);
            Vector3 attach = new Vector3(local.x, chassisHeight + local.y, local.z);
            return new GroundContact
            {
                Grounded = true,
                HitDistance = attach.y,
                HitPoint = new Vector3(attach.x, 0f, attach.z),
                SurfaceNormal = Vector3.up,
                PointVelocity = pointVel,
                SuspensionUp = Vector3.up,
                WheelForward = Vector3.forward,
                WheelRight = Vector3.right,
                AttachPoint = attach,
            };
        }

        // Steps a grounded car under full throttle from walking pace, forcing driven-wheel wheelspin (high
        // slip -> heat). Returns the last RL (rear, driven on the default RWD spec) tyre force.
        private static Vector3 StepGroundedWheelspin(VehicleSim sim, int steps)
        {
            var input = new VehicleInput { Throttle = 1f };
            Vector3 vel = Vector3.forward * 3f;
            var contacts = new GroundContact[VehicleSim.WheelCount];
            Vector3 lastRearForce = Vector3.zero;
            for (int step = 0; step < steps; step++)
            {
                for (int i = 0; i < VehicleSim.WheelCount; i++)
                    contacts[i] = FlatContact(sim, i, 0.65f, vel);
                var forces = sim.Step(Dt, input, contacts, vel, Vector3.forward, Vector3.up, Vector3.zero);
                lastRearForce = forces[VehicleSim.RL].Force;
                for (int i = 0; i < forces.Length; i++)
                {
                    Vector3 f = forces[i].Force;
                    Assert.IsFalse(float.IsNaN(f.x) || float.IsNaN(f.y) || float.IsNaN(f.z), "NaN force");
                    Assert.IsFalse(float.IsInfinity(f.x) || float.IsInfinity(f.y) || float.IsInfinity(f.z), "Inf force");
                }
            }
            return lastRearForce;
        }

        [Test]
        public void VehicleSim_TyreWearDisabledByDefault()
        {
            var sim = new VehicleSim(new VehicleSpec());
            Assert.IsFalse(sim.TyreWearEnabled, "tyre wear must default OFF to preserve driving feel");
        }

        [Test]
        public void VehicleSim_Disabled_GripMultStaysExactlyOne_AfterHardStepping()
        {
            var sim = new VehicleSim(new VehicleSpec()); // TyreWearEnabled == false
            StepGroundedWheelspin(sim, 300);
            for (int i = 0; i < VehicleSim.WheelCount; i++)
                Assert.That(sim.WheelTyreWear(i).GripMult, Is.EqualTo(1f).Within(1e-6f),
                    "disabled: the model must never be stepped, so GripMult stays exactly 1 on wheel " + i);
        }

        [Test]
        public void VehicleSim_EnabledVsDisabled_ProduceDifferentTyreForce()
        {
            // Same inputs, same contacts, same steps — the only difference is the enable flag. If enabling
            // changes the driven-wheel force, the wiring is live; and the disabled sim proves it's a no-op.
            var off = new VehicleSim(new VehicleSpec());
            var on = new VehicleSim(new VehicleSpec()) { TyreWearEnabled = true };

            Vector3 forceOff = StepGroundedWheelspin(off, 400);
            Vector3 forceOn = StepGroundedWheelspin(on, 400);

            Assert.Greater(on.WheelTyreWear(VehicleSim.RL).TempC, on.WheelTyreWear(VehicleSim.RL).AmbientC,
                "enabled: the driven tyre should have heated up");
            Assert.Less(on.WheelTyreWear(VehicleSim.RL).GripMult, 1f, "enabled: the abused tyre should have lost grip");
            Assert.Greater((forceOn - forceOff).magnitude, 1f,
                "enabling tyre wear must change the driven-wheel force, off=" + forceOff + " on=" + forceOn);
        }

        [Test]
        public void VehicleSim_ResetTyreWear_ClearsAccumulatedWear()
        {
            var sim = new VehicleSim(new VehicleSpec()) { TyreWearEnabled = true };
            StepGroundedWheelspin(sim, 400);
            Assert.Greater(sim.WheelTyreWear(VehicleSim.RL).Wear, 0f, "driven tyre should have accrued wear");

            sim.ResetTyreWear();
            for (int i = 0; i < VehicleSim.WheelCount; i++)
            {
                Assert.That(sim.WheelTyreWear(i).Wear, Is.EqualTo(0f).Within(1e-6f), "wheel " + i + " wear not cleared");
                Assert.That(sim.WheelTyreWear(i).GripMult, Is.EqualTo(1f).Within(1e-6f), "wheel " + i + " grip not restored");
            }
        }
    }
}
