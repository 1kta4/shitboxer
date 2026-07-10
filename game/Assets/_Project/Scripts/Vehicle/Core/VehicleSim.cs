using UnityEngine;

namespace Shitboxer.Vehicle
{
    /// <summary>
    /// The engine-loop-independent vehicle simulation. Owns all drivetrain/tyre/suspension
    /// state and maths; per step it consumes ground contacts and produces forces for the
    /// host to apply. Must never read Time.*, Input.*, or the physics scene — the host
    /// supplies dt, input, and contacts so a headless server can step it identically.
    /// </summary>
    public class VehicleSim
    {
        public const int WheelCount = 4;
        public const int FL = 0, FR = 1, RL = 2, RR = 3;

        public readonly VehicleSpec Spec;

        // --- Per-wheel state ---
        public readonly float[] AngularVelocity = new float[WheelCount]; // rad/s, + = rolling forward
        public readonly float[] Compression = new float[WheelCount];     // m, 0 = fully extended
        public readonly float[] SuspensionForce = new float[WheelCount]; // N, this step's vertical load
        public readonly float[] SlipRatio = new float[WheelCount];
        public readonly float[] SlipAngleDeg = new float[WheelCount];
        public readonly bool[] Grounded = new bool[WheelCount];

        // --- Drivetrain state ---
        public int Gear { get; private set; } = 1;        // 1-based; 0 = reverse
        public bool InReverse { get; private set; }
        public float EngineRpm { get; private set; }
        public float SteerAngleDeg { get; private set; }  // current smoothed steer
        private float _shiftTimer;

        private readonly ForceCommand[] _forces = new ForceCommand[WheelCount + 1]; // +1 for aero

        // Tyre relaxation state (smoothed slip) and lengths in metres of travel.
        private readonly float[] _slipRatioState = new float[WheelCount];
        private readonly float[] _slipAngleState = new float[WheelCount];
        private const float LongRelaxationLengthM = 0.15f;
        private const float LatRelaxationLengthM = 0.35f;

        // Internal substep count for the stiff wheel-spin / tyre-slip integration. The wheel
        // angular-velocity ODE and the slip-relaxation state have a time constant (~3 ms) far shorter
        // than a 20 ms FixedUpdate, so a single explicit Euler step over the full dt leans on the
        // overshoot clamps below to stay stable. Each public Step instead advances only that per-wheel
        // spin/slip state over N micro-steps of dt/N — drivetrain, steering, suspension geometry, aero
        // and assists are computed ONCE per Step and held fixed across the substeps — which shrinks the
        // effective dt below the time constant and makes the integration robust on its own. Spec-
        // independent by design (a private const, not a serialized field) so a headless server steps
        // identical maths. N in 4..8 is the practical band; 4 is a safe, cheap default.
        private const int WheelSubsteps = 4;

        // Per-substep multiplier reproducing the per-Step airborne slip-state decay (0.9 over one full
        // Step) for any WheelSubsteps: 0.9^(1/N) applied N times == 0.9. Keeps airborne relaxation
        // identical per Step to the pre-substep behaviour rather than decaying N times as fast.
        private static readonly float AirborneSlipDecayPerSubstep = Mathf.Pow(0.9f, 1f / WheelSubsteps);

        // Sustained airtime (seconds with no wheel grounded). Gates the extra-gravity assist so a car
        // that has sunk under the world is not driven further down — the blind-sink fall-through trap.
        private float _airborneTime;
        private const float ExtraGravityMaxAirborneS = 0.25f;

        public VehicleSim(VehicleSpec spec)
        {
            Spec = spec;
            Spec.Validate(); // clamp every divisor field to a positive minimum before a step can divide by it
            EngineRpm = spec.Engine.IdleRpm;
        }

        /// <summary>Local attach position for wheel i (FL, FR, RL, RR) in chassis space.</summary>
        public Vector3 WheelLocalPosition(int i)
        {
            float x = (i == FL || i == RL) ? -Spec.TrackWidthM * 0.5f : Spec.TrackWidthM * 0.5f;
            float z = (i == FL || i == FR) ? Spec.FrontAxleZ : Spec.RearAxleZ;
            return new Vector3(x, Spec.AxleHeightM, z);
        }

        public bool IsFrontWheel(int i) => i == FL || i == FR;

        public bool IsDriven(int i) => Spec.Layout switch
        {
            DriveLayout.FrontWheelDrive => IsFrontWheel(i),
            DriveLayout.RearWheelDrive => !IsFrontWheel(i),
            _ => true,
        };

