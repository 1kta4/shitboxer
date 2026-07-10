using System;
using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// Special, non-default rules a single race can layer on top of the standard format.
    /// <c>[Flags]</c> so a boss or event race can carry several at once; <see cref="None"/> is the
    /// shipped behaviour (no modifier active), so a standard race reads exactly as it always has.
    /// The bits are inert data here — the run director / economy / combat layers decide what each
    /// one means; this enum only records which are switched on for a given race.
    /// </summary>
    [Flags]
    public enum RaceModifier
    {
        /// <summary>No special rule — the standard race format.</summary>
        None = 0,
        /// <summary>Damage carried out of this race is not auto-repaired in the interlude.</summary>
        NoRepairAfter = 1 << 0,
        /// <summary>This race pays out double credits.</summary>
        DoublePayout = 1 << 1,
        /// <summary>The grid is seeded in reverse championship order.</summary>
        ReverseGrid = 1 << 2,
        /// <summary>Contact / attack damage is amplified for everyone on track.</summary>
        DamageAmplified = 1 << 3,
    }

    /// <summary>
    /// Pure data description of how one race runs: how many laps constitute a finish, the survival
    /// cutoff window, whether it is a boss race, and any special <see cref="RaceModifier"/>s.
    /// <see cref="RaceManager"/> consults a ruleset instead of hard-coded constants — the single
    /// mechanism behind boss races and event races. This is a plain DTO with no scene access and no
    /// engine loop, so a headless server or the run director can build one and pass it in freely.
    ///
    /// <see cref="Standard"/> reproduces RaceManager's shipped serialized defaults exactly, so a race
    /// left on the standard ruleset behaves byte-for-byte as before the ruleset existed. Deciding
    /// *which* race is a boss/event is the run director's job (a later wave); this only provides the
    /// mechanism and a couple of ready templates.
    /// </summary>
    [Serializable]
    public struct RaceRuleset
    {
        // Shipped RaceManager defaults, kept in one place so Standard and the manager agree exactly.
        private const int StandardLaps = 3;
        private const float StandardCutoffFraction = 0.15f;

        [Min(1)]
        [Tooltip("Laps that constitute a finish (mirrors RaceManager.totalLaps).")]
        public int Laps;

        [Range(0.01f, 1f)]
        [Tooltip("Survival gate: after the winner finishes, others must finish within winnerTime * (1 + this) or be eliminated (mirrors RaceManager.cutoffFraction).")]
        public float CutoffFraction;

        [Tooltip("Marks this as a boss race (headline opponent / championship gate). Data only — nothing here decides which race is a boss.")]
        public bool IsBoss;

        [Tooltip("Special rules layered on the standard format; None = shipped behaviour.")]
        public RaceModifier Modifiers;

        /// <summary>
        /// The shipped race format: <see cref="StandardLaps"/> laps, a <see cref="StandardCutoffFraction"/>
        /// survival window, not a boss, no modifiers. Values match RaceManager's serialized defaults
        /// exactly — the neutral ruleset that preserves the original driving feel and economy balance.
        /// </summary>
        public static RaceRuleset Standard => new RaceRuleset
        {
            Laps = StandardLaps,
            CutoffFraction = StandardCutoffFraction,
            IsBoss = false,
            Modifiers = RaceModifier.None,
        };

        /// <summary>
        /// Example headline boss race: a longer, damage-amplified duel with no post-race repair, held to
        /// a tighter survival window. Data only — a ready template for the run director; nothing here
        /// decides which race is a boss.
        /// </summary>
        public static RaceRuleset Boss => new RaceRuleset
        {
            Laps = 5,
            CutoffFraction = 0.10f,
            IsBoss = true,
            Modifiers = RaceModifier.DamageAmplified | RaceModifier.NoRepairAfter,
        };

        /// <summary>
        /// Example high-stakes event race: standard length but double pay on a reversed grid. Data only,
        /// same as <see cref="Boss"/> — a template for the director, not a wiring decision.
        /// </summary>
        public static RaceRuleset DoubleOrNothing => new RaceRuleset
        {
            Laps = StandardLaps,
            CutoffFraction = StandardCutoffFraction,
            IsBoss = false,
            Modifiers = RaceModifier.DoublePayout | RaceModifier.ReverseGrid,
        };

        /// <summary>True if every bit of <paramref name="modifier"/> is set on this ruleset (None matches always).</summary>
        public bool Has(RaceModifier modifier) => (Modifiers & modifier) == modifier;

        /// <summary>Copy of this ruleset with a different lap count (trivial builder for factories / tuning).</summary>
        public RaceRuleset WithLaps(int laps)
        {
            RaceRuleset copy = this;
            copy.Laps = Mathf.Max(1, laps);
            return copy;
        }

        /// <summary>Copy of this ruleset with a different survival cutoff fraction (clamped to the sane band).</summary>
        public RaceRuleset WithCutoff(float cutoffFraction)
        {
            RaceRuleset copy = this;
            copy.CutoffFraction = Mathf.Clamp(cutoffFraction, 0.01f, 1f);
            return copy;
        }

        /// <summary>Copy of this ruleset with the given modifier bits switched on.</summary>
        public RaceRuleset WithModifier(RaceModifier modifier)
        {
            RaceRuleset copy = this;
            copy.Modifiers |= modifier;
            return copy;
        }
    }
}
