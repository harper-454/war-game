using EpochWar.Core.Commands;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that a Unit whose health reached zero was fully removed
    /// from the Match and from any Battalion of which it was a member (Req 3.5).
    ///
    /// The <see cref="UnitSystem"/> emits this whenever it removes a Unit — after combat reduces its
    /// health to zero (Req 3.7), or via the tick's dead-unit sweep. It carries the former Unit's id,
    /// owning Nation, and type id so consumers can drop the corresponding view. The removal it
    /// reports has <em>already</em> been applied.
    /// </summary>
    public sealed class UnitEliminatedEvent : GameEvent
    {
        /// <summary>The id of the Nation that owned the eliminated Unit.</summary>
        public int NationId { get; }

        /// <summary>The id of the eliminated Unit.</summary>
        public int UnitId { get; }

        /// <summary>The id of the eliminated Unit's type.</summary>
        public string UnitDefId { get; }

        public UnitEliminatedEvent(int nationId, int unitId, string unitDefId)
        {
            NationId = nationId;
            UnitId = unitId;
            UnitDefId = unitDefId;
        }

        public override string ToString()
            => $"UnitEliminated(nation {NationId}, unit #{UnitId} \"{UnitDefId}\")";
    }
}
