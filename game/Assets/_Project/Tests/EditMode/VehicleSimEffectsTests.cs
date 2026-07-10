using NUnit.Framework;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>Covers the transient combat-effect subsystem added to the headless sim core.</summary>
    public class VehicleSimEffectsTests : TestBase
    {
        private static VehicleSim NewSim() => new VehicleSim(new VehicleSpec());

        [Test]
        public void FreshSim_HasNominalMultipliers()
        {
            var sim = NewSim();
            Assert.That(sim.GripEffectMult, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(sim.PowerEffectMult, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void GripSap_SetsFloorToOneMinusStrength()
        {
            var sim = NewSim();
            sim.ApplyGripSap(0.3f, 1f);
            Assert.That(sim.GripEffectMult, Is.EqualTo(0.7f).Within(1e-4f));
        }

        [Test]
        public void GripSap_OnlyDeepens_WeakerSapIgnored()
        {
            var sim = NewSim();
            sim.ApplyGripSap(0.4f, 1f); // -> 0.6
            sim.ApplyGripSap(0.1f, 1f); // 0.9 floor is weaker, ignored
            Assert.That(sim.GripEffectMult, Is.EqualTo(0.6f).Within(1e-4f));
        }

        [Test]
        public void GripSap_StrongerSapReplaces()
        {
            var sim = NewSim();
            sim.ApplyGripSap(0.2f, 1f); // -> 0.8
            sim.ApplyGripSap(0.5f, 1f); // -> 0.5, stronger
            Assert.That(sim.GripEffectMult, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void GripSap_NonPositiveStrength_NoOp()
        {
            var sim = NewSim();
            sim.ApplyGripSap(0f, 1f);
            sim.ApplyGripSap(-0.5f, 1f);
            Assert.That(sim.GripEffectMult, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void PowerSap_SetsFloor()
        {
            var sim = NewSim();
            sim.ApplyPowerSap(0.25f, 1f);
            Assert.That(sim.PowerEffectMult, Is.EqualTo(0.75f).Within(1e-4f));
        }

        [Test]
        public void Effects_RecoverPartially_AfterOneStep()
        {
            var sim = NewSim();
            sim.ApplyGripSap(0.5f, 1f);   // 0.5, recover 1.0/s
            StepAirborne(sim, 0.02f, 1);  // decay by 1.0 * 0.02
            Assert.That(sim.GripEffectMult, Is.EqualTo(0.52f).Within(1e-3f));
        }

        [Test]
        public void Effects_FullyRecoverTowardOne_WhenSteppedLongEnough()
        {
            var sim = NewSim();
            sim.ApplyGripSap(0.5f, 1f); // needs 0.5s at 1.0/s
            StepAirborne(sim, 0.02f, 30); // 0.6s
            Assert.That(sim.GripEffectMult, Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void Recovery_NeverOvershootsNominal()
        {
            var sim = NewSim();
            sim.ApplyGripSap(0.1f, 100f); // huge recover rate
            StepAirborne(sim, 0.02f, 5);
            Assert.That(sim.GripEffectMult, Is.EqualTo(1f).Within(1e-5f));
        }

        // Steps the sim with all wheels airborne and no input — isolates effect decay from tyre forces.
        private static void StepAirborne(VehicleSim sim, float dt, int steps)
        {
            var contacts = new GroundContact[VehicleSim.WheelCount]; // Grounded == false by default
            var input = default(VehicleInput);
            for (int i = 0; i < steps; i++)
                sim.Step(dt, input, contacts, Vector3.zero, Vector3.forward, Vector3.up, Vector3.zero);
        }
    }
}
