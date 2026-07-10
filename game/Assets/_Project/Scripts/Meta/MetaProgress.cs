using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// Persistent player PROFILE that outlives any single run — the cross-run "one more run" spine
    /// (doc 03 chassis-as-decks / license-classes-as-stakes; doc 05 Phase 4 meta hook). Unlike
    /// <see cref="RunSave"/> (one per-run snapshot, deleted the moment a run dies or wins) this lives
    /// forever in its OWN file under Application.persistentDataPath, accumulating lifetime stats and
    /// the unlock flags that gate higher license stakes and starter perks.
    /// Flat + [Serializable] so JsonUtility round-trips it directly: there are no object references to
    /// resolve (unlike RunSave's by-Id part DTO), so the class is its own save DTO. Every file op
    /// swallows failures — a missing/corrupt/locked profile loads as a fresh default and a failed
    /// write is logged, never thrown — so profile IO can never break the game.
    /// </summary>
    [Serializable]
    public class MetaProgress
    {
        /// <summary>Profile file name under Application.persistentDataPath (distinct from RunSave's).</summary>
        public const string FileName = "shitboxer_meta.json";

        /// <summary>Total runs ever finished, won OR lost.</summary>
        public int totalRuns;

        /// <summary>Highest 0-based circuit index ever reached across all runs.</summary>
        public int bestCircuitReached;

        /// <summary>How many full seasons have ever been cleared (any stake).</summary>
        public int seasonsCleared;

        /// <summary>Sum of end-of-run wallets across every finished run — a lifetime money stat.</summary>
        public int lifetimeMoney;

        /// <summary>
        /// Unlock flags earned across runs, e.g. "stake2"/"stake3" (higher license stakes, see
        /// <see cref="StakeFlag"/>) and "powerbox_start" (a starter perk). A plain string set kept as a
        /// List so JsonUtility can serialise it.
        /// </summary>
        public List<string> unlocks = new List<string>();

        /// <summary>Default absolute path of the profile file.</summary>
        public static string DefaultPath => Path.Combine(Application.persistentDataPath, FileName);

        // ---- Unlock flags ----------------------------------------------------

        /// <summary>
        /// Unlock-flag name gating a given 0-based license stake. Stake 0 is the base license and is
        /// always playable (no flag needed); stake 1 -> "stake2", stake 2 -> "stake3", ... so the flag
        /// reads as the human "License N" number.
        /// </summary>
        public static string StakeFlag(int stakeLevel) => "stake" + (stakeLevel + 1);

        public bool IsUnlocked(string flag) => flag != null && unlocks.Contains(flag);

        /// <summary>Adds an unlock flag if absent. Returns true only if it was newly unlocked.</summary>
        public bool Unlock(string flag)
        {
            if (string.IsNullOrEmpty(flag) || unlocks.Contains(flag)) return false;
            unlocks.Add(flag);
            return true;
        }

        /// <summary>Stake 0 (base license) is always playable; higher stakes need their unlock flag.</summary>
        public bool IsStakeUnlocked(int stakeLevel) =>
            stakeLevel <= 0 || IsUnlocked(StakeFlag(stakeLevel));

        /// <summary>Unlocks a stake level's flag (no-op for stake 0). Returns true only if newly unlocked.</summary>
        public bool UnlockStake(int stakeLevel) =>
            stakeLevel > 0 && Unlock(StakeFlag(stakeLevel));

        /// <summary>
        /// Highest 0-based stake level currently unlocked (0 when only the base license is available).
        /// Scans up the contiguous ladder — a stake is only reachable once every stake below it is too.
        /// </summary>
        public int HighestUnlockedStake
        {
            get
            {
                int highest = 0;
                for (int s = 1; IsStakeUnlocked(s); s++) highest = s;
                return highest;
            }
        }

        // ---- Run-end bookkeeping --------------------------------------------

        /// <summary>
        /// Folds one finished run into the lifetime profile: counts the run, tracks the best circuit ever
        /// reached and accumulates lifetime money. Call on EVERY run end (won or lost). Does not persist —
        /// the caller saves once, after any season-clear bump too.
        /// </summary>
        public void RegisterRunEnd(int circuitReached, int endingMoney)
        {
            totalRuns++;
            if (circuitReached > bestCircuitReached) bestCircuitReached = circuitReached;
            if (endingMoney > 0) lifetimeMoney += endingMoney; // never subtract from the lifetime stat
        }

        /// <summary>
        /// Records a full season clear at <paramref name="stakeLevel"/>: counts the season and unlocks the
        /// NEXT stake up — the roguelike escalation hook. Returns the newly-unlocked stake flag, or null if
        /// that next stake was already unlocked.
        /// </summary>
        public string RegisterSeasonCleared(int stakeLevel)
        {
            seasonsCleared++;
            return UnlockStake(stakeLevel + 1) ? StakeFlag(stakeLevel + 1) : null;
        }

        // ---- File IO --------------------------------------------------------

        public static MetaProgress Load() => Load(DefaultPath);

        /// <summary>
        /// Reads the profile from <paramref name="path"/>. A missing, unparseable, or locked file yields a
        /// fresh default profile rather than throwing, so IO trouble never breaks the game.
        /// </summary>
        public static MetaProgress Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    MetaProgress loaded = JsonUtility.FromJson<MetaProgress>(json);
                    if (loaded != null)
                    {
                        loaded.unlocks ??= new List<string>();
                        return loaded;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MetaProgress] Load failed, using a fresh profile: {e.Message}");
            }
            return new MetaProgress();
        }

        public static void Save(MetaProgress progress) => Save(progress, DefaultPath);

        /// <summary>Serialises the profile to <paramref name="path"/>; IO failures are logged, never fatal.</summary>
        public static void Save(MetaProgress progress, string path)
        {
            if (progress == null) return;
            try
            {
                File.WriteAllText(path, JsonUtility.ToJson(progress));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MetaProgress] Save failed: {e.Message}");
            }
        }

        public static bool Exists() => Exists(DefaultPath);
        public static bool Exists(string path) => File.Exists(path);

        public static void Delete() => Delete(DefaultPath);

        public static void Delete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MetaProgress] Delete failed: {e.Message}");
            }
        }
    }
}
