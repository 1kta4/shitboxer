using System.Text;
using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// Phase-2 shakedown instrumentation: periodically dumps race standings to the console
    /// so an unattended (or remotely observed) bot race can be judged from logs alone.
    /// Flags cars that made no track progress since the last dump as STALLED.
    /// </summary>
    public class RaceDebugLogger : MonoBehaviour
    {
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private float intervalS = 20f;

        private float _nextDump;
        private readonly System.Collections.Generic.Dictionary<Object, float> _lastDistances = new();
        private bool _loggedComplete;

        public void Configure(RaceManager manager) => raceManager = manager;

        private void Update()
        {
            if (!raceManager || raceManager.Cars.Count == 0) return;

            if (raceManager.RaceComplete && !_loggedComplete)
            {
                _loggedComplete = true;
                Dump("RACE COMPLETE");
                return;
            }

            if (Time.time < _nextDump || raceManager.RaceComplete) return;
            _nextDump = Time.time + intervalS;
            Dump("standings");
        }

        private void Dump(string tag)
        {
            var cars = raceManager.Leaderboard;

            var sb = new StringBuilder();
            sb.AppendLine($"[RaceLog t={raceManager.RaceTimeS:0}s {tag}] track={raceManager.TrackLengthM:0}m laps={raceManager.TotalLaps} cutoff@{(raceManager.WinnerFinished ? raceManager.CutoffDeadlineS.ToString("0.0") + "s" : "-")}");
            for (int i = 0; i < cars.Count; i++)
            {
                var s = cars[i];
                bool hadPrev = _lastDistances.TryGetValue(s.Car, out float prevDist);
                bool stalled = hadPrev
                               && s.State == CarRaceState.Racing
                               && s.TotalDistanceM - prevDist < 5f
                               && raceManager.RaceTimeS > intervalS;
                _lastDistances[s.Car] = s.TotalDistanceM;
                sb.AppendLine(
                    $"  P{s.Position} {s.Car.name,-12} lap {s.Lap}/{raceManager.TotalLaps} dist {s.TotalDistanceM,6:0}m " +
                    $"spd {s.Car.SpeedKmh,4:0}km/h {s.State}{(s.FinishTimeS >= 0 ? $" t={s.FinishTimeS:0.0}s" : "")}{(stalled ? "  ** STALLED **" : "")}");
            }
            Debug.Log(sb.ToString());
        }
    }
}