        /// <summary>World-space torque the host must apply to the chassis this step (assists).</summary>
        public Vector3 BodyTorque { get; private set; }

        // ------------------------------------------------------------------ transient combat effects

        /// <summary>
        /// Multiplier on all tyre grip this step, 1 = nominal. Attack parts (Ram Bars, Disruptor
        /// Field) push it below 1 via ApplyGripSap; it recovers toward 1 on its own. Lives in the
        /// plain-C# core so a headless server decays it identically — the host only injects the
        /// contact/proximity events that trigger a sap.
        /// </summary>
        public float GripEffectMult { get; private set; } = 1f;

        /// <summary>Multiplier on engine drive torque this step, 1 = nominal. See ApplyPowerSap (Spike Plates).</summary>
        public float PowerEffectMult { get; private set; } = 1f;

        private float _gripRecoverPerS = 1f;
        private float _powerRecoverPerS = 1f;

        /// <summary>
        /// Knock grip down by <paramref name="strength01"/> (0.3 = −30%), recovering toward nominal
        /// at <paramref name="recoverPerS"/> per second. Only ever deepens an existing sap (keeps the
        /// stronger of the two), so a continuous aura holds a floor while a one-off ram can spike
        /// below it and fade back to it. No-op for non-positive strength.
        /// </summary>
        public void ApplyGripSap(float strength01, float recoverPerS)
        {
            if (strength01 <= 0f) return;
            float floor = Mathf.Clamp01(1f - strength01);
            if (floor < GripEffectMult)
            {
                GripEffectMult = floor;
                _gripRecoverPerS = Mathf.Max(0.01f, recoverPerS);
            }
        }

        /// <summary>Engine-torque analogue of ApplyGripSap (Spike Plates sap the cars they touch).</summary>
        public void ApplyPowerSap(float strength01, float recoverPerS)
        {
            if (strength01 <= 0f) return;
            float floor = Mathf.Clamp01(1f - strength01);
            if (floor < PowerEffectMult)
            {
                PowerEffectMult = floor;
                _powerRecoverPerS = Mathf.Max(0.01f, recoverPerS);
            }
        }

        private void DecayEffects(float dt)
        {
            GripEffectMult = Mathf.MoveTowards(GripEffectMult, 1f, _gripRecoverPerS * dt);
            PowerEffectMult = Mathf.MoveTowards(PowerEffectMult, 1f, _powerRecoverPerS * dt);
            // Slipstream fades once the car pulls out of the tow (the host re-asserts it each step while drafting).
            DraftDragMult = Mathf.MoveTowards(DraftDragMult, 1f, DraftRecoverPerS * dt);
        }

        // ------------------------------------------------------------------ slipstream / draft

        /// <summary>
        /// Deepest a tow can cut aero drag to: 0.6 keeps ~40% of drag — a strong but bounded slipstream so
        /// a drafting car can't accelerate without limit. The host's DraftSensor supplies the actual factor.
        /// </summary>
        public const float MinDraftDragMult = 0.6f;

        /// <summary>
        /// Multiplier on the aero DRAG term this step, 1 = full drag (clean air), lower = a slipstream. A car
        /// tucked close behind another sits in its wake, so the host's DraftSensor pushes this below 1 via
        /// <see cref="ApplyDraft"/>; it eases back to 1 on its own once the car pulls out — exactly like the
        /// transient grip/power saps. Downforce is deliberately NOT reduced (drafting cuts drag, not grip).
        /// Lives in the plain-C# core so a headless server eases it identically — the host only injects the
        /// proximity that triggers a draft.
        /// </summary>
        public float DraftDragMult { get; private set; } = 1f;

        /// <summary>How fast the drag cut recovers toward 1 once the host stops re-asserting a draft, per second.</summary>
        private const float DraftRecoverPerS = 2f;

        /// <summary>
        /// Put the car in another's slipstream this step, easing its aero drag toward <paramref name="mult"/>
        /// (clamped to [<see cref="MinDraftDragMult"/>, 1]). Like the grip/power saps it only ever DEEPENS the
        /// tow (keeps the lower of the two) and recovers toward 1 on its own, so the host re-asserts it every
        /// step while drafting and it fades once the car pulls out. A value >= 1 (or non-finite) is a no-op —
        /// "no draft" is signalled by simply not calling this, letting DraftDragMult recover.
        /// </summary>
        public void ApplyDraft(float mult)
        {
            if (!(mult < 1f)) return; // rejects >= 1 and NaN
            float floor = Mathf.Clamp(mult, MinDraftDragMult, 1f);
            if (floor < DraftDragMult) DraftDragMult = floor;
        }

