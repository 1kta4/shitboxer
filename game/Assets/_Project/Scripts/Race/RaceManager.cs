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
    }

    /// <summary>
    /// Referee for one race: registers cars, counts laps via continuous progress along the
    /// TrackPath (signed wraparound deltas, so reversing over the line un-counts), tracks
    /// live positions and finishing order, and enforces the survival gate — once the winner
    /// finishes, everyone else must finish within CutoffFraction of the winner's time or
    /// they are ELIMINATED. All state is exposed read-only for the HUD.
    /// </summary>
    public class RaceManager : MonoBehaviour
    {
        [SerializeField] private TrackPath trackPath;
        [SerializeField] private List<VehicleController> cars = new List<VehicleController>();
        [Min(1)]
        [SerializeField] private int totalLaps = 3;
        [Tooltip("Survival gate: after the winner finishes, others must finish within winnerTime * (1 + this) or be eliminated.")]
        [Range(0.01f, 1f)]
        [SerializeField] private float cutoffFraction = 0.15f;
        [Tooltip("Grid-frozen countdown before the green flag, seconds.")]
        [SerializeField] private float countdownS = 3f;

        private readonly List<RaceCarStatus> _statuses = new List<RaceCarStatus>();
        private readonly List<RaceCarStatus> _leaderboard = new List<RaceCarStatus>();
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
            foreach (VehicleController car in cars)
            {
                if (!car) continue;
                float progress = line.ProjectPosition(car.transform.position);
                _statuses.Add(new RaceCarStatus
                {
                    Car = car,
                    LastProgressM = progress,
                    // Grid sits just before the line, so cars start slightly negative and
                    // arm lap 1 by crossing the start line forward.
                    TotalDistanceM = line.SignedDelta(0f, progress),
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
            float raceDistance = totalLaps * line.TotalLength;

            foreach (RaceCarStatus status in _statuses)
            {
                if (status.State != CarRaceState.Racing || !status.Car) continue;

                float progress = line.ProjectPosition(status.Car.transform.position);
                status.TotalDistanceM += line.SignedDelta(status.LastProgressM, progress);
                status.LastProgressM = progress;
                status.Lap = Mathf.Clamp(Mathf.FloorToInt(status.TotalDistanceM / line.TotalLength) + 1, 1, totalLaps);

                if (status.TotalDistanceM >= raceDistance)
                    OnCarCrossedFinish(status);
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
