using System.Collections.Generic;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// Throwaway IMGUI race readout (same style as VehicleDebugHud): position, lap, race
    /// clock, the survival cutoff (projected from pace while racing, then an exact countdown once the
    /// winner finishes), a live payout preview showing what the current position banks versus what
    /// winning pays — together these are the two halves of the run's push-to-win vs hang-back-to-farm
    /// tension, and neither was visible mid-race before wave 16 — PASS/ELIMINATED verdicts,
    /// GRIP/POWER spec bars, an always-on durability bar + on-hit CRUNCH flash for the wave-4
    /// damage model, live combat saps, a live SLIPSTREAM cue while drafting, the in-progress
    /// lap's pace vs the player's best, and a small standings list for eyeballing the bots.
    /// Grouped into RACE / CAR / STANDINGS blocks. Purely a readout of ACTIVE data — it never
    /// touches driving, the race referee, or the economy. Dies when a real HUD arrives.
    /// </summary>
    public class RaceHud : MonoBehaviour
    {
        /// <summary>
        /// Minimum distance, in LAPS, before the cutoff pace projection is shown. The grid spreads the
        /// field over ~27 m, and that offset is a fixed handicap in TotalDistanceM — early on it dwarfs any
        /// real pace difference and would render an alarming, wrong "AT RISK" in the opening corners. After
        /// a full lap the offset is a few percent of distance covered and pace dominates. Costs the readout
        /// the first lap of a 3-lap race; it beats lying for the first lap.
        /// </summary>
        private const float PaceEstimateMinLaps = 1f;

        [SerializeField] private RaceManager raceManager;
        [SerializeField] private VehicleController playerCar;

        private VehicleCombat _playerCombat;
        private DraftSensor _playerDraft;
        private System.Func<int, int> _payoutPreview;

        public void Configure(RaceManager manager, VehicleController player)
        {
            raceManager = manager;
            playerCar = player;
        }

        /// <summary>
        /// Injects the position→cash preview that the run layer owns: given a 1-based finish position, what a
        /// clean finish there banks right now.
        ///
        /// Pushed in rather than imported because Shitboxer.Race CANNOT reference Shitboxer.Meta — Meta
        /// already depends on Race, so a back-reference would be circular (same constraint StatSummary
        /// documents). The tempting alternative, re-implementing the payout formula here, is exactly what
        /// must not happen: a preview that drifts from the real payout would lie to the player at the precise
        /// moment they're weighing push-to-win against hang-back-to-farm. So the run layer hands over a
        /// closure across its OWN resolution math (RunDirector.CleanFinishPayoutFor) instead.
        ///
        /// Null — nobody pushed one, e.g. a bare race scene with no run — simply hides the readout.
        /// </summary>
        public void SetPayoutPreview(System.Func<int, int> preview) => _payoutPreview = preview;

        private void OnGUI()
        {
            if (!raceManager || !playerCar) return;
            RaceCarStatus me = raceManager.GetStatus(playerCar);
            if (me == null) return;

            DrawCountdown();
            DrawImpactFlash();   // brief full-frame CRUNCH cue on a hard hit — drawn behind the box

            // Height tracks the standings list so the box always fits its content (no clipping).
            // Base bumped for the always-on durability bar + the permanent CUR pace line and the
            // transient SLIPSTREAM cue so none of them can push the standings out of the clipped area.
            // Wave-16 adds two more always-on-while-racing lines (the cutoff/pace projection and the
            // payout preview), so the base grows by another 36f to keep the standings inside the area.
            int rows = raceManager.Leaderboard.Count;
            float areaHeight = 328f + rows * 18f;
            GUILayout.BeginArea(new Rect(12, 12, 280, areaHeight), GUI.skin.box);

            // ---- RACE ----------------------------------------------------------------
            GUILayout.Label($"POS {me.Position}/{raceManager.Cars.Count}     LAP {me.Lap}/{raceManager.TotalLaps}");
            GUILayout.Label($"TIME {FormatTime(Mathf.Max(0f, raceManager.RaceTimeS))}");
            // Lap timing (wave-12): FormatTime renders "-:--.-" for the sentinel -1, so both read as
            // placeholders until the player completes their first lap. Additive readout only.
            GUILayout.Label($"LAST {FormatTime(me.LastLapTimeS)}    BEST {FormatTime(me.BestLapTimeS)}");
            DrawLapPace(me);

            DrawCutoff(me);
            DrawPayoutPreview(me);

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
            DrawDraftStatus(me);
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
        /// 1 = fresh). Always shown so it reads as a live status even while pristine — a full green bar
        /// says "undamaged". Fill shrinks and the bar bleeds green→red as the car wears, reaching full
        /// red at the wear floor (<see cref="VehicleSim.MinDurability"/>), where the car is as battered
        /// as it can get.
        /// </summary>
        private void DrawDurabilityBar()
        {
            if (!playerCar || playerCar.Sim == null) return;
            float dur = Mathf.Clamp01(playerCar.Durability);

            float wearT = Mathf.InverseLerp(1f, VehicleSim.MinDurability, dur); // 0 fresh → 1 at the floor
            Color fill = Color.Lerp(new Color(0.3f, 0.85f, 0.35f), new Color(0.9f, 0.2f, 0.15f), wearT);
            DrawBar("DURA", dur, fill, dur.ToString("P0"));
        }

        /// <summary>
        /// Live slipstream cue: lights up while the player's <see cref="DraftSensor"/> reports it is
        /// tucked in a leading car's wake and already banking the aero draft. Racing-only and transient —
        /// it simply disappears the moment the car pulls out of the tow. Display of ACTIVE sensor state;
        /// it never asserts or alters the draft itself (the sensor and sim own that).
        /// </summary>
        private void DrawDraftStatus(RaceCarStatus me)
        {
            if (me.State != CarRaceState.Racing) return;
            if (!EnsureDraft() || !_playerDraft.IsDrafting) return;

            Color prev = GUI.color;
            GUI.color = new Color(0.2f, 0.9f, 1f); // slipstream blue, matching the sensor gizmo
            GUILayout.Label("DRAFT — SLIPSTREAM");
            GUI.color = prev;
        }

        /// <summary>
        /// Live lap-pace readout: the in-progress lap's elapsed time (<see cref="RaceManager.CurrentLapTimeS"/>)
        /// next to the player's BEST, plus a signed delta once a best exists. The delta is this lap's elapsed
        /// minus the best lap's total: it counts up from a large negative toward 0 as the lap runs, then goes
        /// positive (and tints warm) the instant this lap overruns the best — i.e. it is guaranteed slower.
        /// FormatTime renders the -1 "no lap yet" sentinel as a placeholder, so BEST reads "-:--.-" until the
        /// first lap validates and the delta is simply omitted. Additive readout — no effect on the race.
        /// </summary>
        private void DrawLapPace(RaceCarStatus me)
        {
            float current = raceManager.CurrentLapTimeS(me);
            float best = me.BestLapTimeS;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"CUR {FormatTime(current)}    BEST {FormatTime(best)}");

            string delta = FormatPaceDelta(current, best);
            if (delta.Length > 0)
            {
                Color prev = GUI.color;
                GUI.color = current - best <= 0f
                    ? new Color(0.35f, 0.9f, 0.4f)  // still under the best lap's total — on or ahead of pace
                    : new Color(1f, 0.55f, 0.2f);   // this lap has already overrun the best — off pace
                GUILayout.Label($"({delta})", GUILayout.Width(64));
                GUI.color = prev;
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Pure pace-delta text for the current lap: the signed seconds of the in-progress lap's elapsed
        /// time (<paramref name="currentLapS"/>) minus the player's best lap (<paramref name="bestLapS"/>),
        /// e.g. "+1.2" or "-0.8". Returns an empty string when there is no best yet (sentinel &lt; 0) or the
        /// current time is not valid, so callers show nothing until a comparison is meaningful. No engine,
        /// scene or clock state, so it is unit-testable and a headless readout would format it identically.
        /// </summary>
        public static string FormatPaceDelta(float currentLapS, float bestLapS)
        {
            if (bestLapS < 0f || currentLapS < 0f) return string.Empty;
            float delta = currentLapS - bestLapS;
            return delta.ToString("+0.0;-0.0");
        }

        /// <summary>
        /// The survival gate, shown for the WHOLE race rather than only its last seconds.
        ///
        /// Once the winner is in, the deadline is a known wall-clock instant, so count it down exactly.
        /// Before that there is no deadline to show — CutoffDeadlineS is DEFINED as the winner's finish time
        /// × (1 + fraction) and returns a -1 sentinel until someone finishes. That is why this readout used
        /// to be hidden for ~95% of a race, which made Phase 2's whole "can I stay inside the cutoff?" tension
        /// structurally invisible. So project it from pace instead.
        /// </summary>
        private void DrawCutoff(RaceCarStatus me)
        {
            if (me.State != CarRaceState.Racing) return;

            if (raceManager.WinnerFinished)
            {
                float remaining = Mathf.Max(0f, raceManager.CutoffDeadlineS - raceManager.RaceTimeS);
                GUILayout.Label($"CUTOFF IN {remaining:0.0}s");
                return;
            }

            IReadOnlyList<RaceCarStatus> board = raceManager.Leaderboard;
            if (board == null || board.Count == 0) return;

            // Pre-winner the board is sorted by distance, so entry 0 is whoever is leading on the road.
            float excess = ProjectedPaceExcess01(
                board[0].TotalDistanceM, me.TotalDistanceM, raceManager.TrackLengthM * PaceEstimateMinLaps);

            string text = FormatCutoffPace(excess, raceManager.CutoffFraction);
            if (text.Length > 0) GUILayout.Label(text);
        }

        /// <summary>
        /// How far the player is projected to finish BEHIND the winner, as a fraction of the winner's time
        /// (0.08 = projected to finish 8% slower — inside a 15% cutoff). -1 means "not meaningful yet, omit".
        ///
        /// Why this needs no clock: project each car's finish by holding its average pace, and both
        /// projections extrapolate the SAME elapsed time over the SAME loop length. The finish-time ratio is
        /// (T·D/playerDist) / (T·D/leaderDist) — T and D cancel exactly, leaving leaderDist/playerDist. So a
        /// pure distance ratio IS the projected time ratio, with no clock term to get wrong.
        ///
        /// Gated on <paramref name="minDistanceM"/> because the ~27 m grid spread is a fixed handicap in
        /// TotalDistanceM: over the opening metres it swamps genuine pace and would scream AT RISK at a car
        /// that is merely starting at the back. Returns 0 when the player IS the leader (identical distances).
        /// Pure — no engine, scene or clock state — so it is unit-testable and a headless readout matches.
        /// </summary>
        public static float ProjectedPaceExcess01(float leaderDistanceM, float playerDistanceM, float minDistanceM)
        {
            if (minDistanceM < 1f) minDistanceM = 1f;
            if (playerDistanceM < minDistanceM || leaderDistanceM < minDistanceM) return -1f;
            return (leaderDistanceM / playerDistanceM) - 1f;
        }

        /// <summary>
        /// Renders the projected cutoff standing: the player's projected deficit against the gate they must
        /// stay inside, plus a blunt SAFE / AT RISK verdict. Empty string for the -1 "not yet meaningful"
        /// sentinel so the caller draws nothing. Pure and unit-testable.
        /// </summary>
        public static string FormatCutoffPace(float paceExcess01, float cutoffFraction)
        {
            if (paceExcess01 < 0f) return string.Empty;
            string verdict = paceExcess01 <= cutoffFraction ? "SAFE" : "AT RISK";
            return $"PACE +{paceExcess01 * 100f:0}%  /  CUT +{cutoffFraction * 100f:0}%   {verdict}";
        }

        /// <summary>
        /// The money half of the run's signature tension, live on the HUD. Money is INVERTED — a worse finish
        /// pays more (doc 03) — but until now the race screen showed no cash at all, so the player could not
        /// see the trade they are supposed to be agonising over. Showing the cutoff (above) without this would
        /// be worse than showing neither: it would surface only the risk of dropping back and none of the
        /// reward. The two ship together on purpose.
        /// </summary>
        private void DrawPayoutPreview(RaceCarStatus me)
        {
            if (_payoutPreview == null || me.State != CarRaceState.Racing) return;
            GUILayout.Label(FormatPayoutPreview(me.Position, _payoutPreview(me.Position), _payoutPreview(1)));
        }

        /// <summary>
        /// Payout preview text: what the current position banks, and — the point of the whole line — what
        /// winning would pay instead, so the inversion is legible at a glance ("BANKING $10 at P6 (WIN PAYS
        /// $7)"). Leading, the comparison is redundant, so it collapses to the plain figure. Pure and
        /// unit-testable; takes the already-resolved figures rather than computing them, since Race cannot
        /// reach the payout table (see <see cref="SetPayoutPreview"/>).
        /// </summary>
        public static string FormatPayoutPreview(int position, int payoutHere, int payoutIfWon)
        {
            if (position <= 1) return $"BANKING ${payoutHere} — LEADING";
            return $"BANKING ${payoutHere} at P{position}   (WIN PAYS ${payoutIfWon})";
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

        /// <summary>Lazily binds the player's draft sensor; returns false until one exists.</summary>
        private bool EnsureDraft()
        {
            if (!_playerDraft && playerCar) _playerDraft = playerCar.GetComponent<DraftSensor>();
            return _playerDraft;
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
