using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// Flat, [Serializable] snapshot of a run for JSON save/load. Parts are captured by their
    /// stable PartDef.Id (string) rather than object references, so the file survives asset
    /// reimports and never dangles: on load the Ids are resolved back to live PartDefs through
    /// the injected PartPool. RunDirector writes this to Application.persistentDataPath after
    /// each race resolution / garage and reads it back on Awake to resume an interrupted run.
    /// Tuning fields (RacesPerCircuit, TotalCircuits, BossTopN, MaxEquipSlots) are intentionally
    /// NOT stored — they are run-start constants, so a rebuilt RunState keeps today's defaults.
    /// TotalCircuits goes one step further: RunDirector re-stamps it from its own inspector field on
    /// every run it adopts (RunDirector.ApplySeasonShape), so a resumed run tracks the CURRENT season
    /// length rather than whichever default it was rebuilt with.
    /// </summary>
    [Serializable]
    public class RunSave
    {
        /// <summary>Save-file name under Application.persistentDataPath.</summary>
        public const string FileName = "shitboxer_run.json";

        public int money;
        public int lives;
        public int circuitIndex;
        public int raceIndex;
        public int seed;

        /// <summary>
        /// Persistent car durability carried across races (RunState.CarDurability). Defaults to 1 so an
        /// older save written before persistent damage existed (the field absent from its JSON) resumes
        /// as a pristine car rather than a wreck.
        /// </summary>
        public float carDurability = 1f;

        /// <summary>
        /// Which chassis the run drives (RunState.ChassisId — an index into RunDirector.chassisSpecs).
        /// Closed as a gap in doc 08 slice 15: RunState always documented this as persisted, but no
        /// save ever carried it, so a resumed Brute run silently reverted to the GripBox. Absent from
        /// an older save's JSON it defaults to 0 — exactly the reversion those saves already had.
        /// </summary>
        public int chassisId;

        /// <summary>Ids of everything bought this run (PartDef.Id).</summary>
        public List<string> ownedPartIds = new List<string>();

        /// <summary>Ids of the currently slotted subset, in slot order (PartDef.Id).</summary>
        public List<string> equippedPartIds = new List<string>();

        /// <summary>
        /// Ids of a bought-but-unpicked crate's contents (RunState.CrateContents). Persisted because the
        /// crate is paid for on buy and the run saves immediately, so dropping this would turn a
        /// quit-mid-crate into lost money. Absent from an older save's JSON, which deserializes to an
        /// empty list — i.e. no open crate, exactly the pre-crate behaviour.
        /// </summary>
        public List<string> crateContentIds = new List<string>();

        /// <summary>
        /// Permanent team upgrades owned this run, stored by enum NAME rather than ordinal — the same
        /// "stable id, never a positional index" rule the part lists follow, so inserting a new
        /// <see cref="TeamUpgrade"/> member can't silently reinterpret an existing save's upgrades as
        /// different ones. Unparseable names are discarded on load; absent from an older save's JSON means
        /// no upgrades, i.e. exactly the pre-upgrade shop.
        /// </summary>
        public List<string> teamUpgradeIds = new List<string>();

        /// <summary>
        /// Component levels, stored as "Name:Level" pairs rather than a positional array — the same
        /// "stable id, never an index" rule the part and upgrade lists follow. Inserting a new
        /// <see cref="CarComponent"/> member therefore cannot silently reinterpret an existing save's
        /// levels as belonging to different components. Absent from an older save's JSON deserializes
        /// to an empty list, which restores as all-baseline: exactly the pre-component car.
        /// </summary>
        public List<string> componentLevels = new List<string>();

        /// <summary>
        /// Run-applied editions (doc 08 slice 13), stored as "partId:Edition" pairs — by-name, same
        /// discipline as everything above. Only entries above None are written. Absent from an older
        /// save deserializes to an empty list: no materials applied, the pre-Spectral car.
        /// </summary>
        public List<string> partEditions = new List<string>();

        /// <summary>
        /// An open Spectral pack's offers, verbatim in <see cref="SpectralOffer"/>'s own encoding
        /// ("Edition:partId"). Persisted for the same reason the crate is: paid at buy time, saved
        /// immediately — a quit-then-resume must not keep the spend and lose the draw.
        /// </summary>
        public List<string> packSpectralOffers = new List<string>();

        /// <summary>
        /// An open COMPONENTS pack's picks, by component NAME. This closes a save gap the crate and
        /// the Spectral pack never had: the pack is paid for at buy time, so dropping it on a quit
        /// silently ate the money. Absent from an older save = no open pack, as ever.
        /// </summary>
        public List<string> packComponents = new List<string>();

        /// <summary>Default absolute path of the save file.</summary>
        public static string DefaultPath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>
        /// Captures a live run as a by-Id DTO. Parts with no Id are skipped (nothing stable to
        /// key on) — every shipped PartDef is expected to carry one.
        /// </summary>
        public static RunSave From(RunState run)
        {
            var dto = new RunSave
            {
                money = run.Money,
                lives = run.Lives,
                circuitIndex = run.CircuitIndex,
                raceIndex = run.RaceIndex,
                seed = run.Seed,
                carDurability = run.CarDurability,
                chassisId = run.ChassisId,
            };
            foreach (PartDef part in run.OwnedParts)
                if (part && !string.IsNullOrEmpty(part.Id)) dto.ownedPartIds.Add(part.Id);
            foreach (PartDef part in run.EquippedParts)
                if (part && !string.IsNullOrEmpty(part.Id)) dto.equippedPartIds.Add(part.Id);
            foreach (PartDef part in run.CrateContents)
                if (part && !string.IsNullOrEmpty(part.Id)) dto.crateContentIds.Add(part.Id);
            foreach (TeamUpgrade upgrade in run.OwnedUpgrades)
                dto.teamUpgradeIds.Add(upgrade.ToString());
            // Only components ABOVE the baseline are written: level 1 is the default a fresh set
            // restores to, so recording it would be noise in every save file.
            foreach (CarComponentInfo info in CarComponentCatalog.All)
            {
                int level = run.LevelOf(info.Component);
                if (level > CarComponentCatalog.MinLevel)
                    dto.componentLevels.Add($"{info.Component}:{level}");
            }
            // Editions above None only — None is what EditionOf already reads for an absent entry.
            foreach (KeyValuePair<string, PartEdition> entry in run.PartEditions)
                if (!string.IsNullOrEmpty(entry.Key) && entry.Value != PartEdition.None)
                    dto.partEditions.Add($"{entry.Key}:{entry.Value}");
            dto.packSpectralOffers.AddRange(run.PackSpectrals);
            foreach (int ordinal in run.PackComponents)
                if (Enum.IsDefined(typeof(CarComponent), (CarComponent)ordinal))
                    dto.packComponents.Add(((CarComponent)ordinal).ToString());
            return dto;
        }

        /// <summary>
        /// Rebuilds a RunState, resolving part Ids back to live PartDefs via <paramref name="pool"/>.
        /// Unresolvable Ids (a part dropped from the catalogue) are quietly discarded rather than
        /// throwing, and an equipped Id that isn't also owned is discarded too (Equip's own rule).
        /// </summary>
        public RunState ToRunState(PartPool pool)
        {
            Dictionary<string, PartDef> index = BuildIdIndex(pool);
            var run = new RunState
            {
                Money = money,
                Lives = lives,
                CircuitIndex = circuitIndex,
                RaceIndex = raceIndex,
                Seed = seed,
                CarDurability = carDurability,
                ChassisId = chassisId,
            };
            run.OwnedParts.Clear();
            run.EquippedParts.Clear();
            run.CrateContents.Clear();
            run.OwnedUpgrades.Clear();

            // Enum.TryParse by name: a save written before an upgrade was renamed/removed drops that entry
            // rather than throwing, matching how an unresolvable part Id is quietly discarded.
            foreach (string id in teamUpgradeIds)
                if (!string.IsNullOrEmpty(id)
                    && Enum.TryParse(id, out TeamUpgrade upgrade)
                    && Enum.IsDefined(typeof(TeamUpgrade), upgrade)
                    && !run.OwnedUpgrades.Contains(upgrade))
                    run.OwnedUpgrades.Add(upgrade);

            // Component levels, same by-name discipline: an unparseable component or a junk level is
            // dropped rather than throwing, and anything absent stays at the baseline the fresh
            // RunState already holds.
            foreach (string entry in componentLevels)
            {
                if (string.IsNullOrEmpty(entry)) continue;
                int split = entry.LastIndexOf(':');
                if (split <= 0 || split >= entry.Length - 1) continue;
                if (!Enum.TryParse(entry.Substring(0, split), out CarComponent component)) continue;
                if (!Enum.IsDefined(typeof(CarComponent), component)) continue;
                if (!int.TryParse(entry.Substring(split + 1), out int level)) continue;
                run.ComponentLevels[(int)component] = CarComponentCatalog.ClampLevel(level);
            }

            foreach (string id in ownedPartIds)
                if (id != null && index.TryGetValue(id, out PartDef part) && !run.OwnedParts.Contains(part))
                    run.OwnedParts.Add(part);

            // An open crate's contents are NOT owned yet — they're the pending pick, so they resolve
            // independently of OwnedParts. Unresolvable ids drop out like everywhere else; if that empties
            // the list the crate simply reads as closed rather than stranding the player in a pick screen
            // with nothing to pick.
            foreach (string id in crateContentIds)
                if (id != null && index.TryGetValue(id, out PartDef part) && !run.CrateContents.Contains(part))
                    run.CrateContents.Add(part);

            foreach (string id in equippedPartIds)
                if (id != null && index.TryGetValue(id, out PartDef part)
                    && run.OwnedParts.Contains(part) && !run.EquippedParts.Contains(part))
                    run.EquippedParts.Add(part);

            // Run-applied editions: "partId:Edition", LAST colon split because part ids are free-form.
            // An edition on a part no longer owned is dropped (RemovePart would have purged it live).
            foreach (string entry in partEditions)
            {
                if (string.IsNullOrEmpty(entry)) continue;
                int split = entry.LastIndexOf(':');
                if (split <= 0 || split >= entry.Length - 1) continue;
                string partId = entry.Substring(0, split);
                if (!Enum.TryParse(entry.Substring(split + 1), out PartEdition edition)) continue;
                if (!Enum.IsDefined(typeof(PartEdition), edition) || edition == PartEdition.None) continue;
                if (!index.TryGetValue(partId, out PartDef part) || !run.OwnedParts.Contains(part)) continue;
                run.PartEditions[partId] = edition;
            }

            // An open Spectral pack's offers: keep only lines that decode AND still aim at an owned
            // part — if that empties the list the pack reads as closed rather than wedging the shelf.
            foreach (string encoded in packSpectralOffers)
                if (SpectralOffer.TryDecode(encoded, out _, out string targetId)
                    && index.TryGetValue(targetId, out PartDef target) && run.OwnedParts.Contains(target)
                    && !run.PackSpectrals.Contains(encoded))
                    run.PackSpectrals.Add(encoded);

            // An open components pack's picks, by name — junk drops out like everywhere else.
            foreach (string name in packComponents)
                if (!string.IsNullOrEmpty(name)
                    && Enum.TryParse(name, out CarComponent component)
                    && Enum.IsDefined(typeof(CarComponent), component)
                    && !run.PackComponents.Contains((int)component))
                    run.PackComponents.Add((int)component);

            return run;
        }

        private static Dictionary<string, PartDef> BuildIdIndex(PartPool pool)
        {
            var index = new Dictionary<string, PartDef>();
            if (pool == null || pool.Parts == null) return index;
            foreach (PartDef part in pool.Parts)
                if (part && !string.IsNullOrEmpty(part.Id)) index[part.Id] = part;
            return index;
        }

        // ---- File IO --------------------------------------------------------

        public static bool Exists() => Exists(DefaultPath);
        public static bool Exists(string path) => File.Exists(path);

        /// <summary>Serialises a run to the default save path.</summary>
        public static void Save(RunState run) => Save(run, DefaultPath);

        /// <summary>Serialises a run to <paramref name="path"/> as JSON.</summary>
        public static void Save(RunState run, string path)
        {
            string json = JsonUtility.ToJson(From(run));
            File.WriteAllText(path, json);
        }

        public static bool TryLoad(PartPool pool, out RunState run) => TryLoad(pool, DefaultPath, out run);

        /// <summary>
        /// Reads and resolves a run from <paramref name="path"/>. Returns false (with a null run)
        /// when the file is missing or unparseable, so callers cleanly fall back to a fresh run.
        /// </summary>
        public static bool TryLoad(PartPool pool, string path, out RunState run)
        {
            run = null;
            if (!File.Exists(path)) return false;
            try
            {
                string json = File.ReadAllText(path);
                RunSave dto = JsonUtility.FromJson<RunSave>(json);
                if (dto == null) return false;
                run = dto.ToRunState(pool);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void Delete() => Delete(DefaultPath);

        public static void Delete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
