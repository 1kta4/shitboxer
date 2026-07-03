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

        public VehicleSim(VehicleSpec spec)
        {
            Spec = spec;
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

        /// <summary>
        /// Advance the sim by dt. Returns forces to apply to the chassis rigidbody this step.
        /// The returned array is reused between calls — consume it immediately.
        /// </summary>
        public ForceCommand[] Step(float dt, in VehicleInput input, GroundContact[] contacts,
            Vector3 chassisVelocity, Vector3 chassisForward, Vector3 chassisUp,
            Vector3 chassisAngularVelocity)
        {
            UpdateSteering(dt, input.Steer, chassisVelocity.magnitude);

            float forwardSpeed = Vector3.Dot(chassisVelocity, chassisForward);
            UpdateGearbox(dt, input, forwardSpeed);

            // Suspension first: tyre grip this step scales with the spring load it produces.
            for (int i = 0; i < WheelCount; i++)
                StepSuspension(i, dt, contacts[i]);
            ApplyAntiRollBars();

            // Arcade pedal convention: while reversing, the brake pedal is the reverse throttle.
            float driveTorquePerWheel = ComputeDriveTorque(InReverse ? input.Brake : input.Throttle);

            for (int i = 0; i < WheelCount; i++)
                StepWheel(i, dt, input, contacts[i], driveTorquePerWheel);

            // Aero: quadratic drag opposing velocity, plus downforce along -up.
            float v = chassisVelocity.magnitude;
            Vector3 comForce = -Spec.DragCoeff * v * chassisVelocity - Spec.DownforceCoeff * v * v * chassisUp;

            comForce += StepAssists(input, chassisVelocity, chassisForward, chassisUp, chassisAngularVelocity);

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
        private Vector3 StepAssists(in VehicleInput input, Vector3 velocity, Vector3 forward,
            Vector3 up, Vector3 angularVelocity)
        {
            // Extra gravity always applies — heavy landings are most of "not floaty".
            Vector3 force = Vector3.down * (Spec.MassKg * 9.81f * Spec.ExtraGravity);
            BodyTorque = Vector3.zero;

            int groundedCount = 0;
            for (int i = 0; i < WheelCount; i++)
                if (Grounded[i]) groundedCount++;
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
                engineTorque = Spec.Engine.TorqueAt(EngineRpm) * throttle;
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
            Grounded[i] = c.Grounded;
            if (!c.Grounded)
            {
                Compression[i] = 0f;
                SuspensionForce[i] = 0f;
                return;
            }

            float prev = Compression[i];
            Compression[i] = Mathf.Clamp(
                Spec.SuspensionRestLengthM - (c.HitDistance - Spec.WheelRadiusM),
                0f, Spec.SuspensionRestLengthM + Spec.SuspensionTravelM);

            float compressionSpeed = (Compression[i] - prev) / dt;
            float force = Spec.SpringRateNPerM * Compression[i]
                        + Spec.DamperRateNPerMps * compressionSpeed;
            SuspensionForce[i] = Mathf.Max(0f, force); // springs push, never pull
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

        private void StepWheel(int i, float dt, in VehicleInput input, in GroundContact c, float driveTorquePerWheel)
        {
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
                // Airborne: just spin the wheel from drive/brake torque, and relax slip state.
                AngularVelocity[i] += torque / inertia * dt;
                AngularVelocity[i] = ApplyBrake(AngularVelocity[i], brakeTorque, inertia, dt);
                _slipRatioState[i] *= 0.9f;
                _slipAngleState[i] *= 0.9f;
                _forces[i] = default;
                return;
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
            float mu = TyreMu(tyre, rho, load);
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
            Vector3 appPoint = Vector3.Lerp(c.HitPoint, c.AttachPoint, Spec.TyreForceAppLift);
            Vector3 force = c.SuspensionUp * SuspensionForce[i] + fwd * fLong + right * fLat;
            _forces[i] = new ForceCommand { Force = force, Position = appPoint };
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
