using EpochWar.Core.Commands;
using EpochWar.Core.State;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that a Structure was validly placed and its construction
    /// has begun after its Resource and population costs were paid (Req 4.1, 10.2).
    ///
    /// The <see cref="BaseSystem"/> emits this when a <see cref="Commands.PlaceStructureCommand"/> is
    /// accepted (the cost deductions are reported by their own resource/population change events). It
    /// carries the new Structure's id, its owning Nation, its <see cref="StructureDefId"/>, the
    /// footprint origin it occupies, and whether it is the Peace_Arch wonder so the networking/UI
    /// layers can create the corresponding under-construction view.
    /// </summary>
    public sealed class StructurePlacedEvent : GameEvent
    {
        /// <summary>The id of the Nation that placed the Structure.</summary>
        public int NationId { get; }

        /// <summary>The id of the newly placed Structure.</summary>
        public int StructureId { get; }

        /// <summary>The id of the Structure type that was placed.</summary>
        public string StructureDefId { get; }

        /// <summary>The footprint-origin terrain cell the Structure occupies (Req 4.1).</summary>
        public CellCoord Origin { get; }

        /// <summary>True when the placed Structure is the Peace_Arch wonder (Req 10.2).</summary>
        public bool IsPeaceArch { get; }

        public StructurePlacedEvent(
            int nationId, int structureId, string structureDefId, CellCoord origin, bool isPeaceArch)
        {
            NationId = nationId;
            StructureId = structureId;
            StructureDefId = structureDefId;
            Origin = origin;
            IsPeaceArch = isPeaceArch;
        }

        public override string ToString()
            => $"StructurePlaced(nation {NationId}, structure #{StructureId} \"{StructureDefId}\" at {Origin})";
    }
}
