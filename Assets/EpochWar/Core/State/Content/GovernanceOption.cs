using System.Collections.Generic;

namespace EpochWar.Core.State.Content
{
    /// <summary>
    /// An engine-free definition of a governance/civic option a Nation can adopt (Req 5.5).
    ///
    /// When selected, the Civ_System (task 6) applies this option's modifiers to the affected
    /// Resource production and/or Unit attributes. Modifiers are expressed as multiplicative
    /// factors where <c>1.0</c> means "no change":
    /// <list type="bullet">
    ///   <item><see cref="ProductionMultipliers"/> scales per-<see cref="ResourceType"/> production.</item>
    ///   <item><see cref="UnitAttackMultiplier"/> / <see cref="UnitDefenseMultiplier"/> scale combat attributes.</item>
    /// </list>
    /// Storing modifiers as data (rather than code) keeps governance balance authorable and the
    /// Civ_System rules testable in isolation.
    /// </summary>
    public sealed class GovernanceOption
    {
        /// <summary>Stable unique identifier (catalog key).</summary>
        public string Id { get; }

        /// <summary>Human-readable name for UI display (Req 7.1).</summary>
        public string DisplayName { get; }

        /// <summary>
        /// Per-resource production multipliers. A type absent from the map is unaffected
        /// (treated as a factor of 1.0); see <see cref="GetProductionMultiplier"/>.
        /// </summary>
        public IReadOnlyDictionary<ResourceType, float> ProductionMultipliers { get; }

        /// <summary>Multiplier applied to member Units' attack value (1.0 = unchanged).</summary>
        public float UnitAttackMultiplier { get; }

        /// <summary>Multiplier applied to member Units' defense value (1.0 = unchanged).</summary>
        public float UnitDefenseMultiplier { get; }

        public GovernanceOption(
            string id,
            string displayName,
            IReadOnlyDictionary<ResourceType, float> productionMultipliers = null,
            float unitAttackMultiplier = 1f,
            float unitDefenseMultiplier = 1f)
        {
            Id = id;
            DisplayName = displayName;
            ProductionMultipliers = productionMultipliers ?? new Dictionary<ResourceType, float>();
            UnitAttackMultiplier = unitAttackMultiplier;
            UnitDefenseMultiplier = unitDefenseMultiplier;
        }

        /// <summary>
        /// Returns the production multiplier for <paramref name="type"/>, or <c>1.0</c> when
        /// this option does not modify that resource.
        /// </summary>
        public float GetProductionMultiplier(ResourceType type)
        {
            if (ProductionMultipliers != null && ProductionMultipliers.TryGetValue(type, out float factor))
            {
                return factor;
            }

            return 1f;
        }

        public override string ToString() => $"Governance({Id})";
    }
}
