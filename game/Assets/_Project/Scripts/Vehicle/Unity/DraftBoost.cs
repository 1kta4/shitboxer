using UnityEngine;

namespace Shitboxer.Vehicle
{
    /// <summary>
    /// The engine-loop-independent charge/boost reservoir behind the KERS-style overtake boost. Plain
    /// C# — no Time.*, Input.* or scene access; the host passes dt and the per-step drafting/deploy
    /// signals, so a headless server steps it identically to the client. (Uses only Mathf.)
    ///
    /// Sustained drafting fills a bounded charge in [0,1]; deploying it (once the reservoir is above
    /// <see cref="MinActivateCharge01"/>) drains the charge and drives <see cref="BoostMult"/> up to a
    /// bounded <see cref="MaxBoostMult"/> until the reservoir runs dry, whereupon it releases back to 1.
    ///
    /// This is the numbers-only core the <see cref="DraftBoost"/> host component wraps; it is inert
    /// (BoostMult == 1) until the host both enables the feature and feeds it drafting/deploy signals, so
    /// leaving it un-stepped is a perfect no-op and today's driving feel is unchanged.
    /// </summary>
    public sealed class DraftBoostModel
    {
        /// <summary>Hard ceiling the deployed <see cref="BoostMult"/> can never exceed, whatever the tunables ask for.</summary>
        public const float AbsoluteMaxBoostMult = 1.5f;

        // --- Tunables (rates per second; defaults mirror DraftBoost's serialized defaults). ---
        [Tooltip("Reservoir filled per second while sitting in a draft (1 = full). ~0.35 => ~3 s to a full charge.")]
        public float ChargeFillPerSecond = 0.35f;
        [Tooltip("Reservoir drained per second while the boost is deployed. ~0.5 => ~2 s of boost from full.")]
        public float BoostDrainPerSecond = 0.5f;
        [Tooltip("Reservoir slowly bled per second while neither drafting nor boosting. 0 = holds charge.")]
        public float IdleDrainPerSecond = 0f;
        [Tooltip("Peak drive-torque multiplier at full boost. Clamped into [1, AbsoluteMaxBoostMult].")]
        public float MaxBoostMult = 1.15f;
        [Tooltip("Minimum reservoir needed before the boost can be deployed.")]
        public float MinActivateCharge01 = 0.25f;

        // --- State ---
        /// <summary>Reservoir level, 0 = empty, 1 = full. Filled by drafting, spent by boosting.</summary>
        public float Charge01 { get; private set; }
        /// <summary>True while a boost is currently deployed and draining the reservoir.</summary>
        public bool Active { get; private set; }
        /// <summary>Drive-torque multiplier to hand the sim this step: <see cref="MaxBoostMult"/> while active, else 1.</summary>
        public float BoostMult { get; private set; } = 1f;

        /// <summary>Race-start reset: empty reservoir, no boost, nominal multiplier.</summary>
        public void Reset()
        {
            Charge01 = 0f;
            Active = false;
            BoostMult = 1f;
        }

        /// <summary>
        /// Event-driven charge: add a bounded chunk to the reservoir, for conditions that pay in
        /// discrete moments (a landed hit, a sector line, a once-per-race pre-charge) rather than per
        /// second (doc 08 decision 14 — active items each declare their own charge condition). No-op
        /// for non-positive or non-finite amounts; clamped so pulses can never overfill.
        /// </summary>
        public void AddCharge(float amount)
        {
            if (!(amount > 0f)) return; // rejects zero, negatives and NaN
            Charge01 = Mathf.Clamp01(Charge01 + amount);
        }

