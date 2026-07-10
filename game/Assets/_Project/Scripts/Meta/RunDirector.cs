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

        /// <summary>Live state of the current run.</summary>
        public RunState Run { get; private set; } = new RunState();

        /// <summary>Shop rules instance; reroll cost resets each garage visit.</summary>
        public ShopLogic Shop { get; } = new ShopLogic();

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
            ApplyDifficulty();

            // Persistent wear carries ACROSS races within a run: a freshly-rebuilt sim resets to full
            // durability (and ApplyEquippedParts may have just rebuilt it via SetSpec), so re-apply the
            // run's carried value here — after the other Apply* calls — so a battered car stays battered
            // until the player pays to repair it in the garage.
            if (_playerCar.Sim != null)
                _playerCar.Sim.SetDurability(Run.CarDurability);
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
        private const float MaxDifficultyScalar = 1.3f;    // ceiling of the bot-commitment band the director will request
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
            // reading as cheating; the per-bot rubber-band clamps the final result regardless.
            float scalar = 1f + (Run.DifficultyMult - 1f) * DifficultyScalarGain;
            _raceManager.SetDifficultyScalar(Mathf.Clamp(scalar, 1f, MaxDifficultyScalar));

            // Tighten the survival window per circuit off the authored base (a fresh RaceManager
            // resets cutoffFraction each scene reload, so this reads the shipped value every time),
            // clamped to a floor so the gate stays survivable on the hardest circuits.
            float cutoff = _raceManager.CutoffFraction - CutoffTightenPerCircuit * Run.CircuitIndex;
            _raceManager.SetCutoffFraction(Mathf.Max(MinCutoffFraction, cutoff));
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

            RaceCarStatus me = _raceManager.GetStatus(_playerCar);
            if (me == null)
            {
                Debug.LogError("[RunDirector] Player car is not registered with the RaceManager.", this);
                return;
            }

            bool eliminated = me.State == CarRaceState.Eliminated;
            // The boss cushion tightens with the season: the required top-N shrinks by one slot per
            // circuit (never below 1) so later bosses demand a sharper finish. RunState is untouched —
            // this is a per-circuit view of Run.BossTopN, computed only here.
            int effectiveBossTopN = Mathf.Max(1, Run.BossTopN - Run.CircuitIndex);
            bool bossFailed = !eliminated && Run.IsBossRace && me.Position > effectiveBossTopN;
            bool failed = eliminated || bossFailed;

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
            }
            Run.Money += payout + economyBonus;
            int totalPay = payout + economyBonus;

            if (failed)
            {
                Run.Lives -= 1;
                LastRaceSummary = eliminated
                    ? $"P{me.Position} — ELIMINATED (missed the cutoff). +${totalPay}, -1 life."
                    : $"P{me.Position} — boss race demands top {effectiveBossTopN}. +${totalPay}, -1 life. Retry it.";

                if (Run.Lives <= 0)
                {
                    Phase = RunPhase.RunOver;
                    Time.timeScale = 0f;
                    ClearSave(); // the run is dead — don't resume it next launch
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
                        LastRaceSummary = $"P{me.Position} — survived. +${totalPay}. SEASON CLEARED!";
                        Phase = RunPhase.RunComplete;
                        Time.timeScale = 0f;
                        ClearSave(); // season won — the finished run doesn't resume
                        return;
                    }
                    Run.CircuitIndex += 1;
                    Run.RaceIndex = 0;
                    LastRaceSummary =
                        $"P{me.Position} — boss down. +${totalPay}. Circuit {Run.CircuitIndex + 1}/{Run.TotalCircuits} begins.";
                }
                else
                {
                    LastRaceSummary = $"P{me.Position} — survived. +${totalPay}.";
                }
            }

            OpenGarage();
        }

        private void OpenGarage()
        {
            Phase = RunPhase.Garage;
            Time.timeScale = 0f;
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

        private int ComputeRepairCost()
        {
            float wear = 1f - Run.CarDurability;                 // 0 (pristine) .. (1 - MinDurability) at the floor
            if (wear <= 0f) return 0;
            float span = 1f - VehicleSim.MinDurability;          // total wear span from pristine to the floor
            float t = span > 0f ? Mathf.Clamp01(wear / span) : 1f;
            return Mathf.Max(1, Mathf.CeilToInt(fullRepairCost * t)); // any wear costs at least $1
        }

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

        /// <summary>Run-over / run-complete screens: reset everything and go again.</summary>
        public void StartNewRun()
        {
            Run = new RunState { Money = startingMoney, Seed = RollSeed() };
            LastRaceSummary = "";
            Phase = RunPhase.Racing;
            Time.timeScale = 1f;
            Save(); // overwrite any previous save with the fresh, freshly-seeded run
            ReloadRaceScene();
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
