using System.Collections.Generic;

namespace EpochWar.Core.State
{
    /// <summary>
    /// A named, persistent grouping of Units that can be commanded as one entity (Req 3.3, 3.4).
    ///
    /// A Battalion is created from two or more selected Units and retains its
    /// <see cref="MemberUnitIds"/> until the Player disbands it or every member is eliminated
    /// (Req 3.3, Property 13). Commands issued to a Battalion are applied to each surviving
    /// member (Req 3.4, Property 14), and when a Unit's health reaches zero the Unit_System
    /// removes it from the Match and from this membership set (Req 3.5, Property 15).
    ///
    /// Members are referenced by id (rather than by object) so a Battalion never holds a stale
    /// reference to a removed Unit and so the type stays trivially serializable.
    /// </summary>
    public sealed class Battalion
    {
        /// <summary>Stable per-Match identifier.</summary>
        public int Id { get; }

        /// <summary>Player-chosen display name (Req 7.2 info panel).</summary>
        public string Name { get; set; }

        /// <summary>The ids of the member <see cref="UnitInstance"/>s (Req 3.3).</summary>
        public HashSet<int> MemberUnitIds { get; }

        public Battalion(int id, string name, IEnumerable<int> memberUnitIds = null)
        {
            Id = id;
            Name = name;
            MemberUnitIds = memberUnitIds == null
                ? new HashSet<int>()
                : new HashSet<int>(memberUnitIds);
        }

        public override string ToString() => $"Battalion({Id}, \"{Name}\", {MemberUnitIds.Count} units)";
    }
}
