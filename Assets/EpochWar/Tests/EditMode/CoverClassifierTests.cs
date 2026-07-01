using EpochWar.Core.State;
using EpochWar.Core.Systems;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Example-based boundary tests for <see cref="CoverClassifier"/> (Requirement 10.1).
    ///
    /// Covers the boundary cases called out in task 12.4: a cover-qualifying material at equal
    /// elevation, a non-qualifying material at equal elevation, and the exactly-at-margin elevation
    /// boundary.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-combat-visuals-expansion")]
    public sealed class CoverClassifierTests
    {
        [Test]
        public void CoverQualifyingMaterial_AtEqualElevation_Qualifies()
        {
            // Rock qualifies purely on material, even with no elevation advantage.
            Assert.That(CoverClassifier.IsCoverQualifying(CellMaterial.Rock, 3, 3), Is.True);
            Assert.That(CoverClassifier.IsCoverQualifying(CellMaterial.Reinforced, 0, 0), Is.True);
        }

        [Test]
        public void NonQualifyingMaterial_AtEqualElevation_DoesNotQualify()
        {
            // Soft materials with no elevation advantage grant no Cover.
            Assert.That(CoverClassifier.IsCoverQualifying(CellMaterial.Soil, 3, 3), Is.False);
            Assert.That(CoverClassifier.IsCoverQualifying(CellMaterial.Sand, 5, 5), Is.False);
            Assert.That(CoverClassifier.IsCoverQualifying(CellMaterial.Empty, 2, 2), Is.False);
        }

        [Test]
        public void NonQualifyingMaterial_ExactlyAtElevationMargin_Qualifies()
        {
            // A non-cover material exactly the default margin above the comparison elevation qualifies
            // on the basis of high ground.
            int comparison = 4;
            int atMargin = comparison + CoverClassifier.DefaultElevationMargin;

            Assert.That(CoverClassifier.IsCoverQualifying(CellMaterial.Soil, atMargin, comparison), Is.True);
        }

        [Test]
        public void NonQualifyingMaterial_JustBelowElevationMargin_DoesNotQualify()
        {
            // One level short of the margin (and no qualifying material) grants no Cover.
            int comparison = 4;
            int belowMargin = comparison + CoverClassifier.DefaultElevationMargin - 1;

            Assert.That(CoverClassifier.IsCoverQualifying(CellMaterial.Soil, belowMargin, comparison), Is.False);
        }

        [Test]
        public void ExplicitMargin_IsHonoredAtItsBoundary()
        {
            const int margin = 3;

            // Exactly at the explicit margin qualifies; one below does not.
            Assert.That(CoverClassifier.IsCoverQualifying(CellMaterial.Sand, 10, 7, margin), Is.True);
            Assert.That(CoverClassifier.IsCoverQualifying(CellMaterial.Sand, 9, 7, margin), Is.False);
        }

        [Test]
        public void IsCoverMaterial_ClassifiesHardMaterialsOnly()
        {
            Assert.That(CoverClassifier.IsCoverMaterial(CellMaterial.Rock), Is.True);
            Assert.That(CoverClassifier.IsCoverMaterial(CellMaterial.Reinforced), Is.True);
            Assert.That(CoverClassifier.IsCoverMaterial(CellMaterial.Soil), Is.False);
            Assert.That(CoverClassifier.IsCoverMaterial(CellMaterial.Sand), Is.False);
            Assert.That(CoverClassifier.IsCoverMaterial(CellMaterial.Empty), Is.False);
        }
    }
}
