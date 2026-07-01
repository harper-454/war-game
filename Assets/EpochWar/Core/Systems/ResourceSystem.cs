using System;
using System.Collections.Generic;
using EpochWar.Core.Commands;
using EpochWar.Core.Math;
using EpochWar.Core.State;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// The authoritative owner of every Nation's resource economy (Requirement 2).
    ///
    /// Responsibilities:
    /// <list type="bullet">
    /// <item>Tracks an independent stored quantity per <see cref="ResourceType"/> per
    /// <see cref="Nation"/> via that Nation's <see cref="Nation.Resources"/> map — quantities
    /// never bleed across types (Req 2.1, Property 7).</item>
    /// <item>Applies production cycles by adding output to a store, capping at the store's
    /// capacity and discarding any overflow (Req 2.2, 2.5, Property 8).</item>
    /// <item>Exposes an atomic affordability/deduction pair other systems use when paying for
    /// research, recruitment, construction, deployment, or launch: an affordable cost is deducted
    /// exactly (Req 2.3, Property 9); an unaffordable cost is rejected with no mutation whatsoever
    /// (Req 2.4, Property 10).</item>
    /// <item>Emits a <see cref="ResourceChangedEvent"/> for every store it mutates so the
    /// networking/UI layers can replicate and refresh displayed amounts (Req 2.6).</item>
    /// </list>
    ///
    /// The system is stateless — all data lives on the <see cref="Nation"/> — so a single instance
    /// can serve the whole Match and is safe to call from the simulation loop and from other
    /// systems' command handlers. Following the project's pipeline contract, nothing here throws to
    /// signal a rejected cost; callers inspect the returned boolean / event list instead.
    /// </summary>
    public sealed class ResourceSystem
    {
        /// <summary>
        /// Reads the stored quantity of <paramref name="type"/> for <paramref name="nation"/>.
        /// A type the Nation has never held reads as zero (Req 2.1).
        /// </summary>
        public float GetAmount(Nation nation, ResourceType type)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            return nation.Resources.TryGetValue(type, out var store) ? store.Amount : 0f;
        }

        /// <summary>
        /// Returns true when <paramref name="nation"/> currently holds at least every component of
        /// <paramref name="cost"/> (Req 2.3). A free cost is always affordable. This is a pure
        /// query and never mutates state, so handlers can probe availability for UI predicates
        /// (Req 7.5) without side effects.
        /// </summary>
        public bool CanAfford(Nation nation, ResourceCost cost)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            foreach (var component in cost.Components)
            {
                if (GetAmount(nation, component.Key) < component.Value)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Atomically deducts <paramref name="cost"/> from <paramref name="nation"/> if — and only
        /// if — the Nation can afford every component (Req 2.3, 2.4).
        ///
        /// On success the method subtracts each component, clamping each store at zero to guard
        /// against floating-point drift, emits one <see cref="ResourceChangedEvent"/> per affected
        /// type via <paramref name="events"/>, and returns <c>true</c>. On failure it performs no
        /// mutation at all — the affordability check completes before any store is touched — leaves
        /// <paramref name="events"/> empty, and returns <c>false</c> (Property 10). A free cost
        /// succeeds with no events.
        /// </summary>
        public bool TryDeduct(Nation nation, ResourceCost cost, out IReadOnlyList<GameEvent> events)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            // Atomicity: validate the whole cost first so a partially-affordable cost mutates
            // nothing (Req 2.4 / Property 10).
            if (!CanAfford(nation, cost))
            {
                events = Array.Empty<GameEvent>();
                return false;
            }

            if (cost.IsFree)
            {
                events = Array.Empty<GameEvent>();
                return true;
            }

            var changes = new List<GameEvent>();
            foreach (var component in cost.Components)
            {
                ApplyDelta(nation, component.Key, -component.Value, changes);
            }

            events = changes;
            return true;
        }

        /// <summary>
        /// Convenience overload of <see cref="TryDeduct(Nation, ResourceCost, out IReadOnlyList{GameEvent})"/>
        /// that discards the produced events. Returns whether the cost was affordable and applied.
        /// </summary>
        public bool TryDeduct(Nation nation, ResourceCost cost) => TryDeduct(nation, cost, out _);

        /// <summary>
        /// Adds <paramref name="amount"/> of <paramref name="type"/> to <paramref name="nation"/>'s
        /// store as a production cycle, capping at the store's capacity and discarding overflow
        /// (Req 2.2, 2.5). Non-positive amounts are ignored. Returns a single
        /// <see cref="ResourceChangedEvent"/> when the stored quantity actually changed, or an empty
        /// list when it did not (e.g. the store was already full).
        /// </summary>
        public IReadOnlyList<GameEvent> Produce(Nation nation, ResourceType type, float amount)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            if (amount <= 0f)
            {
                return Array.Empty<GameEvent>();
            }

            var changes = new List<GameEvent>();
            ApplyDelta(nation, type, amount, changes);
            return changes;
        }

        /// <summary>
        /// Applies a bundle of production outputs (modelled as a <see cref="ResourceCost"/> whose
        /// components are positive amounts) to <paramref name="nation"/> in one cycle, each capped
        /// at its store capacity with overflow discarded (Req 2.2, 2.5). Returns one
        /// <see cref="ResourceChangedEvent"/> per type whose stored quantity actually changed.
        /// </summary>
        public IReadOnlyList<GameEvent> Produce(Nation nation, ResourceCost output)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            if (output.IsFree)
            {
                return Array.Empty<GameEvent>();
            }

            var changes = new List<GameEvent>();
            foreach (var component in output.Components)
            {
                ApplyDelta(nation, component.Key, component.Value, changes);
            }

            return changes;
        }

        /// <summary>
        /// Sets the capacity of <paramref name="nation"/>'s store for <paramref name="type"/>,
        /// creating the store if absent. A non-positive capacity marks the store uncapped (Req 2.5).
        /// If the new capacity is below the current amount the stored quantity is trimmed to the
        /// capacity and a <see cref="ResourceChangedEvent"/> is returned for the trimmed amount.
        /// </summary>
        public IReadOnlyList<GameEvent> SetCapacity(Nation nation, ResourceType type, float capacity)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            nation.Resources.TryGetValue(type, out var store);
            store.Capacity = capacity;

            var changes = new List<GameEvent>();
            float clampedAmount = ClampToCapacity(store.Amount, capacity);
            if (clampedAmount != store.Amount)
            {
                float old = store.Amount;
                store.Amount = clampedAmount;
                nation.Resources[type] = store;
                changes.Add(new ResourceChangedEvent(nation.Id, type, old, clampedAmount, capacity));
            }
            else
            {
                nation.Resources[type] = store;
            }

            return changes;
        }

        /// <summary>
        /// Adds <paramref name="delta"/> (positive for production, negative for deduction) to the
        /// store for <paramref name="type"/>, clamping the result into [0, capacity] (capacity
        /// ignored when uncapped). Appends a <see cref="ResourceChangedEvent"/> to
        /// <paramref name="changes"/> only when the stored amount actually moves, so callers never
        /// emit no-op change events (Req 2.6).
        /// </summary>
        private static void ApplyDelta(
            Nation nation,
            ResourceType type,
            float delta,
            List<GameEvent> changes)
        {
            nation.Resources.TryGetValue(type, out var store);

            float oldAmount = store.Amount;
            float target = oldAmount + delta;

            // Never negative (deduction floor) and never above capacity (production cap, overflow
            // discarded) — Req 2.5.
            float newAmount = ClampToCapacity(MathEx.ClampNonNegative(target), store.Capacity);

            store.Amount = newAmount;
            nation.Resources[type] = store;

            if (newAmount != oldAmount)
            {
                changes.Add(new ResourceChangedEvent(nation.Id, type, oldAmount, newAmount, store.Capacity));
            }
        }

        /// <summary>
        /// Caps <paramref name="amount"/> at <paramref name="capacity"/> when the store is capped
        /// (capacity &gt; 0); returns it unchanged when uncapped (capacity &lt;= 0).
        /// </summary>
        private static float ClampToCapacity(float amount, float capacity)
            => capacity > 0f && amount > capacity ? capacity : amount;
    }
}
