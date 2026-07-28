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
        /// <summary>The grid is seeded in reverse championship order. Declared, NOT yet wired —
        /// RaceManager's grid seeding ignores it today; don't put it on a live boss until it is.</summary>
        ReverseGrid = 1 << 2,
        /// <summary>Contact / attack damage is amplified for everyone on track.</summary>
        DamageAmplified = 1 << 3,
        /// <summary>No slipstream this race: every DraftSensor is disabled at bind, so drafting,
        /// draft-charged actives, draft-leech income and SLIPSTREAM sector tags all read dead air.</summary>
        DirtyAir = 1 << 4,
        /// <summary>Every active-item deploy is taxed an extra fee this race (doc 08 decision 14's
        /// "cost-to-use is what makes the boss's per-use tax bite"). The fee lands on the same
        /// UseCost path a PaidUse active bills through.</summary>
        ActiveTaxed = 1 << 5,
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

        [Tooltip("Display name for a boss/event race (\"THE TAXMAN\") — what the HUD status line and the race summary call it. Null/empty on Standard.")]
        public string Title;

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
        /// The headline boss template — THE ENFORCER, first entry of <see cref="BossForCircuit"/>'s
        /// rotation: a longer, damage-amplified duel with no post-race repair, held to a tighter
        /// survival window. Data only — nothing here decides which race is a boss.
        /// </summary>
        public static RaceRuleset Boss => BossForCircuit(0);

        /// <summary>
        /// The per-circuit boss rotation (doc 08 slice 12): an 8-circuit season meets each boss
        /// twice instead of the same top-3 gate eight times. Every boss withholds the interlude
        /// repair (NoRepairAfter) — boss damage riding into the garage is the shared identity; the
        /// rest is one distinct lever each, and every lever is one that is actually WIRED (which is
        /// why there is no ReverseGrid boss yet). Cycles for any circuit index, so a longer season
        /// never runs off the table.
        /// </summary>
        public static RaceRuleset BossForCircuit(int circuitIndex)
        {
            int count = 4;
            int slot = ((circuitIndex % count) + count) % count; // negatives wrap too
            switch (slot)
            {
                default: // 0 — the shipped headline boss, unchanged
                    return new RaceRuleset
                    {
                        Laps = 5,
                        CutoffFraction = 0.10f,
                        IsBoss = true,
                        Modifiers = RaceModifier.DamageAmplified | RaceModifier.NoRepairAfter,
                        Title = "THE ENFORCER",
                    };
                case 1: // no tows: drafting builds, Parasite income and SLIPSTREAM styles all die
                    return new RaceRuleset
                    {
                        Laps = 5,
                        CutoffFraction = 0.10f,
                        IsBoss = true,
                        Modifiers = RaceModifier.DirtyAir | RaceModifier.NoRepairAfter,
                        Title = "DIRTY AIR",
                    };
                case 2: // your buttons cost money, but a clean finish pays double
                    return new RaceRuleset
                    {
                        Laps = 5,
                        CutoffFraction = 0.10f,
                        IsBoss = true,
                        Modifiers = RaceModifier.ActiveTaxed | RaceModifier.DoublePayout | RaceModifier.NoRepairAfter,
                        Title = "THE TAXMAN",
                    };
                case 3: // endurance: with decision 15 live, seven laps of contact is a durability exam
                    return new RaceRuleset
                    {
                        Laps = 7,
                        CutoffFraction = 0.12f,
                        IsBoss = true,
                        Modifiers = RaceModifier.NoRepairAfter,
                        Title = "THE LONG HAUL",
                    };
            }
        }

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
