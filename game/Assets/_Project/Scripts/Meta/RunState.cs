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

        /// <summary>0-based index of the current/upcoming race within the circuit.</summary>
        public int RaceIndex;
        public int RacesPerCircuit = 3;

        /// <summary>The circuit's last race is the Boss/Feature race: must finish top-N to advance.</summary>
        public int BossTopN = 3;

        public int MaxEquipSlots = 6;

        /// <summary>Everything bought this run.</summary>
        public List<PartDef> OwnedParts = new List<PartDef>();

        /// <summary>The subset currently slotted onto the car (max MaxEquipSlots).</summary>
        public List<PartDef> EquippedParts = new List<PartDef>();

        public bool IsBossRace => RaceIndex >= RacesPerCircuit - 1;
        public bool RunComplete => RaceIndex >= RacesPerCircuit;
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
