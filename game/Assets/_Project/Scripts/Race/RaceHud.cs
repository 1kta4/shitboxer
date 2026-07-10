using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// Throwaway IMGUI race readout (same style as VehicleDebugHud): position, lap, race
    /// clock, the live cutoff countdown once the winner finishes, PASS/ELIMINATED verdicts,
    /// GRIP/POWER spec bars, a durability bar + on-hit CRUNCH flash for the wave-4 damage
    /// model, live combat saps, and a small standings list for eyeballing the bots. Grouped
    /// into RACE / CAR / STANDINGS blocks. Dies when a real HUD arrives.
    /// </summary>
    public class RaceHud : MonoBehaviour
    {
        [SerializeField] private RaceManager raceManager;
        [SerializeField] private VehicleController playerCar;

        private VehicleCombat _playerCombat;

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
            DrawImpactFlash();   // brief full-frame CRUNCH cue on a hard hit — drawn behind the box

            // Height tracks the standings list so the box always fits its content (no clipping).
            int rows = raceManager.Leaderboard.Count;
            float areaHeight = 248f + rows * 18f;
            GUILayout.BeginArea(new Rect(12, 12, 280, areaHeight), GUI.skin.box);

            // ---- RACE ----------------------------------------------------------------
            GUILayout.Label($"POS {me.Position}/{raceManager.Cars.Count}     LAP {me.Lap}/{raceManager.TotalLaps}");
            GUILayout.Label($"TIME {FormatTime(Mathf.Max(0f, raceManager.RaceTimeS))}");
            // Lap timing (wave-12): FormatTime renders "-:--.-" for the sentinel -1, so both read as
            // placeholders until the player completes their first lap. Additive readout only.
            GUILayout.Label($"LAST {FormatTime(me.LastLapTimeS)}    BEST {FormatTime(me.BestLapTimeS)}");

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

            // ---- CAR -----------------------------------------------------------------
            DrawSeparator();
            DrawSpecBars();
            DrawDurabilityBar();
            DrawCombatStatus(me);

            // ---- STANDINGS -----------------------------------------------------------
            DrawSeparator();
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

        /// <summary>
        /// Live attack-layer readout: grip/power the player is currently losing to contact saps
        /// or an enemy aura (reads the sim's transient effect mults), plus a brief flash when the
        /// player's own attack part bites a rival.
        /// </summary>
        private void DrawCombatStatus(RaceCarStatus me)
        {
            if (me.State != CarRaceState.Racing || !playerCar || playerCar.Sim == null) return;

            Color prev = GUI.color;
            float gripDown = 1f - playerCar.Sim.GripEffectMult;
            float powerDown = 1f - playerCar.Sim.PowerEffectMult;

            if (gripDown > 0.02f)
            {
                GUI.color = Color.Lerp(Color.yellow, Color.red, Mathf.InverseLerp(0.05f, 0.4f, gripDown));
                GUILayout.Label($"GRIP  -{gripDown:P0}");
            }
            if (powerDown > 0.02f)
            {
                GUI.color = Color.Lerp(Color.yellow, Color.red, Mathf.InverseLerp(0.05f, 0.4f, powerDown));
                GUILayout.Label($"POWER -{powerDown:P0}");
            }
            GUI.color = prev;

            if (EnsureCombat() && Time.time - _playerCombat.LastAttackLandedRealtime < 0.7f)
            {
                GUI.color = new Color(1f, 0.55f, 0.2f);
                GUILayout.Label(_playerCombat.HasAura ? "DISRUPTING RIVALS" : "ATTACK HIT!");
                GUI.color = prev;
            }
        }

        /// <summary>
        /// Two headline bars (doc 03) for the player's *current* spec — the one actually driving the
        /// car this race, so equipped stat parts are already baked in. GRIP ≈ tyre + suspension +
        /// aero, POWER ≈ drivetrain + mass.
        /// </summary>
        private void DrawSpecBars()
        {
            if (!playerCar || playerCar.SpecAsset == null) return;
            ComputeGripPower(playerCar.SpecAsset.Spec, out float grip, out float power);

            DrawStatBar("GRIP", grip, new Color(0.3f, 0.75f, 1f));
            DrawStatBar("POWER", power, new Color(1f, 0.55f, 0.2f));
        }

        /// <summary>
        /// Wave-4 damage readout: the player's persistent structural integrity (playerCar.Durability,
        /// 1 = fresh). Fill shrinks and the bar bleeds green→red as the car wears; hidden while pristine
        /// since a full green bar carries no information. Reaches full red at the wear floor
        /// (<see cref="VehicleSim.MinDurability"/>), where the car is as battered as it can get.
        /// </summary>
        private void DrawDurabilityBar()
        {
            if (!playerCar || playerCar.Sim == null) return;
            float dur = Mathf.Clamp01(playerCar.Durability);
            if (dur >= 0.999f) return; // only meaningful once it drops below 1

            float wearT = Mathf.InverseLerp(1f, VehicleSim.MinDurability, dur); // 0 fresh → 1 at the floor
            Color fill = Color.Lerp(new Color(0.3f, 0.85f, 0.35f), new Color(0.9f, 0.2f, 0.15f), wearT);
            DrawBar("DURA", dur, fill, dur.ToString("P0"));
        }

        private static void DrawStatBar(string label, float value, Color fill)
        {
            DrawBar(label, value / 100f, fill, value.ToString("0"));
        }

        /// <summary>Shared labelled 0..1 progress bar for the spec/durability readouts.</summary>
        private static void DrawBar(string label, float fraction01, Color fill, string valueText)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(52));

            Rect track = GUILayoutUtility.GetRect(120, 12, GUILayout.ExpandWidth(true));
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(track, Texture2D.whiteTexture);
            GUI.color = fill;
            float t = Mathf.Clamp01(fraction01);
            GUI.DrawTexture(new Rect(track.x, track.y, track.width * t, track.height), Texture2D.whiteTexture);
            GUI.color = prev;

            GUILayout.Label(valueText, GUILayout.Width(38));
            GUILayout.EndHorizontal();
        }

        /// <summary>Thin translucent rule used to group the box into RACE / CAR / STANDINGS blocks.</summary>
        private static void DrawSeparator()
        {
            GUILayout.Space(4);
            Rect line = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.22f);
            GUI.DrawTexture(line, Texture2D.whiteTexture);
            GUI.color = prev;
            GUILayout.Space(4);
        }

        /// <summary>
        /// Brief on-hit impact cue: a fading red screen-edge frame plus a "CRUNCH" punch whenever the
        /// player just took a HARD hit. Reads the combat layer's shared impact stamp/severity so it stays
        /// in sync with the physics response; only fires above a severity threshold so scrapes stay quiet,
        /// and fades out in well under half a second so it never obscures the drive.
        /// </summary>
        private void DrawImpactFlash()
        {
            if (!EnsureCombat()) return;

            const float flashWindow = 0.45f;
            float since = Time.time - _playerCombat.LastImpactRealtime;
            if (since < 0f || since > flashWindow) return;

            float severity = Mathf.Clamp01(_playerCombat.LastImpactSeverity);
            if (severity < 0.45f) return; // only hard hits crunch — minor contact stays silent

            float fade = 1f - since / flashWindow;          // 1 → 0 over the window
            float alpha = fade * fade * severity;           // ease-out, scaled by how hard the hit was
            Color prev = GUI.color;

            // Red screen-edge frame — thicker for a harder hit, but never covers the play area.
            float thickness = Mathf.Lerp(6f, 22f, severity);
            GUI.color = new Color(1f, 0.15f, 0.1f, 0.55f * alpha);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, thickness), Texture2D.whiteTexture);                       // top
            GUI.DrawTexture(new Rect(0f, Screen.height - thickness, Screen.width, thickness), Texture2D.whiteTexture); // bottom
            GUI.DrawTexture(new Rect(0f, 0f, thickness, Screen.height), Texture2D.whiteTexture);                      // left
            GUI.DrawTexture(new Rect(Screen.width - thickness, 0f, thickness, Screen.height), Texture2D.whiteTexture); // right

            // "CRUNCH" punch near the top-centre, out of the way of the countdown and the HUD box.
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Mathf.Lerp(28f, 52f, severity)),
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            GUI.color = new Color(1f, 0.85f, 0.3f, alpha);
            GUI.Label(new Rect(0f, Screen.height * 0.13f, Screen.width, 60f), "CRUNCH", style);

            GUI.color = prev;
        }

        /// <summary>Lazily binds the player's combat component; returns false until one exists.</summary>
        private bool EnsureCombat()
        {
            if (!_playerCombat && playerCar) _playerCombat = playerCar.GetComponent<VehicleCombat>();
            return _playerCombat;
        }

        /// <summary>
        /// Local copy of Shitboxer.Meta.StatSummary.Compute — RaceHud lives in the Race assembly,
        /// which can't reference Meta (Meta already depends on Race, so it would be circular). Keep
        /// these ranges/weights identical to StatSummary if either side is retuned.
        /// </summary>
        private static void ComputeGripPower(VehicleSpec spec, out float grip, out float power)
        {
            grip = 0f;
            power = 0f;
            if (spec == null) return;

            // GRIP: tyre grip + turn-in sharpness + downforce + suspension stiffness.
            float peakMu = 0.5f * (spec.FrontTyre.PeakMu + spec.RearTyre.PeakMu);
            float slipDeg = 0.5f * (spec.FrontTyre.PeakSlipAngleDeg + spec.RearTyre.PeakSlipAngleDeg);
            float muN = Mathf.InverseLerp(0.90f, 1.60f, peakMu);
            float slipN = 1f - Mathf.InverseLerp(5f, 11f, slipDeg);          // lower slip angle → more grip
            float downforceN = Mathf.InverseLerp(0f, 3.5f, spec.DownforceCoeff);
            float springN = Mathf.InverseLerp(30000f, 85000f, spec.SpringRateNPerM);
            grip = Mathf.Clamp(100f * (0.45f * muN + 0.15f * slipN + 0.20f * downforceN + 0.20f * springN), 0f, 100f);

            // POWER: raw engine torque + power-to-weight (MassKg feeds POWER).
            float torqueN = Mathf.InverseLerp(150f, 450f, spec.Engine.PeakTorqueNm);
            float mass = Mathf.Max(1f, spec.MassKg);
            float peakKw = spec.Engine.PeakTorqueNm * spec.Engine.PeakTorqueRpm / 9549f;
            float p2wN = Mathf.InverseLerp(60f, 170f, peakKw / (mass / 1000f));
            power = Mathf.Clamp(100f * (0.55f * torqueN + 0.45f * p2wN), 0f, 100f);
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
