using System;
using System.Collections.Generic;
using System.Linq;

namespace EpochWar.Core.State
{
    /// <summary>
    /// An immutable bundle of per-<see cref="ResourceType"/> amounts representing the
    /// cost of an action (research, recruitment, construction, deployment, launch).
    ///
    /// Costs are central to several requirements: affordability checks and atomic
    /// deduction (Req 2.3/2.4), research cost (Req 1.2/1.6), unit/structure cost
    /// (Req 3.1/4.1). This type only models the *amounts*; the actual deduction and
    /// rejection logic lives in the ResourceSystem (task 4). Keeping it immutable
    /// guarantees a cost definition can be shared safely and never mutated by a handler.
    /// </summary>
    public readonly struct ResourceCost : IEquatable<ResourceCost>
    {
        // Sparse: only non-zero components are stored. Null is treated as empty.
        private readonly IReadOnlyDictionary<ResourceType, float> _amounts;

        /// <summary>An empty cost (free action).</summary>
        public static readonly ResourceCost Free = new ResourceCost(null);

        private ResourceCost(IReadOnlyDictionary<ResourceType, float> amounts)
        {
            _amounts = amounts;
        }

        /// <summary>
        /// Builds a cost from explicit (type, amount) pairs. Non-positive amounts are
        /// ignored, and duplicate types are summed, so the result is always normalized.
        /// </summary>
        public static ResourceCost Of(params (ResourceType Type, float Amount)[] components)
        {
            if (components == null || components.Length == 0)
            {
                return Free;
            }

            var map = new Dictionary<ResourceType, float>();
            foreach (var (type, amount) in components)
            {
                if (amount <= 0f)
                {
                    continue;
                }

                map.TryGetValue(type, out float existing);
                map[type] = existing + amount;
            }

            return map.Count == 0 ? Free : new ResourceCost(map);
        }

        /// <summary>Builds a single-resource cost.</summary>
        public static ResourceCost Single(ResourceType type, float amount)
            => Of((type, amount));

        /// <summary>True when this cost requires nothing.</summary>
        public bool IsFree => _amounts == null || _amounts.Count == 0;

        /// <summary>The resource types with a non-zero required amount.</summary>
        public IEnumerable<ResourceType> Types
            => _amounts == null ? Enumerable.Empty<ResourceType>() : _amounts.Keys;

        /// <summary>The required amount for a given type (zero if not part of this cost).</summary>
        public float AmountOf(ResourceType type)
        {
            if (_amounts != null && _amounts.TryGetValue(type, out float amount))
            {
                return amount;
            }

            return 0f;
        }

        /// <summary>Enumerates the non-zero (type, amount) components of this cost.</summary>
        public IEnumerable<KeyValuePair<ResourceType, float>> Components
            => _amounts ?? Enumerable.Empty<KeyValuePair<ResourceType, float>>();

        /// <summary>
        /// Returns the component-wise sum of this cost and <paramref name="other"/>.
        /// </summary>
        public ResourceCost Add(ResourceCost other)
        {
            var map = new Dictionary<ResourceType, float>();
            foreach (var kvp in Components)
            {
                map[kvp.Key] = kvp.Value;
            }

            foreach (var kvp in other.Components)
            {
                map.TryGetValue(kvp.Key, out float existing);
                map[kvp.Key] = existing + kvp.Value;
            }

            return map.Count == 0 ? Free : new ResourceCost(map);
        }

        /// <summary>
        /// Returns this cost with every component multiplied by <paramref name="factor"/>.
        /// A non-positive factor yields a free cost.
        /// </summary>
        public ResourceCost Scale(float factor)
        {
            if (factor <= 0f || IsFree)
            {
                return Free;
            }

            var map = new Dictionary<ResourceType, float>();
            foreach (var kvp in Components)
            {
                map[kvp.Key] = kvp.Value * factor;
            }

            return new ResourceCost(map);
        }

        public bool Equals(ResourceCost other)
        {
            // Compare across the union of types; missing entries count as zero.
            var allTypes = new HashSet<ResourceType>(Types);
            allTypes.UnionWith(other.Types);

            foreach (var type in allTypes)
            {
                // Exact equality is intentional: costs are authored, not computed by
                // accumulation, so they should match bit-for-bit when equal.
                if (AmountOf(type) != other.AmountOf(type))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj) => obj is ResourceCost other && Equals(other);

        public override int GetHashCode()
        {
            // Order-independent hash over non-zero components.
            int hash = 19;
            foreach (var kvp in Components)
            {
                hash ^= kvp.Key.GetHashCode() * 397 ^ kvp.Value.GetHashCode();
            }

            return hash;
        }

        public override string ToString()
        {
            if (IsFree)
            {
                return "Free";
            }

            return string.Join(", ", Components
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => $"{kvp.Key}:{kvp.Value:0.##}"));
        }
    }
}
