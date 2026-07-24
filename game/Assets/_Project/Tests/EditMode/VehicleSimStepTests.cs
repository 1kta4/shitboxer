using NUnit.Framework;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Integration-level coverage of the headless sim's per-step force output: numerical
    /// sanity (no NaN/Infinity ever escapes), a grounded car settling to a bounded ride
    /// height (it must not sink without limit), and the blind-sink guard that cuts the
    /// extra-gravity assist once a car has been airborne long enough to be falling rather
    /// than hopping over a bump.
    /// </summary>
    public class VehicleSimStepTests : TestBase
    {
        private const float Dt = 0.02f; // one 50 Hz FixedUpdate step
        private const float G = 9.81f;  // matches the gravity constant the sim uses internally

        private static VehicleSim NewSim() => new VehicleSim(new VehicleSpec());

        // A flat, level, grounded contact for wheel i with the chassis (unrotated, at the XZ origin)
        // sitting chassisHeight metres above a y=0 floor. The suspension ray drops straight down along
        // world up, so HitDistance is just the attach point's height.
        private static GroundContact FlatContact(VehicleSim sim, int i, float chassisHeight, Vector3 pointVel)
        {
            Vector3 local = sim.WheelLocalPosition(i);
            Vector3 attach = new Vector3(local.x, chassisHeight + local.y, local.z);
            return new GroundContact
            {
                Grounded = true,
                HitDistance = attach.y, // attach point straight above the floor
                HitPoint = new Vector3(attach.x, 0f, attach.z),
                SurfaceNormal = Vector3.up,
                PointVelocity = pointVel,
                SuspensionUp = Vector3.up,
                WheelForward = Vector3.forward,
                WheelRight = Vector3.right,
                AttachPoint = attach,
            };
        }

        private static void AssertFinite(Vector3 v, string label)
        {
            Assert.IsFalse(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z), label + " is NaN: " + v);
            Assert.IsFalse(float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z),
                label + " is Infinity: " + v);
        }

        private static void AssertStepFinite(VehicleSim sim, ForceCommand[] forces)
        {
            for (int i = 0; i < forces.Length; i++)
            {
                AssertFinite(forces[i].Force, $"Force[{i}]");
                AssertFinite(forces[i].Position, $"Position[{i}]");
            }
            AssertFinite(sim.BodyTorque, "BodyTorque");
        }

        [Test]
        public void GroundedFlat_ManySteps_ForcesAndTorqueStayFinite()
        {
            var sim = NewSim();
            // Drive it hard so every force path runs: full throttle + hard steer + a stab of handbrake,
            // rolling forward fast enough to arm the yaw assist and build tyre slip.
            var input = new VehicleInput { Throttle = 1f, Steer = 1f, Handbrake = 0.5f };
            Vector3 vel = Vector3.forward * 22f;
            var contacts = new GroundContact[VehicleSim.WheelCount];
            for (int step = 0; step < 400; step++)
            {
                for (int i = 0; i < VehicleSim.WheelCount; i++)
                    contacts[i] = FlatContact(sim, i, 0.65f, vel);
                var forces = sim.Step(Dt, input, contacts, vel, Vector3.forward, Vector3.up, Vector3.zero);
                AssertStepFinite(sim, forces);
            }
        }

        [Test]
        public void PathologicalSpeed_AeroTermsDoNotOverflowToInfinity()
        {
            var sim = NewSim();
            var input = new VehicleInput { Throttle = 1f };
            Vector3 vel = Vector3.forward * 1_000_000f; // absurd — exercises the aero quadratic speed cap
            var contacts = new GroundContact[VehicleSim.WheelCount];
            for (int i = 0; i < VehicleSim.WheelCount; i++)
                contacts[i] = FlatContact(sim, i, 0.65f, vel);
            var forces = sim.Step(Dt, input, contacts, vel, Vector3.forward, Vector3.up, Vector3.zero);
            AssertStepFinite(sim, forces);
        }

        [Test]
        public void AllAirborne_ManySteps_ForcesAndTorqueStayFinite()
        {
            var sim = NewSim();
            var input = new VehicleInput { Throttle = 1f, Brake = 1f, Steer = -1f, Handbrake = 1f };
            var contacts = new GroundContact[VehicleSim.WheelCount]; // every Grounded == false by default
            for (int step = 0; step < 200; step++)
            {
                var forces = sim.Step(Dt, input, contacts, Vector3.zero, Vector3.forward, Vector3.up, Vector3.zero);
                AssertStepFinite(sim, forces);
            }
        }

        [Test]
        public void GroundedCar_SettlesToBoundedRideHeight_DoesNotSinkWithoutBound()
        {
            var sim = NewSim();
            var spec = sim.Spec;
            var input = default(VehicleInput);

            // Analytic settle point. The four springs carry the weight PLUS the always-on extra-gravity
            // assist (a grounded car has zero airborne time, so the assist is active), balanced against
            // the host's real gravity. At rest the aero and lateral cheats are zero, so this is exact.
            float weight = spec.MassKg * G * (1f + spec.ExtraGravity);
            float eqCompression = weight / (4f * spec.SpringRateNPerM);
            // Height at which compression is exactly 0 (springs just kissing the floor).
            float restHeight = spec.SuspensionRestLengthM - spec.AxleHeightM + spec.WheelRadiusM;
            float eqHeight = restHeight - eqCompression;

            const float maxReach = 0.9f; // the ray gives up beyond suspension travel + wheel radius

            // 1-DOF vertical drop test: hold the sim's forces against gravity and integrate the ride
            // height ourselves (the host normally does this on the rigidbody).
            float h = restHeight;
            float vy = 0f;
            float minHeight = h, maxHeight = h;

            var contacts = new GroundContact[VehicleSim.WheelCount];
            for (int step = 0; step < 400; step++)
            {
                Vector3 chassisVel = new Vector3(0f, vy, 0f);
                for (int i = 0; i < VehicleSim.WheelCount; i++)
                {
                    float attachY = h + sim.WheelLocalPosition(i).y;
                    contacts[i] = attachY <= maxReach
                        ? FlatContact(sim, i, h, chassisVel)
                        : default; // out of reach -> airborne wheel
                }

                var forces = sim.Step(Dt, input, contacts, chassisVel, Vector3.forward, Vector3.up, Vector3.zero);
                AssertStepFinite(sim, forces);

                float fy = -spec.MassKg * G; // host applies real gravity; the sim supplies everything else
                foreach (var f in forces) fy += f.Force.y;

                vy += fy / spec.MassKg * Dt;
                h += vy * Dt;
                minHeight = Mathf.Min(minHeight, h);
                maxHeight = Mathf.Max(maxHeight, h);
            }

            Assert.Greater(minHeight, 0.3f, "car sank far past its suspension travel (blind-sink / fall-through)");
            Assert.Less(maxHeight, 1.2f, "car launched off the ground");
            Assert.That(h, Is.EqualTo(eqHeight).Within(0.03f), "did not settle to the analytic ride height");
            Assert.That(vy, Is.EqualTo(0f).Within(0.05f), "ride height had not come to rest");
        }

        [Test]
        public void BlindSinkGuard_ExtraGravityAssistCutsOffAfterSustainedAirtime()
        {
            var sim = NewSim();
            float expectedDown = sim.Spec.MassKg * G * sim.Spec.ExtraGravity;

            // Still inside the airborne window: the heavy-landing assist is active, pulling straight down.
            Vector3 beforeCutoff = StepAirborneComForce(sim, 5); // 0.10 s of airtime
            Assert.That(beforeCutoff.y, Is.EqualTo(-expectedDown).Within(1f));
            Assert.That(beforeCutoff.x, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(beforeCutoff.z, Is.EqualTo(0f).Within(1e-3f));

            // Kept airborne past ExtraGravityMaxAirborneS: the guard must stop pulling the (likely sunk)
            // car further down, so the extra-gravity CoM force drops to zero.
            Vector3 afterCutoff = StepAirborneComForce(sim, 15); // total 0.40 s of airtime
            Assert.That(afterCutoff.magnitude, Is.EqualTo(0f).Within(1e-3f),
                "extra-gravity assist should be zero once airborne beyond the guard window");
            Assert.Less(afterCutoff.magnitude, beforeCutoff.magnitude, "assist did not actually drop off");
        }

        [Test]
        public void LandingAfterAirtime_ProducesNoUnboundedDamperSpike()
        {
            var sim = NewSim();
            var spec = sim.Spec;
            var input = default(VehicleInput);

            // Fall for a while so Compression[] is pinned at 0 and every wheel reads "was airborne"
            // going into the touchdown step.
            var airborne = new GroundContact[VehicleSim.WheelCount]; // all Grounded == false
            for (int step = 0; step < 10; step++)
                sim.Step(Dt, input, airborne, Vector3.zero, Vector3.forward, Vector3.up, Vector3.zero);

            // Touch down HARD in a single step: 0.3 m of compression appears at once. Without the
            // seed-across-the-transition fix the damper term alone would read compressionSpeed =
            // 0.3 / 0.02 = 15 m/s -> DamperRate * 15 = 67,500 N phantom spike. With the fix the rate is
            // 0 on the touchdown step, so the corner sees spring-only load (~SpringRate * 0.3 = 13,500 N).
            const float landingCompression = 0.3f;
            float restHeight = spec.SuspensionRestLengthM - spec.AxleHeightM + spec.WheelRadiusM;
            float h = restHeight - landingCompression;
            Vector3 downVel = Vector3.down * 12f; // slamming into the ground

            var contacts = new GroundContact[VehicleSim.WheelCount];
            for (int i = 0; i < VehicleSim.WheelCount; i++)
                contacts[i] = FlatContact(sim, i, h, downVel);
            var forces = sim.Step(Dt, input, contacts, downVel, Vector3.forward, Vector3.up, Vector3.zero);
            AssertStepFinite(sim, forces);

            for (int i = 0; i < VehicleSim.WheelCount; i++)
            {
                Assert.IsTrue(sim.Grounded[i], $"wheel {i} should have landed");
                Assert.LessOrEqual(sim.SuspensionForce[i], spec.MaxSuspensionForceN + 1f,
                    $"wheel {i} suspension force exceeded the ceiling on touchdown");
                // Well below the ~67.5 kN phantom spike AND below the ceiling, proving the seeding
                // killed the spike rather than the ceiling merely masking it.
                Assert.Less(sim.SuspensionForce[i], 20000f,
                    $"wheel {i} shows a damper spike on the airborne->grounded transition");
            }
        }

        [Test]
        public void HardCompression_SuspensionForceRespectsCeiling()
        {
            var sim = NewSim();
            var spec = sim.Spec;
            var input = default(VehicleInput);
            float ceiling = spec.MaxSuspensionForceN;

            // Bottom the suspension well past its travel limit: compression clamps to rest + travel, so
            // the spring plus the bump-stop together far exceed the ceiling and must be clipped to it.
            float restHeight = spec.SuspensionRestLengthM - spec.AxleHeightM + spec.WheelRadiusM;
            float h = restHeight - (spec.SuspensionRestLengthM + spec.SuspensionTravelM) - 0.1f; // over-travel

            var contacts = new GroundContact[VehicleSim.WheelCount];
            for (int step = 0; step < 5; step++) // steady-state, so this is spring + bump-stop, not a transient
            {
                for (int i = 0; i < VehicleSim.WheelCount; i++)
                    contacts[i] = FlatContact(sim, i, h, Vector3.zero);
                var forces = sim.Step(Dt, input, contacts, Vector3.zero, Vector3.forward, Vector3.up, Vector3.zero);
                AssertStepFinite(sim, forces);
            }

            for (int i = 0; i < VehicleSim.WheelCount; i++)
            {
                Assert.LessOrEqual(sim.SuspensionForce[i], ceiling + 1f,
                    $"wheel {i} suspension force exceeded MaxSuspensionForceN under a hard compression");
                // Spring (SpringRate * maxTravel) + bump-stop clearly overshoots the ceiling here, so a
                // working clamp must pin the force exactly at it.
                Assert.That(sim.SuspensionForce[i], Is.EqualTo(ceiling).Within(1f),
                    $"wheel {i} did not clip to the force ceiling");
            }
        }

        [Test]
        public void Validate_ZeroedDenominators_YieldFiniteNonNaNForces()
        {
            // A spec whose divisor fields have all been scaled to 0 (a pathological asset or part-swap).
            // Untouched these produce Inf/NaN in the slip/steer/drivetrain maths; the VehicleSim ctor must
            // Validate() them up to positive minimums first.
            var spec = new VehicleSpec
            {
                MassKg = 0f,
                WheelbaseM = 0f,
                WheelRadiusM = 0f,
                SteerFalloffSpeedMps = 0f,
                MaxSuspensionForceN = 0f,
            };
            spec.FrontTyre.PeakSlipRatio = 0f;
            spec.FrontTyre.PeakSlipAngleDeg = 0f;
            spec.FrontTyre.RatedLoadN = 0f;
            spec.RearTyre.PeakSlipRatio = 0f;
            spec.RearTyre.PeakSlipAngleDeg = 0f;
            spec.RearTyre.RatedLoadN = 0f;

            var sim = new VehicleSim(spec); // ctor calls Validate(), clamping every zero above

            Assert.Greater(spec.MassKg, 0f, "MassKg still zero after Validate");
            Assert.Greater(spec.WheelbaseM, 0f, "WheelbaseM still zero after Validate");
            Assert.Greater(spec.WheelRadiusM, 0f, "WheelRadiusM still zero after Validate");
            Assert.Greater(spec.SteerFalloffSpeedMps, 0f, "SteerFalloffSpeedMps still zero after Validate");
            Assert.Greater(spec.MaxSuspensionForceN, 0f, "MaxSuspensionForceN still zero after Validate");
            Assert.Greater(spec.FrontTyre.PeakSlipRatio, 0f, "FrontTyre.PeakSlipRatio still zero after Validate");
            Assert.Greater(spec.FrontTyre.PeakSlipAngleDeg, 0f, "FrontTyre.PeakSlipAngleDeg still zero after Validate");
            Assert.Greater(spec.FrontTyre.RatedLoadN, 0f, "FrontTyre.RatedLoadN still zero after Validate");

            // Idempotent: re-validating an already-clamped spec must not move anything.
            float radiusAfterFirst = spec.WheelRadiusM;
            float massAfterFirst = spec.MassKg;
            spec.Validate();
            Assert.AreEqual(radiusAfterFirst, spec.WheelRadiusM, "Validate is not idempotent (WheelRadiusM)");
            Assert.AreEqual(massAfterFirst, spec.MassKg, "Validate is not idempotent (MassKg)");

            // Drive it hard through every force path: the clamped divisors must keep it finite.
            var input = new VehicleInput { Throttle = 1f, Steer = 1f, Handbrake = 1f };
            Vector3 vel = Vector3.forward * 15f;
            var contacts = new GroundContact[VehicleSim.WheelCount];
            for (int step = 0; step < 50; step++)
            {
                for (int i = 0; i < VehicleSim.WheelCount; i++)
                    contacts[i] = FlatContact(sim, i, 0.3f, vel);
                var forces = sim.Step(Dt, input, contacts, vel, Vector3.forward, Vector3.up, Vector3.zero);
                AssertStepFinite(sim, forces);
                for (int i = 0; i < VehicleSim.WheelCount; i++)
                {
                    Assert.IsFalse(float.IsNaN(sim.SlipRatio[i]) || float.IsInfinity(sim.SlipRatio[i]),
                        $"SlipRatio[{i}] non-finite");
                    Assert.IsFalse(float.IsNaN(sim.SuspensionForce[i]) || float.IsInfinity(sim.SuspensionForce[i]),
                        $"SuspensionForce[{i}] non-finite");
                }
            }
        }

        [Test]
        public void ApplyDamage_LowersDurabilityMult_ReducesForces_ClampsAtFloor_ResetsOnRebuild()
        {
            var fresh = NewSim();
            Assert.That(fresh.Durability, Is.EqualTo(1f).Within(1e-6f), "fresh car should start at full durability");
            Assert.That(fresh.DurabilityMult, Is.EqualTo(1f).Within(1e-6f), "fresh car should have a full-performance DurabilityMult");

            // Battered car: wear it hard, but a single hit must not reach the floor here (that is tested below).
            var battered = NewSim();
            battered.ApplyDamage(0.4f);
            Assert.Less(battered.Durability, fresh.Durability, "ApplyDamage did not lower Durability");
            Assert.Less(battered.DurabilityMult, fresh.DurabilityMult, "ApplyDamage did not lower DurabilityMult");

            // GRIP path: a hard-braking, sliding tyre produces a grip-limited longitudinal force (mu * load),
            // and mu folds in DurabilityMult — so the battered car must produce less braking force. Grip does
            // not depend on the drivetrain here, so this is a clean deterministic probe of the grip fold-in.
            var braking = new VehicleInput { Brake = 1f };
            Vector3 brakeVel = Vector3.forward * 15f;
            float freshBrakeForce = StepAndMeasureHorizontalForce(NewSimWith(0f), braking, brakeVel, 60, 10);
            float batteredBrakeForce = StepAndMeasureHorizontalForce(NewSimWith(0.4f), braking, brakeVel, 60, 10);
            Assert.Greater(freshBrakeForce, 1f, "braking scenario produced no tyre force (test is degenerate)");
            Assert.Less(batteredBrakeForce, freshBrakeForce, "a battered car should produce less grip (braking) force");

            // DRIVE path: cruising at speed under full throttle settles below redline and below the grip
            // limit, so the rear tyre force tracks the delivered engine torque — which folds in DurabilityMult
            // (power). The battered car therefore produces less drive force.
            var driving = new VehicleInput { Throttle = 1f };
            Vector3 driveVel = Vector3.forward * 40f;
            float freshDriveForce = StepAndMeasureHorizontalForce(NewSimWith(0f), driving, driveVel, 200, 30);
            float batteredDriveForce = StepAndMeasureHorizontalForce(NewSimWith(0.4f), driving, driveVel, 200, 30);
            Assert.Greater(freshDriveForce, 1f, "drive scenario produced no tyre force (test is degenerate)");
            Assert.Less(batteredDriveForce, freshDriveForce, "a battered car should produce less drive force");

            // Floor: hammering it far past the floor cannot drop Durability (or its mult) without bound.
            // Since decision 15 the floor is ZERO — a wreck is a real state the host retires, not a
            // hobbled-but-driveable car.
            for (int i = 0; i < 50; i++) battered.ApplyDamage(1f);
            float flooredDurability = battered.Durability;
            float flooredMult = battered.DurabilityMult;
            Assert.That(flooredDurability, Is.EqualTo(VehicleSim.MinDurability).Within(1e-6f), "Durability did not clamp to its floor");
            Assert.IsTrue(battered.IsDestroyed, "a car hammered to the floor should read as destroyed");
            Assert.That(flooredMult, Is.EqualTo(0f).Within(1e-6f), "a wreck should have zero performance — retirement, not pace, is the floor");
            battered.ApplyDamage(1f);
            Assert.That(battered.Durability, Is.EqualTo(flooredDurability).Within(1e-6f), "Durability fell below its floor");
            Assert.That(battered.DurabilityMult, Is.EqualTo(flooredMult).Within(1e-6f), "DurabilityMult fell below its floor");

            // Non-positive / NaN damage is a no-op.
            battered.ApplyDamage(0f);
            battered.ApplyDamage(-0.3f);
            battered.ApplyDamage(float.NaN);
            Assert.That(battered.Durability, Is.EqualTo(flooredDurability).Within(1e-6f), "non-positive/NaN damage changed Durability");

            // A rebuilt sim (a fresh car each race) is back to full durability.
            var rebuilt = NewSim();
            Assert.That(rebuilt.Durability, Is.EqualTo(1f).Within(1e-6f), "a rebuilt sim should reset to full durability");
            Assert.That(rebuilt.DurabilityMult, Is.EqualTo(1f).Within(1e-6f), "a rebuilt sim should reset DurabilityMult to full");
        }

        [Test]
        public void SetDurability_ClampsToFloorAndCeiling_AndDrivesDurabilityMult()
        {
            var sim = NewSim();

            // A mid-range value assigns straight through and pulls the performance mult below full,
            // so RunDirector can carry a run's accumulated wear onto a freshly-rebuilt (full) sim.
            sim.SetDurability(0.7f);
            Assert.That(sim.Durability, Is.EqualTo(0.7f).Within(1e-6f), "mid-range durability did not set through");
            Assert.Less(sim.DurabilityMult, 1f, "wear should pull DurabilityMult below full");

            // Above 1 clamps to a pristine car — a repair restoring CarDurability to 1 lands here.
            sim.SetDurability(1.5f);
            Assert.That(sim.Durability, Is.EqualTo(1f).Within(1e-6f), "durability did not clamp to the ceiling of 1");
            Assert.That(sim.DurabilityMult, Is.EqualTo(1f).Within(1e-6f), "a pristine car should have a full-performance mult");

            // Below the floor clamps up to MinDurability (zero since decision 15 — the host retires a wreck).
            sim.SetDurability(0f);
            Assert.That(sim.Durability, Is.EqualTo(VehicleSim.MinDurability).Within(1e-6f), "durability did not clamp up to its floor");
            sim.SetDurability(-5f);
            Assert.That(sim.Durability, Is.EqualTo(VehicleSim.MinDurability).Within(1e-6f), "a negative durability did not clamp to the floor");

            // At the floor SetDurability and hammering ApplyDamage to the floor must agree on the mult.
            var floored = NewSim();
            for (int i = 0; i < 50; i++) floored.ApplyDamage(1f);
            Assert.That(sim.DurabilityMult, Is.EqualTo(floored.DurabilityMult).Within(1e-6f),
                "SetDurability(floor) and ApplyDamage-to-floor should yield the same DurabilityMult");
        }

        // -------------------------------------------------------------- internal substep coverage
        //
        // Step() advances the stiff wheel-spin / tyre-slip integration over several internal substeps
        // of dt/N — holding the drivetrain, steering, suspension load and aero fixed — and hands back
        // the per-wheel force AVERAGED over those substeps (so the net impulse over dt is unchanged).
        // These tests assert the substep scheme keeps the sim finite and physically bounded under hard,
        // sustained driving: no blow-up over many steps, a stable ride height, and per-wheel tyre forces
        // that never exceed the friction circle (mu <= PeakMu, so the in-plane tyre force on a wheel is
        // at most PeakMu * its suspension load). If substepping had destabilised the integration these
        // bounds would be the first thing to break.

        [Test]
        public void Substepped_DrivenCar_ManySteps_ForcesStayFiniteAndSanelyBounded()
        {
            var sim = NewSim();
            var spec = sim.Spec;
            // Drive it as hard as the model allows: full throttle, full lock, a stab of handbrake,
            // rolling fast enough to build heavy slip on every wheel and run every substepped path.
            var input = new VehicleInput { Throttle = 1f, Steer = 1f, Handbrake = 0.5f };
            Vector3 vel = Vector3.forward * 22f;

            // Physical ceiling on any single wheel force: the vertical part is capped at
            // MaxSuspensionForceN and the in-plane (tyre) part at PeakMu * that same ceiling, so the
            // magnitude is at most sqrt(1 + PeakMu^2) * ceiling. A wide safety factor keeps this from
            // being brittle while still catching a substep-induced blow-up.
            float peakMu = Mathf.Max(spec.FrontTyre.PeakMu, spec.RearTyre.PeakMu);
            float wheelForceCeiling = spec.MaxSuspensionForceN * Mathf.Sqrt(1f + peakMu * peakMu) * 1.5f;

            var contacts = new GroundContact[VehicleSim.WheelCount];
            for (int step = 0; step < 400; step++)
            {
                for (int i = 0; i < VehicleSim.WheelCount; i++)
                    contacts[i] = FlatContact(sim, i, 0.65f, vel);
                var forces = sim.Step(Dt, input, contacts, vel, Vector3.forward, Vector3.up, Vector3.zero);
                AssertStepFinite(sim, forces);
                for (int i = 0; i < VehicleSim.WheelCount; i++)
                    Assert.Less(forces[i].Force.magnitude, wheelForceCeiling,
                        $"wheel {i} force blew past its physical ceiling at step {step}");
            }
        }

        [Test]
        public void Substepped_DrivenCar_SettlesToStableRideHeight()
        {
            var sim = NewSim();
            var spec = sim.Spec;
            // A car under full throttle, rolling forward, on flat ground. Integrate ONLY the vertical
            // ride-height DOF (the horizontal drive/grip forces lie in the ground plane and drop out of
            // fy), exactly as the undriven settle test does — hard driving must not destabilise the
            // height under the substepped integration.
            var input = new VehicleInput { Throttle = 1f };
            const float forwardSpeed = 14f;

            float restHeight = spec.SuspensionRestLengthM - spec.AxleHeightM + spec.WheelRadiusM;
            const float maxReach = 0.9f; // the ray gives up beyond suspension travel + wheel radius

            float h = restHeight;
            float vy = 0f;
            float minHeight = h, maxHeight = h;

            var contacts = new GroundContact[VehicleSim.WheelCount];
            for (int step = 0; step < 500; step++)
            {
                Vector3 chassisVel = new Vector3(0f, vy, forwardSpeed);
                for (int i = 0; i < VehicleSim.WheelCount; i++)
                {
                    float attachY = h + sim.WheelLocalPosition(i).y;
                    contacts[i] = attachY <= maxReach
                        ? FlatContact(sim, i, h, chassisVel)
                        : default; // out of reach -> airborne wheel
                }

                var forces = sim.Step(Dt, input, contacts, chassisVel, Vector3.forward, Vector3.up, Vector3.zero);
                AssertStepFinite(sim, forces);

                float fy = -spec.MassKg * G; // host applies real gravity; the sim supplies everything else
                foreach (var f in forces) fy += f.Force.y;
                vy += fy / spec.MassKg * Dt;
                h += vy * Dt;
                minHeight = Mathf.Min(minHeight, h);
                maxHeight = Mathf.Max(maxHeight, h);
            }

            Assert.Greater(minHeight, 0.3f, "driven car sank far past its suspension travel");
            Assert.Less(maxHeight, 1.2f, "driven car launched off the ground");
            Assert.That(vy, Is.EqualTo(0f).Within(0.06f), "ride height had not come to rest");
            // Settles between clearly compressed and free rest — a sane, stable ride height.
            Assert.That(h, Is.InRange(0.45f, restHeight + 1e-3f), "did not settle to a stable ride height");
        }

        [Test]
        public void Substepped_HardDriveAndSlide_TyreForcesRespectFrictionCircle()
        {
            var sim = NewSim();
            var spec = sim.Spec;
            // Full throttle while the whole car also slides sideways: the contact point velocity carries
            // a large lateral component, so each tyre works deep in combined slip (longitudinal wheelspin
            // + lateral slide) right up against the friction circle. The substepped integration must never
            // let a wheel's in-plane tyre force exceed mu * load, and mu <= PeakMu, so the per-wheel
            // ceiling is PeakMu * its suspension load. Well clear of the low-speed stiction regime.
            var input = new VehicleInput { Throttle = 1f, Steer = 1f };
            Vector3 vel = Vector3.forward * 18f + Vector3.right * 11f; // forward drive + hard sideways slide

            var contacts = new GroundContact[VehicleSim.WheelCount];
            for (int step = 0; step < 200; step++)
            {
                for (int i = 0; i < VehicleSim.WheelCount; i++)
                    contacts[i] = FlatContact(sim, i, 0.65f, vel);
                var forces = sim.Step(Dt, input, contacts, vel, Vector3.forward, Vector3.up, Vector3.zero);
                AssertStepFinite(sim, forces);

                for (int i = 0; i < VehicleSim.WheelCount; i++)
                {
                    Assert.IsTrue(sim.Grounded[i], $"wheel {i} should be grounded");
                    float load = sim.SuspensionForce[i];
                    Assert.Greater(load, 0f, $"wheel {i} carried no load (test is degenerate)");

                    // On the flat contact SuspensionUp is world-up, so the wheel's whole in-plane (x,z)
                    // force is the tyre long/lat force; the suspension load is the pure-vertical y term.
                    float peakMu = sim.IsFrontWheel(i) ? spec.FrontTyre.PeakMu : spec.RearTyre.PeakMu;
                    Vector3 f = forces[i].Force;
                    float horizontal = new Vector3(f.x, 0f, f.z).magnitude;
                    Assert.LessOrEqual(horizontal, peakMu * load * 1.02f + 1f,
                        $"wheel {i} tyre force exceeded the friction circle (PeakMu*load) at step {step}");
                }
            }
        }

        [Test]
        public void SurfaceGripMult_LowGripSurfaceScalesTyreForcesDown_GripOneReproducesTodaysForces()
        {
            // A hard-braking, sliding tyre is grip-limited (its horizontal force ~= mu * load), and
            // SurfaceGripMult folds straight into mu alongside the combat/wear mults. So a low-grip
            // patch (grass/dirt) must scale that force down by the same factor, while a grip-1 — or
            // unset/default — contact reproduces today's full-grip force EXACTLY. Full braking locks
            // the wheels identically regardless of grip, so the slip (and thus the tyre curve value) is
            // the same across runs and the only remaining difference is the SurfaceGripMult factor.
            var braking = new VehicleInput { Brake = 1f };
            Vector3 brakeVel = Vector3.forward * 15f;

            // Today's forces: the existing helper builds contacts WITHOUT setting SurfaceGripMult, so an
            // unset/default contact must read as full grip (1) and reproduce the pre-surface baseline.
            float defaultForce = StepAndMeasureHorizontalForce(NewSim(), braking, brakeVel, 60, 10);
            float gripOneForce = StepAndMeasureHorizontalForceOnSurface(NewSim(), braking, brakeVel, 60, 10, 1f);
            float halfGripForce = StepAndMeasureHorizontalForceOnSurface(NewSim(), braking, brakeVel, 60, 10, 0.5f);

            Assert.Greater(defaultForce, 1f, "braking scenario produced no tyre force (test is degenerate)");

            // An explicit grip-1 contact and an unset/default contact must be identical -> default reads as 1.
            Assert.That(gripOneForce, Is.EqualTo(defaultForce).Within(defaultForce * 0.01f + 1f),
                "an explicit SurfaceGripMult of 1 did not reproduce today's (unset/default) forces");

            // A low-grip surface reduces tyre force, proportionally to the multiplier.
            Assert.Less(halfGripForce, defaultForce, "a low-grip surface should reduce tyre force");
            Assert.That(halfGripForce / defaultForce, Is.EqualTo(0.5f).Within(0.05f),
                "low-grip tyre force did not drop in proportion to SurfaceGripMult");
        }

        [Test]
        public void DraftDragMult_BelowOne_ReducesAeroDrag_DefaultReproducesTodaysDrag_AndTowsCarFaster()
        {
            // Dead straight at speed, the aero CoM force's forward (z) component is PURELY the drag term
            // (-DragCoeff * DraftDragMult * |v| * v_z): downforce and the extra-gravity assist are vertical,
            // and the yaw/lateral/flat-ride assists are all zero going perfectly straight. So forces[WheelCount]
            // isolates aero drag, and DraftDragMult must scale it (only) — leaving downforce alone.
            const float speed = 40f;
            Vector3 vel = Vector3.forward * speed;

            var baseline = NewSim();
            float baseDragZ = AeroForwardForce(baseline, vel, draftMult: 1f);

            // Default DraftDragMult (== 1, no ApplyDraft call) reproduces today's drag EXACTLY (the analytic term).
            float expected = -baseline.Spec.DragCoeff * speed * speed;
            Assert.That(baseDragZ, Is.EqualTo(expected).Within(Mathf.Abs(expected) * 0.001f + 1e-3f),
                "the default DraftDragMult of 1 did not reproduce today's aero drag");

            // A tow (DraftDragMult < 1) applied to the DRAG term only must shrink the drag force at the same speed.
            float draftDragZ = AeroForwardForce(NewSim(), vel, draftMult: 0.6f);
            Assert.Less(Mathf.Abs(draftDragZ), Mathf.Abs(baseDragZ),
                "a slipstream (DraftDragMult < 1) did not reduce the aero drag force");

            // End-to-end payoff: less drag every step means a drafting car reaches/holds a higher speed under
            // throttle than an identical car in clean air.
            float cleanAirSpeed = RunForwardSpeed(NewSim(), draft: false, steps: 400);
            float draftingSpeed = RunForwardSpeed(NewSim(), draft: true, steps: 400);
            Assert.Greater(draftingSpeed, cleanAirSpeed,
                "a drafting car should reach/hold a higher speed than one in clean air");
        }

        // Steps a few times at a fixed pure-forward velocity (re-asserting a tow each step when draftMult < 1),
        // then returns the forward (z) component of the aero CoM force — which going dead straight is purely the
        // drag term. draftMult >= 1 leaves the sim in clean air, exercising today's unchanged drag.
        private static float AeroForwardForce(VehicleSim sim, Vector3 vel, float draftMult)
        {
            var input = default(VehicleInput);
            var contacts = new GroundContact[VehicleSim.WheelCount];
            ForceCommand[] forces = null;
            for (int step = 0; step < 3; step++) // let steering/decay settle; the draft is re-asserted each step
            {
                if (draftMult < 1f) sim.ApplyDraft(draftMult);
                for (int i = 0; i < VehicleSim.WheelCount; i++)
                    contacts[i] = FlatContact(sim, i, 0.65f, vel);
                forces = sim.Step(Dt, input, contacts, vel, Vector3.forward, Vector3.up, Vector3.zero);
                AssertStepFinite(sim, forces);
            }
            return forces[VehicleSim.WheelCount].Force.z;
        }

        // Integrates forward speed under full throttle on flat ground (re-asserting a tow each step when
        // drafting) and returns the speed reached after the given number of steps. At any given speed both
        // cars make the same drive force, so the drafting car's lower drag gives it more net forward force
        // every step -> it pulls ahead and holds a higher speed. Only the vertical DOF is held fixed (flat
        // ground), matching how the ride-height tests integrate a single DOF from the sim's forces.
        private static float RunForwardSpeed(VehicleSim sim, bool draft, int steps)
        {
            var input = new VehicleInput { Throttle = 1f };
            float vz = 10f; // a rolling start, already fast enough for aero drag to matter
            var contacts = new GroundContact[VehicleSim.WheelCount];
            for (int step = 0; step < steps; step++)
            {
                if (draft) sim.ApplyDraft(0.6f);
                Vector3 vel = new Vector3(0f, 0f, vz);
                for (int i = 0; i < VehicleSim.WheelCount; i++)
                    contacts[i] = FlatContact(sim, i, 0.65f, vel);
                var forces = sim.Step(Dt, input, contacts, vel, Vector3.forward, Vector3.up, Vector3.zero);
                AssertStepFinite(sim, forces);

                float fz = 0f;
                foreach (var f in forces) fz += f.Force.z;
                vz += fz / sim.Spec.MassKg * Dt;
            }
            return vz;
        }

        // FlatContact with an explicit ground-grip multiplier (grass/dirt < 1); everything else identical.
        private static GroundContact SurfaceContact(VehicleSim sim, int i, float chassisHeight, Vector3 pointVel,
            float surfaceGrip)
        {
            var c = FlatContact(sim, i, chassisHeight, pointVel);
            c.SurfaceGripMult = surfaceGrip;
            return c;
        }

        // As StepAndMeasureHorizontalForce, but stamps a SurfaceGripMult on every contact so the grass/dirt
        // grip fold-in can be measured against the full-grip baseline.
        private static float StepAndMeasureHorizontalForceOnSurface(VehicleSim sim, in VehicleInput input,
            Vector3 vel, int steps, int avgLast, float surfaceGrip)
        {
            var contacts = new GroundContact[VehicleSim.WheelCount];
            float sum = 0f;
            int counted = 0;
            for (int step = 0; step < steps; step++)
            {
                for (int i = 0; i < VehicleSim.WheelCount; i++)
                    contacts[i] = SurfaceContact(sim, i, 0.65f, vel, surfaceGrip);
                var forces = sim.Step(Dt, input, contacts, vel, Vector3.forward, Vector3.up, Vector3.zero);
                AssertStepFinite(sim, forces);
                if (step >= steps - avgLast)
                {
                    float horizontal = 0f;
                    for (int i = 0; i < VehicleSim.WheelCount; i++)
                    {
                        Vector3 f = forces[i].Force;
                        horizontal += new Vector3(f.x, 0f, f.z).magnitude;
                    }
                    sum += horizontal;
                    counted++;
                }
            }
            return counted > 0 ? sum / counted : 0f;
        }

        // A fresh sim pre-worn by the given damage amount (0 = untouched).
        private static VehicleSim NewSimWith(float damage)
        {
            var sim = NewSim();
            if (damage > 0f) sim.ApplyDamage(damage);
            return sim;
        }

        // Steps the sim on flat, grounded contacts at a fixed chassis velocity and returns the summed
        // horizontal (drive/grip) wheel force, averaged over the final avgLast steps. Suspension load is
        // vertical (along world up) so it drops out of the horizontal component, isolating the tyre forces
        // that DurabilityMult scales.
        private static float StepAndMeasureHorizontalForce(VehicleSim sim, in VehicleInput input, Vector3 vel,
            int steps, int avgLast)
        {
            var contacts = new GroundContact[VehicleSim.WheelCount];
            float sum = 0f;
            int counted = 0;
            for (int step = 0; step < steps; step++)
            {
                for (int i = 0; i < VehicleSim.WheelCount; i++)
                    contacts[i] = FlatContact(sim, i, 0.65f, vel);
                var forces = sim.Step(Dt, input, contacts, vel, Vector3.forward, Vector3.up, Vector3.zero);
                AssertStepFinite(sim, forces);
                if (step >= steps - avgLast)
                {
                    float horizontal = 0f;
                    for (int i = 0; i < VehicleSim.WheelCount; i++)
                    {
                        Vector3 f = forces[i].Force;
                        horizontal += new Vector3(f.x, 0f, f.z).magnitude;
                    }
                    sum += horizontal;
                    counted++;
                }
            }
            return counted > 0 ? sum / counted : 0f;
        }

        // Steps the sim fully airborne (no input, no velocity) and returns the CoM force after the given
        // number of steps. With every wheel ungrounded and zero speed the only CoM contribution is the
        // extra-gravity assist, so this isolates the blind-sink guard from tyre/aero forces.
        private static Vector3 StepAirborneComForce(VehicleSim sim, int steps)
        {
            var contacts = new GroundContact[VehicleSim.WheelCount]; // all Grounded == false
            var input = default(VehicleInput);
            Vector3 com = Vector3.zero;
            for (int i = 0; i < steps; i++)
            {
                var forces = sim.Step(Dt, input, contacts, Vector3.zero, Vector3.forward, Vector3.up, Vector3.zero);
                com = forces[VehicleSim.WheelCount].Force; // struct copy — safe to keep past the next Step
            }
            return com;
        }
    }
}
