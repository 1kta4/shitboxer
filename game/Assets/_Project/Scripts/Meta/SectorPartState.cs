using System.Collections.Generic;
using Shitboxer.Race;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// The race-scoped accumulator behind sector-scoring parts: carries the style streaks and any
    /// still-running timed bonuses between sectors, resolves the equipped parts' rules each time a
    /// sector closes, and reports the resulting money, grip/power multipliers and durability change.
    ///
    /// Plain C# — no scene, no Time, no engine loop — so the whole scoring pass is unit-testable
    /// against a hand-built sequence of sectors, and a headless server resolves identical payouts. The
    /// host (RunDirector) does nothing but feed it completed sectors and push the multipliers it
    /// returns onto the sim.
    ///
    /// Per doc 08 decision 9, sectors pay NOTHING on their own: with no sector-rule parts equipped this
    /// resolves to zero money and multipliers of exactly 1, so the base position-only inverted economy
    /// and the shipped driving feel are byte-for-byte untouched.
    /// </summary>
    public sealed class SectorPartState
    {
        /// <summary>
        /// Safety rail on the race-scoped bonus multipliers. This is NOT the real stat ceiling — doc 08
        /// decision 1 puts that in the (not yet built) stat ledger, which will cap against measured
        /// physics limits. Until then a bounded band stops a mis-authored rule or a runaway retrigger
        /// from handing the sim a multiplier that makes the car undriveable, which is the failure mode
        /// <see cref="VehicleSpec.Validate"/> exists to prevent one layer down.
        /// </summary>
        public const float MinBonusMult = 0.25f;
        public const float MaxBonusMult = 3f;

        /// <summary>What one closed sector paid out.</summary>
        public readonly struct Totals
        {
            /// <summary>Credits earned by this sector alone.</summary>
            public readonly int Money;
            /// <summary>Durability change to apply now; positive repairs.</summary>
            public readonly float DurabilityDelta;
            /// <summary>The running grip multiplier AFTER this sector — push straight to the sim.</summary>
            public readonly float GripMult;
            /// <summary>The running power multiplier after this sector.</summary>
            public readonly float PowerMult;
            /// <summary>Extra repeats applied this sector (0 = rules fired once). Telemetry / HUD only.</summary>
            public readonly int Retriggers;

            public Totals(int money, float durabilityDelta, float gripMult, float powerMult, int retriggers)
            {
                Money = money;
                DurabilityDelta = durabilityDelta;
                GripMult = gripMult;
                PowerMult = powerMult;
                Retriggers = retriggers;
            }
        }

        // A granted Grip/Power bonus still in force. SectorsRemaining 0 means "rest of the race".
        private struct TimedEffect
        {
            public SectorEffectKind Kind;
            public SpecModOp Op;
            public float Amount;
            public int SectorsRemaining;
            public bool Permanent;
        }

        private readonly List<TimedEffect> _effects = new List<TimedEffect>();
        private StyleStreaks _streaks;

        /// <summary>
        /// Last time set for each sector INDEX, so a pace rule compares like with like.
        ///
        /// Comparing against "the previous sector you drove" would compare S1 against S2 — and sectors
        /// are equal by DISTANCE, not time, so a corner-heavy third of the track takes seconds longer
        /// than a straight one. A tolerance tight enough to mean anything would then never fire.
        /// Consistency, as a driver means it, is this sector against your last run through THIS sector.
        /// </summary>
        private readonly List<float> _previousTimeByIndex = new List<float>(4);

        // Reused across sectors so flattening the equipped parts into their rules costs no allocation.
        // Nine sectors a race makes this a rounding error either way, but a per-sector garbage spike is
        // exactly the kind of thing that becomes a per-frame one when someone reuses this later.
        private readonly List<SectorRule> _scratch = new List<SectorRule>(16);

        /// <summary>Running grip multiplier for the sim; exactly 1 with no sector parts equipped.</summary>
        public float GripMult { get; private set; } = 1f;

        /// <summary>Running power multiplier for the sim; exactly 1 with no sector parts equipped.</summary>
        public float PowerMult { get; private set; } = 1f;

        /// <summary>Total credits sector parts have paid this race.</summary>
        public int MoneyEarned { get; private set; }

        /// <summary>Race-start reset: clear streaks, timed effects, earnings and multipliers.</summary>
        public void Reset()
        {
            _effects.Clear();
            _streaks.Reset();
            _previousTimeByIndex.Clear();
            GripMult = 1f;
            PowerMult = 1f;
            MoneyEarned = 0;
        }

        /// <summary>
        /// Resolve one closed sector against the equipped parts.
        ///
        /// Order is load-bearing:
        /// <list type="number">
        /// <item>Expire timed effects granted in earlier sectors — done FIRST so a 1-sector bonus is in
        /// force for exactly the one sector that follows its grant, not two.</item>
        /// <item>Fold this sector's style into the streaks, so a "3 in a row" rule sees the count
        /// including the sector that just ended.</item>
        /// <item>Total the retriggers, so they multiply every other rule including ones on parts slotted
        /// before the retrigger part — a retrigger that only affected later slots would make slot order
        /// matter in a way nothing tells the player about.</item>
        /// <item>Apply the remaining rules, then recompute the multipliers from what is still in force.</item>
        /// </list>
        /// A null or empty part list resolves to zero money and multipliers of 1.
        /// </summary>
        public Totals Resolve(IReadOnlyList<PartDef> equipped, int sectorIndex, SectorStyle style,
            SectorColour colour, float sectorTimeS, int contactsTaken, int positionsGained,
            bool isFinalSectorOfRace)
        {
            _scratch.Clear();
            if (equipped != null)
                foreach (PartDef part in equipped)
                    if (part != null && part.SectorRules != null)
                        _scratch.AddRange(part.SectorRules);

            return ResolveRules(_scratch, sectorIndex, style, colour, sectorTimeS, contactsTaken,
                positionsGained, isFinalSectorOfRace);
        }

        /// <summary>
        /// The scoring core, taking rules directly rather than the parts carrying them. Split out so the
        /// whole pass is testable without constructing <see cref="PartDef"/> ScriptableObjects (which
        /// need a live editor), and because nothing in here has any business knowing what a part is.
        /// See <see cref="Resolve"/> for the ordering rationale.
        /// </summary>
        public Totals ResolveRules(IReadOnlyList<SectorRule> rules, int sectorIndex, SectorStyle style,
            SectorColour colour, float sectorTimeS, int contactsTaken, int positionsGained,
            bool isFinalSectorOfRace)
        {
            ExpireEffects();
            _streaks.Observe(style);

            var ctx = new SectorContext(style, colour, sectorTimeS, PreviousTimeFor(sectorIndex),
                contactsTaken, positionsGained, isFinalSectorOfRace, _streaks);
            RememberTime(sectorIndex, sectorTimeS);

            int retriggers = CountRetriggers(rules, ctx);
            int applications = 1 + Mathf.Max(0, retriggers);

            int money = 0;
            float durability = 0f;

            if (rules != null)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    SectorRule rule = rules[i];
                    if (rule.Effect == SectorEffectKind.None || rule.Effect == SectorEffectKind.Retrigger)
                        continue;

                    int fires = SectorRuleMath.FireCount(rule, ctx);
                    if (fires <= 0) continue;

                    int times = fires * applications;
                    switch (rule.Effect)
                    {
                        case SectorEffectKind.Money:
                            money += Mathf.RoundToInt(rule.Amount) * times;
                            break;
                        case SectorEffectKind.Durability:
                            durability += rule.Amount * times;
                            break;
                        case SectorEffectKind.Grip:
                        case SectorEffectKind.Power:
                            Grant(rule, times);
                            break;
                    }
                }
            }

            RecomputeMultipliers();
            MoneyEarned += money;
            return new Totals(money, durability, GripMult, PowerMult, retriggers);
        }

        /// <summary>Your last time through this sector index, or -1 if you haven't run it yet this race.</summary>
        private float PreviousTimeFor(int sectorIndex) =>
            sectorIndex >= 0 && sectorIndex < _previousTimeByIndex.Count
                ? _previousTimeByIndex[sectorIndex]
                : -1f;

        private void RememberTime(int sectorIndex, float timeS)
        {
            if (sectorIndex < 0) return;
            while (_previousTimeByIndex.Count <= sectorIndex) _previousTimeByIndex.Add(-1f);
            _previousTimeByIndex[sectorIndex] = timeS;
        }

        private static int CountRetriggers(IReadOnlyList<SectorRule> rules, in SectorContext ctx)
        {
            if (rules == null) return 0;
            int total = 0;
            for (int i = 0; i < rules.Count; i++)
            {
                SectorRule rule = rules[i];
                if (rule.Effect != SectorEffectKind.Retrigger) continue;
                int fires = SectorRuleMath.FireCount(rule, ctx);
                if (fires > 0) total += Mathf.Max(0, Mathf.RoundToInt(rule.Amount)) * fires;
            }
            // Bounded: a pathological pair of retrigger parts must not turn one sector into an
            // unbounded loop. Four repeats is already far beyond anything the collection should want.
            return Mathf.Min(total, 4);
        }

        private void Grant(in SectorRule rule, int times)
        {
            // A Multiply factor at or below zero would zero (or invert) the car's grip or power; reject
            // it here rather than relying on the clamp downstream to hide a mis-authored asset.
            if (rule.Op == SpecModOp.Multiply && !(rule.Amount > 0f)) return;

            _effects.Add(new TimedEffect
            {
                Kind = rule.Effect,
                Op = rule.Op,
                // Add scales linearly with the repeat count; Multiply compounds, which is what "this
                // rule fired twice" has to mean for a factor.
                Amount = rule.Op == SpecModOp.Add ? rule.Amount * times : Mathf.Pow(rule.Amount, times),
                SectorsRemaining = Mathf.Max(0, rule.DurationSectors),
                Permanent = rule.DurationSectors <= 0,
            });
        }

        private void ExpireEffects()
        {
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                TimedEffect e = _effects[i];
                if (e.Permanent) continue;
                e.SectorsRemaining--;
                if (e.SectorsRemaining <= 0) _effects.RemoveAt(i);
                else _effects[i] = e;
            }
        }

        /// <summary>
        /// Rebuild both multipliers from the effects still in force, walking them in GRANT ORDER so Add
        /// and Multiply compose the same way <see cref="SpecModApplier"/> composes stat parts: an Add
        /// that landed before a Multiply is worth more than the reverse.
        /// </summary>
        private void RecomputeMultipliers()
        {
            float grip = 1f, power = 1f;
            foreach (TimedEffect e in _effects)
            {
                if (e.Kind == SectorEffectKind.Grip)
                    grip = e.Op == SpecModOp.Add ? grip + e.Amount : grip * e.Amount;
                else if (e.Kind == SectorEffectKind.Power)
                    power = e.Op == SpecModOp.Add ? power + e.Amount : power * e.Amount;
            }
            GripMult = Mathf.Clamp(grip, MinBonusMult, MaxBonusMult);
            PowerMult = Mathf.Clamp(power, MinBonusMult, MaxBonusMult);
        }
    }
}