        // ------------------------------------------------------------------ persistent damage / durability

        /// <summary>Floor Durability can never drop below — even a total wreck still drives, just badly.</summary>
        public const float MinDurability = 0.4f;

        /// <summary>
        /// Fraction of peak grip/power stripped once fully battered (Durability == MinDurability). Kept
        /// below a full 1:1 so a wreck is hobbled, not undriveable — at the floor a car still keeps
        /// (1 - MaxWearPerformanceLoss * (1 - MinDurability)) of its output.
        /// </summary>
        private const float MaxWearPerformanceLoss = 0.5f;

        /// <summary>
        /// PERSISTENT 0..1 structural integrity, 1 = a fresh car. Unlike the transient grip/power saps this
        /// does NOT recover during a race — a battered car stays battered — and it resets to 1 only when the
        /// sim is rebuilt (a fresh car each race, i.e. a new VehicleSim). Lowered on hard shunts via
        /// <see cref="ApplyDamage"/>. Lives in the plain-C# core so a headless server accumulates wear
        /// identically — the host only injects the impact events that drive it.
        /// </summary>
        public float Durability { get; private set; } = 1f;

        /// <summary>
        /// Multiplier (≤1) that persistent wear places on BOTH peak tyre grip and engine drive torque:
        /// 1 at full Durability, easing down to (1 - MaxWearPerformanceLoss * (1 - MinDurability)) at the
        /// floor. Folded in alongside <see cref="GripEffectMult"/>/<see cref="PowerEffectMult"/> so lasting
        /// wear stacks multiplicatively with the transient combat saps.
        /// </summary>
        public float DurabilityMult => 1f - (1f - Durability) * MaxWearPerformanceLoss;

        /// <summary>
        /// Permanently wear the car by <paramref name="amount"/> of durability, clamped so Durability never
        /// drops below <see cref="MinDurability"/>. No-op for non-positive or non-finite amounts. Unlike
        /// ApplyGripSap/ApplyPowerSap this does NOT decay back toward nominal — the loss holds for the rest
        /// of the race and is cleared only by rebuilding the sim. Callers scale <paramref name="amount"/> by
        /// impact severity so heavy shunts progressively batter the car down (Wreckfest-style consequences).
        /// </summary>
        public void ApplyDamage(float amount)
        {
            if (!(amount > 0f)) return; // rejects zero, negatives and NaN
            Durability = Mathf.Max(MinDurability, Durability - amount);
        }

        /// <summary>
        /// Directly assign persistent <see cref="Durability"/>, clamped to [<see cref="MinDurability"/>, 1].
        /// Unlike <see cref="ApplyDamage"/> (which only ever lowers it and rejects out-of-range amounts)
        /// this sets an ABSOLUTE value, so the host can carry a run's accumulated wear onto a freshly-rebuilt
        /// sim (a new VehicleSim resets to full) or restore it after a garage repair. Lives in the plain-C#
        /// core so a headless server can carry wear across races identically.
        /// </summary>
        public void SetDurability(float durability)
        {
            Durability = Mathf.Clamp(durability, MinDurability, 1f);
        }

