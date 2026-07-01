using EpochWar.Core.Commands;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that a Nation adopted a governance/civic option whose
    /// modifiers now apply to its Resource production and/or Unit attributes (Req 5.5).
    ///
    /// The <see cref="CivSystem"/> emits this when <see cref="CivSystem.ApplyGovernance"/> adds an
    /// option to a Nation's active set, so the networking layer can replicate the change and the UI
    /// can refresh any modifier-dependent displays (Req 7.4). It reports state that has
    /// <em>already</em> been applied.
    /// </summary>
    public sealed class GovernanceChangedEvent : GameEvent
    {
        /// <summary>The id of the Nation that adopted the governance option.</summary>
        public int NationId { get; }

        /// <summary>The id of the governance option now active.</summary>
        public string GovernanceOptionId { get; }

        public GovernanceChangedEvent(int nationId, string governanceOptionId)
        {
            NationId = nationId;
            GovernanceOptionId = governanceOptionId;
        }

        public override string ToString()
            => $"GovernanceChanged(nation {NationId}, \"{GovernanceOptionId}\")";
    }
}
