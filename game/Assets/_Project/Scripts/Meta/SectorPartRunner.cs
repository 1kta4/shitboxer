using Shitboxer.Race;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// Joins the race's sector events to the player's equipped parts — the seam doc 08's whole scoring
    /// design hangs on. Subscribes to <see cref="RaceManager.SectorCompleted"/>, ignores every car but
    /// the player's, resolves the equipped parts through <see cref="SectorPartState"/>, and pushes the
    /// results where they belong: money into the run, bonus multipliers onto the sim, durability into
    /// the sim's persistent wear.
    ///
    /// Lives in Meta because it is the only layer that can see BOTH sides — Race publishes the sector,
    /// Meta owns the parts, and Race must never reference Meta (Meta already depends on Race, so a
    /// back-reference would be circular). Everything numeric lives in the plain-C# state object, so this
    /// class is nothing but wiring.
    ///
    /// Wholly inert for a player with no sector-rule parts: the state resolves to zero money and
    /// multipliers of exactly 1, and the two sim fields are left at their defaults, so the shipped
    /// driving feel and the position-only inverted economy are byte-for-byte unchanged.
    /// </summary>
    public sealed class SectorPartRunner
    {
        private readonly SectorPartState _state = new SectorPartState();

        private RaceManager _race;
        private VehicleController _player;
        private RunState _run;
        private bool _hooked;

        /// <summary>The accumulator, for the HUD and for tests.</summary>
        public SectorPartState State => _state;

        /// <summary>Credits sector parts have paid so far this race.</summary>
        public int MoneyEarned => _state.MoneyEarned;

        /// <summary>The style of the player's most recently scored sector — HUD readout only.</summary>
        public SectorStyle LastStyle { get; private set; }

        /// <summary>Credits the player's most recently scored sector paid — HUD readout only.</summary>
        public int LastSectorMoney { get; private set; }

        /// <summary>
        /// Bind to a freshly-loaded race. Safe to call repeatedly — it unbinds any previous race first,
        /// so a re-bind can never leave two subscriptions double-scoring every sector.
        /// </summary>
        public void Bind(RaceManager race, VehicleController player, RunState run)
        {
            Unbind();
            _race = race;
            _player = player;
            _run = run;
            _state.Reset();
            LastStyle = SectorStyle.None;
            LastSectorMoney = 0;
            if (run != null) run.InRaceEarnings = 0;

            // Clear last race's bonuses off the sim. RunDirector rebuilds the sim per race so they
            // would normally already be 1, but a re-bind onto a live car must not inherit them.
            ApplyToSim(1f, 1f);

            if (_race == null) return;
            _race.SectorCompleted += OnSectorCompleted;
            _hooked = true;
        }

        /// <summary>Detach from the current race. Idempotent.</summary>
        public void Unbind()
        {
            if (_hooked && _race != null) _race.SectorCompleted -= OnSectorCompleted;
            _hooked = false;
            _race = null;
            _player = null;
            _run = null;
        }

        private void OnSectorCompleted(SectorCompletion completion)
        {
            // Every car in the field raises this; only the player's parts score.
            if (_player == null || completion.Car == null || completion.Car.Car != _player) return;

            bool finalSector = _race != null
                               && completion.Lap >= _race.TotalLaps
                               && completion.SectorIndex >= _race.SectorsPerLap - 1;

            SectorPartState.Totals totals = _state.Resolve(
                _run != null ? _run.EquippedParts : null,
                completion.SectorIndex,
                completion.Style,
                completion.Colour,
                completion.TimeS,
                completion.Evidence.ContactsAsVictim,
                completion.Evidence.PositionsGained,
                finalSector);

            LastStyle = completion.Style;
            LastSectorMoney = totals.Money;

            if (_run != null) _run.InRaceEarnings += totals.Money;

            VehicleSim sim = _player.Sim;
            if (sim == null) return;

            ApplyToSim(totals.GripMult, totals.PowerMult);

            // Durability is the sim's own persistent channel: ApplyDamage only ever lowers and rejects
            // out-of-range amounts, so a REPAIR (Panel Beater) has to go through SetDurability against
            // the current value. Both clamp internally.
            if (totals.DurabilityDelta > 0f) sim.SetDurability(sim.Durability + totals.DurabilityDelta);
            else if (totals.DurabilityDelta < 0f) sim.ApplyDamage(-totals.DurabilityDelta);
        }

        /// <summary>
        /// Re-push the earned multipliers onto the sim. The host calls this every frame while racing
        /// because <see cref="VehicleController"/>'s out-of-world watchdog can call
        /// <c>RebuildSim()</c> mid-race, which constructs a fresh <see cref="VehicleSim"/> with both
        /// bonus fields back at 1 — and a runner that only writes them when a sector closes would let a
        /// bonus earned in sector 2 silently vanish until sector 3. Two float writes; the values are
        /// already resolved, so this recomputes nothing.
        /// </summary>
        public void Reassert() => ApplyToSim(_state.GripMult, _state.PowerMult);

        private void ApplyToSim(float gripMult, float powerMult)
        {
            VehicleSim sim = _player != null ? _player.Sim : null;
            if (sim == null) return;
            sim.BonusGripMult = gripMult;
            sim.BonusPowerMult = powerMult;
        }
    }
}
