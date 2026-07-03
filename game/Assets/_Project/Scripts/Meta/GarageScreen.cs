using System.Collections.Generic;
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

        public void Configure(RunDirector runDirector) => director = runDirector;

        private void OnGUI()
        {
            if (!director) return;

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
                    DrawEndScreen("CIRCUIT CLEARED — run complete!");
                    break;
            }
        }

        /// <summary>Small always-on box (top right, clear of the RaceHud) during the race.</summary>
        private void DrawRacingStatus()
        {
            RunState run = director.Run;
            GUILayout.BeginArea(new Rect(Screen.width - 250, 12, 238, 64), GUI.skin.box);
            GUILayout.Label($"$ {run.Money}    LIVES {run.Lives}");
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
            GUILayout.Label(run.IsBossRace
                ? $"$ {run.Money}    LIVES {run.Lives}    NEXT: BOSS race {run.RaceIndex + 1}/{run.RacesPerCircuit} (top {run.BossTopN} required)"
                : $"$ {run.Money}    LIVES {run.Lives}    NEXT: race {run.RaceIndex + 1}/{run.RacesPerCircuit}");

            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Space(6);
            GUILayout.Label("-- SHOP --");
            if (shop.Offers.Count == 0)
                GUILayout.Label("(sold out — reroll for fresh stock)");

            // Snapshot: buying mutates the offer list mid-draw.
            var offers = new List<PartDef>(shop.Offers);
            foreach (PartDef part in offers)
                DrawOffer(part, run);

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

        private void DrawOffer(PartDef part, RunState run)
        {
            if (!part) return;
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.BeginVertical();
            GUILayout.Label($"{part.DisplayName}  [{part.Category}]  ${part.Price}");
            if (!string.IsNullOrEmpty(part.Description))
                GUILayout.Label(part.Description);
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
