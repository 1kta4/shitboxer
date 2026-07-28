using NUnit.Framework;
using Shitboxer.Fx;

namespace Shitboxer.Tests
{
    /// <summary>
    /// The procedural SFX generators are pure sample math, so the harness can pin the properties that
    /// matter to the ear: nothing clips, everything is deterministic (a re-bake can't change the
    /// game's sound), the engine loop's seam is continuous, and a harder hit genuinely carries more
    /// energy than a tap. The MonoBehaviour half (RaceFxController/FxBootstrap) is play-mode glue and
    /// is exercised by playing, like the rest of the juice.
    /// </summary>
    public class SfxSynthTests : TestBase
    {
        private const int Rate = 44100;

        private static void AssertBounded(float[] s)
        {
            Assert.Greater(s.Length, 0);
            for (int i = 0; i < s.Length; i++)
            {
                Assert.IsFalse(float.IsNaN(s[i]) || float.IsInfinity(s[i]), $"non-finite sample at {i}");
                Assert.LessOrEqual(System.Math.Abs(s[i]), 1f, $"clipped sample at {i}");
            }
        }

        private static float Rms(float[] s)
        {
            double sum = 0;
            for (int i = 0; i < s.Length; i++) sum += s[i] * s[i];
            return (float)System.Math.Sqrt(sum / s.Length);
        }

        [Test]
        public void EveryGenerator_StaysInRange_AndIsAudible()
        {
            float[][] all =
            {
                SfxSynth.EngineLoop(Rate),
                SfxSynth.Impact(Rate, 0.2f), SfxSynth.Impact(Rate, 1f),
                SfxSynth.Beep(Rate, 620f, 0.14f),
                SfxSynth.Whoosh(Rate),
                SfxSynth.Boom(Rate),
                SfxSynth.Sting(Rate, up: true), SfxSynth.Sting(Rate, up: false),
                SfxSynth.Alarm(Rate),
            };
            foreach (float[] s in all)
            {
                AssertBounded(s);
                Assert.Greater(Rms(s), 0.01f, "a generator produced near-silence");
            }
        }

        [Test]
        public void Generators_AreDeterministic()
        {
            // Same call, same buffer — a re-bake at scene load can never change how the game sounds.
            float[] a = SfxSynth.Impact(Rate, 0.7f, seed: 5);
            float[] b = SfxSynth.Impact(Rate, 0.7f, seed: 5);
            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++) Assert.AreEqual(a[i], b[i]);
        }

        [Test]
        public void EngineLoop_SeamIsContinuous()
        {
            // Every partial is snapped to whole cycles per buffer, so the loop point must not step:
            // the last sample flows back into the first within the noise floor.
            float[] s = SfxSynth.EngineLoop(Rate);
            Assert.Less(System.Math.Abs(s[s.Length - 1] - s[0]), 0.2f,
                "audible discontinuity at the engine loop seam");
        }

        [Test]
        public void Impact_SeverityScalesEnergyAndLength()
        {
            float[] tap = SfxSynth.Impact(Rate, 0.1f);
            float[] slam = SfxSynth.Impact(Rate, 1f);
            Assert.Greater(slam.Length, tap.Length, "a slam should ring longer than a tap");
            Assert.Greater(Rms(slam), Rms(tap), "a slam should carry more energy than a tap");
        }

        [Test]
        public void Beep_HasClicklessEdges()
        {
            float[] s = SfxSynth.Beep(Rate, 620f, 0.14f);
            Assert.Less(System.Math.Abs(s[0]), 0.02f);
            Assert.Less(System.Math.Abs(s[s.Length - 1]), 0.02f);
        }
    }
}
