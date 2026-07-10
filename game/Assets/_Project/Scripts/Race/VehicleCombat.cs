using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// Host-side resolver for contact racing's attack layer (doc 03). Detection is inherently
    /// engine-side — it rides PhysX car-to-car collisions and an OverlapSphere aura — so it lives
    /// here, not in the headless sim. What it produces is generic grip/power saps injected into
    /// the OTHER car's <see cref="VehicleSim"/>, whose transient-effect subsystem decays them
    /// identically on a future server. Every race car carries one (RaceManager guarantees it);
    /// the profile is inert until RunDirector fills it from the player's equipped Attack parts.
    ///
    /// Also home to the universal "weighty collision" feel (Phase 1): any hard shunt — car or
    /// wall — briefly rattles the car's OWN grip, scaled by impact. On a car-to-car hit that cost
    /// is no longer symmetric: the car driving IN (higher closing speed, more forward-facing into
    /// the contact) is the aggressor — it shrugs off most of the rattle and gains a brief, finite
    /// forward surge, while the car being rammed takes the heavier grip dip AND the attack sap.
    /// Initiating contact pays; being rammed hurts — the core loop of a contact racer. Carrying
    /// Ram Bars stays a genuine edge, and wall-scraping keeps its symmetric, recoverable price
    /// rather than being consequence-free.
    ///
    /// Every impact also publishes one 0..1 severity number (and its direction) via
    /// <see cref="LastImpactSeverity"/> / <see cref="OnImpact"/> so a future camera-shake / rumble
    /// layer can scale off the SAME figure that drove the physics response — kept here as data only
    /// because this assembly can't reference the camera/input assemblies yet (see followups).
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public sealed class VehicleCombat : MonoBehaviour
    {
        [SerializeField] private AttackProfile profile = AttackProfile.None;

        [Header("Weighty-collision rattle (applies to self on any hard hit, attack parts or not)")]
        [Tooltip("Impulse (N·s) below which a contact is a harmless scrape.")]
        [SerializeField] private float rattleMinImpulse = 2500f;
        [Tooltip("Impulse (N·s) at which the rattle grip dip maxes out.")]
        [SerializeField] private float rattleFullImpulse = 11000f;
        [Tooltip("Largest grip fraction a single impact can dip your own car by.")]
        [Range(0f, 0.5f)][SerializeField] private float rattleMaxGripSap = 0.12f;
        [Tooltip("How fast the self-rattle grip dip recovers, per second.")]
        [SerializeField] private float rattleRecoverPerS = 0.5f;

        [Header("Contact roles (car-to-car aggressor vs victim)")]
        [Tooltip("Self-rattle multiplier when you're the pure aggressor (drove into them). <1 rewards initiating contact.")]
        [Range(0f, 1f)][SerializeField] private float aggressorRattleMult = 0.35f;
        [Tooltip("Self-rattle multiplier when you're the pure victim (got rammed). >=1 makes being rammed sting more.")]
        [Range(0.5f, 2f)][SerializeField] private float victimRattleMult = 1.25f;
        [Tooltip("Forward surge impulse (N·s) the aggressor gains on a full-severity ram, so a good hit carries momentum through contact.")]
        [SerializeField] private float aggressorSurgeImpulse = 4000f;
        [Tooltip("Smallest fraction of an attack part's sap a hit still delivers at the minimum qualifying impulse.")]
        [Range(0f, 1f)][SerializeField] private float attackSeverityFloor = 0.35f;
        [Tooltip("Smallest fraction of an attack part's sap you deal when you're being rammed rather than the aggressor.")]
        [Range(0f, 1f)][SerializeField] private float attackVictimFloor = 0.35f;

        [Header("Persistent damage (Wreckfest-style wear that lasts the whole race)")]
        [Tooltip("Durability fraction a full-severity hit strips from a car taking the FULL share (a pure victim, or a wall hit). Scaled down by severity and by the aggressor/victim role, and reset to full when the sim is rebuilt (a fresh car each race).")]
        [Range(0f, 0.25f)][SerializeField] private float damagePerFullHit = 0.08f;
        [Tooltip("Impulse severity (0..1) a contact must exceed to leave any lasting damage. Below it a hit still rattles grip but only scrapes/taps — no permanent wear.")]
        [Range(0f, 1f)][SerializeField] private float damageSeverityThreshold = 0.4f;

        private VehicleController _controller;
        private int _vehicleMask;
        private readonly Collider[] _auraHits = new Collider[16];

        /// <summary>Realtime stamp of the last attack this car LANDED on a rival — HUD flash only.</summary>
        public float LastAttackLandedRealtime { get; private set; } = -99f;

        /// <summary>True while this car projects a proximity aura (for HUD / future VFX).</summary>
        public bool HasAura => profile.HasAura;

        public AttackProfile Profile => profile;
        public void SetProfile(in AttackProfile p) => profile = p;

        // ---------------------------------------------------------------- presentation hook (read-only)
        // A camera-shake / rumble layer (different assembly, not yet referenced) consumes these later.
        // The physics response above and that future shake read the SAME severity number, so all of the
        // feedback for one hit stays coherent. Nothing here is gameplay state — it is pure output.

        /// <summary>Severity 0..1 of the most recent impact (car OR wall), mapped from collision impulse.
        /// Not decayed here — a presentation layer owns its own falloff off this one figure.</summary>
        public float LastImpactSeverity { get; private set; }

        /// <summary>World-space unit direction the most recent impact shoved THIS car (contact → car),
        /// for a directional camera recoil. <see cref="Vector3.zero"/> when undefined.</summary>
        public Vector3 LastImpactDirection { get; private set; }

        /// <summary>Realtime stamp of the last non-trivial impact — for a presentation layer to gate on.</summary>
        public float LastImpactRealtime { get; private set; } = -99f;

        /// <summary>Payload for <see cref="OnImpact"/>: everything a camera/rumble layer needs, all off the
        /// same severity number that drove the physics response.</summary>
        public readonly struct ImpactEvent
        {
            public readonly float Severity;     // 0..1
            public readonly Vector3 Direction;  // world-space unit push on this car
            public readonly Vector3 Point;      // world contact point
            public readonly bool WasAggressor;  // true if this car initiated the hit

            public ImpactEvent(float severity, Vector3 direction, Vector3 point, bool wasAggressor)
            {
                Severity = severity;
                Direction = direction;
                Point = point;
                WasAggressor = wasAggressor;
            }
        }

        /// <summary>Fires on each non-trivial impact so a (future) camera/rumble layer can subscribe without
        /// this assembly referencing theirs. Presentation-only — no gameplay logic reads it.</summary>
        public event System.Action<ImpactEvent> OnImpact;

        /// <summary>Adds the component if the car doesn't already have one — safe to call repeatedly.</summary>
        public static VehicleCombat GetOrAdd(GameObject go) =>
            go.TryGetComponent(out VehicleCombat existing) ? existing : go.AddComponent<VehicleCombat>();

        private void Awake()
        {
            _controller = GetComponent<VehicleController>();
            _vehicleMask = 1 << gameObject.layer; // cars all share the Vehicle layer; only their root box collides
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Impact magnitude drives everything below (rattle, saps, surge, the presentation hook), so a
            // non-finite PhysX impulse must be dropped before it can poison any of them.
            float impact = collision.impulse.magnitude;
            if (!IsFinite(impact)) return;
            float severity01 = Mathf.InverseLerp(rattleMinImpulse, rattleFullImpulse, impact); // 0..1, clamped

            // Aggressor/victim classification — car-to-car only; walls stay role-neutral (0.5).
            VehicleController other = ResolveVehicle(collision.collider);
            bool isCarHit = other != null && other != _controller && other.Sim != null;
            float aggressorness = isCarHit ? ClassifyAggressor(other) : 0.5f; // 0 victim · 0.5 wall/mutual · 1 aggressor

            // Presentation hook: publish the ONE severity number (and where it shoved us) for a future
            // camera/rumble layer. Direction is contact → our centre, i.e. the way the hit pushes the view.
            Vector3 contactPoint = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            Vector3 push = transform.position - contactPoint;
            LastImpactSeverity = severity01;
            LastImpactDirection = push.sqrMagnitude > 1e-6f ? push.normalized : Vector3.zero;
            if (severity01 > 0f)
            {
                LastImpactRealtime = Time.time;
                OnImpact?.Invoke(new ImpactEvent(severity01, LastImpactDirection, contactPoint, aggressorness > 0.5f));
            }

            // Aggressor/victim split shared by the self-rattle and the persistent wear below: the aggressor
            // shrugs both off (aggressorRattleMult < 1), the victim takes the heavier share; walls stay
            // role-neutral (full). Cheap and side-effect-free, so it is safe to compute unconditionally.
            float selfRoleMult = isCarHit ? Mathf.Lerp(victimRattleMult, aggressorRattleMult, aggressorness) : 1f;

            // Universal feel: any hard hit rattles our own grip, scaled by severity AND by role — the
            // aggressor shrugs it off, the victim takes the heavier dip. Clamped so it can never over-sap.
            if (severity01 > 0f && _controller.Sim != null)
            {
                float sap = Mathf.Clamp(rattleMaxGripSap * severity01 * selfRoleMult, 0f, 0.9f);
                _controller.Sim.ApplyGripSap(sap, rattleRecoverPerS);
            }

            // Persistent Wreckfest-style wear: only HARD shunts leave damage that lasts the whole race —
            // lighter contact rattles grip (above) but does not permanently wear the car. This car damages
            // only its OWN sim, scaled by how far past the gate the hit landed and by the same role split,
            // so across a car-to-car collision the rammed car wears heavily while the aggressor (whose own
            // callback runs the same code with aggressorRattleMult) takes a genuinely smaller share. A wall
            // hit takes the full share. ApplyDamage is finite, clamps to a floor, and is reset on RebuildSim.
            if (severity01 > damageSeverityThreshold && _controller.Sim != null)
            {
                float hardness = Mathf.InverseLerp(damageSeverityThreshold, 1f, severity01); // 0..1 past the gate
                _controller.Sim.ApplyDamage(damagePerFullHit * hardness * selfRoleMult);
            }

            // Aggressor reward: a brief, finite forward surge so a good ram carries momentum through the
            // hit instead of stalling on the contact impulse. Only the aggressor half of the range surges.
            if (isCarHit && aggressorness > 0.5f)
                ApplyAggressorSurge(severity01, aggressorness);

            // Attack: only car-to-car, only above our part's impulse gate.
            if (!profile.HasContact || impact < profile.MinImpactImpulse || !isCarHit) return;

            // Saps bite hardest when WE drove in (aggressorness) and when the hit was hard (severity); the
            // floors keep a marginal or reversed hit from zeroing an equipped part. All factors are 0..1,
            // and ApplyGripSap/ApplyPowerSap clamp again, so the result is always finite and bounded.
            float attackScale = Mathf.Clamp01(Mathf.Lerp(attackSeverityFloor, 1f, severity01)
                                              * Mathf.Lerp(attackVictimFloor, 1f, aggressorness));
            other.Sim.ApplyGripSap(profile.ContactGripSap * attackScale, profile.ContactRecoverPerS);
            other.Sim.ApplyPowerSap(profile.ContactPowerSap * attackScale, profile.ContactRecoverPerS);
            LastAttackLandedRealtime = Time.time;
        }

        /// <summary>
        /// 0..1 estimate of how much THIS car is the aggressor in a car-to-car hit: 1 = we drove into
        /// them, 0 = we were rammed, ~0.5 = a mutual/side smack. Built from symmetric inputs (each car's
        /// velocity, facing, and the closing direction), so both cars derive the same split — one's
        /// aggressorness is exactly the other's (1 − x) — and they always agree on who initiated.
        /// </summary>
        private float ClassifyAggressor(VehicleController other)
        {
            Rigidbody myBody = _controller.Body;
            Rigidbody otherBody = other.Body;
            if (myBody == null || otherBody == null) return 0.5f;

            Vector3 toOther = other.transform.position - transform.position;
            if (toOther.sqrMagnitude < 1e-6f) return 0.5f; // co-located: no meaningful direction
            Vector3 dirToOther = toOther.normalized;

            // Each car's "drive-in": how fast it's closing on the other, gated by how forward-facing it is
            // into the contact. A car reversing or facing away contributes nothing toward being aggressor.
            float myDriveIn = Mathf.Max(0f, Vector3.Dot(myBody.linearVelocity, dirToOther))
                              * Mathf.Clamp01(Vector3.Dot(transform.forward, dirToOther));
            float otherDriveIn = Mathf.Max(0f, Vector3.Dot(otherBody.linearVelocity, -dirToOther))
                                 * Mathf.Clamp01(Vector3.Dot(other.transform.forward, -dirToOther));

            float total = myDriveIn + otherDriveIn;
            return total > 1e-4f ? myDriveIn / total : 0.5f;
        }

        /// <summary>Push the aggressor forward along its own (horizontal) heading by a severity- and
        /// role-scaled impulse. Guards a missing body and a degenerate/non-finite impulse.</summary>
        private void ApplyAggressorSurge(float severity01, float aggressorness)
        {
            Rigidbody body = _controller.Body;
            if (body == null) return;

            float aggro01 = Mathf.InverseLerp(0.5f, 1f, aggressorness); // remap the aggressor half to 0..1
            float impulse = aggressorSurgeImpulse * severity01 * aggro01;
            if (!(impulse > 0f)) return; // also rejects NaN

            // Flatten to horizontal so a ram can never launch the car skyward; fall back to raw forward
            // only if forward is (near) vertical.
            Vector3 dir = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : transform.forward;

            Vector3 surge = dir * impulse;
            if (IsFinite(surge)) body.AddForce(surge, ForceMode.Impulse);
        }

        private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        private static bool IsFinite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);

        private void FixedUpdate()
        {
            if (!profile.HasAura) return;

            Vector3 pos = transform.position;
            Vector3 fwd = transform.forward;
            int count = Physics.OverlapSphereNonAlloc(pos, profile.AuraRadiusM, _auraHits,
                _vehicleMask, QueryTriggerInteraction.Ignore);

            bool landed = false;
            for (int i = 0; i < count; i++)
            {
                var other = ResolveVehicle(_auraHits[i]);
                if (other == null || other == _controller || other.Sim == null) continue;

                // Disruptor Field bites the cars sitting on your gearbox, not the ones you're chasing.
                if (Vector3.Dot(other.transform.position - pos, fwd) >= 0f) continue;

                other.Sim.ApplyGripSap(profile.AuraGripSap, profile.AuraRecoverPerS);
                landed = true;
            }
            if (landed) LastAttackLandedRealtime = Time.time;
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
            if (!Application.isPlaying || !profile.HasAura) return;
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, profile.AuraRadiusM);
        }
#endif
    }
}
