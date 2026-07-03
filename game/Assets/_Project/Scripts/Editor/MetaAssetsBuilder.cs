using System.Collections.Generic;
using Shitboxer.Meta;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shitboxer.Editor
{
    /// <summary>
    /// Phase 3 content bootstrap. "Build Meta Assets" creates the placeholder part catalogue
    /// (doc 03's stat/economy/attack split) plus the PartPool listing them — idempotent: an
    /// existing PartDef asset is never overwritten so inspector tuning survives a rebuild,
    /// while the pool's list is refreshed every run. "Add Run Mode To Race Scene" drops a
    /// RunRig (RunDirector + RunBootstrap, wired to the pool) into RaceTest.unity so pressing
    /// Play runs the full circuit loop instead of a single race.
    /// </summary>
    public static class MetaAssetsBuilder
    {
        private const string SettingsDir = "Assets/_Project/Settings";
        private const string PartsDir = SettingsDir + "/Parts";
        private const string PoolPath = PartsDir + "/PartPool.asset";
        private const string RaceScenePath = "Assets/_Project/Scenes/RaceTest.unity";

        [MenuItem("Shitboxer/Build Meta Assets")]
        public static void BuildMetaAssets()
        {
            EnsureFolders();

            var parts = new List<PartDef>
            {
                // ---- Stat parts (grip / power / weight / downforce flavours) ----
                EnsurePart("Part_StickyCompound", p =>
                {
                    p.Id = "sticky_compound";
                    p.DisplayName = "Sticky Compound";
                    p.Description = "Gummy rubber all round. +10% grip front and rear.";
                    p.Category = PartCategory.Stat;
                    p.Price = 6;
                    p.SpecMods = Mods((SpecModTarget.GripFront, 1.10f), (SpecModTarget.GripRear, 1.10f));
                }),
                EnsurePart("Part_RaceRears", p =>
                {
                    p.Id = "race_rears";
                    p.DisplayName = "Race Rears";
                    p.Description = "Slicks on the back axle only. +12% rear grip.";
                    p.Category = PartCategory.Stat;
                    p.Price = 4;
                    p.SpecMods = Mods((SpecModTarget.GripRear, 1.12f));
                }),
                EnsurePart("Part_JunkyardTurbo", p =>
                {
                    p.Id = "junkyard_turbo";
                    p.DisplayName = "Junkyard Turbo";
                    p.Description = "Whistles ominously. +15% engine torque.";
                    p.Category = PartCategory.Stat;
                    p.Price = 8;
                    p.SpecMods = Mods((SpecModTarget.Power, 1.15f));
                }),
                EnsurePart("Part_ChippedEcu", p =>
                {
                    p.Id = "chipped_ecu";
                    p.DisplayName = "Chipped ECU";
                    p.Description = "Warranty voided. +6% engine torque, cheap.";
                    p.Category = PartCategory.Stat;
                    p.Price = 3;
                    p.SpecMods = Mods((SpecModTarget.Power, 1.06f));
                }),
                EnsurePart("Part_GuttedInterior", p =>
                {
                    p.Id = "gutted_interior";
                    p.DisplayName = "Gutted Interior";
                    p.Description = "Who needs seats? -8% mass.";
                    p.Category = PartCategory.Stat;
                    p.Price = 5;
                    p.SpecMods = Mods((SpecModTarget.Weight, 0.92f));
                }),
                EnsurePart("Part_ParkBenchWing", p =>
                {
                    p.Id = "park_bench_wing";
                    p.DisplayName = "Park Bench Wing";
                    p.Description = "Enormous, embarrassing, effective. +50% downforce, +2% mass.";
                    p.Category = PartCategory.Stat;
                    p.Price = 5;
                    p.SpecMods = Mods((SpecModTarget.Downforce, 1.50f), (SpecModTarget.Weight, 1.02f));
                }),

                // ---- Economy parts (payout hook only, this phase) ----
                EnsurePart("Part_SponsorLivery", p =>
                {
                    p.Id = "sponsor_livery";
                    p.DisplayName = "Sponsor Livery";
                    p.Description = "Backmarker TV time pays. +$1 per finishing position each race.";
                    p.Category = PartCategory.Economy;
                    p.Price = 5;
                    p.MoneyPerPositionHeld = 1;
                }),
                EnsurePart("Part_TeamAccountant", p =>
                {
                    p.Id = "team_accountant";
                    p.DisplayName = "Team Accountant";
                    p.Description = "Squeezes the sponsors properly. +$2 per finishing position each race.";
                    p.Category = PartCategory.Economy;
                    p.Price = 8;
                    p.MoneyPerPositionHeld = 2;
                }),
                EnsurePart("Part_ScrapDealer", p =>
                {
                    p.Id = "scrap_dealer";
                    p.DisplayName = "Scrap Dealer";
                    p.Description = "Knows a guy. +$1 per finishing position (sell-for-cash hook comes later).";
                    p.Category = PartCategory.Economy;
                    p.Price = 4;
                    p.MoneyPerPositionHeld = 1;
                }),

                // ---- Attack parts (placeholder data; resolution is a later phase) ----
                EnsurePart("Part_RamBars", p =>
                {
                    p.Id = "ram_bars";
                    p.DisplayName = "Ram Bars";
                    p.Description = "Hit them harder than they hit you. (Does nothing yet.)";
                    p.Category = PartCategory.Attack;
                    p.Price = 6;
                    p.ContactDamageMult = 1.5f;
                }),
                EnsurePart("Part_SpikePlates", p =>
                {
                    p.Id = "spike_plates";
                    p.DisplayName = "Spike Plates";
                    p.Description = "Contact costs THEM. (Does nothing yet.)";
                    p.Category = PartCategory.Attack;
                    p.Price = 7;
                    p.ContactDamageMult = 2.0f;
                }),
                EnsurePart("Part_DisruptorField", p =>
                {
                    p.Id = "disruptor_field";
                    p.DisplayName = "Disruptor Field";
                    p.Description = "Nearby rivals lose their nerve. 6 m aura. (Does nothing yet.)";
                    p.Category = PartCategory.Attack;
                    p.Price = 8;
                    p.ContactDamageMult = 1f;
                    p.AuraRadiusM = 6f;
                }),
            };

            // The pool is refreshed every run so new parts always show up.
            var pool = AssetDatabase.LoadAssetAtPath<PartPool>(PoolPath);
            if (pool == null)
            {
                pool = ScriptableObject.CreateInstance<PartPool>();
                AssetDatabase.CreateAsset(pool, PoolPath);
            }
            pool.Parts = parts;
            EditorUtility.SetDirty(pool);

            AssetDatabase.SaveAssets();
            Debug.Log($"[Shitboxer] Meta assets ready — {parts.Count} parts in {PartsDir}, pool at {PoolPath}.");
        }

        [MenuItem("Shitboxer/Add Run Mode To Race Scene")]
        public static void AddRunModeToRaceScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(RaceScenePath) == null)
            {
                Debug.LogError($"[Shitboxer] {RaceScenePath} not found — run 'Shitboxer/Build Race Test Scene' first.");
                return;
            }

            BuildMetaAssets(); // idempotent; guarantees the PartPool exists
            var pool = AssetDatabase.LoadAssetAtPath<PartPool>(PoolPath);

            Scene scene = EditorSceneManager.OpenScene(RaceScenePath, OpenSceneMode.Single);

            GameObject rig = GameObject.Find("RunRig");
            if (rig == null) rig = new GameObject("RunRig");

            var director = rig.GetComponent<RunDirector>();
            if (!director) director = rig.AddComponent<RunDirector>();
            if (!rig.GetComponent<RunBootstrap>()) rig.AddComponent<RunBootstrap>();
            director.Configure(pool);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Shitboxer] Run mode added to RaceTest.unity — press Play for the full circuit loop (heats, then a boss race).");
        }

        // ------------------------------------------------------------------ helpers

        private static void EnsureFolders()
        {
            foreach (string dir in new[] { "Assets/_Project", SettingsDir, PartsDir })
            {
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    string parent = dir.Substring(0, dir.LastIndexOf('/'));
                    AssetDatabase.CreateFolder(parent, dir.Substring(dir.LastIndexOf('/') + 1));
                }
            }
        }

        /// <summary>Creates a PartDef asset only if missing, so hand-tuning survives a rebuild.</summary>
        private static PartDef EnsurePart(string fileName, System.Action<PartDef> configure)
        {
            string path = $"{PartsDir}/{fileName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<PartDef>(path);
            if (existing != null) return existing;

            var part = ScriptableObject.CreateInstance<PartDef>();
            configure(part);
            AssetDatabase.CreateAsset(part, path);
            return part;
        }

        private static List<SpecMod> Mods(params (SpecModTarget target, float multiplier)[] entries)
        {
            var mods = new List<SpecMod>(entries.Length);
            foreach ((SpecModTarget target, float multiplier) in entries)
                mods.Add(new SpecMod { Target = target, Multiplier = multiplier });
            return mods;
        }
    }
}
