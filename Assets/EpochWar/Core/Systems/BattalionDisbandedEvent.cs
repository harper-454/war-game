using EpochWar.Core.Commands;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that a Battalion was disbanded and no longer exists
    /// (Req 3.3).
    ///
    /// The <see cref="UnitSystem"/> emits this both when a <see cref="Commands.DisbandBattalionCommand"/>
    /// is accepted (Player-initiated disband) and when a Battalion's last surviving member is
    /// eliminated (Req 3.3, 3.5). Its former member Units — if any remain — keep existing in the
    /// Match with their Battalion assignment cleared. Consumers use it to drop the Battalion from the
    /// UI/replicated view.
    /// </summary>
    public sealed class BattalionDisbandedEvent : GameEvent
    {
        /// <summary>The id of the Nation that owned the Battalion.</summary>
        public int NationId { get; }

        /// <summary>The id of the disbanded Battalion.</summary>
        public int BattalionId { get; }

        /// <summary>True when the disband was caused by every member being eliminated (Req 3.5).</summary>
        public bool CausedByElimination { get; }

        public BattalionDisbandedEvent(int nationId, int battalionId, bool causedByElimination)
        {
            NationId = nationId;
            BattalionId = battalionId;
            CausedByElimination = causedByElimination;
        }

        public override string ToString()
            => $"BattalionDisbanded(nation {NationId}, battalion {BattalionId}"
               + $"{(CausedByElimination ? ", eliminated" : string.Empty)})";
    }
}
