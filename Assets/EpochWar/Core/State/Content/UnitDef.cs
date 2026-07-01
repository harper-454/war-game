using System.Collections.Generic;
using EpochWar.Core.Math;

namespace EpochWar.Core.State.Content
{
    /// <summary>
    /// An engine-free definition of a recruitable Unit type (Req 3).
    ///
    /// Mirrors the authored <c>UnitDef</c> ScriptableObject from the design as a plain POCO so
    /// the Unit_System and Combat resolution can run under EditMode property tests with no
    /// Unity Play loop. The detailed attributes here (health, attack, defense, move speed,
    /// Era of origin) satisfy the per-Unit attribute schema required by Req 3.6, and back the
    /// combat formula in Req 3.7.
    ///
    /// Immutable: a recruited <c>UnitInstance</c> (task 2.2) references the shared
    /// <see cref="UnitDef"/> and tracks only its own mutable runtime state (health, position).
    /// </summary>
    public sealed class UnitDef
    {
        /// <summary>Stable unique identifier (catalog key).</summary>
        public string Id { get; }

        /// <summary>The Era of origin for this Unit type (Req 3.6).</summary>
        public Era Era { get; }

        /// <summary>Resource cost deducted when recruitment is issued (Req 3.1).</summary>
        public ResourceCost Cost { get; }

        /// <summary>Simulation seconds that must elapse before the Unit is produced (Req 3.1).</summary>
        public float BuildTimeSeconds { get; }

        /// <summary>Population required to field this Unit; checked against availability (Req 5.4).</summary>
        public int PopulationCost { get; }

        /// <summary>Maximum (and starting) health for instances of this Unit (Req 3.6).</summary>
        public int MaxHealth { get; }

        /// <summary>Attack value used by the combat damage formula (Req 3.6, 3.7).</summary>
        public int Attack { get; }

        /// <summary>Defense value used by the combat damage formula (Req 3.6, 3.7).</summary>
        public int Defense { get; }

        /// <summary>Movement speed in cells per second over the navigation grid (Req 3.2, 3.6).</summary>
        public float MoveSpeed { get; }

        /// <summary>Functional role; influences pathfinding and special victory behavior.</summary>
        public UnitRole Role { get; }

        /// <summary>
        /// The Resource cost paid to <em>launch</em> a completed <see cref="UnitRole.ColonyShip"/> to
        /// begin its colonization sequence, distinct from the <see cref="Cost"/> paid to recruit it
        /// (Req 11.2). Defaults to <see cref="ResourceCost.Free"/> for units that have no launch step.
        /// </summary>
        public ResourceCost LaunchCost { get; }

        /// <summary>
        /// The distance within which this Unit grants vision to its owning Nation (Req 14.1).
        /// Defaults to <see cref="Fixed.Zero"/>.
        /// </summary>
        public Fixed SightRadius { get; }

        /// <summary>The activatable abilities available to Units of this type (Req 13.1). Never null.</summary>
        public List<UnitAbilityDef> AbilityDefs { get; }

        /// <summary>
        /// The Veterancy_Curve for this Unit type, ordered ascending by
        /// <see cref="VeterancyTierDef.ExperienceThreshold"/> (Req 12.2). Never null.
        /// </summary>
        public List<VeterancyTierDef> VeterancyCurve { get; }

        /// <summary>True when this Unit type is an Artillery_Unit capable of Indirect_Fire (Req 15.1).</summary>
        public bool IsArtillery { get; }

        /// <summary>Maximum Indirect_Fire range; unused (0) when <see cref="IsArtillery"/> is false (Req 15.1, 15.2).</summary>
        public Fixed IndirectFireRange { get; }

        /// <summary>Range below which direct fire applies instead of Indirect_Fire (Req 15.1).</summary>
        public Fixed DirectFireRange { get; }

        /// <summary>Seconds between an accepted Indirect_Fire command and its impact (Req 15.5).</summary>
        public Fixed IndirectFireFlightDelay { get; }

        /// <summary>Area_Effect radius; 0 = single-target, &gt;0 = Area_Effect damage (Req 11, 15.6).</summary>
        public Fixed AreaEffectRadius { get; }

        /// <summary>
        /// Era-derived default or content-author override determining visual representation
        /// richness (Req 7). Presentation-only; consumed by the Unity Entity_View_System.
        /// </summary>
        public int VisualDetailTier { get; }

        public UnitDef(
            string id,
            Era era,
            ResourceCost cost,
            float buildTimeSeconds,
            int populationCost,
            int maxHealth,
            int attack,
            int defense,
            float moveSpeed,
            UnitRole role,
            ResourceCost launchCost = default,
            Fixed sightRadius = default,
            List<UnitAbilityDef> abilityDefs = null,
            List<VeterancyTierDef> veterancyCurve = null,
            bool isArtillery = false,
            Fixed indirectFireRange = default,
            Fixed directFireRange = default,
            Fixed indirectFireFlightDelay = default,
            Fixed areaEffectRadius = default,
            int visualDetailTier = 0)
        {
            Id = id;
            Era = era;
            Cost = cost;
            BuildTimeSeconds = buildTimeSeconds;
            PopulationCost = populationCost;
            MaxHealth = maxHealth;
            Attack = attack;
            Defense = defense;
            MoveSpeed = moveSpeed;
            Role = role;
            LaunchCost = launchCost;
            SightRadius = sightRadius;
            AbilityDefs = abilityDefs ?? new List<UnitAbilityDef>();
            VeterancyCurve = veterancyCurve ?? new List<VeterancyTierDef>();
            IsArtillery = isArtillery;
            IndirectFireRange = indirectFireRange;
            DirectFireRange = directFireRange;
            IndirectFireFlightDelay = indirectFireFlightDelay;
            AreaEffectRadius = areaEffectRadius;
            VisualDetailTier = visualDetailTier;
        }

        public override string ToString() => $"Unit({Id}, {Era}, {Role})";
    }
}
