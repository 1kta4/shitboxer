using Shitboxer.Meta;
using Shitboxer.Race;
using Shitboxer.Vehicle;
using UnityEngine;
using UnityEngine.UIElements;

namespace Shitboxer.UI.Views
{
    /// <summary>
    /// The in-race HUD (v3), laid out like a PS2 racer (Gran Turismo): sparse readouts spread to the
    /// screen corners over a TRANSPARENT overlay, not one dense box. Top-left position + lap; top-centre
    /// the current lap time + gap; top-right best/last lap; bottom-left the survival cutoff, payout
    /// preview, verdict and transient contact/draft cues; bottom-right the run status (funds/lives/
    /// circuit) + durability. Text is dark-outlined for legibility with no plate to block the drive.
    /// Refreshed each frame from the <see cref="IRunHost"/>. GRIP/POWER and the standings list are
    /// deliberately gone — the first is a static garage stat, the second is covered by the position.
    /// The 3-2-1 countdown and CRUNCH flash stay in the stripped IMGUI RaceHud for now.
    /// </summary>
    public sealed class RaceHudView
    {
        public VisualElement Root { get; }

        private readonly Label _pos = new Label();
        private readonly Label _lap = new Label();
        private readonly Label _curTime = new Label();
        private readonly Label _delta = new Label();
        private readonly Label _best = new Label();
        private readonly Label _last = new Label();
        private readonly Label _cutoff = new Label();
        private readonly Label _payout = new Label();
        private readonly Label _verdict = new Label();
        private readonly Label _draft = new Label();
        private readonly Label _sapGrip = new Label();
        private readonly Label _sapPower = new Label();
        private readonly Label _attack = new Label();
        private readonly Label _sCash = new Label();
        private readonly Label _sProgress = new Label();
        private readonly VisualElement _duraFill = new VisualElement();
        private readonly Label _duraVal = new Label();

        public RaceHudView()
        {
            var screen = new VisualElement();
            screen.AddToClassList("hud-screen");

            // top-left: POSITION + LAP
            var tl = Corner("hud-tl");
            var posBox = new VisualElement();
            posBox.AddToClassList("hud-posbox");
            posBox.Add(Cap("POS"));
            _pos.AddToClassList("hud-pos");
            posBox.Add(_pos);
            tl.Add(posBox);
            var lapBox = new VisualElement();
            lapBox.AddToClassList("hud-lapbox");
            lapBox.Add(Cap("LAP"));
            _lap.AddToClassList("hud-lap");
            lapBox.Add(_lap);
            tl.Add(lapBox);
            screen.Add(tl);

            // top-centre: current lap time + gap
            var tc = Corner("hud-tc");
            _curTime.AddToClassList("hud-curtime");
            _delta.AddToClassList("hud-delta");
            tc.Add(_curTime);
            tc.Add(_delta);
            screen.Add(tc);

            // top-right: BEST / LAST lap
            var tr = Corner("hud-tr");
            tr.Add(RightStat("BEST LAP", _best));
            tr.Add(RightStat("LAST LAP", _last));
            screen.Add(tr);

            // bottom-left: cutoff, payout, verdict, transient cues
            var bl = Corner("hud-bl");
            _cutoff.AddToClassList("hud-line");
            _payout.AddToClassList("hud-line");
            _payout.AddToClassList("amber");
            _verdict.AddToClassList("hud-verdict");
            _draft.AddToClassList("hud-line");
            _draft.AddToClassList("green");
            _sapGrip.AddToClassList("hud-line");
            _sapGrip.AddToClassList("red");
            _sapPower.AddToClassList("hud-line");
            _sapPower.AddToClassList("red");
            _attack.AddToClassList("hud-line");
            _attack.AddToClassList("amber");
            bl.Add(_cutoff);
            bl.Add(_payout);
            bl.Add(_verdict);
            bl.Add(_draft);
            bl.Add(_sapGrip);
            bl.Add(_sapPower);
            bl.Add(_attack);
            screen.Add(bl);

            // bottom-right: run status + durability
            var br = Corner("hud-br");
            _sCash.AddToClassList("hud-line");
            _sCash.AddToClassList("amber");
            _sProgress.AddToClassList("hud-line");
            _sProgress.AddToClassList("dim");
            br.Add(_sCash);
            br.Add(_sProgress);
            br.Add(BuildDura());
            screen.Add(br);

            Root = screen;
        }

        public void Refresh(IRunHost host, System.Func<int, int> payout)
        {
            if (host == null) return;
            RefreshStatus(host.Run);

            RaceManager m = host.CurrentRace;
            VehicleController player = host.PlayerCar;
            if (m == null || player == null) return;
            RaceCarStatus me = m.GetStatus(player);
            if (me == null) return;

            _pos.text = me.Position.ToString();
            _lap.text = $"{me.Lap}/{m.TotalLaps}";
            _curTime.text = RaceDisplay.FormatRaceClock(m.CurrentLapTimeS(me));
            _best.text = RaceDisplay.FormatRaceClock(me.BestLapTimeS);
            _last.text = RaceDisplay.FormatRaceClock(me.LastLapTimeS);

            RefreshDelta(m, me);
            RefreshCutoff(m, me);
            RefreshPayout(me, payout);
            RefreshVerdict(me);
            RefreshCues(player, me);
            RefreshDura(player);
        }

        private void RefreshStatus(RunState run)
        {
            if (run == null) return;
            _sCash.text = $"$ {run.Money}   LIVES {run.Lives}";
            _sProgress.text = run.IsBossRace
                ? $"C{run.CircuitIndex + 1}/{run.TotalCircuits}  BOSS {run.RaceIndex + 1}/{run.RacesPerCircuit}"
                : $"C{run.CircuitIndex + 1}/{run.TotalCircuits}  R{run.RaceIndex + 1}/{run.RacesPerCircuit}";
        }

