using System.Collections.Generic;
using UnityEngine;
using EpochWar.Core.State;

namespace EpochWar.Unity.Content
{
    /// <summary>
    /// An inspector-authorable counterpart of the engine-free <see cref="ResourceCost"/> struct.
    ///
    /// The <see cref="ResourceCost"/> in <c>EpochWar.Core</c> is an immutable, sparse value type with
    /// no public fields, so it cannot be edited directly in the Unity inspector. This serializable
    /// class lets content authors specify a cost as a simple list of (type, amount) rows on a
    /// ScriptableObject, and <see cref="ToCore"/> converts those rows into the normalized core cost
    /// consumed by the systems (Req 1.2, 2.3, 3.1, 4.1). Rows with a non-positive amount are dropped
    /// by <see cref="ResourceCost.Of"/>, and duplicate types are summed, so authoring mistakes degrade
    /// gracefully.
    /// </summary>
    [System.Serializable]
    public sealed class ResourceCostAuthoring
    {
        /// <summary>One authored cost component: a resource type and the amount required of it.</summary>
        [System.Serializable]
        public struct Entry
        {
            [Tooltip("The resource type this cost component consumes.")]
            public ResourceType Type;

            [Tooltip("The amount of the resource required. Non-positive amounts are ignored.")]
            public float Amount;
        }

        [SerializeField]
        [Tooltip("The individual (resource, amount) components that make up this cost.")]
        private List<Entry> _entries = new List<Entry>();

        /// <summary>The authored cost components (never null).</summary>
        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>
        /// Converts the authored rows into a normalized engine-free <see cref="ResourceCost"/>.
        /// Returns <see cref="ResourceCost.Free"/> when no positive components are present.
        /// </summary>
        public ResourceCost ToCore()
        {
            if (_entries == null || _entries.Count == 0)
            {
                return ResourceCost.Free;
            }

            var components = new (ResourceType Type, float Amount)[_entries.Count];
            for (int i = 0; i < _entries.Count; i++)
            {
                components[i] = (_entries[i].Type, _entries[i].Amount);
            }

            return ResourceCost.Of(components);
        }
    }
}
