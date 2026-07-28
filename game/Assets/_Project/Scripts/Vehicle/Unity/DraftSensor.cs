using UnityEngine;

namespace Shitboxer.Vehicle
{
    /// <summary>
    /// Host-side slipstream sensor: each FixedUpdate it looks a short way AHEAD (within a narrow
    /// forward cone and only above a minimum speed) for another car whose wake this car is sitting
    /// in, and tells this car's <see cref="VehicleSim"/> to cut its aero drag — a draft/tow that
    /// rewards close racing and sets up the "Draft Leech" idea. Detection is inherently engine-side
    /// (an OverlapSphere over the shared Vehicle layer, mirroring VehicleCombat) so it lives here,
    /// not in the headless sim; the sim only eases the drag cut it is handed and recovers it once the
    /// car pulls out.
    ///
    /// Every race car carries one (RaceManager guarantees it). Inert until it finds a car ahead — it
    /// simply stops re-asserting the draft, and the sim's <see cref="VehicleSim.DraftDragMult"/>
    /// eases back to 1 (clean air) on its own.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public sealed class DraftSensor : MonoBehaviour
    {
        [Header("Draft window")]
        [Tooltip("How far ahead (m) a leading car's wake still tows this car.")]
        [SerializeField] private float rangeM = 12f;
        [Tooltip("Half-width (m) of the wake corridor: a car offset more than this from our nose line isn't drafted.")]
        [SerializeField] private float corridorHalfWidthM = 3f;
        [Tooltip("Forward-cone cosine gate: the car ahead must sit within this cone (1 = dead ahead, 0 = 90 deg). 0.8 ~= a 37 deg half-cone.")]
        [Range(0f, 1f)][SerializeField] private float minForwardDot = 0.8f;
        [Tooltip("Below this forward speed (m/s) there's no meaningful slipstream, so no draft.")]
        [SerializeField] private float minSpeedMps = 8f;

        [Header("Draft strength")]
        [Tooltip("Drag multiplier right on the leading car's tail (deepest tow). 0.6 keeps ~40% of drag; fades to 1 at range.")]
        [Range(0.4f, 1f)][SerializeField] private float maxDraftDragMult = 0.6f;

        private VehicleController _controller;
        private int _vehicleMask;
        private readonly Collider[] _hits = new Collider[16];

        /// <summary>True on the last FixedUpdate this car was sitting in another's tow (for HUD / future VFX).</summary>
        public bool IsDrafting { get; private set; }

        /// <summary>Adds the component if the car doesn't already have one — safe to call repeatedly.</summary>
        public static DraftSensor GetOrAdd(GameObject go) =>
            go.TryGetComponent(out DraftSensor existing) ? existing : go.AddComponent<DraftSensor>();

        // A disabled sensor stops running FixedUpdate, which would otherwise FREEZE IsDrafting at
        // whatever it last read — and the DIRTY AIR boss (doc 08 slice 12) disables sensors as its
        // whole mechanism. Dead air must read as dead air.
        private void OnDisable() => IsDrafting = false;

        private void Awake()
        {
            _controller = GetComponent<VehicleController>();
            _vehicleMask = 1 << gameObject.layer; // cars all share the Vehicle layer; only their root box collides
        }

        private void FixedUpdate()
        {
            IsDrafting = false;

            VehicleSim sim = _controller ? _controller.Sim : null;
            Rigidbody body = _controller ? _controller.Body : null;
            if (sim == null || body == null) return;

            Vector3 fwd = transform.forward;
            float forwardSpeed = Vector3.Dot(body.linearVelocity, fwd);
            if (forwardSpeed < minSpeedMps) return; // no meaningful slipstream at a crawl

            Vector3 pos = transform.position;

            // One overlap covering the whole forward corridor: a sphere centred half-range ahead reaches
            // from the nose out to rangeM, wide enough to catch a car offset by the corridor half-width. The
            // per-hit filters below narrow it to cars genuinely ahead, in the cone, and close to the nose line.
            Vector3 probeCentre = pos + fwd * (rangeM * 0.5f);
            float overlapRadius = rangeM * 0.5f + corridorHalfWidthM;
            int count = Physics.OverlapSphereNonAlloc(probeCentre, overlapRadius, _hits,
                _vehicleMask, QueryTriggerInteraction.Ignore);

            float bestFactor = 1f; // 1 = clean air; lower = a deeper tow
            for (int i = 0; i < count; i++)
            {
                VehicleController other = ResolveVehicle(_hits[i]);
                if (other == null || other == _controller) continue;

                Vector3 toOther = other.transform.position - pos;
                float ahead = Vector3.Dot(toOther, fwd);
                if (ahead <= 0f || ahead > rangeM) continue; // must be genuinely ahead, within range

                // Perpendicular offset from our nose line — a car off to the side isn't in the wake.
                float lateral = Vector3.ProjectOnPlane(toOther, fwd).magnitude;
                if (lateral > corridorHalfWidthM) continue;

                // Forward-cone gate (dead-ahead = 1); guards a co-located car before the divide.
                float dist = toOther.magnitude;
                if (dist < 1e-4f || ahead / dist < minForwardDot) continue;

                // Proximity factor: deepest tow right on the leader's tail, fading to none (1) at range.
                float close01 = 1f - Mathf.Clamp01(ahead / rangeM);
                float factor = Mathf.Lerp(1f, maxDraftDragMult, close01);
                if (factor < bestFactor) bestFactor = factor;
            }

            if (bestFactor < 1f)
            {
                sim.ApplyDraft(bestFactor); // sim clamps to MinDraftDragMult and eases it
                IsDrafting = true;
            }
        }

        /// <summary>The VehicleController owning a collider hit (its root, via the shared rigidbody).</summary>
        private static VehicleController ResolveVehicle(Collider col)
        {
            if (!col) return null;
            Rigidbody rb = col.attachedRigidbody;
            return rb ? rb.GetComponent<VehicleController>() : col.GetComponentInParent<VehicleController>();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;
            Gizmos.color = IsDrafting ? new Color(0.2f, 0.9f, 1f, 0.6f) : new Color(0.3f, 0.5f, 0.6f, 0.3f);
            Vector3 pos = transform.position;
            Vector3 fwd = transform.forward;
            Gizmos.DrawLine(pos, pos + fwd * rangeM);
            Gizmos.DrawWireSphere(pos + fwd * rangeM, corridorHalfWidthM);
        }
#endif
    }
}
