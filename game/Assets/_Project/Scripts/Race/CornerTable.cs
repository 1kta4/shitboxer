using System.Collections.Generic;
using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>How tight a corner is. Metrics are stored per CLASS, never per corner index.</summary>
    public enum CornerClass : byte
    {
        Fast = 0,
        Medium,
        Hairpin,
    }

    /// <summary>One corner found on the racing line, in arc-length coordinates.</summary>
    public readonly struct Corner
    {
        /// <summary>Arc-length where the corner starts (turn-in).</summary>
        public readonly float EntryM;
        /// <summary>Arc-length of peak curvature.</summary>
        public readonly float ApexM;
        /// <summary>Arc-length where it straightens out again.</summary>
        public readonly float ExitM;
        /// <summary>+1 = right-hander (inside is +lateral), -1 = left-hander (inside is -lateral).</summary>
        public readonly float Sign;
        /// <summary>Peak |curvature| (rad/m) through the corner.</summary>
        public readonly float PeakKappa;
        public readonly CornerClass Class;

        public Corner(float entryM, float apexM, float exitM, float sign, float peakKappa, CornerClass cls)
        {
            EntryM = entryM;
            ApexM = apexM;
            ExitM = exitM;
            Sign = sign;
            PeakKappa = peakKappa;
            Class = cls;
        }

        /// <summary>Arc length from turn-in to exit.</summary>
        public float LengthM => ExitM - EntryM;
    }

    /// <summary>
    /// The corners of one track, found once by sweeping <see cref="RacingLine.SignedCurvatureAt"/>.
    ///
    /// WHY THIS EXISTS. Almost every behavioural metric worth learning is only meaningful RELATIVE TO A
    /// CORNER: braking is late compared to a braking point, a pass is inside or outside depending on which
    /// way the track bends, a defensive line is a shift from where you'd otherwise be. Without a corner
    /// table all of those collapse into un-normalised noise across three tracks of different shapes.
    ///
    /// Pure and static: takes a <see cref="RacingLine"/>, returns data. No scene, no Time, no Random, so it
    /// is unit-testable against a hand-built line and a headless server derives the identical table.
    /// </summary>
    public sealed class CornerTable
    {
        // --- Detection tuning ---------------------------------------------------------------------------
        // Hysteresis is load-bearing: a centripetal Catmull-Rom line has small curvature ripples, and a
        // single threshold shatters one real corner into a handful of fragments that each look like their
        // own (very short) corner. Enter high, leave low.
        public const float EnterKappa = 0.010f;
        public const float ExitKappa = 0.006f;
        public const float SampleStepM = 2f;
        /// <summary>Matches BotBrain's own curvature window so the table and the speed planner agree.</summary>
        public const float HalfWindowM = 6f;
        /// <summary>Runs closer than this are the same corner interrupted by a ripple.</summary>
        public const float MergeGapM = 15f;
        /// <summary>Shorter than this is a kink, not a corner.</summary>
        public const float MinLengthM = 8f;

        // Class thresholds on peak |curvature| (rad/m). ~1/radius: 0.02 ≈ 50 m radius, 0.05 ≈ 20 m.
        public const float MediumKappa = 0.020f;
        public const float HairpinKappa = 0.050f;

        private readonly Corner[] _corners;
        private readonly float _totalLength;

        public IReadOnlyList<Corner> Corners => _corners;
        public int Count => _corners.Length;

        private CornerTable(Corner[] corners, float totalLength)
        {
            _corners = corners;
            _totalLength = totalLength;
        }

        /// <summary>An empty table — every query reports "straight". Used when there is no line to read.</summary>
        public static CornerTable Empty => new CornerTable(System.Array.Empty<Corner>(), 1f);

        /// <summary>
        /// Sweeps the line and returns its corners. Walks the loop at <see cref="SampleStepM"/>, opens a run
        /// when |kappa| crosses <see cref="EnterKappa"/> and closes it below <see cref="ExitKappa"/>, then
        /// merges near-touching runs and drops the ones too short to be a corner.
        /// </summary>
        public static CornerTable Build(RacingLine line)
        {
            if (line == null || line.TotalLength <= 0f) return Empty;

            float total = line.TotalLength;
            int samples = Mathf.Max(4, Mathf.CeilToInt(total / SampleStepM));

            // Pass 1 — raw runs of "we are cornering".
            var runs = new List<(float start, float end, float peak, float sign)>();
            bool inCorner = false;
            float runStart = 0f, runPeak = 0f, runPeakSigned = 0f;

            for (int i = 0; i <= samples; i++)
            {
                float d = i * total / samples;
                float k = line.SignedCurvatureAt(d, HalfWindowM);
                float mag = Mathf.Abs(k);

                if (!inCorner && mag >= EnterKappa)
                {
                    inCorner = true;
                    runStart = d;
                    runPeak = mag;
                    runPeakSigned = k;
                }
                else if (inCorner)
                {
                    if (mag > runPeak) { runPeak = mag; runPeakSigned = k; }
                    if (mag < ExitKappa)
                    {
                        runs.Add((runStart, d, runPeak, Mathf.Sign(runPeakSigned)));
                        inCorner = false;
                    }
                }
            }
            if (inCorner) runs.Add((runStart, total, runPeak, Mathf.Sign(runPeakSigned)));

            // Pass 2 — merge runs separated by less than MergeGapM, but only when they bend the SAME way.
            // Merging opposite-handed runs would fuse an S-bend into one corner with a meaningless sign.
            var merged = new List<(float start, float end, float peak, float sign)>();
            foreach (var r in runs)
            {
                if (merged.Count > 0)
                {
                    var last = merged[merged.Count - 1];
                    if (r.start - last.end < MergeGapM && Mathf.Approximately(last.sign, r.sign))
                    {
                        merged[merged.Count - 1] = (last.start, r.end, Mathf.Max(last.peak, r.peak), last.sign);
                        continue;
                    }
                }
                merged.Add(r);
            }

            // Pass 3 — drop kinks, classify, and locate the apex by re-scanning the merged span.
            var corners = new List<Corner>(merged.Count);
            foreach (var r in merged)
            {
                if (r.end - r.start < MinLengthM) continue;

                float apex = r.start;
                float best = -1f;
                for (float d = r.start; d <= r.end; d += SampleStepM)
                {
                    float mag = Mathf.Abs(line.SignedCurvatureAt(d, HalfWindowM));
                    if (mag > best) { best = mag; apex = d; }
                }

                CornerClass cls = r.peak >= HairpinKappa ? CornerClass.Hairpin
                    : r.peak >= MediumKappa ? CornerClass.Medium
                    : CornerClass.Fast;

                corners.Add(new Corner(r.start, apex, r.end, r.sign, r.peak, cls));
            }

            return new CornerTable(corners.ToArray(), total);
        }

        /// <summary>
        /// The corner containing <paramref name="distance"/>, or the next one starting within
        /// <paramref name="lookaheadM"/>. False when the car is on a genuine straight — which is a real
        /// answer, not a failure: a pass completed there carries no side information at all, and folding it
        /// into a left/right preference would teach a rival the TRACK's shape rather than the player's habit.
        /// </summary>
        public bool TryGetCornerAt(float distance, float lookaheadM, out Corner corner)
        {
            for (int i = 0; i < _corners.Length; i++)
            {
                Corner c = _corners[i];
                if (distance >= c.EntryM && distance <= c.ExitM) { corner = c; return true; }

                // Wrapped forward distance to this corner's entry, so a lookahead near the start/finish
                // line still finds the first corner of the lap.
                float ahead = c.EntryM - distance;
                if (ahead < 0f) ahead += _totalLength;
                if (ahead <= lookaheadM) { corner = c; return true; }
            }
            corner = default;
            return false;
        }
    }
}
