using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// Host adapter for BotBrain: gathers sensors from the rigidbody each physics step,
    /// runs the brain, and writes VehicleController.Input. Sits where VehicleInputProvider
    /// would on a human car — never put both on the same vehicle.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public class BotDriver : MonoBehaviour
    {
        private const float FlippedRecoverS = 3f;
        private const float ResetToTrackAfterS = 8f;
        private const float ProgressAnchorWindowM = 6f;
        private const float NeighborSenseRadiusM = 30f; // must cover the brain's follow/draft ranges

        // Rubber-band: keep the pack tense instead of stringing out. We compare our own track
        // distance to the field's mean; trailing the pack buys a small commitment boost, running
        // away eases us off. Tapered by gap here, then clamped subtle inside BotBrain.
        private const float RubberbandFullGapM = 45f; // gap (m) to the pack mean at which the nudge saturates
        private const float RubberbandSpan = 0.10f;   // max +/- fraction before BotBrain's own clamp

        [SerializeField] private TrackPath trackPath;
        [SerializeField] private BotSkill skill = BotSkill.Default;
        [Tooltip("Grip fraction an all-out (high-aggression) bot saps from a car it rams. Timid bots sap 40% of this. 0 disables bot attacks.")]
        [SerializeField] private float maxContactGripSap = 0.16f;

        private VehicleController _controller;
        private BotBrain _brain;
        private RaceManager _race; // cached at Start for rubber-band gap lookups; null = solo run, stays neutral
        private float _flippedTimer;
        private float _noProgressTimer;
        private float _lastProgress;

        // Opponent sensing: mirrors VehicleCombat's aura scan (Vehicle-layer OverlapSphere, resolve via
        // the attached rigidbody). Buffers are reused each step so FixedUpdate stays allocation-free.
        private int _vehicleMask;
        private readonly Collider[] _neighborHits = new Collider[16];
        private readonly BotNeighbor[] _neighborBuf = new BotNeighbor[15];

        /// <summary>Wires the bot up (used by editor builders — sets serialized fields only).</summary>
        public void Configure(TrackPath path, float cornerSpeedMult, float aggression, float lookaheadM, float lateralOffsetM = 0f)
        {
            trackPath = path;
            skill = new BotSkill
            {
                CornerSpeedMult = cornerSpeedMult,
                Aggression = aggression,
                LookaheadM = lookaheadM,
                LateralOffsetM = lateralOffsetM,
            };
            _brain = null;
        }

        private void Awake()
        {
            _controller = GetComponent<VehicleController>();
            _vehicleMask = 1 << gameObject.layer; // cars all share the Vehicle layer (same convention as VehicleCombat)
        }

        private void Start()
        {
            // Same-assembly lookup only (Race must not reference Meta). Prefer a manager on a parent
            // (nested race rigs), else the single scene manager. Null is fine — rubber-band stays neutral.
            _race = GetComponentInParent<RaceManager>();
            if (!_race) _race = FindFirstObjectByType<RaceManager>();
            ApplyAttackProfile();
        }

        /// <summary>
        /// Gives the bot a contact-only attack scaled by aggression so ramming is two-way:
        /// aggressive bots punish contact, timid ones only nibble. Uses the already-serialized
        /// skill, so it works in the saved scene without a rebuild. Proximity auras stay a player
        /// tool for now; bots keep the universal self-rattle from VehicleCombat regardless.
        /// </summary>
        private void ApplyAttackProfile()
        {
            if (maxContactGripSap <= 0f) return;
            float aggro01 = Mathf.Clamp01(Mathf.InverseLerp(0.7f, 1.15f, skill.Aggression));
            AttackProfile profile = AttackProfile.None;
            profile.ContactGripSap = Mathf.Lerp(maxContactGripSap * 0.4f, maxContactGripSap, aggro01);
            VehicleCombat.GetOrAdd(gameObject).SetProfile(profile);
        }

        private void OnDisable()
        {
            // Coast when switched off (e.g. by RaceManager after finishing) — a lingering
            // brake input would read as reverse once the car stops.
            if (_controller) _controller.Input = default;
        }

        private void FixedUpdate()
        {
            if (!trackPath || trackPath.Line == null) return;
            _brain ??= new BotBrain(trackPath.Line, skill);

            Vector3 velocity = _controller.Body ? _controller.Body.linearVelocity : Vector3.zero;
            UpdateFlipRecovery(velocity);
            UpdateResetToTrack();

            var sensors = new BotSensors
            {
                Position = transform.position,
                Forward = transform.forward,
                Velocity = velocity,
                DrivenWheelSlip = MaxDrivenWheelSlip(),
                Neighbors = _neighborBuf,
                NeighborCount = GatherNeighbors(transform.position),
            };

            _controller.Input = _brain.Step(Time.fixedDeltaTime, sensors, ComputeRubberband());
        }

        /// <summary>
        /// Turns our standing in the field into a commitment factor for BotBrain. Reference is the
        /// mean track distance of the cars still racing (the pack centre, player included): fall behind
        /// it and we push a little harder, run away from it and we ease off, tapered by the gap and
        /// scaled by the manager's global difficulty. Returns 1 (neutral) with no manager or too small
        /// a field; BotBrain re-clamps the result so it can never read as cheating.
        /// </summary>
        private float ComputeRubberband()
        {
            if (!_race) return 1f;
            var board = _race.Leaderboard;
            if (board == null || board.Count < 2) return 1f;

            float sum = 0f;
            int n = 0;
            float mine = 0f;
            bool found = false;
            for (int i = 0; i < board.Count; i++)
            {
                RaceCarStatus s = board[i];
                if (s == null || s.State != CarRaceState.Racing || !s.Car) continue;
                sum += s.TotalDistanceM;
                n++;
                if (s.Car == _controller)
                {
                    mine = s.TotalDistanceM;
                    found = true;
                }
            }
            if (!found || n < 2) return 1f;

            float gap = mine - sum / n; // + = ahead of the pack (ease off), - = behind it (boost)
            float t = Mathf.Clamp(gap / RubberbandFullGapM, -1f, 1f);
            return (1f - t * RubberbandSpan) * _race.DifficultyScalar;
        }

        /// <summary>
        /// Fills <see cref="_neighborBuf"/> with the rival cars within sensing range, in world-space
        /// relative position + velocity. Same scan the combat aura uses: Vehicle-layer OverlapSphere,
        /// resolve the VehicleController off the attached rigidbody, skip ourselves. Returns the count.
        /// </summary>
        private int GatherNeighbors(Vector3 pos)
        {
            int hits = Physics.OverlapSphereNonAlloc(pos, NeighborSenseRadiusM, _neighborHits,
                _vehicleMask, QueryTriggerInteraction.Ignore);
            int n = 0;
            for (int i = 0; i < hits && n < _neighborBuf.Length; i++)
            {
                Collider col = _neighborHits[i];
                if (!col) continue;
                Rigidbody rb = col.attachedRigidbody;
                if (!rb || rb.gameObject == gameObject) continue;        // skip self (all our colliders share our body)
                if (!rb.TryGetComponent(out VehicleController _)) continue; // race cars only

                _neighborBuf[n].RelativePosition = rb.position - pos;
                _neighborBuf[n].Velocity = rb.linearVelocity;
                n++;
            }
            return n;
        }

        private float MaxDrivenWheelSlip()
        {
            var sim = _controller.Sim;
            if (sim == null) return 0f;
            float max = 0f;
            for (int i = 0; i < VehicleSim.WheelCount; i++)
                if (sim.IsDriven(i))
                    max = Mathf.Max(max, Mathf.Abs(sim.SlipRatio[i]));
            return max;
        }

        /// <summary>
        /// Bots have no "press R to flip" — right them after a few seconds resting well off
        /// their wheels. Threshold is deliberately loose (leaning on a wall on two wheels
        /// counts): with wheels unloaded the brain's reverse-out recovery is powerless.
        /// </summary>
        private void UpdateFlipRecovery(Vector3 velocity)
        {
            bool flipped = transform.up.y < 0.6f && velocity.magnitude < 3f;
            _flippedTimer = flipped ? _flippedTimer + Time.fixedDeltaTime : 0f;
            if (_flippedTimer < FlippedRecoverS || !_controller.Body) return;

            _flippedTimer = 0f;
            Rigidbody body = _controller.Body;
            Vector3 flatFwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (flatFwd.sqrMagnitude < 0.01f) flatFwd = Vector3.forward;
            body.position += Vector3.up * 1.5f;
            body.rotation = Quaternion.LookRotation(flatFwd, Vector3.up);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        /// <summary>
        /// Last-resort watchdog: whatever the failure mode (wedged between crates, balanced
        /// on a wall lip, recovery loop that never escapes), a bot that makes no track
        /// progress for ResetToTrackAfterS gets teleported onto the centreline facing the
        /// racing direction. Guarantees no permanent stalls.
        /// </summary>
        private void UpdateResetToTrack()
        {
            float progress = trackPath.ProjectPosition(transform.position);
            // _lastProgress is an anchor: gaining ProgressAnchorWindowM of track distance
            // from it counts as progress and re-anchors; failing to for ResetToTrackAfterS
            // (slowest legit crawl still re-anchors well inside the window) triggers reset.
            if (Mathf.Abs(trackPath.Line.SignedDelta(_lastProgress, progress)) > ProgressAnchorWindowM)
            {
                _lastProgress = progress;
                _noProgressTimer = 0f;
                return;
            }

            _noProgressTimer += Time.fixedDeltaTime;
            if (_noProgressTimer < ResetToTrackAfterS || !_controller.Body) return;

            _noProgressTimer = 0f;
            _lastProgress = progress;
            Rigidbody body = _controller.Body;
            body.position = trackPath.Line.PointAt(progress) + Vector3.up * 1.2f;
            body.rotation = Quaternion.LookRotation(trackPath.Line.DirectionAt(progress), Vector3.up);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }
}
