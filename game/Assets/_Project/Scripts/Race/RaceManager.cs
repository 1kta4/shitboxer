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

        internal float LastProgressM;

        /// <summary>Checkpoint the car must reach next to keep its lap valid (index into the ordered ring).</summary>
        internal int NextCheckpoint;

        /// <summary>Ordered checkpoints (excluding the start/finish line) cleared since the line was last crossed.</summary>
        internal int CheckpointsPassedThisLap;

        /// <summary>Laps whose full checkpoint ring was cleared in order — the gate the finish counts, not raw distance.</summary>
        internal int ValidatedLaps;
    }

    /// <summary>
    /// Referee for one race: registers cars, counts laps along the TrackPath, tracks live
    /// positions and finishing order, and enforces the survival gate — once the winner
    /// finishes, everyone else must finish within CutoffFraction of the winner's time or
    /// they are ELIMINATED. All state is exposed read-only for the HUD.
    ///
    /// Laps are <b>checkpoint-gated</b>: an ordered ring of checkpoints is laid evenly along the
    /// loop, and a lap only counts when a car clears every checkpoint in order and then crosses
    /// the start/finish line. This defeats the two failure modes of naive nearest-segment
    /// progress: cutting the course (skips checkpoints → no lap) and projection snapping to the
    /// wrong corridor on a hairpin or a BotDriver teleport (a jump too big for one physics step
    /// is rejected, so it can never inject distance or fake a lap). Continuous distance is still
    /// accumulated (guarded) for live standings; only the discrete lap/finish is gated.
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
        [Tooltip("Ordered checkpoints laid evenly along the loop; a lap only counts when a car clears them in order (anti-cut / anti-mis-projection gate).")]
        [Min(4)]
        [SerializeField] private int checkpointCount = 16;
        [Tooltip("Global bot-commitment scalar — a hook for a future per-circuit difficulty ramp. 1 = shipped balance; each bot multiplies it into its own rubber-band, which BotBrain then clamps subtle so it never reads as cheating. Leave at 1 for now.")]
        [Range(0.5f, 1.5f)]
        [SerializeField] private float difficultyScalar = 1f;

        private readonly List<RaceCarStatus> _statuses = new List<RaceCarStatus>();
        private readonly List<RaceCarStatus> _leaderboard = new List<RaceCarStatus>();
        private float[] _checkpoints;      // arc-length position of each checkpoint; [0] = start/finish line
        private float _checkpointSpacing;  // even arc-length gap between checkpoints (TotalLength / count)
        private float _raceTime;
        private bool _greenFlag;
        private bool _running;

        public int TotalLaps => totalLaps;
        public float CutoffFraction => cutoffFraction;
        public float RaceTimeS => _raceTime;
        public float TrackLengthM => trackPath ? trackPath.TotalLength : 0f;

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

            RacingLine line = trackPath.Line;

            // Lay the ordered checkpoint ring evenly by arc length; checkpoint 0 is the start/finish line.
            int k = Mathf.Max(4, checkpointCount);
            _checkpoints = new float[k];
            _checkpointSpacing = line.TotalLength / k;
            for (int i = 0; i < k; i++) _checkpoints[i] = i * _checkpointSpacing;

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
                    // Grid sits just before the line, so cars start slightly negative and
                    // arm lap 1 by crossing the start line forward. NextCheckpoint points at the
                    // checkpoint ahead of the grid slot: a car behind the line targets checkpoint 0
                    // (its first crossing only arms lap 1, matching the old distance semantics).
                    TotalDistanceM = line.SignedDelta(0f, progress),
                    NextCheckpoint = NextCheckpointAhead(line, progress),
                });
            }

            _leaderboard.AddRange(_statuses);
            _raceTime = -countdownS;
            _running = true;
            SetDriversEnabled(false);
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
                // inject distance or a lap — re-point the gate at the car's new position so it can't
                // stall, then make it re-earn the checkpoints ahead (a reset can only delay a lap).
                if (Mathf.Abs(step) > MaxPlausibleStepM)
                {
                    ResyncCheckpoints(status, line, progress);
                    status.Lap = Mathf.Clamp(status.ValidatedLaps + 1, 1, totalLaps);
                    continue;
                }

                status.TotalDistanceM += step;
                CreditCheckpoints(status, line, progress);
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
        /// Credits each checkpoint the car has reached, in ring order, by POSITION — the car is at or
        /// just past the checkpoint — not by requiring one physics step to sweep exactly across it. The
        /// old per-step-window version measured from the PREVIOUS frame's projected position, so a
        /// one-metre forward blip of the spline projection near a checkpoint (common when driving wide
        /// or cutting a corner) could overshoot the window and permanently strand the gate — the lap
        /// would never validate and the car had to drive an extra lap. Anti-cut is still enforced
        /// upstream: a real cut or mis-snap jumps the projection past MaxPlausibleStepM and is rejected
        /// (and re-syncs the gate) before this runs, so only checkpoints the car genuinely drove past
        /// reach here. The guard loop and the two-spacing window bound a fast car to at most one ring of
        /// credits and stop a stale pointer from crediting a far-behind checkpoint. A lap validates only
        /// when the start/finish line (checkpoint 0) is crossed having cleared every other checkpoint.
        /// </summary>
        private void CreditCheckpoints(RaceCarStatus status, RacingLine line, float progress)
        {
            for (int guard = 0; guard < _checkpoints.Length; guard++)
            {
                float cp = _checkpoints[status.NextCheckpoint];
                float pastBy = line.SignedDelta(cp, progress); // >= 0 => car is at/just past this checkpoint
                if (pastBy < 0f || pastBy > _checkpointSpacing * 2f) break; // still ahead, or a stale far-behind pointer

                int passed = status.NextCheckpoint;
                status.NextCheckpoint = (passed + 1) % _checkpoints.Length;

                if (passed == 0)
                {
                    // Back over the start/finish line. Count the lap only if the whole ring was cleared
                    // in order first; a grid car's very first crossing has cleared none, so it just
                    // arms lap 1 (matching the old distance-based start).
                    if (status.CheckpointsPassedThisLap >= _checkpoints.Length - 1)
                        ValidateLap(status);
                    status.CheckpointsPassedThisLap = 0;
                }
                else
                {
                    status.CheckpointsPassedThisLap++;
                }
            }
        }

        private void ValidateLap(RaceCarStatus status)
        {
            status.ValidatedLaps++;
            if (status.ValidatedLaps >= totalLaps)
                OnCarCrossedFinish(status);
        }

        /// <summary>
        /// After a teleport/mis-snap, aim the gate at the checkpoint just ahead of the car's new
        /// position and drop the per-lap tally so the remaining checkpoints must be re-earned. A
        /// teleport can therefore only delay a lap, never inject one (a reset near the line can't
        /// fake a crossing). Distance is left untouched so the leaderboard doesn't lurch.
        /// </summary>
        private void ResyncCheckpoints(RaceCarStatus status, RacingLine line, float progress)
        {
            status.NextCheckpoint = NextCheckpointAhead(line, progress);
            status.CheckpointsPassedThisLap = 0;
        }

        /// <summary>Index of the first checkpoint strictly ahead of a progress value (checkpoints are evenly spaced).</summary>
        private int NextCheckpointAhead(RacingLine line, float progress)
        {
            int seg = Mathf.FloorToInt(line.Wrap(progress) / _checkpointSpacing);
            return (seg + 1) % _checkpoints.Length;
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
