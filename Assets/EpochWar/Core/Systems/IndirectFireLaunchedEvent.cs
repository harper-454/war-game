using EpochWar.Core.Commands;
using EpochWar.Core.Math;
using EpochWar.Core.State;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that an Indirect_Fire command was accepted and an
    /// in-flight projectile enqueued by the <see cref="CombatSystem"/> (Req 15.2, 15.5).
    ///
    /// The <see cref="CombatSystem"/>'s <see cref="IndirectFireCommand"/> handler emits this on an
    /// accepted launch, before the projectile's flight delay elapses. It carries the issuing Nation,
    /// the firing Artillery_Unit, the targeted world-space location, the flight delay after which the
    /// attack will resolve, and the Area_Effect radius the resolution will use (0 = single-target).
    /// It exists so the Unity VFX_System can render the arcing projectile trail for the full flight
    /// delay, visible to all Nations regardless of Spotting (Req 15.7), without polling Core internals.
    /// </summary>
    public sealed class IndirectFireLaunchedEvent : GameEvent
    {
        /// <summary>The id of the Nation that issued the Indirect_Fire command.</summary>
        public int NationId { get; }

        /// <summary>The id of the firing Artillery_Unit.</summary>
        public int ArtilleryUnitId { get; }

        /// <summary>The world-space location targeted by the Indirect_Fire attack.</summary>
        public WorldPosition TargetLocation { get; }

        /// <summary>The flight delay after which the attack resolves at the target location (Req 15.5).</summary>
        public Fixed FlightDelay { get; }

        /// <summary>The Area_Effect radius the resolution will apply (0 = single-target, Req 15.6).</summary>
        public Fixed AreaEffectRadius { get; }

        public IndirectFireLaunchedEvent(
            int nationId,
            int artilleryUnitId,
            WorldPosition targetLocation,
            Fixed flightDelay,
            Fixed areaEffectRadius)
        {
            NationId = nationId;
            ArtilleryUnitId = artilleryUnitId;
            TargetLocation = targetLocation;
            FlightDelay = flightDelay;
            AreaEffectRadius = areaEffectRadius;
        }

        public override string ToString()
            => $"IndirectFireLaunched(nation {NationId}, artillery #{ArtilleryUnitId}, "
               + $"target {TargetLocation}, delay {FlightDelay}, aoe {AreaEffectRadius})";
    }
}
