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

    /// <summary>One multiplicative tweak a stat part applies to the player's spec.</summary>
    [Serializable]
    public struct SpecMod
    {
        public SpecModTarget Target;
        [Tooltip("Multiplier on the target value: 1.1 = +10%, 0.9 = -10%.")]
        public float Multiplier;
    }

    /// <summary>
    /// One shop part — the "jokers as equipment" unit (doc 03). Stat parts carry SpecMods that
    /// SpecModApplier bakes into the player's VehicleSpec; economy parts hook the payout step;
    /// attack parts are placeholder data only this phase (the shop sells them, nothing resolves
    /// them yet).
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
        [Min(0)] public int Price = 5;

        [Header("Stat parts")]
        public List<SpecMod> SpecMods = new List<SpecMod>();

        [Header("Economy parts (payout hook only, this phase)")]
        [Tooltip("$ bonus per finishing-position number at payout (finishing P6 pays 6x this) — leans further into the inverted economy.")]
        public int MoneyPerPositionHeld;

        [Header("Attack parts (placeholder — attack resolution is a later phase)")]
        [Tooltip("Multiplier on damage/stat-sap dealt to rivals on contact. Unused for now.")]
        public float ContactDamageMult = 1f;
        [Tooltip("Radius of a proximity aura effect, metres. Unused for now.")]
        public float AuraRadiusM;
    }
}
