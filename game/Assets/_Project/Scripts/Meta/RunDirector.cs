using System.Collections.Generic;
using Shitboxer.Race;
using Shitboxer.Vehicle;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shitboxer.Meta
{
    /// <summary>What the run loop is currently doing.</summary>
    public enum RunPhase
    {
        Racing,
        Garage,
        RunOver,
        RunComplete,
    }

    /// <summary>
    /// Drives the roguelike loop around the race scene: race → payout → garage → reload the
    /// race scene → next race. DontDestroyOnLoad singleton (the RunRig saved in the scene
    /// self-destructs on reload when a run is already alive). After every scene load it
    /// re-finds the RaceManager, finds the player car by its VehicleInputProvider, and bakes
    /// the equipped stat parts into a runtime copy of the player's spec.
    /// Pause approach: the race scene stays loaded underneath the garage — between races we
    /// just set Time.timeScale = 0; NEXT RACE restores it and reloads the scene for a clean
    /// grid. Boss rule: the circuit's final race requires a top-BossTopN finish; failing it
    /// (or any elimination) costs a life and the same race is retried. Winning a boss race
    /// promotes the run to the next circuit (fresh race ladder); the run is only complete once
    /// the FINAL circuit's boss falls.
    /// </summary>
    public class RunDirector : MonoBehaviour
    {
        public static RunDirector Instance { get; private set; }

        [SerializeField] private PartPool partPool;
        [SerializeField] private PayoutTable payoutTable = new PayoutTable();
        [Tooltip("Cash a fresh run starts with.")]
        [SerializeField] private int startingMoney = 5;
        [Tooltip("Cash to fully repair a car worn all the way to the durability floor; lighter wear costs proportionally less. A money sink tensioning the inverted catch-up economy.")]
        [SerializeField] private int fullRepairCost = 12;

        [Tooltip("Damage-curve exponent for the repair price (see RunState.RepairCostFor). 1 (default) prices repairs LINEARLY in wear — byte-for-byte today's cost; >1 makes deep damage disproportionately dear; <1 front-loads light damage. Endpoints are unchanged either way.")]
        [SerializeField] private float repairDamageExponent = 1f;

        [Header("Boss races (opt-in — default OFF reproduces today's sequence exactly)")]
        [Tooltip("When ON, each circuit's final race runs under RaceRuleset.Boss and a clean boss finish pays the boss bonus (and any DoublePayout) and honours NoRepairAfter. OFF (default) makes NO SetRuleset call and no boss reward — the race sequence, rulesets, rewards and repairs are byte-for-byte as shipped.")]
        [SerializeField] private bool bossRacesEnabled = false;

        [Tooltip("Flat cash added to a clean boss-race finish when boss races are enabled. Only ever applied on a designated boss race, so its value never affects a run with boss races OFF.")]
        [SerializeField] private int bossRewardBonus = 8;

        [Header("Draft-Leech payoff (opt-in — pays only if the player OWNS a DraftLeech part)")]
        [Tooltip("$ paid per second the player spent drafting during a race, then rounded — but ONLY when the player owns a part flagged DraftLeech. A player owning no such part earns nothing here and the base economy is byte-for-byte unchanged.")]
        [SerializeField] private float draftLeechRate = 0.5f;
        [Tooltip("Cap on the Draft-Leech payoff granted in a single race. <= 0 means uncapped. Only ever applied when a DraftLeech part is owned, so its value never affects a run without one.")]
        [SerializeField] private int draftLeechCapPerRace = 10;

        [Header("Per-circuit difficulty ramp (opt-in — default 0 reproduces today's difficulty exactly)")]
        [Tooltip("Extra bot-commitment scalar added per circuit index, ON TOP of the shipped per-run difficulty. 0 (default) = OFF: the scalar handed to RaceManager.SetDifficultyScalar is byte-for-byte today's, and a run in progress is unchanged. >0 makes each later circuit lift the whole bot field a little more (base + ramp*CircuitIndex), clamped to difficultyRampMaxScalar and then re-clamped by SetDifficultyScalar to its own authored range. The license stake is already folded into the base via RunState.DifficultyMult, so the ramp never double-applies it.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float difficultyRampPerCircuit = 0f;

        [Tooltip("Ceiling the ramped difficulty scalar is clamped to WHEN THE RAMP IS ON (difficultyRampPerCircuit > 0). Defaults to RaceManager.SetDifficultyScalar's authored max (1.5). Ignored while the ramp is 0, where the shipped ceiling applies so the value stays byte-for-byte today.")]
        [Range(0.5f, 1.5f)]
        [SerializeField] private float difficultyRampMaxScalar = 1.5f;

        /// <summary>
        /// Opt-in boss-race master switch (default OFF). With it off, RunDirector makes no SetRuleset call
        /// and applies no boss reward, so the run plays and pays exactly as shipped. With it on, each
        /// circuit's boss (its final race) runs under <see cref="RaceRuleset.Boss"/> (see
        /// <see cref="ApplyRuleset"/>) and a clean boss finish earns <see cref="bossRewardBonus"/>.
        /// </summary>
        public bool BossRacesEnabled => bossRacesEnabled;

        /// <summary>Live state of the current run.</summary>
        public RunState Run { get; private set; } = new RunState();

        /// <summary>Shop rules instance; reroll cost resets each garage visit.</summary>
        public ShopLogic Shop { get; } = new ShopLogic();

        /// <summary>
        /// Persistent cross-run profile (lifetime stats + license-stake unlocks). Loaded once on Awake
        /// and updated/saved on every run end; survives run death/victory, unlike the per-run RunSave.
        /// Never null after Awake — a corrupt/absent profile loads as a fresh default.
        /// </summary>
        public MetaProgress Meta { get; private set; } = new MetaProgress();

        public RunPhase Phase { get; private set; } = RunPhase.Racing;

        /// <summary>One-line player verdict of the last resolved race, for the garage header.</summary>
        public string LastRaceSummary { get; private set; } = "";

        private RaceManager _raceManager;
        private VehicleController _playerCar;
        private GarageScreen _garage;
        private bool _raceResolved;

        /// <summary>Editor wiring (MetaAssetsBuilder) — sets serialized fields only.</summary>
        public void Configure(PartPool pool) => partPool = pool;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Load the persistent cross-run profile once (lifetime stats + stake unlocks). It outlives
            // any single run, so run-end bookkeeping and a future stake-select UI both read/write it.
            Meta = MetaProgress.Load();

            // Resume an interrupted run if a save exists (parts resolved by Id via the pool);
            // otherwise begin fresh with the starting cash and a freshly-rolled deterministic seed.
            if (partPool != null && RunSave.TryLoad(partPool, out RunState resumed))
            {
                Run = resumed;
            }
            else
            {
                Run.Money = startingMoney;
                Run.Seed = RollSeed();
            }

            _garage = GetComponent<GarageScreen>();
            if (!_garage) _garage = gameObject.AddComponent<GarageScreen>();
            _garage.Configure(this);
        }

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            // sceneLoaded may not fire for the scene already open when play begins, so bind lazily.
            if (Instance == this && _raceManager == null) BindToScene();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => BindToScene();

        /// <summary>Finds the race actors in the freshly loaded scene and preps the player car.</summary>
        private void BindToScene()
        {
            if (Instance != this) return; // a doomed duplicate — leave the live run alone

            _raceResolved = false;
            _raceManager = FindFirstObjectByType<RaceManager>();
            var inputProvider = FindFirstObjectByType<VehicleInputProvider>();
            _playerCar = inputProvider ? inputProvider.GetComponent<VehicleController>() : null;

            if (_raceManager == null || _playerCar == null)
            {
                Debug.LogError("[RunDirector] Scene needs a RaceManager and a player car with a VehicleInputProvider.", this);
                return;
            }

            ApplyEquippedParts();
            ApplyAttackProfile();
            ApplyRuleset();     // opt-in boss ruleset BEFORE ApplyDifficulty so the per-circuit tighten reads its base
            ApplyDifficulty();

            // Persistent wear carries ACROSS races within a run: a freshly-rebuilt sim resets to full
            // durability (and ApplyEquippedParts may have just rebuilt it via SetSpec), so re-apply the
            // run's carried value here — after the other Apply* calls — so a battered car stays battered
            // until the player pays to repair it in the garage.
            if (_playerCar.Sim != null)
                _playerCar.Sim.SetDurability(Run.CarDurability);

            // Draft-Leech payoff (opt-in): ensure the draft-time accumulator on the player car and zero it
            // for this race. Purely additive telemetry that applies no forces and never touches the sim;
            // RunDirector reads its total at race end ONLY for a player who owns a DraftLeech part, so a car
            // without one is entirely unaffected.
            DraftReward.GetOrAdd(_playerCar.gameObject).Reset();
        }

        /// <summary>
        /// Bakes equipped stat parts into a deep copy of the player's spec, wraps it in a
        /// throwaway runtime VehicleSpecAsset, and swaps it onto the controller. Skipped when
        /// no stat parts are equipped so the authored asset keeps driving the car.
        /// </summary>
        private void ApplyEquippedParts()
        {
            bool anyStatPart = false;
            foreach (PartDef part in Run.EquippedParts)
            {
                if (part && part.Category == PartCategory.Stat)
                {
                    anyStatPart = true;
                    break;
                }
            }
            if (!anyStatPart) return;

            VehicleSpec modified = SpecModApplier.Apply(_playerCar.SpecAsset.Spec, Run.EquippedParts);
            var asset = ScriptableObject.CreateInstance<VehicleSpecAsset>();
            asset.name = "PlayerSpec_Runtime";
            asset.Spec = modified;
            _playerCar.SetSpec(asset);
        }

        /// <summary>
        /// Builds the player's AttackProfile from equipped parts (see AttackLoadout) and hands it
        /// to their car's VehicleCombat, adding the component if absent. Always runs — with no
        /// attack parts the player simply gets the inert profile.
        /// </summary>
        private void ApplyAttackProfile()
        {
            AttackProfile profile = AttackLoadout.Build(Run.EquippedParts);
            VehicleCombat.GetOrAdd(_playerCar.gameObject).SetProfile(profile);
        }

        // Season-ramp tuning. Deliberately gentle and bounded so circuit 1 plays exactly as
        // shipped and later circuits get tense, not impossible.
        private const float DifficultyScalarGain = 0.4f;   // fraction of DifficultyMult's excess folded into bot commitment
        private const float MinDifficultyScalar = 1f;      // neutral floor: the request never dips below shipped-neutral
        private const float MaxDifficultyScalar = 1.3f;    // ceiling of the bot-commitment band the director requests with the ramp OFF
        private const float CutoffTightenPerCircuit = 0.02f; // survival window shaved off per later circuit
        private const float MinCutoffFraction = 0.08f;     // floor so the cutoff never becomes brutal

        /// <summary>
        /// Ramps the freshly-bound race to the current circuit: lifts the whole bot field by mapping
        /// RunState.DifficultyMult into a narrow band above neutral, and tightens the survival cutoff
        /// a little each circuit off the scene's authored base. Both stay subtle and bounded — the
        /// RaceManager clamps whatever we ask — and the referee's lap/leaderboard logic is untouched.
        /// </summary>
        private void ApplyDifficulty()
        {
            // DifficultyMult is 1.0 on circuit 1 and climbs (1.0, 1.35, 1.70, ...). Fold only a
            // fraction of the excess into a band above neutral so bots commit harder without ever
            // reading as cheating; the per-bot rubber-band clamps the final result regardless. This
            // base already carries the license stake (RunState.DifficultyMult multiplies in StakeMult),
            // so the ramp below must NOT re-apply the stake — it only adds a per-circuit term.
            float baseScalar = 1f + (Run.DifficultyMult - 1f) * DifficultyScalarGain;

            // Per-circuit difficulty ramp (opt-in). With difficultyRampPerCircuit == 0 (the default)
            // RampedDifficulty adds nothing and clamps into today's [MinDifficultyScalar, MaxDifficultyScalar]
            // band, so the value handed to SetDifficultyScalar is byte-for-byte the shipped one — a run in
            // progress is unchanged. With it > 0 each later circuit lifts the field a little more, clamped
            // into [MinDifficultyScalar, difficultyRampMaxScalar]; SetDifficultyScalar re-clamps to its own
            // authored [0.5, 1.5] range regardless, so the request can never leave that range.
            float ceiling = difficultyRampPerCircuit > 0f ? difficultyRampMaxScalar : MaxDifficultyScalar;
            float scalar = RampedDifficulty(baseScalar, Run.CircuitIndex, difficultyRampPerCircuit, MinDifficultyScalar, ceiling);
            _raceManager.SetDifficultyScalar(scalar);

            // Tighten the survival window per circuit off the authored base (a fresh RaceManager
            // resets cutoffFraction each scene reload, so this reads the shipped value every time),
            // clamped to a floor so the gate stays survivable on the hardest circuits.
            float cutoff = _raceManager.CutoffFraction - CutoffTightenPerCircuit * Run.CircuitIndex;
            _raceManager.SetCutoffFraction(Mathf.Max(MinCutoffFraction, cutoff));
        }

        /// <summary>
        /// The per-circuit-ramped bot-commitment scalar: <paramref name="baseScalar"/> plus
        /// <paramref name="rampPerCircuit"/> for each circuit already reached, clamped into
        /// [<paramref name="min"/>, <paramref name="max"/>]. Pure/static so the ramp is unit-testable
        /// without a live scene. With <paramref name="rampPerCircuit"/> &lt;= 0 (the shipped default) it
        /// adds NOTHING and reduces to <c>Mathf.Clamp(baseScalar, min, max)</c> for EVERY circuit — the
        /// difficulty stays exactly today's flat-per-run value. With it &gt; 0 the result is strictly
        /// increasing in <paramref name="circuitIndex"/> until it saturates at <paramref name="max"/>, and
        /// never leaves the [min, max] band. A negative circuit index contributes no ramp. The MonoBehaviour
        /// still passes the value through <see cref="RaceManager.SetDifficultyScalar"/>, which re-clamps to
        /// its own authored range, so the returned value can never push the race out of range.
        /// </summary>
        public static float RampedDifficulty(float baseScalar, int circuitIndex, float rampPerCircuit, float min, float max)
        {
            float added = rampPerCircuit > 0f ? rampPerCircuit * Mathf.Max(0, circuitIndex) : 0f;
            return Mathf.Clamp(baseScalar + added, min, max);
        }

        /// <summary>
        /// Opt-in boss wiring: when <see cref="BossRacesEnabled"/> is on, pushes <see cref="RaceRuleset.Boss"/>
        /// onto the freshly-bound race iff this is the circuit's boss (its final race), else the neutral
        /// <see cref="RaceRuleset.Standard"/>. Runs BEFORE <see cref="ApplyDifficulty"/> so the per-circuit
        /// cutoff tighten layers on top of the ruleset's base laps/cutoff. With boss races OFF (the default)
        /// it makes NO SetRuleset call at all, leaving the RaceManager on its authored Standard defaults —
        /// byte-for-byte the behaviour before this wave.
        ///
        /// NOTE: <see cref="RaceModifier.DamageAmplified"/> (carried by the Boss template) is consumed
        /// RACE-side later — RaceManager/VehicleCombat will scale contact damage from it during the race.
        /// This method only selects and applies the ruleset flag; the combat scaling is out of scope here.
        /// </summary>
        private void ApplyRuleset()
        {
            if (!bossRacesEnabled) return; // shipped path: no ruleset call — the race stays on its Standard defaults
            _raceManager.SetRuleset(RulesetForRace(bossRacesEnabled, Run.IsBossRace));
        }

        /// <summary>
        /// True when boss races are enabled AND the given race is the circuit's boss (its final race). Pure
        /// and static so the designation is unit-testable without a live scene. Defaults false — with boss
        /// races off, no race is ever designated a boss and the run plays exactly as shipped.
        /// </summary>
        public static bool IsDesignatedBoss(bool bossRacesEnabled, bool runIsBossRace) =>
            bossRacesEnabled && runIsBossRace;

        /// <summary>
        /// The ruleset to apply to a race: <see cref="RaceRuleset.Boss"/> for a designated boss (see
        /// <see cref="IsDesignatedBoss"/>), else <see cref="RaceRuleset.Standard"/>. Pure/static for unit
        /// tests; SetRuleset(Standard) is itself a documented no-op, so an enabled non-boss race still
        /// behaves as shipped.
        /// </summary>
        public static RaceRuleset RulesetForRace(bool bossRacesEnabled, bool runIsBossRace) =>
            IsDesignatedBoss(bossRacesEnabled, runIsBossRace) ? RaceRuleset.Boss : RaceRuleset.Standard;

        /// <summary>
        /// Boss-clear payout: doubles the position cash iff the ruleset carries
        /// <see cref="RaceModifier.DoublePayout"/>, then adds the flat <paramref name="bossRewardBonus"/>.
        /// Pure/static so the boss economy is testable in isolation. Only ever called on a clean boss finish.
        /// </summary>
        public static int ApplyBossReward(int payout, in RaceRuleset ruleset, int bossRewardBonus)
        {
            if (ruleset.Has(RaceModifier.DoublePayout)) payout *= 2;
            return payout + bossRewardBonus;
        }

        /// <summary>
        /// Whether a clean boss finish grants the interlude free-repair: yes on a boss race UNLESS the
        /// ruleset withholds it via <see cref="RaceModifier.NoRepairAfter"/> (which <see cref="RaceRuleset.Boss"/>
        /// carries, so the shipped boss template's damage rides into the garage). Pure/static for tests.
        /// </summary>
        public static bool GrantsFreeRepair(bool bossRace, in RaceRuleset ruleset) =>
            bossRace && !ruleset.Has(RaceModifier.NoRepairAfter);

        /// <summary>
        /// The ownership gate for the Draft-Leech payoff: true iff <paramref name="ownedParts"/> contains any
        /// part flagged <see cref="PartDef.DraftLeech"/>. Pure/static so the gate — and the guarantee that a
        /// run owning no such part is never paid the draft bonus — is unit-testable without a live scene. A
        /// null/empty list, or one holding no DraftLeech part, returns false; the caller then grants nothing.
        /// </summary>
        public static bool OwnsDraftLeechPart(IReadOnlyList<PartDef> ownedParts)
        {
            if (ownedParts == null) return false;
            for (int i = 0; i < ownedParts.Count; i++)
            {
                PartDef part = ownedParts[i];
                if (part != null && part.DraftLeech) return true;
            }
            return false;
        }

        /// <summary>
        /// Draft-Leech payoff math: the money earned for <paramref name="draftSeconds"/> spent drafting at
        /// <paramref name="ratePerSecond"/> $/s, rounded to whole cash and clamped to a per-race cap. Pure/
        /// static so the payoff is unit-testable in isolation. Returns 0 for a non-positive time or rate, is
        /// never negative, and a non-positive <paramref name="capPerRace"/> means uncapped. Only ever called
        /// once the ownership gate (see <see cref="OwnsDraftLeechPart"/>) has passed.
        /// </summary>
        public static int DraftLeechPayout(float draftSeconds, float ratePerSecond, int capPerRace)
        {
            if (draftSeconds <= 0f || ratePerSecond <= 0f) return 0;
            int raw = Mathf.Max(0, Mathf.RoundToInt(ratePerSecond * draftSeconds));
            return capPerRace > 0 ? Mathf.Min(raw, capPerRace) : raw;
        }

        private void Update()
        {
            if (Phase != RunPhase.Racing || _raceResolved) return;
            if (_raceManager == null || !_raceManager.RaceComplete) return;
            ResolveRace();
        }

        /// <summary>Payout + survival/boss verdict once every car has finished or been eliminated.</summary>
        private void ResolveRace()
        {
            _raceResolved = true;

            // Carry the car's accumulated wear out of this race BEFORE any payout/boss/save branch below,
            // so damage persists into the next race (and into the save the garage writes on open). Done
            // once here regardless of the verdict — a battered car is battered whether it finished or was
            // eliminated. Repairing it costs money in the garage (see RepairCar).
            if (_playerCar != null && _playerCar.Sim != null)
                Run.CarDurability = _playerCar.Sim.Durability;

            // Fragile parts are strong but breakable: if the just-captured wear says the car finished
            // this race battered near the sim's durability floor, ONE equipped Fragile part shakes loose
            // and is destroyed. The note is folded into every race-summary branch below.
            string fragileNote = BreakOneFragilePartOnHeavyDamage();

            RaceCarStatus me = _raceManager.GetStatus(_playerCar);
            if (me == null)
            {
                Debug.LogError("[RunDirector] Player car is not registered with the RaceManager.", this);
                return;
            }

            // Wave-12: fold the player's fastest lap of this race into the persistent per-track records.
            // Runs before any early-return branch below so it captures the last race of a run too. Purely
            // additive history — no gameplay/economy effect — and a no-op when no lap validated.
            RecordPlayerBestLap(me);

            bool eliminated = me.State == CarRaceState.Eliminated;
            // The boss cushion tightens with the season: the required top-N shrinks by one slot per
            // circuit (never below 1) so later bosses demand a sharper finish. RunState is untouched —
            // this is a per-circuit view of Run.BossTopN, computed only here.
            int effectiveBossTopN = Mathf.Max(1, Run.BossTopN - Run.CircuitIndex);
            bool bossFailed = !eliminated && Run.IsBossRace && me.Position > effectiveBossTopN;
            bool failed = eliminated || bossFailed;

            // Opt-in boss race: true only when BossRacesEnabled designated THIS race a boss (its circuit's
            // final race) — the same designation ApplyRuleset used to push RaceRuleset.Boss at bind time.
            // Default (disabled) leaves this false, so every boss-reward/repair branch below is skipped and
            // the payout/repair stay byte-for-byte as shipped.
            bool bossRace = IsDesignatedBoss(bossRacesEnabled, Run.IsBossRace);

            // Failure — elimination OR a flunked boss race — pays only the flat consolation: the
            // price of failure is the life AND the wallet, so tanking a boss for the fat inverted
            // payout and retrying richer is no longer a play. Only a clean finish collects the
            // position cash plus (capped) sponsor money.
            int payout;
            int economyBonus = 0;
            if (failed)
            {
                payout = payoutTable.EliminationConsolation;
            }
            else
            {
                payout = payoutTable.PayoutFor(me.Position, false);
                foreach (PartDef part in Run.EquippedParts)
                    if (part && part.Category == PartCategory.Economy)
                        economyBonus += payoutTable.EconomyBonusFor(part.MoneyPerPositionHeld, me.Position);

                // Higher license stakes pay a modest bump on a clean finish, scaling the earned
                // position cash by RunState.StakeMult. Guarded so stake 0 (the shipped default, and
                // the only reachable value until a stake-select UI lands) skips this entirely and the
                // payout stays byte-for-byte as shipped.
                if (Run.StakeLevel > 0)
                    payout = Mathf.CeilToInt(payout * Run.StakeMult);

                // Boss-race rewards on a clean boss finish (opt-in): honour the ruleset's DoublePayout
                // modifier and add the flat boss-clear bonus, then grant the interlude free-repair unless
                // the ruleset withholds it via NoRepairAfter (RaceRuleset.Boss does, so its damage rides
                // into the garage). All gated on bossRace, so a disabled run never reaches this and the
                // economy stays byte-for-byte as shipped.
                if (bossRace)
                {
                    payout = ApplyBossReward(payout, _raceManager.Ruleset, bossRewardBonus);
                    if (GrantsFreeRepair(bossRace, _raceManager.Ruleset))
                        Run.CarDurability = 1f;
                }
            }
            // Draft-Leech payoff (opt-in): pay money proportional to the time the player spent drafting this
            // race, but ONLY if the run owns a part flagged DraftLeech. With no such part owned the gate is
            // false, so the reward component is never even read and leechBonus stays 0 — the additions below
            // reduce to the shipped `payout + economyBonus`, byte-for-byte. It rides alongside the existing
            // payout without touching the payout/economy formulas themselves.
            int leechBonus = 0;
            if (OwnsDraftLeechPart(Run.OwnedParts))
            {
                DraftReward reward = _playerCar != null ? _playerCar.GetComponent<DraftReward>() : null;
                float draftSeconds = reward != null ? reward.DraftSeconds : 0f;
                leechBonus = DraftLeechPayout(draftSeconds, draftLeechRate, draftLeechCapPerRace);
            }

            Run.Money += payout + economyBonus + leechBonus;
            int totalPay = payout + economyBonus + leechBonus;

            if (failed)
            {
                Run.Lives -= 1;
                LastRaceSummary = (eliminated
                    ? $"P{me.Position} — ELIMINATED (missed the cutoff). +${totalPay}, -1 life."
                    : $"P{me.Position} — boss race demands top {effectiveBossTopN}. +${totalPay}, -1 life. Retry it.")
                    + fragileNote;

                if (Run.Lives <= 0)
                {
                    // Run's over: refund every owned Cashout part's Price into the final wallet.
                    int cashout = Run.CashoutRefundTotal();
                    Run.Money += cashout;
                    if (cashout > 0) LastRaceSummary += $" Cashout parts refunded +${cashout}.";
                    Phase = RunPhase.RunOver;
                    Time.timeScale = 0f;
                    ClearSave(); // the run is dead — don't resume it next launch
                    RecordRunEndToMeta(seasonCleared: false);
                    return;
                }
                // RaceIndex unchanged: the same race is retried after the garage.
            }
            else
            {
                Run.RaceIndex += 1;

                // Cleared the circuit's boss race? On the final circuit that wins the whole
                // season; otherwise promote to the next (harder) circuit with a fresh race ladder
                // instead of ending the run.
                if (Run.RaceIndex >= Run.RacesPerCircuit)
                {
                    if (Run.RunComplete)
                    {
                        // Season won — the run ends here: refund every owned Cashout part's Price.
                        int cashout = Run.CashoutRefundTotal();
                        Run.Money += cashout;
                        LastRaceSummary = $"P{me.Position} — survived. +${totalPay}. SEASON CLEARED!"
                            + fragileNote
                            + (cashout > 0 ? $" Cashout parts refunded +${cashout}." : "");
                        Phase = RunPhase.RunComplete;
                        Time.timeScale = 0f;
                        ClearSave(); // season won — the finished run doesn't resume
                        RecordRunEndToMeta(seasonCleared: true);
                        return;
                    }
                    Run.CircuitIndex += 1;
                    Run.RaceIndex = 0;
                    LastRaceSummary =
                        $"P{me.Position} — boss down. +${totalPay}. Circuit {Run.CircuitIndex + 1}/{Run.TotalCircuits} begins."
                        + fragileNote;
                }
                else
                {
                    LastRaceSummary = $"P{me.Position} — survived. +${totalPay}." + fragileNote;
                }
            }

            OpenGarage();
        }

        // A car that finished the race within this band ABOVE the sim's durability floor took HEAVY
        // damage — the signal that a Fragile part shook loose (see BreakOneFragilePartOnHeavyDamage).
        private const float FragileBreakDurabilityBand = 0.05f;

        /// <summary>
        /// Fragile parts (PartCondition.Fragile) are strong but breakable: if the car finished the race
        /// battered near the sim's durability floor — read from the just-captured Run.CarDurability, the
        /// HEAVY-damage signal — ONE equipped Fragile part shakes loose and is destroyed, removed from
        /// both EquippedParts and OwnedParts (parts are unique, so dropping the PartDef is a clean delete).
        /// At most one break per race. Returns a summary suffix noting the loss, or "" if nothing broke.
        /// </summary>
        private string BreakOneFragilePartOnHeavyDamage()
        {
            bool heavyDamage = Run.CarDurability <= VehicleSim.MinDurability + FragileBreakDurabilityBand;
            if (!heavyDamage) return "";

            PartDef toBreak = null;
            foreach (PartDef part in Run.EquippedParts)
            {
                if (part != null && part.Condition == PartCondition.Fragile)
                {
                    toBreak = part;
                    break;
                }
            }
            if (toBreak == null) return "";

            Run.RemovePart(toBreak);
            return $" Your Fragile {toBreak.DisplayName} shook loose and broke!";
        }

        private void OpenGarage()
        {
            Phase = RunPhase.Garage;
            Time.timeScale = 0f;

            // Economy-depth hooks, both no-ops at the shipped defaults: reset the standalone per-visit
            // reroll counter, and pay Balatro-style interest on banked cash. RunState.InterestPerBlock
            // defaults to 0, so ApplyShopInterest grants $0 and Money is unchanged — the shipped economy
            // is untouched until a designer raises the interest rate. (The live garage reroll runs through
            // ShopLogic; ResetRerollCounter only clears RunState's separate ChargeReroll counter.)
            Run.ResetRerollCounter();
            Run.ApplyShopInterest();

            // Seed the shop deterministically from the run so a resumed/shared run reproduces the
            // exact same stock and rerolls, then persist the post-race state.
            Shop.BeginVisit(partPool ? partPool.Parts : null, Run, VisitSeed());
            Save();
        }

        /// <summary>Garage Buy button.</summary>
        public bool BuyOffer(PartDef part)
        {
            bool bought = Shop.TryBuy(part, Run);
            if (bought) Save();
            return bought;
        }

        /// <summary>Garage Reroll button — escalating cost handled by ShopLogic.</summary>
        public bool RerollShop()
        {
            bool rerolled = Shop.TryReroll(partPool ? partPool.Parts : null, Run);
            if (rerolled) Save();
            return rerolled;
        }

        /// <summary>
        /// Current cost to fully repair the run's car, scaling with how worn it is: 0 when pristine,
        /// up to <see cref="fullRepairCost"/> when battered all the way to the durability floor, and at
        /// least $1 for any wear at all. The garage reads this to label and gate the REPAIR CAR button.
        /// </summary>
        public int RepairCost => ComputeRepairCost();

        // Repair pricing lives in RunState.RepairCostFor (a pure, testable helper). At the shipped
        // repairDamageExponent of 1 this is byte-for-byte the original inline formula:
        // Mathf.Max(1, Mathf.CeilToInt(fullRepairCost * normalizedWear)).
        private int ComputeRepairCost() =>
            RunState.RepairCostFor(Run.CarDurability, fullRepairCost, repairDamageExponent);

        /// <summary>
        /// Garage REPAIR CAR button: pays to restore the car to full durability. The cost scales with how
        /// worn the car is (see <see cref="RepairCost"/>) — a money sink that tensions the inverted catch-up
        /// economy. No-op returning false when the car is already pristine or the wallet can't cover the cost;
        /// otherwise deducts the cost, resets CarDurability to full, persists and returns true.
        /// </summary>
        public bool RepairCar()
        {
            if (Run.CarDurability >= 1f) return false; // nothing to repair
            int cost = ComputeRepairCost();
            if (Run.Money < cost) return false;        // can't afford it
            Run.Money -= cost;
            Run.CarDurability = 1f;
            Save();
            return true;
        }

        /// <summary>Garage NEXT RACE button: unpause and reload the race scene for a clean grid.</summary>
        public void StartNextRace()
        {
            if (Phase != RunPhase.Garage) return;
            Phase = RunPhase.Racing;
            Time.timeScale = 1f;
            ReloadRaceScene();
        }

        /// <summary>
        /// Run-over / run-complete screens: reset everything and go again at the SAME license stake the
        /// just-ended run was played at (read before the run is replaced). Preserved parameterless entry
        /// point for existing callers (GarageScreen); it defers to <see cref="StartNewRun(int)"/>.
        /// </summary>
        public void StartNewRun() => StartNewRun(Run != null ? Run.StakeLevel : 0);

        /// <summary>
        /// Starts a brand-new run at the given 0-based license stake. Higher stakes ramp difficulty and
        /// reward through RunState.StakeLevel; a stake is unlocked by clearing the season below it
        /// (MetaProgress). The requested stake is clamped to what the profile has actually unlocked, so a
        /// caller can never force a locked stake. Selection UI is a follow-up — this is the entry point
        /// it will call.
        /// </summary>
        public void StartNewRun(int stakeLevel)
        {
            int stake = ClampToUnlockedStake(stakeLevel);
            Run = new RunState { Money = startingMoney, Seed = RollSeed(), StakeLevel = stake };
            LastRaceSummary = "";
            Phase = RunPhase.Racing;
            Time.timeScale = 1f;
            Save(); // overwrite any previous save with the fresh, freshly-seeded run
            ReloadRaceScene();
        }

        /// <summary>Clamps a requested stake to the range the persistent profile has unlocked (>= 0).</summary>
        private int ClampToUnlockedStake(int stakeLevel)
        {
            if (stakeLevel <= 0) return 0;
            if (Meta == null) return 0;
            return Meta.IsStakeUnlocked(stakeLevel) ? stakeLevel : Meta.HighestUnlockedStake;
        }

        /// <summary>
        /// Folds the just-ended run into the persistent MetaProgress profile and saves it: always counts
        /// the run and tracks the best circuit reached + lifetime money; on a season clear it also counts
        /// the season and unlocks the NEXT license stake (the cross-run escalation hook). MetaProgress.Save
        /// swallows its own IO errors, so this can never break the run-end flow.
        /// </summary>
        private void RecordRunEndToMeta(bool seasonCleared)
        {
            if (Meta == null) Meta = new MetaProgress();
            Meta.RegisterRunEnd(Run.CircuitIndex, Run.Money);
            if (seasonCleared) Meta.RegisterSeasonCleared(Run.StakeLevel);

            // Wave-12: append a compact summary of the just-ended run to the rolling history log. The
            // timestamp is read from the HOST clock here (never inside pure logic) and passed in; the
            // entry is purely additive history with no effect on any future run's difficulty or reward.
            Meta.RecordRun(new RunHistoryEntry
            {
                circuitsCleared = Run.CircuitIndex,
                finalMoney = Run.Money,
                stakeLevel = Run.StakeLevel,
                timestamp = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });

            MetaProgress.Save(Meta);
        }

        /// <summary>
        /// Folds the player's fastest lap of the just-finished race into the persistent per-track lap
        /// records (wave-12), keyed by a stable track id, and persists the profile only when it is a NEW
        /// record. Purely additive history — lap records carry no gameplay or economy weight — so this
        /// never touches a run's feel or balance. A race in which the player validated no lap
        /// (BestLapTimeS &lt; 0) is a no-op inside <see cref="MetaProgress.RecordBestLap"/>.
        /// </summary>
        private void RecordPlayerBestLap(RaceCarStatus me)
        {
            if (Meta == null) Meta = new MetaProgress();
            if (me == null) return;
            if (Meta.RecordBestLap(CurrentTrackId(), me.BestLapTimeS))
                MetaProgress.Save(Meta); // a new record — flush now so lap records survive a mid-run quit
        }

        /// <summary>
        /// Stable identifier for the track the current race runs on — the active scene's name (every race
        /// in a run reloads the same greybox loop, so its name IS the track's identity, and a future
        /// multi-track build distinguishes tracks by scene automatically). Used only to key the additive
        /// lap records; falls back to a constant so a nameless scene still records.
        /// </summary>
        private static string CurrentTrackId()
        {
            string scene = SceneManager.GetActiveScene().name;
            return string.IsNullOrEmpty(scene) ? "track" : scene;
        }

        /// <summary>Rolls a fresh run seed for a brand-new run (non-negative).</summary>
        private static int RollSeed() => new System.Random().Next();

        /// <summary>
        /// Per-garage-visit shop seed: mixes the run seed with the circuit and race indices so each
        /// visit is deterministic AND distinct (a plain sum would collide, e.g. circuit 1/race 0 vs
        /// circuit 0/race 1). A resumed or shared run reproduces the exact same stock and rerolls.
        /// </summary>
        private int VisitSeed()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + Run.Seed;
                h = h * 31 + Run.CircuitIndex;
                h = h * 31 + Run.RaceIndex;
                return h;
            }
        }

        /// <summary>Persists the live run to disk; failures are logged, never fatal to the loop.</summary>
        private void Save()
        {
            try { RunSave.Save(Run); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RunDirector] Run save failed: {e.Message}", this);
            }
        }

        /// <summary>Deletes the save so a finished/dead run doesn't resume next launch.</summary>
        private void ClearSave()
        {
            try { RunSave.Delete(); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RunDirector] Run save clear failed: {e.Message}", this);
            }
        }

        private void ReloadRaceScene()
        {
#if UNITY_EDITOR
            // RaceTest.unity is not in Build Settings; load it by path while in play mode.
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                SceneManager.GetActiveScene().path,
                new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
#endif
        }
    }
}
