using System;
using System.Collections.Generic;
using EpochWar.Core.Commands;
using EpochWar.Core.Math;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// The authoritative owner of every Nation's population and governance (Requirement 5).
    ///
    /// Responsibilities:
    /// <list type="bullet">
    /// <item>Tracks the available population count and population capacity per
    /// <see cref="Nation"/> (Req 5.1) and, each <see cref="Tick"/>, grows the count over time
    /// toward the capacity while the Nation's food suffices, never letting the count exceed the
    /// capacity (Req 5.2, 5.3, Property 23). Growth halts once the count equals the capacity.</item>
    /// <item>Exposes the population reservation contract recruit/construct handlers use:
    /// <see cref="HasAvailablePopulation"/> is a pure availability query, <see cref="TryConsumePopulation"/>
    /// draws the required population down atomically and rejects (with no mutation) any request that
    /// exceeds the available count (Req 5.4, Property 24), and <see cref="ReleasePopulation"/>
    /// returns population to the pool when a unit/structure is removed.</item>
    /// <item>Applies governance/civic options (<see cref="ApplyGovernance"/>) and exposes the
    /// resulting modifier queries — a per-<see cref="ResourceType"/> production multiplier and the
    /// Unit attack/defense multipliers — that the Resource_System and Combat resolution consult so
    /// each option's defined modifiers take effect (Req 5.5, Property 25).</item>
    /// </list>
    ///
    /// All per-Match population/governance data lives on the <see cref="Nation"/>; the only state the
    /// system holds itself is a small per-Nation accumulator of sub-unit growth progress so that
    /// fractional growth across many small fixed steps eventually yields whole-population increments
    /// deterministically. Food is checked through the injected <see cref="ResourceSystem"/>, reusing
    /// its read API. Following the project's pipeline contract, nothing here throws to signal a
    /// rejected reservation; callers inspect the returned boolean / event list instead.
    /// </summary>
    public sealed class CivSystem
    {
        private readonly ResourceSystem _resources;
        private readonly float _growthPerSecond;
        private readonly float _foodThreshold;

        // Per-Nation carry of fractional growth progress (population units not yet whole). Kept here
        // rather than on the Nation so the runtime state schema stays a plain data container; it is
        // purely derived and reproducible from the fixed-step ticks.
        private readonly Dictionary<int, float> _growthAccumulators = new Dictionary<int, float>();

        /// <summary>
        /// Creates the system.
        /// </summary>
        /// <param name="resourceSystem">Used to read each Nation's food balance for the growth gate.</param>
        /// <param name="growthPerSecond">
        /// Population units added per second of simulation time while a Nation is below capacity and
        /// food suffices. Must be positive.
        /// </param>
        /// <param name="foodThreshold">
        /// A Nation's food is considered sufficient for growth while its stored
        /// <see cref="ResourceType.Food"/> quantity is strictly greater than this threshold
        /// (Req 5.2). Defaults to zero, i.e. any positive food permits growth.
        /// </param>
        public CivSystem(ResourceSystem resourceSystem, float growthPerSecond = 1f, float foodThreshold = 0f)
        {
            _resources = resourceSystem ?? throw new ArgumentNullException(nameof(resourceSystem));
            _growthPerSecond = growthPerSecond > 0f ? growthPerSecond : 1f;
            _foodThreshold = foodThreshold;
        }

        // ------------------------------------------------------------------
        // Simulation tick: population growth (Req 5.2, 5.3)
        // ------------------------------------------------------------------

        /// <summary>
        /// Advances every Nation's population toward its capacity by the configured growth rate over
        /// <paramref name="deltaSeconds"/>, but only while the Nation has room to grow
        /// (count &lt; capacity) and its food suffices (Req 5.2). The count is clamped at the
        /// capacity so it never exceeds it, and growth stops while count equals capacity (Req 5.3,
        /// Property 23). Eliminated Nations and a non-positive delta are no-ops. Returns a
        /// <see cref="PopulationChangedEvent"/> for every Nation whose count actually increased.
        /// </summary>
        public IReadOnlyList<GameEvent> Tick(MatchState state, float deltaSeconds)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (deltaSeconds <= 0f)
            {
                return Array.Empty<GameEvent>();
            }

            var events = new List<GameEvent>();

            foreach (var nation in state.Nations.Values)
            {
                if (nation.Eliminated)
                {
                    continue;
                }

                int capacity = EffectiveCapacity(nation);

                // At or above capacity: no growth, and there is no partial progress worth carrying
                // (Req 5.3). Clearing the accumulator keeps the carry from leaking into a later
                // capacity increase as an instantaneous jump.
                if (nation.Population >= capacity)
                {
                    _growthAccumulators[nation.Id] = 0f;
                    continue;
                }

                // Food gate: only grow while food suffices (Req 5.2).
                if (!HasSufficientFood(nation))
                {
                    continue;
                }

                _growthAccumulators.TryGetValue(nation.Id, out float carry);
                carry += _growthPerSecond * deltaSeconds;

                int whole = (int)carry;
                if (whole <= 0)
                {
                    _growthAccumulators[nation.Id] = carry;
                    continue;
                }

                carry -= whole;
                _growthAccumulators[nation.Id] = carry;

                int oldPopulation = nation.Population;
                int newPopulation = MathEx.Min(capacity, oldPopulation + whole);

                if (newPopulation != oldPopulation)
                {
                    nation.Population = newPopulation;
                    events.Add(new PopulationChangedEvent(
                        nation.Id, oldPopulation, newPopulation, capacity, PopulationChangeCause.Growth));
                }
            }

            return events;
        }

        // ------------------------------------------------------------------
        // Population reservation (Req 5.4)
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns true when <paramref name="nation"/> currently has at least
        /// <paramref name="populationCost"/> available population to staff a recruit/construct
        /// action (Req 5.4, Property 24). A non-positive cost is always satisfiable. Pure query —
        /// never mutates state, so it is safe to drive UI availability predicates (Req 7.5).
        /// </summary>
        public bool HasAvailablePopulation(Nation nation, int populationCost)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            return populationCost <= 0 || nation.Population >= populationCost;
        }

        /// <summary>
        /// Atomically draws <paramref name="populationCost"/> down from <paramref name="nation"/>'s
        /// available population if — and only if — the Nation has at least that much (Req 5.4).
        ///
        /// On success the count is reduced, a <see cref="PopulationChangedEvent"/> is produced via
        /// <paramref name="events"/>, and the method returns <c>true</c>. On failure it performs no
        /// mutation at all, leaves <paramref name="events"/> empty, and returns <c>false</c>
        /// (Property 24). A non-positive cost succeeds with no event.
        /// </summary>
        public bool TryConsumePopulation(Nation nation, int populationCost, out IReadOnlyList<GameEvent> events)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            if (populationCost <= 0)
            {
                events = Array.Empty<GameEvent>();
                return true;
            }

            if (nation.Population < populationCost)
            {
                events = Array.Empty<GameEvent>();
                return false;
            }

            int oldPopulation = nation.Population;
            nation.Population = oldPopulation - populationCost;

            events = new GameEvent[]
            {
                new PopulationChangedEvent(
                    nation.Id, oldPopulation, nation.Population, EffectiveCapacity(nation),
                    PopulationChangeCause.Consumed),
            };
            return true;
        }

        /// <summary>
        /// Convenience overload of
        /// <see cref="TryConsumePopulation(Nation, int, out IReadOnlyList{GameEvent})"/> that discards
        /// the produced events. Returns whether the population was available and consumed.
        /// </summary>
        public bool TryConsumePopulation(Nation nation, int populationCost)
            => TryConsumePopulation(nation, populationCost, out _);

        /// <summary>
        /// Returns <paramref name="populationCount"/> population to <paramref name="nation"/>'s
        /// available pool — for example when a Unit or Structure that had staffed it is removed —
        /// clamping the result at the capacity so the count never exceeds it (Req 5.3, Property 23).
        /// A non-positive count is a no-op. Returns a <see cref="PopulationChangedEvent"/> when the
        /// count actually increased.
        /// </summary>
        public IReadOnlyList<GameEvent> ReleasePopulation(Nation nation, int populationCount)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            if (populationCount <= 0)
            {
                return Array.Empty<GameEvent>();
            }

            int capacity = EffectiveCapacity(nation);
            int oldPopulation = nation.Population;
            int newPopulation = MathEx.Min(capacity, oldPopulation + populationCount);

            if (newPopulation == oldPopulation)
            {
                return Array.Empty<GameEvent>();
            }

            nation.Population = newPopulation;
            return new GameEvent[]
            {
                new PopulationChangedEvent(
                    nation.Id, oldPopulation, newPopulation, capacity, PopulationChangeCause.Released),
            };
        }

        // ------------------------------------------------------------------
        // Governance (Req 5.5)
        // ------------------------------------------------------------------

        /// <summary>
        /// Adopts <paramref name="option"/> for <paramref name="nation"/> so its defined modifiers
        /// apply to the Nation's Resource production and Unit attributes (Req 5.5, Property 25).
        ///
        /// The option is added to the Nation's active governance set; an option already active (by
        /// <see cref="GovernanceOption.Id"/>) is not added again and produces no event. Returns a
        /// <see cref="GovernanceChangedEvent"/> when a new option was adopted, otherwise an empty
        /// list.
        /// </summary>
        public IReadOnlyList<GameEvent> ApplyGovernance(Nation nation, GovernanceOption option)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            if (option == null)
            {
                throw new ArgumentNullException(nameof(option));
            }

            foreach (var active in nation.ActiveGovernance)
            {
                if (active.Id == option.Id)
                {
                    return Array.Empty<GameEvent>();
                }
            }

            nation.ActiveGovernance.Add(option);
            return new GameEvent[] { new GovernanceChangedEvent(nation.Id, option.Id) };
        }

        /// <summary>
        /// Removes the active governance option with id <paramref name="optionId"/> from
        /// <paramref name="nation"/>, so its modifiers no longer apply. Returns true when an option
        /// was removed.
        /// </summary>
        public bool RemoveGovernance(Nation nation, string optionId)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            for (int i = 0; i < nation.ActiveGovernance.Count; i++)
            {
                if (nation.ActiveGovernance[i].Id == optionId)
                {
                    nation.ActiveGovernance.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------
        // Governance modifier queries (Req 5.5)
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns the combined production multiplier <paramref name="nation"/>'s active governance
        /// applies to <paramref name="type"/> (Req 5.5, Property 25). Multiple active options stack
        /// multiplicatively; the result is <c>1.0</c> when no active option modifies that resource.
        /// The Resource_System multiplies authored production output by this factor.
        /// </summary>
        public float GetProductionMultiplier(Nation nation, ResourceType type)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            float multiplier = 1f;
            foreach (var option in nation.ActiveGovernance)
            {
                multiplier *= option.GetProductionMultiplier(type);
            }

            return multiplier;
        }

        /// <summary>
        /// Returns the combined attack multiplier <paramref name="nation"/>'s active governance
        /// applies to its Units (Req 5.5, Property 25). Active options stack multiplicatively;
        /// the result is <c>1.0</c> when none modifies attack.
        /// </summary>
        public float GetUnitAttackMultiplier(Nation nation)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            float multiplier = 1f;
            foreach (var option in nation.ActiveGovernance)
            {
                multiplier *= option.UnitAttackMultiplier;
            }

            return multiplier;
        }

        /// <summary>
        /// Returns the combined defense multiplier <paramref name="nation"/>'s active governance
        /// applies to its Units (Req 5.5, Property 25). Active options stack multiplicatively;
        /// the result is <c>1.0</c> when none modifies defense.
        /// </summary>
        public float GetUnitDefenseMultiplier(Nation nation)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            float multiplier = 1f;
            foreach (var option in nation.ActiveGovernance)
            {
                multiplier *= option.UnitDefenseMultiplier;
            }

            return multiplier;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// True while <paramref name="nation"/>'s stored food exceeds the configured threshold, the
        /// condition under which population may grow (Req 5.2).
        /// </summary>
        private bool HasSufficientFood(Nation nation)
            => _resources.GetAmount(nation, ResourceType.Food) > _foodThreshold;

        /// <summary>
        /// The Nation's population capacity floored at zero, so a negative authored/seeded capacity
        /// never permits a negative population bound (Req 5.1, 5.3).
        /// </summary>
        private static int EffectiveCapacity(Nation nation) => MathEx.ClampNonNegative(nation.PopulationCapacity);
    }
}
