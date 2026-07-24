using System.Collections.Generic;
using Shitboxer.Race;
using Shitboxer.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;
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
    public class RunDirector : MonoBehaviour, IRunHost
    {
        public static RunDirector Instance { get; private set; }

        [SerializeField] private PartPool partPool;
        [SerializeField] private VehicleSpecAsset[] chassisSpecs;
        [SerializeField] private PayoutTable payoutTable = new PayoutTable();
        [Tooltip("Cash a fresh run starts with.")]
        [SerializeField] private int startingMoney = 5;
        [Tooltip("Cash to fully repair a car worn all the way to the durability floor; lighter wear costs proportionally less. A money sink tensioning the inverted catch-up economy.")]
        [SerializeField] private int fullRepairCost = 12;

        [Tooltip("Damage-curve exponent for the repair price (see RunState.RepairCostFor). 1 (default) prices repairs LINEARLY in wear — byte-for-byte today's cost; >1 makes deep damage disproportionately dear; <1 front-loads light damage. Endpoints are unchanged either way.")]
        [SerializeField] private float repairDamageExponent = 1f;

        [Header("Part crates (doc 03's booster-style packs)")]
        [Tooltip("Cost of a part crate. Priced against the $5 reroll on purpose: a crate is a GUARANTEED pick of N with no part price on top, while a reroll only reshuffles a shelf you must still pay from. Too cheap and it strictly dominates rerolling; too dear and the first garage stays the non-choice it is today. A prime tuning target for the enable+tune pass.")]
        [Min(0)]
        [SerializeField] private int cratePrice = 6;

        [Tooltip("How many parts a crate draws for the player to pick ONE from. Drawn on the shipped rarity curve (Common common, Rare rare) and excluding both owned parts and the current shelf. 3 = Balatro's standard pack shape.")]
        [Min(1)]
        [SerializeField] private int crateDrawCount = 3;

        /// <summary>Shop-facing crate price, so the garage can render and gate the offer.</summary>
        public int CratePrice => cratePrice;

        [Header("Season shape")]
        [Tooltip("How many circuits make up a full season. 8 (doc 08 decision 12): 24 races, ~75 minutes — the length RunState.DifficultyMult's convex ramp was built for, and what makes team upgrades and long-horizon parts viable at all (both are structurally dead in a short run by their own docs). Track scenes rotate modulo the raceScenes list, so 8 circuits over 3 built tracks just cycles. Clamped to >= 1. Season length is CONFIG, not run progress — RunSave never persists it — so this field is re-stamped onto every run the director adopts (fresh, resumed, or restarted), letting a change here take effect on a run already in flight.")]
        [Min(1)]
        [SerializeField] private int totalCircuits = 8;

        [Tooltip("Races in each circuit — the last one is the boss (top-3 required). 3 per doc 08 decision 12 (8 circuits x 3 = the 24-race season). The old value of 5 existed to give a ONE-circuit season enough garages for the shop to breathe; at 8 circuits the run has 24 garages, so that rationale is superseded and 3 keeps each circuit's boss cadence tight. Same CONFIG rule as totalCircuits: never persisted by RunSave, re-stamped onto every run the director adopts, so a retune reaches a run already in flight.")]
        [Min(1)]
        [SerializeField] private int racesPerCircuit = 3;

        [Tooltip("Track scenes the run rotates through, one per race — otherwise a 5-race run is the same rectangle five times. Every name must be a scene in Build Settings; 'Shitboxer/Build Race Scenes' generates them and registers them. Leave EMPTY to reload the active scene instead, which is the old single-track behaviour.")]
        [SerializeField] private string[] raceScenes = { "RaceTest", "RaceGauntlet", "RaceSpeedway" };

        [Header("Persistent rivals")]
        [Tooltip("Career roster the run draws its field of named rivals from. Leave EMPTY to fall back to the built-in 24-rival roster in RivalRoster.Default. A null roster pushes no identities at all, so every bot keeps its legacy hierarchy-derived character — byte-for-byte the pre-roster behaviour.")]
        [SerializeField] private RivalRoster rivalRoster;

        [Tooltip("When ON, rivals adapt their RACECRAFT to what they have learned about this player across the career — defending the side you favour, leaving room if you race dirty, fighting harder if there's history. Never touches pace. OFF (default) pushes no memory at all, so every rival races exactly as it does today; observation still runs and memories still accumulate, so flipping this on later starts from a career's worth of evidence rather than nothing.")]
        [SerializeField] private bool rivalMemoryEnabled = false;

        /// <summary>Opt-in rivalry adaptation (default OFF). See the tooltip; observation runs either way.</summary>
        public bool RivalMemoryEnabled => rivalMemoryEnabled;

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

        /// <summary>
        /// Raised on every run-phase transition. A retained-mode UI subscribes to this instead of
        /// polling <see cref="Phase"/> each frame. NOTE the initial Racing phase is a field
        /// initializer above and fires NO event, so a UI must read Phase once when it wakes rather
        /// than wait for the first transition (see ShitboxerUIRoot, wave 27+).
        /// </summary>
        public event System.Action<RunPhase> PhaseChanged;

        /// <summary>
        /// The single write-point for <see cref="Phase"/>: every transition routes here so the run
        /// loop's state machine has exactly one edge — what a retained-mode UI (and any future
        /// save/replay/netcode layer) needs. Idempotent; re-entering the same phase raises nothing.
        /// </summary>
        private void SetPhase(RunPhase phase)
        {
            if (Phase == phase) return;
            Phase = phase;
            PhaseChanged?.Invoke(phase);
        }

        /// <summary>The live race manager, or null between scenes / while paused in the garage.</summary>
        /// <summary>
        /// Joins race sector events to the player's sector-scoring parts (doc 08). Owned here because
        /// RunDirector is what knows about both the race and the loadout; it holds no Unity state of its
        /// own, so it is a plain field rather than a component.
        /// </summary>
        private readonly SectorPartRunner _sectorParts = new SectorPartRunner();

        /// <summary>Live sector-part scoring for the current race — read by the HUD.</summary>
        public SectorPartRunner SectorParts => _sectorParts;

        /// <summary>
        /// Joins the race to the player's equipped ACTIVE item (doc 08 decision 14). Same ownership
        /// logic as the sector runner: the director knows both the race and the loadout. The single
        /// ACTIVATE bind is read here (the host layer) from the settings file's key name.
        /// </summary>
        private readonly ActivePartRunner _activeItem = new ActivePartRunner();

        // The parsed ACTIVATE key, refreshed from GameSettings at every scene bind so a rebind made
        // in the main menu reaches a run already in flight at its next race.
        private UnityEngine.InputSystem.Key _activateKey = UnityEngine.InputSystem.Key.Q;
        private string _activateKeyLabel = ActivateKeyBinding.DefaultKey;

        /// <summary>The equipped active item's live charge meter, flattened for the HUD (IRunHost).</summary>
        public ActiveReadout ActiveItem => _activeItem.Readout(_activateKeyLabel);

        public RaceManager CurrentRace => _raceManager;

        /// <summary>The live player car, or null between scenes.</summary>
        public VehicleController PlayerCar => _playerCar;

        /// <summary>
        /// Cash a clean finish at <paramref name="position"/> banks right now — exactly what RaceHud's
        /// pushed payout closure computes, but PULLED through IRunHost so the UI can ask rather than be
        /// injected. Uses the same <see cref="CleanFinishPayoutFor"/> the real resolution calls, so the
        /// preview can't drift from the payout. (ApplyPayoutPreview's push stays until RaceHud is
        /// deleted in wave 30.)
        /// </summary>
        public int PayoutPreviewFor(int position) =>
            CleanFinishPayoutFor(position, IsDesignatedBoss(bossRacesEnabled, Run.IsBossRace));

        /// <summary>One-line player verdict of the last resolved race, for the garage header.</summary>
        public string LastRaceSummary { get; private set; } = "";

        private RaceManager _raceManager;
        private VehicleController _playerCar;
        private bool _raceResolved;

        // Dev pause menu (ESC): a throwaway harness affordance to bail on a season and start a fresh run
        // mid-test without racing it out. Replaced, not ported, when the UI Toolkit pause menu lands.
        private bool _devMenuOpen;
        private float _preMenuTimeScale = 1f;

        /// <summary>Editor wiring (MetaAssetsBuilder) — sets serialized fields only.</summary>
        public void Configure(PartPool pool) => partPool = pool;

        /// <summary>Editor wiring: the base spec per chassis id (0 = Grip, 1 = Power), swapped onto the
        /// player at scene bind so car-select actually changes the car driven.</summary>
        public void ConfigureChassis(VehicleSpecAsset[] specs) => chassisSpecs = specs;

        /// <summary>Editor wiring: the career roster this run draws its named rivals from. Null is legal —
        /// bots then keep their legacy hierarchy-derived character and no identities are pushed.</summary>
        public void ConfigureRivalRoster(RivalRoster roster) => rivalRoster = roster;

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

            // A fresh run requested by the main menu (with a chosen chassis) wins over any save.
            if (RunLaunch.ConsumeNewRun(out int launchChassis, out int launchStake))
            {
                Run = new RunState
                {
                    Money = startingMoney,
                    Seed = RollSeed(),
                    StakeLevel = ClampToUnlockedStake(launchStake),
                    ChassisId = launchChassis,
                };
            }
            // Otherwise resume an interrupted run if a save exists (parts resolved by Id via the pool);
            // else begin fresh with the starting cash and a freshly-rolled deterministic seed.
            else if (partPool != null && RunSave.TryLoad(partPool, out RunState resumed))
            {
                Run = resumed;
            }
            else
            {
                Run.Money = startingMoney;
                Run.Seed = RollSeed();
            }

            // Season shape is config, not saved progress — stamp it on whichever run we just adopted so a
            // resumed run picks up the current inspector value rather than the default it was rebuilt with.
            ApplySeasonShape(Run, totalCircuits, racesPerCircuit);
        }

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnDestroy()
        {
            // Drop the sector subscription before the director goes away — RaceManager outlives it on a
            // scene teardown, and a dangling handler on a destroyed director would keep scoring.
            _sectorParts.Unbind();
            _activeItem.Unbind();
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            // sceneLoaded may not fire for the scene already open when play begins, so bind lazily.
            if (Instance == this && _raceManager == null) BindToScene();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => BindToScene();

        /// <summary>Finds the race actors in the freshly loaded scene and preps the player car.</summary>
        /// <summary>
        /// The player's part-free authored spec, cloned at every scene bind BEFORE
        /// <see cref="ApplyEquippedParts"/> bakes the equipped stat parts over it. The garage reads
        /// this to preview an arbitrary equipped set without touching the live car. Null only when
        /// there is no player car / spec to read. Captured in <see cref="BindToScene"/>, where the
        /// moment is unconditional — unlike the old GarageScreen.TryCaptureBaseSpec, which guessed
        /// at it from OnGUI and missed it entirely on a resumed run that already had a stat part on.
        /// </summary>
        public VehicleSpec BaseSpec { get; private set; }

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

            // Snapshot the player's part-free authored spec BEFORE ApplyEquippedParts bakes the
            // equipped stat parts over it. Unconditional here because the scene is rebuilt every
            // race, so the car carries its prefab spec at this exact moment every time (SetSpec only
            // ever writes a runtime asset onto the live component, never the prefab). This retires
            // GarageScreen.TryCaptureBaseSpec, which had to guess the same moment from inside OnGUI
            // and so blacked out the garage's stat bars for the rest of any RESUMED run.
            // Car-select chassis: swap the run's chosen base spec onto the player before we snapshot it,
            // so BaseSpec (and every part preview built on it) reflects the picked car. Idempotent — the
            // scene is rebuilt each race, so the player carries its prefab spec here every time.
            if (chassisSpecs != null && Run.ChassisId >= 0 && Run.ChassisId < chassisSpecs.Length
                && chassisSpecs[Run.ChassisId] != null)
                _playerCar.SetSpec(chassisSpecs[Run.ChassisId]);

            BaseSpec = _playerCar.SpecAsset != null
                ? SpecModApplier.Clone(_playerCar.SpecAsset.Spec)
                : null;

            ApplyEquippedParts();
            ApplyAttackProfile();
            ApplyBotStrength();      // AFTER ApplyEquippedParts: skips whichever car is the player's
            ApplyRivalIdentities();  // AFTER ApplyBotStrength: that one early-outs when the ramp is off, this must always run
            ApplyRuleset();     // opt-in boss ruleset BEFORE ApplyDifficulty so the per-circuit tighten reads its base
            ApplyDifficulty();

            // Deal this race's grid. BindToScene runs off sceneLoaded, which Unity fires BEFORE Start, so
            // RaceManager picks the seed up in time to shuffle ahead of its own position snapshot.
            _raceManager.SetGridSeed(GridSeed());

            // Persistent wear carries ACROSS races within a run: a freshly-rebuilt sim resets to full
            // durability (and ApplyEquippedParts may have just rebuilt it via SetSpec), so re-apply the
            // run's carried value here — after the other Apply* calls — so a battered car stays battered
            // until the player pays to repair it in the garage. Floored first: with retirement at zero
            // (decision 15), rolling a wreck onto the grid would retire it at the green flag and drain
            // the run's remaining lives with zero player input — so overnight the crew hammers the
            // panels straight enough to roll, free. 25% is still deeply crippled, just drivable.
            Run.CarDurability = RaceStartDurability(Run.CarDurability);
            if (_playerCar.Sim != null)
                _playerCar.Sim.SetDurability(Run.CarDurability);

            // Draft-Leech payoff (opt-in): ensure the draft-time accumulator on the player car and zero it
            // for this race. Purely additive telemetry that applies no forces and never touches the sim;
            // RunDirector reads its total at race end ONLY for a player who owns a DraftLeech part, so a car
            // without one is entirely unaffected.
            DraftReward.GetOrAdd(_playerCar.gameObject).Reset();

            // Sector-scoring parts (doc 08): join this race's sector events to the player's loadout.
            // Bind() unhooks any previous race first, so a scene reload can never double-subscribe and
            // score every sector twice. Inert for a loadout with no sector rules.
            _sectorParts.Bind(_raceManager, _playerCar, Run);

            // Active item (doc 08 decision 14): arm the first equipped active part's reservoir for
            // this race and refresh the ACTIVATE bind from settings, so a rebind made in the main
            // menu reaches a run already in flight at its next race. Inert for a loadout without one.
            _activeItem.Bind(_raceManager, _playerCar, Run);
            _activateKey = ActivateKeyBinding.Parse(GameSettings.Load().activateKey);
            _activateKeyLabel = _activateKey.ToString(); // normalized: the HUD hint shows what actually works
        }

        /// <summary>
        /// Bakes equipped stat parts into a deep copy of the player's spec, wraps it in a
        /// throwaway runtime VehicleSpecAsset, and swaps it onto the controller. Skipped when
        /// no stat parts are equipped so the authored asset keeps driving the car.
        /// </summary>
        /// <summary>
        /// Bakes the player's build onto the car: component levels through the stat ledger first, then
        /// equipped stat parts over the top.
        ///
        /// Order is deliberate. Components are the car's own specification — what it IS — so they set
        /// the baseline; parts are bolt-ons that modify whatever is underneath. Doing it the other way
        /// round would make a part's percentage apply to the bare chassis and then be diluted by
        /// component levels, which is backwards from how a player reads it.
        ///
        /// Skips the whole rebuild only when there is genuinely nothing to apply — no stat part AND
        /// every component still at its baseline — because rebuilding the sim mid-run is not free
        /// (SetSpec reconstructs it, and the caller re-applies carried durability afterwards).
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

            BuildLedger components = Run.ComponentLedger();
            bool anyComponentLevel = components.Power != 0f || components.Grip != 0f
                                     || components.Weight != 0f || components.Durability != 0f;
            if (!anyStatPart && !anyComponentLevel) return;

            VehicleSpec built = StatLedger.Bake(_playerCar.SpecAsset.Spec, components);
            if (anyStatPart) built = SpecModApplier.Apply(built, Run.EquippedParts);

            var asset = ScriptableObject.CreateInstance<VehicleSpecAsset>();
            asset.name = "PlayerSpec_Runtime";
            asset.Spec = built;
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

        // --- Bot strength ramp -----------------------------------------------------------------
        // Bots never equip parts — SpecModApplier.Apply is only ever handed the player's car — so
        // without this the whole field stays showroom-stock while the player's build compounds at
        // every garage. Playtest 2026-07-17: the player lapped all 7 rivals by lap 2 of 3.
        //
        // This is deliberately NOT DifficultyScalar. That scales a bot's TARGET speed, and a measured
        // quasi-steady-state lap sim says the lap is ACCELERATION-limited, not target-limited: bots
        // run ~15-39 m/s against a target of 52, so raising the target changes nothing at all (52 and
        // 70 give identical lap times). Only the car moves the number. Same reason the racing line
        // was a dead end — widening corners was worth ~3%.

        [Tooltip("Rival-car grip/power multiplier on race 1, BEFORE any ramp. 1.0 = showroom-stock, which playtested far too soft: rivals drive the same shitbox the player starts in, but a human out-drives BotBrain's speed plan by ~25% (measured: the player's fresh-season best is 14.4s where the plan says a stock car laps ~28s), so an unscaled field is never a race even on lap 1. 1.4 puts race 1 near 23s.")]
        [Min(1f)]
        [SerializeField] private float botStrengthBase = 1.4f;

        [Tooltip("Extra grip/power per race already completed this run, added to botStrengthBase. 0.013 across the 24-race season (doc 08 decision 13) ramps 1.4 -> 1.70, landing exactly on the cap at the final race. This is the rivals' shop: bots never buy parts, so without it the player's build simply walks away. The old 0.40 was tuned for a 5-race season — at 24 races it capped the field at race 4 and sat flat for the remaining 20. 0 = a flat field all season.")]
        [SerializeField] private float botStrengthPerRace = 0.013f;

        [Tooltip("Ceiling on the rival-car scale, so a long season can't hand the field a spaceship. 1.70 (doc 08 decision 13) is the PRACTICAL player-build ceiling, not the theoretical x2.0: a typical build (~x1.45) never out-stats the field, a genuinely good one passes it in the last third of the season — earned, not scheduled. The old 3.0 put bots at u~3.96 on GripBox (4g cornering), survivable only because they ride rails.")]
        [SerializeField] private float botStrengthMax = 1.7f;

        /// <summary>
        /// Scales every rival's car for this race off <see cref="RunState.RaceNumber"/>. Rivals are
        /// found by their BotDriver — a car with one IS a rival — rather than through
        /// RaceManager.Cars, which is still empty here: BindToScene runs off sceneLoaded, before
        /// RaceManager.Start has registered anything. The scene is rebuilt each race so every bot
        /// starts from its authored prefab spec and this never compounds. Runs before BotDriver
        /// builds its brain (that happens in FixedUpdate), so BotLimits reads the scaled grip.
        /// </summary>
        /// <summary>
        /// Rival-car scale for a given race of the run: base, plus the ramp per race already run,
        /// clamped to [1, max]. Pure and static so the ramp is unit-testable without a live scene
        /// (same convention as <see cref="ApplySeasonShape"/> / <see cref="IsDesignatedBoss"/>).
        /// Returns 1 for an OFF configuration, which callers treat as "leave rivals as authored".
        /// </summary>
        public static float BotStrengthFor(int raceNumber, float baseScale, float perRace, float max) =>
            Mathf.Clamp(Mathf.Max(1f, baseScale) + Mathf.Max(0f, perRace) * Mathf.Max(0, raceNumber),
                        1f, Mathf.Max(1f, max));

        /// <summary>
        /// Durability floor applied to the carried wear when a race BEGINS (decision 15). With
        /// retirement at zero, a car rolled onto the grid as a wreck would retire at the green flag —
        /// and since a failed race is retried, a broke player with a wrecked car would lose every
        /// remaining life with zero input. So the crew hammers the panels straight enough to roll,
        /// free: 25% durability, still deeply crippled (25% pace at the default wear exponent), but
        /// drivable. Applied only at race start — garage repair costs still read the true carried wear.
        /// </summary>
        public const float MinRaceStartDurability = 0.25f;

        /// <summary>The durability a race actually starts from, given the run's carried wear.</summary>
        public static float RaceStartDurability(float carried) =>
            Mathf.Max(Mathf.Clamp01(carried), MinRaceStartDurability);

        private void ApplyBotStrength()
        {
            // Grip and power scale together behind one knob, but they are NOT equal levers: measured
            // on this track, 3x grip alone takes a bot lap 28.0s -> 19.3s while 3x power alone only
            // reaches 24.5s. Grip is what buys pace; the power rides along so rivals can use the
            // exits. If this ever needs splitting, raise the grip term first.
            float scale = BotStrengthFor(Run.RaceNumber, botStrengthBase, botStrengthPerRace, botStrengthMax);
            if (scale <= 1f) return; // base 1 and no ramp = OFF: rivals stay exactly as authored

            foreach (BotDriver bot in FindObjectsByType<BotDriver>(FindObjectsSortMode.None))
            {
                var car = bot.GetComponent<VehicleController>();
                if (!car || car == _playerCar || car.SpecAsset == null) continue;

                var asset = ScriptableObject.CreateInstance<VehicleSpecAsset>();
                asset.name = $"BotSpec_Runtime_x{scale:F2}";
                asset.Spec = SpecModApplier.Scaled(car.SpecAsset.Spec, scale, scale);
                car.SetSpec(asset);
            }
        }

        // This run's drawn field: entry i is the roster index racing in grid slot i. Rebuilt at every
        // BindToScene rather than persisted — RivalField.Draw depends only on the run seed and the sizes,
        // so it reproduces identically across the scene reload, the track rotation, and a resumed run.
        private int[] _rivalField = System.Array.Empty<int>();

        /// <summary>
        /// Binds each scene-baked bot to a persistent roster rival: a stable name, a stable character, and
        /// the per-race key the observation layer attributes events with.
        ///
        /// This is what makes a rival a WHO rather than a grid position. Character is seeded off the rival's
        /// id (see <see cref="RivalField.IdentitySeed"/>), so Vera Kestrel drives like Vera Kestrel on every
        /// track in every run — which is the precondition for any memory of her being worth keeping.
        ///
        /// A null roster makes no calls at all, leaving every bot on its legacy hierarchy-derived seed, so
        /// the pre-roster behaviour is reproduced byte-for-byte.
        /// </summary>
        private void ApplyRivalIdentities()
        {
            IReadOnlyList<RivalDef> roster = rivalRoster != null ? rivalRoster.Rivals : null;
            if (roster == null || roster.Count == 0) return;

            // Sort by the baked slot so the draw is stable regardless of the order FindObjectsByType
            // happens to return — that order is not contractual, and letting it leak in would reshuffle
            // who is who between two loads of the same race.
            var bots = new List<BotDriver>(FindObjectsByType<BotDriver>(FindObjectsSortMode.None));
            bots.Sort((a, b) => a.RivalSlot.CompareTo(b.RivalSlot));
            if (bots.Count == 0) return;

            _rivalField = RivalField.Draw(RivalFieldSeed(), roster.Count, bots.Count);

            for (int i = 0; i < bots.Count; i++)
            {
                RivalDef def = roster[_rivalField[i]];
                bots[i].SetRivalIdentity(RivalField.KeyForSlot(i), RivalField.IdentitySeed(def.id),
                    def.drivingArchetype);
            }

            if (rivalMemoryEnabled) PushRivalMemories(bots, roster);
        }

        /// <summary>
        /// Hands each rival what it has learned about this player. Opt-in via <see cref="rivalMemoryEnabled"/>;
        /// with the flag off nothing is pushed and every bot keeps <c>RivalMemoryProfile.Unknown</c>, which is
        /// a true no-op at every tactical site.
        ///
        /// Runs the nemesis budget across the whole field before pushing, so the caps are decided once with
        /// everyone's history in view rather than per car.
        /// </summary>
        private void PushRivalMemories(List<BotDriver> bots, IReadOnlyList<RivalDef> roster)
        {
            if (Meta == null) Meta = new MetaProgress();
            Meta.rivalMemories ??= new List<RivalMemory>();
            Meta.playerStyle ??= new PlayerStyleProfile();

            PlayerStyleProfile style = RivalMemoryStore.GetStyle(
                Meta.playerStyle, Meta.careerRaces, Meta.styleLastFoldedRace);

            var ids = new List<string>(bots.Count);
            var memories = new List<RivalMemory>(bots.Count);
            var profiles = new List<RivalMemoryProfile>(bots.Count);

            for (int i = 0; i < bots.Count; i++)
            {
                RivalDef def = roster[_rivalField[i]];
                RivalMemory mem = RivalMemoryStore.Get(Meta.rivalMemories, def.id, Meta.careerRaces);
                ids.Add(def.id);
                memories.Add(mem);
                profiles.Add(RivalAdaptation.ToProfile(style, mem, RivalLearningProfile.For(def.personality)));
            }

            RivalAdaptation.ApplyNemesisBudget(ids, memories, profiles);

            for (int i = 0; i < bots.Count; i++)
            {
                bots[i].SetPlayerMemory(profiles[i]);
                // Persist what was actually emitted so next race slews from here rather than recomputing —
                // this is what makes the anti-oscillation limit hold ACROSS races, not just within one.
                StoreRememberedBiases(ids[i], memories[i], profiles[i]);
            }
        }

        private void StoreRememberedBiases(string rivalId, RivalMemory mem, in RivalMemoryProfile profile)
        {
            RivalMemory updated = RivalAdaptation.RememberBiases(mem, profile);
            updated.rivalId = rivalId;
            for (int i = 0; i < Meta.rivalMemories.Count; i++)
            {
                if (Meta.rivalMemories[i].rivalId != rivalId) continue;
                Meta.rivalMemories[i] = updated;
                return;
            }
        }

        /// <summary>
        /// Seed for the rival draw. Deliberately keyed off the RUN seed alone — not <see cref="GridSeed"/>,
        /// which mixes in the circuit/race indices — because the field must be the SAME cast of drivers for
        /// every race of a run. Mixing race indices in would redraw the roster each race and there would be
        /// no one to build a rivalry with.
        /// </summary>
        private int RivalFieldSeed() => unchecked(Run.Seed * 31 + 977);

        /// <summary>
        /// The rival racing in a given grid slot this run, for the HUD and (later) the collection screen.
        /// Returns an invalid <see cref="RivalDef"/> when the slot is unbound or no roster is assigned;
        /// callers should check <see cref="RivalDef.IsValid"/>.
        /// </summary>
        public RivalDef RivalForSlot(int slot)
        {
            IReadOnlyList<RivalDef> roster = rivalRoster != null ? rivalRoster.Rivals : null;
            if (roster == null || slot < 0 || slot >= _rivalField.Length) return default;
            int index = _rivalField[slot];
            return index >= 0 && index < roster.Count ? roster[index] : default;
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
        /// Stamps the configured season length onto a run, clamped to at least one circuit. Season shape is
        /// configuration rather than run progress — RunSave documents it as a run-start constant it will not
        /// persist — so the director applies this to every RunState it adopts: freshly constructed, restarted,
        /// or resumed from disk. That keeps the inspector the single source of truth and lets a retune take
        /// effect on a run already in flight, instead of the value being frozen into whatever default the
        /// RunState happened to be built with. Pure and static so it's unit-testable without a live scene
        /// (same convention as <see cref="IsDesignatedBoss"/> / <see cref="RulesetForRace"/>). Null-tolerant.
        /// </summary>
        /// <param name="racesPerCircuit">Races in each circuit. 0 (the default) leaves the RunState's own
        /// value alone, so callers that only care about circuit count are unaffected.</param>
        public static RunState ApplySeasonShape(RunState run, int totalCircuits, int racesPerCircuit = 0)
        {
            if (run == null) return null;
            run.TotalCircuits = Mathf.Max(1, totalCircuits);
            if (racesPerCircuit > 0) run.RacesPerCircuit = racesPerCircuit;
            return run;
        }

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
        /// The position-dependent cash a CLEAN finish at <paramref name="position"/> banks right now: the
        /// inverted base+podium payout, scaled by the license stake, then boss-rewarded, plus the capped
        /// sponsor money from equipped economy parts.
        ///
        /// The ORDER is load-bearing and reproduces the shipped sequence exactly: sponsor money is added
        /// LAST, so it is never scaled by <see cref="RunState.StakeMult"/> and never doubled by a boss
        /// ruleset's <see cref="RaceModifier.DoublePayout"/>. Moving it earlier would silently inflate the
        /// economy — see the order test in RunFlowTests.
        ///
        /// Deliberately EXCLUDES the draft-leech payoff: that scales with drafting TIME, not position, so it
        /// isn't a function of where you finish and rides alongside this in ResolveRace.
        ///
        /// Single source of truth. Both the live race resolution and RaceHud's mid-race payout preview call
        /// this, so the number the player is shown while deciding whether to hang back cannot drift from the
        /// number they are actually paid.
        /// </summary>
        public static int CleanFinishPayout(
            int position,
            PayoutTable table,
            IReadOnlyList<PartDef> equippedParts,
            float stakeMult,
            bool bossRace,
            in RaceRuleset ruleset,
            int bossRewardBonus)
        {
            if (table == null) return 0;

            int pay = table.PayoutFor(position, false);

            // Stake 0 — the shipped default and the only reachable value until a stake-select UI lands —
            // yields StakeMult exactly 1.0, so this is skipped and the payout stays byte-for-byte as shipped.
            if (stakeMult > 1f)
                pay = Mathf.CeilToInt(pay * stakeMult);

            if (bossRace)
                pay = ApplyBossReward(pay, ruleset, bossRewardBonus);

            // LAST, and deliberately so: sponsor money is neither stake-scaled nor DoublePayout-doubled.
            int sponsor = 0;
            if (equippedParts != null)
                for (int i = 0; i < equippedParts.Count; i++)
                {
                    PartDef part = equippedParts[i];
                    if (part && part.Category == PartCategory.Economy)
                        sponsor += table.EconomyBonusFor(part.MoneyPerPositionHeld, position);
                }

            return pay + sponsor;
        }

        /// <summary>
        /// Instance wrapper over <see cref="CleanFinishPayout"/> bound to this director's live run, table and
        /// ruleset. This is what both ResolveRace and the HUD preview call.
        /// </summary>
        public int CleanFinishPayoutFor(int position, bool bossRace) =>
            CleanFinishPayout(
                position,
                payoutTable,
                Run?.EquippedParts,
                Run != null ? Run.StakeMult : 1f,
                bossRace,
                _raceManager != null ? _raceManager.Ruleset : default,
                bossRewardBonus);

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
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                ToggleDevMenu();

            if (Phase != RunPhase.Racing || _raceResolved) return;

            // Hold the sector-part bonuses on the sim. The car's out-of-world watchdog can rebuild the
            // sim mid-race, which resets those fields to 1; re-asserting here means a bonus earned
            // earlier survives that recovery instead of quietly disappearing.
            _sectorParts.Reassert();

            // Active item (decision 14): gather this frame's charge, read the single ACTIVATE bind,
            // and hold any live boost on the sim (its write doubles as the watchdog re-assert).
            // While the dev pause is open Update still runs but Time.deltaTime is 0, and the model
            // treats a zero-dt step as a no-op — so a key pressed into a frozen menu neither charges
            // nor deploys anything.
            bool activatePressed = Keyboard.current != null && Keyboard.current[_activateKey].wasPressedThisFrame;
            _activeItem.Tick(Time.deltaTime, activatePressed);

            if (_raceManager == null) return;

            // Decision 15: a retired player must not sit in a dead car watching the field lap for
            // minutes — stamp the running order as final and resolve now. The retired state survives
            // FinishRaceNow (only Racing cars are stamped), so the verdict below still reads RETIRED.
            if (!_raceManager.RaceComplete && _playerCar != null)
            {
                RaceCarStatus mine = _raceManager.GetStatus(_playerCar);
                if (mine != null && mine.State == CarRaceState.Retired)
                    _raceManager.FinishRaceNow();
            }

            if (!_raceManager.RaceComplete) return;
            ResolveRace();
        }

        /// <summary>ESC toggles a minimal dev pause overlay. Freezes time while open, remembering the
        /// pre-pause scale so resuming in the already-paused garage (timeScale 0) doesn't unfreeze it.</summary>
        private void ToggleDevMenu()
        {
            _devMenuOpen = !_devMenuOpen;
            if (_devMenuOpen)
            {
                _preMenuTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }
            else
            {
                Time.timeScale = _preMenuTimeScale;
            }
        }

        // Throwaway IMGUI, like the rest of the dev harness (GarageScreen/RaceHud) — replaced, not ported,
        // when the UI Toolkit pause menu arrives. Draws only while open, on top of whatever HUD is up.
        private void OnGUI()
        {
            if (!_devMenuOpen) return;

            GUI.depth = -1000; // topmost: over the HUD, takes clicks first

            // Dim the scene, then a navy plate + cobalt top accent — the v3 palette, faked in IMGUI.
            GUI.color = new Color(0.02f, 0.03f, 0.05f, 0.72f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);

#if UNITY_EDITOR
            const float w = 260f, h = 394f;   // room for the editor-only slice-test row
#else
            const float w = 240f, h = 138f;
#endif
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.color = new Color(0.11f, 0.137f, 0.188f, 0.98f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = new Color(0.31f, 0.545f, 1f);
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 2f), Texture2D.whiteTexture);
            GUI.color = new Color(0f, 0f, 0f, 0.45f);   // shadowed bottom + right edges (bevel)
            GUI.DrawTexture(new Rect(panel.x, panel.yMax - 1f, panel.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(panel.xMax - 1f, panel.y, 1f, panel.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var title = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 15,
            };
            title.normal.textColor = new Color(0.56f, 0.77f, 1f);
            GUI.Label(new Rect(panel.x, panel.y + 10f, panel.width, 24f), "PAUSED", title);

            float bx = panel.x + 20f, bw = w - 40f;
            if (DevButton(new Rect(bx, panel.y + 44f, bw, 32f), "RESUME",
                    new Color(0.14f, 0.17f, 0.23f), new Color(0.73f, 0.79f, 0.88f)))
                ToggleDevMenu();
            if (DevButton(new Rect(bx, panel.y + 82f, bw, 32f), "NEW RUN",
                    new Color(0.31f, 0.545f, 1f), new Color(0.94f, 0.96f, 1f)))
            {
                _devMenuOpen = false;
                StartNewRun(); // fresh seed at the same stake; saves + reloads to racing (restores timeScale)
            }

#if UNITY_EDITOR
            DrawSliceTestTools(panel, bx, bw);
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// EDITOR-ONLY tuning shortcuts for the doc-08 sector slice. A 24-race season makes "shop for
        /// the right part, then drive three laps" far too slow a loop to answer "is this fun", which is
        /// the question the slice exists to settle — so these collapse the setup to one click.
        /// Compiled out of player builds; a dev cheat must never ship.
        /// </summary>
        private void DrawSliceTestTools(Rect panel, float bx, float bw)
        {
            var caption = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
            };
            caption.normal.textColor = new Color(0.45f, 0.52f, 0.62f);
            GUI.Label(new Rect(panel.x, panel.y + 120f, panel.width, 16f), "— EDITOR ONLY —", caption);

            var devBg = new Color(0.17f, 0.20f, 0.26f);
            var devFg = new Color(0.79f, 0.85f, 0.93f);

            if (DevButton(new Rect(bx, panel.y + 140f, bw, 28f), "EQUIP SECTOR PARTS", devBg, devFg))
                DevEquipSectorParts();

            if (DevButton(new Rect(bx, panel.y + 174f, bw, 28f), "+ $50", devBg, devFg))
                Run.Money += 50;

            // Components have no garage UI yet, so without this every component would sit at level 1
            // forever and the whole ledger would be untestable in play.
            if (DevButton(new Rect(bx, panel.y + 208f, bw, 28f), "+5 LEVELS, ALL COMPONENTS", devBg, devFg))
                DevLevelAllComponents(5);

            if (DevButton(new Rect(bx, panel.y + 242f, bw, 28f), "FINISH RACE NOW", devBg, devFg))
            {
                _devMenuOpen = false;
                Time.timeScale = _preMenuTimeScale;
                if (_raceManager != null) _raceManager.FinishRaceNow();
            }

            // 24-race season tuning aids (doc 08 open question 6): jump a whole circuit ahead to
            // sample late-season difficulty without driving there, and fast-forward the clock so a
            // three-lap payout check doesn't take three real laps.
            bool onLastCircuit = Run.IsFinalCircuit;
            if (DevButton(new Rect(bx, panel.y + 276f, bw, 28f),
                    onLastCircuit ? "NEXT CIRCUIT (AT LAST)" : $"NEXT CIRCUIT >> ({Run.CircuitIndex + 2}/{Run.TotalCircuits})",
                    devBg, devFg) && !onLastCircuit)
            {
                _devMenuOpen = false;
                DevJumpToNextCircuit();
            }

            if (DevButton(new Rect(bx, panel.y + 310f, bw, 28f),
                    _devFastForward ? "TIME x4  (CLICK FOR x1)" : "TIME x1  (CLICK FOR x4)", devBg, devFg))
            {
                // Applied on resume: the menu itself holds timeScale 0 and restores _preMenuTimeScale.
                _devFastForward = !_devFastForward;
                _preMenuTimeScale = _devFastForward ? 4f : 1f;
            }

            // Live sector-scoring readout, so a test drive can be verified without adding print
            // statements: what the parts have paid and what they've done to the car.
            var readout = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
            readout.normal.textColor = new Color(0.55f, 0.75f, 0.55f);
            GUI.Label(new Rect(panel.x, panel.y + 346f, panel.width, 16f),
                $"sector $ {_sectorParts.MoneyEarned}   grip x{_sectorParts.State.GripMult:0.00}   pow x{_sectorParts.State.PowerMult:0.00}",
                readout);

            var slots = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
            slots.normal.textColor = new Color(0.45f, 0.52f, 0.62f);
            StatSummary.Stats stats = _playerCar != null && _playerCar.SpecAsset != null
                ? StatSummary.Compute(_playerCar.SpecAsset.Spec)
                : default;
            GUI.Label(new Rect(panel.x, panel.y + 364f, panel.width, 16f),
                $"parts {Run.EquippedParts.Count}/{Run.EffectiveEquipSlots}   " +
                $"P{stats.Power:0} G{stats.Grip:0} W{stats.Weight:0} D{stats.Durability:0}", slots);
        }

        // Dev fast-forward toggle (x4). Lives on the director, not Time.timeScale directly, because the
        // ESC menu freezes/restores timeScale around itself — the toggle just changes what "resumed" is.
        private bool _devFastForward;

        /// <summary>
        /// EDITOR-ONLY: abandon the current race and start the NEXT circuit's first race, free of
        /// charge — no payout, no life, no summary. Exists to sample circuit-6 difficulty without
        /// driving fifteen races (doc 08 open question 6). Deliberately mirrors StartNextRace's
        /// transition (phase + timeScale + save + scene reload) so nothing downstream can tell the
        /// difference between arriving here honestly and jumping.
        /// </summary>
        private void DevJumpToNextCircuit()
        {
            if (Run.IsFinalCircuit) return;
            Run.CircuitIndex += 1;
            Run.RaceIndex = 0;
            Run.InRaceEarnings = 0;      // the abandoned race's sector income does not bank
            LastRaceSummary = $"DEV JUMP — circuit {Run.CircuitIndex + 1}/{Run.TotalCircuits}.";
            SetPhase(RunPhase.Racing);
            Time.timeScale = _devFastForward ? 4f : 1f;
            Save();
            ReloadRaceScene();
        }

        /// <summary>
        /// EDITOR-ONLY: raises every component by <paramref name="levels"/>, free, and re-bakes the car
        /// so the change is felt immediately rather than at the next scene load. Bypasses the shop
        /// deliberately — Blueprints now have to be ROLLED before they can be bought, so reaching a
        /// deep build in play means shopping for it; this gets there in one click for physics testing.
        /// Re-applies the run's carried durability afterwards, since re-baking rebuilds the sim.
        /// </summary>
        private void DevLevelAllComponents(int levels)
        {
            foreach (CarComponentInfo info in CarComponentCatalog.All)
            {
                int index = (int)info.Component;
                Run.ComponentLevels[index] =
                    CarComponentCatalog.ClampLevel(Run.LevelOf(info.Component) + levels);
            }

            if (_playerCar != null)
            {
                ApplyEquippedParts();
                if (_playerCar.Sim != null) _playerCar.Sim.SetDurability(Run.CarDurability);
                _sectorParts.Reassert();   // the rebuilt sim lost the race-scoped sector bonuses
            }
            Debug.Log($"[RunDirector] Components +{levels}. Engine L{Run.LevelOf(CarComponent.Engine)}, " +
                      $"Tyres L{Run.LevelOf(CarComponent.Tyres)}.", this);
        }

        /// <summary>
        /// Grants and slots every sector-rule part in the pool, up to the run's free slots. Deliberately
        /// does NOT re-bake the spec: sector parts carry no SpecMods, and calling ApplyEquippedParts
        /// mid-race would rebuild the sim and wipe the car's accumulated damage. The runner reads
        /// <see cref="RunState.EquippedParts"/> fresh at every sector line, so parts slotted mid-race
        /// simply start scoring at the next one.
        /// </summary>
        private void DevEquipSectorParts()
        {
            if (partPool == null || partPool.Parts == null)
            {
                Debug.LogWarning("[RunDirector] No part pool — run Shitboxer/Build Meta Assets first.", this);
                return;
            }

            int slotted = 0;
            foreach (PartDef part in partPool.Parts)
            {
                if (part == null || !part.HasSectorRules) continue;
                if (!Run.Owns(part)) Run.OwnedParts.Add(part);
                if (!Run.IsEquipped(part) && Run.HasFreeSlot && Run.Equip(part)) slotted++;
            }
            Debug.Log($"[RunDirector] Slotted {slotted} sector part(s); " +
                      $"{Run.EquippedParts.Count}/{Run.EffectiveEquipSlots} slots used.", this);
        }
#endif

        /// <summary>A flat v3-style IMGUI button: solid fill + a lit top edge + centred bold text.</summary>
        private static bool DevButton(Rect rect, string label, Color bg, Color fg)
        {
            Color prev = GUI.color;
            GUI.color = bg;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.15f);   // lit top + left edges
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1f, rect.height), Texture2D.whiteTexture);
            GUI.color = new Color(0f, 0f, 0f, 0.4f);    // shadowed bottom + right edges
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), Texture2D.whiteTexture);
            GUI.color = prev;

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 12,
            };
            style.normal.textColor = fg;
            GUI.Label(rect, label, style);

            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
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

            // Rivalry memory: fold what the observation layer saw into the persistent career model. Sits
            // here for the same reason as the lap record — before any early-return branch — so the last
            // race of a dying run still teaches the rivals who were in it.
            RecordRivalEncounters(me);

            bool eliminated = me.State == CarRaceState.Eliminated;
            // Decision 15: the car was wrecked outright — durability hit zero mid-race. Same failure
            // price as elimination (a life and the position payout), distinct summary so the player
            // knows WHY the race ended.
            bool retired = me.State == CarRaceState.Retired;
            // Flat top-N: a boss race asks for the same finish on every circuit. This used to shrink one
            // slot per circuit (Max(1, BossTopN - CircuitIndex)), which quietly escalated an already
            // unannounced gate into "win or lose a life" by circuit 3 — the rule moved under the player
            // with nothing on screen saying so, and cost a life in playtest.
            bool bossFailed = !eliminated && !retired && Run.IsBossRace && me.Position > Run.BossTopN;
            bool failed = eliminated || retired || bossFailed;

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
            if (failed)
            {
                payout = payoutTable.EliminationConsolation;
            }
            else
            {
                // Position cash + stake scaling + boss reward + capped sponsor money, in that exact order
                // (see CleanFinishPayoutFor). Shared verbatim with RaceHud's mid-race preview so the figure
                // the player weighs their hang-back decision against is the figure they're actually paid.
                payout = CleanFinishPayoutFor(me.Position, bossRace);

                // The interlude free-repair is a SIDE EFFECT of a clean boss finish, not part of the payout,
                // so it stays here rather than in the shared (pure-ish) payout helper. Withheld when the
                // ruleset says NoRepairAfter (RaceRuleset.Boss does, so its damage rides into the garage).
                if (bossRace && GrantsFreeRepair(bossRace, _raceManager.Ruleset))
                    Run.CarDurability = 1f;
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

            // Sector-part income (doc 08 decision 9): banked whatever the finish, because it was earned
            // sector by sector during the race rather than awarded for the result. That is the whole
            // point of the channel — it lets a build pay off without the player having to finish well,
            // so a strong car is no longer punished by the inverted position payout. Stays exactly 0
            // for a run that owns no sector-rule parts, so the shipped economy is untouched.
            int sectorEarnings = Run.InRaceEarnings;
            Run.InRaceEarnings = 0;

            Run.Money += payout + leechBonus + sectorEarnings;
            int totalPay = payout + leechBonus + sectorEarnings;

            if (failed)
            {
                Run.Lives -= 1;
                LastRaceSummary = (retired
                    ? $"P{me.Position} — RETIRED, car destroyed. +${totalPay}, -1 life. Repair before the retry."
                    : eliminated
                        ? $"P{me.Position} — ELIMINATED (missed the cutoff). +${totalPay}, -1 life."
                        : $"P{me.Position} — boss race demands top {Run.BossTopN}. +${totalPay}, -1 life. Retry it.")
                    + fragileNote;

                if (Run.Lives <= 0)
                {
                    // Run's over: refund every owned Cashout part's Price into the final wallet.
                    int cashout = Run.CashoutRefundTotal();
                    Run.Money += cashout;
                    if (cashout > 0) LastRaceSummary += $" Cashout parts refunded +${cashout}.";
                    SetPhase(RunPhase.RunOver);
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
                        SetPhase(RunPhase.RunComplete);
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

        // The crippled line of decision 15: at durability 0.5 the default chassis runs at half pace.
        // Fragile parts break at or below it — the same threshold the Gold enhancement will read
        // ("removed if durability drops below 50%"), so "heavily damaged" means ONE thing everywhere.
        // (Pre-rework this was MinDurability + 0.05 = 0.45 against the old 0.4 floor; near-identical
        // trigger point, now anchored to a threshold that exists for its own reasons.)
        private const float FragileBreakDurabilityThreshold = 0.5f;

        /// <summary>
        /// Fragile parts (PartCondition.Fragile) are strong but breakable: if the car finished the race
        /// crippled — at or below the decision-15 half-durability line, read from the just-captured
        /// Run.CarDurability — ONE equipped Fragile part shakes loose and is destroyed, removed from
        /// both EquippedParts and OwnedParts (parts are unique, so dropping the PartDef is a clean delete).
        /// At most one break per race. Returns a summary suffix noting the loss, or "" if nothing broke.
        /// </summary>
        private string BreakOneFragilePartOnHeavyDamage()
        {
            bool heavyDamage = Run.CarDurability <= FragileBreakDurabilityThreshold;
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

            // Fire the phase change LAST — AFTER the shop is stocked and the cash settled — so the
            // retained-mode garage UI (GarageUiHost builds its ViewModel on PhaseChanged and reads the
            // shop ONCE) sees a populated shop and post-interest money, not the empty/pre-interest state.
            // The old IMGUI garage re-read every frame, so this ordering never mattered before.
            SetPhase(RunPhase.Garage);
        }

        /// <summary>Garage Buy button.</summary>
        public bool BuyOffer(PartDef part)
        {
            bool bought = Shop.TryBuy(part, Run);
            if (bought) { RebakeCar(); Save(); }
            return bought;
        }

        /// <summary>Garage SELL button: half the price back, and the slot with it.</summary>
        public bool SellPart(PartDef part)
        {
            bool sold = Shop.TrySell(part, Run);
            if (sold) { RebakeCar(); Save(); }
            return sold;
        }

        /// <summary>
        /// Garage Buy-pack button. Saves on success for the same reason the crate does: the money is
        /// gone the moment this returns true, so the pending pick must survive a quit.
        /// </summary>
        public bool BuyPack(int packIndex)
        {
            bool bought = Shop.TryBuyPack(packIndex, partPool ? partPool.Parts : null, Run);
            if (bought) Save();
            return bought;
        }

        /// <summary>Takes one component from an open components pack (already paid for).</summary>
        public bool TakeComponent(CarComponent component)
        {
            bool took = Shop.TryTakeComponent(component, Run);
            if (took) { RebakeCar(); Save(); }
            return took;
        }

        /// <summary>
        /// Garage Blueprint button: buy one component level off the shelf. Goes through the shop, not
        /// straight to the run — a Blueprint is only buyable if it turned up in this visit's roll.
        /// </summary>
        public bool BuyBlueprint(CarComponent component)
        {
            bool bought = Shop.TryBuyBlueprint(component, Run);
            if (bought) { RebakeCar(); Save(); }
            return bought;
        }

        /// <summary>
        /// Re-bakes the player's spec after the build changes in the garage, so the stat bars and the
        /// car itself agree with what was just bought or sold without waiting for the next scene load.
        ///
        /// Re-applies the run's carried durability afterwards: <see cref="ApplyEquippedParts"/> goes
        /// through SetSpec, which constructs a fresh sim at full durability, and a battered car must
        /// stay battered until it is paid for. No-op between scenes, when there is no car to bake onto.
        /// </summary>
        private void RebakeCar()
        {
            if (_playerCar == null || _playerCar.SpecAsset == null || BaseSpec == null) return;

            // Bake from the ORIGINAL authored spec, never from the currently-baked one — otherwise
            // every purchase would compound on top of the last bake and a sold part's bonus would
            // never come back off.
            _playerCar.SetSpec(NewRuntimeSpecAsset(BaseSpec));
            ApplyEquippedParts();
            if (_playerCar.Sim != null) _playerCar.Sim.SetDurability(Run.CarDurability);
        }

        private static VehicleSpecAsset NewRuntimeSpecAsset(VehicleSpec spec)
        {
            var asset = ScriptableObject.CreateInstance<VehicleSpecAsset>();
            asset.name = "PlayerSpec_Runtime";
            asset.Spec = SpecModApplier.Clone(spec);
            return asset;
        }

        /// <summary>Garage Reroll button — escalating cost handled by ShopLogic.</summary>
        public bool RerollShop()
        {
            bool rerolled = Shop.TryReroll(partPool ? partPool.Parts : null, Run);
            if (rerolled) Save();
            return rerolled;
        }

        /// <summary>
        /// Garage Buy-crate button. Saves on success — and that save is exactly why the drawn contents live
        /// on RunState rather than in the shop: the money is gone the moment this returns true, so the pick
        /// has to be able to survive a quit (see RunState.CrateContents).
        /// </summary>
        public bool BuyCrate()
        {
            int draws = crateDrawCount + TeamUpgrades.ExtraCrateDraws(Run);
            bool bought = Shop.TryBuyCrate(partPool ? partPool.Parts : null, Run, cratePrice, draws);
            if (bought) Save();
            return bought;
        }

        /// <summary>
        /// Garage Buy-upgrade button: a permanent, run-long team upgrade (doc 03's vouchers). Saves on
        /// success — the upgrade is owned from this moment and must survive a quit.
        /// </summary>
        public bool BuyUpgrade(TeamUpgrade upgrade)
        {
            bool bought = Shop.TryBuyUpgrade(upgrade, Run);
            if (bought) Save();
            return bought;
        }

        /// <summary>How many parts a crate draws for this run: the authored base plus any Bulk Buyer.</summary>
        public int CrateDrawCount => crateDrawCount + TeamUpgrades.ExtraCrateDraws(Run);

        /// <summary>Garage crate-pick button: take one drawn part, the rest are discarded.</summary>
        public bool TakeFromCrate(PartDef part)
        {
            bool took = Shop.TryTakeFromCrate(part, Run);
            if (took) Save();
            return took;
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
            SetPhase(RunPhase.Racing);
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
            Run = ApplySeasonShape(
                new RunState { Money = startingMoney, Seed = RollSeed(), StakeLevel = stake, ChassisId = Run.ChassisId },
                totalCircuits, racesPerCircuit);
            LastRaceSummary = "";
            SetPhase(RunPhase.Racing);
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
        /// Folds this race's observations into the persistent rivalry memory, then advances the decay clock
        /// and saves. Maps each rival's per-race observation key back to its permanent roster id — the key
        /// is only unique within one race, and keying memory by it would scramble histories between races.
        ///
        /// Deliberately tolerant: no observer, no roster, or no rivals simply means nothing is learned this
        /// race. Memory is enrichment, never a precondition for the run resolving.
        /// </summary>
        private void RecordRivalEncounters(RaceCarStatus me)
        {
            if (Meta == null) Meta = new MetaProgress();
            Meta.rivalMemories ??= new List<RivalMemory>();
            Meta.playerStyle ??= new PlayerStyleProfile();

            var observerHost = FindFirstObjectByType<RaceObserverHost>();
            if (observerHost == null || me == null) return;

            RaceObservationSummary race = observerHost.Summarize(me.Position);
            if (race.Rivals == null || race.Rivals.Length == 0) return;

            // Advance the clock FIRST so this race's fold stamps the ordinal it belongs to, and the decay
            // applied on the next read counts from here.
            Meta.careerRaces++;

            // Tier 1: the shared style profile every rival reads.
            Meta.playerStyle = RivalMemoryStore.GetStyle(Meta.playerStyle, Meta.careerRaces, Meta.styleLastFoldedRace);
            RivalMemoryStore.FoldStyle(Meta.playerStyle, race);
            Meta.styleLastFoldedRace = Meta.careerRaces;

            // Tier 2: personal history, per rival.
            //
            // Folded in a CANONICAL ORDER (by roster id), not in whatever order the observer happens to
            // hold them. PhysX decides collision callback ordering and it can differ between a client and a
            // headless server, so folding in observation order would let float summation drift the two
            // models apart — which the multiplayer constraint forbids and which would be near-impossible to
            // diagnose after the fact.
            var folds = new List<(string id, RivalEncounterSummary enc)>(race.Rivals.Length);
            foreach (RivalEncounterSummary enc in race.Rivals)
            {
                RivalDef def = RivalForSlot(SlotForKey(enc.RivalKey));
                if (!def.IsValid) continue;
                folds.Add((def.id, enc));
            }
            folds.Sort((x, y) => string.CompareOrdinal(x.id, y.id));

            long stamp = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach ((string id, RivalEncounterSummary enc) in folds)
                RivalMemoryStore.Fold(Meta.rivalMemories, id, enc, Meta.careerRaces, stamp);

            MetaProgress.Save(Meta);
        }

        /// <summary>Inverse of <see cref="RivalField.KeyForSlot"/>. -1 for the player or an unknown key.</summary>
        private static int SlotForKey(int rivalKey) => rivalKey > 0 ? rivalKey - 1 : -1;

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
        private int VisitSeed() => MixSeed(17);

        /// <summary>
        /// Per-race starting-grid seed. Same mix as <see cref="VisitSeed"/> but off a different base, so
        /// each race gets its own deterministic grid AND the grid can't correlate with that race's shop
        /// stock — one stream driving both would tie "what I'm offered" to "where I start" for no reason.
        /// </summary>
        private int GridSeed() => MixSeed(29);

        /// <summary>
        /// Deterministic hash of the run seed with the circuit/race indices, off a caller-supplied base so
        /// separate systems get separate streams. Multiplicative rather than additive: a plain sum collides
        /// (circuit 1/race 0 vs circuit 0/race 1 would draw identically).
        /// </summary>
        private int MixSeed(int seedBase)
        {
            unchecked
            {
                int h = seedBase;
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

        /// <summary>
        /// Which track a given race runs on: rotates through <paramref name="scenes"/> so consecutive
        /// races differ and a 5-race run sees every layout. Pure and static so the rotation is
        /// unit-testable without a live scene (same convention as <see cref="ApplySeasonShape"/> /
        /// <see cref="BotStrengthFor"/>). Falls back to <paramref name="fallback"/> — the active scene —
        /// when unconfigured or when a slot is blank, which reproduces the single-track behaviour
        /// rather than throwing on a scene that isn't in Build Settings.
        /// </summary>
        public static string SceneForRace(int raceNumber, string[] scenes, string fallback)
        {
            if (scenes == null || scenes.Length == 0) return fallback;
            int index = ((raceNumber % scenes.Length) + scenes.Length) % scenes.Length; // negatives wrap too
            return string.IsNullOrWhiteSpace(scenes[index]) ? fallback : scenes[index];
        }

        /// <summary>
        /// Loads the track for the race that is about to run. Both callers reach here with RaceIndex
        /// already pointing at that race (a retry leaves it alone, so a failed race is retried on the
        /// same track). The RunRig survives the load — it's a DontDestroyOnLoad singleton — and
        /// re-binds to the new scene's RaceManager via OnSceneLoaded.
        /// </summary>
        private void ReloadRaceScene()
        {
            string active = SceneManager.GetActiveScene().name;
            int raceNumber = Run != null ? Run.RaceNumber : 0;
            string next = SceneForRace(raceNumber, raceScenes, active);

            // A layout that was never generated — or generated but not registered in Build Settings —
            // must not end a run in an exception two races in. Stay on the current track and say why.
            if (next != active && !Application.CanStreamedLevelBeLoaded(next))
            {
                Debug.LogWarning(
                    $"[RunDirector] Track scene '{next}' is not in Build Settings — racing '{active}' again. " +
                    "Run 'Shitboxer/Build Race Scenes' to generate the layouts and register them.", this);
                next = active;
            }

            SceneManager.LoadScene(next);
        }
    }
}