        /// <summary>
        /// Advance the sim by dt. Returns forces to apply to the chassis rigidbody this step.
        /// The returned array is reused between calls — consume it immediately.
        /// </summary>
        public ForceCommand[] Step(float dt, in VehicleInput input, GroundContact[] contacts,
            Vector3 chassisVelocity, Vector3 chassisForward, Vector3 chassisUp,
            Vector3 chassisAngularVelocity)
        {
            DecayEffects(dt); // transient grip/power saps ease back toward nominal (auras re-clamp below)
            UpdateSteering(dt, input.Steer, chassisVelocity.magnitude);

            float forwardSpeed = Vector3.Dot(chassisVelocity, chassisForward);
            UpdateGearbox(dt, input, forwardSpeed);

            // Suspension first: tyre grip this step scales with the spring load it produces.
            for (int i = 0; i < WheelCount; i++)
                StepSuspension(i, dt, contacts[i]);
            ApplyAntiRollBars();

            // Arcade pedal convention: while reversing, the brake pedal is the reverse throttle.
            float driveTorquePerWheel = ComputeDriveTorque(InReverse ? input.Brake : input.Throttle);

            // Substep the stiff wheel/tyre integration. Contacts and the suspension load computed
            // above are held fixed across the substeps; only each wheel's spin and slip-relaxation
            // state advances, over WheelSubsteps micro-steps of dt/N. Wheels don't couple to one
            // another within a substep (the only cross-wheel coupling — the anti-roll bar — already
            // ran on the fixed suspension load), so a wheel can run all its substeps before the next.
            // The force handed back to the host is the per-wheel force AVERAGED over the substeps, so
            // the net impulse applied over dt equals the integral of the tyre force across the step.
            float subDt = dt / WheelSubsteps;
            for (int i = 0; i < WheelCount; i++)
            {
                Vector3 forceAccum = Vector3.zero;
                Vector3 appPoint = Vector3.zero;
                for (int s = 0; s < WheelSubsteps; s++)
                    forceAccum += StepWheel(i, subDt, input, contacts[i], driveTorquePerWheel, out appPoint);

                _forces[i] = new ForceCommand
                {
                    Force = forceAccum / WheelSubsteps,
                    Position = appPoint,
                };
            }

            // Aero: quadratic drag opposing velocity, plus downforce along -up. Speed is capped for
            // the quadratic terms only so a pathological velocity can't overflow them to Infinity.
            float v = Mathf.Min(chassisVelocity.magnitude, 200f);
            // DraftDragMult (1 = clean air, lower = a slipstream) scales the DRAG term only; downforce is untouched.
            Vector3 comForce = -Spec.DragCoeff * DraftDragMult * v * chassisVelocity - Spec.DownforceCoeff * v * v * chassisUp;

            comForce += StepAssists(dt, input, chassisVelocity, chassisForward, chassisUp, chassisAngularVelocity);

            _forces[WheelCount] = new ForceCommand
            {
                Force = comForce,
                Position = Vector3.zero, // host applies at CoM
            };

            return _forces;
        }

        // ------------------------------------------------------------------ arcade assists

        /// <summary>
        /// The NFS layer: openly unphysical forces that make the car feel planted and
        /// immediate without touching the tyre model. Lives in the sim (not the host) so a
        /// headless server steps identical maths. Returns a CoM force; also sets BodyTorque.
        /// </summary>
        private Vector3 StepAssists(float dt, in VehicleInput input, Vector3 velocity, Vector3 forward,
            Vector3 up, Vector3 angularVelocity)
        {
            BodyTorque = Vector3.zero;

            int groundedCount = 0;
            for (int i = 0; i < WheelCount; i++)
                if (Grounded[i]) groundedCount++;

            // The extra "downforce gravity" is a heavy-landing feel cheat, applied even airborne so
            // brief hops over bumps stay planted. But a car out of contact for a WHILE may be sinking
            // through the ground (wheel rays blind past RayStartLift); pulling it further down then
            // drives it out the bottom of the world (the blind-sink fall-through). So cut the pull
            // once airborne long enough to be a fall, not a hop — it then falls under normal gravity
            // only and can recover.
            _airborneTime = groundedCount > 0 ? 0f : _airborneTime + dt;
            Vector3 force = _airborneTime < ExtraGravityMaxAirborneS
                ? Vector3.down * (Spec.MassKg * 9.81f * Spec.ExtraGravity)
                : Vector3.zero;

            if (groundedCount < 3) return force; // airborne/tipped: no steering cheats

            float forwardSpeed = Vector3.Dot(velocity, forward);
            Vector3 right = Vector3.Cross(up, forward).normalized;

            // Yaw assist: torque toward the yaw rate the steering geometry implies,
            // capped by a grip-plausible lateral acceleration so it can't spin the car.
            if (Spec.YawAssist > 0f && Mathf.Abs(forwardSpeed) > 3f)
            {
                float targetYawRate = forwardSpeed / Spec.WheelbaseM
                                      * Mathf.Tan(SteerAngleDeg * Mathf.Deg2Rad);
                float muAvg = (Spec.FrontTyre.PeakMu + Spec.RearTyre.PeakMu) * 0.5f;
                float maxYawRate = muAvg * 9.81f * 1.3f / Mathf.Max(Mathf.Abs(forwardSpeed), 3f);
                targetYawRate = Mathf.Clamp(targetYawRate, -maxYawRate, maxYawRate);

                float yawInertia = Spec.MassKg
                    * (Spec.WheelbaseM * Spec.WheelbaseM + Spec.TrackWidthM * Spec.TrackWidthM) / 12f * 1.2f;
                float yawError = targetYawRate - Vector3.Dot(angularVelocity, up);
                BodyTorque += up * (yawInertia * Mathf.Clamp(yawError * 6f * Spec.YawAssist, -8f, 8f));
            }

            // Lateral velocity damping: "goes where it points". Handbrake suspends most of
            // it so deliberate slides remain a tool.
            if (Spec.LateralVelocityDamping > 0f)
            {
                float latSpeed = Vector3.Dot(velocity, right);
                float damping = Spec.LateralVelocityDamping * (1f - 0.85f * input.Handbrake);
                Vector3 latForce = -right * (latSpeed * Spec.MassKg * damping);
                float maxLatForce = Spec.MassKg * 12f; // ≤ ~1.2 g of cheating
                force += Vector3.ClampMagnitude(latForce, maxLatForce);
            }

            // Flat ride: damp roll/pitch wobble.
            if (Spec.FlatRideDamping > 0f)
            {
                Vector3 rollPitch = angularVelocity - up * Vector3.Dot(angularVelocity, up);
                BodyTorque += -rollPitch * (Spec.FlatRideDamping * Spec.MassKg * 0.4f);
            }

            return force;
        }

