using NUnit.Framework;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the wave-11 boost input plumbing: the additive <see cref="VehicleInput.Boost"/> flag
    /// (defaults false and is invisible to the sim, so driving feel is byte-for-byte unchanged) and the
    /// pure <see cref="DraftBoost.ResolveActivate"/> deploy-signal resolver that folds that flag — the
    /// overtake button — into the KERS boost's deploy decision. A disabled DraftBoost ignores the input
    /// entirely, so wiring the plumbing changes nothing until a designer arms the feature.
    /// </summary>
    public class BoostInputTests : TestBase
    {
        private const float Dt = 0.02f; // one 50 Hz FixedUpdate step

        // ---------------------------------------------------------------- VehicleInput.Boost is additive

        [Test]
        public void Boost_DefaultsFalse_OnFreshInput()
        {
            Assert.IsFalse(default(VehicleInput).Boost, "a default VehicleInput must not request boost");
            Assert.IsFalse(new VehicleInput { Throttle = 1f, Steer = -1f, Brake = 0.5f }.Boost,
                "an object-initialised VehicleInput that omits Boost leaves it false");
        }

        // ------------------------------------------------ The sim never reads Boost: setting it is a no-op

        // A flat, level, grounded contact for wheel i (mirrors DraftBoostTests / VehicleSimStepTests).
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

        // Steps a grounded car under full throttle at a held 3 m/s, returning the final driven (RL) tyre force.
        private static Vector3 RunRearForce(VehicleSim sim, bool boost, int steps)
        {
            var input = new VehicleInput { Throttle = 1f, Boost = boost };
            Vector3 vel = Vector3.forward * 3f;
            var contacts = new GroundContact[VehicleSim.WheelCount];
            Vector3 last = Vector3.zero;
            for (int step = 0; step < steps; step++)
            {
                for (int i = 0; i < VehicleSim.WheelCount; i++)
                    contacts[i] = FlatContact(sim, i, 0.65f, vel);
                var forces = sim.Step(Dt, input, contacts, vel, Vector3.forward, Vector3.up, Vector3.zero);
                last = forces[VehicleSim.RL].Force;
            }
            return last;
        }

        [Test]
        public void VehicleSim_IgnoresBoostField_ForceIdenticalToBaseline()
        {
            // Same inputs and maths; the ONLY difference is VehicleInput.Boost. The sim never reads that
            // field (boost is delivered via VehicleSim.BoostMult, which nothing here touches), so the
            // driven-wheel force must match bit-for-bit — proving the new field is a pure no-op for the sim.
            var withBoost = new VehicleSim(new VehicleSpec());
            var without = new VehicleSim(new VehicleSpec());

            Vector3 fBoost = RunRearForce(withBoost, boost: true, steps: 120);
            Vector3 fPlain = RunRearForce(without, boost: false, steps: 120);

            Assert.That(fBoost.x, Is.EqualTo(fPlain.x).Within(1e-6f), "Boost must not affect sim force (x)");
            Assert.That(fBoost.y, Is.EqualTo(fPlain.y).Within(1e-6f), "Boost must not affect sim force (y)");
            Assert.That(fBoost.z, Is.EqualTo(fPlain.z).Within(1e-6f), "Boost must not affect sim force (z)");
        }

        // ------------------------------------------- The pure deploy-signal resolver (host-decoupled seam)

        [Test]
        public void ResolveActivate_BoostInput_RequestsDeploy_WhenEnabled()
        {
            // The overtake button (VehicleInput.Boost) alone must request a deploy — this is the wiring
            // under test: input.Boost -> ActivateRequested-equivalent deploy signal.
            Assert.IsTrue(DraftBoost.ResolveActivate(enabled: true, activateRequested: false,
                    boostInput: true, autoActivate: false, drafting: false, charge01: 0.5f),
                "the boost button alone must request a deploy when the feature is enabled");
        }

        [Test]
        public void ResolveActivate_PassesThroughActivateRequested()
        {
            Assert.IsTrue(DraftBoost.ResolveActivate(enabled: true, activateRequested: true,
                    boostInput: false, autoActivate: false, drafting: false, charge01: 0f),
                "an external ActivateRequested poke still deploys");
        }

        [Test]
        public void ResolveActivate_NoSignals_DoesNotDeploy()
        {
            Assert.IsFalse(DraftBoost.ResolveActivate(enabled: true, activateRequested: false,
                    boostInput: false, autoActivate: false, drafting: false, charge01: 1f),
                "no boost, no request and no auto-deploy -> the boost must stay stowed");
        }

        [Test]
        public void ResolveActivate_AutoActivate_OnlyWhenDraftingAndFull()
        {
            Assert.IsFalse(DraftBoost.ResolveActivate(true, false, false, autoActivate: true, drafting: false, charge01: 1f),
                "auto-deploy needs an active draft");
            Assert.IsFalse(DraftBoost.ResolveActivate(true, false, false, autoActivate: true, drafting: true, charge01: 0.9f),
                "auto-deploy needs a full reservoir");
            Assert.IsTrue(DraftBoost.ResolveActivate(true, false, false, autoActivate: true, drafting: true, charge01: 1f),
                "auto-deploy fires once the reservoir tops out while drafting");
        }

        [Test]
        public void ResolveActivate_Disabled_IgnoresEverySignal()
        {
            // The load-bearing no-op guarantee: with the feature gated OFF (the shipped default), even a
            // held boost button + a manual poke + a full auto-deploy setup must NOT request a deploy, so a
            // disabled DraftBoost can never touch the sim's BoostMult and driving feel is unchanged.
            Assert.IsFalse(DraftBoost.ResolveActivate(enabled: false, activateRequested: true,
                    boostInput: true, autoActivate: true, drafting: true, charge01: 1f),
                "a disabled DraftBoost must ignore the boost button and never request a deploy");
        }
    }
}
