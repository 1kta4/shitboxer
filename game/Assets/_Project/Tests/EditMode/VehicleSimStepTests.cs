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
            for (int i = 0; i < 50; i++) battered.ApplyDamage(1f);
            float flooredDurability = battered.Durability;
            float flooredMult = battered.DurabilityMult;
            Assert.Greater(flooredDurability, 0f, "the Durability floor must keep a wreck driveable, not zero it");
            Assert.That(flooredDurability, Is.EqualTo(VehicleSim.MinDurability).Within(1e-6f), "Durability did not clamp to its floor");
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
