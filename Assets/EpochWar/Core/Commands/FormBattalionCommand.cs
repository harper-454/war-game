using System.Collections.Generic;
using System.Linq;

namespace EpochWar.Core.Commands
{
    /// <summary>
    /// A Player's (or AI_Nation's) intent to group two or more of the Nation's Units into a named
    /// Battalion (Req 3.3).
    ///
    /// The command carries the issuing Nation, the Battalion's display <see cref="Name"/>, and the
    /// ids of the Units to group. The <see cref="EpochWar.Core.Systems.UnitSystem"/> handler
    /// validates that at least two of the referenced Units exist and are owned by the Nation, creates
    /// a named Battalion that retains its membership until disbanded or all members are eliminated
    /// (Req 3.3), and records each member's Battalion assignment. Fewer than two valid Units results
    /// in rejection with no state change.
    /// </summary>
    public sealed class FormBattalionCommand : ICommand
    {
        /// <inheritdoc />
        public int IssuingNationId { get; }

        /// <summary>The display name for the new Battalion (Req 3.3, 7.2).</summary>
        public string Name { get; }

        /// <summary>The ids of the Units to group into the Battalion (Req 3.3). Never <c>null</c>.</summary>
        public IReadOnlyList<int> UnitIds { get; }

        public FormBattalionCommand(int issuingNationId, string name, IEnumerable<int> unitIds)
        {
            IssuingNationId = issuingNationId;
            Name = name;
            UnitIds = unitIds?.ToList() ?? new List<int>();
        }

        public override string ToString()
            => $"FormBattalion(nation {IssuingNationId}, \"{Name}\", {UnitIds.Count} unit(s))";
    }
}
