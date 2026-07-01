using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;

namespace EpochWar.Unity.Content
{
    /// <summary>
    /// A ScriptableObject authoring wrapper for a <see cref="TechnologyDef"/> (Req 1, 9.1, 10.1, 11.1).
    ///
    /// One asset describes one researchable Technology. Cross-references (prerequisites and the
    /// Units/Structures it unlocks) are authored as direct asset references for inspector convenience,
    /// and <see cref="ToCore"/> flattens them to the string ids the engine-free
    /// <see cref="TechnologyDef"/> uses (the core resolves them through <see cref="ICatalog"/>). The
    /// <see cref="TechCategory"/> drives the extra victory-path gating — Doomsday weapon (Req 9.1),
    /// Peace Arch prerequisite (Req 10.1), Colony Ship (Req 11.1) — and <see cref="_deploymentCost"/>
    /// is the cost paid to deploy a completed Doomsday weapon, distinct from its research cost
    /// (Req 9.2).
    /// </summary>
    [CreateAssetMenu(menuName = "EpochWar/Technology", fileName = "Technology")]
    public sealed class TechnologyAsset : ScriptableObject
    {
        [Tooltip("Stable unique identifier; also the catalog key. Must be unique across all technologies.")]
        [SerializeField] private string _id = string.Empty;

        [Tooltip("The era at which this technology becomes researchable.")]
        [SerializeField] private Era _era = Era.Prehistoric;

        [Tooltip("The Research-resource cost to complete this technology.")]
        [SerializeField] private ResourceCostAuthoring _researchCost = new ResourceCostAuthoring();

        [Tooltip("Technologies that must be completed before this one is available.")]
        [SerializeField] private List<TechnologyAsset> _prerequisites = new List<TechnologyAsset>();

        [Tooltip("Unit types unlocked when this technology / its era is reached.")]
        [SerializeField] private List<UnitAsset> _unlocksUnits = new List<UnitAsset>();

        [Tooltip("Structure types unlocked when this technology / its era is reached.")]
        [SerializeField] private List<StructureAsset> _unlocksStructures = new List<StructureAsset>();

        [Tooltip("Resource types unlocked when this technology / its era is reached.")]
        [SerializeField] private List<ResourceType> _unlocksResources = new List<ResourceType>();

        [Tooltip("Victory-path classification used for extra gating (Doomsday/PeaceArch/ColonyShip).")]
        [SerializeField] private TechCategory _category = TechCategory.Normal;

        [Tooltip("Resource cost paid to DEPLOY a completed Doomsday weapon (Req 9.2). Free for others.")]
        [SerializeField] private ResourceCostAuthoring _deploymentCost = new ResourceCostAuthoring();

        /// <summary>Stable unique identifier (catalog key).</summary>
        public string Id => _id;

        /// <summary>The era at which this technology becomes researchable.</summary>
        public Era Era => _era;

        /// <summary>The victory-path classification of this technology.</summary>
        public TechCategory Category => _category;

        /// <summary>Converts this authored asset into its engine-free <see cref="TechnologyDef"/>.</summary>
        public TechnologyDef ToCore()
        {
            IEnumerable<string> prereqIds = (_prerequisites ?? new List<TechnologyAsset>())
                .Where(t => t != null).Select(t => t.Id);
            IEnumerable<string> unitIds = (_unlocksUnits ?? new List<UnitAsset>())
                .Where(u => u != null).Select(u => u.Id);
            IEnumerable<string> structureIds = (_unlocksStructures ?? new List<StructureAsset>())
                .Where(s => s != null).Select(s => s.Id);
            IEnumerable<ResourceType> resourceTypes = _unlocksResources ?? new List<ResourceType>();

            return new TechnologyDef(
                _id,
                _era,
                _researchCost != null ? _researchCost.ToCore() : ResourceCost.Free,
                prereqIds,
                unitIds,
                structureIds,
                resourceTypes,
                _category,
                _deploymentCost != null ? _deploymentCost.ToCore() : ResourceCost.Free);
        }
    }
}
