using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// Accumulates the CAR-LOCAL half of one sector's <see cref="SectorEvidence"/>: things a car can
    /// observe about itself — who it hit and whose fault that was, how long it spent in a tow, off the
    /// pedals, sideways, or off the surface, and how much durability it shed.
    ///
    /// The FIELD-LOCAL half — sector duration, positions gained and lost, and how long a rival sat on
    /// the gearbox — needs the leaderboard, so <see cref="RaceManager"/> fills those in and merges the
    /// two at the sector line. Splitting it this way keeps the referee out of the collision-callback
    /// business and keeps this component from needing to know the running order.
    ///
    /// Every racer carries one; <see cref="RaceManager"/> guarantees it via <see cref="GetOrAdd"/>, the
    /// same pattern it already uses for <see cref="VehicleCombat"/> and <see cref="DraftSensor"/>, so no
    /// scene or prefab has to be re-authored.
    ///
    /// Deliberately has no <c>FixedUpdate</c> of its own: the referee calls <see cref="Sample"/> inside
    /// its existing per-car loop. That makes sampling and boundary-detection strictly ordered (a
    /// self-driven observer could sample after the referee had already closed the sector, silently
    /// losing a step to script execution order) and avoids eight extra engine callbacks per physics step.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public sealed class SectorObserver : MonoBehaviour
    {
        /// <summary>Slip angle (deg, worst wheel) beyond which the car counts as genuinely sideways rather than merely cornering hard.</summary>
        public const float SpinSlipAngleDeg = 30f;

        /// <summary>Pedal input below which a pedal counts as released — both released is coasting.</summary>
        public const float PedalDeadzone = 0.05f;

        /// <summary>Surface grip below which a wheel counts as off the racing surface. Just under 1 so unmarked tracks never trip it.</summary>
        public const float OffSurfaceGripMult = 0.95f;

        private VehicleController _controller;
        private VehicleCombat _combat;
        private DraftSensor _draft;

        // Car-local accumulators for the sector in progress. Cleared by Arm/TakeAndReset.
        private int _contactsAsAggressor;
        private int _contactsAsVictim;
        private float _draftSeconds;
        private float _coastSeconds;
        private float _spinSeconds;
        private float _offSurfaceSeconds;
        private float _durabilityLost;

        // Previous step's durability, so loss accrues incrementally. Comparing against a value captured
        // at the sector start would misread a mid-race RebuildSim (durability snaps back to 1) as a huge
        // negative loss; an incremental delta simply contributes nothing on that step.
        private float _lastDurability = 1f;

        // Contacts arrive on the collision callback whenever PhysX says so — including while the field is
        // still parked on the grid during the countdown. Until the referee arms us at the green flag, a
        // grid nudge must not be banked as this race's first incident.
        private bool _armed;

        /// <summary>Adds the component if the car doesn't already have one — safe to call repeatedly.</summary>
        public static SectorObserver GetOrAdd(GameObject go) =>
            go.TryGetComponent(out SectorObserver existing) ? existing : go.AddComponent<SectorObserver>();

        private void Awake()
        {
            _controller = GetComponent<VehicleController>();
            _draft = GetComponent<DraftSensor>();
        }

        private void OnEnable() => Hook();
        private void OnDisable() => Unhook();

        private void Hook()
        {
            if (_combat != null) return;
            _combat = VehicleCombat.GetOrAdd(gameObject);
            _combat.OnContact += OnContact;
        }

        private void Unhook()
        {
            if (_combat == null) return;
            _combat.OnContact -= OnContact;
            _combat = null;
        }

        /// <summary>
        /// Green-flag reset: clears every accumulator and begins recording. Called by the referee when the
        /// countdown ends, so contact during the grid formation is discarded rather than charged to the
        /// opening sector.
        /// </summary>
        public void Arm()
        {
            _armed = true;
            _lastDurability = _controller != null ? _controller.Durability : 1f;
            Clear();
        }

        /// <summary>
        /// Advance the car-local accumulators by one physics step. No-op until armed, so nothing is
        /// recorded before the green flag.
        /// </summary>
        public void Sample(float dt)
        {
            if (!_armed || !(dt > 0f) || _controller == null) return;

            if (_draft == null) _draft = GetComponent<DraftSensor>();
            if (_draft != null && _draft.IsDrafting) _draftSeconds += dt;

            VehicleInput input = _controller.Input;
            if (input.Throttle < PedalDeadzone && input.Brake < PedalDeadzone) _coastSeconds += dt;

            VehicleSim sim = _controller.Sim;
            if (sim != null)
            {
                float worstSlip = 0f;
                for (int i = 0; i < VehicleSim.WheelCount; i++)
                {
                    float slip = Mathf.Abs(sim.SlipAngleDeg[i]);
                    if (slip > worstSlip) worstSlip = slip;
                }
                if (worstSlip > SpinSlipAngleDeg) _spinSeconds += dt;

                // Incremental, and floored at zero so a sim rebuild (durability resets to 1) contributes
                // nothing rather than a spurious negative.
                float durability = sim.Durability;
                if (durability < _lastDurability) _durabilityLost += _lastDurability - durability;
                _lastDurability = durability;
            }

            if (_controller.SurfaceGripMult < OffSurfaceGripMult) _offSurfaceSeconds += dt;
        }

        /// <summary>
        /// Hands back everything accumulated since the last call and clears for the next sector. The
        /// field-local fields (<see cref="SectorEvidence.DurationS"/>, positions, pressure) are left at
        /// zero for the referee to fill in — this component cannot see the running order.
        /// </summary>
        public SectorEvidence TakeAndReset()
        {
            var evidence = new SectorEvidence
            {
                ContactsAsAggressor = _contactsAsAggressor,
                ContactsAsVictim = _contactsAsVictim,
                DraftSeconds = _draftSeconds,
                CoastSeconds = _coastSeconds,
                SpinSeconds = _spinSeconds,
                OffSurfaceSeconds = _offSurfaceSeconds,
                DurabilityLost = _durabilityLost,
            };
            Clear();
            return evidence;
        }

        private void Clear()
        {
            _contactsAsAggressor = 0;
            _contactsAsVictim = 0;
            _draftSeconds = 0f;
            _coastSeconds = 0f;
            _spinSeconds = 0f;
            _offSurfaceSeconds = 0f;
            _durabilityLost = 0f;
        }

        /// <summary>
        /// Buckets one attributed car-to-car hit by fault. The split is <see cref="VehicleCombat"/>'s
        /// symmetric aggressorness, so the two cars in a collision always bucket it as mirror images.
        ///
        /// Every contact lands in exactly one bucket — a dead-even mutual smack counts as a victim hit.
        /// That is the conservative reading: it costs the car its CLEAN sector (which any contact should)
        /// without crediting it with an AGGRESSIVE one it did not earn.
        /// </summary>
        private void OnContact(VehicleCombat.ContactReport report)
        {
            if (!_armed) return;
            if (report.Aggressorness01 > 0.5f) _contactsAsAggressor++;
            else _contactsAsVictim++;
        }
    }
}
