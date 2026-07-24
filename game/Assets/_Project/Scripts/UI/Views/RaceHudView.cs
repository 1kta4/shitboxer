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
        private readonly Label _sectorCash = new Label();
        private readonly Label _verdict = new Label();
        private readonly Label _draft = new Label();
        private readonly Label _sapGrip = new Label();
        private readonly Label _sapPower = new Label();
        private readonly Label _attack = new Label();
        private readonly Label _sCash = new Label();
        private readonly Label _sProgress = new Label();
        private readonly VisualElement _duraFill = new VisualElement();
        private readonly Label _duraVal = new Label();
        private readonly Label _countdown = new Label();
        private readonly VisualElement _flash = new VisualElement();

        // --- sector timing strip (top-right, under the lap times) -------------------------------
        // Built lazily on the first refresh, because the sector count comes from the race and this view
        // is constructed before any race exists.
        private readonly VisualElement _sectorStrip = new VisualElement();
        private Label[] _sectorTimes = System.Array.Empty<Label>();
        private VisualElement[] _sectorBars = System.Array.Empty<VisualElement>();
        private VisualElement[] _sectorCells = System.Array.Empty<VisualElement>();

        // Last values pushed into the strip. A sector's time and colour change three times a LAP, but
        // Refresh runs every frame — and every style/text write dirties the element for UI Toolkit's
        // next layout pass, plus the time write allocates a string. Caching turns ~60 writes a second
        // into 3 a lap.
        private float[] _shownSectorTimes = System.Array.Empty<float>();
        private SectorColour[] _shownSectorColours = System.Array.Empty<SectorColour>();
        private int _shownCurrentSector = -1;
        private int _shownSectorCash = -1;

        // --- transient style readout (top-centre) ------------------------------------------------
        private readonly Label _styleFlash = new Label();

        /// <summary>Seconds the just-driven sector's style stays on screen before fading out.</summary>
        private const float StyleFlashWindowS = 2.4f;

        // Monotonic sector count last seen for the player, so a crossing can be detected without the
        // race layer having to carry a realtime stamp for a purely presentational fade.
        private int _lastSeenSectorCount = -1;
        private float _styleFlashStartedRealtime = -99f;
        private bool _styleFlashVisible;

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
            // How the last sector was DRIVEN — flashes on each crossing, then fades. Deliberately
            // transient: a permanent style readout would coach you through the sector, when the point is
            // to find out whether players can already tell what they just drove.
            _styleFlash.AddToClassList("hud-style");
            _styleFlash.style.display = DisplayStyle.None;
            tc.Add(_styleFlash);
            screen.Add(tc);

            // top-right: BEST / LAST lap, then the sector strip beneath them
            var tr = Corner("hud-tr");
            tr.Add(RightStat("BEST LAP", _best));
            tr.Add(RightStat("LAST LAP", _last));
            _sectorStrip.AddToClassList("hud-sectors");
            tr.Add(_sectorStrip);
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
            _sectorCash.AddToClassList("hud-line");
            _sectorCash.AddToClassList("green");
            bl.Add(_cutoff);
            bl.Add(_payout);
            bl.Add(_sectorCash);
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

            // full-screen overlays: the 3-2-1 countdown and the on-hit CRUNCH flash (were IMGUI)
            _countdown.AddToClassList("hud-countdown");
            _countdown.style.display = DisplayStyle.None;
            screen.Add(_countdown);

            _flash.AddToClassList("hud-flash");
            _flash.style.display = DisplayStyle.None;
            var crunch = new Label { text = "CRUNCH" };
            crunch.AddToClassList("hud-crunch");
            _flash.Add(crunch);
            screen.Add(_flash);

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
            RefreshSectors(m, me);
            RefreshStyleFlash(me, host.SectorParts);
            RefreshSectorCash(host.SectorParts);
            RefreshCutoff(m, me);
            RefreshPayout(me, payout);
            RefreshVerdict(me);
            RefreshCues(player, me);
            RefreshDura(player);
            RefreshCountdown(m);
            RefreshFlash(player);
        }

        /// <summary>
        /// The broadcast-style timing strip: one cell per sector showing this lap's time, with a colour
        /// bar underneath — purple for the session's fastest, green for a personal best, yellow otherwise.
        /// The cell for the sector currently being driven is highlighted so the strip also answers
        /// "where am I".
        /// </summary>
        private void RefreshSectors(RaceManager m, RaceCarStatus me)
        {
            int count = m.SectorsPerLap;
            if (count <= 0) { _sectorStrip.style.display = DisplayStyle.None; return; }
            if (_sectorTimes.Length != count) BuildSectorStrip(count);
            _sectorStrip.style.display = DisplayStyle.Flex;

            var times = me.LapSectorTimesS;
            var colours = me.LapSectorColours;
            bool racing = me.State == CarRaceState.Racing;
            int current = racing ? me.CurrentSector : -1;

            for (int i = 0; i < count; i++)
            {
                float t = i < times.Count ? times[i] : -1f;
                SectorColour c = i < colours.Count ? colours[i] : SectorColour.None;

                // Only touch the element when something actually changed — see the cache fields.
                if (!Mathf.Approximately(_shownSectorTimes[i], t))
                {
                    _sectorTimes[i].text = RaceDisplay.FormatSectorTime(t);
                    _shownSectorTimes[i] = t;
                }
                if (_shownSectorColours[i] != c)
                {
                    _sectorBars[i].style.backgroundColor = SectorTiming.ToColor(c);
                    // An un-set sector's bar is a faint placeholder rather than a solid grey block, so
                    // an empty strip reads as "not yet" instead of as a fourth colour tier.
                    _sectorBars[i].style.opacity = c == SectorColour.None ? 0.25f : 1f;
                    _shownSectorColours[i] = c;
                }
            }

            if (current != _shownCurrentSector)
            {
                for (int i = 0; i < count; i++)
                    _sectorCells[i].EnableInClassList("current", i == current);
                _shownCurrentSector = current;
            }
        }

        private void BuildSectorStrip(int count)
        {
            _sectorStrip.Clear();
            _sectorTimes = new Label[count];
            _sectorBars = new VisualElement[count];
            _sectorCells = new VisualElement[count];

            // Seed the cache to values the race can never produce, so the first refresh always writes.
            _shownSectorTimes = new float[count];
            _shownSectorColours = new SectorColour[count];
            for (int i = 0; i < count; i++)
            {
                _shownSectorTimes[i] = float.NaN;
                _shownSectorColours[i] = (SectorColour)byte.MaxValue;
            }
            _shownCurrentSector = -1;

            for (int i = 0; i < count; i++)
            {
                var cell = new VisualElement();
                cell.AddToClassList("hud-sector");

                var name = new Label { text = RaceDisplay.FormatSectorName(i) };
                name.AddToClassList("hud-sector__name");

                var value = new Label { text = RaceDisplay.FormatSectorTime(-1f) };
                value.AddToClassList("hud-sector__time");

                var bar = new VisualElement();
                bar.AddToClassList("hud-sector__bar");

                cell.Add(name);
                cell.Add(value);
                cell.Add(bar);
                _sectorStrip.Add(cell);

                _sectorTimes[i] = value;
                _sectorBars[i] = bar;
                _sectorCells[i] = cell;
            }
        }

        /// <summary>
        /// Flashes how the just-completed sector was driven, then fades. Detects the crossing by watching
        /// the monotonic sector counter rather than a timestamp, so the race layer carries no state that
        /// exists purely for a UI fade.
        /// </summary>
        private void RefreshStyleFlash(RaceCarStatus me, SectorPartRunner parts)
        {
            if (me.CompletedSectors != _lastSeenSectorCount)
            {
                bool firstSight = _lastSeenSectorCount < 0;
                _lastSeenSectorCount = me.CompletedSectors;
                // Don't flash on the very first observation — that's this view binding to a race already
                // in progress, not a sector the player just drove.
                if (!firstSight && me.LastSectorIndex >= 0)
                {
                    _styleFlashStartedRealtime = Time.time;
                    // What the sector paid rides on the SAME flash as how it was driven, so the causal
                    // link — "that money came from that style" — is one glance rather than two.
                    int cash = parts != null ? parts.LastSectorMoney : 0;
                    string paid = cash > 0 ? $"   +${cash}" : string.Empty;
                    _styleFlash.text =
                        $"{RaceDisplay.FormatSectorName(me.LastSectorIndex)}  {SectorStyleClassifier.Describe(me.LastSectorStyle)}{paid}";
                    _styleFlash.style.color = SectorTiming.ToColor(me.LastSectorColour);
                }
            }

            float since = Time.time - _styleFlashStartedRealtime;
            if (since < 0f || since > StyleFlashWindowS)
            {
                // Guarded: the flash is idle for most of a race, and an unconditional display write
                // would dirty the element for UI Toolkit's layout pass on every one of those frames.
                if (_styleFlashVisible)
                {
                    _styleFlash.style.display = DisplayStyle.None;
                    _styleFlashVisible = false;
                }
                return;
            }
            if (!_styleFlashVisible)
            {
                _styleFlash.style.display = DisplayStyle.Flex;
                _styleFlashVisible = true;
            }
            // Hold at full opacity for the first half, then ease out — so it is readable before it goes.
            float fade = Mathf.Clamp01(1f - (since / StyleFlashWindowS - 0.5f) * 2f);
            _styleFlash.style.opacity = fade;
        }

        private void RefreshCountdown(RaceManager m)
        {
            float remaining = m.CountdownRemainingS;
            bool go = remaining <= 0f && m.RaceTimeS >= 0f && m.RaceTimeS < 1.2f;
            if (remaining > 0f)
            {
                _countdown.style.display = DisplayStyle.Flex;
                _countdown.text = Mathf.Ceil(remaining).ToString("0");
            }
            else if (go)
            {
                _countdown.style.display = DisplayStyle.Flex;
                _countdown.text = "GO!";
            }
            else
            {
                _countdown.style.display = DisplayStyle.None;
            }
        }

        private void RefreshFlash(VehicleController player)
        {
            VehicleCombat combat = player.GetComponent<VehicleCombat>();
            const float window = 0.45f;
            float since = combat != null ? Time.time - combat.LastImpactRealtime : window + 1f;
            float severity = combat != null ? Mathf.Clamp01(combat.LastImpactSeverity) : 0f;

            // Only hard hits crunch; fades out over the window in well under half a second.
            if (since < 0f || since > window || severity < 0.45f)
            {
                _flash.style.display = DisplayStyle.None;
                return;
            }
            float fade = 1f - since / window;
            _flash.style.display = DisplayStyle.Flex;
            _flash.style.opacity = fade * fade * severity;
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

        /// <summary>
        /// Running total of what sector parts have paid this race. Hidden entirely at zero, so a loadout
        /// with no sector rules sees no line at all rather than a permanent "$0" — sectors pay nothing
        /// on their own (doc 08 decision 9) and the HUD should say so by omission.
        /// </summary>
        private void RefreshSectorCash(SectorPartRunner parts)
        {
            int earned = parts != null ? parts.MoneyEarned : 0;
            if (earned == _shownSectorCash) return;   // changes at most 9x a race; Refresh runs every frame
            _shownSectorCash = earned;

            _sectorCash.style.display = earned > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (earned > 0) _sectorCash.text = $"SECTORS  +${earned}";
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
                case CarRaceState.Retired:
                    _verdict.style.display = DisplayStyle.Flex;
                    _verdict.AddToClassList("bad");
                    _verdict.text = "RETIRED — CAR DESTROYED";
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
