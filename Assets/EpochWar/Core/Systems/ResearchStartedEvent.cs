using EpochWar.Core.Commands;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that a Nation began researching a Technology — its
    /// Research cost has been deducted and progress has started accumulating (Req 1.2).
    ///
    /// The <see cref="TechSystem"/> emits this when a <see cref="StartResearchCommand"/> is accepted,
    /// alongside the <see cref="ResourceChangedEvent"/>s produced by the cost deduction, so the
    /// networking layer can replicate the started research and the UI can reflect that the
    /// Technology is now in progress (Req 7.4). It reports state that has <em>already</em> been
    /// applied.
    /// </summary>
    public sealed class ResearchStartedEvent : GameEvent
    {
        /// <summary>The id of the Nation that started the research.</summary>
        public int NationId { get; }

        /// <summary>The id of the Technology now in progress.</summary>
        public string TechnologyId { get; }

        /// <summary>
        /// The simulation seconds of accumulated progress required before the Technology completes.
        /// </summary>
        public float DurationSeconds { get; }

        public ResearchStartedEvent(int nationId, string technologyId, float durationSeconds)
        {
            NationId = nationId;
            TechnologyId = technologyId;
            DurationSeconds = durationSeconds;
        }

        public override string ToString()
            => $"ResearchStarted(nation {NationId}, tech \"{TechnologyId}\", {DurationSeconds:0.##}s)";
    }
}
