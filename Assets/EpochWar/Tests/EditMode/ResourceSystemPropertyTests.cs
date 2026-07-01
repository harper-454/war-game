using System;
using System.Collections.Generic;
using System.Linq;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Core.Systems;
using FsCheck;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Property-based tests for <see cref="ResourceSystem"/> (Requirement 2), covering the
    /// four universal properties defined in design.md ("Correctness Properties"):
    ///
    /// <list type="bullet">
    /// <item>Property 7 — Resource independence across types (Req 2.1).</item>
    /// <item>Property 8 — Production adds output capped at capacity (Req 2.2, 2.5).</item>
    /// <item>Property 9 — Affordable cost is deducted exactly (Req 2.3).</item>
    /// <item>Property 10 — Unaffordable cost is rejected without state change (Req 2.4).</item>
    /// </list>
    ///
    /// Every property is exercised for at least <see cref="MinimumIterations"/> generated cases,
    /// matching the harness conventions established in <see cref="HarnessSmokePropertyTests"/> and
    /// the design's testing strategy (>= 100 generated iterations per property).
    ///
    /// Generators are constrained to integer-valued, non-negative amounts so the exact-arithmetic
    /// claims (Properties 8 and 9) are checked deterministically without float-representation noise;
    /// the system under test performs the identical <c>float</c> operations, so this narrows the
    /// input space intelligently rather than weakening the properties.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    public sealed class ResourceSystemPropertyTests
    {
        // The minimum number of generated cases every property in this feature must run.
        private const int MinimumIterations = 100;

        private static readonly ResourceType[] AllTypes =
            (ResourceType[])Enum.GetValues(typeof(ResourceType));

        // Non-negative, integer-valued amounts kept in a range large enough to exercise the logic
        // yet exactly representable as float (so deduction/production arithmetic is bit-exact).
        private static Gen<float> AmountGen => Gen.Choose(0, 1000).Select(i => (float)i);

        private static void CheckAtLeast100<T>(Arbitrary<T> arb, Func<T, bool> body)
        {
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;
            Prop.ForAll(arb, body).Check(config);
        }

        private static Nation NationWithUncapped(float[] amounts)
        {
            var nation = new Nation(1);
            for (int i = 0; i < AllTypes.Length; i++)
            {
                nation.Resources[AllTypes[i]] = new ResourceStore(amounts[i], 0f);
            }

            return nation;
        }

        /// <summary>
        /// Property 7: Resource independence across types.
        /// For any Nation and any change applied to one Resource type, the stored quantities of all
        /// other Resource types remain unchanged.
        ///
        /// **Validates: Requirements 2.1**
        /// </summary>
        [Test]
        [Category("Property 7: Resource independence across types")]
        public void Property7_ChangingOneType_LeavesOtherTypesUnchanged()
        {
            var gen =
                from amounts in Gen.ArrayOf(AllTypes.Length, AmountGen)
                from targetIdx in Gen.Choose(0, AllTypes.Length - 1)
                from produced in Gen.Choose(1, 1000)
                select (amounts, targetIdx, produced);

            CheckAtLeast100(gen.ToArbitrary(), input =>
            {
                var (amounts, targetIdx, produced) = input;
                var sys = new ResourceSystem();
                var nation = NationWithUncapped(amounts);
                var targetType = AllTypes[targetIdx];

                sys.Produce(nation, targetType, produced);

                for (int i = 0; i < AllTypes.Length; i++)
                {
                    if (i == targetIdx)
                    {
                        continue;
                    }

                    if (sys.GetAmount(nation, AllTypes[i]) != amounts[i])
                    {
                        return false;
                    }
                }

                // Sanity: the touched type actually absorbed the (uncapped) production.
                return sys.GetAmount(nation, targetType) == amounts[targetIdx] + produced;
            });
        }

        /// <summary>
        /// Property 8: Production adds output capped at capacity.
        /// For any Resource store with a pre-production amount and any non-negative produced
        /// quantity, the post-production amount equals min(capacity, pre + produced) and never
        /// exceeds the capacity.
        ///
        /// **Validates: Requirements 2.2, 2.5**
        /// </summary>
        [Test]
        [Category("Property 8: Production adds output capped at capacity")]
        public void Property8_ProductionIsCappedAtCapacity()
        {
            var gen =
                from capacity in Gen.Choose(1, 1000)
                from pre in Gen.Choose(0, capacity)
                from produced in Gen.Choose(0, 1000) // non-negative, includes zero
                select (capacity, pre, produced);

            CheckAtLeast100(gen.ToArbitrary(), input =>
            {
                var (capacity, pre, produced) = input;
                var sys = new ResourceSystem();
                var nation = new Nation(1);
                nation.Resources[ResourceType.Food] = new ResourceStore(pre, capacity);

                sys.Produce(nation, ResourceType.Food, produced);

                float actual = sys.GetAmount(nation, ResourceType.Food);
                float expected = System.Math.Min(capacity, pre + produced);

                return actual == expected && actual <= capacity;
            });
        }

        /// <summary>
        /// Property 9: Affordable cost is deducted exactly.
        /// For any multi-Resource cost that does not exceed a Nation's stored quantities, applying
        /// the cost reduces each affected Resource by exactly its cost component and leaves the
        /// remaining amount for every other type untouched.
        ///
        /// **Validates: Requirements 2.3**
        /// </summary>
        [Test]
        [Category("Property 9: Affordable cost is deducted exactly")]
        public void Property9_AffordableCostIsDeductedExactly()
        {
            // For each type, generate a stored amount and a cost component in [0, amount] so the
            // whole cost is always affordable.
            var pairGen =
                from amount in Gen.Choose(0, 1000)
                from cost in Gen.Choose(0, amount)
                select (amount: (float)amount, cost: (float)cost);

            var gen = Gen.ArrayOf(AllTypes.Length, pairGen);

            CheckAtLeast100(gen.ToArbitrary(), pairs =>
            {
                var sys = new ResourceSystem();
                var amounts = pairs.Select(p => p.amount).ToArray();
                var nation = NationWithUncapped(amounts);

                var components = new List<(ResourceType, float)>();
                for (int i = 0; i < AllTypes.Length; i++)
                {
                    components.Add((AllTypes[i], pairs[i].cost));
                }

                var cost = ResourceCost.Of(components.ToArray());

                if (!sys.CanAfford(nation, cost))
                {
                    return false; // construction guarantees affordability
                }

                bool accepted = sys.TryDeduct(nation, cost);
                if (!accepted)
                {
                    return false;
                }

                for (int i = 0; i < AllTypes.Length; i++)
                {
                    float expected = pairs[i].amount - pairs[i].cost;
                    if (sys.GetAmount(nation, AllTypes[i]) != expected)
                    {
                        return false;
                    }
                }

                return true;
            });
        }

        /// <summary>
        /// Property 10: Unaffordable cost is rejected without state change.
        /// For any Resource cost that exceeds the Nation's stored quantity in at least one Resource
        /// type, the action is rejected and all stored quantities remain unchanged.
        ///
        /// **Validates: Requirements 2.4**
        /// </summary>
        [Test]
        [Category("Property 10: Unaffordable cost is rejected without state change")]
        public void Property10_UnaffordableCostIsRejectedWithNoStateChange()
        {
            var gen =
                from amounts in Gen.ArrayOf(AllTypes.Length, AmountGen)
                // The type whose cost component is forced to exceed the stored amount.
                from overIdx in Gen.Choose(0, AllTypes.Length - 1)
                // How far past the stored amount that component reaches (>= 1 guarantees unaffordable).
                from overshoot in Gen.Choose(1, 1000)
                // Arbitrary (possibly affordable) components for the remaining types.
                from otherCosts in Gen.ArrayOf(AllTypes.Length, Gen.Choose(0, 1000))
                select (amounts, overIdx, overshoot, otherCosts);

            CheckAtLeast100(gen.ToArbitrary(), input =>
            {
                var (amounts, overIdx, overshoot, otherCosts) = input;
                var sys = new ResourceSystem();
                var nation = NationWithUncapped(amounts);

                var components = new List<(ResourceType, float)>();
                for (int i = 0; i < AllTypes.Length; i++)
                {
                    float component = i == overIdx
                        ? amounts[i] + overshoot // strictly greater than stored => unaffordable
                        : otherCosts[i];
                    components.Add((AllTypes[i], component));
                }

                var cost = ResourceCost.Of(components.ToArray());

                // Precondition: the cost must be unaffordable (the overshoot component ensures it).
                if (sys.CanAfford(nation, cost))
                {
                    return false;
                }

                bool accepted = sys.TryDeduct(nation, cost, out var events);

                if (accepted || events.Count != 0)
                {
                    return false;
                }

                // No stored quantity may have moved.
                for (int i = 0; i < AllTypes.Length; i++)
                {
                    if (sys.GetAmount(nation, AllTypes[i]) != amounts[i])
                    {
                        return false;
                    }
                }

                return true;
            });
        }
    }
}
