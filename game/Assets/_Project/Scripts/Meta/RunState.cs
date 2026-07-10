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

        /// <summary>
        /// License-stake level (0 = base license, i.e. exactly today's balance). Higher stakes are
        /// UNLOCKED across runs by clearing the season one stake below them (recorded in MetaProgress),
        /// and they scale BOTH difficulty and reward: StakeLevel folds into <see cref="DifficultyMult"/>
        /// via <see cref="StakeMult"/> (so the season ramp AND RunDirector's bot/cutoff scaling pick it
        /// up automatically), and RunDirector applies the same <see cref="StakeMult"/> as a modest payout
        /// bump on a clean finish. Defaults to 0 so an un-staked run plays and pays exactly as shipped.
        /// </summary>
        public int StakeLevel;

        /// <summary>Per-stake difficulty/reward gain. Stake 0 is a no-op (factor 1.0), gentle above.</summary>
        public const float StakeGainPerLevel = 0.15f;

        /// <summary>
        /// Difficulty/reward scalar contributed purely by the license stake: 1.0 at stake 0 (shipped
        /// balance), climbing gently above. Multiplies the per-circuit ramp in <see cref="DifficultyMult"/>
        /// and doubles as RunDirector's clean-finish reward multiplier.
        /// </summary>
        public float StakeMult => 1f + StakeGainPerLevel * Math.Max(0, StakeLevel);

        /// <summary>
        /// Persistent 0..1 structural integrity of the run's car (1 = pristine). Unlike the sim's
        /// per-race Durability this carries ACROSS races within a run: RunDirector re-applies it onto
        /// each freshly-rebuilt sim, captures the sim's ending value back after every race, and resets
        /// it to 1 when the player pays to repair in the garage. A fresh run starts pristine.
        /// </summary>
        public float CarDurability = 1f;

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
        /// this without hard-coding a per-circuit table. Wave-1 default (stake 0): 1.0, 1.35, 1.70, ...
        /// The license stake multiplies the whole ramp (<see cref="StakeMult"/>): stake 0 leaves the
        /// sequence untouched, higher stakes lift every circuit uniformly so the existing ramp — and
        /// RunDirector's bot/cutoff scaling that reads this — picks the stake up with no extra wiring.
        /// </summary>
        public float DifficultyMult =>
            (1f + 0.3f * CircuitIndex + 0.05f * CircuitIndex * CircuitIndex) * StakeMult;

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

        /// <summary>
        /// Removes a part from the run entirely — both the equipped slot (if slotted) and the owned
        /// pool. Parts are unique instances in the pool, so dropping the PartDef reference is a clean
        /// delete. Returns true if the part was owned (and thus removed). Used when a Fragile part
        /// breaks under heavy race damage (RunDirector); safe on null or an unowned part.
        /// </summary>
        public bool RemovePart(PartDef part)
        {
            if (part == null) return false;
            EquippedParts.Remove(part);
            return OwnedParts.Remove(part);
        }

        /// <summary>
        /// Total end-of-run refund from owned Cashout parts: the Price of every owned part tagged
        /// PartCondition.Cashout, whether equipped or not (refund-if-KEPT — you get the money back
        /// for holding onto them to the end). RunDirector folds this into final Money when the run
        /// terminates. 0 when no Cashout parts are held.
        /// </summary>
        public int CashoutRefundTotal()
        {
            int total = 0;
            foreach (PartDef part in OwnedParts)
                if (part != null && part.Condition == PartCondition.Cashout)
                    total += part.Price;
            return total;
        }
    }
}
