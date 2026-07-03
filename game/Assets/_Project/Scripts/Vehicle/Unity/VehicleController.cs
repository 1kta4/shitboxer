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

        private void Awake()
        {
            Body = GetComponent<Rigidbody>();
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
            float dt = Time.fixedDeltaTime;
            GatherContacts();

            ForceCommand[] forces = Sim.Step(dt, Input, _contacts,
                Body.linearVelocity, transform.forward, transform.up, Body.angularVelocity);

            for (int i = 0; i < VehicleSim.WheelCount; i++)
                if (forces[i].Force.sqrMagnitude > 0f)
                    Body.AddForceAtPosition(forces[i].Force, forces[i].Position);

            // Aero + assist forces act at the centre of mass; assists may also add torque.
            Body.AddForce(forces[VehicleSim.WheelCount].Force);
            Body.AddTorque(Sim.BodyTorque);
        }

        private void GatherContacts()
        {
            var spec = specAsset.Spec;
            float rayLength = spec.SuspensionRestLengthM + spec.SuspensionTravelM + spec.WheelRadiusM;

            for (int i = 0; i < VehicleSim.WheelCount; i++)
            {
                Vector3 attach = transform.TransformPoint(Sim.WheelLocalPosition(i));
                float steer = Sim.IsFrontWheel(i) ? Sim.SteerAngleDeg : 0f;
                Quaternion steerRot = Quaternion.AngleAxis(steer, transform.up);

                bool hit = Physics.Raycast(attach, -transform.up, out RaycastHit hitInfo,
                    rayLength, groundMask, QueryTriggerInteraction.Ignore);

                _contacts[i] = new GroundContact
                {
                    Grounded = hit,
                    HitDistance = hit ? hitInfo.distance : rayLength,
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
