using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// The engine-loop-independent core of one equipped active item (doc 08 decision 14): wraps the
    /// KERS reservoir (<see cref="DraftBoostModel"/> — the reference implementation the doc names)
    /// and adds what the generalisation needs — per-condition charge intake, the deploy gate
    /// (charge AND money), and the per-use cost. Plain C#: no Time.*, no Input.*, no scene access.
    /// The host feeds dt, the per-step condition signals and the pressed key; a headless server
    /// steps it identically.
    ///
    /// With no spec armed (<see cref="Armed"/> false) every query reads inert (BoostMult 1, charge 0)
    /// and <see cref="Tick"/> spends nothing — a loadout without an active item is a perfect no-op.
    /// </summary>
    public sealed class ActivePartState
    {
        private readonly DraftBoostModel _model = new DraftBoostModel();
        private ActiveSpec _spec;
        private int _useTax;

        /// <summary>True once a spec with a real charge condition is armed.</summary>
        public bool Armed => _spec != null && _spec.Charge != ActiveCharge.None;

        public float Charge01 => Armed ? _model.Charge01 : 0f;
        public bool Deployed => Armed && _model.Active;
        /// <summary>The multiplier the host should hold on the sim this step (1 = no boost).</summary>
        public float BoostMult => Armed ? _model.BoostMult : 1f;
        /// <summary>What one deploy actually costs: the part's authored fee plus any per-race tax
        /// (the ActiveTaxed boss). This is the number the HUD shows and the wallet is billed.</summary>
        public int UseCost => Armed ? _spec.UseCost + _useTax : 0;

        /// <summary>Charged enough and affordable — the moment the ACTIVATE key would actually bite.</summary>
        public bool ReadyToDeploy(int money) =>
            Armed && !_model.Active
            && _model.Charge01 >= MinCharge()
            && _model.Charge01 > 0f
            && money >= UseCost;

        /// <summary>
        /// Arm this state with a part's authored spec (or null to disarm) and reset for a fresh race.
        /// <paramref name="useTax"/> is the race's per-deploy surcharge (the ActiveTaxed boss) — 0 on
        /// every normal race. OncePerRace and PaidUse start FULL — one is its whole design, the other
        /// is gated by money alone. Every tunable is clamped here so hand-edited YAML can't smuggle
        /// in an unbounded boost; the model bounds BoostMult again to its absolute 1.5 ceiling.
        /// </summary>
        public void Arm(ActiveSpec spec, int useTax = 0)
        {
            _spec = spec != null && spec.Charge != ActiveCharge.None ? spec : null;
            _useTax = Mathf.Max(0, useTax);
            _model.Reset();
            if (!Armed) return;

            _model.ChargeFillPerSecond = FillsPerSecond() ? Mathf.Max(0f, _spec.FillPerSecond) : 0f;
            _model.BoostDrainPerSecond = Mathf.Max(0.05f, _spec.DrainPerSecond); // 0 would boost forever
            _model.IdleDrainPerSecond = 0f;
            _model.MaxBoostMult = Mathf.Clamp(_spec.BoostMult, 1f, DraftBoostModel.AbsoluteMaxBoostMult);
            _model.MinActivateCharge01 = MinCharge();

            if (_spec.Charge == ActiveCharge.OncePerRace || _spec.Charge == ActiveCharge.PaidUse)
                _model.AddCharge(1f);
        }

        /// <summary>The per-step condition signals the HOST gathers from its world (drafting sensor,
        /// combat events, sector lines). Chunk events arrive pre-scaled as raw reservoir amounts.</summary>
        public struct Signals
        {
            /// <summary>The per-second condition holds this step (Drafting / CleanRunning; Cooldown passes true always).</summary>
            public bool Filling;
            /// <summary>Total event charge this step: hits landed x ChargePerEvent, sectors x ChargePerEvent, durability lost scaled, ...</summary>
            public float EventCharge;
        }

        /// <summary>What one step decided: the money the deploy just consumed (0 almost always).</summary>
        public int Tick(float dt, in Signals signals, bool activatePressed, int money)
        {
            if (!Armed) return 0;

            _model.AddCharge(signals.EventCharge);

            // PaidUse is "always ready": the reservoir refills instantly between deploys, so the only
            // real gates are the key and the wallet.
            if (_spec.Charge == ActiveCharge.PaidUse && !_model.Active)
                _model.AddCharge(1f);

            // The deploy gate: the model checks charge; money is ours to check, and the cost (fee +
            // any boss tax) is spent exactly on the not-deployed -> deployed transition so holding
            // the key never double-pays.
            bool wasActive = _model.Active;
            bool activate = activatePressed && (!wasActive) && money >= UseCost;
            _model.Step(dt, FillingThisStep(signals), activate);

            bool deployStarted = !wasActive && _model.Active;
            return deployStarted ? UseCost : 0;
        }

        private float MinCharge() => Mathf.Clamp01(_spec.MinCharge01);

        private bool FillsPerSecond() =>
            _spec.Charge == ActiveCharge.Drafting
            || _spec.Charge == ActiveCharge.CleanRunning
            || _spec.Charge == ActiveCharge.Cooldown;

        private bool FillingThisStep(in Signals signals) =>
            _spec.Charge == ActiveCharge.Cooldown || (FillsPerSecond() && signals.Filling);
    }
}
