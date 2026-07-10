using System;
using System.Collections.Generic;

namespace Shitboxer.Meta
{
    /// <summary>
    /// All persistent state of one roguelike run. Plain C# and [Serializable] — no
    /// UnityEngine.Object references except the PartDef lists — so a headless server could
    /// own the authoritative copy later. RunDirector holds the single live instance.
    /// </summary>
    [Serializable]
    public class RunState
    {
        public int Money;
        public int Lives = 3;

        /// <summary>
        /// Deterministic run seed, set once when a run starts (RunDirector rolls it for a fresh
        /// run and persists it in the save). RunDirector derives a per-garage-visit shop seed
        /// from Seed + CircuitIndex + RaceIndex, so a resumed or shared run reproduces the exact
        /// same shop stock and reroll chain. 0 on a state that was never explicitly seeded.
        /// </summary>
        public int Seed;

        /// <summary>0-based index of the current/upcoming race within the circuit.</summary>
        public int RaceIndex;
        public int RacesPerCircuit = 3;

        /// <summary>0-based index of the current circuit within the season.</summary>
        public int CircuitIndex;

        /// <summary>How many circuits make up a full season. "Start small" default per the plan.</summary>
        public int TotalCircuits = 3;

        /// <summary>The circuit's last race is the Boss/Feature race: must finish top-N to advance.</summary>
        public int BossTopN = 3;

        public int MaxEquipSlots = 6;

        /// <summary>Everything bought this run.</summary>
        public List<PartDef> OwnedParts = new List<PartDef>();

        /// <summary>The subset currently slotted onto the car (max MaxEquipSlots).</summary>
        public List<PartDef> EquippedParts = new List<PartDef>();

        public bool IsBossRace => RaceIndex >= RacesPerCircuit - 1;

        /// <summary>True once the run reaches the season's last circuit.</summary>
        public bool IsFinalCircuit => CircuitIndex >= TotalCircuits - 1;

        /// <summary>
        /// Per-circuit difficulty scalar and tuning hook: 1.0 on the first circuit, ramping
        /// gently at first then steeper as the season wears on (convex in CircuitIndex).
        /// RunDirector — or a headless server — can multiply payouts / survival expectations by
        /// this without hard-coding a per-circuit table. Wave-1 default: 1.0, 1.35, 1.70, ...
        /// </summary>
        public float DifficultyMult => 1f + 0.3f * CircuitIndex + 0.05f * CircuitIndex * CircuitIndex;

        /// <summary>The run is only won after clearing the FINAL race of the FINAL circuit.</summary>
        public bool RunComplete => IsFinalCircuit && RaceIndex >= RacesPerCircuit;
        public bool HasFreeSlot => EquippedParts.Count < MaxEquipSlots;

        public bool Owns(PartDef part) => OwnedParts.Contains(part);
        public bool IsEquipped(PartDef part) => EquippedParts.Contains(part);

        /// <summary>Slots an owned part if a slot is free. False if unowned, duplicate, or full.</summary>
        public bool Equip(PartDef part)
        {
            if (part == null || !Owns(part) || IsEquipped(part) || !HasFreeSlot) return false;
            EquippedParts.Add(part);
            return true;
        }

        public bool Unequip(PartDef part) => EquippedParts.Remove(part);
    }
}
