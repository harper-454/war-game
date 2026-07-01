namespace EpochWar.Core.Commands
{
    /// <summary>
    /// A Player's (or AI_Nation's) intent to advance their Nation to the next Era (Req 1.4).
    ///
    /// The advancement action is only enabled once the Nation's completed Technology set contains
    /// every Technology required for the next Era; the <see cref="EpochWar.Core.Systems.TechSystem"/>
    /// handler re-validates that gate when this command is dispatched. On acceptance the Nation's
    /// <see cref="EpochWar.Core.State.Nation.CurrentEra"/> advances by one stage and every Unit,
    /// Structure, and Resource type designated for the new Era becomes unlocked (Req 1.5).
    /// </summary>
    public sealed class AdvanceEraCommand : ICommand
    {
        /// <inheritdoc />
        public int IssuingNationId { get; }

        public AdvanceEraCommand(int issuingNationId)
        {
            IssuingNationId = issuingNationId;
        }

        public override string ToString() => $"AdvanceEra(nation {IssuingNationId})";
    }
}
