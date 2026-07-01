using EpochWar.Core.Commands;
using EpochWar.Core.Math;
using EpochWar.Core.State;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that an in-flight Indirect_Fire projectile reached its
    /// target location and resolved, after its flight delay elapsed (Req 15.5, 15.6).
    ///
    /// The <see cref="CombatSystem"/>'s <see cref="CombatSystem.Tick"/> emits this the moment a
    /// pending projectile's remaining flight time reaches zero, immediately before the resulting
    /// <see cref="CombatResolvedEvent"/>/<see cref="StructureCombatResolvedEvent"/> damage events for
    /// that impact. It carries the firing Artillery_Unit's id (as recorded at launch — the Unit may
    /// have been removed during flight), the impact world-space location, and the Area_Effect radius
    /// applied (0 = single-target). The Unity VFX_System keys the impact explosion off this event.
    /// </summary>
    public sealed class IndirectFireImpactEvent : GameEvent
    {
        /// <summary>The id of the Artillery_Unit that fired, recorded at launch time.</summary>
        public int ArtilleryUnitId { get; }

        /// <summary>The world-space location where the projectile resolved.</summary>
        public WorldPosition TargetLocation { get; }

        /// <summary>The Area_Effect radius applied at resolution (0 = single-target, Req 15.6).</summary>
        public Fixed AreaEffectRadius { get; }

        public IndirectFireImpactEvent(int artilleryUnitId, WorldPosition targetLocation, Fixed areaEffectRadius)
        {
            ArtilleryUnitId = artilleryUnitId;
            TargetLocation = targetLocation;
            AreaEffectRadius = areaEffectRadius;
        }

        public override string ToString()
            => $"IndirectFireImpact(artillery #{ArtilleryUnitId}, target {TargetLocation}, aoe {AreaEffectRadius})";
    }
}
