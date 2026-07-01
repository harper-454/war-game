using System.Collections.Generic;
using System.Linq;

namespace EpochWar.Core.State.Content
{
    /// <summary>
    /// An engine-free definition describing one <see cref="State.Era"/> stage (Req 1.1, 1.4).
    ///
    /// The ordered Era progression itself is the natural order of the <see cref="State.Era"/>
    /// enum; this definition supplies the authored data the Tech_System needs to gate
    /// advancement: <see cref="RequiredTechIds"/> lists every Technology that must be in a
    /// Nation's completed set before it may advance <em>into</em> this Era (Req 1.4). The
    /// first Era (Prehistoric) has no requirements.
    /// </summary>
    public sealed class EraDef
    {
        /// <summary>The Era this definition describes (also the catalog key).</summary>
        public Era Era { get; }

        /// <summary>Human-readable name for UI display (Req 7.1).</summary>
        public string DisplayName { get; }

        /// <summary>Ids of Technologies that must be completed to advance into this Era (Req 1.4).</summary>
        public IReadOnlyList<string> RequiredTechIds { get; }

        public EraDef(Era era, string displayName, IEnumerable<string> requiredTechIds = null)
        {
            Era = era;
            DisplayName = displayName;
            RequiredTechIds = requiredTechIds?.ToList() ?? new List<string>();
        }

        public override string ToString() => $"Era({Era})";
    }
}
