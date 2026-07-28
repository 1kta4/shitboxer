using UnityEngine;

namespace Shitboxer.Fx
{
    /// <summary>
    /// Procedural placeholder SFX — pure sample math, no assets, no licences, no load step. Every
    /// generator returns mono float samples in [-1, 1] and is DETERMINISTIC (seeded xorshift noise,
    /// no UnityEngine.Random / System.Random), so the same call always yields the same clip and the
    /// generators are unit-testable in the standalone harness. RaceFxController bakes these into
    /// AudioClips once at scene start. Placeholder by design (doc 06: placeholders are for finding
    /// the fun) — a real audio pass replaces the GENERATORS, and nothing else has to change.
    /// </summary>
    public static class SfxSynth
    {
        // ---------------------------------------------------------------- noise
        /// <summary>Deterministic xorshift32 noise in [-1, 1). Never zero-seeded (0 locks xorshift).</summary>
        private struct Noise
        {
            private uint _state;
            public Noise(int seed) { _state = seed == 0 ? 2463534242u : (uint)seed; }

            public float Next()
            {
                _state ^= _state << 13;
                _state ^= _state >> 17;
                _state ^= _state << 5;
                return (_state & 0xFFFFFF) / 8388608f - 1f; // 24-bit mantissa -> [-1, 1)
            }
        }

        private const float Tau = Mathf.PI * 2f;

        // ---------------------------------------------------------------- engine
        /// <summary>
        /// A seamless engine loop: a small harmonic stack (fundamental + 2x + 3x + a gravelly
        /// half-order) whose frequencies are all snapped to WHOLE cycles per buffer, so the loop
        /// point is inaudible by construction. Pitch it at runtime with AudioSource.pitch = rpm map.
        /// </summary>
        public static float[] EngineLoop(int sampleRate, float baseHz = 55f, float seconds = 1f)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(sampleRate * seconds));
            var samples = new float[n];

            // Snap each partial to an integer cycle count over the buffer -> phase 0 at both ends.
            float f0 = Mathf.Max(1f, Mathf.Round(baseHz * seconds)) / seconds;
            float[] partials = { f0 * 0.5f, f0, f0 * 2f, f0 * 3f };
            float[] gains = { 0.22f, 0.5f, 0.2f, 0.08f };

