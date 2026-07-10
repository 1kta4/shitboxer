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
                    DrawEndScreen("SEASON CLEARED — run complete!");
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

            GUILayout.Space(6);
            GUILayout.Label("-- SHOP --");
            if (shop.Offers.Count == 0)
                GUILayout.Label("(sold out — reroll for fresh stock)");

            // Snapshot: buying mutates the offer list mid-draw.
            var offers = new List<PartDef>(shop.Offers);
            foreach (PartDef part in offers)
                DrawOffer(part, run, current);

            GUI.enabled = run.Money >= shop.RerollCost;
            if (GUILayout.Button($"REROLL  (${shop.RerollCost})"))
                director.RerollShop();
            GUI.enabled = true;

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

        private void DrawOffer(PartDef part, RunState run, VehicleSpec current)
        {
            if (!part) return;
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.BeginVertical();
            GUILayout.Label($"{part.DisplayName}  [{part.Category}]  ${part.Price}");
            if (!string.IsNullOrEmpty(part.Description))
                GUILayout.Label(part.Description);

            // Preview a Stat part's effect: BEFORE -> AFTER on the two headline bars. Applying the
            // single part on top of the current spec matches equipping it (mods are multiplicative),
            // and never touches run state.
            if (part.Category == PartCategory.Stat && current != null)
            {
                StatSummary.Stats before = StatSummary.Compute(current);
                StatSummary.Stats after = StatSummary.Compute(SpecModApplier.Apply(current, new[] { part }));
                DrawStatDelta("GRIP", before.Grip, after.Grip);
                DrawStatDelta("POWER", before.Power, after.Power);
            }
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

        private void DrawEndScreen(string headline)
        {
            RunState run = director.Run;
            const float width = 400f;
            GUILayout.BeginArea(new Rect((Screen.width - width) * 0.5f, Screen.height * 0.3f, width, 220f), GUI.skin.box);
            GUILayout.Label(headline);
            if (!string.IsNullOrEmpty(director.LastRaceSummary))
                GUILayout.Label(director.LastRaceSummary);
            GUILayout.Label($"Ended with ${run.Money}, {run.Lives} lives, {run.OwnedParts.Count} parts owned.");
            GUILayout.Space(12);
            if (GUILayout.Button("NEW RUN", GUILayout.Height(32)))
                director.StartNewRun();
            GUILayout.EndArea();
        }
    }
}
