using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// A decayed success/failure tally for a PROPORTION — "how often does this player go inside", "how
    /// often do their passes stick".
    ///
    /// WHY COUNTS RATHER THAN AN EWMA. An exponentially-weighted average of a 0/1 sequence gives you the
    /// estimate but tells you nothing about how much evidence is behind it, so you end up carrying a
    /// parallel sample counter — at which point you have built a decayed Beta with extra steps and no
    /// variance. Keeping a and b explicitly gives estimate, sample count and confidence from one structure.
    ///
    /// The prior is deliberately NOT baked into the stored counts: it is applied at read time, so the
    /// prior strength can be retuned later without invalidating every existing save, and <see cref="N"/>
    /// stays an honest "how much have I actually seen".
    /// </summary>
    [System.Serializable]
    public struct BetaStat
    {
        public float a; // successes / "yes" evidence
        public float b; // failures / "no" evidence

        /// <summary>Strength of the neutral prior, in pseudo-observations.</summary>
        public const float PriorStrength = 2f;
        public const float Neutral = 0.5f;

        /// <summary>Observations actually seen (excludes the prior).</summary>
        public float N => a + b;

        /// <summary>Point estimate in 0..1, pulled toward 0.5 while evidence is thin.</summary>
        public float P => (a + Neutral * PriorStrength) / Mathf.Max(1e-6f, a + b + PriorStrength);

        /// <summary>Signed form in -1..1, 0 = no preference. What the bias mappings consume.</summary>
        public float Signed => Mathf.Clamp(2f * (P - Neutral), -1f, 1f);

        public void Add(float yes, float no)
        {
            if (yes > 0f) a += yes;
            if (no > 0f) b += no;
        }
    }

    /// <summary>
    /// A decayed mean with variance, for CONTINUOUS quantities (dive score, defensive shift).
    /// <see cref="sumSq"/> costs four bytes and earns them: variance is the "this player is unpredictable"
    /// signal, which is a first-class input to how much room a rival leaves.
    /// </summary>
    [System.Serializable]
    public struct MeanStat
    {
        public float sum;
        public float sumSq;
        public float weight;

        public float N => weight;
        public float Mean => weight > 1e-6f ? sum / weight : 0f;

        public float Variance
        {
            get
            {
                if (weight <= 1e-6f) return 0f;
                float m = sum / weight;
                return Mathf.Max(0f, sumSq / weight - m * m);
            }
        }

        public void Add(float value, float w = 1f)
        {
            if (w <= 0f) return;
            sum += value * w;
            sumSq += value * value * w;
            weight += w;
        }
    }

    /// <summary>
    /// A decayed rate: events over EXPOSURE. This is what makes a player who raced 40 races comparable to
    /// one who raced 10 — and, just as importantly, what stops a player who drives away at the front from
    /// being scored as clean by default. They simply had no opportunity, and the denominator knows it.
    /// </summary>
    [System.Serializable]
    public struct RateStat
    {
        public float events;
        public float exposure;

        /// <summary>
        /// Raw exposure. NOTE this is in exposure UNITS (seconds of proximity), not observations — do not
        /// feed it to <see cref="RivalMemoryMath.Confidence"/> directly, or a couple of minutes of close
        /// racing reads as hundreds of samples and saturates the gate instantly. Use
        /// <see cref="SampleEquivalent"/> for anything confidence-related.
        /// </summary>
        public float N => exposure;

        public float Rate => exposure > 1e-6f ? events / exposure : 0f;

        /// <summary>
        /// Exposure converted to an equivalent observation count, so it is commensurate with the counts
        /// <see cref="BetaStat"/> and <see cref="MeanStat"/> carry and can share one confidence model.
        /// </summary>
        public float SampleEquivalent(float exposurePerSample) =>
            exposurePerSample > 1e-6f ? exposure / exposurePerSample : 0f;

        public void Add(float eventCount, float exposureAmount)
        {
            if (eventCount > 0f) events += eventCount;
            if (exposureAmount > 0f) exposure += exposureAmount;
        }
    }

    /// <summary>
    /// Decay, confidence and the folding rules for the persistent player model. All pure statics — no
    /// Unity objects, no Time, no Random, no DateTime — so the whole memory model is unit-testable and a
    /// headless server evolves it identically.
    /// </summary>
    public static class RivalMemoryMath
    {
        // --- Half-lives, in RACES ---------------------------------------------------------------------
        // The tick is races, not wall-clock and not observations. Wall-clock is unavailable to pure logic
        // and wrong anyway (a week away doesn't change how you drive). Per-observation decay is worse: a
        // metric with many samples per race would decay far faster than a rare one, so the effective memory
        // length would silently differ per metric. One decay application per race keeps them comparable.
        public const float StyleHalfLifeRaces = 8f;      // side preference, defensive line, dive propensity
        public const float SuccessHalfLifeRaces = 10f;   // skill moves slower than style
        public const float ContactHalfLifeRaces = 6f;    // most salient, so most responsive AND most forgivable
        public const float BluffHalfLifeRaces = 12f;     // rarest signal, needs the longest window
        public const float RivalryHalfLifeRaces = 5f;    // a grudge can be cooled within a season
        /// <summary>Extra decay applied to a rival you did not race this time.</summary>
        public const float AbsentRivalDecay = 0.85f;

        // --- Accumulator caps -------------------------------------------------------------------------
        // THE SUBTLE ONE. Half-life bounds how fast old evidence fades but NOT how much accumulates, and
        // equilibrium is samplesPerRace / (1 - lambda). At 10 samples/race with an 8-race half-life that
        // settles at n ~= 120, and a model holding 120 samples barely moves however new the evidence is —
        // so the system would look correct on paper and feel unresponsive in play. Capping the accumulator
        // is what actually bounds the adaptation window:
        //     effective window ~= min(halfLife, cap / samplesPerRace) races
        // Common metrics are therefore CAP-bound and rare ones HALF-LIFE-bound, by design.
        public const float StyleCap = 40f;
        public const float SuccessCap = 40f;
        public const float ContactCap = 25f;
        public const float BluffCap = 20f;

        // --- Confidence -------------------------------------------------------------------------------
        public const float ConfidenceK = 6f;      // common metrics
        public const float RareConfidenceK = 5f;  // rare metrics (dives, bluffs)
        /// <summary>Below this many observations a metric has no vote at all, whatever it seems to say.</summary>
        public const int MinSamples = 3;

        /// <summary>Per-race decay factor for a given half-life. 1 half-life = 0.5.</summary>
        public static float DecayFactor(float halfLifeRaces, int racesElapsed)
        {
            if (racesElapsed <= 0 || halfLifeRaces <= 0f) return 1f;
            return Mathf.Pow(0.5f, racesElapsed / halfLifeRaces);
        }

        /// <summary>
        /// Decays a proportion tally and re-caps it.
        ///
        /// Multiplying numerator AND denominator by the same factor is the load-bearing property of this
        /// scheme, and it gives three guarantees worth being precise about:
        ///
        ///  1. The RAW evidence ratio <c>a/(a+b)</c> is preserved EXACTLY. Decay never reinterprets what
        ///     was seen — it only reduces how much of it is left.
        ///  2. The read estimate <see cref="BetaStat.P"/> relaxes monotonically toward neutral, because the
        ///     fixed prior does not decay alongside the evidence and so counts for proportionally more as
        ///     the evidence thins. That is the desirable behaviour, not a side effect: a rival who hasn't
        ///     raced you in thirty races should end up treating you as an unknown quantity again.
        ///  3. Confidence strictly falls.
        ///
        /// So forgetting means becoming less sure and drifting back to neutral — never becoming
        /// confidently wrong. And because n shrinks, the next real evidence moves the estimate faster, so
        /// the model self-regulates rather than whipsawing on a single outlier.
        /// </summary>
        public static BetaStat Decay(BetaStat s, float halfLifeRaces, int racesElapsed, float cap)
        {
            float f = DecayFactor(halfLifeRaces, racesElapsed);
            s.a *= f;
            s.b *= f;

            float n = s.a + s.b;
            if (n > cap && n > 1e-6f)
            {
                float scale = cap / n; // proportional rescale: again, estimate preserved exactly
                s.a *= scale;
                s.b *= scale;
            }
            return s;
        }

        public static MeanStat Decay(MeanStat s, float halfLifeRaces, int racesElapsed, float cap)
        {
            float f = DecayFactor(halfLifeRaces, racesElapsed);
            s.sum *= f;
            s.sumSq *= f;
            s.weight *= f;

            if (s.weight > cap && s.weight > 1e-6f)
            {
                float scale = cap / s.weight;
                s.sum *= scale;
                s.sumSq *= scale;
                s.weight *= scale;
            }
            return s;
        }

        public static RateStat Decay(RateStat s, float halfLifeRaces, int racesElapsed, float cap)
        {
            float f = DecayFactor(halfLifeRaces, racesElapsed);
            s.events *= f;
            s.exposure *= f;

            if (s.exposure > cap && s.exposure > 1e-6f)
            {
                float scale = cap / s.exposure;
                s.events *= scale;
                s.exposure *= scale;
            }
            return s;
        }

        /// <summary>
        /// 0..1 certainty from an observation count. Saturating, and hard-zero below
        /// <see cref="MinSamples"/> so one fluke race can never change how a rival races you.
        /// </summary>
        public static float Confidence(float n, float k = ConfidenceK)
        {
            if (n < MinSamples) return 0f;
            return n / (n + Mathf.Max(1e-6f, k));
        }

        /// <summary>
        /// Smooth gate from a confidence value: 0 below <paramref name="lo"/>, 1 above <paramref name="hi"/>.
        /// This is what makes a fresh rival play it straight without any special-casing.
        /// </summary>
        public static float Gate(float confidence, float lo, float hi)
        {
            if (hi <= lo) return confidence >= hi ? 1f : 0f;
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lo, hi, confidence));
        }

        /// <summary>
        /// Moves a bias toward its target by at most <paramref name="maxDelta"/>.
        ///
        /// Slewing the BIAS rather than the estimate is the primary defence against the adversarial
        /// oscillation this design's failure mode: player goes inside, rival covers inside, player switches
        /// outside, rival chases... Low-passing the behaviour means it physically cannot flip sign in fewer
        /// than several races even if the underlying estimate does, so the duel stays legible.
        /// </summary>
        public static float SlewBias(float previous, float target, float maxDelta) =>
            Mathf.MoveTowards(previous, target, Mathf.Max(0f, maxDelta));

        /// <summary>
        /// Applies a deadband: values inside <paramref name="threshold"/> read as no preference at all.
        /// A 55/45 player is not "a player who goes inside", and acting as though they were is how a model
        /// ends up chasing noise.
        /// </summary>
        public static float Deadband(float signed, float threshold)
        {
            if (Mathf.Abs(signed) <= threshold) return 0f;
            float sign = Mathf.Sign(signed);
            return sign * Mathf.InverseLerp(threshold, 1f, Mathf.Abs(signed));
        }

        /// <summary>Quantises to 1e-4 so JsonUtility save/load is idempotent and float drift can't creep.</summary>
        public static float Quantize(float v) => Mathf.Round(v * 10000f) / 10000f;
    }
}
