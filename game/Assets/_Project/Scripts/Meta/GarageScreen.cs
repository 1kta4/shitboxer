using System.Collections.Generic;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// Throwaway IMGUI garage (same style as RaceHud/VehicleDebugHud): run status, the three
    /// shop offers with Buy buttons, the escalating Reroll, owned-part equip toggles (max
    /// slots enforced by RunState) and NEXT RACE. Rendered while RunDirector holds the game
    /// paused (Time.timeScale = 0) between races; also draws a small run-status box during
    /// the race and the run-over / run-complete screens. Dies when a real UI arrives.
    /// </summary>
    public class GarageScreen : MonoBehaviour
    {
        [SerializeField] private RunDirector director;

        private Vector2 _scroll;
        private Vector2 _endScroll;

        // Part families listed in a stable order on the run-summary screen (see DrawOwnedPartsGrouped).
        private static readonly PartCategory[] PartCategories =
            { PartCategory.Stat, PartCategory.Economy, PartCategory.Attack };

        // Player's part-free base spec, snapshotted during the race (see TryCaptureBaseSpec) so the
        // garage can recompute Grip/Power for any equipped set without touching the live car.
        private VehicleController _playerCar;
        private VehicleSpec _baseSpec;

        public void Configure(RunDirector runDirector) => director = runDirector;

        private void OnGUI()
        {
            if (!director) return;

            TryCaptureBaseSpec();

            switch (director.Phase)
            {
                case RunPhase.Racing:
                    DrawRacingStatus();
                    break;
                case RunPhase.Garage:
                    DrawGarage();
                    break;
                case RunPhase.RunOver:
                    DrawEndScreen("RUN OVER — out of lives.");
                    break;
                case RunPhase.RunComplete:
                    DrawEndScreen("SEASON CLEARED!");
                    break;
            }
        }

        /// <summary>Small always-on box (top right, clear of the RaceHud) during the race.</summary>
        private void DrawRacingStatus()
        {
            RunState run = director.Run;
            GUILayout.BeginArea(new Rect(Screen.width - 250, 12, 238, 82), GUI.skin.box);
            GUILayout.Label($"$ {run.Money}    LIVES {run.Lives}");
            GUILayout.Label($"CIRCUIT {run.CircuitIndex + 1}/{run.TotalCircuits}");
            GUILayout.Label(run.IsBossRace
                ? $"BOSS RACE {run.RaceIndex + 1}/{run.RacesPerCircuit} — top {run.BossTopN} required"
                : $"RACE {run.RaceIndex + 1}/{run.RacesPerCircuit}");
            GUILayout.EndArea();
        }

        private void DrawGarage()
        {
            RunState run = director.Run;
            ShopLogic shop = director.Shop;

            const float width = 480f;
            GUILayout.BeginArea(new Rect((Screen.width - width) * 0.5f, 32f, width, Screen.height - 64f), GUI.skin.box);
            GUILayout.Label("== GARAGE ==");
            if (!string.IsNullOrEmpty(director.LastRaceSummary))
                GUILayout.Label(director.LastRaceSummary);
            GUILayout.Label($"CIRCUIT {run.CircuitIndex + 1}/{run.TotalCircuits}");
            GUILayout.Label(run.IsBossRace
                ? $"$ {run.Money}    LIVES {run.Lives}    NEXT: BOSS race {run.RaceIndex + 1}/{run.RacesPerCircuit} (top {run.BossTopN} required)"
                : $"$ {run.Money}    LIVES {run.Lives}    NEXT: race {run.RaceIndex + 1}/{run.RacesPerCircuit}");

            // Current headline stats for the equipped set the player will take into the next race.
            VehicleSpec current = _baseSpec != null ? SpecModApplier.Apply(_baseSpec, run.EquippedParts) : null;
            if (current != null)
            {
                StatSummary.Stats now = StatSummary.Compute(current);
                GUILayout.Space(4);
                DrawStatBar("GRIP", now.Grip, new Color(0.3f, 0.75f, 1f));
                DrawStatBar("POWER", now.Power, new Color(1f, 0.55f, 0.2f));
            }

            // Persistent wear carries across races; the garage is the only place to pay it back off.
            if (run.CarDurability < 1f)
            {
                int repairCost = director.RepairCost;
                GUILayout.Space(4);
                GUI.enabled = run.Money >= repairCost;
                if (GUILayout.Button($"REPAIR CAR (${repairCost}) — durability {run.CarDurability * 100f:0}%"))
                    director.RepairCar();
                GUI.enabled = true;
            }

            _scroll = GUILayout.BeginScrollView(_scroll);

            // An open crate is already paid for, so it takes over the shelf until it's resolved — the pick
            // IS the transaction, and leaving the shop live underneath would let the player wander off and
            // forget cash they've already spent.
            if (run.CrateOpen)
                DrawCratePick(run, current);
            else
                DrawShelf(run, shop, current);

            GUILayout.Space(10);
            GUILayout.Label($"-- OWNED PARTS ({run.EquippedParts.Count}/{run.MaxEquipSlots} slots used) --");
            if (run.OwnedParts.Count == 0)
                GUILayout.Label("(none yet — buy something)");
            foreach (PartDef part in run.OwnedParts)
                DrawOwnedPart(part, run);

            GUILayout.EndScrollView();

            GUILayout.Space(8);
            if (GUILayout.Button("NEXT RACE", GUILayout.Height(36)))
                director.StartNextRace();

            GUILayout.EndArea();
        }

        /// <summary>The normal shelf: three rarity-weighted part offers, a crate, and the escalating reroll.</summary>
        private void DrawShelf(RunState run, ShopLogic shop, VehicleSpec current)
        {
            GUILayout.Space(6);
            GUILayout.Label("-- SHOP --");
            if (shop.Offers.Count == 0)
                GUILayout.Label("(sold out — reroll for fresh stock)");

            // Snapshot: buying mutates the offer list mid-draw.
            var offers = new List<PartDef>(shop.Offers);
            foreach (PartDef part in offers)
                DrawOffer(part, run, current);

            int cratePrice = director.CratePrice;
            GUI.enabled = run.Money >= cratePrice;
            if (GUILayout.Button($"BUY PARTS CRATE  (${cratePrice}) — open 3, keep 1"))
                director.BuyCrate();
            GUI.enabled = true;

            GUI.enabled = run.Money >= shop.RerollCost;
            if (GUILayout.Button($"REROLL  (${shop.RerollCost})"))
                director.RerollShop();
            GUI.enabled = true;
        }

        /// <summary>
        /// The open-crate pick. Everything drawn is free to take — the cost was paid on buy — so the only
        /// decision is which one, and the rest are discarded. Stat parts still get the BEFORE→AFTER preview,
        /// since "which one" is exactly the question that preview exists to answer.
        /// </summary>
        private void DrawCratePick(RunState run, VehicleSpec current)
        {
            GUILayout.Space(6);
            GUILayout.Label("-- PARTS CRATE — KEEP ONE --");
            GUILayout.Label("(already paid for; the rest are scrapped)");

            // Snapshot: taking clears the contents mid-draw.
            var contents = new List<PartDef>(run.CrateContents);
            foreach (PartDef part in contents)
                DrawCrateItem(part, run, current);
        }

        private void DrawCrateItem(PartDef part, RunState run, VehicleSpec current)
        {
            if (!part) return;
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.BeginVertical();
            GUILayout.Label($"{part.DisplayName}  [{part.Category}]");
            DrawEditionTag(part.Edition);
            if (!string.IsNullOrEmpty(part.Description))
                GUILayout.Label(part.Description);
            DrawStatPreview(part, current);
            GUILayout.EndVertical();
            if (GUILayout.Button("KEEP", GUILayout.Width(64), GUILayout.ExpandHeight(true)))
                director.TakeFromCrate(part);
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Shared BEFORE→AFTER preview of a Stat part's effect on the two headline bars. Applying the single
        /// part on top of the current spec matches equipping it (mods are multiplicative) and never touches
        /// run state. Shared by the shelf and the crate pick so the two can't describe the same part
        /// differently.
        /// </summary>
        private static void DrawStatPreview(PartDef part, VehicleSpec current)
        {
            if (part.Category != PartCategory.Stat || current == null) return;
            StatSummary.Stats before = StatSummary.Compute(current);
            StatSummary.Stats after = StatSummary.Compute(SpecModApplier.Apply(current, new[] { part }));
            DrawStatDelta("GRIP", before.Grip, after.Grip);
            DrawStatDelta("POWER", before.Power, after.Power);
        }

        private void DrawOffer(PartDef part, RunState run, VehicleSpec current)
        {
            if (!part) return;
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.BeginVertical();
            GUILayout.Label($"{part.DisplayName}  [{part.Category}]  ${part.Price}");
            DrawEditionTag(part.Edition);
            if (!string.IsNullOrEmpty(part.Description))
                GUILayout.Label(part.Description);

            DrawStatPreview(part, current);
            GUILayout.EndVertical();
            GUI.enabled = run.Money >= part.Price;
            if (GUILayout.Button("BUY", GUILayout.Width(64), GUILayout.ExpandHeight(true)))
                director.BuyOffer(part);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void DrawOwnedPart(PartDef part, RunState run)
        {
            if (!part) return;
            bool equipped = run.IsEquipped(part);

            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label($"{part.DisplayName}  [{part.Category}]{(equipped ? "  — EQUIPPED" : "")}");
            DrawEditionTag(part.Edition);
            GUILayout.FlexibleSpace();
            if (equipped)
            {
                if (GUILayout.Button("UNEQUIP", GUILayout.Width(80)))
                    run.Unequip(part);
            }
            else
            {
                GUI.enabled = run.HasFreeSlot;
                if (GUILayout.Button("EQUIP", GUILayout.Width(80)))
                    run.Equip(part);
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Snapshots the player's part-free base spec so the garage can preview any equipped set.
        /// RunDirector bakes equipped stat parts into the live car spec at scene load, so the car
        /// carries its authored (base) spec exactly when no stat part is equipped — guaranteed at
        /// least during race 1 of every run. We only read it during the race (the equipped set can't
        /// be edited then) and deep-clone it so a later SetSpec swap can't mutate our snapshot.
        /// Read-only w.r.t. run state.
        /// </summary>
        private void TryCaptureBaseSpec()
        {
            if (director.Phase != RunPhase.Racing || HasEquippedStatPart()) return;
            if (!_playerCar)
            {
                var provider = FindFirstObjectByType<VehicleInputProvider>();
                _playerCar = provider ? provider.GetComponent<VehicleController>() : null;
            }
            if (_playerCar && _playerCar.SpecAsset != null)
                _baseSpec = SpecModApplier.Clone(_playerCar.SpecAsset.Spec);
        }

        private bool HasEquippedStatPart()
        {
            foreach (PartDef part in director.Run.EquippedParts)
                if (part && part.Category == PartCategory.Stat) return true;
            return false;
        }

        private static void DrawStatBar(string label, float value, Color fill)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(52));

            Rect track = GUILayoutUtility.GetRect(120, 12, GUILayout.ExpandWidth(true));
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(track, Texture2D.whiteTexture);
            GUI.color = fill;
            float t = Mathf.Clamp01(value / 100f);
            GUI.DrawTexture(new Rect(track.x, track.y, track.width * t, track.height), Texture2D.whiteTexture);
            GUI.color = prev;

            GUILayout.Label(value.ToString("0"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
        }

        /// <summary>One BEFORE -> AFTER line, tinted green for a gain and red for a loss.</summary>
        private static void DrawStatDelta(string label, float before, float after)
        {
            float d = after - before;
            Color prev = GUI.color;
            if (d > 0.5f) GUI.color = new Color(0.4f, 1f, 0.4f);
            else if (d < -0.5f) GUI.color = new Color(1f, 0.45f, 0.45f);
            string delta = Mathf.Abs(d) < 0.5f ? "" : $"  ({(d > 0f ? "+" : "")}{d:0})";
            GUILayout.Label($"{label} {before:0} -> {after:0}{delta}");
            GUI.color = prev;
        }

        /// <summary>
        /// End-of-run RUN SUMMARY, shared by RunOver ("RUN OVER") and RunComplete ("SEASON
        /// CLEARED!") — the caller supplies the headline, which is the only difference between the
        /// two verdicts. Reports how the run finished (circuits/races reached, final wallet, lives
        /// left, the last race's verdict) and lists everything bought this run grouped by part
        /// family. Scrolls when the parts list runs long. Read-only w.r.t. run state; NEW RUN kicks
        /// a fresh run off through the director.
        /// </summary>
        private void DrawEndScreen(string headline)
        {
            RunState run = director.Run;
            const float width = 460f;
            float height = Mathf.Min(Screen.height - 64f, 520f);
            GUILayout.BeginArea(
                new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height),
                GUI.skin.box);

            GUILayout.Label(headline);
            if (!string.IsNullOrEmpty(director.LastRaceSummary))
                GUILayout.Label(director.LastRaceSummary);

            GUILayout.Space(6);
            GUILayout.Label("== RUN SUMMARY ==");
            GUILayout.Label($"Circuits cleared: {run.CircuitIndex}/{run.TotalCircuits}");
            GUILayout.Label($"Reached race {run.RaceIndex}/{run.RacesPerCircuit} of the current circuit");
            GUILayout.Label($"Final money: ${run.Money}");
            GUILayout.Label($"Lives remaining: {run.Lives}");

            DrawRecordsSection();

            GUILayout.Space(6);
            GUILayout.Label($"-- OWNED PARTS ({run.OwnedParts.Count}) --");

            _endScroll = GUILayout.BeginScrollView(_endScroll);
            if (run.OwnedParts.Count == 0)
                GUILayout.Label("(none — bought nothing this run)");
            else
                DrawOwnedPartsGrouped(run);
            GUILayout.EndScrollView();

            GUILayout.Space(8);
            if (GUILayout.Button("NEW RUN", GUILayout.Height(32)))
                director.StartNewRun();

            GUILayout.EndArea();
        }

        /// <summary>Owned parts as a name-per-line list under a header for each non-empty part family.</summary>
        private void DrawOwnedPartsGrouped(RunState run)
        {
            foreach (PartCategory category in PartCategories)
            {
                int count = 0;
                foreach (PartDef part in run.OwnedParts)
                    if (part && part.Category == category) count++;
                if (count == 0) continue;

                GUILayout.Space(4);
                GUILayout.Label($"[{category}]  ({count})");
                foreach (PartDef part in run.OwnedParts)
                {
                    if (!part || part.Category != category) continue;
                    string tag = run.IsEquipped(part) ? "  — EQUIPPED" : "";
                    string edition = EditionTag(part.Edition);
                    string editionSuffix = edition.Length > 0 ? "  " + edition : "";
                    GUILayout.Label($"    {part.DisplayName}{editionSuffix}{tag}");
                }
            }
        }

        // ---- Editions + records display helpers -------------------------------------------------
        // The formatting helpers below (EditionTag / FormatLapTime / RunHistoryLine) are pure and
        // engine-loop-free (no Time/Input/scene reads) so they can be unit-tested in GarageDisplayTests
        // without a live scene; everything else here is OnGUI-only drawing. Display only — no read or
        // write of driving physics or the run economy, so today's numbers are untouched.

        /// <summary>How many best-lap records / recent runs the end screen lists at most.</summary>
        private const int MaxRecordsShown = 5;

        /// <summary>
        /// Short bracketed edition tag with its stat-effect magnitude, e.g. "[FOIL x1.25]". Returns ""
        /// for <see cref="PartEdition.None"/> so an un-editioned part shows nothing extra and looks
        /// exactly as it does today. The magnitude is <see cref="PartEditionInfo.StatMult"/> — the same
        /// factor SpecModApplier scales the part's effect by. Pure/static, so it is unit-testable.
        /// </summary>
        public static string EditionTag(PartEdition edition)
        {
            if (edition == PartEdition.None) return "";
            return $"[{edition.ToString().ToUpperInvariant()} x{PartEditionInfo.StatMult(edition):0.##}]";
        }

        /// <summary>
        /// Formats a lap time in seconds as "M:SS.mm", or "--" for a non-positive / missing time
        /// (<see cref="MetaProgress.NoLapRecord"/> is 0). Mirrors RaceHud's mm:ss readout style.
        /// Pure/static — unit-testable without a scene.
        /// </summary>
        public static string FormatLapTime(float seconds)
        {
            if (seconds <= 0f) return "--";
            int minutes = (int)(seconds / 60f);
            return $"{minutes}:{seconds - minutes * 60f:00.00}";
        }

        /// <summary>
        /// One compact line summarising a finished run for the end-screen history list, e.g.
        /// "License 1 - 2 circuits - $37". The stake is shown 1-based as a human "License N". Pure/static.
        /// </summary>
        public static string RunHistoryLine(RunHistoryEntry entry)
        {
            string circuits = entry.circuitsCleared == 1 ? "1 circuit" : $"{entry.circuitsCleared} circuits";
            return $"License {entry.stakeLevel + 1} - {circuits} - ${entry.finalMoney}";
        }

        /// <summary>Tint for a non-None edition tag; falls through to the current GUI colour otherwise.</summary>
        private static Color EditionColor(PartEdition edition)
        {
            switch (edition)
            {
                case PartEdition.Foil: return new Color(0.60f, 0.85f, 1f);       // icy blue
                case PartEdition.Holo: return new Color(0.70f, 1f, 0.70f);       // green
                case PartEdition.Polychrome: return new Color(1f, 0.70f, 1f);    // magenta
                default: return GUI.color;
            }
        }

        /// <summary>
        /// Draws a compact, edition-tinted tag label (e.g. "[FOIL x1.25]") for a non-None edition, or
        /// nothing at all for <see cref="PartEdition.None"/> — so an un-editioned part looks unchanged.
        /// </summary>
        private static void DrawEditionTag(PartEdition edition)
        {
            string tag = EditionTag(edition);
            if (tag.Length == 0) return;
            Color prev = GUI.color;
            GUI.color = EditionColor(edition);
            GUILayout.Label(tag);
            GUI.color = prev;
        }

        /// <summary>
        /// End-screen RECORDS block: the persistent per-track best laps and the last few finished runs
        /// from the cross-run <see cref="MetaProgress"/> profile (read-only). Guarded so a fresh profile
        /// with no history draws a dash / "(none)" note rather than an empty gap. Purely a display of
        /// stored history — reads no gameplay or economy state and mutates nothing.
        /// </summary>
        private void DrawRecordsSection()
        {
            MetaProgress meta = director.Meta;
            GUILayout.Space(6);
            GUILayout.Label("== RECORDS ==");
            if (meta == null)
            {
                GUILayout.Label("-");
                return;
            }

            GUILayout.Label("-- BEST LAPS --");
            List<LapRecord> laps = meta.lapRecords;
            if (laps == null || laps.Count == 0)
            {
                GUILayout.Label("    (no lap records yet)");
            }
            else
            {
                int shown = Mathf.Min(laps.Count, MaxRecordsShown);
                for (int i = 0; i < shown; i++)
                    GUILayout.Label($"    {laps[i].trackId}: {FormatLapTime(laps[i].lapSeconds)}");
            }

            GUILayout.Space(4);
            GUILayout.Label("-- RECENT RUNS --");
            List<RunHistoryEntry> history = meta.runHistory;
            if (history == null || history.Count == 0)
            {
                GUILayout.Label("    (no finished runs yet)");
            }
            else
            {
                // runHistory is oldest-first; list the newest few, newest at the top.
                int shown = Mathf.Min(history.Count, MaxRecordsShown);
                for (int i = 0; i < shown; i++)
                    GUILayout.Label($"    {RunHistoryLine(history[history.Count - 1 - i])}");
            }
        }
    }
}
