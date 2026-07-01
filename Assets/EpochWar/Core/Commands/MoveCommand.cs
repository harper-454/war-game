using System.Collections.Generic;
using System.Linq;
using EpochWar.Core.State;

namespace EpochWar.Core.Commands
{
    /// <summary>
    /// A Player's (or AI_Nation's) intent to move one or more Units — or an entire Battalion —
    /// toward a reachable destination (Req 3.2, 3.4).
    ///
    /// The command targets either an explicit set of <see cref="UnitIds"/> or, when
    /// <see cref="BattalionId"/> is set, every surviving member of that Battalion (Req 3.4). The
    /// <see cref="EpochWar.Core.Systems.UnitSystem"/> handler resolves the target Units it owns,
    /// computes a navigable path to <see cref="Destination"/> for each over the current terrain's
    /// navigation grid, and issues a move order; Units advance along their paths on subsequent ticks
    /// (Req 3.2). Units for which no path exists are left in place, and the command is rejected only
    /// when no targeted Unit can reach the destination.
    /// </summary>
    public sealed class MoveCommand : ICommand
    {
        /// <inheritdoc />
        public int IssuingNationId { get; }

        /// <summary>The destination terrain cell the targeted Units should move toward (Req 3.2).</summary>
        public CellCoord Destination { get; }

        /// <summary>
        /// The ids of the individual Units to move. When <see cref="BattalionId"/> is set this may be
        /// empty, as the Battalion's members supply the targets (Req 3.4). Never <c>null</c>.
        /// </summary>
        public IReadOnlyList<int> UnitIds { get; }

        /// <summary>
        /// The id of the Battalion whose surviving members should move, or <c>null</c> for a plain
        /// per-Unit move (Req 3.4).
        /// </summary>
        public int? BattalionId { get; }

        /// <summary>Creates a move order for an explicit set of Units.</summary>
        public MoveCommand(int issuingNationId, IEnumerable<int> unitIds, CellCoord destination)
        {
            IssuingNationId = issuingNationId;
            Destination = destination;
            UnitIds = unitIds?.ToList() ?? new List<int>();
            BattalionId = null;
        }

        /// <summary>Creates a move order for every surviving member of a Battalion (Req 3.4).</summary>
        public MoveCommand(int issuingNationId, int battalionId, CellCoord destination)
        {
            IssuingNationId = issuingNationId;
            Destination = destination;
            UnitIds = new List<int>();
            BattalionId = battalionId;
        }

        public override string ToString()
            => BattalionId.HasValue
                ? $"Move(nation {IssuingNationId}, battalion {BattalionId.Value} -> {Destination})"
                : $"Move(nation {IssuingNationId}, {UnitIds.Count} unit(s) -> {Destination})";
    }
}
