using EpochWar.Core.State;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// Pure, static classification of whether a Terrain_Cell qualifies a defending Unit for Cover
    /// (Requirement 10.1).
    ///
    /// <para>
    /// A position qualifies for Cover when <b>either</b> of these holds:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     its <see cref="CellMaterial"/> is one of the configured cover-qualifying materials —
    ///     <see cref="CellMaterial.Rock"/> or <see cref="CellMaterial.Reinforced"/>, the harder
    ///     materials that a Unit can shelter in/behind; or
    ///   </item>
    ///   <item>
    ///     its elevation exceeds a comparison elevation (e.g. the attacker's) by at least the
    ///     configured <see cref="DefaultElevationMargin"/> — high ground grants Cover.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// The classification is a pure function of its inputs (no state, no floating point) so
    /// <c>CombatSystem</c> and <c>TerrainSystem</c> can call it deterministically, and its result is
    /// consumed by the Terrain_System's cover-qualification query (Req 10.4).
    /// </para>
    /// </summary>
    public static class CoverClassifier
    {
        /// <summary>
        /// The minimum number of elevation levels a cell must sit above the comparison elevation to
        /// qualify for Cover on the basis of high ground alone. A margin of 1 means the cell must be
        /// at least one level higher than the comparison elevation.
        /// </summary>
        public const int DefaultElevationMargin = 1;

        /// <summary>
        /// True when <paramref name="material"/> is a configured cover-qualifying material
        /// (<see cref="CellMaterial.Rock"/> or <see cref="CellMaterial.Reinforced"/>).
        /// </summary>
        public static bool IsCoverMaterial(CellMaterial material)
            => material == CellMaterial.Rock || material == CellMaterial.Reinforced;

        /// <summary>
        /// Returns whether a cell of the given <paramref name="material"/> at
        /// <paramref name="cellElevation"/> qualifies a defender for Cover relative to
        /// <paramref name="comparisonElevation"/>, using the default elevation margin (Req 10.1).
        ///
        /// Qualifies when the material is cover-qualifying, or when the cell's elevation exceeds the
        /// comparison elevation by at least <see cref="DefaultElevationMargin"/>.
        /// </summary>
        public static bool IsCoverQualifying(CellMaterial material, int cellElevation, int comparisonElevation)
            => IsCoverQualifying(material, cellElevation, comparisonElevation, DefaultElevationMargin);

        /// <summary>
        /// Overload allowing an explicit <paramref name="elevationMargin"/> (Req 10.1). A cell
        /// qualifies for Cover when its material is cover-qualifying, or when
        /// <c>cellElevation - comparisonElevation &gt;= elevationMargin</c>. At exactly the margin the
        /// cell qualifies; one level below the margin it does not (unless the material qualifies).
        /// </summary>
        public static bool IsCoverQualifying(
            CellMaterial material,
            int cellElevation,
            int comparisonElevation,
            int elevationMargin)
        {
            if (IsCoverMaterial(material))
            {
                return true;
            }

            return cellElevation - comparisonElevation >= elevationMargin;
        }
    }
}
