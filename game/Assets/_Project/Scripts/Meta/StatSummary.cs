using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// Collapses a full VehicleSpec down to the two headline numbers players actually read
    /// (doc 03's two-bar UI): GRIP and POWER, each normalized to 0..100. Pure data-in/data-out,
    /// no scene refs — so the garage preview, a HUD, or a headless tooling pass can all call it.
    ///
    /// The mapping mirrors doc 03: GRIP ≈ tyre + suspension + aero, POWER ≈ drivetrain + mass.
    /// Reference ranges are hand-picked so the two starter cars land at distinct, readable values
    /// (GripBox ≈ 67/29, PowerBox ≈ 35/57) and so stat parts move the bars a visible amount without
    /// instantly pinning them at 100.
    ///
    /// NB: RaceHud (Shitboxer.Race) can't reference this type — Meta already depends on Race, so a
    /// back-reference would be circular — and re-implements the same formula locally. Keep the two
    /// in sync if you retune the ranges here.
    /// </summary>
    public static class StatSummary
    {
        /// <summary>The two headline bars, each 0..100.</summary>
        public readonly struct Stats
        {
            public readonly float Grip;
            public readonly float Power;

            public Stats(float grip, float power)
            {
                Grip = grip;
                Power = power;
            }
        }

        // --- GRIP: tyre grip + turn-in sharpness + downforce + suspension stiffness ---
        private const float WGripMu = 0.45f;      // tyre PeakMu — the single biggest grip number
        private const float WGripSlip = 0.15f;    // peak slip angle (lower = pointier = grippier feel)
        private const float WGripDownforce = 0.20f;
        private const float WGripSpring = 0.20f;

        private const float MuMin = 0.90f, MuMax = 1.60f;
        private const float SlipLowDeg = 5f, SlipHighDeg = 11f;      // inverse: low slip angle → high grip
        private const float DownforceMin = 0f, DownforceMax = 3.5f;
        private const float SpringMin = 30000f, SpringMax = 85000f;

        // --- POWER: raw engine torque + power-to-weight (so MassKg feeds POWER, per doc 03) ---
        private const float WPowerTorque = 0.55f;
        private const float WPowerToWeight = 0.45f;

        private const float TorqueMin = 150f, TorqueMax = 450f;
        private const float P2WMin = 60f, P2WMax = 170f;            // kW per tonne
        private const float KwPerNmRpm = 9549f;                     // kW = Nm * rpm / 9549

        public static Stats Compute(VehicleSpec spec)
        {
            if (spec == null) return new Stats(0f, 0f);

            // GRIP -----------------------------------------------------------------
            float peakMu = 0.5f * (spec.FrontTyre.PeakMu + spec.RearTyre.PeakMu);
            float slipDeg = 0.5f * (spec.FrontTyre.PeakSlipAngleDeg + spec.RearTyre.PeakSlipAngleDeg);

            float muN = Normalize(peakMu, MuMin, MuMax);
            float slipN = 1f - Normalize(slipDeg, SlipLowDeg, SlipHighDeg);   // lower slip angle → more grip
            float downforceN = Normalize(spec.DownforceCoeff, DownforceMin, DownforceMax);
            float springN = Normalize(spec.SpringRateNPerM, SpringMin, SpringMax);

            float grip = 100f * (WGripMu * muN
                               + WGripSlip * slipN
                               + WGripDownforce * downforceN
                               + WGripSpring * springN);

            // POWER ----------------------------------------------------------------
            float torqueN = Normalize(spec.Engine.PeakTorqueNm, TorqueMin, TorqueMax);

            float mass = Mathf.Max(1f, spec.MassKg);
            float peakKw = spec.Engine.PeakTorqueNm * spec.Engine.PeakTorqueRpm / KwPerNmRpm;
            float p2w = peakKw / (mass / 1000f);                    // kW per tonne
            float p2wN = Normalize(p2w, P2WMin, P2WMax);

            float power = 100f * (WPowerTorque * torqueN + WPowerToWeight * p2wN);

            return new Stats(Mathf.Clamp(grip, 0f, 100f), Mathf.Clamp(power, 0f, 100f));
        }

        /// <summary>Clamped 0..1 position of <paramref name="value"/> within [min, max].</summary>
        private static float Normalize(float value, float min, float max) =>
            Mathf.InverseLerp(min, max, value);
    }
}
