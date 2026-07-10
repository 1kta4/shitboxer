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

                // ---- Stat parts with real trade-offs + slot-order depth (doc 03) ----
                // Additive anchors (Op=Add) want to sit LEFT of the x-payoffs below: a +grip
                // Add resolved before a x1.20 grip Multiply beats the reverse (SpecModApplier).
                EnsurePart("Part_Coilovers", p =>
                {
                    p.Id = "coilovers";
                    p.DisplayName = "Coilovers";
                    p.Description = "Adjustable stiff setup. +14% grip front and rear (additive — slot it LEFT of a x-grip part for more), but +3% mass.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Uncommon;
                    p.Price = 7;
                    p.SpecMods = ModList(
                        AddPct(SpecModTarget.GripFront, 0.14f),
                        AddPct(SpecModTarget.GripRear, 0.14f),
                        Mul(SpecModTarget.Weight, 1.03f));
                }),
                EnsurePart("Part_StiffSprings", p =>
                {
                    p.Id = "stiff_springs";
                    p.DisplayName = "Stiff Springs";
                    p.Description = "Plants the nose, unsettles the tail. +10% front grip (additive), -4% rear grip.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Common;
                    p.Price = 4;
                    p.SpecMods = ModList(
                        AddPct(SpecModTarget.GripFront, 0.10f),
                        AddPct(SpecModTarget.GripRear, -0.04f));
                }),
                EnsurePart("Part_BigCam", p =>
                {
                    p.Id = "big_cam";
                    p.DisplayName = "Big Cam";
                    p.Description = "Lumpy idle, angry top end. +12% torque (additive — pair it before a x-power part), but the extra shove costs -3% rear grip.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Uncommon;
                    p.Price = 6;
                    p.SpecMods = ModList(
                        AddPct(SpecModTarget.Power, 0.12f),
                        Mul(SpecModTarget.GripRear, 0.97f));
                }),
                EnsurePart("Part_LightFlywheel", p =>
                {
                    p.Id = "light_flywheel";
                    p.DisplayName = "Lightweight Flywheel";
                    p.Description = "Revs snap up instantly. +9% torque (additive), but the snappier throttle costs -2% front grip.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Uncommon;
                    p.Price = 6;
                    p.SpecMods = ModList(
                        AddPct(SpecModTarget.Power, 0.09f),
                        Mul(SpecModTarget.GripFront, 0.98f));
                }),
                EnsurePart("Part_RaceSlicks", p =>
                {
                    p.Id = "race_slicks";
                    p.DisplayName = "Race Slicks";
                    p.Description = "Full soft compound. x1.20 grip front and rear — wants every additive grip part to ITS LEFT — but the extra rubber adds +3% mass.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Rare;
                    p.Price = 12;
                    p.SpecMods = ModList(
                        Mul(SpecModTarget.GripFront, 1.20f),
                        Mul(SpecModTarget.GripRear, 1.20f),
                        Mul(SpecModTarget.Weight, 1.03f));
                }),
                EnsurePart("Part_BigTurbo", p =>
                {
                    p.Id = "big_turbo";
                    p.DisplayName = "Big Turbo";
                    p.Description = "Comically oversized snail. x1.25 torque (stack Big Cam to its left first), but the spikes overwhelm the tail (-6% rear grip) and the plumbing adds +2% mass.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Rare;
                    p.Price = 11;
                    p.SpecMods = ModList(
                        Mul(SpecModTarget.Power, 1.25f),
                        Mul(SpecModTarget.GripRear, 0.94f),
                        Mul(SpecModTarget.Weight, 1.02f));
                }),
                EnsurePart("Part_SemiSlicks", p =>
                {
                    p.Id = "semi_slicks";
                    p.DisplayName = "Semi-Slicks";
                    p.Description = "Rear-biased treadless tyres. x1.12 rear grip, but -2% front grip. Slots to the RIGHT of your additive grip parts.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Common;
                    p.Price = 5;
                    p.SpecMods = ModList(
                        Mul(SpecModTarget.GripRear, 1.12f),
                        Mul(SpecModTarget.GripFront, 0.98f));
                }),
                EnsurePart("Part_BigWing", p =>
                {
                    p.Id = "big_wing";
                    p.DisplayName = "Big Wing";
                    p.Description = "Proper motorsport aero. x1.60 downforce for high-speed grip, but the drag and hardware cost +4% mass (and top speed).";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Uncommon;
                    p.Price = 7;
                    p.SpecMods = ModList(
                        Mul(SpecModTarget.Downforce, 1.60f),
                        Mul(SpecModTarget.Weight, 1.04f));
                }),
                EnsurePart("Part_CarbonTub", p =>
                {
                    p.Id = "carbon_tub";
                    p.DisplayName = "Carbon Tub";
                    p.Description = "Featherweight monocoque. -15% mass, but the stripped-back body loses -5% downforce. Pricey.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Rare;
                    p.Price = 12;
                    p.SpecMods = ModList(
                        Mul(SpecModTarget.Weight, 0.85f),
                        Mul(SpecModTarget.Downforce, 0.95f));
                }),
                EnsurePart("Part_StrippedPanels", p =>
                {
                    p.Id = "stripped_panels";
                    p.DisplayName = "Stripped Panels";
                    p.Description = "Binned the bumpers and glass. -5% mass, but -4% downforce with less bodywork to bite the air.";
                    p.Category = PartCategory.Stat;
                    p.Rarity = Rarity.Common;
                    p.Price = 4;
                    p.SpecMods = ModList(
                        Mul(SpecModTarget.Weight, 0.95f),
                        Mul(SpecModTarget.Downforce, 0.96f));
                }),

                // ---- Economy parts (payout hook only, this phase) ----
                EnsurePart("Part_SponsorLivery", p =>
                {
                    p.Id = "sponsor_livery";
                    p.DisplayName = "Sponsor Livery";
                    p.Description = "Backmarker TV time pays. +$1 per finishing position each race, capped at mid-pack (no bonus for tanking to the very back).";
                    p.Category = PartCategory.Economy;
                    p.Price = 5;
                    p.MoneyPerPositionHeld = 1;
                }),
                EnsurePart("Part_TeamAccountant", p =>
                {
                    p.Id = "team_accountant";
                    p.DisplayName = "Team Accountant";
                    p.Description = "Squeezes the sponsors properly. +$2 per finishing position each race, capped at mid-pack.";
                    p.Category = PartCategory.Economy;
                    p.Price = 8;
                    p.MoneyPerPositionHeld = 2;
                }),
                EnsurePart("Part_ScrapDealer", p =>
                {
                    p.Id = "scrap_dealer";
                    p.DisplayName = "Scrap Dealer";
                    p.Description = "Knows a guy. +$1 per finishing position, capped at mid-pack (sell-for-cash hook comes later).";
                    p.Category = PartCategory.Economy;
                    p.Price = 4;
                    p.MoneyPerPositionHeld = 1;
                }),
                EnsurePart("Part_UnderdogBonus", p =>
                {
                    p.Id = "underdog_bonus";
                    p.DisplayName = "Underdog Bonus";
                    p.Description = "The crowd loves a backmarker. +$3 per finishing position each race, capped at mid-pack — pays big for scrapping near the back, nothing for winning clean.";
                    p.Category = PartCategory.Economy;
                    p.Rarity = Rarity.Uncommon;
                    p.Price = 8;
                    p.MoneyPerPositionHeld = 3;
                }),

                // ---- Attack parts (on-contact saps + proximity aura, doc 03) ----
                EnsurePart("Part_RamBars", p =>
                {
                    p.Id = "ram_bars";
                    p.DisplayName = "Ram Bars";
                    p.Description = "Weld-on battering ram. Shunt a rival hard and their tyres go greasy — -28% grip for a moment.";
                    p.Category = PartCategory.Attack;
                    p.Price = 6;
                    p.ContactGripSap = 0.28f;
                }),
                EnsurePart("Part_SpikePlates", p =>
                {
                    p.Id = "spike_plates";
                    p.DisplayName = "Spike Plates";
                    p.Description = "Contact costs THEM. A solid hit bleeds a rival's engine — -30% power and -10% grip.";
                    p.Category = PartCategory.Attack;
                    p.Price = 7;
                    p.ContactPowerSap = 0.30f;
                    p.ContactGripSap = 0.10f;
                }),
                EnsurePart("Part_DisruptorField", p =>
                {
                    p.Id = "disruptor_field";
                    p.DisplayName = "Disruptor Field";
                    p.Description = "Rivals tucked in behind you can't find grip. 6 m aura, -18% grip to cars on your gearbox.";
                    p.Category = PartCategory.Attack;
                    p.Price = 8;
                    p.AuraRadiusM = 6f;
                    p.AuraGripSap = 0.18f;
                }),
                EnsurePart("Part_EmpBumper", p =>
                {
                    p.Id = "emp_bumper";
                    p.DisplayName = "EMP Bumper";
                    p.Description = "Discharges on impact. A clean hit kills a rival's ignition — -45% power — but the capacitor is delicate and the whole rig is pricey.";
                    p.Category = PartCategory.Attack;
                    p.Rarity = Rarity.Rare;
                    p.Price = 10;
                    p.ContactPowerSap = 0.45f;
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

        // For parts that mix additive anchors with multiplicative payoffs on one target, spell
        // the ops out per-entry so slot order reads clearly (SpecModApplier resolves left-to-right).
        private static List<SpecMod> ModList(params SpecMod[] mods) => new List<SpecMod>(mods);

        /// <summary>One multiplicative mod: 1.10 = x1.10, 0.94 = x0.94.</summary>
        private static SpecMod Mul(SpecModTarget target, float multiplier) =>
            new SpecMod { Target = target, Multiplier = multiplier, Op = SpecModOp.Multiply };

        /// <summary>One additive mod (Op=Add): a +fraction folded before later x-mods, e.g. 0.14 = +14%, -0.04 = -4%.</summary>
        private static SpecMod AddPct(SpecModTarget target, float amount) =>
            new SpecMod { Target = target, Multiplier = amount, Op = SpecModOp.Add };
    }
}
