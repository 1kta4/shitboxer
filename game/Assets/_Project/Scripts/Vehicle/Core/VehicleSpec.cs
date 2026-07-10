using System;
using UnityEngine;

namespace Shitboxer.Vehicle
{
    public enum DriveLayout
    {
        FrontWheelDrive,
        RearWheelDrive,
        AllWheelDrive,
    }

    /// <summary>
    /// Full tuning description of a car. Plain serializable data — no scene references —
    /// so the same spec can configure a headless server sim. The GRIP/POWER economy stats
    /// map onto this: GRIP parts touch tyres/suspension/aero, POWER parts touch the engine
    /// block and mass.
    /// </summary>
    [Serializable]
    public class VehicleSpec
    {
        [Header("Chassis")]
        public float MassKg = 1200f;
        [Tooltip("Local offset of centre of mass from the rigidbody origin. Negative Y = lower = more stable.")]
        public Vector3 CentreOfMassOffset = new Vector3(0f, -0.35f, 0f);
        [Tooltip("Distance between front and rear axles, metres.")]
        public float WheelbaseM = 2.6f;
        [Tooltip("Distance between left and right wheels, metres.")]
        public float TrackWidthM = 1.6f;
        [Tooltip("Height of the wheel attach points relative to rigidbody origin.")]
        public float AxleHeightM = -0.1f;

        [Header("Suspension (per wheel)")]
        public float SuspensionRestLengthM = 0.35f;
        [Tooltip("Extra travel beyond rest before the ray gives up, metres.")]
        public float SuspensionTravelM = 0.2f;
        public float SpringRateNPerM = 45000f;
        public float DamperRateNPerMps = 4500f;
        [Tooltip("N per metre of left/right compression difference, resists body roll. Per axle.")]
        public float AntiRollBarNPerM = 8000f;
        [Tooltip("Hard upper bound (N) on per-wheel vertical suspension force before it becomes tyre load. Caps landing/bottoming spikes so one corner can't launch or fling the car. ~4-6x static corner load.")]
        public float MaxSuspensionForceN = 30000f;
        [Tooltip("Fraction of total travel (rest + travel) at which the progressive bump-stop starts to engage. 0.85 = last 15% of travel.")]
        [Range(0.5f, 1f)] public float BumpStopStartFraction = 0.85f;
        [Tooltip("Bump-stop stiffness (N/m) through its engagement zone — resists bottoming so max travel isn't held by the linear spring alone.")]
        public float BumpStopRateNPerM = 250000f;

        [Header("Wheels")]
        public float WheelRadiusM = 0.32f;
        public float WheelMassKg = 18f;

        [Header("Tyres — the GRIP stat lives here")]
        public TyreSpec FrontTyre = TyreSpec.Default();
        public TyreSpec RearTyre = TyreSpec.Default();
        [Tooltip("0 = tyre forces act at the contact patch (more body roll), 1 = at axle height (arcade-flat). Sim-cade sweet spot ~0.5.")]
        [Range(0f, 1f)] public float TyreForceAppLift = 0.5f;

        [Header("Steering")]
        [Tooltip("Max steer angle at standstill, degrees.")]
        public float MaxSteerDeg = 32f;
        [Tooltip("Max steer angle at/above SteerFalloffSpeed, degrees.")]
        public float HighSpeedSteerDeg = 12f;
        [Tooltip("Speed (m/s) at which steering lock has fully tightened to HighSpeedSteerDeg.")]
        public float SteerFalloffSpeedMps = 30f;
        [Tooltip("How fast the steered angle chases the input, deg/s.")]
        public float SteerRateDegPerS = 220f;

        [Header("Engine — the POWER stat lives here")]
        public EngineSpec Engine = EngineSpec.Default();

        [Header("Transmission")]
        public DriveLayout Layout = DriveLayout.RearWheelDrive;
        public float[] GearRatios = { 3.2f, 2.1f, 1.55f, 1.2f, 0.95f };
        public float ReverseGearRatio = 3.0f;
        public float FinalDriveRatio = 3.9f;
        [Range(0.5f, 1f)] public float DrivetrainEfficiency = 0.9f;
        public float UpshiftRpm = 6200f;
        public float DownshiftRpm = 3200f;
        public float ShiftTimeS = 0.25f;

        [Header("Brakes")]
        public float BrakeTorqueNm = 2600f;
        [Tooltip("Fraction of brake torque on the front axle.")]
        [Range(0f, 1f)] public float BrakeBias = 0.62f;
        public float HandbrakeTorqueNm = 3500f;
        [Tooltip("Rear lateral grip multiplier while the handbrake is pulled — under 1 makes it a slide button.")]
        [Range(0.2f, 1f)] public float HandbrakeGripFactor = 0.55f;

        [Header("Arcade Assists — the NFS layer. Openly unphysical, spec-driven so cars keep distinct characters; 0 disables each.")]
        [Tooltip("Extra downward pull as a fraction of normal gravity, applied always (also airborne). The #1 anti-float knob: heavy landings, planted ride.")]
        public float ExtraGravity = 0.8f;
        [Tooltip("Yaw torque toward the steering-implied rotation rate. Makes turn-in immediate instead of waiting for tyre forces to build.")]
        [Range(0f, 1f)] public float YawAssist = 0.55f;
        [Tooltip("Fraction of sideways chassis velocity removed per second while grounded — 'the car goes where it points'. Handbrake mostly suspends it so slides stay possible.")]
        public float LateralVelocityDamping = 1.6f;
        [Tooltip("Extra roll/pitch angular damping while grounded — kills wallowing.")]
        public float FlatRideDamping = 1.5f;

