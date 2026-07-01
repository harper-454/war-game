using EpochWar.Core.State;

namespace EpochWar.Core.Commands
{
    /// <summary>
    /// A Player's (or AI_Nation's) intent to place one unlocked Structure type at a terrain
    /// location and begin its construction (Req 4.1, 4.2, 10.2).
    ///
    /// The command carries the issuing Nation, the id of the <see cref="EpochWar.Core.State.Content.StructureDef"/>
    /// to build, and the footprint-origin <see cref="CellCoord"/> at which to place it. All
    /// validation — that the Structure type exists and is unlocked for the Nation (Req 4.6), or, for
    /// the Peace_Arch, that its prerequisite Technologies are complete (Req 10.1); that the target
    /// terrain is inside the volume, supported, and not already occupied (Req 4.2); and that the
    /// Nation can afford both the Resource cost (Req 4.1) and the population cost (Req 5.4) — is
    /// performed by the <see cref="EpochWar.Core.Systems.BaseSystem"/> handler when this command is
    /// dispatched. On acceptance the Resource and population costs are deducted and an
    /// under-construction Structure is created at the location (Req 4.1); it becomes operational once
    /// its build time elapses in simulation (Req 4.3). Rejection leaves all state untouched (Req 4.2).
    /// </summary>
    public sealed class PlaceStructureCommand : ICommand
    {
        /// <inheritdoc />
        public int IssuingNationId { get; }

        /// <summary>The id of the <see cref="EpochWar.Core.State.Content.StructureDef"/> to place.</summary>
        public string StructureId { get; }

        /// <summary>The footprint-origin terrain cell at which to anchor the Structure (Req 4.1, 4.2).</summary>
        public CellCoord Origin { get; }

        public PlaceStructureCommand(int issuingNationId, string structureId, CellCoord origin)
        {
            IssuingNationId = issuingNationId;
            StructureId = structureId;
            Origin = origin;
        }

        public override string ToString()
            => $"PlaceStructure(nation {IssuingNationId}, structure \"{StructureId}\", {Origin})";
    }
}
