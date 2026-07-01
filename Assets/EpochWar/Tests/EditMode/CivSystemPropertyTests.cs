using System.Collections.Generic;
using System.Linq;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Core.Systems;
using FsCheck;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Property-based tests for <see cref="CivSystem"/> (Requirement 5), validating the universal
    /// correctness properties from design.md, each exercised for at least the design-mandated
    /// minimum of 100 generated iterations (see design.md, "Testing Strategy").
    ///
    /// Covered properties, tagged <c>Feature: epoch-war-game, Property N</c>:
    /// <list type="bullet">
    ///   <item>Property 23 — Population growth is bounded by capacity (Req 5.2, 5.3).</item>
    ///   <item>Property 24 — Commands exceeding available population are rejected (Req 5.4).</item>
    ///   <item>Property 25 — Governance options apply their defined modifiers (Req 5.5).</item>
    /// </list>
    ///
    /// The <see cref="CivSystem"/> reads each Nation's food balance through a
    /// <see cref="ResourceSystem"/>, so every scenario constructs both systems and seeds food via
    /// <see cref="ResourceSystem.Produce"/>. Tests target the engine-free <c>EpochWar.Core</c>
    /// assembly with no Unity Play loop.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    public sealed class CivSystemPropertyTests
    {
        // The minimum number of generated cases every property in this feature must run.
        private const int MinimumIterations = 100;

        private static Configuration Config()
        {
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;
            return config;
        }

        // ==================================================================
        // Property 23: Population growth is bounded by capacity (Req 5.2, 5.3)
        // ==================================================================

        /// <summary>
        /// A generated population-growth scenario: a Nation with a capacity and starting population
        /// (constrained to lie at or below the capacity), a positive growth rate, a fixed food
        /// balance, and a sequence of fixed-step ticks to advance.
        /// </summary>
        public sealed class GrowthScenario
        {
            public int Capacity;
            public int InitialPopulation;
            public float GrowthPerSecond;
            public float Food;
            public int Ticks;
            public float DeltaSeconds;

            public override string ToString()
                => $"GrowthScenario(cap={Capacity}, pop0={InitialPopulation}, growth/s={GrowthPerSecond}, "
                   + $"food={Food}, ticks={Ticks}, dt={DeltaSeconds})";
        }

        private static Arbitrary<GrowthScenario> GrowthScenarios()
        {
            var gen = from capacity in Gen.Choose(0, 200)
                      from popRaw in Gen.Choose(0, 200)
                      from growthTenths in Gen.Choose(1, 60)
                      from foodRaw in Gen.Choose(0, 1000)
                      from ticks in Gen.Choose(0, 40)
                      from dtTenths in Gen.Choose(1, 30)
                      select new GrowthScenario
                      {
                          Capacity = capacity,
                          // Keep the starting population within the capacity (Req 5.1/5.3 precondition).
                          InitialPopulation = capacity == 0 ? 0 : popRaw % (capacity + 1),
                          GrowthPerSecond = growthTenths / 10f,   // 0.1 .. 6.0 population/sec
                          Food = foodRaw,                          // 0 .. 1000 stored food
                          Ticks = ticks,                           // 0 .. 40 fixed steps
                          DeltaSeconds = dtTenths / 10f,           // 0.1 .. 3.0 sec/step
                      };

            return Arb.From(gen);
        }

        /// <summary>
        /// Property 23: Population growth is bounded by capacity.
        ///
        /// For any Nation and any sequence of simulation ticks, the population count never exceeds
        /// the population capacity; and when the capacity exceeds the count and food is sufficient,
        /// the count grows over time toward the capacity.
        ///
        /// **Validates: Requirements 5.2, 5.3**
        /// </summary>
        [Test]
        [Category("Property 23")]
        public void Property23_PopulationGrowthIsBoundedByCapacity()
        {
            Prop.ForAll(GrowthScenarios(), scenario =>
            {
                var resources = new ResourceSystem();
                var civ = new CivSystem(resources, scenario.GrowthPerSecond);

                var nation = new Nation(1)
                {
                    PopulationCapacity = scenario.Capacity,
                    Population = scenario.InitialPopulation,
                };

                // Seed the food balance the growth gate reads (food is only read, never consumed).
                resources.Produce(nation, ResourceType.Food, scenario.Food);

                var state = new MatchState();
                state.Nations[nation.Id] = nation;

                // Advance the requested number of fixed steps, asserting the capacity bound after
                // each one so a transient overshoot is caught (Req 5.3, upper bound of Property 23).
                for (int i = 0; i < scenario.Ticks; i++)
                {
                    civ.Tick(state, scenario.DeltaSeconds);

                    if (nation.Population > scenario.Capacity)
                    {
                        return false;
                    }

                    // Growth is monotonic non-decreasing: a tick never shrinks the count.
                    if (nation.Population < scenario.InitialPopulation)
                    {
                        return false;
                    }
                }

                // Growth-toward-capacity: when there was room to grow, food sufficed (> 0), and enough
                // simulated time elapsed to accumulate at least one whole population unit, the count
                // must have strictly increased toward the capacity (lower behaviour of Property 23).
                float totalTime = scenario.Ticks * scenario.DeltaSeconds;
                bool hadRoom = scenario.InitialPopulation < scenario.Capacity;
                bool foodSufficient = scenario.Food > 0f;
                bool enoughTimeForOneUnit = scenario.GrowthPerSecond * totalTime >= 1f;

                if (hadRoom && foodSufficient && enoughTimeForOneUnit)
                {
                    if (nation.Population <= scenario.InitialPopulation)
                    {
                        return false;
                    }
                }

                return true;
            }).Check(Config());
        }

        // ==================================================================
        // Property 24: Commands exceeding available population are rejected (Req 5.4)
        // ==================================================================

        /// <summary>
        /// Property 24: Commands exceeding available population are rejected.
        ///
        /// For any recruit or construct command whose population requirement exceeds the Nation's
        /// available population, the command is rejected (no consumption succeeds) and population
        /// usage is unchanged. Conversely, a satisfiable requirement is accepted and draws the count
        /// down by exactly the cost.
        ///
        /// **Validates: Requirements 5.4**
        /// </summary>
        [Test]
        [Category("Property 24")]
        public void Property24_CommandsExceedingAvailablePopulationAreRejected()
        {
            var scenarios = from available in Gen.Choose(0, 500)
                            from cost in Gen.Choose(0, 1000)
                            select (available, cost);

            Prop.ForAll(Arb.From(scenarios), tuple =>
            {
                var (available, cost) = tuple;

                var resources = new ResourceSystem();
                var civ = new CivSystem(resources);

                var nation = new Nation(1) { Population = available };

                int before = nation.Population;
                bool hasAvailable = civ.HasAvailablePopulation(nation, cost);
                bool consumed = civ.TryConsumePopulation(nation, cost, out var events);

                bool exceeds = cost > 0 && cost > available;

                if (exceeds)
                {
                    // Rejected: no availability, no consumption, no mutation, no events (Property 24).
                    return !hasAvailable
                           && !consumed
                           && nation.Population == before
                           && events.Count == 0;
                }

                // Satisfiable (including a non-positive/zero cost): accepted, and the count drops by
                // exactly the cost (a non-positive cost draws nothing down).
                int expectedCost = cost > 0 ? cost : 0;
                return hasAvailable
                       && consumed
                       && nation.Population == before - expectedCost;
            }).Check(Config());
        }

        // ==================================================================
        // Property 25: Governance options apply their defined modifiers (Req 5.5)
        // ==================================================================

        private static readonly ResourceType[] AllResourceTypes =
            (ResourceType[])System.Enum.GetValues(typeof(ResourceType));

        /// <summary>
        /// A generated governance option carrying a per-resource production multiplier plus attack
        /// and defense multipliers, each drawn as a positive factor (in tenths) so the modifier is a
        /// meaningful, exactly-representable change.
        /// </summary>
        private static Arbitrary<GovernanceOption> GovernanceOptions()
        {
            // A multiplier for every resource type, generated as tenths (0.1 .. 3.0).
            Gen<float> factorGen = Gen.Choose(1, 30).Select(tenths => tenths / 10f);

            Gen<Dictionary<ResourceType, float>> productionGen =
                Gen.Sequence(AllResourceTypes.Select(_ => factorGen))
                   .Select(factors =>
                   {
                       var list = factors.ToList();
                       var map = new Dictionary<ResourceType, float>();
                       for (int i = 0; i < AllResourceTypes.Length; i++)
                       {
                           map[AllResourceTypes[i]] = list[i];
                       }

                       return map;
                   });

            var gen = from production in productionGen
                      from attackTenths in Gen.Choose(1, 30)
                      from defenseTenths in Gen.Choose(1, 30)
                      from idNum in Gen.Choose(0, 100000)
                      select new GovernanceOption(
                          $"gov-{idNum}",
                          $"Governance {idNum}",
                          production,
                          attackTenths / 10f,
                          defenseTenths / 10f);

            return Arb.From(gen);
        }

        /// <summary>
        /// Property 25: Governance options apply their defined modifiers.
        ///
        /// For any selected governance option, the option's defined modifier is applied to the
        /// affected Resource production and Unit attributes: after adoption, the Civ_System's
        /// production multiplier for each Resource type equals the option's defined production
        /// multiplier, and the Unit attack/defense multipliers equal the option's defined factors.
        ///
        /// **Validates: Requirements 5.5**
        /// </summary>
        [Test]
        [Category("Property 25")]
        public void Property25_GovernanceOptionsApplyTheirDefinedModifiers()
        {
            Prop.ForAll(GovernanceOptions(), option =>
            {
                var resources = new ResourceSystem();
                var civ = new CivSystem(resources);
                var nation = new Nation(1);

                var events = civ.ApplyGovernance(nation, option);

                // Adoption is announced so the UI/networking can refresh (Req 5.5 / 7.4).
                bool announced = events.OfType<GovernanceChangedEvent>()
                    .Any(e => e.GovernanceOptionId == option.Id && e.NationId == nation.Id);
                if (!announced)
                {
                    return false;
                }

                // Each affected Resource production multiplier equals the option's defined modifier.
                foreach (var type in AllResourceTypes)
                {
                    if (civ.GetProductionMultiplier(nation, type) != option.GetProductionMultiplier(type))
                    {
                        return false;
                    }
                }

                // The Unit attack/defense modifiers equal the option's defined factors.
                return civ.GetUnitAttackMultiplier(nation) == option.UnitAttackMultiplier
                       && civ.GetUnitDefenseMultiplier(nation) == option.UnitDefenseMultiplier;
            }).Check(Config());
        }
    }
}
