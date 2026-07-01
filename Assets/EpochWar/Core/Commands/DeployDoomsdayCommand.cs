namespace EpochWar.Core.Commands
{
    /// <summary>
    /// A Player's (or AI_Nation's) intent to deploy a completed Doomsday_Weapon against an opposing
    /// Nation (Req 9.2, Annihilation path).
    ///
    /// The command carries the issuing Nation, the id of the completed
    /// <see cref="EpochWar.Core.State.Content.TechnologyDef"/> classified as a
    /// <see cref="EpochWar.Core.State.Content.TechCategory.DoomsdayWeapon"/>, and the id of the
    /// targeted opposing Nation. The <see cref="EpochWar.Core.Systems.UnitSystem"/> handler validates
    /// that the weapon's research is complete, that the target is a distinct, not-yet-eliminated
    /// Nation, and that the Nation can pay the weapon's deployment cost; on acceptance it deducts the
    /// deployment cost and executes the weapon's defined elimination effect against the target,
    /// removing the target's forces and marking it eliminated (Req 9.2, 9.3). Rejection leaves all
    /// state untouched.
    /// </summary>
    public sealed class DeployDoomsdayCommand : ICommand
    {
        /// <inheritdoc />
        public int IssuingNationId { get; }

        /// <summary>The id of the completed Doomsday_Weapon Technology to deploy (Req 9.1, 9.2).</summary>
        public string TechnologyId { get; }

        /// <summary>The id of the opposing Nation targeted by the elimination effect (Req 9.2).</summary>
        public int TargetNationId { get; }

        public DeployDoomsdayCommand(int issuingNationId, string technologyId, int targetNationId)
        {
            IssuingNationId = issuingNationId;
            TechnologyId = technologyId;
            TargetNationId = targetNationId;
        }

        public override string ToString()
            => $"DeployDoomsday(nation {IssuingNationId}, weapon \"{TechnologyId}\", target {TargetNationId})";
    }
}