        /// <summary>
        /// Advance the reservoir by <paramref name="dt"/> seconds. <paramref name="drafting"/> is whether the
        /// car is sitting in another's tow this step (fills the reservoir), <paramref name="activate"/> a
        /// momentary deploy request. Returns (and stores) the <see cref="BoostMult"/> the host should fold
        /// into the sim: while a boost is live the reservoir drains and the multiplier holds at the bounded
        /// <see cref="MaxBoostMult"/>; it releases to 1 the moment the reservoir empties. A non-positive or
        /// non-finite dt is a no-op that leaves the state untouched.
        /// </summary>
        public float Step(float dt, bool drafting, bool activate)
        {
            if (!(dt > 0f)) return BoostMult; // rejects zero, negatives and NaN — nothing integrates

            if (Active)
            {
                // Spend the reservoir; release the boost the instant it runs dry.
                Charge01 = Mathf.Clamp01(Charge01 - Mathf.Max(0f, BoostDrainPerSecond) * dt);
                if (Charge01 <= 0f) Active = false;
            }
            else if (activate && Charge01 > 0f && Charge01 >= Mathf.Clamp01(MinActivateCharge01))
            {
                // Deploy: begin draining this very step so a one-frame request still bites.
                Active = true;
                Charge01 = Mathf.Clamp01(Charge01 - Mathf.Max(0f, BoostDrainPerSecond) * dt);
                if (Charge01 <= 0f) Active = false;
            }
            else if (drafting)
            {
                Charge01 = Mathf.Clamp01(Charge01 + Mathf.Max(0f, ChargeFillPerSecond) * dt);
            }
            else
            {
                Charge01 = Mathf.Clamp01(Charge01 - Mathf.Max(0f, IdleDrainPerSecond) * dt);
            }

            BoostMult = Active ? Mathf.Clamp(MaxBoostMult, 1f, AbsoluteMaxBoostMult) : 1f;
            return BoostMult;
        }
    }

    /// <summary>
    /// Host-side overtake boost: each FixedUpdate it reads this car's <see cref="DraftSensor.IsDrafting"/>,
    /// integrates a bounded charge (the <see cref="DraftBoostModel"/>), and — once armed and deployed —
    /// folds the resulting multiplier into its <see cref="VehicleSim.BoostMult"/> for a short, bounded
    /// power burst before releasing it. Rewards sustained close racing with an NFS/KERS "overtake button"
    /// feel. Time/Input live HERE (the host layer); the charge maths lives in the headless-testable model.
    ///
    /// GATED OFF by default: <see cref="Enabled"/> is false, so the component never touches the sim's
    /// <see cref="VehicleSim.BoostMult"/> (it stays 1) and the car's driving feel is byte-for-byte unchanged
    /// until a designer arms it. A disabled component is completely inert — it does not even read the sensor.
    /// </summary>
    public sealed class DraftBoost : MonoBehaviour
    {
        [Tooltip("Master gate. OFF (default) => this never touches the sim's BoostMult, so driving feel is " +
                 "byte-for-byte unchanged. Flip on to arm the KERS-style overtake boost. (Distinct from the " +
                 "MonoBehaviour's own 'enabled' checkbox.)")]
        public bool Enabled = false;

        [Tooltip("Auto-deploy the boost the instant the reservoir tops out while drafting, instead of waiting " +
                 "for a manual ActivateRequested trigger.")]
        public bool AutoActivate = false;

        [Header("Charge model (live only while Enabled) — defaults mirror DraftBoostModel")]
        [Tooltip("Reservoir filled per second while sitting in a draft (1 = full). ~0.35 => ~3 s to a full charge.")]
        [SerializeField] private float chargeFillPerSecond = 0.35f;
        [Tooltip("Reservoir drained per second while the boost is deployed. ~0.5 => ~2 s of boost from full.")]
        [SerializeField] private float boostDrainPerSecond = 0.5f;
        [Tooltip("Reservoir slowly bled per second while neither drafting nor boosting. 0 = holds charge.")]
        [SerializeField] private float idleDrainPerSecond = 0f;
        [Tooltip("Peak drive-torque multiplier at full boost. Bounded to [1, 1.5]. ~1.15 = a strong but fair shove.")]
        [Range(1f, DraftBoostModel.AbsoluteMaxBoostMult)]
        [SerializeField] private float maxBoostMult = 1.15f;
        [Tooltip("Minimum reservoir needed before the boost can be deployed.")]
        [Range(0f, 1f)]
        [SerializeField] private float minActivateCharge01 = 0.25f;

        /// <summary>Set true for one FixedUpdate by an input layer to deploy the boost (a momentary overtake button).</summary>
        [System.NonSerialized] public bool ActivateRequested;

        // Runtime charge model (a field initializer so it exists without Awake); the serialized tunables
        // above are pushed into it each step. Not serialized — its state (charge/boost) is race-transient.
        private readonly DraftBoostModel _model = new DraftBoostModel();
        private VehicleController _controller;
        private DraftSensor _sensor;

        // True once we've written a boost value this session; lets a mid-boost runtime-disable release the
        // multiplier exactly once. In the shipped/default state (never enabled) it stays false, so the
        // disabled branch below never touches BoostMult — a true no-op.
        private bool _wroteBoost;

