using EpochWar.Core.State;
using EpochWar.Core.Systems;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Example-based unit tests for <see cref="TerrainSystem.GetCoverQualification"/> (task 13.2,
    /// Requirement 10.4).
    ///
    /// The query is a thin wrapper over <see cref="CoverClassifier.IsCoverQualifying"/>: it reads the
    /// defender cell's material from the terrain volume and compares the defender cell elevation to
    /// the attacker cell elevation. These tests verify it reports qualifying for a cover-qualifying
    /// material or an elevation-margin advantage and non-qualifying otherwise, and that its result
    /// matches calling <see cref="CoverClassifier"/> directly against the same inputs.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-combat-visuals-expansion")]
    public sealed class TerrainCoverQualificationTests
    {
        private static readonly Int3 Dims = new Int3(8, 8, 8);

        [Test]
        public void CoverMaterial_AtEqualElevation_Qualifies()
        {
            var terrain = new TerrainVolume(Dims, CellMaterial.Rock);
            var system = new TerrainSystem();

            var defenderCell = new CellCoord(2, 3, 2);
            var attackerCell = new CellCoord(5, 3, 2); // same elevation

            Assert.That(system.GetCoverQualification(terrain, defenderCell, attackerCell), Is.True);
            // Matches the classifier directly.
            Assert.That(
                CoverClassifier.IsCoverQualifying(CellMaterial.Rock, defenderCell.Y, attackerCell.Y),
                Is.True);
        }

        [Test]
        public void NonCoverMaterial_AtEqualElevation_DoesNotQualify()
        {
            var terrain = new TerrainVolume(Dims, CellMaterial.Soil);
            var system = new TerrainSystem();

            var defenderCell = new CellCoord(2, 3, 2);
            var attackerCell = new CellCoord(5, 3, 2); // same elevation, no advantage

            Assert.That(system.GetCoverQualification(terrain, defenderCell, attackerCell), Is.False);
            Assert.That(
                CoverClassifier.IsCoverQualifying(CellMaterial.Soil, defenderCell.Y, attackerCell.Y),
                Is.False);
        }

        [Test]
        public void NonCoverMaterial_WithElevationMargin_Qualifies()
        {
            var terrain = new TerrainVolume(Dims, CellMaterial.Soil);
            var system = new TerrainSystem();

            // Defender one level above the attacker meets the default elevation margin.
            var attackerCell = new CellCoord(5, 2, 2);
            var defenderCell = new CellCoord(2, 2 + CoverClassifier.DefaultElevationMargin, 2);

            Assert.That(system.GetCoverQualification(terrain, defenderCell, attackerCell), Is.True);
            Assert.That(
                CoverClassifier.IsCoverQualifying(CellMaterial.Soil, defenderCell.Y, attackerCell.Y),
                Is.True);
        }

        [Test]
        public void NonCoverMaterial_BelowElevationMargin_DoesNotQualify()
        {
            var terrain = new TerrainVolume(Dims, CellMaterial.Soil);
            var system = new TerrainSystem();

            // Defender lower than the attacker: neither material nor high ground grants cover.
            var attackerCell = new CellCoord(5, 4, 2);
            var defenderCell = new CellCoord(2, 3, 2);

            Assert.That(system.GetCoverQualification(terrain, defenderCell, attackerCell), Is.False);
            Assert.That(
                CoverClassifier.IsCoverQualifying(CellMaterial.Soil, defenderCell.Y, attackerCell.Y),
                Is.False);
        }

        [Test]
        public void OutOfRangeDefenderCell_ReadsAsEmptyMaterial_ElevationStillGoverns()
        {
            var terrain = new TerrainVolume(Dims, CellMaterial.Rock);
            var system = new TerrainSystem();

            // Way outside the volume: Get returns Empty (non-cover material), so only elevation counts.
            var attackerCell = new CellCoord(0, 0, 0);
            var farLowDefender = new CellCoord(100, 0, 100); // equal elevation, empty material
            var farHighDefender = new CellCoord(100, 5, 100); // above the margin

            Assert.That(system.GetCoverQualification(terrain, farLowDefender, attackerCell), Is.False);
            Assert.That(system.GetCoverQualification(terrain, farHighDefender, attackerCell), Is.True);
        }

        [Test]
        public void NullTerrain_ReportsNonQualifying()
        {
            var system = new TerrainSystem();
            Assert.That(
                system.GetCoverQualification(null, new CellCoord(0, 5, 0), new CellCoord(0, 0, 0)),
                Is.False);
        }
    }
}
