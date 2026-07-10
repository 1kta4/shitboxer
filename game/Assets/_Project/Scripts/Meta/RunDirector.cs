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
    /// (or any elimination) costs a life and the same race is retried.
    /// </summary>
    public class RunDirector : MonoBehaviour
    {
        public static RunDirector Instance { get; private set; }

        [SerializeField] private PartPool partPool;
        [SerializeField] private PayoutTable payoutTable = new PayoutTable();
        [Tooltip("Cash a fresh run starts with.")]
        [SerializeField] private int startingMoney = 5;

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

            Run.Money = startingMoney;
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

            RaceCarStatus me = _raceManager.GetStatus(_playerCar);
            if (me == null)
            {
                Debug.LogError("[RunDirector] Player car is not registered with the RaceManager.", this);
                return;
            }

            bool eliminated = me.State == CarRaceState.Eliminated;
            bool bossFailed = !eliminated && Run.IsBossRace && me.Position > Run.BossTopN;
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
                    : $"P{me.Position} — boss race demands top {Run.BossTopN}. +${totalPay}, -1 life. Retry it.";

                if (Run.Lives <= 0)
                {
                    Phase = RunPhase.RunOver;
                    Time.timeScale = 0f;
                    return;
                }
                // RaceIndex unchanged: the same race is retried after the garage.
            }
            else
            {
                LastRaceSummary = $"P{me.Position} — survived. +${totalPay}.";
                Run.RaceIndex += 1;
                if (Run.RunComplete)
                {
                    Phase = RunPhase.RunComplete;
                    Time.timeScale = 0f;
                    return;
                }
            }

            OpenGarage();
        }

        private void OpenGarage()
        {
            Phase = RunPhase.Garage;
            Time.timeScale = 0f;
            Shop.BeginVisit(partPool ? partPool.Parts : null, Run);
        }

        /// <summary>Garage Buy button.</summary>
        public bool BuyOffer(PartDef part) => Shop.TryBuy(part, Run);

        /// <summary>Garage Reroll button — escalating cost handled by ShopLogic.</summary>
        public bool RerollShop() => Shop.TryReroll(partPool ? partPool.Parts : null, Run);

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
            Run = new RunState { Money = startingMoney };
            LastRaceSummary = "";
            Phase = RunPhase.Racing;
            Time.timeScale = 1f;
            ReloadRaceScene();
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
