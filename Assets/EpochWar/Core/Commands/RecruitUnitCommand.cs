namespace EpochWar.Core.Commands
{
    /// <summary>
    /// A Player's (or AI_Nation's) intent to recruit one Unit of an unlocked type at one of the
    /// Nation's Structures (Req 3.1).
    ///
    /// The command carries the issuing Nation, the id of the producing <see cref="EpochWar.Core.State.StructureInstance"/>,
    /// and the id of the <see cref="EpochWar.Core.State.Content.UnitDef"/> to build. All validation —
    /// that the Structure exists, is owned by the Nation and operational, that the Unit type is
    /// unlocked, and that the Nation can afford both the Resource cost (Req 3.1) and the population
    /// cost (Req 5.4) — is performed by the <see cref="EpochWar.Core.Systems.UnitSystem"/> handler
    /// when this command is dispatched. On acceptance the Resource and population costs are deducted
    /// and a build is queued at the Structure; exactly one Unit is produced there once the Unit's
    /// build time elapses in simulation. Rejection leaves all state untouched.
    /// </summary>
    public sealed class RecruitUnitCommand : ICommand
    {
        /// <inheritdoc />
        public int IssuingNationId { get; }

        /// <summary>The id of the Structure that produces the Unit and where it spawns (Req 3.1).</summary>
        public int StructureId { get; }

        /// <summary>The id of the <see cref="EpochWar.Core.State.Content.UnitDef"/> to recruit.</summary>
        public string UnitId { get; }

        public RecruitUnitCommand(int issuingNationId, int structureId, string unitId)
        {
            IssuingNationId = issuingNationId;
            StructureId = structureId;
            UnitId = unitId;
        }

        public override string ToString()
            => $"RecruitUnit(nation {IssuingNationId}, structure {StructureId}, unit \"{UnitId}\")";
    }
}
