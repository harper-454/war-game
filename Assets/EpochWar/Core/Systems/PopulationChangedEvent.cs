using EpochWar.Core.Commands;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// The reason a Nation's population count changed, so consumers (UI/networking) can present
    /// the change appropriately (Req 5.1, 7.1/7.4).
    /// </summary>
    public enum PopulationChangeCause
    {
        /// <summary>Organic growth toward capacity while food sufficed (Req 5.2).</summary>
        Growth = 0,

        /// <summary>Population drawn down to staff a recruit/construct action (Req 5.4).</summary>
        Consumed = 1,

        /// <summary>Population returned to the available pool (e.g. a unit/structure was removed).</summary>
        Released = 2,
    }

    /// <summary>
    /// A <see cref="GameEvent"/> announcing that a single Nation's available population count
    /// changed (Req 5.1, 5.2).
    ///
    /// The <see cref="CivSystem"/> emits one of these whenever it mutates a Nation's
    /// <see cref="State.Nation.Population"/> — through organic growth in
    /// <see cref="CivSystem.Tick"/> (Req 5.2/5.3), through population consumed to staff a
    /// recruit/construct command (Req 5.4), or through population released back to the pool when an
    /// entity is removed — so the networking layer can replicate the change and the UI can refresh
    /// the displayed population within its freshness budget (Req 7.1, 7.4). The event reports state
    /// that has <em>already</em> been applied and carries the before/after counts so consumers can
    /// render deltas without re-reading the Nation.
    /// </summary>
    public sealed class PopulationChangedEvent : GameEvent
    {
        /// <summary>The id of the Nation whose population changed.</summary>
        public int NationId { get; }

        /// <summary>The population count before the change.</summary>
        public int OldPopulation { get; }

        /// <summary>The population count after the change.</summary>
        public int NewPopulation { get; }

        /// <summary>The Nation's population capacity at the time of the change (Req 5.1, 5.3).</summary>
        public int PopulationCapacity { get; }

        /// <summary>What caused the change.</summary>
        public PopulationChangeCause Cause { get; }

        public PopulationChangedEvent(
            int nationId,
            int oldPopulation,
            int newPopulation,
            int populationCapacity,
            PopulationChangeCause cause)
        {
            NationId = nationId;
            OldPopulation = oldPopulation;
            NewPopulation = newPopulation;
            PopulationCapacity = populationCapacity;
            Cause = cause;
        }

        /// <summary>The signed change actually applied to the population count.</summary>
        public int Delta => NewPopulation - OldPopulation;

        public override string ToString()
            => $"PopulationChanged(nation {NationId}, {OldPopulation} -> {NewPopulation}"
               + $"/{PopulationCapacity}, {Cause})";
    }
}
