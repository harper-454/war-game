using EpochWar.Core.State;

namespace EpochWar.Core.Commands
{
    /// <summary>
    /// A Player's (or AI_Nation's) intent to have an Artillery_Unit bombard a location with
    /// Indirect_Fire (Req 15.1, 15.2).
    ///
    /// The command carries the issuing Nation, the id of the firing Artillery_Unit, and the
    /// world-space target location. The Combat_System handler validates that the Unit is an
    /// Artillery_Unit, that the target is beyond direct-fire range and within maximum
    /// Indirect_Fire range, and that the issuing Nation currently has Spotting on the target; on
    /// acceptance it enqueues an in-flight projectile that resolves after the Unit's flight delay
    /// (regardless of later Spotting loss), otherwise it rejects with a reason distinguishing an
    /// out-of-range target from a no-Spotting target and leaves all state unchanged
    /// (Req 15.2, 15.3, 15.4, 15.5).
    /// </summary>
    public sealed class IndirectFireCommand : ICommand
    {
        /// <inheritdoc />
        public int IssuingNationId { get; }

        /// <summary>The id of the firing Artillery_Unit (Req 15.1).</summary>
        public int ArtilleryUnitId { get; }

        /// <summary>The world-space location targeted by the Indirect_Fire attack (Req 15.2).</summary>
        public WorldPosition TargetLocation { get; }

        public IndirectFireCommand(int issuingNationId, int artilleryUnitId, WorldPosition targetLocation)
        {
            IssuingNationId = issuingNationId;
            ArtilleryUnitId = artilleryUnitId;
            TargetLocation = targetLocation;
        }

        public override string ToString()
            => $"IndirectFire(nation {IssuingNationId}, artillery {ArtilleryUnitId}, target {TargetLocation})";
    }
}
