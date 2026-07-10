using System;
using System.Collections.Generic;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>The three part families from doc 03: tune yourself, tune the money, hurt rivals.</summary>
    public enum PartCategory
    {
        Stat,
        Economy,
        Attack,
    }

    /// <summary>Which slice of the VehicleSpec a stat part multiplies (see SpecModApplier).</summary>
    public enum SpecModTarget
    {
        GripFront,  // FrontTyre.PeakMu & SlideMu
        GripRear,   // RearTyre.PeakMu & SlideMu
        Power,      // Engine.PeakTorqueNm
        Weight,     // MassKg (multipliers below 1 are upgrades)
        Downforce,  // DownforceCoeff
    }

    /// <summary>
    /// How a SpecMod folds into its target's running factor (see SpecModApplier). Multiply (0)
    /// is the default so existing single-op assets deserialize to today's behaviour, and a pile
    /// of pure-Multiply mods still commutes. Add is the non-commuting op that makes slot order
    /// matter: an Add mod slotted BEFORE a Multiply mod on the same target beats the reverse
    /// (Balatro's +Mult-before-xMult, doc 03).
    /// </summary>
    public enum SpecModOp
    {
        Multiply,  // running factor *= Multiplier  (1.1 = +10%)
        Add,       // running factor += Multiplier as a +fraction  (0.10 = +10%)
    }

    /// <summary>One tweak a stat part applies to the player's spec (multiplicative by default).</summary>
    [Serializable]
    public struct SpecMod
    {
        public SpecModTarget Target;
        [Tooltip("Op=Multiply: factor on the target, 1.1 = +10%, 0.9 = -10%. Op=Add: a +fraction added to the running factor, 0.10 = +10%, -0.04 = -4%.")]
        public float Multiplier;
        [Tooltip("Multiply (default) scales the target's running factor; Add adds to it — so slot order matters when both ops hit one target.")]
        public SpecModOp Op;
    }

    /// <summary>
    /// Shop draw-weight tier (doc 03's Balatro DNA). Common (0) is the default so every existing
    /// PartDef asset stays Common; ShopLogic.Roll biases the shelf toward Common and makes Rare
    /// scarce.
    /// </summary>
    public enum Rarity
    {
        Common,
        Uncommon,
        Rare,
    }

    /// <summary>
    /// doc 03's per-part modifier. Passive (0) is the default so every existing PartDef asset stays
    /// a plain part with no special behaviour. Fragile parts carry a stronger effect but break (are
    /// destroyed, removed from the run) if the car finishes a race badly battered (RunDirector).
    /// Cashout parts refund their Price into final Money if still owned when the run ends
    /// (RunDirector / RunState.CashoutRefundTotal) — a "buy it, keep it, get it back" economy hook.
    /// </summary>
    public enum PartCondition
    {
        Passive,
        Fragile,
        Cashout,
    }

    /// <summary>
    /// One shop part — the "jokers as equipment" unit (doc 03). Stat parts carry SpecMods that
    /// SpecModApplier bakes into the player's VehicleSpec; economy parts hook the payout step;
    /// attack parts carry on-contact and proximity-aura saps that RunDirector flattens into an
    /// AttackProfile for the car's VehicleCombat to resolve against rivals.
    /// </summary>
    [CreateAssetMenu(menuName = "Shitboxer/Part", fileName = "Part")]
    public class PartDef : ScriptableObject
    {
        [Tooltip("Stable string id for save data / dedup — never rename once shipped.")]
        public string Id;
        public string DisplayName;
        [TextArea]
        public string Description;
        public PartCategory Category;
        [Tooltip("Shop draw-weight tier — Common shows up often, Rare rarely (ShopLogic.Roll).")]
        public Rarity Rarity = Rarity.Common;
        [Min(0)] public int Price = 5;
        [Tooltip("doc 03 part modifier: Passive (default) is inert; Fragile breaks and is destroyed if the car finishes a race badly battered; Cashout refunds its Price into final money if still owned when the run ends.")]
        public PartCondition Condition = PartCondition.Passive;

        [Header("Stat parts")]
        public List<SpecMod> SpecMods = new List<SpecMod>();

        [Header("Economy parts (payout hook only, this phase)")]
        [Tooltip("$ bonus per finishing-position number at payout (finishing P6 pays 6x this) — leans further into the inverted economy.")]
        public int MoneyPerPositionHeld;

        [Header("Attack parts — on-contact saps + proximity aura (doc 03)")]
        [Tooltip("Grip fraction stripped from a rival you hit hard enough on contact. 0.3 = -30%.")]
        [Range(0f, 0.9f)] public float ContactGripSap;
        [Tooltip("Engine-torque fraction stripped from a rival you hit on contact. 0.3 = -30%.")]
        [Range(0f, 0.9f)] public float ContactPowerSap;
        [Tooltip("Radius of a proximity aura, metres. 0 = no aura.")]
        public float AuraRadiusM;
        [Tooltip("Grip fraction stripped each step from rivals inside the aura (and behind you). 0.2 = -20%.")]
        [Range(0f, 0.9f)] public float AuraGripSap;
    }
}
