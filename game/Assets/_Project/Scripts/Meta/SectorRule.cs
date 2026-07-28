using System;
using Shitboxer.Race;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// What makes a sector part fire. Sectors are doc 08's replacement for Balatro's poker hands, so
    /// this is the "which hand type does this joker care about" axis — except a racing sector can be
    /// described several ways at once, hence the mix of style, timing and outcome triggers.
    /// <see cref="None"/> is the default so an existing PartDef with an empty rule list stays inert.
    /// </summary>
    public enum SectorTriggerKind
    {
        /// <summary>Never fires — the default, so a rule left unconfigured is a no-op rather than a surprise.</summary>
        None = 0,
        /// <summary>The sector carried <see cref="SectorRule.StyleTag"/>.</summary>
        Style,
        /// <summary>Fires the moment <see cref="SectorRule.StyleTag"/> has held for exactly <see cref="SectorRule.StreakLength"/> consecutive sectors.</summary>
        StyleStreak,
        /// <summary>The sector took <see cref="SectorRule.TimingColour"/> (purple / green).</summary>
        Colour,
        /// <summary>Once per contact TAKEN in the sector. Pairs with <see cref="SectorRule.ScaleByCount"/>.</summary>
        ContactTaken,
        /// <summary>Once per place gained in the sector.</summary>
        PositionGained,
        /// <summary>The final sector of the final lap.</summary>
        FinalSector,
        /// <summary>Within <see cref="SectorRule.PaceToleranceS"/> of the previous sector's time.</summary>
        ConsistentPace,
    }

    /// <summary>
    /// What a fired rule does. Money lands in the run's in-race earnings; Grip and Power drive the
    /// sim's race-scoped bonus multipliers; Durability repairs (positive) or wears (negative).
    /// Retrigger is the odd one out — it makes every OTHER rule fire again this sector, which is how
    /// Balatro's retrigger jokers translate once sectors are the scoring unit.
    /// </summary>
    public enum SectorEffectKind
    {
        None = 0,
        Money,
        Grip,
        Power,
        Durability,
        /// <summary>Repeats every other rule this sector. <see cref="SectorRule.Amount"/> = extra repeats.</summary>
        Retrigger,
    }

    /// <summary>
    /// One "when X happens in a sector, do Y" clause on a part. A part can carry several, so a single
    /// part can reward two different styles or pair a bonus with a drawback.
    ///
    /// Deliberately DATA on the asset rather than code keyed by part id, unlike <see cref="TeamUpgrade"/>.
    /// The line this project draws is "content is assets, shop RULES are code" — and 150 parts that each
    /// say "on style S, grant N of resource R" is content, not rules. Hand-coding them would also make
    /// every balance pass a recompile.
    /// </summary>
    [Serializable]
    public struct SectorRule
    {
        [Tooltip("What makes this clause fire. None (default) = inert.")]
        public SectorTriggerKind Trigger;

        [Tooltip("Which driven style the trigger watches (Style / StyleStreak triggers only).")]
        public SectorStyle StyleTag;

        [Tooltip("Which timing colour the trigger watches (Colour trigger only).")]
        public SectorColour TimingColour;

        [Tooltip("Consecutive sectors the style must hold (StyleStreak only). Fires on reaching exactly this length, so a permanent multiplier can't compound every sector afterwards.")]
        [Min(1)] public int StreakLength;

        [Tooltip("Seconds of tolerance against your LAST LAP THROUGH THIS SAME SECTOR (ConsistentPace only). Not against the previous sector driven — sectors are equal by distance, so adjacent ones differ by seconds and no useful tolerance would ever be met.")]
        public float PaceToleranceS;

        [Tooltip("What happens when it fires.")]
        public SectorEffectKind Effect;

        [Tooltip("Multiply scales the running bonus factor, Add adds to it — the same op split stat parts use. Ignored for Money and Durability, which are always additive.")]
        public SpecModOp Op;

        [Tooltip("Effect magnitude. Money: whole credits. Grip/Power with Add: a +fraction (0.04 = +4%). Grip/Power with Multiply: a factor (1.5 = +50%). Durability: a 0..1 fraction, positive repairs. Retrigger: extra repeats.")]
        public float Amount;

        [Tooltip("How many sectors the effect lasts. 0 = the rest of the race. Only meaningful for Grip and Power; Money and Durability are instant.")]
        [Min(0)] public int DurationSectors;

        [Tooltip("Multiply the amount by how many times the trigger occurred this sector (contacts taken, places gained). Off = a flat grant however many times it happened.")]
        public bool ScaleByCount;
    }

    /// <summary>
    /// Consecutive-sector counters, one per style tag. A streak trigger asks "how many sectors in a row
    /// have been CLEAN", which the flags value of a single sector can't answer — the count has to be
    /// carried between sectors, and it has to be PER TAG because a sector that is both Aggressive and
    /// Ragged continues both of those streaks while ending the Clean one.
    ///
    /// Plain C# and engine-independent, so a headless server tracks streaks identically.
    /// </summary>
    [Serializable]
    public struct StyleStreaks
    {
        public int Clean, Aggressive, Defensive, Slipstream, Patient, Ragged;

        /// <summary>The current streak for a single tag. Returns 0 for None or a multi-bit value.</summary>
        public int For(SectorStyle tag)
        {
            switch (tag)
            {
                case SectorStyle.Clean: return Clean;
                case SectorStyle.Aggressive: return Aggressive;
                case SectorStyle.Defensive: return Defensive;
                case SectorStyle.Slipstream: return Slipstream;
                case SectorStyle.Patient: return Patient;
                case SectorStyle.Ragged: return Ragged;
                default: return 0;
            }
        }

        /// <summary>
        /// Fold one sector's style in: every tag it carries extends its streak, every tag it lacks
        /// resets to zero. Call once per completed sector, before evaluating streak triggers, so a
        /// rule asking for "3 in a row" sees the count including the sector that just ended.
        /// </summary>
        public void Observe(SectorStyle style)
        {
            Clean = Step(Clean, style, SectorStyle.Clean);
            Aggressive = Step(Aggressive, style, SectorStyle.Aggressive);
            Defensive = Step(Defensive, style, SectorStyle.Defensive);
            Slipstream = Step(Slipstream, style, SectorStyle.Slipstream);
            Patient = Step(Patient, style, SectorStyle.Patient);
            Ragged = Step(Ragged, style, SectorStyle.Ragged);
        }

        /// <summary>Race-start reset — every streak back to zero.</summary>
        public void Reset() => this = default;

        private static int Step(int current, SectorStyle style, SectorStyle tag) =>
            SectorStyleClassifier.Has(style, tag) ? current + 1 : 0;
    }

    /// <summary>
    /// Everything a sector rule can be evaluated against: what the sector was, how it was timed, and
    /// where it sat in the race. Assembled by the runner from the race's
    /// <see cref="SectorCompletion"/> plus the streak counters it carries between sectors.
    ///
    /// A plain readonly struct with no scene or engine access, which is what makes every trigger rule
    /// unit-testable without running a race.
    /// </summary>
    public readonly struct SectorContext
    {
        public readonly SectorStyle Style;
        public readonly SectorColour Colour;
        public readonly float TimeS;
        /// <summary>
        /// Your last time through THIS SAME sector index, or negative on your first run at it this
        /// race. Not the previous sector driven — see <see cref="SectorRule.PaceToleranceS"/>.
        /// </summary>
        public readonly float PreviousTimeS;
        public readonly int ContactsTaken;
        public readonly int PositionsGained;
        /// <summary>True only for the last sector of the last lap.</summary>
        public readonly bool IsFinalSectorOfRace;
        /// <summary>Streak counts INCLUDING the sector just completed.</summary>
        public readonly StyleStreaks Streaks;

        public SectorContext(SectorStyle style, SectorColour colour, float timeS, float previousTimeS,
            int contactsTaken, int positionsGained, bool isFinalSectorOfRace, in StyleStreaks streaks)
        {
            Style = style;
            Colour = colour;
            TimeS = timeS;
            PreviousTimeS = previousTimeS;
            ContactsTaken = contactsTaken;
            PositionsGained = positionsGained;
            IsFinalSectorOfRace = isFinalSectorOfRace;
            Streaks = streaks;
        }
    }

    /// <summary>
    /// Pure trigger evaluation — the single place that decides whether a rule fires and how many times.
    /// Static and side-effect-free so every rule in the collection can be tested against a hand-built
    /// context, and so a headless server resolves identical payouts.
    /// </summary>
    public static class SectorRuleMath
    {
        /// <summary>
        /// How many times <paramref name="rule"/> fires for this sector — 0 when it doesn't.
        /// Count-scaled triggers (contacts taken, places gained) return the raw count when
        /// <see cref="SectorRule.ScaleByCount"/> is set and 1 otherwise, so a part can choose between
        /// "per hit" and "at least one hit" without needing two trigger kinds.
        /// </summary>
        public static int FireCount(in SectorRule rule, in SectorContext ctx)
        {
            switch (rule.Trigger)
            {
                case SectorTriggerKind.Style:
                    return SectorStyleClassifier.Has(ctx.Style, rule.StyleTag) ? 1 : 0;

                case SectorTriggerKind.StyleStreak:
                    // Fires on REACHING the length, not on being at or past it. Otherwise a permanent
                    // multiplier would compound every sector for the rest of a clean race.
                    return rule.StreakLength >= 1 && ctx.Streaks.For(rule.StyleTag) == rule.StreakLength ? 1 : 0;

                case SectorTriggerKind.Colour:
                    return ctx.Colour == rule.TimingColour ? 1 : 0;

                case SectorTriggerKind.ContactTaken:
                    return Scaled(ctx.ContactsTaken, rule.ScaleByCount);

                case SectorTriggerKind.PositionGained:
                    return Scaled(ctx.PositionsGained, rule.ScaleByCount);

                case SectorTriggerKind.FinalSector:
                    return ctx.IsFinalSectorOfRace ? 1 : 0;

                case SectorTriggerKind.ConsistentPace:
                    // Needs a previous run at THIS sector, so it never fires on the opening lap.
                    if (ctx.PreviousTimeS < 0f || ctx.TimeS < 0f) return 0;
                    return Mathf.Abs(ctx.TimeS - ctx.PreviousTimeS) <= Mathf.Max(0f, rule.PaceToleranceS) ? 1 : 0;

                default:
                    return 0;
            }
        }

        private static int Scaled(int count, bool scaleByCount) =>
            count <= 0 ? 0 : (scaleByCount ? count : 1);
    }
}
