using Shitboxer.Race;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// Joins the live race to the player's equipped ACTIVE item (doc 08 decision 14) — the same seam
    /// shape as <see cref="SectorPartRunner"/>: Race publishes events, Meta owns the parts, and this
    /// is the only layer that sees both. Gathers the charge condition's signals from the world
    /// (draft sensor, attributed contacts, durability, sector lines), steps the pure
    /// <see cref="ActivePartState"/>, and holds the resulting boost on the sim every tick — which
    /// doubles as the re-assert against the out-of-world watchdog's mid-race sim rebuild.
    ///
    /// One active at a time: the FIRST equipped part with an active spec is THE active (single bind,
    /// single reservoir). Wholly inert for a loadout without one — the state reads BoostMult 1 and
    /// this never touches the sim, so the shipped driving feel is byte-for-byte unchanged (and the
    /// dormant designer-gated DraftBoost component is never fought: disabled, it writes nothing).
    /// </summary>
    public sealed class ActivePartRunner
    {
        /// <summary>Seconds without a car-to-car impact before CleanRunning counts as clean again.</summary>
        public const float CleanGraceS = 1.5f;

        /// <summary>DamageTaken charge scale: ChargePerEvent is "per 10% durability lost".</summary>
        public const float DamagePerEventUnit = 0.1f;

        private readonly ActivePartState _state = new ActivePartState();

        private RaceManager _race;
        private VehicleController _player;
        private VehicleCombat _combat;
        private DraftSensor _sensor;
        private RunState _run;
        private PartDef _part;
        private bool _hooked;

        private float _pendingEventCharge;   // chunk charge gathered between ticks (contacts, sectors)
        private float _lastDurability = 1f;

        /// <summary>The pure core, for tests and tuning readouts.</summary>
        public ActivePartState State => _state;

        /// <summary>The equipped active part, or null when the loadout has none.</summary>
        public PartDef Part => _part;

        /// <summary>
        /// Bind to a freshly-loaded race: pick the first equipped active part, arm the reservoir for a
        /// fresh race, and hook the sector line. Safe to call repeatedly — unbinds first, so a re-bind
        /// can never double-charge.
        /// </summary>
        public void Bind(RaceManager race, VehicleController player, RunState run)
        {
            Unbind();
            _race = race;
            _player = player;
            _run = run;
            _combat = player != null ? player.GetComponent<VehicleCombat>() : null;
            _sensor = player != null ? player.GetComponent<DraftSensor>() : null;

            _part = FirstActivePart(run);
            _state.Arm(_part != null ? _part.Active : null);
            _pendingEventCharge = 0f;
            _lastDurability = player != null && player.Sim != null ? player.Sim.Durability : 1f;

            if (!_state.Armed) return;
            if (_race != null)
            {
                _race.SectorCompleted += OnSectorCompleted;
                _hooked = true;
            }
            if (_combat != null) _combat.OnContact += OnContact;
        }

        /// <summary>Detach from the current race. Idempotent.</summary>
        public void Unbind()
        {
            if (_hooked && _race != null) _race.SectorCompleted -= OnSectorCompleted;
            if (_combat != null) _combat.OnContact -= OnContact;
            _hooked = false;
            _race = null;
            _player = null;
            _combat = null;
            _sensor = null;
            _run = null;
            _part = null;
        }

        /// <summary>
        /// Per-frame while racing: gather this step's condition signals, step the reservoir, pay any
        /// per-use cost, and hold the boost on the sim. <paramref name="activatePressed"/> is the
        /// single ACTIVATE bind, already read by the host (input stays out of this layer).
        /// </summary>
        public void Tick(float dt, bool activatePressed)
        {
            if (!_state.Armed || _player == null) return;

            VehicleSim sim = _player.Sim;
            if (sim == null) return;

            // DamageTaken: charge in proportion to durability lost since the last tick, in units of
            // "per 10% lost". Repairs (Panel Beater) raise durability and add nothing.
            ActiveCharge charge = _part.Active.Charge;
            if (charge == ActiveCharge.DamageTaken && sim.Durability < _lastDurability)
                _pendingEventCharge += (_lastDurability - sim.Durability) / DamagePerEventUnit
                                       * _part.Active.ChargePerEvent;
            _lastDurability = sim.Durability;

            var signals = new ActivePartState.Signals
            {
                Filling = FillingNow(charge),
                EventCharge = _pendingEventCharge,
            };
            _pendingEventCharge = 0f;

            int cost = _state.Tick(dt, signals, activatePressed, _run != null ? _run.Money : 0);
            if (cost > 0 && _run != null) _run.Money -= cost;

            // Hold the boost on the sim every tick: the write is the re-assert (a watchdog RebuildSim
            // resets BoostMult to 1 and would otherwise eat a live deploy).
            sim.BoostMult = _state.BoostMult;
        }

        /// <summary>The HUD readout, flattened (see <see cref="ActiveReadout"/>).</summary>
        public ActiveReadout Readout(string keyLabel) => !_state.Armed
            ? default
            : new ActiveReadout(
                hasActive: true,
                name: _part != null ? _part.DisplayName : "",
                charge01: _state.Charge01,
                deployed: _state.Deployed,
                ready: _state.ReadyToDeploy(_run != null ? _run.Money : 0),
                useCost: _state.UseCost,
                keyLabel: keyLabel);

        private bool FillingNow(ActiveCharge charge)
        {
            switch (charge)
            {
                case ActiveCharge.Drafting:
                    return _sensor != null && _sensor.IsDrafting;
                case ActiveCharge.CleanRunning:
                    // Clean = no car-to-car impact for a grace window. Realtime matches the combat
                    // layer's own stamps; the dev pause freezes racing anyway.
                    return _combat == null
                           || Time.realtimeSinceStartup - _combat.LastImpactRealtime > CleanGraceS;
                default:
                    return false; // Cooldown fills unconditionally inside the state; chunks don't fill
            }
        }

        private void OnSectorCompleted(SectorCompletion completion)
        {
            if (_part == null || _player == null || completion.Car == null || completion.Car.Car != _player) return;
            if (_part.Active.Charge == ActiveCharge.SectorLine)
                _pendingEventCharge += _part.Active.ChargePerEvent;
        }

        private void OnContact(VehicleCombat.ContactReport report)
        {
            // ContactDealt charges only hits that were mostly OUR doing — getting rammed is the
            // DamageTaken archetype's job, and paying both ways would blur the two identities.
            if (_part == null || _part.Active.Charge != ActiveCharge.ContactDealt) return;
            if (report.Aggressorness01 >= 0.5f)
                _pendingEventCharge += _part.Active.ChargePerEvent;
        }

        private static PartDef FirstActivePart(RunState run)
        {
            if (run == null || run.EquippedParts == null) return null;
            foreach (PartDef part in run.EquippedParts)
                if (part != null && part.IsActive)
                    return part;
            return null;
        }
    }
}
