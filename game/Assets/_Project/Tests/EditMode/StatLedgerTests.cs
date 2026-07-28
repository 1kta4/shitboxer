using NUnit.Framework;
using Shitboxer.Meta;
using Shitboxer.Vehicle;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the stat ledger — doc 08 decision 1, the one place that decides what a stat POINT is
    /// worth. The properties pinned here are what let the collection quote enormous numbers without
    /// producing an undriveable car: the curve saturates, a penalty can never invert a stat, and every
    /// baked spec lands inside the measured physics ceilings however extreme the input.
    /// </summary>
    public class StatLedgerTests : TestBase
    {
        /// <summary>A stand-in for GripBox: the grippier, lighter starter.</summary>
        static VehicleSpec Chassis()
        {
            var spec = new VehicleSpec
            {
                MassKg = 1050f,
                FinalDriveRatio = 4.7f,
            };
            spec.FrontTyre.PeakMu = 1.32f;
            spec.FrontTyre.SlideMu = 1.10f;
            spec.RearTyre.PeakMu = 1.32f;
            spec.RearTyre.SlideMu = 1.10f;
            spec.Engine.PeakTorqueNm = 205f;
            return spec;
        }

        // ---------------------------------------------------------------- the curve

        [Test]
        public void ZeroPoints_IsAnExactIdentity()
        {
            // An un-built car must be byte-for-byte the authored chassis.
            Assert.AreEqual(1f, StatLedger.Curve(0f, StatLedger.GripSpan), 1e-6f);
            Assert.AreEqual(1f, StatLedger.GripMult(0f), 1e-6f);
            Assert.AreEqual(1f, StatLedger.PowerMult(0f), 1e-6f);
            Assert.AreEqual(1f, StatLedger.WeightMult(0f), 1e-6f);
            Assert.AreEqual(0f, StatLedger.DamageResistance(0f), 1e-6f);
        }

        [Test]
        public void TheCurveSaturates_SoAbsurdNumbersStayDriveable()
        {
            // The whole point: an item can honestly say "+250 grip" and the number can feel enormous
            // while the multiplier it buys asymptotes.
            float at60 = StatLedger.GripMult(60f);
            float at250 = StatLedger.GripMult(250f);
            float at10k = StatLedger.GripMult(10000f);

            Assert.Less(at60, at250);
            Assert.Less(at250, at10k);
            Assert.LessOrEqual(at10k, 1f + StatLedger.GripSpan + 1e-4f, "must never exceed the span");
            Assert.Less(at10k - at250, 0.05f, "the last 9750 points buy almost nothing — that's saturation");
        }

        [Test]
        public void DiminishingReturns_EveryExtraPointIsWorthLess()
        {
            float previousGain = float.MaxValue;
            for (float p = 0f; p < 400f; p += 20f)
            {
                float gain = StatLedger.GripMult(p + 20f) - StatLedger.GripMult(p);
                Assert.Less(gain, previousGain, $"gain did not shrink across {p} -> {p + 20f}");
                previousGain = gain;
            }
        }

        [Test]
        public void APenaltyBitesButCanNeverInvertAStat()
        {
            // Negative points are a drawback, not a sign flip. A stat must stay positive however deep
            // the hole, or the friction circle and the drivetrain start producing nonsense.
            Assert.Less(StatLedger.GripMult(-60f), 1f);
            Assert.Greater(StatLedger.GripMult(-10000f), 0f);
            Assert.Greater(StatLedger.PowerMult(-10000f), 0f);
            Assert.Greater(StatLedger.WeightMult(-10000f), 0f);
        }

        [Test]
        public void NaNPointsFallBackToIdentity()
        {
            Assert.AreEqual(1f, StatLedger.GripMult(float.NaN), 1e-6f);
        }

        [Test]
        public void WeightPointsMakeTheCarLighter_NotHeavier()
        {
            // The one stat where "more points" means a SMALLER number on the spec.
            Assert.Less(StatLedger.WeightMult(120f), 1f);
            Assert.Greater(StatLedger.WeightMult(-120f), 1f);
        }

        // ---------------------------------------------------------------- baking

        [Test]
        public void EmptyLedger_BakesAnIdenticalSpec()
        {
            VehicleSpec baked = StatLedger.Bake(Chassis(), default);
            Assert.AreEqual(1.32f, baked.FrontTyre.PeakMu, 1e-4f);
            Assert.AreEqual(205f, baked.Engine.PeakTorqueNm, 1e-4f);
            Assert.AreEqual(1050f, baked.MassKg, 1e-3f);
            Assert.AreEqual(4.7f, baked.FinalDriveRatio, 1e-4f);
        }

        [Test]
        public void BakeNeverMutatesTheAuthoredAsset()
        {
            VehicleSpec source = Chassis();
            StatLedger.Bake(source, new BuildLedger { Grip = 500f, Power = 500f, Weight = 500f });
            Assert.AreEqual(1.32f, source.FrontTyre.PeakMu, 1e-4f);
            Assert.AreEqual(1050f, source.MassKg, 1e-3f);
        }

        [Test]
        public void PowerAlsoBuysTallerGearing()
        {
            // The measured finding: PowerBox is already 1.45x traction-limited in first and rev-limited
            // in top, so torque alone barely moves lap time. Half the gain goes into gearing, or a
            // "+Power" part is close to a lie.
            VehicleSpec baked = StatLedger.Bake(Chassis(), new BuildLedger { Power = 200f });
            Assert.Greater(baked.Engine.PeakTorqueNm, 205f);
            Assert.Less(baked.FinalDriveRatio, 4.7f, "gearing should have gone taller");
        }

        [Test]
        public void APowerPenaltyDoesNotAlsoShortenTheGearing()
        {
            // A drawback must not quietly hand back what it took by re-gearing for the lower torque.
            VehicleSpec baked = StatLedger.Bake(Chassis(), new BuildLedger { Power = -200f });
            Assert.Less(baked.Engine.PeakTorqueNm, 205f);
            Assert.AreEqual(4.7f, baked.FinalDriveRatio, 1e-4f);
        }

        [Test]
        public void AnAbsurdBuildStillLandsInsideThePhysicsCeilings()
        {
            // The safety property the whole ledger exists for.
            VehicleSpec baked = StatLedger.Bake(Chassis(), new BuildLedger
            {
                Grip = 99999f,
                Power = 99999f,
                Weight = 99999f,
                Durability = 99999f,
            });

            Assert.LessOrEqual(baked.FrontTyre.PeakMu, PhysicsCeilings.MaxPeakMu + 1e-4f);
            Assert.LessOrEqual(baked.RearTyre.PeakMu, PhysicsCeilings.MaxPeakMu + 1e-4f);
            Assert.LessOrEqual(baked.Engine.PeakTorqueNm, PhysicsCeilings.MaxPeakTorqueNm + 1e-3f);
            Assert.GreaterOrEqual(baked.MassKg, PhysicsCeilings.MinMassKg - 1e-3f);
            Assert.GreaterOrEqual(baked.FinalDriveRatio, PhysicsCeilings.MinFinalDriveRatio - 1e-4f);
            Assert.LessOrEqual(baked.DamageResistance, 1f);
        }

        [Test]
        public void TheStrongestChassisMaxesJustUnderTheGripCeiling()
        {
            // The spans are chosen so a fully-built GripBox lands ON the measured ceiling rather than
            // being clipped by it — if this starts failing, the span and the ceiling have drifted apart.
            VehicleSpec baked = StatLedger.Bake(Chassis(), new BuildLedger { Grip = 100000f });
            Assert.That(baked.FrontTyre.PeakMu, Is.EqualTo(1.32f * (1f + StatLedger.GripSpan)).Within(0.02f));
            Assert.Less(baked.FrontTyre.PeakMu, PhysicsCeilings.MaxPeakMu);
        }

        // ---------------------------------------------------------------- ceilings

        [Test]
        public void SlideMuIsNeverAllowedAboveThePeak()
        {
            // Inverting the tyre curve would make SLIDING grip better than the peak, and the friction
            // circle's falloff branch would read as a gain.
            var spec = new VehicleSpec();
            spec.FrontTyre.PeakMu = 5f;
            spec.FrontTyre.SlideMu = 4.5f;
            PhysicsCeilings.Clamp(spec);
            Assert.LessOrEqual(spec.FrontTyre.SlideMu, spec.FrontTyre.PeakMu);
            Assert.LessOrEqual(spec.FrontTyre.PeakMu, PhysicsCeilings.MaxPeakMu);
        }

        [Test]
        public void ClampIsIdempotentAndANoOpInsideTheBands()
        {
            VehicleSpec spec = Chassis();
            PhysicsCeilings.Clamp(spec);
            Assert.AreEqual(1.32f, spec.FrontTyre.PeakMu, 1e-4f);
            Assert.AreEqual(1050f, spec.MassKg, 1e-3f);

            PhysicsCeilings.Clamp(spec);   // again
            Assert.AreEqual(1.32f, spec.FrontTyre.PeakMu, 1e-4f);
        }

        [Test]
        public void ClampSurvivesANullSpec()
        {
            Assert.DoesNotThrow(() => PhysicsCeilings.Clamp(null));
        }

        // ---------------------------------------------------------------- ledger struct

        [Test]
        public void LedgerAddAndIndexerAgree()
        {
            var ledger = new BuildLedger();
            ledger.Add(BuildStat.Power, 12f);
            ledger.Add(BuildStat.Grip, 7f);
            ledger.Add(BuildStat.Weight, -3f);
            ledger.Add(BuildStat.Durability, 5f);

            Assert.AreEqual(12f, ledger[BuildStat.Power], 1e-4f);
            Assert.AreEqual(7f, ledger[BuildStat.Grip], 1e-4f);
            Assert.AreEqual(-3f, ledger[BuildStat.Weight], 1e-4f);
            Assert.AreEqual(5f, ledger[BuildStat.Durability], 1e-4f);
        }

        [Test]
        public void PointsAccumulateRatherThanReplace()
        {
            var ledger = new BuildLedger();
            ledger.Add(BuildStat.Grip, 10f);
            ledger.Add(BuildStat.Grip, 15f);
            Assert.AreEqual(25f, ledger.Grip, 1e-4f);
        }
    }
}
