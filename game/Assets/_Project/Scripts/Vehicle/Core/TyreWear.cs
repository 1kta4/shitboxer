using UnityEngine;

namespace Shitboxer.Vehicle
{
    /// <summary>
    /// Per-wheel tyre heat + wear model. Plain, engine-loop-independent C# — no Time.*, Input.* or
    /// scene access; the caller passes dt and this wheel's normalized slip/load each Step, so a
    /// headless server steps it identically to the client. (Uses only Mathf, which is pure maths.)
    ///
    /// The tyre warms with slip under load, peaks in an optimal temperature band, loses grip when
    /// cold or overheated, cools toward ambient when idle, and accumulates slow wear across a race
    /// that lowers peak grip until <see cref="Reset"/> clears it between races.
    ///
    /// This is opt-in at the sim level (<c>VehicleSim.TyreWearEnabled == false</c> by default). A
    /// fresh, un-stepped model reads <see cref="GripMult"/> == 1, so leaving it un-stepped is a
    /// perfect no-op: today's driving feel is unchanged unless the sim explicitly enables it.
    /// </summary>
    public class TyreWear
    {
        // --- Thermal tunables (degrees, seconds). Chosen so light cruising barely warms the tyre,
        //     moderate sustained slip settles it in the optimal band (grip ~1), and only aggressive or
        //     sustained abuse pushes it past the band and sheds grip. ---
        [Tooltip("Rest / cold-soak temperature the tyre cools back toward.")]
        public float AmbientC = 20f;
        [Tooltip("Lower edge of the full-grip band — grip climbs from cold up to here.")]
        public float OptimalLowC = 75f;
        [Tooltip("Upper edge of the full-grip band — grip falls once the tyre climbs past here.")]
        public float OptimalHighC = 95f;
        [Tooltip("Temperature at (or above) which the tyre has fully overheated to HotFloorGrip.")]
        public float OverheatC = 140f;
        [Tooltip("Grip factor of a stone-cold tyre (at or below ambient) — cold rubber is slippy.")]
        public float ColdGrip = 0.85f;
        [Tooltip("Grip factor once the tyre is fully overheated.")]
        public float HotFloorGrip = 0.7f;

        [Tooltip("Heat generated per second at full slip AND full load, degrees/s. Slip-gated: no slip, no heat.")]
        public float HeatGenRate = 130f;
        [Tooltip("Newtonian cooling coefficient toward ambient, per second.")]
        public float CoolRate = 0.6f;

        // --- Wear tunables. Deliberately slow: a clean stint barely wears, but sustained overheating
        //     tells over a full race. Wear is monotonic and only cleared by Reset(). ---
        [Tooltip("Base wear accrued per second at full slip (fraction of full wear). Multiplied up when overheated.")]
        public float WearRate = 0.004f;
        [Tooltip("Extra wear multiplier when fully overheated — heat chews the tyre far faster than slip alone.")]
        public float OverheatWearBoost = 4f;
        [Tooltip("Peak-grip fraction removed at full wear (Wear == 1). Folded on top of the thermal factor.")]
        public float MaxWearGripLoss = 0.25f;

        [Tooltip("Hard floor the combined thermal*wear grip multiplier can never drop below.")]
        public float MinGrip = 0.6f;

        // --- State ---
        /// <summary>Current tyre temperature, degrees. Starts at (and never cools below) ambient.</summary>
        public float TempC { get; private set; }
        /// <summary>Accumulated wear, 0 = fresh, 1 = fully worn. Monotonic across a race; cleared by Reset().</summary>
        public float Wear { get; private set; }
        /// <summary>Grip multiplier this model applies to tyre friction, in [MinGrip, 1]. 1 on a fresh/reset model.</summary>
        public float GripMult { get; private set; } = 1f;

        public TyreWear() => Reset();

        /// <summary>Race-start reset: tyre back to ambient temperature and zero accumulated wear.</summary>
        public void Reset()
        {
            TempC = AmbientC;
            Wear = 0f;
            GripMult = 1f;
        }

        /// <summary>
        /// Advance the model by <paramref name="dt"/> seconds given this wheel's normalized combined slip
        /// and load (both are clamped to 0..1). Integrates temperature (friction heat from slip*load,
        /// Newtonian cooling toward ambient), accumulates wear, and recomputes <see cref="GripMult"/>.
        /// </summary>
        public void Step(float dt, float slip01, float load01)
        {
            slip01 = Mathf.Clamp01(slip01);
            load01 = Mathf.Clamp01(load01);

            // Temperature: friction heating scales with slip AND load; cooling is Newtonian toward ambient.
            float heat = HeatGenRate * slip01 * load01;
            float cool = CoolRate * (TempC - AmbientC);
            TempC += (heat - cool) * dt;
            if (TempC < AmbientC) TempC = AmbientC; // a tyre never cools below the air around it

            // Wear: slow monotonic accumulation, gated by slip and greatly accelerated once overheated.
            float overheat01 = Mathf.InverseLerp(OptimalHighC, OverheatC, TempC); // clamps to 0..1
            float wearGain = WearRate * slip01 * (1f + OverheatWearBoost * overheat01) * dt;
            Wear = Mathf.Clamp01(Wear + wearGain);

            GripMult = ComputeGrip();
        }

        // Thermal grip factor (cold-slippy -> full across the band -> overheated-slippy) times the
        // peak-grip loss that accumulated wear has baked in, floored at MinGrip.
        private float ComputeGrip()
        {
            float thermal;
            if (TempC < OptimalLowC)
            {
                float t = Mathf.InverseLerp(AmbientC, OptimalLowC, TempC); // 0 cold -> 1 at the band
                thermal = Mathf.Lerp(ColdGrip, 1f, t * (2f - t));          // ease in, zero slope at the band
            }
            else if (TempC <= OptimalHighC)
            {
                thermal = 1f; // inside the optimal band
            }
            else
            {
                float t = Mathf.InverseLerp(OptimalHighC, OverheatC, TempC); // 0 at the band -> 1 fully overheated
                thermal = Mathf.Lerp(1f, HotFloorGrip, t * t);              // accelerating fade past the band
            }

            float wearFactor = 1f - Wear * MaxWearGripLoss;
            return Mathf.Max(MinGrip, thermal * wearFactor);
        }
    }
}
