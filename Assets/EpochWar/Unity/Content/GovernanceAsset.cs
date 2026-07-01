using System.Collections.Generic;
using UnityEngine;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;

namespace EpochWar.Unity.Content
{
    /// <summary>
    /// A ScriptableObject authoring wrapper for a <see cref="GovernanceOption"/> (Req 5.5).
    ///
    /// One asset describes a governance/civic option a Nation can adopt and the multiplicative
    /// modifiers it applies when selected: per-<see cref="ResourceType"/> production multipliers plus
    /// Unit attack/defense multipliers (<c>1.0</c> means unchanged). <see cref="ToCore"/> builds the
    /// engine-free <see cref="GovernanceOption"/> the Civ_System consumes.
    /// </summary>
    [CreateAssetMenu(menuName = "EpochWar/Governance", fileName = "Governance")]
    public sealed class GovernanceAsset : ScriptableObject
    {
        /// <summary>One authored production multiplier row: a resource type and its multiplier.</summary>
        [System.Serializable]
        public struct ProductionMultiplier
        {
            [Tooltip("The resource type whose production is scaled.")]
            public ResourceType Type;

            [Tooltip("Multiplicative factor applied to that resource's production (1.0 = unchanged).")]
            public float Multiplier;
        }

        [Tooltip("Stable unique identifier; also the catalog key.")]
        [SerializeField] private string _id = string.Empty;

        [Tooltip("Human-readable name shown in the UI.")]
        [SerializeField] private string _displayName = string.Empty;

        [Tooltip("Per-resource production multipliers. Types not listed are unaffected (factor 1.0).")]
        [SerializeField] private List<ProductionMultiplier> _productionMultipliers = new List<ProductionMultiplier>();

        [Tooltip("Multiplier applied to member units' attack value (1.0 = unchanged).")]
        [SerializeField] private float _unitAttackMultiplier = 1f;

        [Tooltip("Multiplier applied to member units' defense value (1.0 = unchanged).")]
        [SerializeField] private float _unitDefenseMultiplier = 1f;

        /// <summary>Stable unique identifier (catalog key).</summary>
        public string Id => _id;

        /// <summary>Human-readable display name for UI.</summary>
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? _id : _displayName;

        /// <summary>Converts this authored asset into its engine-free <see cref="GovernanceOption"/>.</summary>
        public GovernanceOption ToCore()
        {
            var map = new Dictionary<ResourceType, float>();
            if (_productionMultipliers != null)
            {
                foreach (var entry in _productionMultipliers)
                {
                    // Last write wins on duplicate rows; keeps the converter total and predictable.
                    map[entry.Type] = entry.Multiplier;
                }
            }

            return new GovernanceOption(
                _id,
                DisplayName,
                map,
                _unitAttackMultiplier,
                _unitDefenseMultiplier);
        }
    }
}
