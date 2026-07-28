using System.Collections.Generic;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// Feeds <see cref="RaceObserver"/> from the live scene: samples every car into the track frame each
    /// physics step and forwards attributed contacts. Deliberately thin — all the judgement lives in the
    /// pure core, which is why the detectors are unit-testable without a scene.
    ///
    /// Modelled on <see cref="RaceDebugLogger"/>: a Race-layer component the scene builder drops on the
    /// RaceRig, resolving its dependencies with the <c>GetComponentInParent</c> → <c>FindFirstObjectByType</c>
    /// pattern the rest of the assembly uses.
    /// </summary>
    public sealed class RaceObserverHost : MonoBehaviour
    {
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private TrackPath trackPath;

        private readonly RaceObserver _observer = new RaceObserver();
        private CarFrame[] _frames = new CarFrame[8];

        // Cars we've hooked, so contacts can be attributed and unsubscribed cleanly.
        private readonly List<VehicleCombat> _hooked = new List<VehicleCombat>(8);
        private readonly List<System.Action<VehicleCombat.ContactReport>> _handlers =
            new List<System.Action<VehicleCombat.ContactReport>>(8);

        private bool _bound;

        /// <summary>The race's observations so far. Pulled by the run layer at race end.</summary>
        public RaceObserver Observer => _observer;

        public void Configure(RaceManager manager, TrackPath path)
        {
            raceManager = manager;
            trackPath = path;
        }

        private void Start()
        {
            if (!raceManager) raceManager = GetComponentInParent<RaceManager>();
            if (!raceManager) raceManager = FindFirstObjectByType<RaceManager>();
            if (!trackPath) trackPath = FindFirstObjectByType<TrackPath>();
        }

        private void OnDisable() => Unhook();

        /// <summary>
        /// Assigns each car its observation key and subscribes to its contact channel. Deferred to the first
        /// physics step rather than <c>Start</c> because the run layer pushes rival identities at
        /// <c>sceneLoaded</c> and <c>RaceManager</c> registers its cars in its own <c>Start</c> — by the
        /// first FixedUpdate both are settled regardless of script execution order.
        /// </summary>
        private void Bind()
        {
            _bound = true;
            _observer.Reset();
            if (raceManager == null) return;

            IReadOnlyList<RaceCarStatus> cars = raceManager.Cars;
            if (_frames.Length < cars.Count) _frames = new CarFrame[cars.Count];

            foreach (RaceCarStatus status in cars)
            {
                VehicleController car = status.Car;
                if (car == null) continue;

                int key = KeyFor(car);
                if (key > 0) _observer.RegisterRival(key);

                var combat = VehicleCombat.GetOrAdd(car.gameObject);
                VehicleController self = car;
                System.Action<VehicleCombat.ContactReport> handler = report => OnContact(self, report);
                combat.OnContact += handler;
                _hooked.Add(combat);
                _handlers.Add(handler);
            }
        }

        private void Unhook()
        {
            for (int i = 0; i < _hooked.Count; i++)
                if (_hooked[i] != null) _hooked[i].OnContact -= _handlers[i];
            _hooked.Clear();
            _handlers.Clear();
            _bound = false;
        }

        /// <summary>
        /// 0 for the player, the bot's <c>RivalKey</c> for a rival, -1 for anything unidentified. Positive
        /// test on the input provider — the same way the run layer finds the player — rather than "has no
        /// BotDriver", which would misread any driverless car as human.
        /// </summary>
        private static int KeyFor(VehicleController car)
        {
            if (car.TryGetComponent(out VehicleInputProvider _)) return 0;
            if (car.TryGetComponent(out BotDriver bot)) return bot.RivalKey;
            return -1;
        }

        /// <summary>
        /// Turns one car's contact report into a player-relative one. Bot-vs-bot contact is dropped: memory
        /// is about the player, and tracking the other 21 pairs would cost CPU and save size for something
        /// no one can perceive.
        /// </summary>
        private void OnContact(VehicleController self, in VehicleCombat.ContactReport report)
        {
            if (raceManager == null || report.Other == null) return;

            int selfKey = KeyFor(self);
            int otherKey = KeyFor(report.Other);

            float playerFault;
            int rivalKey;
            if (selfKey == 0 && otherKey > 0)
            {
                playerFault = report.Aggressorness01;
                rivalKey = otherKey;
            }
            else if (otherKey == 0 && selfKey > 0)
            {
                // The rival raised it, so its aggressorness is the complement of the player's. Both cars
                // report every collision; RaceObserver.RecordContact de-duplicates the pair.
                playerFault = 1f - report.Aggressorness01;
                rivalKey = selfKey;
            }
            else return;

            _observer.RecordContact(raceManager.RaceTimeS, rivalKey, report.Severity01, playerFault);
        }

        private void FixedUpdate()
        {
            if (raceManager == null || trackPath == null || trackPath.Line == null) return;
            if (!_bound) Bind();

            // Post-green only. During the countdown cars are parked on a shuffled grid, and treating that
            // as racing would manufacture engagements out of the starting formation.
            if (raceManager.RaceTimeS < 0f) return;

            RacingLine line = trackPath.Line;
            IReadOnlyList<RaceCarStatus> cars = raceManager.Cars;
            if (_frames.Length < cars.Count) _frames = new CarFrame[cars.Count];

            int n = 0;
            for (int i = 0; i < cars.Count; i++)
            {
                RaceCarStatus status = cars[i];
                VehicleController car = status.Car;
                if (car == null) continue;

                int key = KeyFor(car);
                if (key < 0) continue;

                // Reuse the referee's per-step projection rather than calling ProjectPosition again —
                // it is an O(samples) linear scan and the most expensive per-car op in the race loop.
                float progress = status.LastProgressM;
                Vector3 onLine = line.PointAt(progress);
                Vector3 dir = line.DirectionAt(progress);
                Vector3 right = Vector3.Cross(Vector3.up, dir);
                Vector3 offset = car.transform.position - onLine;
                offset.y = 0f;

                VehicleInput input = car.Input;
                _frames[n++] = new CarFrame
                {
                    Key = key,
                    TotalDistanceM = status.TotalDistanceM,
                    ProgressM = progress,
                    LateralM = Vector3.Dot(offset, right),
                    SpeedMps = car.Body != null ? car.Body.linearVelocity.magnitude : 0f,
                    Throttle = input.Throttle,
                    Brake = input.Brake,
                    Racing = status.State == CarRaceState.Racing,
                };
            }

            _observer.Observe(raceManager.RaceTimeS, Time.fixedDeltaTime, _frames, n, _corners ??= BuildCorners(line));
        }

        private CornerTable _corners;

        /// <summary>Built once on first use — the track shape doesn't change during a race.</summary>
        private static CornerTable BuildCorners(RacingLine line) => CornerTable.Build(line);

        /// <summary>Rolls up the race. Safe to call at any time; see <see cref="RaceObserver.Summarize"/>.</summary>
        public RaceObservationSummary Summarize(int playerFinishPosition) =>
            _observer.Summarize(playerFinishPosition, raceManager != null ? raceManager.Cars.Count : 0);
    }
}
