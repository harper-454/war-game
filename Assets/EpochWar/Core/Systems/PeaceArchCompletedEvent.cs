using EpochWar.Core.Commands;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> signalling that a Nation's Peace_Arch wonder has completed
    /// construction — the trigger the Victory_System consumes to declare the Peace victory and end
    /// the Match (Req 10.3).
    ///
    /// The <see cref="BaseSystem"/> emits this from <see cref="BaseSystem.Tick"/> alongside the
    /// generic <see cref="StructureConstructionCompletedEvent"/> when the Peace_Arch becomes
    /// operational. Because the signal is produced only on completion, a Peace_Arch destroyed before
    /// its construction finishes never produces this event and so its owner is never awarded the
    /// Peace victory (Req 10.4).
    /// </summary>
    public sealed class PeaceArchCompletedEvent : GameEvent
    {
        /// <summary>The id of the Nation whose Peace_Arch completed (Req 10.3).</summary>
        public int NationId { get; }

        /// <summary>The id of the completed Peace_Arch Structure.</summary>
        public int StructureId { get; }

        public PeaceArchCompletedEvent(int nationId, int structureId)
        {
            NationId = nationId;
            StructureId = structureId;
        }

        public override string ToString()
            => $"PeaceArchCompleted(nation {NationId}, structure #{StructureId})";
    }
}
