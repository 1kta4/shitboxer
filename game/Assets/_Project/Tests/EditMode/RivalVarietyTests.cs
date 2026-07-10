using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Race;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers <see cref="BotDriver"/>'s rival-variety activation layer: the pure, deterministic
    /// seed -> (archetype, difficulty) resolver that turns on mild, bounded rival distinctiveness behind a
    /// single master toggle. The contract mirrors the project's headline constraint two ways:
    ///   - OFF (or the serialized-default Neutral fallback) is a TRUE no-op — the resolver yields the fallback
    ///     personality at nominal difficulty regardless of seed, i.e. byte-for-byte today's identical bots.
    ///   - ON fans the field EVENLY across the four archetypes and a MILD skill band, deterministically per
    ///     seed, and every produced BotPersonality/BotDifficulty stays inside the subtle clamped bands (a
    ///     texture difference, never a difficulty spike).
    /// Only the pure resolver is unit-tested here; wiring it into the MonoBehaviour's FixedUpdate (seed built
    /// from the serialized base seed + the bot's sibling index) is scene-level and covered by that indirection.
    /// </summary>
    public class RivalVarietyTests : TestBase
    {
        // Documented output clamps (mirror BotDifficulty's private consts, as BotDifficultyTests does).
        private const float SpeedMin = 0.90f, SpeedMax = 1.10f;
        private const float ThrottleMin = 0.85f, ThrottleMax = 1.12f;
        private const float SteerMin = 0.85f, SteerMax = 1.20f;

        // Mirrors BotDriver.SkillBandHalf: the variety layer only ever draws base skill within +/- this of
        // nominal (0.5). Kept far tighter than the full clamps above so the activation is provably subtle.
        private const float SkillBandHalf = 0.08f;

        // A "mild" envelope comfortably inside the full clamps: proves the skill spread is a nudge, not a spike.
        private const float MildMin = 0.97f, MildMax = 1.03f;

        private static readonly float[] Gaps = { -1000f, -45f, -1f, 0f, 1f, 45f, 1000f };

        private static void AssertIdentity(BotModifiers m, string label)
        {
            Assert.That(m.TargetSpeedScale, Is.EqualTo(1f), label + " target-speed scale == 1");
            Assert.That(m.ThrottleScale, Is.EqualTo(1f), label + " throttle scale == 1");
            Assert.That(m.SteerSharpness, Is.EqualTo(1f), label + " steer sharpness == 1");
        }

        private static void AssertNeutralPersonality(BotPersonality p, string label)
        {
            Assert.That(p.BlockBiasClamped, Is.EqualTo(0f), label + " no block bias");
            Assert.That(p.DiveAggressionClamped, Is.EqualTo(0f), label + " no dive bias");
            Assert.That(p.FollowGapScale, Is.EqualTo(1f), label + " identity follow gap");
        }

        private static void AssertPersonalityWithinClamps(BotPersonality p, string label)
        {
            Assert.That(p.BlockBiasClamped,
                Is.InRange(-BotPersonality.MaxBlockBias, BotPersonality.MaxBlockBias), label + " block bias");
            Assert.That(p.DiveAggressionClamped,
                Is.InRange(-BotPersonality.MaxDiveAggression, BotPersonality.MaxDiveAggression), label + " dive bias");
            Assert.That(p.FollowGapScale,
                Is.InRange(1f - BotPersonality.MaxFollowGapBias, 1f + BotPersonality.MaxFollowGapBias), label + " follow gap");
        }

        private static void AssertDifficultyWithinClamps(BotDifficulty d, string label)
        {
            foreach (float g in Gaps)
            {
                BotModifiers m = d.Evaluate(g);
                Assert.That(m.TargetSpeedScale, Is.InRange(SpeedMin, SpeedMax), $"{label} target-speed @ gap {g}");
                Assert.That(m.ThrottleScale, Is.InRange(ThrottleMin, ThrottleMax), $"{label} throttle @ gap {g}");
                Assert.That(m.SteerSharpness, Is.InRange(SteerMin, SteerMax), $"{label} steer @ gap {g}");
            }
        }

        // ---- OFF: the revert path is a true no-op (identity), independent of the seed ----------------------

        [Test]
        public void Off_WithNeutralFallback_IsIdentity_ForAnySeed()
        {
            // The task's headline OFF contract: variety disabled -> Neutral personality + nominal (identity)
            // difficulty, exactly today's identical bots. Must hold for every seed (the seed is ignored).
            foreach (int seed in new[] { -1000, -1, 0, 1, 7, 12345, int.MinValue, int.MaxValue })
            {
                BotDriver.ResolveRivalConfig(false, seed, BotPersonalityKind.Neutral,
                    out BotPersonalityKind kind, out BotPersonality p, out BotDifficulty d);

                Assert.That(kind, Is.EqualTo(BotPersonalityKind.Neutral), $"seed {seed} resolves to Neutral");
                AssertNeutralPersonality(p, $"seed {seed}");
                foreach (float g in Gaps)
                    AssertIdentity(d.Evaluate(g), $"seed {seed} @ gap {g}");
            }
        }

        [Test]
        public void Off_PreservesSerializedFallback_ByteForByte()
        {
            // OFF is defined as "byte-for-byte what BotDriver did before": it must faithfully replay the
            // serialized personality field (whatever a scene set it to) at nominal difficulty, not force Neutral.
            foreach (BotPersonalityKind fallback in new[]
                { BotPersonalityKind.Neutral, BotPersonalityKind.Blocker, BotPersonalityKind.Diver, BotPersonalityKind.Cruiser })
            {
                BotDriver.ResolveRivalConfig(false, seed: 999, fallback,
                    out BotPersonalityKind kind, out BotPersonality p, out BotDifficulty d);

                Assert.That(kind, Is.EqualTo(fallback), $"fallback {fallback} preserved");
                BotPersonality expected = BotPersonality.FromKind(fallback);
                Assert.That(p.BlockBiasClamped, Is.EqualTo(expected.BlockBiasClamped), $"{fallback} block bias");
                Assert.That(p.DiveAggressionClamped, Is.EqualTo(expected.DiveAggressionClamped), $"{fallback} dive bias");
                Assert.That(p.FollowGapScale, Is.EqualTo(expected.FollowGapScale), $"{fallback} follow gap");
                foreach (float g in Gaps)
                    AssertIdentity(d.Evaluate(g), $"{fallback} difficulty identity @ gap {g}"); // still nominal
            }
        }

        // ---- ON: deterministic per seed ---------------------------------------------------------------------

        [Test]
        public void On_IsDeterministic_ForAGivenSeed()
        {
            // A given seed must map to the same archetype + difficulty every call (repeatable across runs and
            // identical on a headless server — the resolver draws only from the seed, no Random / no Time).
            for (int seed = 0; seed < 50; seed++)
            {
                BotDriver.ResolveRivalConfig(true, seed, BotPersonalityKind.Neutral,
                    out BotPersonalityKind k1, out BotPersonality p1, out BotDifficulty d1);
                BotDriver.ResolveRivalConfig(true, seed, BotPersonalityKind.Neutral,
                    out BotPersonalityKind k2, out BotPersonality p2, out BotDifficulty d2);

                Assert.That(k2, Is.EqualTo(k1), $"seed {seed} kind stable");
                Assert.That(p2.BlockBias, Is.EqualTo(p1.BlockBias), $"seed {seed} personality stable");
                Assert.That(d2.SkillBias01, Is.EqualTo(d1.SkillBias01), $"seed {seed} skill bias stable");
                Assert.That(d2.RubberBandStrength, Is.EqualTo(d1.RubberBandStrength), $"seed {seed} rubber-band stable");
                Assert.That(d2.StakeLevel, Is.EqualTo(d1.StakeLevel), $"seed {seed} stake stable");
            }
        }

        [Test]
        public void On_IgnoresFallbackKind()
        {
            // When variety is on the seeded assignment wins; the serialized fallback is only consulted when off.
            BotDriver.ResolveRivalConfig(true, seed: 2, BotPersonalityKind.Neutral, out BotPersonalityKind a, out _, out _);
            BotDriver.ResolveRivalConfig(true, seed: 2, BotPersonalityKind.Cruiser, out BotPersonalityKind b, out _, out _);
            Assert.That(b, Is.EqualTo(a), "fallback ignored while variety is on");
        }

        // ---- ON: even spread across the four archetypes -----------------------------------------------------

        [Test]
        public void On_ConsecutiveSeeds_CycleAllFourArchetypes()
        {
            // Consecutive sibling indices index the archetype table by the seed's low bits, so a field of >=4
            // bots always sees all four characters (Neutral included) in a stable order.
            Assert.That(BotDriver.RivalKind(0), Is.EqualTo(BotPersonalityKind.Neutral), "seed 0");
            Assert.That(BotDriver.RivalKind(1), Is.EqualTo(BotPersonalityKind.Blocker), "seed 1");
            Assert.That(BotDriver.RivalKind(2), Is.EqualTo(BotPersonalityKind.Diver), "seed 2");
            Assert.That(BotDriver.RivalKind(3), Is.EqualTo(BotPersonalityKind.Cruiser), "seed 3");
            Assert.That(BotDriver.RivalKind(4), Is.EqualTo(BotPersonalityKind.Neutral), "seed 4 wraps");

            var kinds = new HashSet<BotPersonalityKind>();
            for (int seed = 0; seed < 32; seed++) kinds.Add(BotDriver.RivalKind(seed));
            Assert.That(kinds.Count, Is.EqualTo(4), "all four archetypes appear across the field");
        }

        [Test]
        public void On_SkillSpread_IsPresentAndMild()
        {
            // The skill draw must actually vary the field (>= 2 distinct tiers over a range) while every tier
            // stays inside the narrow band around nominal — variety, not a difficulty spike.
            var tiers = new HashSet<float>();
            for (int seed = 0; seed < 64; seed++)
            {
                float s = BotDriver.RivalBaseSkill01(seed);
                Assert.That(s, Is.InRange(0.5f - SkillBandHalf - 1e-6f, 0.5f + SkillBandHalf + 1e-6f), $"seed {seed} base skill within band");
                tiers.Add(s);
            }
            Assert.That(tiers.Count, Is.GreaterThanOrEqualTo(2), "the field carries more than one skill tier");
        }

        // ---- ON: every produced config stays bounded and subtle ---------------------------------------------

        [Test]
        public void On_EveryConfig_StaysWithinClamps()
        {
            // No seed can produce a personality bias or difficulty modifier outside its subtle clamped band.
            for (int seed = -64; seed < 192; seed++)
            {
                BotDriver.ResolveRivalConfig(true, seed, BotPersonalityKind.Neutral,
                    out _, out BotPersonality p, out BotDifficulty d);
                AssertPersonalityWithinClamps(p, $"seed {seed}");
                AssertDifficultyWithinClamps(d, $"seed {seed}");
            }
        }

        [Test]
        public void On_DifficultyIsMild_NeverASpike()
        {
            // Tighter than the full clamps: with no rubber-band and no stake the only source of movement is the
            // narrow skill band, so at every gap the modifiers hug identity — a hair, not a spike.
            for (int seed = 0; seed < 128; seed++)
            {
                BotDriver.ResolveRivalConfig(true, seed, BotPersonalityKind.Neutral, out _, out _, out BotDifficulty d);

                Assert.That(d.RubberBandStrength, Is.EqualTo(0f), $"seed {seed} rubber-band stays off");
                Assert.That(d.StakeLevel, Is.EqualTo(0), $"seed {seed} no stake lift");
                Assert.That(d.Competence01, Is.InRange(0.5f - SkillBandHalf - 1e-6f, 0.5f + SkillBandHalf + 1e-6f),
                    $"seed {seed} competence near nominal");

                foreach (float g in Gaps)
                {
                    BotModifiers m = d.Evaluate(g);
                    Assert.That(m.TargetSpeedScale, Is.InRange(MildMin, MildMax), $"seed {seed} target-speed mild @ gap {g}");
                    Assert.That(m.ThrottleScale, Is.InRange(MildMin, MildMax), $"seed {seed} throttle mild @ gap {g}");
                    Assert.That(m.SteerSharpness, Is.InRange(MildMin, MildMax), $"seed {seed} steer mild @ gap {g}");
                }
            }
        }
    }
}