        private void RefreshDelta(RaceManager m, RaceCarStatus me)
        {
            float cur = m.CurrentLapTimeS(me);
            string d = RaceDisplay.FormatPaceDelta(cur, me.BestLapTimeS);
            _delta.style.display = d.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (d.Length == 0) return;
            _delta.text = $"({d})";
            bool ahead = cur - me.BestLapTimeS <= 0f;
            _delta.EnableInClassList("green", ahead);
            _delta.EnableInClassList("amber", !ahead);
        }

        private void RefreshCutoff(RaceManager m, RaceCarStatus me)
        {
            if (me.State != CarRaceState.Racing) { _cutoff.style.display = DisplayStyle.None; return; }

            if (m.WinnerFinished)
            {
                float remaining = Mathf.Max(0f, m.CutoffDeadlineS - m.RaceTimeS);
                ShowLine(_cutoff, $"TIME LIMIT  {remaining:0.0}s", "red");
                return;
            }

            var board = m.Leaderboard;
            if (board == null || board.Count == 0) { _cutoff.style.display = DisplayStyle.None; return; }
            float excess = RaceDisplay.ProjectedPaceExcess01(board[0].TotalDistanceM, me.TotalDistanceM, m.TrackLengthM);
            string text = RaceDisplay.FormatCutoffPace(excess, m.CutoffFraction);
            if (text.Length > 0) ShowLine(_cutoff, text, "amber");
            else _cutoff.style.display = DisplayStyle.None;
        }

        private void RefreshPayout(RaceCarStatus me, System.Func<int, int> payout)
        {
            if (payout == null || me.State != CarRaceState.Racing) { _payout.style.display = DisplayStyle.None; return; }
            _payout.style.display = DisplayStyle.Flex;
            _payout.text = RaceDisplay.FormatPayoutPreview(me.Position, payout(me.Position), payout(1));
        }

        private void RefreshVerdict(RaceCarStatus me)
        {
            switch (me.State)
            {
                case CarRaceState.Finished:
                    _verdict.style.display = DisplayStyle.Flex;
                    _verdict.RemoveFromClassList("bad");
                    _verdict.text = me.Position == 1 ? "WINNER" : $"FINISHED P{me.Position}";
                    break;
                case CarRaceState.Eliminated:
                    _verdict.style.display = DisplayStyle.Flex;
                    _verdict.AddToClassList("bad");
                    _verdict.text = "ELIMINATED";
                    break;
                default:
                    _verdict.style.display = DisplayStyle.None;
                    break;
            }
        }

        private void RefreshCues(VehicleController player, RaceCarStatus me)
        {
            bool racing = me.State == CarRaceState.Racing;
            VehicleSim sim = player.Sim;

            var draft = racing ? player.GetComponent<DraftSensor>() : null;
            SetCue(_draft, draft != null && draft.IsDrafting, "DRAFT — SLIPSTREAM");

            float gripDown = racing && sim != null ? 1f - sim.GripEffectMult : 0f;
            float powerDown = racing && sim != null ? 1f - sim.PowerEffectMult : 0f;
            SetCue(_sapGrip, gripDown > 0.02f, $"GRIP -{gripDown:P0}");
            SetCue(_sapPower, powerDown > 0.02f, $"POWER -{powerDown:P0}");

            var combat = racing ? player.GetComponent<VehicleCombat>() : null;
            bool hit = combat != null && Time.time - combat.LastAttackLandedRealtime < 0.7f;
            SetCue(_attack, hit, combat != null && combat.HasAura ? "DISRUPTING" : "ATTACK HIT!");
        }

        private void RefreshDura(VehicleController player)
        {
            float dur = Mathf.Clamp01(player.Durability);
            float wearT = Mathf.InverseLerp(1f, VehicleSim.MinDurability, dur);
            _duraFill.style.width = Length.Percent(dur * 100f);
            _duraFill.style.backgroundColor =
                Color.Lerp(new Color(0.37f, 0.85f, 0.48f), new Color(0.88f, 0.22f, 0.3f), wearT);
            _duraVal.text = dur.ToString("P0");
        }

        // ---- element factories / helpers -------------------------------------------------------

        private VisualElement BuildDura()
        {
            var row = new VisualElement();
            row.AddToClassList("hud-dura");
            var name = new Label { text = "DURA" };
            name.AddToClassList("hud-dura__name");
            var track = new VisualElement();
            track.AddToClassList("hud-dura__track");
            _duraFill.AddToClassList("hud-dura__fill");
            track.Add(_duraFill);
            _duraVal.AddToClassList("hud-dura__val");
            row.Add(name);
            row.Add(track);
            row.Add(_duraVal);
            return row;
        }

        private static VisualElement Corner(string cls)
        {
            var e = new VisualElement();
            e.AddToClassList(cls);
            return e;
        }

        private static Label Cap(string text)
        {
            var l = new Label { text = text };
            l.AddToClassList("hud-cap");
            return l;
        }

        private static VisualElement RightStat(string caption, Label value)
        {
            var item = new VisualElement();
            item.AddToClassList("hud-tr-item");
            item.Add(Cap(caption));
            value.AddToClassList("hud-time");
            item.Add(value);
            return item;
        }

        private static void ShowLine(Label e, string text, string cls)
        {
            e.style.display = DisplayStyle.Flex;
            e.text = text;
            e.EnableInClassList("amber", cls == "amber");
            e.EnableInClassList("red", cls == "red");
        }

        private static void SetCue(Label e, bool on, string text)
        {
            e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (on) e.text = text;
        }
    }
}