        // ------------------------------------------------------------------ steering

        private void UpdateSteering(float dt, float steerInput, float speed)
        {
            float speed01 = Mathf.Clamp01(speed / Spec.SteerFalloffSpeedMps);
            float maxSteer = Mathf.Lerp(Spec.MaxSteerDeg, Spec.HighSpeedSteerDeg, speed01);
            float target = steerInput * maxSteer;
            SteerAngleDeg = Mathf.MoveTowards(SteerAngleDeg, target, Spec.SteerRateDegPerS * dt);
        }

        // ------------------------------------------------------------------ drivetrain

        private void UpdateGearbox(float dt, in VehicleInput input, float forwardSpeed)
        {
            // Arcade pedal convention, symmetric in both directions: the "other" pedal always
            // brakes first, and only flips the direction once the car is nearly stopped.
            if (InReverse)
            {
                if (input.Throttle > 0.1f && forwardSpeed > -0.5f) { InReverse = false; Gear = 1; }
            }
            else if (input.Brake > 0.1f && forwardSpeed < 0.5f && EngineDrivenSpeedAbs() < 2f)
            {
                InReverse = true;
            }

            if (_shiftTimer > 0f) _shiftTimer -= dt;

            float wheelRpm = AvgDrivenWheelRadPerS() * 60f / (2f * Mathf.PI);
            EngineRpm = Mathf.Clamp(Mathf.Abs(wheelRpm * CurrentTotalRatio()),
                Spec.Engine.IdleRpm, Spec.Engine.RedlineRpm);

            if (InReverse || _shiftTimer > 0f) return;

            // Shift decisions use road speed, NOT wheel rpm: during wheelspin the wheels sit
            // at redline, and rpm-based shifting would ratchet the box into top gear at
            // walking pace (then crawl back down one torque-cut shift at a time).
            float roadRpmCurrent = RoadRpmInGear(forwardSpeed, Gear);
            if (Gear < Spec.GearRatios.Length && roadRpmCurrent > Spec.UpshiftRpm)
            {
                Gear++;
                _shiftTimer = Spec.ShiftTimeS;
            }
            else if (Gear > 1 && roadRpmCurrent < Spec.DownshiftRpm
                     && RoadRpmInGear(forwardSpeed, Gear - 1) < Spec.UpshiftRpm * 0.85f)
            {
                Gear--;
                _shiftTimer = Spec.ShiftTimeS * 0.4f; // downshifts snap much faster than upshifts
            }
        }

        /// <summary>Engine rpm the car's actual ground speed implies in the given gear.</summary>
        private float RoadRpmInGear(float forwardSpeed, int gear)
        {
            float wheelRadPerS = Mathf.Abs(forwardSpeed) / Spec.WheelRadiusM;
            return wheelRadPerS * Spec.GearRatios[gear - 1] * Spec.FinalDriveRatio * 60f / (2f * Mathf.PI);
        }

        private float CurrentTotalRatio() =>
            (InReverse ? Spec.ReverseGearRatio : Spec.GearRatios[Gear - 1]) * Spec.FinalDriveRatio;

        private float AvgDrivenWheelRadPerS()
        {
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < WheelCount; i++)
                if (IsDriven(i)) { sum += AngularVelocity[i]; n++; }
            return n > 0 ? sum / n : 0f;
        }

