using EpochWar.Core.State;

namespace EpochWar.Core.Commands
{
    /// <summary>
    /// A Player's (or AI_Nation's) intent to activate a <see cref="EpochWar.Core.State.Content.UnitAbilityDef"/>
    /// on one of its Units (Req 13.2).
    ///
    /// The command carries the issuing Nation, the target Unit id, the id of the ability to
    /// activate (matched against that Unit type's <c>UnitDef.AbilityDefs</c>), and an optional
    /// target position for abilities that require one (<c>null</c> for self/no-target abilities).
    /// The Unit_System handler validates that the ability exists on the Unit, that its cooldown
    /// has fully elapsed, and that the owning Nation can afford its cost; on acceptance it
    /// executes the effect, deducts the cost, and starts the cooldown, otherwise it rejects with
    /// a reason distinguishing an active cooldown from insufficient resources and leaves all
    /// state unchanged (Req 13.2, 13.3).
    /// </summary>
    public sealed class ActivateAbilityCommand : ICommand
    {
        /// <inheritdoc />
        public int IssuingNationId { get; }

        /// <summary>The id of the Unit whose ability is being activated (Req 13.2).</summary>
        public int UnitId { get; }

        /// <summary>The id of the ability to activate, matched against the Unit type's defined list (Req 13.1).</summary>
        public string AbilityId { get; }

        /// <summary>The target position for targeted abilities, or <c>null</c> for self/no-target abilities.</summary>
        public WorldPosition? TargetPosition { get; }

        public ActivateAbilityCommand(int issuingNationId, int unitId, string abilityId, WorldPosition? targetPosition = null)
        {
            IssuingNationId = issuingNationId;
            UnitId = unitId;
            AbilityId = abilityId;
            TargetPosition = targetPosition;
        }

        public override string ToString()
            => $"ActivateAbility(nation {IssuingNationId}, unit {UnitId}, ability \"{AbilityId}\")";
    }
}
