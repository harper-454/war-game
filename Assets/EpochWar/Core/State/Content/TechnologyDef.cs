using System.Collections.Generic;
using System.Linq;

namespace EpochWar.Core.State.Content
{
    /// <summary>
    /// An engine-free definition of a researchable Technology (Req 1).
    ///
    /// This is the plain-C# counterpart of the authored <c>TechnologyDef</c> ScriptableObject
    /// described in the design. Because <see cref="EpochWar.Core"/> has no reference to
    /// <c>UnityEngine</c>, cross-references to other definitions are stored as string ids and
    /// resolved through <see cref="ICatalog"/> rather than as direct object references. The
    /// Unity content layer (task 15.3) converts authored ScriptableObjects into these POCOs.
    ///
    /// Definitions are immutable so a single catalog instance can be shared safely across the
    /// simulation and tests without any handler mutating shared content.
    /// </summary>
    public sealed class TechnologyDef
    {
        /// <summary>Stable unique identifier (catalog key).</summary>
        public string Id { get; }

        /// <summary>The Era at which this Technology becomes researchable (Req 1.1, 9.1, 11.1).</summary>
        public Era Era { get; }

        /// <summary>The Research-resource cost to complete this Technology (Req 1.2, 1.6).</summary>
        public ResourceCost ResearchCost { get; }

        /// <summary>Ids of Technologies that must be completed before this one is available (Req 1.3).</summary>
        public IReadOnlyList<string> Prerequisites { get; }

        /// <summary>Ids of <see cref="UnitDef"/>s unlocked when this Technology / its Era is reached (Req 1.5).</summary>
        public IReadOnlyList<string> UnlocksUnits { get; }

        /// <summary>Ids of <see cref="StructureDef"/>s unlocked when this Technology / its Era is reached (Req 1.5).</summary>
        public IReadOnlyList<string> UnlocksStructures { get; }

        /// <summary>Resource types unlocked when this Technology / its Era is reached (Req 1.5).</summary>
        public IReadOnlyList<ResourceType> UnlocksResources { get; }

        /// <summary>Victory-path classification used for extra gating (Req 9.1, 10.1, 11.1).</summary>
        public TechCategory Category { get; }

        /// <summary>
        /// The Resource cost paid to <em>deploy</em> a completed <see cref="TechCategory.DoomsdayWeapon"/>
        /// against a target Nation, distinct from the <see cref="ResearchCost"/> paid to research it
        /// (Req 9.2). Defaults to <see cref="ResourceCost.Free"/> for ordinary technologies, which have
        /// no deployment step.
        /// </summary>
        public ResourceCost DeploymentCost { get; }

        public TechnologyDef(
            string id,
            Era era,
            ResourceCost researchCost,
            IEnumerable<string> prerequisites = null,
            IEnumerable<string> unlocksUnits = null,
            IEnumerable<string> unlocksStructures = null,
            IEnumerable<ResourceType> unlocksResources = null,
            TechCategory category = TechCategory.Normal,
            ResourceCost deploymentCost = default)
        {
            Id = id;
            Era = era;
            ResearchCost = researchCost;
            Prerequisites = prerequisites?.ToList() ?? new List<string>();
            UnlocksUnits = unlocksUnits?.ToList() ?? new List<string>();
            UnlocksStructures = unlocksStructures?.ToList() ?? new List<string>();
            UnlocksResources = unlocksResources?.ToList() ?? new List<ResourceType>();
            Category = category;
            DeploymentCost = deploymentCost;
        }

        public override string ToString() => $"Tech({Id}, {Era}, {Category})";
    }
}
