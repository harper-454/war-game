using EpochWar.Core.Systems;

namespace EpochWar.Core.Simulation
{
    /// <summary>
    /// The tuning parameters used to construct the simulation's systems, so a
    /// <see cref="MatchSimulation"/> can be wired with the same knobs the individual systems expose
    /// without the caller having to build each system by hand.
    ///
    /// All values default to the same defaults the underlying systems use, so
    /// <c>new SimulationConfig()</c> reproduces the systems' out-of-the-box behaviour. The config is
    /// a plain, immutable-by-convention data holder; it carries no logic.
    /// </summary>
    public sealed class SimulationConfig
    {
        /// <summary>
        /// Population units a Nation gains per second while below capacity with sufficient food,
        /// passed to <see cref="CivSystem"/>. Must be positive.
        /// </summary>
        public float PopulationGrowthPerSecond { get; set; } = 1f;

        /// <summary>
        /// A Nation's food is considered sufficient for growth while its stored Food strictly
        /// exceeds this threshold, passed to <see cref="CivSystem"/>.
        /// </summary>
        public float FoodThreshold { get; set; } = 0f;

        /// <summary>
        /// Simulation seconds a launched Colony_Ship's colonization sequence takes to complete,
        /// passed to <see cref="UnitSystem"/> (Req 11.2).
        /// </summary>
        public float ColonizationDurationSeconds { get; set; } = 0f;

        /// <summary>
        /// Maximum surface step (in cells) a ground Unit may traverse between adjacent columns,
        /// passed to <see cref="UnitSystem"/> / the derived nav grid.
        /// </summary>
        public int NavMaxStepHeight { get; set; } = 1;

        /// <summary>
        /// The consequence applied to a Structure/Unit that loses its supporting terrain, passed to
        /// <see cref="TerrainSystem"/> (Req 6.4).
        /// </summary>
        public SupportLossConsequence SupportLossConsequence { get; set; } = SupportLossConsequence.Destroy;

        /// <summary>
        /// Health removed from an unsupported entity when <see cref="SupportLossConsequence"/> is
        /// <see cref="Systems.SupportLossConsequence.Damage"/>, passed to <see cref="TerrainSystem"/>.
        /// </summary>
        public int SupportLossDamage { get; set; } = 0;

        /// <summary>A shared default configuration instance.</summary>
        public static SimulationConfig Default => new SimulationConfig();
    }
}
