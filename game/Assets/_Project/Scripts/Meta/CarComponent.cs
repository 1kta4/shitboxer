using System.Collections.Generic;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// The ten things bolted to every car (doc 08 decision 4) — the source document's "Planet cards",
    /// formalised. Unlike parts these are ALWAYS installed and never bought: what you buy is a
    /// Blueprint that raises one of them a level.
    ///
    /// Ordinals are save-format-relevant only through <see cref="CarComponentCatalog"/>'s ordering, and
    /// <see cref="RunSave"/> stores levels by enum NAME, so inserting a member here can never
    /// reinterpret an existing save's levels as different components.
    /// </summary>
    public enum CarComponent
    {
        Engine,
        Turbo,
        Exhaust,
        Ecu,
        Tyres,
        Suspension,
        Interior,
        Chassis,
        Cooling,
        Transmission,
    }

    /// <summary>Points one component level contributes to one stat. Several per component is normal.</summary>
    public readonly struct StatGain
    {
        public readonly BuildStat Stat;
        public readonly float PointsPerLevel;

        public StatGain(BuildStat stat, float pointsPerLevel)
        {
            Stat = stat;
            PointsPerLevel = pointsPerLevel;
        }
    }

    /// <summary>Everything the garage and the ledger need to know about one component.</summary>
    public readonly struct CarComponentInfo
    {
        public readonly CarComponent Component;
        public readonly string DisplayName;
        public readonly string Description;
        /// <summary>Which of the four stat bars this belongs to — decision 5's "families ARE the stats".</summary>
        public readonly BuildStat Family;
        /// <summary>What one level above the baseline contributes. May span several stats, including penalties.</summary>
        public readonly StatGain[] PerLevel;

        public CarComponentInfo(CarComponent component, string displayName, string description,
            BuildStat family, StatGain[] perLevel)
        {
            Component = component;
            DisplayName = displayName;
            Description = description;
            Family = family;
            PerLevel = perLevel;
        }
    }

    /// <summary>
    /// The component catalogue and the single place a component's stat contribution is defined.
    ///
    /// <b>Level 1 is the baseline and contributes NOTHING.</b> Points scale with <c>level − 1</c>, so a
    /// fresh run — every component at 1 — produces an empty <see cref="BuildLedger"/> and therefore a
    /// spec byte-for-byte identical to the authored chassis. That is what lets the director bake
    /// unconditionally instead of guessing whether a build exists.
    ///
    /// Families follow decision 5 exactly, so the ESC-menu stat display doubles as the family display.
    /// A component's *secondary* contributions may fall outside its family (the chassis is a WEIGHT
    /// component that also stiffens grip) — the family is the grouping tag jokers read, not a claim
    /// that nothing else moves.
    ///
    /// Pure static data, no scene access, so a headless server accumulates identical points.
    /// </summary>
    public static class CarComponentCatalog
    {
        /// <summary>Every component starts here and every Blueprint raises it by one.</summary>
        public const int MinLevel = 1;

        /// <summary>Ceiling per component (decision 11). Ten components × 19 buyable levels = 190 total.</summary>
        public const int MaxLevel = 20;

        private static readonly CarComponentInfo[] Catalogue =
        {
            // ---- POWER family ----
            new CarComponentInfo(CarComponent.Engine, "Engine",
                "Displacement and compression. The single biggest source of power — and it weighs.",
                BuildStat.Power, new[]
                {
                    new StatGain(BuildStat.Power, 3f),
                    new StatGain(BuildStat.Weight, -0.6f),   // negative weight points = heavier
                }),
            new CarComponentInfo(CarComponent.Turbo, "Turbo",
                "Forced induction. Enormous power for the size, and it cooks everything around it.",
                BuildStat.Power, new[]
                {
                    new StatGain(BuildStat.Power, 3f),
                    new StatGain(BuildStat.Durability, -0.5f),
                }),
            new CarComponentInfo(CarComponent.Exhaust, "Exhaust",
                "Lets the engine breathe out. Pure, uncomplicated power.",
                BuildStat.Power, new[] { new StatGain(BuildStat.Power, 2f) }),
            new CarComponentInfo(CarComponent.Ecu, "ECU",
                "Fuelling and timing. Power, and an engine that doesn't detonate itself.",
                BuildStat.Power, new[]
                {
                    new StatGain(BuildStat.Power, 2f),
                    new StatGain(BuildStat.Durability, 0.8f),
                }),

            // ---- GRIP family ----
            new CarComponentInfo(CarComponent.Tyres, "Tyres",
                "The only four patches of the car that touch the road. Nothing matters more.",
                BuildStat.Grip, new[] { new StatGain(BuildStat.Grip, 3f) }),
            new CarComponentInfo(CarComponent.Suspension, "Suspension",
                "Springs, dampers, bars. Keeps the tyres loaded through everything the track does.",
                BuildStat.Grip, new[] { new StatGain(BuildStat.Grip, 2f) }),

            // ---- WEIGHT family ----
            new CarComponentInfo(CarComponent.Interior, "Interior",
                "Everything you throw out. Carpet, trim, sound deadening, the passenger seat.",
                BuildStat.Weight, new[] { new StatGain(BuildStat.Weight, 3f) }),
            new CarComponentInfo(CarComponent.Chassis, "Chassis",
                "Seam welds and a cage. Lighter and considerably stiffer.",
                BuildStat.Weight, new[]
                {
                    new StatGain(BuildStat.Weight, 2f),
                    new StatGain(BuildStat.Grip, 1f),
                }),

            // ---- DURABILITY family ----
            new CarComponentInfo(CarComponent.Cooling, "Cooling",
                "Rad, oil cooler, ducting. Heat is what actually kills a race car.",
                BuildStat.Durability, new[] { new StatGain(BuildStat.Durability, 3f) }),
            new CarComponentInfo(CarComponent.Transmission, "Transmission",
                "A gearbox that survives being hit. Takes the abuse and passes the power through.",
                BuildStat.Durability, new[]
                {
                    new StatGain(BuildStat.Durability, 2f),
                    new StatGain(BuildStat.Power, 1f),
                }),
        };

        /// <summary>Every component, in garage display order (grouped by family).</summary>
        public static IReadOnlyList<CarComponentInfo> All => Catalogue;

        /// <summary>How many components exist. The length every level array must have.</summary>
        public static int Count => Catalogue.Length;

        /// <summary>Catalogue entry for a component. Falls back to the first entry for an out-of-range value.</summary>
        public static CarComponentInfo Info(CarComponent component)
        {
            int index = (int)component;
            return index >= 0 && index < Catalogue.Length ? Catalogue[index] : Catalogue[0];
        }

        /// <summary>Clamps a level into the legal band. Anything unset or corrupt reads as the baseline.</summary>
        public static int ClampLevel(int level) => Mathf.Clamp(level, MinLevel, MaxLevel);

        /// <summary>
        /// Total stat points a whole set of component levels contributes. Indexed by
        /// <see cref="CarComponent"/> ordinal; a short or null array reads the missing entries as the
        /// baseline, so a save written before a component existed loads as "that one is at level 1".
        ///
        /// An all-baseline set returns a default (all-zero) ledger — the identity that keeps a fresh
        /// run's car byte-for-byte the authored chassis.
        /// </summary>
        public static BuildLedger Accumulate(IReadOnlyList<int> levels)
        {
            var ledger = new BuildLedger();
            for (int i = 0; i < Catalogue.Length; i++)
            {
                int level = levels != null && i < levels.Count ? ClampLevel(levels[i]) : MinLevel;
                int steps = level - MinLevel;
                if (steps <= 0) continue;

                StatGain[] gains = Catalogue[i].PerLevel;
                for (int g = 0; g < gains.Length; g++)
                    ledger.Add(gains[g].Stat, gains[g].PointsPerLevel * steps);
            }
            return ledger;
        }

        /// <summary>
        /// Cost of the Blueprint that raises a component from <paramref name="currentLevel"/> to the
        /// next. Escalates gently so deep investment in one component genuinely costs more than
        /// spreading levels around, without making the last few levels unreachable.
        ///
        /// FIRST-PASS NUMBERS. Maxing a single component from 1 to 20 costs roughly $85 against a
        /// season's ~$250 of position income, which should mean a run deepens three or four components
        /// rather than maxing one — but that balance has never been played and is a tuning target.
        /// </summary>
        public static int BlueprintPrice(int currentLevel) =>
            Mathf.Max(2, 2 + ClampLevel(currentLevel) / 4);

        /// <summary>True if this component can still be levelled.</summary>
        public static bool CanLevel(int currentLevel) => currentLevel < MaxLevel;
    }
}