        private float EngineDrivenSpeedAbs() =>
            Mathf.Abs(AvgDrivenWheelRadPerS()) * Spec.WheelRadiusM;

        /// <summary>Torque delivered to each driven wheel this step (signed; negative in reverse).</summary>
        private float ComputeDriveTorque(float throttle)
        {
            if (_shiftTimer > 0f) return 0f; // torque cut during shifts

            float engineTorque;
            if (throttle > 0.01f)
            {
                // Transient power sap AND persistent wear both scale the delivered engine torque.
                engineTorque = Spec.Engine.TorqueAt(EngineRpm) * throttle * PowerEffectMult * DurabilityMult;
                // Rev limiter: no more torque at the wall.
                if (EngineRpm >= Spec.Engine.RedlineRpm - 10f) engineTorque = 0f;
            }
            else
            {
                // Engine braking, proportional to revs.
                float rev01 = Mathf.InverseLerp(Spec.Engine.IdleRpm, Spec.Engine.RedlineRpm, EngineRpm);
                engineTorque = -Spec.Engine.EngineBrakeNm * rev01;
            }

            int drivenCount = Spec.Layout == DriveLayout.AllWheelDrive ? 4 : 2;
            float wheelTorque = engineTorque * CurrentTotalRatio() * Spec.DrivetrainEfficiency / drivenCount;
            return InReverse ? -wheelTorque : wheelTorque;
        }

        // ------------------------------------------------------------------ suspension

        private void StepSuspension(int i, float dt, in GroundContact c)
        {
            bool wasGrounded = Grounded[i]; // last step's state, before we overwrite it below
            Grounded[i] = c.Grounded;
            if (!c.Grounded)
            {
                Compression[i] = 0f;
                SuspensionForce[i] = 0f;
                return;
            }

            float maxCompression = Spec.SuspensionRestLengthM + Spec.SuspensionTravelM;
            float compression = Mathf.Clamp(
                Spec.SuspensionRestLengthM - (c.HitDistance - Spec.WheelRadiusM),
                0f, maxCompression);

            // Damper needs the compression RATE, but while airborne we force Compression to 0. On the
            // first grounded step after airtime a naive (compression - 0)/dt reads the whole touchdown
            // compression as one step of velocity — e.g. 0.3 m / 0.02 s = 15 m/s → a ~67 kN phantom
            // damper spike on one corner (a launch AND a yaw/roll kick, and a spiked tyre load downstream).
            // Seed prev from THIS step's geometry across the airborne->grounded transition so the rate is
            // 0 there, then clamp the rate so even a curb/step blip while grounded can't spike the damper
            // past the force ceiling.
            float prev = wasGrounded ? Compression[i] : compression;
            Compression[i] = compression;

            float compressionSpeed = (compression - prev) / dt;
            float maxCompressionSpeed = Spec.MaxSuspensionForceN / Mathf.Max(1f, Spec.DamperRateNPerMps);
            compressionSpeed = Mathf.Clamp(compressionSpeed, -maxCompressionSpeed, maxCompressionSpeed);

            float force = Spec.SpringRateNPerM * compression
                        + Spec.DamperRateNPerMps * compressionSpeed;

            // Progressive bump-stop: near the end of travel the linear spring alone (SpringRate * maxTravel)
            // may not resist bottoming, so add a stiff term through the last stretch of travel. over^2/span
            // grows from zero slope at engagement to very stiff at the limit, so it ramps in smoothly rather
            // than as a hard corner.
            float bumpStart = maxCompression * Spec.BumpStopStartFraction;
            if (compression > bumpStart)
            {
                float over = compression - bumpStart;
                float span = Mathf.Max(1e-3f, maxCompression - bumpStart);
                force += Spec.BumpStopRateNPerM * (over * over) / span;
            }

            // Springs push, never pull (floor at 0); the ceiling caps landing/bottoming spikes so a single
            // corner's vertical force — and the tyre load it becomes downstream — can't fling the car.
            SuspensionForce[i] = Mathf.Clamp(force, 0f, Spec.MaxSuspensionForceN);
        }

        private void ApplyAntiRollBars()
        {
            // Transfers load across an axle proportional to the compression difference.
            for (int axle = 0; axle < 2; axle++)
            {
                int l = axle == 0 ? FL : RL;
                int r = axle == 0 ? FR : RR;
                if (!Grounded[l] || !Grounded[r]) continue;
                float transfer = (Compression[l] - Compression[r]) * Spec.AntiRollBarNPerM;
                SuspensionForce[l] = Mathf.Max(0f, SuspensionForce[l] - transfer);
                SuspensionForce[r] = Mathf.Max(0f, SuspensionForce[r] + transfer);
            }
        }

