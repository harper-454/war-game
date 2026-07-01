using EpochWar.Core.Commands;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that a Structure's construction time has elapsed and it
    /// is now operational with its functions enabled (Req 4.3).
    ///
    /// The <see cref="BaseSystem"/> emits this from <see cref="BaseSystem.Tick"/> when a Structure's
    /// accumulated construction progress reaches its build time — not when it is first placed (that
    /// is reported by <see cref="StructurePlacedEvent"/>). For the Peace_Arch a companion
    /// <see cref="PeaceArchCompletedEvent"/> is emitted so the Victory_System can resolve the Peace
    /// victory (Req 10.3). The transition to operational it reports has <em>already</em> been applied.
    /// </summary>
    public sealed class StructureConstructionCompletedEvent : GameEvent
    {
        /// <summary>The id of the Nation that owns the completed Structure.</summary>
        public int NationId { get; }

        /// <summary>The id of the completed Structure.</summary>
        public int StructureId { get; }

        /// <summary>The id of the completed Structure's type.</summary>
        public string StructureDefId { get; }

        /// <summary>True when the completed Structure is the Peace_Arch wonder (Req 10.3).</summary>
        public bool IsPeaceArch { get; }

        public StructureConstructionCompletedEvent(
            int nationId, int structureId, string structureDefId, bool isPeaceArch)
        {
            NationId = nationId;
            StructureId = structureId;
            StructureDefId = structureDefId;
            IsPeaceArch = isPeaceArch;
        }

        public override string ToString()
            => $"StructureConstructionCompleted(nation {NationId}, structure #{StructureId} \"{StructureDefId}\")";
    }
}
