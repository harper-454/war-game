using System.Collections.Generic;

namespace EpochWar.Core.State
{
    /// <summary>
    /// The complete authoritative state of a single Match — the root the Host owns and the
    /// systems mutate each tick (Req 8.3, 12).
    ///
    /// It holds the simulation clock (<see cref="TickCount"/>, a monotonic counter used to
    /// timestamp victory completions for tie-breaks per Req 11.4), the lifecycle
    /// <see cref="Status"/> and resolved <see cref="Outcome"/> (Req 12.2–12.4), and the
    /// authoritative collections of <see cref="Nations"/>, <see cref="Units"/>, and
    /// <see cref="Structures"/> keyed by id, plus the destructible <see cref="Terrain"/>
    /// (Req 6.1). All gameplay rules operate on this single instance through the command
    /// pipeline and the per-system Tick methods (added in later tasks), so there is exactly one
    /// place state changes are applied and replicated.
    ///
    /// This type is a plain data container; ordering and mutation logic live in the systems
    /// and the simulation loop (task 13.1).
    /// </summary>
    public sealed class MatchState
    {
        /// <summary>Monotonic simulation tick counter; advanced once per fixed step (Req 11.4).</summary>
        public long TickCount { get; set; }

        /// <summary>Whether the Match is in progress or ended (Req 12.2, 12.3).</summary>
        public MatchStatus Status { get; set; }

        /// <summary>The resolved outcome, or <c>null</c> until the Match ends (Req 12.3, 12.4).</summary>
        public MatchOutcome Outcome { get; set; }

        /// <summary>All Nations in the Match keyed by Nation id.</summary>
        public Dictionary<int, Nation> Nations { get; }

        /// <summary>All live Units in the Match keyed by Unit id (Req 3).</summary>
        public Dictionary<int, UnitInstance> Units { get; }

        /// <summary>All placed Structures in the Match keyed by Structure id (Req 4).</summary>
        public Dictionary<int, StructureInstance> Structures { get; }

        /// <summary>The destructible/diggable terrain volume (Req 6.1).</summary>
        public TerrainVolume Terrain { get; set; }

        public MatchState(TerrainVolume terrain = null)
        {
            TickCount = 0;
            Status = MatchStatus.InProgress;
            Outcome = null;
            Nations = new Dictionary<int, Nation>();
            Units = new Dictionary<int, UnitInstance>();
            Structures = new Dictionary<int, StructureInstance>();
            Terrain = terrain ?? new TerrainVolume();
        }

        public override string ToString()
            => $"MatchState(tick {TickCount}, {Status}, {Nations.Count} nations, "
               + $"{Units.Count} units, {Structures.Count} structures)";
    }
}