        // ------------------------------------------------------------------ wheels & tyres

        /// <summary>
        /// Advance one wheel's spin + tyre-slip state by a single substep of <paramref name="dt"/> and
        /// return the world-space force it produces this substep (suspension load + tyre long/lat), with
        /// its application point in <paramref name="appPoint"/>. Called WheelSubsteps times per Step; the
        /// caller averages the returned forces. Suspension load (SuspensionForce[i]) and the contact are
        /// held fixed across the substeps — only AngularVelocity[i] and the slip-relaxation state evolve.
        /// </summary>
        private Vector3 StepWheel(int i, float dt, in VehicleInput input, in GroundContact c, float driveTorquePerWheel, out Vector3 appPoint)
        {
            appPoint = Vector3.zero;
            float inertia = 0.5f * Spec.WheelMassKg * Spec.WheelRadiusM * Spec.WheelRadiusM;
            float torque = IsDriven(i) ? driveTorquePerWheel : 0f;

            // Brakes: base brake with bias, handbrake locks the rears. While reversing the
            // brake pedal is the reverse throttle and the THROTTLE pedal brakes — without
            // that, a reversing car couldn't slow down to leave reverse at all.
            float brakePedal = InReverse ? input.Throttle : input.Brake;
            float brakeTorque = brakePedal * Spec.BrakeTorqueNm *
                                (IsFrontWheel(i) ? Spec.BrakeBias : 1f - Spec.BrakeBias) * 2f;
            if (!IsFrontWheel(i))
                brakeTorque += input.Handbrake * Spec.HandbrakeTorqueNm;

            if (!c.Grounded)
            {
                // Airborne: just spin the wheel from drive/brake torque, and relax slip state. Produces
                // no ground force, so the averaged contribution over the substeps stays zero.
                AngularVelocity[i] += torque / inertia * dt;
                AngularVelocity[i] = ApplyBrake(AngularVelocity[i], brakeTorque, inertia, dt);
                _slipRatioState[i] *= AirborneSlipDecayPerSubstep;
                _slipAngleState[i] *= AirborneSlipDecayPerSubstep;
                return Vector3.zero;
            }

            TyreSpec tyre = IsFrontWheel(i) ? Spec.FrontTyre : Spec.RearTyre;

            // Contact-plane velocity decomposition.
            Vector3 fwd = Vector3.ProjectOnPlane(c.WheelForward, c.SurfaceNormal).normalized;
            Vector3 right = Vector3.ProjectOnPlane(c.WheelRight, c.SurfaceNormal).normalized;
            float vLong = Vector3.Dot(c.PointVelocity, fwd);
            float vLat = Vector3.Dot(c.PointVelocity, right);

            // Instantaneous slip (denominators guarded so standstill doesn't explode).
            float wheelSurfaceSpeed = AngularVelocity[i] * Spec.WheelRadiusM;
            float targetSlipRatio = (wheelSurfaceSpeed - vLong) / Mathf.Max(Mathf.Abs(vLong), 1.2f);
            float targetSlipAngle = Mathf.Atan2(vLat, Mathf.Max(Mathf.Abs(vLong), 0.8f));

            // Tyre relaxation length (SAE950311-style): slip — and therefore force — builds
            // over a fixed distance of travel, not instantly. This is the canonical cure for
            // the low-speed slip singularity: as speed drops the lag lengthens, turning what
            // would be a stiff oscillation into a stable first-order response.
            float travel = (Mathf.Abs(vLong) + 1.0f) * dt;
            _slipRatioState[i] += (targetSlipRatio - _slipRatioState[i]) * Mathf.Min(1f, travel / LongRelaxationLengthM);
            _slipAngleState[i] += (targetSlipAngle - _slipAngleState[i]) * Mathf.Min(1f, travel / LatRelaxationLengthM);

            float slipRatio = _slipRatioState[i];
            float slipAngleRad = _slipAngleState[i];
            SlipRatio[i] = slipRatio;
            SlipAngleDeg[i] = slipAngleRad * Mathf.Rad2Deg;

            // Normalized combined slip → friction circle.
            float sLong = slipRatio / tyre.PeakSlipRatio;
            float sLat = slipAngleRad / (tyre.PeakSlipAngleDeg * Mathf.Deg2Rad);
            float rho = Mathf.Sqrt(sLong * sLong + sLat * sLat);

            float load = SuspensionForce[i];
            // Combat grip sap, persistent wear AND the ground surface (grass/dirt vs tarmac) all fold
            // straight into the friction circle. SurfaceGripMult reads 1 for an unset contact, so this
            // is a no-op until a track marks a low-grip zone.
            float mu = TyreMu(tyre, rho, load) * GripEffectMult * DurabilityMult * c.SurfaceGripMult;
            if (!IsFrontWheel(i) && input.Handbrake > 0.1f)
                mu *= Spec.HandbrakeGripFactor;

            float totalForce = mu * load;
            float fLong = 0f, fLat = 0f;
            if (rho > 1e-4f)
            {
                fLong = totalForce * (sLong / rho);
                fLat = -totalForce * (sLat / rho);
            }

            // Low-speed stiction: below walking pace with no drive intent, kill residual sliding
            // instead of letting slip maths jitter the car around.
            float speed = c.PointVelocity.magnitude;
            if (speed < 0.6f && Mathf.Abs(torque) < 1f)
            {
                float stick = Mathf.InverseLerp(0.6f, 0.1f, speed);
                fLat = Mathf.Lerp(fLat, -vLat * load * 2f, stick);
                fLong = Mathf.Lerp(fLong, -vLong * load * 2f, stick);
            }

            // Overshoot clamps. The slip-correction dynamics are far stiffer than the fixed
            // timestep (τ ≈ 3 ms vs dt = 20 ms), so unclamped explicit integration makes slip
            // oscillate every step — phantom slip that eats the friction circle and reads as
            // permanently slidy tyres. Cap each force at "cancels its slip in exactly one
            // step" (plus sustained drive/brake force longitudinally); cf. ArcadeCarPhysics'
            // slideVelocity*m/dt static friction and RVP's force smoothing.
            float slipSpeed = wheelSurfaceSpeed - vLong;
            float wheelEquivMass = inertia / (Spec.WheelRadiusM * Spec.WheelRadiusM);
            float fLongMax = Mathf.Abs(slipSpeed) * wheelEquivMass / dt
                           + (Mathf.Abs(torque) + brakeTorque) / Spec.WheelRadiusM;
            fLong = Mathf.Clamp(fLong, -fLongMax, fLongMax);

            float carriedMass = load / 9.81f;
            float fLatMax = Mathf.Abs(vLat) * carriedMass / dt;
            fLat = Mathf.Clamp(fLat, -fLatMax, fLatMax);

            // Integrate wheel spin: drive torque minus tyre reaction, then brakes.
            float reaction = fLong * Spec.WheelRadiusM;
            AngularVelocity[i] += (torque - reaction) / inertia * dt;
            AngularVelocity[i] = ApplyBrake(AngularVelocity[i], brakeTorque, inertia, dt);

            // Tyre forces act between the contact patch and axle height (TyreForceAppLift):
            // full contact-patch application makes raycast cars roll over unrealistically hard,
            // because there's no real suspension geometry to generate jacking forces.
            appPoint = Vector3.Lerp(c.HitPoint, c.AttachPoint, Spec.TyreForceAppLift);
            return c.SuspensionUp * SuspensionForce[i] + fwd * fLong + right * fLat;
        }

        private static float ApplyBrake(float omega, float brakeTorque, float inertia, float dt)
        {
            // Brake opposes spin and must not reverse it within a step.
            float delta = brakeTorque / inertia * dt;
            return Mathf.MoveTowards(omega, 0f, delta);
        }

        /// <summary>
        /// Normalized slip curve: linear-ish rise to PeakMu at rho=1, decay toward SlideMu after.
        /// Load sensitivity trims mu as the wheel is loaded past its rated value.
        /// </summary>
        private static float TyreMu(in TyreSpec tyre, float rho, float load)
        {
            float mu;
            if (rho <= 1f)
                mu = tyre.PeakMu * rho * (2f - rho); // smooth rise, zero slope at the peak
            else
                mu = tyre.SlideMu + (tyre.PeakMu - tyre.SlideMu) / (1f + (rho - 1f) * tyre.FalloffSharpness);

            if (tyre.LoadSensitivity > 0f && load > tyre.RatedLoadN)
            {
                float overload = load / tyre.RatedLoadN - 1f;
                mu *= Mathf.Max(0.5f, 1f - tyre.LoadSensitivity * overload);
            }
            return mu;
        }
    }
}
