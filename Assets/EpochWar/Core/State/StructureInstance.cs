using EpochWar.Core.State.Content;

namespace EpochWar.Core.State
{
    /// <summary>
    /// A placed Structure in the Match, either under construction or operational (Req 4).
    ///
    /// The instance tracks its own mutable runtime state — <see cref="Health"/>, terrain
    /// <see cref="Origin"/>, accumulated <see cref="ConstructionProgress"/>, and the
    /// <see cref="IsOperational"/> flag — while sharing the immutable <see cref="StructureDef"/>
    /// for its static attributes (cost, build time, footprint, function, max health). While a
    /// Structure is building, its production/command functions are disabled (Req 4.4); once
    /// construction time elapses it becomes operational (Req 4.3). When <see cref="Health"/>
    /// reaches zero the Base_System removes it and disables its functions (Req 4.5).
    /// </summary>
    public sealed class StructureInstance
    {
        /// <summary>Stable per-Match identifier.</summary>
        public int Id { get; }

        /// <summary>The id of the owning <see cref="Nation"/>.</summary>
        public int OwnerNationId { get; }

        /// <summary>The shared immutable definition supplying static attributes (Req 4).</summary>
        public StructureDef Def { get; }

        /// <summary>Current health; reaching zero triggers removal (Req 4.5).</summary>
        public int Health { get; set; }

        /// <summary>The footprint-origin terrain cell this Structure occupies (Req 4.1, 4.2).</summary>
        public CellCoord Origin { get; set; }

        /// <summary>Construction seconds accumulated toward <see cref="StructureDef.BuildTimeSeconds"/> (Req 4.3).</summary>
        public float ConstructionProgress { get; set; }

        /// <summary>True once construction completes and functions are enabled (Req 4.3, 4.4).</summary>
        public bool IsOperational { get; set; }

        public StructureInstance(int id, int ownerNationId, StructureDef def, CellCoord origin)
        {
            Id = id;
            OwnerNationId = ownerNationId;
            Def = def;
            Health = def?.MaxHealth ?? 0;
            Origin = origin;
            ConstructionProgress = 0f;
            IsOperational = false;
        }

        public override string ToString()
            => $"Structure#{Id}(owner {OwnerNationId}, {Def?.Id}, {(IsOperational ? "operational" : "building")})";
    }
}
