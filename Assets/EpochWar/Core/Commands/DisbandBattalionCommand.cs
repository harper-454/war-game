namespace EpochWar.Core.Commands
{
    /// <summary>
    /// A Player's (or AI_Nation's) intent to disband one of the Nation's Battalions (Req 3.3).
    ///
    /// The command carries the issuing Nation and the id of the Battalion to disband. The
    /// <see cref="EpochWar.Core.Systems.UnitSystem"/> handler removes the Battalion from the Nation
    /// and clears the Battalion assignment on each of its surviving member Units, which otherwise
    /// remain in the Match. Disbanding is the Player-initiated end of a Battalion's membership
    /// referenced by Req 3.3. An unknown Battalion id results in rejection with no state change.
    /// </summary>
    public sealed class DisbandBattalionCommand : ICommand
    {
        /// <inheritdoc />
        public int IssuingNationId { get; }

        /// <summary>The id of the Battalion to disband.</summary>
        public int BattalionId { get; }

        public DisbandBattalionCommand(int issuingNationId, int battalionId)
        {
            IssuingNationId = issuingNationId;
            BattalionId = battalionId;
        }

        public override string ToString()
            => $"DisbandBattalion(nation {IssuingNationId}, battalion {BattalionId})";
    }
}
