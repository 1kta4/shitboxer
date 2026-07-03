using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// Throwaway IMGUI race readout (same style as VehicleDebugHud): position, lap, race
    /// clock, the live cutoff countdown once the winner finishes, PASS/ELIMINATED verdicts,
    /// and a small standings list for eyeballing the bots. Dies when a real HUD arrives.
    /// </summary>
    public class RaceHud : MonoBehaviour
    {
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private VehicleController playerCar;

        public void Configure(RaceManager manager, VehicleController player)
        {
            raceManager = manager;
            playerCar = player;
        }

        private void OnGUI()
        {
            if (!raceManager || !playerCar) return;
            RaceCarStatus me = raceManager.GetStatus(playerCar);
            if (me == null) return;

            DrawCountdown();

            GUILayout.BeginArea(new Rect(12, 12, 280, 340), GUI.skin.box);

            GUILayout.Label($"POS {me.Position}/{raceManager.Cars.Count}     LAP {me.Lap}/{raceManager.TotalLaps}");
            GUILayout.Label($"TIME {FormatTime(Mathf.Max(0f, raceManager.RaceTimeS))}");

            if (me.State == CarRaceState.Racing && raceManager.WinnerFinished)
            {
                float remaining = Mathf.Max(0f, raceManager.CutoffDeadlineS - raceManager.RaceTimeS);
                GUILayout.Label($"CUTOFF IN {remaining:0.0}s");
            }

            switch (me.State)
            {
                case CarRaceState.Finished:
                    GUILayout.Label(me.Position == 1
                        ? $"WINNER — {FormatTime(me.FinishTimeS)}"
                        : $"FINISHED P{me.Position} — PASS ({FormatTime(me.FinishTimeS)})");
                    break;
                case CarRaceState.Eliminated:
                    GUILayout.Label(me.FinishTimeS >= 0f
                        ? "FINISHED OUTSIDE CUTOFF — ELIMINATED"
                        : $"ELIMINATED — missed the {raceManager.CutoffFraction:P0} cutoff");
                    break;
            }

            GUILayout.Space(6);
            foreach (RaceCarStatus s in raceManager.Leaderboard)
            {
                string state = s.State switch
                {
                    CarRaceState.Finished => $"FIN {FormatTime(s.FinishTimeS)}",
                    CarRaceState.Eliminated => "ELIM",
                    _ => $"L{s.Lap}",
                };
                string marker = s.Car == playerCar ? "  <<" : "";
                GUILayout.Label($"{s.Position}. {(s.Car ? s.Car.name : "?")}  [{state}]{marker}");
            }

            GUILayout.EndArea();
        }

        private void DrawCountdown()
        {
            float remaining = raceManager.CountdownRemainingS;
            bool showGo = remaining <= 0f && raceManager.RaceTimeS >= 0f && raceManager.RaceTimeS < 1.2f;
            if (remaining <= 0f && !showGo) return;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 64,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            string text = remaining > 0f ? Mathf.Ceil(remaining).ToString("0") : "GO!";
            GUI.Label(new Rect(0, Screen.height * 0.25f, Screen.width, 90), text, style);
        }

        private static string FormatTime(float seconds)
        {
            if (seconds < 0f) return "-:--.-";
            int minutes = (int)(seconds / 60f);
            return $"{minutes}:{seconds - minutes * 60f:00.0}";
        }
    }
}
