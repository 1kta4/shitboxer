using NUnit.Framework;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the KERS-style overtake boost: the pure <see cref="DraftBoostModel"/> reservoir (fills only
    /// while drafting, clamps to [0,1], deploys a bounded multiplier for its duration then releases), the
    /// <see cref="VehicleSim.BoostMult"/> drivetrain fold (BoostMult == 1 is byte-for-byte the baseline;
    /// above 1 delivers more drive torque), and the <see cref="DraftBoost"/> host gate (disabled never
    /// touches BoostMult, enabled drives it). The load-bearing contract: today's driving feel is unchanged
    /// until the feature is enabled.
    /// </summary>
    public class DraftBoostTests : TestBase
    {
        private const float Dt = 0.02f; // one 50 Hz FixedUpdate step

        // ---------------------------------------------------------------- DraftBoostModel, direct

        [Test]
        public void FreshModel_IsNoOp_EmptyChargeBoostOne()
        {
            var m = new DraftBoostModel();
            Assert.That(m.Charge01, Is.EqualTo(0f).Within(1e-6f), "a fresh reservoir is empty");
            Assert.IsFalse(m.Active, "a fresh model is not boosting");
            Assert.That(m.BoostMult, Is.EqualTo(1f).Within(1e-6f), "an un-stepped model must be a perfect no-op");
        }

        [Test]
        public void Charge_FillsOnlyWhileDrafting_ClampedTo01()
        {
            var m = new DraftBoostModel(); // idle drain 0 by default

            // Filling while drafting is monotonic up and never leaves [0,1].
            float prev = m.Charge01;
            for (int i = 0; i < 50; i++)
            {
                m.Step(Dt, drafting: true, activate: false);
                Assert.GreaterOrEqual(m.Charge01, prev, "drafting must not lower the reservoir");
                Assert.GreaterOrEqual(m.Charge01, 0f);
                Assert.LessOrEqual(m.Charge01, 1f);
                prev = m.Charge01;
            }
            Assert.Greater(m.Charge01, 0f, "drafting should have accrued some charge");

            // Not drafting (and not boosting) with the default zero idle-drain holds the charge put.
            float held = m.Charge01;
            for (int i = 0; i < 50; i++) m.Step(Dt, drafting: false, activate: false);
            Assert.That(m.Charge01, Is.EqualTo(held).Within(1e-6f),
                "with zero idle-drain, not drafting must neither fill nor drain the reservoir");

            // Over-filling clamps at exactly 1.
            for (int i = 0; i < 2000; i++) m.Step(Dt, drafting: true, activate: false);
            Assert.That(m.Charge01, Is.EqualTo(1f).Within(1e-6f), "reservoir clamps to a full 1");
        }

        [Test]
        public void IdleDrain_BleedsReservoir_ClampedAtZero()
        {
            var m = new DraftBoostModel { IdleDrainPerSecond = 0.5f };
            for (int i = 0; i < 400; i++) m.Step(Dt, drafting: true, activate: false); // fill up
            Assert.That(m.Charge01, Is.EqualTo(1f).Within(1e-4f));

            for (int i = 0; i < 50; i++) m.Step(Dt, drafting: false, activate: false);
            Assert.Less(m.Charge01, 1f, "idle drain must bleed the reservoir when the tunable is set");

            for (int i = 0; i < 2000; i++) m.Step(Dt, drafting: false, activate: false);
            Assert.That(m.Charge01, Is.EqualTo(0f).Within(1e-6f), "idle drain floors at 0");
        }

        [Test]
        public void Boost_AppliesBoundedMult_ForItsDuration_ThenReleasesToOne()
        {
            var m = new DraftBoostModel(); // MaxBoostMult 1.15, MinActivate 0.25
            for (int i = 0; i < 400; i++) m.Step(Dt, drafting: true, activate: false);
            Assert.That(m.Charge01, Is.EqualTo(1f).Within(1e-4f), "precondition: full reservoir");
            Assert.IsFalse(m.Active);
            Assert.That(m.BoostMult, Is.EqualTo(1f).Within(1e-6f), "no boost before deploy");

            // Deploy.
            m.Step(Dt, drafting: false, activate: true);
            Assert.IsTrue(m.Active, "deploy with a full reservoir must engage the boost");
            Assert.That(m.BoostMult, Is.EqualTo(1.15f).Within(1e-4f), "deployed multiplier is the bounded MaxBoostMult");
            Assert.Less(m.Charge01, 1f, "deploying spends the reservoir");

            // Holds through its duration while the reservoir lasts.
            for (int i = 0; i < 10; i++) m.Step(Dt, drafting: false, activate: false);
            Assert.IsTrue(m.Active, "boost persists while the reservoir is not yet dry");
            Assert.Greater(m.BoostMult, 1f);

            // Runs the reservoir dry -> releases back to a nominal 1.
            for (int i = 0; i < 400; i++) m.Step(Dt, drafting: false, activate: false);
            Assert.IsFalse(m.Active, "an empty reservoir releases the boost");
            Assert.That(m.Charge01, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(m.BoostMult, Is.EqualTo(1f).Within(1e-6f), "released boost returns the multiplier to 1");
        }

        [Test]
        public void Boost_RefusesToDeploy_BelowMinCharge()
        {
            var m = new DraftBoostModel { MinActivateCharge01 = 0.5f };
            for (int i = 0; i < 20; i++) m.Step(Dt, drafting: true, activate: false); // a sliver of charge
            Assert.Less(m.Charge01, 0.5f, "precondition: below the activation threshold");

            m.Step(Dt, drafting: false, activate: true);
            Assert.IsFalse(m.Active, "must not deploy below MinActivateCharge01");
            Assert.That(m.BoostMult, Is.EqualTo(1f).Within(1e-6f), "a refused deploy leaves the multiplier at 1");
        }

        [Test]
        public void BoostMult_IsBounded_ToAbsoluteCeiling()
        {
            var m = new DraftBoostModel { MaxBoostMult = 99f, MinActivateCharge01 = 0f };
            for (int i = 0; i < 400; i++) m.Step(Dt, drafting: true, activate: false);
            m.Step(Dt, drafting: false, activate: true);
            Assert.IsTrue(m.Active);
            Assert.That(m.BoostMult, Is.EqualTo(DraftBoostModel.AbsoluteMaxBoostMult).Within(1e-6f),
                "a runaway MaxBoostMult must be clamped to the absolute ceiling");
        }

        [Test]
        public void BoostMult_NeverReducesPower_EvenWithSubOneTunable()
        {
            // A misconfigured MaxBoostMult below 1 must never SAP power (boost only ever adds).
            var m = new DraftBoostModel { MaxBoostMult = 0.5f, MinActivateCharge01 = 0f };
            for (int i = 0; i < 400; i++) m.Step(Dt, drafting: true, activate: false);
            m.Step(Dt, drafting: false, activate: true);
            Assert.IsTrue(m.Active);
            Assert.That(m.BoostMult, Is.EqualTo(1f).Within(1e-6f), "boost is floored at 1 — it never cuts power");
        }

        [Test]
        public void Step_NonPositiveDt_IsANoOp()
        {
            var m = new DraftBoostModel();
            for (int i = 0; i < 400; i++) m.Step(Dt, drafting: true, activate: false);
            float charge = m.Charge01;
            m.Step(0f, drafting: true, activate: true);
            m.Step(-1f, drafting: true, activate: true);
            Assert.That(m.Charge01, Is.EqualTo(charge).Within(1e-6f), "a non-positive dt must not integrate");
            Assert.IsFalse(m.Active, "a non-positive dt must not deploy");
        }

        [Test]
        public void Reset_ClearsChargeAndBoost()
        {
            var m = new DraftBoostModel { MinActivateCharge01 = 0f };
            for (int i = 0; i < 400; i++) m.Step(Dt, drafting: true, activate: false);
            m.Step(Dt, drafting: false, activate: true);
            Assert.IsTrue(m.Active);

            m.Reset();
            Assert.That(m.Charge01, Is.EqualTo(0f).Within(1e-6f), "Reset empties the reservoir");
            Assert.IsFalse(m.Active, "Reset ends any boost");
            Assert.That(m.BoostMult, Is.EqualTo(1f).Within(1e-6f), "Reset returns the multiplier to 1");
        }

        // ---------------------------------------------------------------- VehicleSim.BoostMult fold

        // A flat, level, grounded contact for wheel i (mirrors VehicleSimStepTests / TyreWearTests).
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

        // Steps a grounded car under full throttle from walking pace. The chassis velocity is held fixed
        // (a static harness), so extra drive torque spins the driven wheels up further. Optionally re-asserts
        // BoostMult every step exactly as a host would; returns the last RL (rear, driven on the default RWD
        // spec) tyre force and, via out, that wheel's final angular velocity.
        private static Vector3 RunThrottle(VehicleSim sim, int steps, bool writeBoost, float boostMult, out float drivenSpin)
        {
            var input = new VehicleInput { Throttle = 1f };
            Vector3 vel = Vector3.forward * 3f;
            var contacts = new GroundContact[VehicleSim.WheelCount];
            Vector3 lastRearForce = Vector3.zero;
            for (int step = 0; step < steps; step++)
            {
                if (writeBoost) sim.BoostMult = boostMult; // host re-asserts each step
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
            drivenSpin = sim.AngularVelocity[VehicleSim.RL];
            return lastRearForce;
        }

        [Test]
        public void VehicleSim_BoostMultDefaultsToOne()
        {
            var sim = new VehicleSim(new VehicleSpec());
            Assert.That(sim.BoostMult, Is.EqualTo(1f).Within(1e-6f), "BoostMult must default to 1 (no boost)");
        }

        [Test]
        public void VehicleSim_BoostMultOne_ForceIdenticalToUntouchedBaseline()
        {
            // Baseline never touches BoostMult; the other pins it to 1 every step. Same inputs, same maths,
            // so the driven-wheel force and spin must match bit-for-bit — proving BoostMult == 1 is a no-op.
            var baseline = new VehicleSim(new VehicleSpec());
            var pinnedOne = new VehicleSim(new VehicleSpec());

            Vector3 fBase = RunThrottle(baseline, 200, writeBoost: false, boostMult: 0f, out float spinBase);
            Vector3 fOne = RunThrottle(pinnedOne, 200, writeBoost: true, boostMult: 1f, out float spinOne);

            Assert.That(fOne.x, Is.EqualTo(fBase.x).Within(1e-6f), "BoostMult==1 must reproduce baseline force (x)");
            Assert.That(fOne.y, Is.EqualTo(fBase.y).Within(1e-6f), "BoostMult==1 must reproduce baseline force (y)");
            Assert.That(fOne.z, Is.EqualTo(fBase.z).Within(1e-6f), "BoostMult==1 must reproduce baseline force (z)");
            Assert.That(spinOne, Is.EqualTo(spinBase).Within(1e-6f), "BoostMult==1 must reproduce baseline wheel spin");
        }

        [Test]
        public void VehicleSim_BoostAboveOne_DeliversMoreDriveTorque()
        {
            // Same inputs; the only difference is BoostMult. Under full throttle at a held 3 m/s the driven
            // wheel settles where its longitudinal tyre force balances drive torque, so a higher BoostMult
            // must deliver a measurably larger driven-wheel force (fLong ~ driveTorque/R) and, with it, a
            // slightly higher wheel spin — a direct proof the torque fold is live.
            var plain = new VehicleSim(new VehicleSpec());
            var boosted = new VehicleSim(new VehicleSpec());

            Vector3 fPlain = RunThrottle(plain, 200, writeBoost: true, boostMult: 1f, out float spinPlain);
            Vector3 fBoost = RunThrottle(boosted, 200, writeBoost: true, boostMult: 1.15f, out float spinBoost);

            Assert.Greater((fBoost - fPlain).magnitude, 1f,
                "boost must raise the driven-wheel force, plain=" + fPlain + " boost=" + fBoost);
            Assert.Greater(spinBoost, spinPlain + 1e-4f,
                "boosted drive torque must spin the driven wheel up further, plain=" + spinPlain + " boost=" + spinBoost);
        }

        // ---------------------------------------------------------------- DraftBoost host gate

        [Test]
        public void DraftBoost_DisabledByDefault()
        {
            var go = new GameObject(nameof(DraftBoost_DisabledByDefault));
            try
            {
                var db = go.AddComponent<DraftBoost>();
                Assert.IsFalse(db.Enabled, "the boost must be gated OFF by default to preserve driving feel");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DraftBoost_Disabled_NeverTouchesBoostMult()
        {
            var go = new GameObject(nameof(DraftBoost_Disabled_NeverTouchesBoostMult));
            try
            {
                var db = go.AddComponent<DraftBoost>();
                db.Enabled = false;
                db.Model.MinActivateCharge01 = 0f; // even the most eager config must stay inert while disabled
                var sim = new VehicleSim(new VehicleSpec());

                // Feed it a full draft AND a deploy request every step: disabled, it must do nothing.
                for (int i = 0; i < 100; i++) db.Tick(sim, Dt, drafting: true, activate: true);

                Assert.That(sim.BoostMult, Is.EqualTo(1f).Within(1e-6f),
                    "a disabled DraftBoost must never touch the sim's BoostMult");
                Assert.That(db.Charge01, Is.EqualTo(0f).Within(1e-6f), "a disabled model must not accrue charge");
                Assert.IsFalse(db.Active, "a disabled DraftBoost must never engage a boost");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DraftBoost_Enabled_ChargesThenDrivesBoostMult()
        {
            var go = new GameObject(nameof(DraftBoost_Enabled_ChargesThenDrivesBoostMult));
            try
            {
                var db = go.AddComponent<DraftBoost>();
                db.Enabled = true;
                db.Model.MinActivateCharge01 = 0f;
                var sim = new VehicleSim(new VehicleSpec());

                // Drafting fills the reservoir but does not (yet) boost.
                for (int i = 0; i < 400; i++) db.Tick(sim, Dt, drafting: true, activate: false);
                Assert.Greater(db.Charge01, 0f, "drafting should have charged the reservoir");
                Assert.That(sim.BoostMult, Is.EqualTo(1f).Within(1e-6f), "no deploy yet -> BoostMult stays 1");

                // Deploying folds a bounded boost into the sim.
                db.Tick(sim, Dt, drafting: false, activate: true);
                Assert.IsTrue(db.Active, "a deploy with charge must engage the boost");
                Assert.Greater(sim.BoostMult, 1f, "an enabled, deployed boost must raise the sim's BoostMult");
                Assert.LessOrEqual(sim.BoostMult, DraftBoostModel.AbsoluteMaxBoostMult, "boost stays bounded");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
