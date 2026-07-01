using EpochWar.Core.Math;

namespace EpochWar.Core.State.Content
{
    /// <summary>
    /// An engine-free definition of a buildable Structure type (Req 4, 10).
    ///
    /// Plain-C# counterpart of the authored <c>StructureDef</c> ScriptableObject. The design's
    /// <c>Vector2Int Footprint</c> is represented here as two integers
    /// (<see cref="FootprintWidth"/> along terrain X, <see cref="FootprintLength"/> along
    /// terrain Z) so the type stays free of <c>UnityEngine</c>. The footprint is consumed by
    /// terrain occupancy/validity checks and support queries (Req 4.1, 4.2, 6.4).
    ///
    /// <see cref="IsPeaceArch"/> tags the single wonder whose completion satisfies the Peace
    /// victory condition (Req 10); availability of that structure is gated by prerequisite
    /// technologies handled in the Tech_/Base_ systems (Req 10.1).
    /// </summary>
    public sealed class StructureDef
    {
        /// <summary>Stable unique identifier (catalog key).</summary>
        public string Id { get; }

        /// <summary>The Era at which this Structure type unlocks (Req 1.5, 4.6).</summary>
        public Era Era { get; }

        /// <summary>Resource cost deducted when placement begins construction (Req 4.1).</summary>
        public ResourceCost Cost { get; }

        /// <summary>Simulation seconds required for construction to complete (Req 4.3).</summary>
        public float BuildTimeSeconds { get; }

        /// <summary>Population required to construct this Structure; checked against availability (Req 5.4).</summary>
        public int PopulationCost { get; }

        /// <summary>Maximum (and starting) health for instances of this Structure (Req 4.5).</summary>
        public int MaxHealth { get; }

        /// <summary>Number of terrain cells occupied along the X axis.</summary>
        public int FootprintWidth { get; }

        /// <summary>Number of terrain cells occupied along the Z axis.</summary>
        public int FootprintLength { get; }

        /// <summary>The function enabled once the Structure is operational (Req 4.3, 4.4).</summary>
        public StructureFunction Function { get; }

        /// <summary>True only for the Peace_Arch wonder (Req 10).</summary>
        public bool IsPeaceArch { get; }

        /// <summary>
        /// The distance within which this Structure grants vision to its owning Nation (Req 14.1).
        /// Defaults to <see cref="Fixed.Zero"/>.
        /// </summary>
        public Fixed SightRadius { get; }

        /// <summary>
        /// Era-derived default or content-author override determining visual representation
        /// richness (Req 7). Presentation-only; consumed by the Unity Entity_View_System.
        /// </summary>
        public int VisualDetailTier { get; }

        public StructureDef(
            string id,
            Era era,
            ResourceCost cost,
            float buildTimeSeconds,
            int populationCost,
            int maxHealth,
            int footprintWidth,
            int footprintLength,
            StructureFunction function,
            bool isPeaceArch = false,
            Fixed sightRadius = default,
            int visualDetailTier = 0)
        {
            Id = id;
            Era = era;
            Cost = cost;
            BuildTimeSeconds = buildTimeSeconds;
            PopulationCost = populationCost;
            MaxHealth = maxHealth;
            FootprintWidth = footprintWidth;
            FootprintLength = footprintLength;
            Function = function;
            IsPeaceArch = isPeaceArch;
            SightRadius = sightRadius;
            VisualDetailTier = visualDetailTier;
        }

        public override string ToString() => $"Structure({Id}, {Era}, {Function})";
    }
}
