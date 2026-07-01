using System.Collections.Generic;
using EpochWar.Core.Commands;
using EpochWar.Core.State;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that a queued <see cref="TerrainEffect"/> was applied to
    /// the Match's <see cref="TerrainVolume"/> and listing exactly the cells it changed (Req 6.2).
    ///
    /// The <see cref="TerrainSystem"/> emits one of these for every applied effect that actually
    /// altered or removed at least one cell. Because the payload is just the effect and the compact
    /// list of modified <see cref="CellCoord"/>s, the networking layer can replicate the change to
    /// all clients as a cell-delta message (Req 6.5) and the presentation layer can rebuild only the
    /// affected chunk meshes. The event reports terrain that has <em>already</em> been modified; it
    /// never itself mutates state.
    /// </summary>
    public sealed class TerrainModifiedEvent : GameEvent
    {
        /// <summary>The effect that produced this modification.</summary>
        public TerrainEffect Effect { get; }

        /// <summary>The coordinates of every cell altered or removed by the effect (never empty).</summary>
        public IReadOnlyList<CellCoord> ModifiedCells { get; }

        public TerrainModifiedEvent(TerrainEffect effect, IReadOnlyList<CellCoord> modifiedCells)
        {
            Effect = effect;
            ModifiedCells = modifiedCells ?? new List<CellCoord>();
        }

        /// <summary>The number of cells changed by the effect.</summary>
        public int ModifiedCount => ModifiedCells.Count;

        public override string ToString()
            => $"TerrainModified(center {Effect.Center}, r{Effect.Radius}/d{Effect.Depth}, "
               + $"{ModifiedCount} cells)";
    }
}
