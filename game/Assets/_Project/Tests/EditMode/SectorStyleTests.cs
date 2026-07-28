using NUnit.Framework;
using Shitboxer.Race;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the sector-style classifier — doc 08's replacement for Balatro's poker-hand type, and the
    /// load-bearing invention of the whole collection design. These rules ARE the design: what counts as
    /// "patient" is what the Coward's Purse part rewards, so each threshold gets a test that pins the
    /// behaviour either side of it.
    /// </summary>
    public class SectorStyleTests : TestBase
    {
        const float Duration = 10f; // a ~230 m sector at racing speed

        /// <summary>A sector where nothing whatsoever happened — the baseline every test perturbs.</summary>
        static SectorEvidence Nothing() => new SectorEvidence { DurationS = Duration };

        static bool Has(SectorStyle style, SectorStyle tag) => SectorStyleClassifier.Has(style, tag);

        // ---------------------------------------------------------------- guards

        [Test]
        public void ZeroOrNegativeDuration_ClassifiesAsNothing()
        {
            Assert.AreEqual(SectorStyle.None, SectorStyleClassifier.Classify(new SectorEvidence()));
            Assert.AreEqual(SectorStyle.None,
                SectorStyleClassifier.Classify(new SectorEvidence { DurationS = -1f }));
        }

        [Test]
        public void InfiniteDuration_ClassifiesAsNothing_NoDivideBlowup()
        {
            var e = Nothing();
            e.DurationS = float.PositiveInfinity;
            Assert.AreEqual(SectorStyle.None, SectorStyleClassifier.Classify(e));
        }

        // ---------------------------------------------------------------- clean / ragged

        [Test]
        public void UneventfulSector_IsClean()
        {
            Assert.IsTrue(Has(SectorStyleClassifier.Classify(Nothing()), SectorStyle.Clean));
        }

        [Test]
        public void SustainedSpin_IsRagged_AndNotClean()
        {
            var e = Nothing();
            e.SpinSeconds = SectorStyleClassifier.SpinSecondsTolerance + 0.1f;
            SectorStyle style = SectorStyleClassifier.Classify(e);
            Assert.IsTrue(Has(style, SectorStyle.Ragged));
            Assert.IsFalse(Has(style, SectorStyle.Clean));
        }

        [Test]
        public void BriefSlide_IsNotRagged()
        {
            // Catching a slide is driving, not a mistake.
            var e = Nothing();
            e.SpinSeconds = SectorStyleClassifier.SpinSecondsTolerance - 0.05f;
            SectorStyle style = SectorStyleClassifier.Classify(e);
            Assert.IsFalse(Has(style, SectorStyle.Ragged));
            Assert.IsTrue(Has(style, SectorStyle.Clean));
        }

        [Test]
        public void Excursion_IsRagged()
        {
            var e = Nothing();
            e.OffSurfaceSeconds = SectorStyleClassifier.OffSurfaceSecondsTolerance + 0.1f;
            Assert.IsTrue(Has(SectorStyleClassifier.Classify(e), SectorStyle.Ragged));
        }

        [Test]
        public void WheelBrushingTheGrass_IsNotAnExcursion()
        {
            var e = Nothing();
            e.OffSurfaceSeconds = SectorStyleClassifier.OffSurfaceSecondsTolerance - 0.05f;
            Assert.IsTrue(Has(SectorStyleClassifier.Classify(e), SectorStyle.Clean));
        }

        [Test]
        public void RealDamage_IsRagged_ButAScrapeIsNot()
        {
            var damaged = Nothing();
            damaged.DurabilityLost = SectorStyleClassifier.DurabilityLostTolerance + 0.001f;
            Assert.IsTrue(Has(SectorStyleClassifier.Classify(damaged), SectorStyle.Ragged));

            var scraped = Nothing();
            scraped.DurabilityLost = SectorStyleClassifier.DurabilityLostTolerance - 0.001f;
            Assert.IsFalse(Has(SectorStyleClassifier.Classify(scraped), SectorStyle.Ragged));
        }

        [Test]
        public void CleanAndRagged_AreNeverBothSet()
        {
            // Not special-cased anywhere — it falls out of the rules, so it's worth pinning that it
            // still holds across every combination of the three ragged triggers.
            for (int mask = 0; mask < 8; mask++)
            {
                var e = Nothing();
                if ((mask & 1) != 0) e.SpinSeconds = 5f;
                if ((mask & 2) != 0) e.OffSurfaceSeconds = 5f;
                if ((mask & 4) != 0) e.DurabilityLost = 0.5f;

                SectorStyle style = SectorStyleClassifier.Classify(e);
                Assert.IsFalse(Has(style, SectorStyle.Clean) && Has(style, SectorStyle.Ragged),
                    $"mask {mask} produced both Clean and Ragged");
            }
        }

        // ---------------------------------------------------------------- contact

        [Test]
        public void BeingRammed_CostsYourCleanSector_ButIsNotRagged()
        {
            // Someone else's divebomb is not your mistake. You lose Clean; you have not driven raggedly.
            var e = Nothing();
            e.ContactsAsVictim = 1;
            SectorStyle style = SectorStyleClassifier.Classify(e);
            Assert.IsFalse(Has(style, SectorStyle.Clean));
            Assert.IsFalse(Has(style, SectorStyle.Ragged));
            Assert.IsFalse(Has(style, SectorStyle.Aggressive));
        }

        [Test]
        public void DrivingIntoSomeone_IsAggressive()
        {
            var e = Nothing();
            e.ContactsAsAggressor = 1;
            SectorStyle style = SectorStyleClassifier.Classify(e);
            Assert.IsTrue(Has(style, SectorStyle.Aggressive));
            Assert.IsFalse(Has(style, SectorStyle.Clean));
        }

        [Test]
        public void TakingAPlaceCleanly_IsAggressiveAndStillClean()
        {
            // A clean overtake should be credited as both — it is the ideal aggressive sector.
            var e = Nothing();
            e.PositionsGained = 1;
            SectorStyle style = SectorStyleClassifier.Classify(e);
            Assert.IsTrue(Has(style, SectorStyle.Aggressive));
            Assert.IsTrue(Has(style, SectorStyle.Clean));
        }

        // ---------------------------------------------------------------- defensive

        [Test]
        public void HoldingPositionUnderPressure_IsDefensive()
        {
            var e = Nothing();
            e.PressureSeconds = Duration * (SectorStyleClassifier.PressureFraction + 0.05f);
            Assert.IsTrue(Has(SectorStyleClassifier.Classify(e), SectorStyle.Defensive));
        }

        [Test]
        public void PressureButLosingAPlace_IsNotDefensive()
        {
            // Being leaned on is not the same as withstanding it.
            var e = Nothing();
            e.PressureSeconds = Duration * 0.9f;
            e.PositionsLost = 1;
            Assert.IsFalse(Has(SectorStyleClassifier.Classify(e), SectorStyle.Defensive));
        }

        [Test]
        public void BriefPressure_IsNotDefensive()
        {
            var e = Nothing();
            e.PressureSeconds = Duration * (SectorStyleClassifier.PressureFraction - 0.05f);
            Assert.IsFalse(Has(SectorStyleClassifier.Classify(e), SectorStyle.Defensive));
        }

        // ---------------------------------------------------------------- slipstream / patient

        [Test]
        public void SustainedTow_IsSlipstream()
        {
            var e = Nothing();
            e.DraftSeconds = Duration * (SectorStyleClassifier.DraftFraction + 0.05f);
            Assert.IsTrue(Has(SectorStyleClassifier.Classify(e), SectorStyle.Slipstream));
        }

        [Test]
        public void BriefTow_IsNotSlipstream()
        {
            var e = Nothing();
            e.DraftSeconds = Duration * (SectorStyleClassifier.DraftFraction - 0.05f);
            Assert.IsFalse(Has(SectorStyleClassifier.Classify(e), SectorStyle.Slipstream));
        }

        [Test]
        public void SustainedCoasting_IsPatient()
        {
            var e = Nothing();
            e.CoastSeconds = Duration * (SectorStyleClassifier.CoastFraction + 0.05f);
            Assert.IsTrue(Has(SectorStyleClassifier.Classify(e), SectorStyle.Patient));
        }

        [Test]
        public void ALiftIsNotPatience()
        {
            var e = Nothing();
            e.CoastSeconds = Duration * (SectorStyleClassifier.CoastFraction - 0.05f);
            Assert.IsFalse(Has(SectorStyleClassifier.Classify(e), SectorStyle.Patient));
        }

        [Test]
        public void FractionsAreRelativeToSectorLength_NotAbsoluteSeconds()
        {
            // The same absolute drafting time must classify differently in a short sector and a long
            // one. Absolute thresholds would silently mean different things on a speedway and a hairpin
            // complex — this is why every duration is compared as a fraction.
            var shortSector = new SectorEvidence { DurationS = 5f, DraftSeconds = 3f };   // 60%
            var longSector = new SectorEvidence { DurationS = 30f, DraftSeconds = 3f };   // 10%

            Assert.IsTrue(Has(SectorStyleClassifier.Classify(shortSector), SectorStyle.Slipstream));
            Assert.IsFalse(Has(SectorStyleClassifier.Classify(longSector), SectorStyle.Slipstream));
        }

        // ---------------------------------------------------------------- multi-tag

        [Test]
        public void TagsCoOccur_AForcedMoveThatWentWrong()
        {
            // The case that motivated making this [Flags] rather than exclusive: you barged past and ran
            // wide doing it. An exclusive classifier would have to discard one of those facts.
            var e = Nothing();
            e.ContactsAsAggressor = 1;
            e.PositionsGained = 1;
            e.OffSurfaceSeconds = 1f;

            SectorStyle style = SectorStyleClassifier.Classify(e);
            Assert.IsTrue(Has(style, SectorStyle.Aggressive));
            Assert.IsTrue(Has(style, SectorStyle.Ragged));
            Assert.IsFalse(Has(style, SectorStyle.Clean));
        }

        [Test]
        public void TagsCoOccur_TuckedInAndConserving()
        {
            var e = Nothing();
            e.DraftSeconds = Duration * 0.8f;
            e.CoastSeconds = Duration * 0.4f;

            SectorStyle style = SectorStyleClassifier.Classify(e);
            Assert.IsTrue(Has(style, SectorStyle.Slipstream));
            Assert.IsTrue(Has(style, SectorStyle.Patient));
            Assert.IsTrue(Has(style, SectorStyle.Clean));
        }

        [Test]
        public void Describe_ListsEveryTagSet_AndHandlesNone()
        {
            Assert.AreEqual("—", SectorStyleClassifier.Describe(SectorStyle.None));

            string text = SectorStyleClassifier.Describe(SectorStyle.Aggressive | SectorStyle.Ragged);
            StringAssert.Contains("AGGRESSIVE", text);
            StringAssert.Contains("RAGGED", text);
        }
    }
}
