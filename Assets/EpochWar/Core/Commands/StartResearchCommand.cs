namespace EpochWar.Core.Commands
{
    /// <summary>
    /// A Player's (or AI_Nation's) intent to begin researching a single Technology (Req 1.2).
    ///
    /// The command carries only the issuing Nation and the target Technology id; all validation —
    /// that the Technology exists, is available (prerequisites complete and Era reached, Req 1.3),
    /// is not already completed or in progress, and is affordable (Req 1.2/1.6) — is performed by
    /// the <see cref="EpochWar.Core.Systems.TechSystem"/> handler when this command is dispatched
    /// through the <see cref="CommandRouter"/>. On acceptance the Research cost is deducted and
    /// progress begins accumulating; rejection leaves all tech and resource state untouched.
    /// </summary>
    public sealed class StartResearchCommand : ICommand
    {
        /// <inheritdoc />
        public int IssuingNationId { get; }

        /// <summary>The id of the <see cref="EpochWar.Core.State.Content.TechnologyDef"/> to research.</summary>
        public string TechnologyId { get; }

        public StartResearchCommand(int issuingNationId, string technologyId)
        {
            IssuingNationId = issuingNationId;
            TechnologyId = technologyId;
        }

        public override string ToString()
            => $"StartResearch(nation {IssuingNationId}, tech \"{TechnologyId}\")";
    }
}
