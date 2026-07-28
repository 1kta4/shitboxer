using System;
using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// How a sector was DRIVEN — the racing analog of a Balatro poker hand, and the reason sectors exist
    /// as gameplay rather than telemetry (doc 08, decision 7).
    ///
    /// In Balatro you choose five cards, that makes a hand TYPE, and jokers reward types; the choice
    /// repeats four or five times a blind. Here you choose how to drive a sector, that makes a style, and
    /// parts reward styles — nine times a race instead of the one-shot moments (start reaction time) the
    /// source collection leaned on. That repetition is the whole point: a once-per-race trigger is a
    /// twitch, a nine-times-per-race trigger is a strategy.
    ///
    /// <c>[Flags]</c> and deliberately NON-EXCLUSIVE: a sector can be both AGGRESSIVE and RAGGED (you
    /// forced a move and it went wrong), or SLIPSTREAM and PATIENT (you sat in the tow and lifted).
    /// Balatro hands are exclusive — best hand wins — but forcing exclusivity here would mean inventing
    /// a priority order between "you passed someone" and "you went off", which has no natural answer and
    /// would silently discard half the evidence a part might want to read.
    ///
    /// <see cref="Clean"/> and <see cref="Ragged"/> are the one pair that cannot co-occur, and that falls
    /// out of the rules rather than being special-cased: everything that raises Ragged also disqualifies
    /// Clean.
    /// </summary>
    [Flags]
    public enum SectorStyle
    {
        /// <summary>Nothing classified — an un-driven or zero-length sector.</summary>
        None = 0,
        /// <summary>No contact, no spin, no excursion, no damage. You drove it properly.</summary>
        Clean = 1 << 0,
        /// <summary>You initiated contact or took a place.</summary>
        Aggressive = 1 << 1,
        /// <summary>You were under pressure the whole way and gave up nothing.</summary>
        Defensive = 1 << 2,
        /// <summary>You spent most of it in someone's tow.</summary>
        Slipstream = 1 << 3,
        /// <summary>You spent a real share of it off the pedals — coasting, lifting, conserving.</summary>
        Patient = 1 << 4,
        /// <summary>It went wrong: a spin, an excursion, or damage taken.</summary>
        Ragged = 1 << 5,
    }

    /// <summary>
    /// Everything observed about one car over one sector, already reduced to plain numbers. Filled by the
    /// host layer (which owns the collision callbacks, the draft sensor and the input) and consumed by
    /// <see cref="SectorStyleClassifier"/>, which touches no scene — so every classification rule is
    /// unit-testable without a race, and a headless server classifies identically.
    ///
    /// Durations are seconds and are compared as FRACTIONS of <see cref="DurationS"/>, never as absolute
    /// values: sector duration varies enormously between a 190 km/h speedway sector and a hairpin
    /// complex, and absolute thresholds would silently mean different things on different tracks.
    /// </summary>
    public struct SectorEvidence
    {
        /// <summary>Seconds the sector took. The denominator for every fraction below; zero disables classification.</summary>
        public float DurationS;

        /// <summary>Contacts where THIS car drove in (VehicleCombat aggressorness above 0.5).</summary>
        public int ContactsAsAggressor;
        /// <summary>Contacts where this car was the one rammed. Breaks Clean, but never raises Ragged — being hit is not your mistake.</summary>
        public int ContactsAsVictim;

        /// <summary>Track positions taken over the sector.</summary>
        public int PositionsGained;
        /// <summary>Track positions surrendered over the sector.</summary>
        public int PositionsLost;

        /// <summary>Seconds sitting in another car's tow (DraftSensor).</summary>
        public float DraftSeconds;
        /// <summary>Seconds with neither pedal applied.</summary>
        public float CoastSeconds;
        /// <summary>Seconds with a rival close enough behind to be a threat.</summary>
        public float PressureSeconds;

        /// <summary>Seconds beyond the spin slip-angle threshold.</summary>
        public float SpinSeconds;
        /// <summary>Seconds with a wheel on a low-grip surface (grass/dirt/gravel).</summary>
        public float OffSurfaceSeconds;
        /// <summary>Persistent durability lost across the sector.</summary>
        public float DurabilityLost;
    }

    /// <summary>
    /// Turns <see cref="SectorEvidence"/> into a <see cref="SectorStyle"/>. Pure and static — no scene,
    /// no Time, no Random — so the rules are testable directly and a headless server derives the same
    /// style from the same numbers.
    ///
    /// Every threshold is a named public const rather than a magic number, because these ARE the design:
    /// tuning what counts as "patient" is tuning what the Coward's Purse part rewards, and that
    /// conversation should happen against a named number in one file.
    /// </summary>
    public static class SectorStyleClassifier
    {
        // --- Fraction-of-sector thresholds ---------------------------------------------------------
        /// <summary>Share of the sector spent in a tow before it counts as a slipstream sector.</summary>
        public const float DraftFraction = 0.40f;
        /// <summary>Share of the sector spent off the pedals before it counts as patient.</summary>
        public const float CoastFraction = 0.25f;
        /// <summary>Share of the sector spent under pressure before a hold counts as a defensive sector.</summary>
        public const float PressureFraction = 0.35f;

        // --- Absolute tolerances: below these, an incident is noise rather than a mistake -----------
        /// <summary>Sustained oversteer below this is a slide you caught, not a spin.</summary>
        public const float SpinSecondsTolerance = 0.35f;
        /// <summary>A wheel brushing the grass for less than this is not an excursion.</summary>
        public const float OffSurfaceSecondsTolerance = 0.30f;
        /// <summary>Durability loss below this is a scrape, not damage. Matches the scale of a light tap.</summary>
        public const float DurabilityLostTolerance = 0.005f;

        /// <summary>
        /// Classify one sector. Returns <see cref="SectorStyle.None"/> for a zero-length or non-finite
        /// sector, so a car that crossed two boundaries in one physics step (impossible under the
        /// teleport guard, but cheap to be safe about) cannot produce a bogus style.
        /// </summary>
        public static SectorStyle Classify(in SectorEvidence e)
        {
            if (!(e.DurationS > 0f) || float.IsInfinity(e.DurationS)) return SectorStyle.None;

            SectorStyle style = SectorStyle.None;

            bool spun = e.SpinSeconds > SpinSecondsTolerance;
            bool wentOff = e.OffSurfaceSeconds > OffSurfaceSecondsTolerance;
            bool damaged = e.DurabilityLost > DurabilityLostTolerance;
            bool anyContact = e.ContactsAsAggressor > 0 || e.ContactsAsVictim > 0;

            // Ragged: it went wrong. Being RAMMED is deliberately excluded — a victim of someone else's
            // divebomb has not driven a ragged sector, though they have lost their clean one.
            if (spun || wentOff || damaged) style |= SectorStyle.Ragged;

            // Clean: nothing happened to you and nothing happened because of you. Note this is strictly
            // the complement of Ragged plus "no contact at all", so the two can never both be set.
            if (!spun && !wentOff && !damaged && !anyContact) style |= SectorStyle.Clean;

            // Aggressive: you made something happen — drove into someone, or took a place.
            if (e.ContactsAsAggressor > 0 || e.PositionsGained > 0) style |= SectorStyle.Aggressive;

            // Defensive: you were leaned on for a real share of the sector and conceded nothing. The
            // "lost nothing" half is what stops a car that simply got passed from being credited for
            // defending — being under pressure is not the same as withstanding it.
            if (e.PressureSeconds / e.DurationS >= PressureFraction && e.PositionsLost <= 0)
                style |= SectorStyle.Defensive;

            if (e.DraftSeconds / e.DurationS >= DraftFraction) style |= SectorStyle.Slipstream;
            if (e.CoastSeconds / e.DurationS >= CoastFraction) style |= SectorStyle.Patient;

            return style;
        }

        /// <summary>True if every bit of <paramref name="tag"/> is set. Mirrors <c>RaceRuleset.Has</c>.</summary>
        public static bool Has(SectorStyle style, SectorStyle tag) => (style & tag) == tag;

        /// <summary>
        /// Short HUD/debug label, most-interesting tag first so a one-line readout stays useful when a
        /// sector earned several. Order is a presentation choice only — no rule reads it.
        /// </summary>
        public static string Describe(SectorStyle style)
        {
            if (style == SectorStyle.None) return "—";

            var sb = new System.Text.StringBuilder(48);
            Append(sb, style, SectorStyle.Aggressive, "AGGRESSIVE");
            Append(sb, style, SectorStyle.Defensive, "DEFENSIVE");
            Append(sb, style, SectorStyle.Ragged, "RAGGED");
            Append(sb, style, SectorStyle.Clean, "CLEAN");
            Append(sb, style, SectorStyle.Slipstream, "SLIPSTREAM");
            Append(sb, style, SectorStyle.Patient, "PATIENT");
            return sb.ToString();
        }

        private static void Append(System.Text.StringBuilder sb, SectorStyle style, SectorStyle tag, string label)
        {
            if (!Has(style, tag)) return;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(label);
        }
    }
}
