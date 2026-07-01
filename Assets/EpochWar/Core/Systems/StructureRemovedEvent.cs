using EpochWar.Core.Commands;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that a Structure reduced to zero health was removed from
    /// the Match and its functions disabled (Req 4.5).
    ///
    /// The <see cref="BaseSystem"/> emits this whenever it removes a Structure — via the tick's
    /// destroyed-structure sweep or an explicit <see cref="BaseSystem.RemoveStructure"/> call. It
    /// carries the former Structure's id, owning Nation, and type id so consumers can drop the
    /// corresponding view, plus <see cref="WasIncompletePeaceArch"/> which is true when the removed
    /// Structure was a Peace_Arch destroyed before completion — the case in which the Peace victory is
    /// withheld from its owner (Req 10.4). The removal it reports has <em>already</em> been applied.
    /// </summary>
    public sealed class StructureRemovedEvent : GameEvent
    {
        /// <summary>The id of the Nation that owned the removed Structure.</summary>
        public int NationId { get; }

        /// <summary>The id of the removed Structure.</summary>
        public int StructureId { get; }

        /// <summary>The id of the removed Structure's type.</summary>
        public string StructureDefId { get; }

        /// <summary>True when the removed Structure was a Peace_Arch destroyed before completion (Req 10.4).</summary>
        public bool WasIncompletePeaceArch { get; }

        public StructureRemovedEvent(
            int nationId, int structureId, string structureDefId, bool wasIncompletePeaceArch)
        {
            NationId = nationId;
            StructureId = structureId;
            StructureDefId = structureDefId;
            WasIncompletePeaceArch = wasIncompletePeaceArch;
        }

        public override string ToString()
            => $"StructureRemoved(nation {NationId}, structure #{StructureId} \"{StructureDefId}\""
               + $"{(WasIncompletePeaceArch ? ", incomplete Peace_Arch" : string.Empty)})";
    }
}
