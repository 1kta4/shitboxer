using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Meta;
using Shitboxer.Race;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the rival identity spine: the career roster, the per-run field draw, and the stable per-rival
    /// seed that makes a rival's on-track character follow their NAME rather than their grid slot.
    ///
    /// Two properties here are load-bearing for everything the rivalry system will later build on top.
    ///
    /// (1) The draw must be a pure function of (run seed, roster size, slot count). <c>RunDirector</c> calls
    /// it at every scene bind — once per race, again on a resume — and nothing about the field is persisted,
    /// so reproducibility from the run seed alone is what keeps the same cast of drivers together for a whole
    /// run across three different track scenes.
    ///
    /// (2) <see cref="RivalField.IdentitySeed"/> must be stable forever and across processes. It is
    /// deliberately NOT <c>string.GetHashCode()</c>, which modern .NET randomises per process — using that
    /// would silently hand a rival a different personality on every launch, and diverge between a client and
    /// a headless server in a way only a player would notice.
    /// </summary>
    public class RivalIdentityTests : TestBase
    {
        // --- RivalField.Draw --------------------------------------------------------------------------

        [Test]
        public void Draw_IsDeterministicForTheSameArguments()
        {
            int[] a = RivalField.Draw(12345, 24, 7);
            int[] b = RivalField.Draw(12345, 24, 7);
            Assert.That(b, Is.EqualTo(a), "the same run seed must reproduce the same field, every race");
        }

        [Test]
        public void Draw_DiffersBetweenRunSeeds()
        {
            int[] a = RivalField.Draw(1, 24, 7);
            int[] b = RivalField.Draw(2, 24, 7);
            Assert.That(b, Is.Not.EqualTo(a), "consecutive runs should meet a visibly different cast");
        }

        [Test]
        public void Draw_HasNoDuplicates_WhenRosterExceedsSlots()
        {
            int[] field = RivalField.Draw(99, 24, 7);
            Assert.That(field.Length, Is.EqualTo(7));
            Assert.That(new HashSet<int>(field).Count, Is.EqualTo(7),
                "one rival must not occupy two cars in the same race while another sits out");
        }

        [Test]
        public void Draw_StaysInRangeOfTheRoster()
        {
            int[] field = RivalField.Draw(7, 24, 7);
            foreach (int index in field)
                Assert.That(index, Is.InRange(0, 23));
        }

        [Test]
        public void Draw_WrapsWhenRosterIsSmallerThanTheGrid()
        {
            // Documented, harmless degenerate case: both cars are the same driver and share one memory.
            int[] field = RivalField.Draw(5, 3, 7);
            Assert.That(field.Length, Is.EqualTo(7));
            foreach (int index in field)
                Assert.That(index, Is.InRange(0, 2));
        }

        [Test]
        public void Draw_ReturnsEmptyForDegenerateSizes()
        {
            Assert.That(RivalField.Draw(1, 24, 0), Is.Empty);
            Assert.That(RivalField.Draw(1, 0, 7), Is.Empty);
            Assert.That(RivalField.Draw(1, -3, -3), Is.Empty);
        }

        // --- RivalField.IdentitySeed ------------------------------------------------------------------

        [Test]
        public void IdentitySeed_IsStableAcrossCalls()
        {
            Assert.That(RivalField.IdentitySeed("vera_kestrel"),
                Is.EqualTo(RivalField.IdentitySeed("vera_kestrel")));
        }

        [Test]
        public void IdentitySeed_PinsKnownValues()
        {
            // Pinned FNV-1a outputs. If these ever change, every rival in every existing save silently
            // becomes a different driver — so a failure here is a save-compatibility break, not a test nit.
            Assert.That(RivalField.IdentitySeed("vera_kestrel"), Is.EqualTo(unchecked((int)0x8AF671BCu)));
            Assert.That(RivalField.IdentitySeed("dex_karro"), Is.EqualTo(unchecked((int)0x197B249Cu)));
        }

        [Test]
        public void IdentitySeed_DiffersBetweenRivals()
        {
            var seeds = new HashSet<int>();
            foreach (RivalDef def in RivalRoster.Default)
                Assert.That(seeds.Add(RivalField.IdentitySeed(def.id)), Is.True,
                    $"seed collision on '{def.id}' — two rivals would share a character");
        }

        [Test]
        public void IdentitySeed_IsNeverZero()
        {
            // 0 is BotDriver's reserved "no identity pushed" sentinel: a rival hashing to it would silently
            // fall back to the legacy sibling-index seed and lose its stable character.
            foreach (RivalDef def in RivalRoster.Default)
                Assert.That(RivalField.IdentitySeed(def.id), Is.Not.Zero);

            Assert.That(RivalField.IdentitySeed(null), Is.Zero, "a missing id is genuinely unidentified");
            Assert.That(RivalField.IdentitySeed(""), Is.Zero);
        }

        [Test]
        public void IdentitySeed_ProducesAStableCharacterPerRival()
        {
            // The whole point of hashing the id: the same rival resolves to the same archetype and skill
            // tier no matter which grid slot they draw, on which track, in which run.
            foreach (RivalDef def in RivalRoster.Default)
            {
                int seed = RivalField.IdentitySeed(def.id);
                BotDriver.ResolveRivalConfig(true, seed, BotPersonalityKind.Neutral,
                    out BotPersonalityKind kindA, out _, out _, def.drivingArchetype);
                BotDriver.ResolveRivalConfig(true, seed, BotPersonalityKind.Neutral,
                    out BotPersonalityKind kindB, out _, out _, def.drivingArchetype);
                Assert.That(kindB, Is.EqualTo(kindA));
                Assert.That(BotDriver.RivalBaseSkill01(seed), Is.EqualTo(BotDriver.RivalBaseSkill01(seed)));
            }
        }

        [Test]
        public void AuthoredArchetype_WinsOverTheSeededDraw()
        {
            // The roster is deliberately built as a 6x4 personality-by-archetype cross. Deriving the
            // archetype from the id hash instead would discard that and fan unevenly (the shipped roster
            // lands 8/7/6/3 that way, leaving only three Cruisers), so the authored value must win.
            foreach (RivalDef def in RivalRoster.Default)
            {
                BotDriver.ResolveRivalConfig(true, RivalField.IdentitySeed(def.id), BotPersonalityKind.Neutral,
                    out BotPersonalityKind kind, out BotPersonality personality, out _, def.drivingArchetype);
                Assert.That(kind, Is.EqualTo(def.drivingArchetype), $"'{def.id}' did not race its authored archetype");
                Assert.That(personality, Is.EqualTo(BotPersonality.FromKind(def.drivingArchetype)));
            }
        }

        [Test]
        public void AuthoredArchetype_OmittedKeepsTheSeededDrawBitForBit()
        {
            // Every caller that predates the roster passes no authored kind. Those must be untouched.
            for (int seed = -50; seed <= 50; seed++)
            {
                BotDriver.ResolveRivalConfig(true, seed, BotPersonalityKind.Neutral,
                    out BotPersonalityKind kind, out BotPersonality personality, out BotDifficulty difficulty);
                Assert.That(kind, Is.EqualTo(BotDriver.RivalKind(seed)));
                Assert.That(personality, Is.EqualTo(BotPersonality.FromKind(BotDriver.RivalKind(seed))));
                Assert.That(difficulty.SkillBias01,
                    Is.EqualTo(BotDifficulty.FromTier(BotDriver.RivalBaseSkill01(seed)).SkillBias01));
            }
        }

        [Test]
        public void AuthoredArchetype_IsIgnoredWhenVarietyIsOff()
        {
            // Variety OFF is the revert path and must stay the serialized fallback at nominal difficulty,
            // regardless of what the roster would have asked for.
            BotDriver.ResolveRivalConfig(false, 12345, BotPersonalityKind.Neutral,
                out BotPersonalityKind kind, out BotPersonality personality, out BotDifficulty difficulty,
                BotPersonalityKind.Diver);
            Assert.That(kind, Is.EqualTo(BotPersonalityKind.Neutral));
            Assert.That(personality, Is.EqualTo(BotPersonality.Neutral));
            Assert.That(difficulty.SkillBias01, Is.EqualTo(BotDifficulty.Nominal.SkillBias01));
        }

        // --- RivalField.KeyForSlot --------------------------------------------------------------------

        [Test]
        public void KeyForSlot_ReservesZeroForThePlayer()
        {
            Assert.That(RivalField.KeyForSlot(0), Is.EqualTo(1));
            Assert.That(RivalField.KeyForSlot(6), Is.EqualTo(7));
        }

        // --- RivalRoster.Default ----------------------------------------------------------------------

        [Test]
        public void DefaultRoster_HasUniqueNonEmptyIds()
        {
            var ids = new HashSet<string>();
            foreach (RivalDef def in RivalRoster.Default)
            {
                Assert.That(def.IsValid, Is.True, $"'{def.displayName}' has no primary key");
                Assert.That(ids.Add(def.id), Is.True,
                    $"duplicate rival id '{def.id}' — two drivers would share one memory");
            }
        }

        [Test]
        public void DefaultRoster_SpansEveryPersonalityAndArchetypeCombination()
        {
            // 6 learning personalities x 4 driving archetypes, one of each. This is what guarantees a drawn
            // field mixes both how rivals drive AND how they learn, rather than (say) four Blockers who all
            // form opinions at the same rate.
            var seen = new HashSet<(RivalPersonality, BotPersonalityKind)>();
            foreach (RivalDef def in RivalRoster.Default)
                Assert.That(seen.Add((def.personality, def.drivingArchetype)), Is.True,
                    $"duplicate combination on '{def.id}'");

            Assert.That(RivalRoster.Default.Count, Is.EqualTo(24));
            Assert.That(seen.Count, Is.EqualTo(24));
        }

        [Test]
        public void DefaultRoster_HasHudSizedShortNames()
        {
            foreach (RivalDef def in RivalRoster.Default)
                Assert.That(def.shortName, Has.Length.EqualTo(3), $"'{def.id}' won't fit the leaderboard column");
        }

        [Test]
        public void EmptyRoster_FallsBackToTheBuiltInDefault()
        {
            // A missing or un-authored asset must still field a full cast rather than silently no-op.
            var roster = UnityEngine.ScriptableObject.CreateInstance<RivalRoster>();
            try
            {
                Assert.That(roster.Rivals.Count, Is.EqualTo(RivalRoster.Default.Count));
                Assert.That(roster.TryGet("vera_kestrel", out RivalDef def), Is.True);
                Assert.That(def.displayName, Is.EqualTo("Vera Kestrel"));
                Assert.That(roster.TryGet("nobody_here", out _), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(roster);
            }
        }
    }
}