        [Header("Aero")]
        [Tooltip("Longitudinal drag: F = -Coeff * v * |v|.")]
        public float DragCoeff = 0.38f;
        [Tooltip("Downforce: F = Coeff * v^2, applied at CoM. GRIP parts raise this.")]
        public float DownforceCoeff = 1.2f;

        public float FrontAxleZ => WheelbaseM * 0.5f;
        public float RearAxleZ => -WheelbaseM * 0.5f;

        /// <summary>
        /// Clamp every field the sim divides by (or whose zero would poison the maths) up to a small
        /// positive minimum. A hand-authored asset or a runtime part-swap in the roguelike economy that
        /// scales a stat to 0 would otherwise feed an Inf/NaN into the force sent to the rigidbody — the
        /// classic "car vanishes / tunnels through the world" bug. This is the cheapest elimination of
        /// that whole NaN class. Idempotent: clamping an already-valid value is a no-op, so it is safe to
        /// run on a spec asset that several cars share and each re-validate from their own VehicleSim ctor.
        /// </summary>
        public void Validate()
        {
            MassKg = Mathf.Max(1f, MassKg);
            WheelbaseM = Mathf.Max(0.5f, WheelbaseM);
            WheelRadiusM = Mathf.Max(0.05f, WheelRadiusM);
            SteerFalloffSpeedMps = Mathf.Max(0.1f, SteerFalloffSpeedMps);
            MaxSuspensionForceN = Mathf.Max(1f, MaxSuspensionForceN);

            ClampTyre(ref FrontTyre);
            ClampTyre(ref RearTyre);
        }

        // The tyre divisors (PeakSlipRatio, PeakSlipAngleDeg) and the load-sensitivity denominator
        // (RatedLoadN) each appear in a division inside the friction-circle maths; a zero there is an
        // instant NaN. Same positive-minimum clamp, applied to whichever tyre is passed by ref.
        private static void ClampTyre(ref TyreSpec tyre)
        {
            tyre.PeakSlipRatio = Mathf.Max(0.01f, tyre.PeakSlipRatio);
            tyre.PeakSlipAngleDeg = Mathf.Max(0.5f, tyre.PeakSlipAngleDeg);
            tyre.RatedLoadN = Mathf.Max(1f, tyre.RatedLoadN);
        }
    }

    [Serializable]
    public struct TyreSpec
    {
        [Tooltip("Peak friction coefficient — the single biggest GRIP number.")]
        public float PeakMu;
        [Tooltip("Friction coefficient once fully sliding (ratio of peak is typical: 0.75–0.9).")]
        public float SlideMu;
        [Tooltip("Slip angle (degrees) where lateral grip peaks.")]
        public float PeakSlipAngleDeg;
        [Tooltip("Slip ratio where longitudinal grip peaks.")]
        public float PeakSlipRatio;
        [Tooltip("How quickly grip decays past the peak — higher = snappier breakaway.")]
        public float FalloffSharpness;
        [Tooltip("Grip lost per unit of overload relative to rated load. 0 = none. Makes weight transfer matter.")]
        public float LoadSensitivity;
        [Tooltip("Per-wheel load (N) at which PeakMu is quoted.")]
        public float RatedLoadN;

        public static TyreSpec Default() => new TyreSpec
        {
            PeakMu = 1.05f,
            SlideMu = 0.85f,
            PeakSlipAngleDeg = 9f,
            PeakSlipRatio = 0.12f,
            FalloffSharpness = 0.8f,
            LoadSensitivity = 0.08f,
            RatedLoadN = 3500f,
        };
    }

    [Serializable]
    public struct EngineSpec
    {
        public float IdleRpm;
        public float RedlineRpm;
        [Tooltip("Peak torque in Nm — the single biggest POWER number.")]
        public float PeakTorqueNm;
        [Tooltip("RPM where torque peaks.")]
        public float PeakTorqueRpm;
        [Tooltip("Fraction of peak torque available at idle.")]
        public float LowEndFraction;
        [Tooltip("Fraction of peak torque left at redline.")]
        public float TopEndFraction;
        [Tooltip("Engine braking torque at redline with throttle closed, Nm.")]
        public float EngineBrakeNm;

        public static EngineSpec Default() => new EngineSpec
        {
            IdleRpm = 900f,
            RedlineRpm = 6800f,
            PeakTorqueNm = 240f,
            PeakTorqueRpm = 4200f,
            LowEndFraction = 0.55f,
            TopEndFraction = 0.7f,
            EngineBrakeNm = 60f,
        };

        /// <summary>Piecewise curve: LowEnd at idle → 1.0 at peak → TopEnd at redline.</summary>
        public float TorqueAt(float rpm)
        {
            rpm = Mathf.Clamp(rpm, IdleRpm, RedlineRpm);
            float t;
            if (rpm <= PeakTorqueRpm)
            {
                t = Mathf.InverseLerp(IdleRpm, PeakTorqueRpm, rpm);
                // Ease toward the peak instead of a hard corner.
                return PeakTorqueNm * Mathf.Lerp(LowEndFraction, 1f, t * (2f - t));
            }
            t = Mathf.InverseLerp(PeakTorqueRpm, RedlineRpm, rpm);
            return PeakTorqueNm * Mathf.Lerp(1f, TopEndFraction, t * t);
        }
    }
}
