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

        /// <summary>Ids of everything bought this run (PartDef.Id).</summary>
        public List<string> ownedPartIds = new List<string>();

        /// <summary>Ids of the currently slotted subset, in slot order (PartDef.Id).</summary>
        public List<string> equippedPartIds = new List<string>();

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
            };
            foreach (PartDef part in run.OwnedParts)
                if (part && !string.IsNullOrEmpty(part.Id)) dto.ownedPartIds.Add(part.Id);
            foreach (PartDef part in run.EquippedParts)
                if (part && !string.IsNullOrEmpty(part.Id)) dto.equippedPartIds.Add(part.Id);
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
            };
            run.OwnedParts.Clear();
            run.EquippedParts.Clear();

            foreach (string id in ownedPartIds)
                if (id != null && index.TryGetValue(id, out PartDef part) && !run.OwnedParts.Contains(part))
                    run.OwnedParts.Add(part);

            foreach (string id in equippedPartIds)
                if (id != null && index.TryGetValue(id, out PartDef part)
                    && run.OwnedParts.Contains(part) && !run.EquippedParts.Contains(part))
                    run.EquippedParts.Add(part);

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
