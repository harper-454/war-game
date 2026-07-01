using EpochWar.Core.Commands;
using EpochWar.Core.State;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that a recruited Unit has been produced at its issuing
    /// Structure after its build time elapsed (Req 3.1).
    ///
    /// The <see cref="UnitSystem"/> emits this in <see cref="UnitSystem.Tick"/> when a queued build
    /// completes and exactly one Unit spawns — not when the recruit command is first accepted (the
    /// Resource/population cost is deducted then, and reported by its own change events). It carries
    /// the new Unit's id, its owning Nation, its <see cref="UnitDefId"/>, the producing Structure,
    /// and the cell it spawned at so the networking/UI layers can create the corresponding view.
    /// </summary>
    public sealed class UnitRecruitedEvent : GameEvent
    {
        /// <summary>The id of the Nation that recruited the Unit.</summary>
        public int NationId { get; }

        /// <summary>The id of the newly produced Unit.</summary>
        public int UnitId { get; }

        /// <summary>The id of the Unit type that was recruited.</summary>
        public string UnitDefId { get; }

        /// <summary>The id of the Structure that produced the Unit (Req 3.1).</summary>
        public int StructureId { get; }

        /// <summary>The terrain cell the Unit spawned at.</summary>
        public CellCoord SpawnCell { get; }

        public UnitRecruitedEvent(int nationId, int unitId, string unitDefId, int structureId, CellCoord spawnCell)
        {
            NationId = nationId;
            UnitId = unitId;
            UnitDefId = unitDefId;
            StructureId = structureId;
            SpawnCell = spawnCell;
        }

        public override string ToString()
            => $"UnitRecruited(nation {NationId}, unit #{UnitId} \"{UnitDefId}\" at structure {StructureId})";
    }
}
