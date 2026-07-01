using System.Collections.Generic;

namespace EpochWar.Core.State
{
    /// <summary>
    /// Per-Nation fog-of-war state (Requirement 14), owned and mutated exclusively by the
    /// Vision_System.
    ///
    /// This type is intentionally <em>not</em> stored on <see cref="Nation"/>: unlike the
    /// Nation's durable data (Resources, CompletedTechIds, Battalions), the visible-cell set is
    /// a fully-derived cache recomputed from scratch every Vision_System tick from the current
    /// positions of the Nation's owned entities (Req 14.1/14.7/14.8), so it belongs with the
    /// system that derives it (Design Principle 5). <see cref="LastKnownPosition"/>, by contrast,
    /// <em>is</em> durable — it persists across many ticks until overwritten on re-sighting or
    /// discarded on removal (Req 14.3/14.9) — so it is stored here rather than recomputed.
    /// </summary>
    public sealed class VisionState
    {
        /// <summary>
        /// The set of Terrain_Cells currently visible to this Nation, computed as the union of
        /// every owned Unit's/Structure's Sight_Radius coverage (Req 14.1).
        /// </summary>
        public HashSet<CellCoord> VisibleCells { get; } = new HashSet<CellCoord>();

        /// <summary>
        /// Maps each enemy Unit/Structure id to whether it is currently visible to this Nation
        /// (Req 14.2, 14.8).
        /// </summary>
        public Dictionary<int, bool> EnemyVisibility { get; } = new Dictionary<int, bool>();

        /// <summary>
        /// Maps each hidden enemy Unit/Structure id to its Last_Known_Position; an entry is
        /// present only while that entity is hidden and a position was recorded at the
        /// visible-to-hidden transition (Req 14.3, 14.6, 14.9).
        /// </summary>
        public Dictionary<int, WorldPosition> LastKnownPosition { get; } = new Dictionary<int, WorldPosition>();
    }
}
