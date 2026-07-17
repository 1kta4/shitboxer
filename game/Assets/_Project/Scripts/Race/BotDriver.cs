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
        [Tooltip("On-track archetype used as the FALLBACK when rival variety is off (see enableRivalVariety). Neutral (the default) leaves today's behaviour unchanged. When variety is on, the seeded assignment overrides this. Orthogonal to skill; never randomised — deterministic per bot.")]
        [SerializeField] private BotPersonalityKind personality = BotPersonalityKind.Neutral;
        [Tooltip("Master toggle for mild rival variety. OFF (or a field left all-Neutral) reverts to today's identical bots: each bot runs its serialized personality above at nominal difficulty — byte-for-byte the previous behaviour. ON fans the field across the four archetypes and a subtle skill band, seeded deterministically off each bot's index so it's repeatable and headless-server-safe. Bounded either way.")]
        [SerializeField] private bool enableRivalVariety = true;
        [Tooltip("Base seed for the deterministic variety assignment. Combined with each bot's sibling index, so changing it deterministically reshuffles which archetype/skill each bot draws (without touching any code). 0 = seed straight off the sibling index. Ignored when variety is off.")]
        [SerializeField] private int rivalVarietySeed = 0;
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

        /// <summary>
        /// Wires the bot up (used by editor builders — sets serialized fields only). The personality/racecraft
        /// knobs are optional: left null they are DERIVED from the driving stats, so the existing presets fan
        /// out into distinct characters (quick, aggressive bots attack and defend harder and rarely bobble;
        /// slow, timid ones cede the line and make more of the bounded mistakes) without any caller change.
        /// Pass an explicit 0..1 value to override a derived knob.
        /// </summary>
        public void Configure(TrackPath path, float cornerSpeedMult, float aggression, float lookaheadM,
            float lateralOffsetM = 0f, float? defensiveness = null, float? overtakeBoldness = null,
            float? mistakeRate = null, float? consistency = null)
        {
            trackPath = path;
            // 0..1 driving-skill proxy from the two stats the presets already vary — high = quick & committed.
            float skill01 = Mathf.Clamp01(0.5f * (Mathf.InverseLerp(0.78f, 1.05f, cornerSpeedMult)
                                                + Mathf.InverseLerp(0.75f, 1.15f, aggression)));
            float aggro01 = Mathf.Clamp01(Mathf.InverseLerp(0.75f, 1.15f, aggression));
            skill = new BotSkill
            {
                CornerSpeedMult = cornerSpeedMult,
                Aggression = aggression,
                LookaheadM = lookaheadM,
                LateralOffsetM = lateralOffsetM,
                Defensiveness = Mathf.Clamp01(defensiveness ?? aggro01),
                OvertakeBoldness = Mathf.Clamp01(overtakeBoldness ?? aggro01),
                MistakeRate = Mathf.Clamp01(mistakeRate ?? Mathf.Lerp(0.30f, 0.05f, skill01)),
                Consistency = Mathf.Clamp01(consistency ?? skill01),
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

        /// <summary>
        /// What this car can actually do, read off its live spec rather than assumed. Read once, at
        /// brain construction — which happens in FixedUpdate, after the run layer has bound and
        /// applied its per-race bot scaling, so a ramped car gets planned as a ramped car. Falls
        /// back to the historical hardcoded pair when there's no spec to read (a bare race scene),
        /// so that case drives exactly as before.
        /// </summary>
        private BotLimits ResolveLimits()
        {
            VehicleSpec spec = _controller && _controller.SpecAsset ? _controller.SpecAsset.Spec : null;
            if (spec == null) return BotLimits.Default;
            // Plan on the weaker axle: whichever end lets go first is what sets the corner.
            return BotLimits.FromGrip(Mathf.Min(spec.FrontTyre.PeakMu, spec.RearTyre.PeakMu));
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
            if (_brain == null)
            {
                _brain = new BotBrain(trackPath.Line, skill, ResolveLimits());
                // Seeded, bounded rival variety (opt-in via enableRivalVariety). OFF resolves to the serialized
                // `personality` at Nominal difficulty — byte-for-byte today's bots. ON fans a stable per-bot seed
                // (the base seed reshuffles the whole field; the sibling index gives each bot its own draw — both
                // deterministic, no Random) across the four archetypes and a subtle skill band. All the bounds and
                // the identity-when-off guarantee live in the pure resolver below, so this stays engine-loop-thin.
                int seed = rivalVarietySeed * SeedStride + transform.GetSiblingIndex();
                ResolveRivalConfig(enableRivalVariety, seed, personality,
                    out _, out BotPersonality botPersonality, out BotDifficulty botDifficulty);
                _brain.SetPersonality(botPersonality);
                _brain.SetDifficulty(botDifficulty);
            }

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

            // Rubber-band commitment factor AND the raw signed gap-to-field (+ ahead, - behind). The gap
            // feeds BotBrain's difficulty/skill-tier model; with the default (nominal) difficulty that model
            // is inert, so passing the real gap changes nothing until a host opts in via SetDifficulty.
            float rubberband = ComputeRubberband(out float signedGapM);
            _controller.Input = _brain.Step(Time.fixedDeltaTime, sensors, rubberband, signedGapM);
        }

        /// <summary>
        /// Turns our standing in the field into a commitment factor for BotBrain. Reference is the
        /// mean track distance of the cars still racing (the pack centre, player included): fall behind
        /// it and we push a little harder, run away from it and we ease off, tapered by the gap and
        /// scaled by the manager's global difficulty. Returns 1 (neutral) with no manager or too small
        /// a field; BotBrain re-clamps the result so it can never read as cheating.
        /// </summary>
        private float ComputeRubberband(out float signedGapM)
        {
            signedGapM = 0f; // neutral gap for every early-out below (solo run / not on the board yet)
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
            signedGapM = gap;
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

        // --- Rival variety: pure, deterministic seed -> (archetype, difficulty) assignment. -------------------
        // Kept static and scene-free (no Time / Input / transform in here) so it is unit-testable and a headless
        // server could reuse it verbatim; the MonoBehaviour above only supplies the seed. Two properties are
        // load-bearing: ON must stay a texture difference, never a difficulty spike (the skill band is narrow and
        // every produced config is re-clamped by BotDifficulty/BotPersonality); OFF must reproduce today's bots.

        // Reshuffles the whole field when rivalVarietySeed changes. Odd multiplier so it's a bijection on int;
        // int overflow wraps (unchecked) and stays deterministic.
        private const int SeedStride = unchecked((int)0x9E3779B1);

        // The archetypes the field is fanned across, indexed by the seed's low bits so consecutive sibling
        // indices cycle through all four (Neutral included, so a share of the grid still drives the reference
        // line — one more reason the activation stays subtle).
        private static readonly BotPersonalityKind[] RivalKinds =
        {
            BotPersonalityKind.Neutral,
            BotPersonalityKind.Blocker,
            BotPersonalityKind.Diver,
            BotPersonalityKind.Cruiser,
        };

        // Half-width of the rookie->pro base-skill band around nominal (0.5). Deliberately narrow: at the
        // extremes a rival's BotDifficulty sits only ~1.2% off identity speed/throttle — well inside
        // BotDifficulty's own clamps, so the "fastest" rival is a hair sharper, never a spike.
        private const float SkillBandHalf = 0.08f;

        // The mild base-skill tiers, symmetric about nominal. FromTier centres and clamps these, and 0.5 maps to
        // exactly Nominal, so the middle tier is identity.
        private static readonly float[] SkillTiers =
        {
            0.5f - SkillBandHalf,
            0.5f,
            0.5f + SkillBandHalf,
        };

        /// <summary>
        /// Deterministic per-bot archetype for the variety layer: the seed's low bits index
        /// <see cref="RivalKinds"/> (Neutral / Blocker / Diver / Cruiser), so consecutive seeds fan evenly
        /// across all four. Pure — same seed, same kind, on client or headless server.
        /// </summary>
        public static BotPersonalityKind RivalKind(int seed)
            => RivalKinds[(int)((uint)seed % (uint)RivalKinds.Length)];

        /// <summary>
        /// Deterministic per-bot base skill (rookie-&gt;pro, 0..1) for the variety layer, drawn from a MILD band
        /// around nominal via a hashed slice of the seed so skill doesn't correlate with the archetype (which
        /// keys off the raw low bits). Pure and bounded to <see cref="SkillTiers"/>.
        /// </summary>
        public static float RivalBaseSkill01(int seed)
            => SkillTiers[(int)((Hash((uint)seed) >> 8) % (uint)SkillTiers.Length)];

        /// <summary>
        /// Pure, deterministic, scene-free resolver from a stable per-bot <paramref name="seed"/> to the bounded
        /// (personality, difficulty) a bot runs. This is the single source of the variety layer's behaviour,
        /// factored out of the MonoBehaviour so it is unit-testable without a scene.
        ///
        /// <paramref name="enableVariety"/> false is the revert path: it yields <paramref name="fallbackKind"/>
        /// at <see cref="BotDifficulty.Nominal"/> and ignores the seed — with the serialized-default
        /// <see cref="BotPersonalityKind.Neutral"/> fallback that is exactly today's identical bots (Neutral
        /// personality + identity difficulty). true fans the field across the four archetypes and the mild skill
        /// band; every produced config is already clamped by <see cref="BotDifficulty"/> / <see cref="BotPersonality"/>,
        /// so nothing here can push a bot past the subtle bands.
        /// </summary>
        public static void ResolveRivalConfig(bool enableVariety, int seed, BotPersonalityKind fallbackKind,
            out BotPersonalityKind kind, out BotPersonality personality, out BotDifficulty difficulty)
        {
            if (!enableVariety)
            {
                // Revert path: the serialized personality (Neutral by default) at nominal difficulty is
                // byte-for-byte what BotDriver did before this layer existed.
                kind = fallbackKind;
                personality = BotPersonality.FromKind(fallbackKind);
                difficulty = BotDifficulty.Nominal;
                return;
            }

            kind = RivalKind(seed);
            personality = BotPersonality.FromKind(kind);
            difficulty = BotDifficulty.FromTier(RivalBaseSkill01(seed));
        }

        /// <summary>
        /// Small deterministic integer bit-avalanche (no <see cref="UnityEngine.Random"/>, no Time) so a given
        /// seed always maps to the same skill draw — repeatable across runs and identical on a headless server.
        /// </summary>
        private static uint Hash(uint x)
        {
            unchecked
            {
                x ^= 2747636419u;
                x *= 2654435769u;
                x ^= x >> 16;
                x *= 2654435769u;
                x ^= x >> 16;
                return x;
            }
        }
    }
}
