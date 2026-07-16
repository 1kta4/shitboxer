using System.Collections.Generic;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>Where a car currently stands in the race lifecycle.</summary>
    public enum CarRaceState
    {
        Racing,
        Finished,   // crossed the line inside the survival cutoff (winner included)
        Eliminated, // finished outside the cutoff, or timed out before finishing
    }

    /// <summary>Live race bookkeeping for one car. Read-only outside the Race assembly.</summary>
    public sealed class RaceCarStatus
    {
        public VehicleController Car { get; internal set; }
        public CarRaceState State { get; internal set; } = CarRaceState.Racing;

        /// <summary>Continuous distance travelled along the loop since the start line (m). Negative on the grid.</summary>
        public float TotalDistanceM { get; internal set; }

        /// <summary>Current lap, 1-based for display. Clamped to the race lap count.</summary>
        public int Lap { get; internal set; } = 1;

        /// <summary>1-based standing among all cars (finishers by time, then by distance).</summary>
        public int Position { get; internal set; }

        /// <summary>Race clock at the moment the car completed the final lap; negative if it never finished.</summary>
        public float FinishTimeS { get; internal set; } = -1f;

        /// <summary>True if the car finished inside the survival cutoff window.</summary>
        public bool PassedCutoff { get; internal set; }

        /// <summary>Seconds of the car's most recently completed (validated) lap; negative until the first lap validates.</summary>
        public float LastLapTimeS { get; internal set; } = -1f;

        /// <summary>Fastest validated lap so far (seconds); negative until the first lap validates.</summary>
        public float BestLapTimeS { get; internal set; } = -1f;

        internal float LastProgressM;

        /// <summary>Race clock at which the current lap's timing began — 0 (the green flag) for lap 1, then the clock captured at each validated lap. Last/Best lap are derived from it.</summary>
        internal float LapStartTimeS;

        /// <summary>Laps completed by guarded net forward progress around the loop — the gate the finish counts.</summary>
        internal int ValidatedLaps;
    }

    /// <summary>
    /// Pure lap-timing math shared by the referee and its unit tests — no engine, scene or clock state,
    /// so a headless server steps it identically. All times are race-clock seconds. Purely a readout/record
    /// concern: it has no effect on driving, checkpoint/lap validation, or the economy.
    /// </summary>
    public static class LapTiming
    {
        /// <summary>Elapsed seconds of a lap: the race clock now minus when the lap's timing began. Clamped non-negative (zero during the pre-green countdown).</summary>
        public static float Elapsed(float nowS, float lapStartS) => Mathf.Max(0f, nowS - lapStartS);

        /// <summary>The new fastest lap given the prior best (negative = none yet) and a just-completed lap: keeps the minimum, and the first valid lap always becomes the best.</summary>
        public static float Fold(float bestSoFarS, float lapTimeS) =>
            bestSoFarS < 0f || lapTimeS < bestSoFarS ? lapTimeS : bestSoFarS;
    }

    /// <summary>
    /// Pure lap-counting math shared by the referee and its unit tests. A car's guarded net forward
    /// distance is measured in metres from the start/finish line (arc-length 0), so every whole
    /// multiple of the loop length is one completed lap. No engine, scene or clock state.
    /// </summary>
    public static class LapProgress
    {
        /// <summary>Whole laps completed for a guarded forward distance on a loop of the given length
        /// (a non-positive length or negative distance yields 0). Distance is from the line, so
        /// N*lapLength = N laps.</summary>
        public static int CompletedLaps(float totalDistanceM, float lapLengthM) =>
            lapLengthM <= 0f ? 0 : Mathf.Max(0, Mathf.FloorToInt(totalDistanceM / lapLengthM));
    }

    /// <summary>
    /// Referee for one race: registers cars, counts laps along the TrackPath, tracks live
    /// positions and finishing order, and enforces the survival gate — once the winner
    /// finishes, everyone else must finish within CutoffFraction of the winner's time or
    /// they are ELIMINATED. All state is exposed read-only for the HUD.
    ///
    /// Laps are <b>distance-gated</b>: continuous net forward progress along the loop is accumulated
    /// (guarded — a jump too big for one physics step is a teleport/mis-projection and is rejected, so
    /// it can never inject distance or fake a lap), measured in metres from the start/finish line, and
    /// a lap counts each time that distance crosses a whole loop-length. Anti-cut survives via the
    /// same guard (a real course-cut adds no distance, so it cannot manufacture a lap), while a HUMAN
    /// driving off the racing line — wide lines, cut corners, weaving — can no longer be stranded the
    /// way the former ordered-checkpoint ring stranded it (that ring hard-reset a car's lap progress
    /// on every projection swing, so human laps almost never validated while on-rails bots were fine).
    /// </summary>
    public class RaceManager : MonoBehaviour
    {
        // A single physics step can't move a car more than a few metres of track; a larger jump in
        // projected progress is a teleport (BotDriver flip/reset) or a nearest-segment mis-snap. Such
        // a step is never trusted for distance or checkpoint/lap credit.
        private const float MaxPlausibleStepM = 10f;

        [SerializeField] private TrackPath trackPath;
        [SerializeField] private List<VehicleController> cars = new List<VehicleController>();
        [Min(1)]
        [SerializeField] private int totalLaps = 3;
        [Tooltip("Survival gate: after the winner finishes, others must finish within winnerTime * (1 + this) or be eliminated.")]
        [Range(0.01f, 1f)]
        [SerializeField] private float cutoffFraction = 0.15f;
        [Tooltip("Grid-frozen countdown before the green flag, seconds.")]
        [SerializeField] private float countdownS = 3f;
        [Tooltip("Global bot-commitment scalar — a hook for a future per-circuit difficulty ramp. 1 = shipped balance; each bot multiplies it into its own rubber-band, which BotBrain then clamps subtle so it never reads as cheating. Leave at 1 for now.")]
        [Range(0.5f, 1.5f)]
        [SerializeField] private float difficultyScalar = 1f;

        [Tooltip("ON (default): cars are dealt to the scene's grid slots by a seeded shuffle, so the player doesn't start on pole every single race — which would make winning the default and gut the push-to-win-vs-hang-back-to-farm decision. Turn OFF to pin the scene's authored grid (player on pole) when you want a repeatable slot for debugging. Only ever applies when the run layer pushes a seed via SetGridSeed; a bare race scene is unaffected either way.")]
        [SerializeField] private bool shuffleGrid = true;

        private readonly List<RaceCarStatus> _statuses = new List<RaceCarStatus>();
        private readonly List<RaceCarStatus> _leaderboard = new List<RaceCarStatus>();
        private float _raceTime;
        private bool _greenFlag;
        private bool _running;

        /// <summary>Grid seed pushed by the run layer; null means nobody set one — leave the grid alone.</summary>
        private int? _gridSeed;
        // Boss / special-race state applied by SetRuleset. Default false / None reproduces the standard
        // race exactly, so a race left on the standard ruleset behaves byte-for-byte as before it existed.
        private bool _isBoss;
        private RaceModifier _modifiers = RaceModifier.None;

        public int TotalLaps => totalLaps;
        public float CutoffFraction => cutoffFraction;

        /// <summary>True when the active ruleset marks this as a boss race. Default false (standard race).</summary>
        public bool IsBossRace => _isBoss;

        /// <summary>Special rules the active ruleset layers on the standard format. Default None (standard race).</summary>
        public RaceModifier Modifiers => _modifiers;

        /// <summary>True if every bit of <paramref name="modifier"/> is active on this race.</summary>
        public bool HasModifier(RaceModifier modifier) => (_modifiers & modifier) == modifier;

        /// <summary>The race's current ruleset, reconstructed from live state (laps, cutoff, boss, modifiers).</summary>
        public RaceRuleset Ruleset => new RaceRuleset
        {
            Laps = totalLaps,
            CutoffFraction = cutoffFraction,
            IsBoss = _isBoss,
            Modifiers = _modifiers,
        };

        public float RaceTimeS => _raceTime;
        public float TrackLengthM => trackPath ? trackPath.TotalLength : 0f;

        /// <summary>
        /// Elapsed seconds of <paramref name="status"/>'s CURRENT (in-progress) lap: the race clock now
        /// minus when this lap's timing began. Zero during the countdown and clamped non-negative; a
        /// finished car's value stops advancing with the clock. Additive readout — no effect on the race.
        /// </summary>
        public float CurrentLapTimeS(RaceCarStatus status) =>
            status == null ? 0f : LapTiming.Elapsed(_raceTime, status.LapStartTimeS);

        /// <summary>All registered cars, in registration order.</summary>
        public IReadOnlyList<RaceCarStatus> Cars => _statuses;

        /// <summary>All registered cars sorted by current position (1 first). Re-sorted every physics step.</summary>
        public IReadOnlyList<RaceCarStatus> Leaderboard => _leaderboard;

        /// <summary>Global bot-commitment scalar (default 1). Bots fold it into their rubber-band; BotBrain clamps the result subtle. A future per-circuit ramp can raise it to lift the whole field.</summary>
        public float DifficultyScalar => difficultyScalar;

        public bool WinnerFinished { get; private set; }

        /// <summary>Winner's race time; negative until someone finishes.</summary>
        public float WinnerFinishTimeS { get; private set; } = -1f;

        /// <summary>Race clock everyone else must finish by; negative until the winner finishes.</summary>
        public float CutoffDeadlineS => WinnerFinished ? WinnerFinishTimeS * (1f + cutoffFraction) : -1f;

        /// <summary>True once every car has either finished or been eliminated.</summary>
        public bool RaceComplete { get; private set; }

        /// <summary>Wires the race up (used by editor builders — sets serialized fields only).</summary>
        public void Configure(TrackPath path, List<VehicleController> raceCars, int laps, float cutoff)
        {
            trackPath = path;
            cars = raceCars ?? new List<VehicleController>();
            totalLaps = Mathf.Max(1, laps);
            cutoffFraction = cutoff;
        }

        /// <summary>
        /// Runtime bot-commitment tune (see difficultyScalar). Lets the run director ramp the whole
        /// field per circuit at bind time without touching Configure or any race logic. Clamped to
        /// the serialized field's authored band so a caller can never push it out of range; the
        /// per-bot rubber-band still clamps the final commitment subtle. 1 = shipped balance.
        /// </summary>
        public void SetDifficultyScalar(float value) => difficultyScalar = Mathf.Clamp(value, 0.5f, 1.5f);

        /// <summary>
        /// Runtime tune of the survival cutoff window (see cutoffFraction). Lets the director tighten
        /// the gate on later circuits. Clamped to the field's sane range so the cutoff can never be
        /// zero (instant elimination) or a full extra lap of slack. Leaves the lap/leaderboard logic
        /// untouched — it only sets the fraction the deadline is computed from.
        /// </summary>
        public void SetCutoffFraction(float value) => cutoffFraction = Mathf.Clamp(value, 0.01f, 1f);

        /// <summary>
        /// Seeds the starting-grid shuffle for this race. The run layer pushes it (Shitboxer.Race can't
        /// reference Shitboxer.Meta — Meta already depends on Race, so a back-reference would be circular),
        /// exactly as it pushes the ruleset and difficulty. Must be called before <see cref="Start"/>, which
        /// RunDirector.BindToScene satisfies: it runs off sceneLoaded, and Unity fires that before Start.
        ///
        /// Never set — a bare race scene with no run driving it — leaves the grid exactly as the scene
        /// authored it, so opening RaceTest.unity on its own still behaves as before.
        /// </summary>
        public void SetGridSeed(int seed) => _gridSeed = seed;

        /// <summary>
        /// Applies a <see cref="RaceRuleset"/> — the data-driven description of how this race runs (laps,
        /// survival cutoff, boss flag, special modifiers) — the mechanism behind boss and event races.
        /// Folds the lap count and cutoff into the same backing fields <see cref="Configure"/> and the
        /// runtime setters already drive (clamped to their authored ranges), so the lap / leaderboard /
        /// checkpoint logic is untouched and reads exactly what it always did. Passing
        /// <see cref="RaceRuleset.Standard"/> restores the shipped defaults exactly. Call before
        /// <see cref="Start"/> (bind time) so the checkpoint ring is laid for the final lap count. Default
        /// (no call) leaves the race on the standard ruleset — byte-for-byte the shipped behaviour.
        /// </summary>
        public void SetRuleset(in RaceRuleset ruleset)
        {
            totalLaps = Mathf.Max(1, ruleset.Laps);
            cutoffFraction = Mathf.Clamp(ruleset.CutoffFraction, 0.01f, 1f);
            _isBoss = ruleset.IsBoss;
            _modifiers = ruleset.Modifiers;
        }

        /// <summary>Seconds of countdown left before the green flag; 0 once racing.</summary>
        public float CountdownRemainingS => Mathf.Max(0f, -_raceTime);

        private void SetDriversEnabled(bool value)
        {
            foreach (VehicleController car in cars)
            {
                if (!car) continue;
                var provider = car.GetComponent<VehicleInputProvider>();
                if (provider) provider.InputEnabled = value;
                var bot = car.GetComponent<BotDriver>();
                if (bot) bot.enabled = value;
                if (!value) car.Input = default;
            }
        }

        public RaceCarStatus GetStatus(VehicleController car)
        {
            for (int i = 0; i < _statuses.Count; i++)
                if (_statuses[i].Car == car)
                    return _statuses[i];
            return null;
        }

        private void Start()
        {
            if (!trackPath || trackPath.Line == null || cars.Count == 0)
            {
                Debug.LogError("[RaceManager] Needs a TrackPath (3+ waypoints) and at least one car.", this);
                enabled = false;
                return;
            }

            // MUST run before the snapshot below: that loop reads each car's transform to seed
            // TotalDistanceM, so a grid assigned afterwards would be recorded at the OLD positions and
            // every car's lap distance would start wrong.
            ShuffleGrid();

            RacingLine line = trackPath.Line;

            foreach (VehicleController car in cars)
            {
                if (!car) continue;
                // A registered racer must always be simulated. A VehicleController left disabled in
                // the scene (e.g. a leftover from debugging) makes that car sit inert on the floor —
                // no suspension, no drive — while every other car races. Guarantee it steps.
                car.enabled = true;
                // Every racer can be hit and can carry attack parts — guarantee the resolver
                // even for scenes/prefabs built before the combat layer existed.
                VehicleCombat.GetOrAdd(car.gameObject);
                // Every racer can also slipstream the car ahead — guarantee the draft sensor the same way.
                DraftSensor.GetOrAdd(car.gameObject);
                float progress = line.ProjectPosition(car.transform.position);
                _statuses.Add(new RaceCarStatus
                {
                    Car = car,
                    LastProgressM = progress,
                    // Distance is measured from arc-length 0 (the start/finish line). The grid sits
                    // just before the line, so cars start slightly negative and reach distance 0 as
                    // they cross into lap 1; lap N completes when forward distance reaches N*loop.
                    TotalDistanceM = line.SignedDelta(0f, progress),
                });
            }

            _leaderboard.AddRange(_statuses);
            _raceTime = -countdownS;
            _running = true;
            SetDriversEnabled(false);
        }

        /// <summary>
        /// Deals the cars to the grid by a seeded shuffle (see <see cref="StartingGrid"/> for why the
        /// player must not simply keep pole).
        ///
        /// The scene's authored car placements ARE the grid slots — this reads them back off the cars and
        /// redistributes them, so it needs no knowledge of the track geometry and can't drift out of sync
        /// with whatever the builder laid out.
        ///
        /// No-ops unless the run layer pushed a seed AND the toggle is on, so a bare race scene keeps its
        /// authored grid.
        /// </summary>
        private void ShuffleGrid()
        {
            if (!shuffleGrid || _gridSeed == null) return;

            var racers = new List<VehicleController>(cars.Count);
            foreach (VehicleController car in cars)
                if (car && car.Body) racers.Add(car);
            if (racers.Count < 2) return;

            // Snapshot the slots BEFORE moving anything — otherwise a car that's already been moved would
            // be read back as its own new slot and the "permutation" would collapse cars onto each other.
            var slotPos = new Vector3[racers.Count];
            var slotRot = new Quaternion[racers.Count];
            for (int i = 0; i < racers.Count; i++)
            {
                slotPos[i] = racers[i].Body.position;
                slotRot[i] = racers[i].Body.rotation;
            }

            int[] order = StartingGrid.Permutation(racers.Count, _gridSeed.Value);
            for (int i = 0; i < racers.Count; i++)
            {
                Rigidbody body = racers[i].Body;
                body.position = slotPos[order[i]];
                body.rotation = slotRot[order[i]];
                // Zero the carried motion, matching the reset pattern used by BotDriver/VehicleController.
                // Cars are stationary on the grid anyway; this just guarantees a teleport can't smuggle
                // velocity from the old slot into the new one.
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        private void FixedUpdate()
        {
            if (!_running || RaceComplete) return;

            _raceTime += Time.fixedDeltaTime;

            // Countdown: clocks and drivers frozen until zero.
            if (_raceTime < 0f) return;
            if (!_greenFlag)
            {
                _greenFlag = true;
                SetDriversEnabled(true);
            }
            RacingLine line = trackPath.Line;

            foreach (RaceCarStatus status in _statuses)
            {
                if (status.State != CarRaceState.Racing || !status.Car) continue;

                float prev = status.LastProgressM;
                float progress = line.ProjectPosition(status.Car.transform.position);
                float step = line.SignedDelta(prev, progress);
                status.LastProgressM = progress;

                // Teleport / mis-projection guard: a step too big to be one physics tick of driving is
                // a BotDriver flip/reset or a nearest-segment snap to the wrong corridor. Don't let it
                // inject distance or a lap — just refresh the live lap readout and skip this step.
                if (Mathf.Abs(step) > MaxPlausibleStepM)
                {
                    status.Lap = Mathf.Clamp(status.ValidatedLaps + 1, 1, totalLaps);
                    continue;
                }

                status.TotalDistanceM += step;
                CreditLaps(status, line);
                status.Lap = Mathf.Clamp(status.ValidatedLaps + 1, 1, totalLaps);
            }

            // Survival gate timeout: cutoff clock ran out on everyone still on track.
            if (WinnerFinished && _raceTime > CutoffDeadlineS)
            {
                foreach (RaceCarStatus status in _statuses)
                    if (status.State == CarRaceState.Racing)
                        Eliminate(status);
            }

            RaceComplete = AllCarsDone();
            SortLeaderboard();
        }

        /// <summary>
        /// Credits laps by monotonic net forward progress. TotalDistanceM is the guarded forward
        /// arc-length travelled from the start/finish line (arc-length 0), so each whole loop-length
        /// boundary crossed is one completed lap. This replaces the former ordered-checkpoint ring,
        /// which HARD-RESET a car's lap progress every time the teleport/mis-projection guard fired —
        /// and that guard fires constantly for a HUMAN driving off the racing line (wide lines, cut
        /// corners, weaving through traffic swing the nearest-point projection), so a human's laps
        /// almost never validated while on-rails bots were fine. Anti-cut survives via the same guard:
        /// a jump too big for one physics step is rejected upstream and adds no distance, so a real
        /// course-cut cannot manufacture the loop-length of forward progress a lap requires. Bounded by
        /// totalLaps and by the &lt;= MaxPlausibleStepM step, so it credits at most one lap per call.
        /// </summary>
        private void CreditLaps(RaceCarStatus status, RacingLine line)
        {
            int completed = LapProgress.CompletedLaps(status.TotalDistanceM, line.TotalLength);
            while (status.ValidatedLaps < totalLaps && status.ValidatedLaps < completed)
                ValidateLap(status);
        }

        private void ValidateLap(RaceCarStatus status)
        {
            // Lap-time capture: the just-completed lap ran from its recorded start to now. Fold it into
            // last/best and re-start timing for the next lap. Lap 1 times from the green flag
            // (LapStartTimeS defaults to 0).
            float lapTime = LapTiming.Elapsed(_raceTime, status.LapStartTimeS);
            status.LastLapTimeS = lapTime;
            status.BestLapTimeS = LapTiming.Fold(status.BestLapTimeS, lapTime);
            status.LapStartTimeS = _raceTime;

            status.ValidatedLaps++;
            if (status.ValidatedLaps >= totalLaps)
                OnCarCrossedFinish(status);
        }

        private void OnCarCrossedFinish(RaceCarStatus status)
        {
            status.FinishTimeS = _raceTime;

            if (!WinnerFinished)
            {
                WinnerFinished = true;
                WinnerFinishTimeS = _raceTime;
            }

            status.PassedCutoff = status.FinishTimeS <= CutoffDeadlineS || status.FinishTimeS <= WinnerFinishTimeS;
            status.State = status.PassedCutoff ? CarRaceState.Finished : CarRaceState.Eliminated;
            ReleaseBot(status);
        }

        private void Eliminate(RaceCarStatus status)
        {
            status.State = CarRaceState.Eliminated;
            status.PassedCutoff = false;
            ReleaseBot(status);
        }

        /// <summary>Stops a bot from lapping forever once its race is over; humans keep control.</summary>
        private static void ReleaseBot(RaceCarStatus status)
        {
            if (status.Car && status.Car.TryGetComponent(out BotDriver bot))
            {
                bot.enabled = false;
                status.Car.Input = default;
            }
        }

        private bool AllCarsDone()
        {
            foreach (RaceCarStatus status in _statuses)
                if (status.State == CarRaceState.Racing)
                    return false;
            return true;
        }

        private void SortLeaderboard()
        {
            _leaderboard.Sort((a, b) =>
            {
                bool aFinished = a.FinishTimeS >= 0f;
                bool bFinished = b.FinishTimeS >= 0f;
                if (aFinished != bFinished) return aFinished ? -1 : 1;
                if (aFinished) return a.FinishTimeS.CompareTo(b.FinishTimeS);
                return b.TotalDistanceM.CompareTo(a.TotalDistanceM);
            });
            for (int i = 0; i < _leaderboard.Count; i++)
                _leaderboard[i].Position = i + 1;
        }
    }
}
