using NUnit.Framework;
using Shitboxer.Meta;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the pure display-formatting helpers in <see cref="PartDisplay"/> — edition tags, lap-record
    /// times and run-history lines — lifted out of the throwaway IMGUI GarageScreen so they (and these
    /// tests) survive the UI Toolkit rewrite. Cosmetic-only: they must render editions/records clearly
    /// while leaving <see cref="PartEdition.None"/> showing nothing extra, and they never read or write any
    /// gameplay/economy state.
    /// </summary>
    public class GarageDisplayTests : TestBase
    {
        // --- EditionTag ------------------------------------------------------------------------

        [Test]
        public void EditionTag_None_IsEmpty()
        {
            // The load-bearing invariant: an un-editioned part must show nothing extra, so today's look
            // is unchanged for every existing PartDef (all default to None).
            Assert.That(PartDisplay.EditionTag(PartEdition.None), Is.EqualTo(""));
        }

        [Test]
        public void EditionTag_NonNone_ShowsUppercaseNameInBrackets()
        {
            string foil = PartDisplay.EditionTag(PartEdition.Foil);
            Assert.That(foil, Does.StartWith("[FOIL "));
            Assert.That(foil, Does.EndWith("]"));
            Assert.That(foil, Does.Contain("x"));

            Assert.That(PartDisplay.EditionTag(PartEdition.Holo), Does.StartWith("[HOLO "));
            Assert.That(PartDisplay.EditionTag(PartEdition.Polychrome), Does.StartWith("[POLYCHROME "));
        }

        [Test]
        public void EditionTag_MagnitudeMatchesEditionInfo()
        {
            // The displayed magnitude is the same factor SpecModApplier scales the effect by, so the tag
            // never advertises a different power than the part actually has. Self-derived so it is
            // independent of the machine's decimal-separator culture.
            foreach (PartEdition edition in new[] { PartEdition.Foil, PartEdition.Holo, PartEdition.Polychrome })
            {
                string expectedMagnitude = $"x{PartEditionInfo.StatMult(edition):0.##}";
                Assert.That(PartDisplay.EditionTag(edition), Does.Contain(expectedMagnitude));
            }
        }

        // --- FormatLapRecord ---------------------------------------------------------------------

        [Test]
        public void FormatLapRecord_NonPositive_IsDash()
        {
            Assert.That(PartDisplay.FormatLapRecord(0f), Is.EqualTo("--"));
            Assert.That(PartDisplay.FormatLapRecord(MetaProgress.NoLapRecord), Is.EqualTo("--"));
            Assert.That(PartDisplay.FormatLapRecord(-3f), Is.EqualTo("--"));
        }

        [Test]
        public void FormatLapRecord_SubMinute_ShowsZeroMinutes()
        {
            // 42.5s -> under a minute, so the minute field is 0. Structural check keeps it culture-safe.
            string s = PartDisplay.FormatLapRecord(42.5f);
            Assert.That(s, Does.StartWith("0:"));
            Assert.That(s, Does.Contain("42"));
            Assert.That(s, Is.Not.EqualTo("--"));
        }

        [Test]
        public void FormatLapRecord_OverAMinute_SplitsMinutesAndSeconds()
        {
            // 83.25s = 1 min 23.25 s — the minute field must roll over to 1 and seconds back under 60.
            string s = PartDisplay.FormatLapRecord(83.25f);
            Assert.That(s, Does.StartWith("1:"));
            Assert.That(s, Does.Contain("23"));
        }

        // --- RunHistoryLine --------------------------------------------------------------------

        [Test]
        public void RunHistoryLine_ShowsLicenseCircuitsAndMoney()
        {
            var entry = new RunHistoryEntry { circuitsCleared = 2, finalMoney = 37, stakeLevel = 0 };
            // Stake 0 reads as the human "License 1"; all fields are integers, so this is culture-safe.
            Assert.That(PartDisplay.RunHistoryLine(entry), Is.EqualTo("License 1 - 2 circuits - $37"));
        }

        [Test]
        public void RunHistoryLine_SingularCircuit_UsesSingularNoun()
        {
            var entry = new RunHistoryEntry { circuitsCleared = 1, finalMoney = 5, stakeLevel = 2 };
            Assert.That(PartDisplay.RunHistoryLine(entry), Is.EqualTo("License 3 - 1 circuit - $5"));
        }
    }
}
