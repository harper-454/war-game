namespace EpochWar.Core.Commands
{
    /// <summary>
    /// A Player's (or AI_Nation's) intent to launch a completed Colony_Ship, beginning its
    /// colonization sequence (Req 11.2, Ascension path).
    ///
    /// The command carries the issuing Nation and the id of the
    /// <see cref="EpochWar.Core.State.UnitInstance"/> Colony_Ship to launch. The
    /// <see cref="EpochWar.Core.Systems.UnitSystem"/> handler validates that the Unit exists, is owned
    /// by the Nation and is a Colony_Ship, that the Nation has reached the Space Era (Req 11.1), and
    /// that the Nation can pay the launch cost; on acceptance it deducts the launch cost and begins
    /// the Nation's colonization sequence, whose completion the Victory_System later resolves as an
    /// Ascension victory (Req 11.2, 11.3). Rejection leaves all state untouched.
    /// </summary>
    public sealed class LaunchColonyShipCommand : ICommand
    {
        /// <inheritdoc />
        public int IssuingNationId { get; }

        /// <summary>The id of the Colony_Ship <see cref="EpochWar.Core.State.UnitInstance"/> to launch.</summary>
        public int ColonyShipUnitId { get; }

        public LaunchColonyShipCommand(int issuingNationId, int colonyShipUnitId)
        {
            IssuingNationId = issuingNationId;
            ColonyShipUnitId = colonyShipUnitId;
        }

        public override string ToString()
            => $"LaunchColonyShip(nation {IssuingNationId}, unit {ColonyShipUnitId})";
    }
}
