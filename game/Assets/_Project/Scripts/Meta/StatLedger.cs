using System;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// The four headline stats (doc 08 decision 2), shown in the ESC menu and the shop — never on the
    /// race HUD. Also the FAMILY tags components are grouped by (decision 5), which is why the two are
    /// one enum: the stat display doubles as the family display for free.
    /// </summary>
    public enum BuildStat
    {
        Power,
        Grip,
        Weight,
        Durability,
    }

    /// <summary>
    /// A build's accumulated stat POINTS — Balatro's Chips/Mult, except there is no product to inflate,
    /// only a physical car. Components, enhancements, seals, editions and stat parts all add here rather
    /// than reaching into <see cref="VehicleSpec"/> directly, so there is exactly one place the whole
    /// collection is balanced from and exactly one curve deciding what a point is worth.
    ///
    /// Points are unbounded and may be negative (a drawback). What keeps the car driveable is the
    /// SATURATING mapping in <see cref="StatLedger"/>, not a limit here — so an item can honestly say
    /// "+250 grip" and the number can feel enormous while the car stays a car.
    /// </summary>
    [Serializable]
    public struct BuildLedger
    {
        public float Power;
        public float Grip;
        /// <summary>Positive points make the car LIGHTER — weight reduction is the upgrade.</summary>
        public float Weight;
        /// <summary>Positive points make the car take proportionally less damage.</summary>
        public float Durability;

        public float this[BuildStat stat]
        {
            get
            {
                switch (stat)
                {
                    case BuildStat.Power: return Power;
                    case BuildStat.Grip: return Grip;
                    case BuildStat.Weight: return Weight;
                    default: return Durability;
                }
            }
        }

        /// <summary>Adds points to one stat. The single mutation the whole collection needs.</summary>
        public void Add(BuildStat stat, float points)
        {
            switch (stat)
            {
                case BuildStat.Power: Power += points; break;
                case BuildStat.Grip: Grip += points; break;
                case BuildStat.Weight: Weight += points; break;
                default: Durability += points; break;
            }
        }
    }

    /// <summary>
    /// Hard ceilings measured against the shipped chassis (doc 08, "Measured physics headroom"). These
    /// are not balance opinions — they are where the car stops being a car:
    ///
    /// <list type="bullet">
    /// <item><b>µ 2.2</b> is roughly 2.2 g of lateral grip. Past it, rollover risk climbs (the centre of
    /// mass sits at −0.35 with TyreForceAppLift 0.5) and the arcade layer's 1.2 g
    /// LateralVelocityDamping clamp becomes irrelevant, so the car quietly changes character.</item>
    /// <item><b>700 kg</b> is the spring floor. Suspension natural frequency scales as √(k/m) and spring
    /// rates are authored per chassis, so a much lighter car on GripBox's 68 kN/m springs pogos.</item>
    /// <item><b>700 Nm</b> is generous headroom over PowerBox's 360; beyond it every gear is
    /// traction-limited and the number is a lie however it is geared.</item>
    /// </list>
    ///
    /// Applied as a FINAL clamp to any spec, whatever produced it — the ledger, stat parts, or a
    /// hand-authored asset. This is the cheapest possible elimination of "a stack of parts made the car
    /// undriveable", and it complements <see cref="VehicleSpec.Validate"/>, which guards the NaN class
    /// one layer down.
    /// </summary>
    public static class PhysicsCeilings
    {
        public const float MaxPeakMu = 2.2f;
        public const float MinMassKg = 700f;
        public const float MaxMassKg = 3000f;
        public const float MaxPeakTorqueNm = 700f;
        public const float MaxDownforceCoeff = 5f;
        public const float MinFinalDriveRatio = 2.2f;

        /// <summary>
        /// Clamp every stat the collection can drive into its physically sane band. Idempotent, and a
        /// no-op on a spec already inside the bands — so today's chassis and every part loadout that
        /// doesn't reach a ceiling bake byte-for-byte as before.
        /// </summary>
        public static void Clamp(VehicleSpec spec)
        {
            if (spec == null) return;

            ClampTyre(ref spec.FrontTyre);
            ClampTyre(ref spec.RearTyre);

            spec.MassKg = Mathf.Clamp(spec.MassKg, MinMassKg, MaxMassKg);
            spec.Engine.PeakTorqueNm = Mathf.Min(spec.Engine.PeakTorqueNm, MaxPeakTorqueNm);
            spec.DownforceCoeff = Mathf.Min(spec.DownforceCoeff, MaxDownforceCoeff);
            spec.FinalDriveRatio = Mathf.Max(spec.FinalDriveRatio, MinFinalDriveRatio);
        }

        private static void ClampTyre(ref TyreSpec tyre)
        {
            tyre.PeakMu = Mathf.Min(tyre.PeakMu, MaxPeakMu);
            // A slide coefficient above the peak would invert the tyre curve: sliding would grip BETTER
            // than the peak, and the friction circle's falloff branch would read as a gain.
            tyre.SlideMu = Mathf.Min(tyre.SlideMu, tyre.PeakMu);
        }
    }

    /// <summary>
    /// Turns <see cref="BuildLedger"/> points into a <see cref="VehicleSpec"/>. THE mapping — the one
    /// place that decides what a point is worth (doc 08 decision 1).
    ///
    /// The curve saturates. That is the whole design: a racing car's performance is bounded by physics
    /// and by the track in a way Balatro's score is not, so the numbers are allowed to go absurd while
    /// the multiplier they buy asymptotes. Measured headroom across the shipped chassis is only about
    /// ×2 on everything, against a Balatro run's ×500 — which is exactly why the collection is carried
    /// by rule-altering items rather than stat stacking (decision 3).
    ///
    /// Pure data-in / data-out, no scene access, so a headless server bakes an identical spec.
    /// </summary>
    public static class StatLedger
    {
        /// <summary>Points at which a stat reaches ~63% of its available span. Shared so the four stats stay comparable.</summary>
        public const float PointScale = 60f;

        // Spans are the MOST a stat can gain, chosen so the strongest shipped chassis lands on the
        // measured ceiling rather than through it: GripBox's µ1.32 × 1.65 = 2.18, just under µ2.2.
        public const float GripSpan = 0.65f;
        public const float PowerSpan = 0.80f;
        /// <summary>Weight REDUCTION span: GripBox's 1050 kg × 0.67 = 703 kg, just above the spring floor.</summary>
        public const float WeightSpan = 0.33f;
        /// <summary>Most damage a build can shrug off, as a fraction. Inert until the damage rework (decision 15) reads it.</summary>
        public const float DurabilitySpan = 0.60f;

        /// <summary>
        /// How much of a power gain is spent on TALLER gearing rather than raw torque.
        ///
        /// Measured finding (doc 08): PowerBox is already 1.45× traction-limited in first gear and
        /// rev-limited in top, so torque alone barely moves lap time — a "+Power" part that only scales
        /// PeakTorqueNm is close to a lie. Spending half the gain on gearing is what a real tuner does,
        /// and it converts the extra torque into top speed instead of wheelspin.
        /// </summary>
        public const float GearingShareOfPower = 0.5f;

        /// <summary>
        /// The saturating curve: 1 at zero points, easing toward <c>1 + span</c> as points grow without
        /// bound. Negative points (a drawback) decay toward a shallower floor of <c>1 − 0.6·span</c>, so
        /// a penalty bites hard but can never invert or zero a stat.
        /// </summary>
        public static float Curve(float points, float span)
        {
            if (float.IsNaN(points)) return 1f;
            if (points >= 0f) return 1f + span * (1f - Mathf.Exp(-points / PointScale));
            return 1f - span * 0.6f * (1f - Mathf.Exp(points / PointScale));
        }

        public static float GripMult(float points) => Curve(points, GripSpan);
        public static float PowerMult(float points) => Curve(points, PowerSpan);

        /// <summary>Mass multiplier — BELOW 1 for positive points, because weight reduction is the upgrade.</summary>
        public static float WeightMult(float points) => 2f - Curve(points, WeightSpan);

        /// <summary>Fraction of incoming damage a build shrugs off, 0 at zero points. Clamped non-negative.</summary>
        public static float DamageResistance(float points) => Mathf.Max(0f, Curve(points, DurabilitySpan) - 1f);

        /// <summary>
        /// Bakes the ledger onto a DEEP COPY of <paramref name="baseSpec"/> — the authored asset is never
        /// mutated — and clamps the result to the physics ceilings. A default (all-zero) ledger produces
        /// a spec identical to the input, so an un-built car is byte-for-byte the authored chassis.
        /// </summary>
        public static VehicleSpec Bake(VehicleSpec baseSpec, in BuildLedger ledger)
        {
            VehicleSpec spec = SpecModApplier.Clone(baseSpec);
            if (spec == null) return null;

            float grip = GripMult(ledger.Grip);
            spec.FrontTyre.PeakMu *= grip;
            spec.FrontTyre.SlideMu *= grip;
            spec.RearTyre.PeakMu *= grip;
            spec.RearTyre.SlideMu *= grip;

            float power = PowerMult(ledger.Power);
            spec.Engine.PeakTorqueNm *= power;
            // Taller gearing so the gain reaches the road (see GearingShareOfPower). Only for a genuine
            // gain: a power PENALTY must not also shorten the gearing and hand back what it took.
            if (power > 1f)
                spec.FinalDriveRatio /= Mathf.Pow(power, GearingShareOfPower);

            spec.MassKg *= WeightMult(ledger.Weight);
            // Capped at the field's authored 0.9 ceiling, not 1: since the damage rework reads this at
            // ApplyDamage intake, a Clamp01 here would let a deep-durability build become literally
            // unhittable — every contact part and boss in the game switched off by one stat.
            spec.DamageResistance = Mathf.Clamp(spec.DamageResistance + DamageResistance(ledger.Durability), 0f, 0.9f);

            PhysicsCeilings.Clamp(spec);
            spec.Validate();
            return spec;
        }
    }
}