        // --- HUD / telemetry read-outs (safe to poll every frame) ---
        /// <summary>Reservoir level in [0,1] for a charge meter.</summary>
        public float Charge01 => _model.Charge01;
        /// <summary>True while a boost is currently deployed.</summary>
        public bool Active => _model.Active;
        /// <summary>The drive-torque multiplier currently handed to the sim (1 = no boost).</summary>
        public float BoostMult => _model.BoostMult;
        /// <summary>The underlying charge model — exposed for tuning and headless tests.</summary>
        public DraftBoostModel Model => _model;

        private void Awake()
        {
            _controller = GetComponent<VehicleController>();
            _sensor = GetComponent<DraftSensor>();
        }

        private void FixedUpdate()
        {
            if (_controller == null) _controller = GetComponent<VehicleController>();
            VehicleSim sim = _controller ? _controller.Sim : null;

            if (!Enabled)
            {
                // Feature off. In the default state (_wroteBoost == false) this returns having touched
                // nothing. Only if a designer disables the feature mid-boost do we release the residual
                // multiplier once, so a stale boost can never linger.
                if (_wroteBoost && sim != null)
                {
                    sim.BoostMult = 1f;
                    _model.Reset();
                    _wroteBoost = false;
                }
                return;
            }

            if (sim == null) return;

            if (_sensor == null) _sensor = GetComponent<DraftSensor>();
            bool drafting = _sensor && _sensor.IsDrafting;

            // The overtake button plumbs in through this car's VehicleInput.Boost — a momentary deploy
            // request, exactly like an external ActivateRequested poke. Reading Input here is fine (this is
            // the host layer); ResolveActivate keeps the whole decision in one pure, headless-testable spot.
            bool boostInput = _controller != null && _controller.Input.Boost;
            bool activate = ResolveActivate(Enabled, ActivateRequested, boostInput, AutoActivate, drafting, _model.Charge01);
            ActivateRequested = false; // momentary: consumed this step

            SyncTunables();
            Tick(sim, Time.fixedDeltaTime, drafting, activate);
        }

        // Push the serialized tunables into the runtime model each step so inspector edits take effect live.
        private void SyncTunables()
        {
            _model.ChargeFillPerSecond = chargeFillPerSecond;
            _model.BoostDrainPerSecond = boostDrainPerSecond;
            _model.IdleDrainPerSecond = idleDrainPerSecond;
            _model.MaxBoostMult = maxBoostMult;
            _model.MinActivateCharge01 = minActivateCharge01;
        }

        /// <summary>
        /// Pure, host-decoupled deploy-signal resolver (the unit-test seam for the boost wiring): folds the
        /// momentary <paramref name="activateRequested"/> flag, the per-step overtake button
        /// <paramref name="boostInput"/> (this car's <see cref="VehicleInput.Boost"/>), and the auto-deploy
        /// rule (reservoir topped out while drafting) into the single deploy signal handed to the model.
        /// Returns false whenever <paramref name="enabled"/> is false, so a disabled DraftBoost can never
        /// ask the model to deploy and thus never touches <see cref="VehicleSim.BoostMult"/>.
        /// </summary>
        public static bool ResolveActivate(bool enabled, bool activateRequested, bool boostInput,
            bool autoActivate, bool drafting, float charge01)
        {
            if (!enabled) return false;
            return activateRequested || boostInput || (autoActivate && drafting && charge01 >= 1f);
        }

        /// <summary>
        /// Host-independent per-step seam (also the unit-test entry): steps the charge model and, ONLY while
        /// <see cref="Enabled"/>, folds the resulting multiplier onto <paramref name="sim"/>'s
        /// <see cref="VehicleSim.BoostMult"/>. Disabled => returns without touching the sim, so BoostMult
        /// stays 1 and the drivetrain force is byte-for-byte the un-boosted baseline. Returns the multiplier
        /// in effect (the sim's current value while disabled).
        /// </summary>
        public float Tick(VehicleSim sim, float dt, bool drafting, bool activate)
        {
            if (!Enabled) return sim != null ? sim.BoostMult : 1f; // disabled: never touch BoostMult
            float mult = _model.Step(dt, drafting, activate);
            if (sim != null) sim.BoostMult = mult;
            _wroteBoost = true;
            return mult;
        }
    }
}
