using UnityEngine;

namespace Shitboxer.Vehicle
{
    /// <summary>
    /// Host adapter between VehicleSim and a PhysX rigidbody: raycasts the ground,
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

        public VehicleInput Input;               // written by an input provider or AI each frame
        public VehicleSim Sim { get; private set; }
        public Rigidbody Body { get; private set; }
        public VehicleSpecAsset SpecAsset => specAsset;

        public float SpeedKmh => Body ? Body.linearVelocity.magnitude * 3.6f : 0f;

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

        // Rays start this far ABOVE the attach point. Raycasts can't hit a collider from
        // inside it, so a chassis that ever sinks slightly below the surface would otherwise
        // go blind (no suspension force, no recovery). Starting high keeps the ground visible
        // and lets the springs eject the car; the lift is subtracted from the hit distance so
        // the sim's suspension maths is unchanged.
        private const float RayStartLiftM = 0.4f;

        private void GatherContacts()
        {
            var spec = specAsset.Spec;
            float rayLength = spec.SuspensionRestLengthM + spec.SuspensionTravelM + spec.WheelRadiusM;

            for (int i = 0; i < VehicleSim.WheelCount; i++)
            {
                Vector3 attach = transform.TransformPoint(Sim.WheelLocalPosition(i));
                float steer = Sim.IsFrontWheel(i) ? Sim.SteerAngleDeg : 0f;
                Quaternion steerRot = Quaternion.AngleAxis(steer, transform.up);

                Vector3 rayOrigin = attach + transform.up * RayStartLiftM;
                bool hit = Physics.Raycast(rayOrigin, -transform.up, out RaycastHit hitInfo,
                    rayLength + RayStartLiftM, groundMask, QueryTriggerInteraction.Ignore);

                _contacts[i] = new GroundContact
                {
                    Grounded = hit,
                    HitDistance = hit ? hitInfo.distance - RayStartLiftM : rayLength,
                    HitPoint = hit ? hitInfo.point : attach - transform.up * rayLength,
                    SurfaceNormal = hit ? hitInfo.normal : transform.up,
                    PointVelocity = Body.GetPointVelocity(hit ? hitInfo.point : attach),
                    SuspensionUp = transform.up,
                    WheelForward = steerRot * transform.forward,
                    WheelRight = steerRot * transform.right,
                    AttachPoint = attach,
                };
            }
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