            var grit = new Noise(101);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / sampleRate;
                float s = 0f;
                for (int p = 0; p < partials.Length; p++)
                    s += gains[p] * Mathf.Sin(Tau * partials[p] * t);
                // A whisper of noise so the tone reads combustion rather than organ. Same amount at
                // both ends of the buffer (it's per-sample), so the seam stays clean.
                s += 0.04f * grit.Next();
                samples[i] = s * 0.85f; // near full-scale: the engine is the bed every other cue sits on
            }
            return samples;
        }

        // ---------------------------------------------------------------- impacts
        /// <summary>
        /// Contact crunch: a filtered noise burst over a low sine thump. Severity (0..1) deepens the
        /// thump, lengthens the tail and raises the level, so taps tick and slams crunch.
        /// </summary>
        public static float[] Impact(int sampleRate, float severity01, int seed = 7)
        {
            float severity = Mathf.Clamp01(severity01);
            float seconds = 0.09f + 0.26f * severity;
            int n = Mathf.Max(1, Mathf.RoundToInt(sampleRate * seconds));
            var samples = new float[n];
            var noise = new Noise(seed);

            float thumpHz = Mathf.Lerp(180f, 55f, severity);
            float level = 0.35f + 0.6f * severity;
            float lp = 0f;
            // One-pole low-pass on the noise: heavier hits get a darker crunch.
            float alpha = Mathf.Lerp(0.55f, 0.18f, severity);

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / sampleRate;
                float env = Mathf.Exp(-t / (0.03f + 0.08f * severity));
                lp += alpha * (noise.Next() - lp);
                float thump = Mathf.Sin(Tau * thumpHz * t) * Mathf.Exp(-t / 0.05f);
                samples[i] = Mathf.Clamp((lp * 0.8f + thump * 0.9f) * env * level, -1f, 1f);
            }
            return samples;
        }

        // ---------------------------------------------------------------- one-shots
        /// <summary>A clean sine beep with fast fades — the countdown voice.</summary>
        public static float[] Beep(int sampleRate, float hz, float seconds, float level = 0.35f)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(sampleRate * seconds));
            var samples = new float[n];
            int fade = Mathf.Max(1, sampleRate / 200); // 5 ms edges, no clicks
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / sampleRate;
                float env = Mathf.Min(1f, Mathf.Min(i / (float)fade, (n - 1 - i) / (float)fade));
                samples[i] = level * env * Mathf.Sin(Tau * hz * t);
            }
            return samples;
        }

        /// <summary>Boost deploy: a rising band of noise — pure acceleration, no tone.</summary>
        public static float[] Whoosh(int sampleRate, float seconds = 0.5f, int seed = 23)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(sampleRate * seconds));
            var samples = new float[n];
            var noise = new Noise(seed);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)n;
                float alpha = Mathf.Lerp(0.06f, 0.6f, u * u);   // filter opens as it builds
                lp += alpha * (noise.Next() - lp);
                float env = Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.7f)); // swell in, clip out
                samples[i] = lp * env * 0.5f;
            }
            return samples;
        }

        /// <summary>Retirement: a falling low tone over rumble — the car is over.</summary>
        public static float[] Boom(int sampleRate, float seconds = 0.9f, int seed = 41)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(sampleRate * seconds));
            var samples = new float[n];
            var noise = new Noise(seed);
            float lp = 0f;
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / sampleRate;
                float u = i / (float)n;
                float hz = Mathf.Lerp(110f, 38f, u); // integrate a falling pitch, don't jump it
                phase += Tau * hz / sampleRate;
                lp += 0.12f * (noise.Next() - lp);
                float env = Mathf.Exp(-t / (seconds * 0.38f));
                samples[i] = Mathf.Clamp((Mathf.Sin(phase) * 0.8f + lp * 0.5f) * env * 0.85f, -1f, 1f);
            }
            return samples;
        }

        /// <summary>
        /// Race verdict: a quick two-note figure — up (finished) resolves, down (eliminated) doesn't.
        /// </summary>
        public static float[] Sting(int sampleRate, bool up)
        {
            float a = up ? 440f : 392f;
            float b = up ? 660f : 261f;
            int half = Mathf.RoundToInt(sampleRate * 0.12f);
            var samples = new float[half * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = (float)i / sampleRate;
                float hz = i < half ? a : b;
                float local = i < half ? t : t - half / (float)sampleRate;
                float env = Mathf.Exp(-local / 0.09f);
                samples[i] = 0.3f * env * Mathf.Sin(Tau * hz * t);
            }
            return samples;
        }

        /// <summary>Crippled-at-half alarm (decision 15's threshold): two urgent pulses, then silence —
        /// a warning, not a siren that drones over the rest of the race.</summary>
        public static float[] Alarm(int sampleRate)
        {
            int pulse = Mathf.RoundToInt(sampleRate * 0.09f);
            int gap = Mathf.RoundToInt(sampleRate * 0.06f);
            var samples = new float[pulse * 2 + gap];
            for (int p = 0; p < 2; p++)
            {
                int start = p * (pulse + gap);
                for (int i = 0; i < pulse; i++)
                {
                    float t = (float)i / sampleRate;
                    float env = Mathf.Min(1f, Mathf.Min(i / (sampleRate * 0.005f), (pulse - 1 - i) / (sampleRate * 0.005f)));
                    samples[start + i] = 0.32f * env * Mathf.Sin(Tau * 870f * t);
                }
            }
            return samples;
        }
    }
}
