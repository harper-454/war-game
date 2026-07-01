using System.Collections.Generic;
using EpochWar.Core.Commands;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that a named Battalion was formed from a set of Units
    /// (Req 3.3).
    ///
    /// The <see cref="UnitSystem"/> emits this when a <see cref="Commands.FormBattalionCommand"/> is
    /// accepted. It carries the owning Nation, the new Battalion's id and name, and the ids of the
    /// member Units so the networking/UI layers can present the grouping. The Battalion retains this
    /// membership until it is disbanded or all members are eliminated (Req 3.3).
    /// </summary>
    public sealed class BattalionFormedEvent : GameEvent
    {
        /// <summary>The id of the Nation that owns the Battalion.</summary>
        public int NationId { get; }

        /// <summary>The id of the newly formed Battalion.</summary>
        public int BattalionId { get; }

        /// <summary>The Battalion's display name.</summary>
        public string Name { get; }

        /// <summary>The ids of the Units grouped into the Battalion (Req 3.3).</summary>
        public IReadOnlyList<int> MemberUnitIds { get; }

        public BattalionFormedEvent(int nationId, int battalionId, string name, IReadOnlyList<int> memberUnitIds)
        {
            NationId = nationId;
            BattalionId = battalionId;
            Name = name;
            MemberUnitIds = memberUnitIds ?? new List<int>();
        }

        public override string ToString()
            => $"BattalionFormed(nation {NationId}, battalion {BattalionId} \"{Name}\", {MemberUnitIds.Count} member(s))";
    }
}
