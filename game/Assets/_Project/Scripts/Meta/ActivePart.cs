using System;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// How an active item EARNS its charge (doc 08 decision 14). Deliberately not one model — an
    /// active is defined partly by how it is earned, so equipping one changes how you want to drive.
    /// None (0) is the default so every existing PartDef asset deserializes as a non-active part and
    /// nothing about the shipped loadouts moves.
    /// </summary>
    public enum ActiveCharge
    {
        None,          // not an active item
        Drafting,      // fills per second while sitting in a rival's tow (the DraftBoost condition)
        CleanRunning,  // fills per second while free of contact
        ContactDealt,  // a chunk per car-to-car hit that was mostly YOUR doing
        DamageTaken,   // a chunk per durability lost — the inverted economy applied to charge
        SectorLine,    // a chunk per sector completed
        OncePerRace,   // starts full, never refills — one big moment per race
        Cooldown,      // fills per second unconditionally — a timer, not a behaviour
        PaidUse,       // always ready; every deploy costs money (what makes a per-use tax bite)
    }

    /// <summary>
    /// The authored half of one active item, carried on <see cref="PartDef"/>. A class (not a struct)
    /// so Unity runs the field initializers when a designer adds one in the inspector — a fresh entry
    /// deploys a sane boost instead of a zeroed one. Charge == None (every pre-existing asset) makes
    /// the whole block inert. Consumed by <see cref="ActivePartState"/>, which clamps every value
    /// again at runtime, so a hand-edited YAML can't smuggle in an unbounded boost.
    /// </summary>
    [Serializable]
    public class ActiveSpec
    {
        [Tooltip("How this item charges. None (default) = not an active item; the rest of the block is ignored.")]
        public ActiveCharge Charge = ActiveCharge.None;

        [Tooltip("Per-SECOND reservoir fill while the condition holds (Drafting / CleanRunning / Cooldown). ~0.35 => ~3 s to full. Ignored by chunk conditions.")]
        public float FillPerSecond = 0.35f;

        [Tooltip("Reservoir chunk per EVENT (ContactDealt: per attributed hit; SectorLine: per sector; DamageTaken: per 10% durability lost). 0.34 => three events to full. Ignored by per-second conditions.")]
        public float ChargePerEvent = 0.34f;

        [Tooltip("Peak drive-torque multiplier while deployed. Clamped at runtime into [1, 1.5] (DraftBoostModel.AbsoluteMaxBoostMult).")]
        public float BoostMult = 1.15f;

        [Tooltip("Reservoir drained per second while deployed. 0.5 => ~2 s of boost from a full reservoir.")]
        public float DrainPerSecond = 0.5f;

        [Tooltip("Minimum reservoir needed to deploy. 1 = full-charge-only (chunk builds); 0.25 = deploy early at the cost of a shorter burst.")]
        [Range(0f, 1f)] public float MinCharge01 = 1f;

        [Tooltip("Money charged per DEPLOY. The PaidUse condition lives on this; any condition may carry it (it is also where a boss's per-use tax lands). Deploy is refused when the run can't pay.")]
        [Min(0)] public int UseCost = 0;
    }

    /// <summary>
    /// Everything the HUD needs to draw the active-item meter, flattened so the UI reads one struct
    /// off <see cref="IRunHost"/> instead of poking at the runner. <see cref="HasActive"/> false (the
    /// default) hides the element entirely — a loadout without an active shows nothing new.
    /// </summary>
    public readonly struct ActiveReadout
    {
        public readonly bool HasActive;
        public readonly string Name;
        public readonly float Charge01;
        public readonly bool Deployed;
        /// <summary>Charged enough AND affordable — the moment the key would actually work.</summary>
        public readonly bool Ready;
        public readonly int UseCost;
        /// <summary>The bound key, for the "Q" hint on the meter.</summary>
        public readonly string KeyLabel;

        public ActiveReadout(bool hasActive, string name, float charge01, bool deployed, bool ready,
            int useCost, string keyLabel)
        {
            HasActive = hasActive;
            Name = name;
            Charge01 = charge01;
            Deployed = deployed;
            Ready = ready;
            UseCost = useCost;
            KeyLabel = keyLabel;
        }
    }

    /// <summary>
    /// The single ACTIVATE key bind (doc 08 decision 14: one bind, default Q, rebindable in
    /// settings). Stored as a string in <see cref="GameSettings"/> so the JSON stays readable and an
    /// unknown / corrupted value falls back to Q instead of throwing. Pure and static — the parse is
    /// the unit-test seam; reading the actual keyboard stays in the host layer.
    /// </summary>
    public static class ActivateKeyBinding
    {
        public const string DefaultKey = "Q";

        /// <summary>The curated rebind choices the settings screen cycles through.</summary>
        public static readonly string[] Choices = { "Q", "E", "F", "R", "X", "C", "LeftShift", "Space" };

        /// <summary>
        /// Parse a stored binding into an InputSystem key, case-insensitively, falling back to Q for
        /// anything unknown so a stale settings file can never leave the active item undeployable.
        /// </summary>
        public static UnityEngine.InputSystem.Key Parse(string stored)
        {
            if (!string.IsNullOrWhiteSpace(stored)
                && Enum.TryParse(stored.Trim(), ignoreCase: true, out UnityEngine.InputSystem.Key key)
                && key != UnityEngine.InputSystem.Key.None)
                return key;
            return UnityEngine.InputSystem.Key.Q;
        }

        /// <summary>The next entry in <see cref="Choices"/> after <paramref name="stored"/> (wrapping), for the settings cycle row.</summary>
        public static string Next(string stored, int direction = 1)
        {
            int index = Array.FindIndex(Choices, c => string.Equals(c, stored, StringComparison.OrdinalIgnoreCase));
            if (index < 0) index = 0;
            int count = Choices.Length;
            return Choices[((index + direction) % count + count) % count];
        }
    }
}
