using UnityEngine;

namespace Shitboxer.Vehicle
{
    /// <summary>
    /// Host adapter between VehicleSim and a PhysX rigidbody: sphere-casts the ground,
    /// feeds contacts into the sim, applies the returned forces, and poses wheel visuals.
    /// Keep this thin — all car behaviour belongs in VehicleSim so a headless server can
    /// run the identical maths.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleController : MonoBehaviour
    {
        [SerializeField] private VehicleSpecAsset specAsset;
        [Tooltip("Layers the wheel rays collide with. Exclude the Vehicle layer so cars never ray-hit themselves.")]
        [SerializeField] private LayerMask groundMask = ~0;
        [Tooltip("Optional wheel visual transforms: FL, FR, RL, RR.")]
        [SerializeField] private Transform[] wheelVisuals = new Transform[4];
        [Tooltip("Radius (m) of the wheel ground probe, a fraction of the tyre width. A fat sphere sweep " +
                 "catches curbs and edges a single centre ray would skip or 'pop' over, and smooths jumps. " +
                 "0 degenerates to the old single downward ray.")]
        [SerializeField] private float contactProbeRadius = 0.06f;

        public VehicleInput Input;               // written by an input provider or AI each frame
        public VehicleSim Sim { get; private set; }
        public Rigidbody Body { get; private set; }
        public VehicleSpecAsset SpecAsset => specAsset;

        public float SpeedKmh => Body ? Body.linearVelocity.magnitude * 3.6f : 0f;

        /// <summary>Persistent 0..1 structural integrity of this car for the current race (1 = fresh), for a
        /// HUD / repair layer to read later. Read-only here — lasting damage is applied through the combat
        /// layer, and RebuildSim (a fresh car each race) resets it to 1. Falls back to 1 before the sim exists.</summary>
        public float Durability => Sim != null ? Sim.Durability : 1f;

        /// <summary>
        /// Lowest surface-grip multiplier under any GROUNDED wheel as of the last contact gather —
        /// 1 while every wheel is on tarmac (or the track marks no <see cref="SurfaceZone"/> at all),
        /// lower with a wheel on grass/dirt/gravel. Read-only telemetry: the value is already folded into
        /// the tyre friction circle inside the sim, and this simply surfaces it so an observation layer
        /// can tell that a car ran wide without duplicating the surface lookup.
        /// Defaults to 1, so a car that has never stepped reads as fully on-surface.
        /// </summary>
        public float SurfaceGripMult { get; private set; } = 1f;

        private readonly GroundContact[] _contacts = new GroundContact[VehicleSim.WheelCount];
        private readonly float[] _visualSpin = new float[VehicleSim.WheelCount];

        // Fall-through / NaN watchdog state: the last pose where the car was finite, upright and on
        // the ground. A transient bad force or collision must never permanently brick the car by
        // corrupting its rigidbody or punting it out of the world ("falls through, can't move").
        [Tooltip("If the car drops this many metres below its last safe pose, treat it as fallen out of the world and recover.")]
        [SerializeField] private float fallRecoverMarginM = 25f;
        private Vector3 _safePosition;
        private Quaternion _safeRotation;
        private bool _hasSafePose;

        private void Awake()
        {
            Body = GetComponent<Rigidbody>();
            _safePosition = transform.position;
            _safeRotation = transform.rotation;
            _hasSafePose = true;
            RebuildSim();
        }

        /// <summary>Re-create the sim from the spec (call after swapping/tuning specs at runtime).</summary>
        public void RebuildSim()
        {
            Sim = new VehicleSim(specAsset.Spec);
            Body.mass = specAsset.Spec.MassKg;
            Body.centerOfMass = specAsset.Spec.CentreOfMassOffset;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        private void FixedUpdate()
        {
            // Watchdog: if the rigidbody state has gone non-finite or the car has fallen out of the
            // world, restore the last good pose instead of stepping physics from garbage. Cures the
            // "falls through the ground and can't move" failure mode regardless of what seeded it.
            if (!StateIsUsable())
            {
                RecoverToSafePose();
                return;
            }

            float dt = Time.fixedDeltaTime;
            GatherContacts();

            ForceCommand[] forces = Sim.Step(dt, Input, _contacts,
                Body.linearVelocity, transform.forward, transform.up, Body.angularVelocity);

            // Finite-guard every applied force/torque so one bad value can never corrupt the body.
            for (int i = 0; i < VehicleSim.WheelCount; i++)
                if (forces[i].Force.sqrMagnitude > 0f && IsFinite(forces[i].Force) && IsFinite(forces[i].Position))
                    Body.AddForceAtPosition(forces[i].Force, forces[i].Position);

            // Aero + assist forces act at the centre of mass; assists may also add torque.
            Vector3 comForce = forces[VehicleSim.WheelCount].Force;
            if (IsFinite(comForce)) Body.AddForce(comForce);
            if (IsFinite(Sim.BodyTorque)) Body.AddTorque(Sim.BodyTorque);

            RecordSafePoseIfGood();
        }

        private static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
            !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);

        /// <summary>False if the body's state is non-finite or it has fallen far below the last safe pose.</summary>
        private bool StateIsUsable()
        {
            if (!IsFinite(Body.position) || !IsFinite(Body.linearVelocity) || !IsFinite(Body.angularVelocity))
                return false;
            return !_hasSafePose || Body.position.y > _safePosition.y - fallRecoverMarginM;
        }

        /// <summary>Teleport back to the last good pose, at rest, with a fresh sim.</summary>
        private void RecoverToSafePose()
        {
            if (!_hasSafePose) return;
            Debug.LogWarning($"[VehicleController] {name} recovered from a non-finite / out-of-world state.", this);
            Body.position = _safePosition + Vector3.up * 0.5f;
            Body.rotation = _safeRotation;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            RebuildSim(); // clears any corrupted wheel-spin / effect state
        }

        /// <summary>Remember the current pose as a recovery target only when it is finite, upright and grounded.</summary>
        private void RecordSafePoseIfGood()
        {
            if (transform.up.y < 0.5f || !IsFinite(Body.position)) return;
            for (int i = 0; i < VehicleSim.WheelCount; i++)
            {
                if (!Sim.Grounded[i]) continue;
                _safePosition = Body.position;
                _safeRotation = Body.rotation;
                _hasSafePose = true;
                return;
            }
        }

        // Casts start this far ABOVE the attach point. A downward sweep can't register a collider
        // it begins inside of, so a chassis that ever sinks slightly below the surface would
        // otherwise go blind (no suspension force, no recovery). Starting high keeps the ground
        // visible and lets the springs eject the car; the lift is subtracted from the hit distance
        // so the sim's suspension maths is unchanged.
        private const float RayStartLiftM = 0.4f;

        private void GatherContacts()
        {
            var spec = specAsset.Spec;
            float rayLength = spec.SuspensionRestLengthM + spec.SuspensionTravelM + spec.WheelRadiusM;

            // Sweep a small sphere (not a hair-thin ray) down the suspension line so a wheel catches
            // curbs/edges a single centre ray would skip or pop over. A spherecast reports the distance
            // its CENTRE travels, so the leading face reaches one radius further; probe over
            // rayLength+lift MINUS the radius to plant that face exactly where the old ray tip stopped,
            // keeping grounded reach (and HitDistance on flat ground) identical. radius 0 collapses the
            // whole thing back to the original single downward raycast.
            float probeRadius = Mathf.Max(0f, contactProbeRadius);
            float castDistance = Mathf.Max(0f, rayLength + RayStartLiftM - probeRadius);

            // Tracked across the wheel loop below and published for the observation layer. Starts at 1
            // (full tarmac) so an airborne car — no grounded wheel to sample — reads as on-surface
            // rather than as having run wide.
            float lowestSurfaceGrip = 1f;

            for (int i = 0; i < VehicleSim.WheelCount; i++)
            {
                Vector3 attach = transform.TransformPoint(Sim.WheelLocalPosition(i));
                float steer = Sim.IsFrontWheel(i) ? Sim.SteerAngleDeg : 0f;
                Quaternion steerRot = Quaternion.AngleAxis(steer, transform.up);

                Vector3 rayOrigin = attach + transform.up * RayStartLiftM;
                RaycastHit hitInfo;
                bool hit = probeRadius > 0f
                    ? Physics.SphereCast(rayOrigin, probeRadius, -transform.up, out hitInfo,
                        castDistance, groundMask, QueryTriggerInteraction.Ignore)
                    : Physics.Raycast(rayOrigin, -transform.up, out hitInfo,
                        castDistance, groundMask, QueryTriggerInteraction.Ignore);

                // If the sphere begins already overlapping a collider (chassis sunk so the sweep starts
                // inside the ground) SphereCast reports distance 0 with a degenerate zero normal. Treat
                // that like the ray's inside-a-collider case: stay grounded but fall back to an up-normal
                // and a point below the attach so the springs still get a clean eject, never a NaN basis.
                bool degenerate = hit && hitInfo.normal.sqrMagnitude < 1e-6f;
                Vector3 hitPoint = (hit && !degenerate) ? hitInfo.point : attach - transform.up * rayLength;
                Vector3 hitNormal = (hit && !degenerate) ? hitInfo.normal : transform.up;

                // Surface grip: a SurfaceZone on the hit collider (or a parent of it) marks grass/dirt as
                // low-grip. Absent -> full tarmac grip (1), so unmarked tracks are unaffected.
                float surfaceGrip = 1f;
                if (hit && hitInfo.collider)
                {
                    SurfaceZone zone = hitInfo.collider.GetComponent<SurfaceZone>();
                    if (!zone) zone = hitInfo.collider.GetComponentInParent<SurfaceZone>();
                    if (zone) surfaceGrip = zone.GripMultiplier;
                }
                if (hit && surfaceGrip < lowestSurfaceGrip) lowestSurfaceGrip = surfaceGrip;

                _contacts[i] = new GroundContact
                {
                    Grounded = hit,
                    // Centre-travel distance -> attach->ground clearance: add the sphere radius back (the
                    // surface sits one radius beyond the sphere centre along the cast), then subtract the
                    // start lift exactly as the single ray did. Reduces to hitInfo.distance - lift at radius 0.
                    HitDistance = hit ? hitInfo.distance + probeRadius - RayStartLiftM : rayLength,
                    HitPoint = hitPoint,
                    SurfaceNormal = hitNormal,
                    PointVelocity = Body.GetPointVelocity(hit ? hitPoint : attach),
                    SuspensionUp = transform.up,
                    WheelForward = steerRot * transform.forward,
                    WheelRight = steerRot * transform.right,
                    AttachPoint = attach,
                    SurfaceGripMult = surfaceGrip,
                };
            }

            SurfaceGripMult = lowestSurfaceGrip;
        }

        private void Update()
        {
            PoseWheelVisuals();
        }

        private void PoseWheelVisuals()
        {
            var spec = specAsset.Spec;
            for (int i = 0; i < VehicleSim.WheelCount; i++)
            {
                Transform w = wheelVisuals[i];
                if (!w) continue;

                float extension = Sim.Grounded[i]
                    ? spec.SuspensionRestLengthM - Sim.Compression[i]
                    : spec.SuspensionRestLengthM + spec.SuspensionTravelM;
                Vector3 local = Sim.WheelLocalPosition(i) + Vector3.down * extension;
                w.localPosition = local;

                _visualSpin[i] += Sim.AngularVelocity[i] * Mathf.Rad2Deg * Time.deltaTime;
                float steer = Sim.IsFrontWheel(i) ? Sim.SteerAngleDeg : 0f;
                w.localRotation = Quaternion.Euler(0f, steer, 0f) * Quaternion.Euler(_visualSpin[i], 0f, 0f);
            }
        }

        public void SetSpec(VehicleSpecAsset asset)
        {
            specAsset = asset;
            if (Body) RebuildSim();
        }

        public void SetWheelVisuals(Transform[] wheels) => wheelVisuals = wheels;
        public void SetGroundMask(LayerMask mask) => groundMask = mask;

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (Sim == null) return;
            for (int i = 0; i < VehicleSim.WheelCount; i++)
            {
                Gizmos.color = Sim.Grounded[i] ? Color.green : Color.red;
                Vector3 attach = transform.TransformPoint(Sim.WheelLocalPosition(i));
                Gizmos.DrawLine(attach, attach - transform.up *
                    (specAsset.Spec.SuspensionRestLengthM + specAsset.Spec.WheelRadiusM));
            }
        }
#endif
    }
}
