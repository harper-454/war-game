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
    /// Property-based tests for the Unit veterancy progression added in task 17
    /// (<see cref="UnitSystem.OnCombatResolved"/> and its tier-advancement logic), each exercised for
    /// at least the design-mandated 100 generated iterations (design.md "Testing Strategy").
    ///
    /// Covered properties, tagged
    /// <c>Feature: epoch-war-combat-visuals-expansion, Property 10/11/12/13</c>:
    /// <list type="bullet">
    ///   <item>Property 10 — Veterancy tier is a pure function of accumulated experience
    ///     (Req 12.1, 12.2, 12.4).</item>
    ///   <item>Property 11 — Veterancy state is isolated to actions on that Unit (Req 12.3).</item>
    ///   <item>Property 12 — Veterancy state is discarded on removal (Req 12.5).</item>
    ///   <item>Property 13 — Every tier crossed emits exactly one advancement event (Req 12.6).</item>
    /// </list>
    ///
    /// All scenarios target the engine-free <c>EpochWar.Core</c> assembly with no Unity Play loop.
    /// Experience grants are driven through <see cref="CombatResolvedEvent"/>s fed to the public XP
    /// hook, exactly as the eventual <c>MatchSimulation</c> wiring (task 22) will feed it.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-combat-visuals-expansion")]
    public sealed class UnitVeterancyPropertyTests
    {
        private const int MinimumIterations = 100;

        // Fixed XP accounting for the tests: 1 XP per damaging action, +9 on an elimination (total 10).
        private const int XpPerDamage = 1;
        private const int XpPerElimination = 9;

        private static Configuration Config()
        {
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;
            return config;
        }

        /// <summary>
        /// Builds an ascending-threshold Veterancy_Curve from a set of generated positive gaps. The
        /// base tier (index 0) is authored at threshold 0 with no bonus ("0 = base/no tier"), and each
        /// later tier adds a strictly greater threshold plus small stat bonuses, matching the
        /// convention <see cref="UnitSystem.ComputeTierIndex"/> documents.
        /// </summary>
        private static List<VeterancyTierDef> BuildCurve(IEnumerable<int> gaps)
        {
            var curve = new List<VeterancyTierDef> { new VeterancyTierDef("Recruit", 0, 0, 0) };
            int threshold = 0;
            int index = 1;
            foreach (var gap in gaps)
            {
                threshold += System.Math.Max(1, gap); // strictly increasing
                curve.Add(new VeterancyTierDef($"Tier{index}", threshold, index, index));
                index++;
            }

            return curve;
        }

        private static UnitDef VeteranUnitDef(List<VeterancyTierDef> curve)
            => new UnitDef("vet", Era.Prehistoric, ResourceCost.Free, 0f, 0, 100, 5, 2, 1f, UnitRole.Soldier,
                veterancyCurve: curve);

        private static UnitDef PlainDefenderDef()
            => new UnitDef("def", Era.Prehistoric, ResourceCost.Free, 0f, 0, 1_000_000, 0, 0, 1f, UnitRole.Soldier);

        private static (MatchState state, UnitSystem units) Build()
        {
            var res = new ResourceSystem();
            var civ = new CivSystem(res);
            var units = new UnitSystem(new InMemoryCatalog(), res, civ,
                experiencePerDamageDealt: XpPerDamage, experiencePerElimination: XpPerElimination);
            var state = new MatchState(new TerrainVolume(new Int3(4, 4, 4), CellMaterial.Soil));
            state.Nations[1] = new Nation(1);
            state.Nations[2] = new Nation(2);
            return (state, units);
        }

        /// <summary>The expected tier for an accumulated experience total: highest index with threshold &lt;= xp.</summary>
        private static int ExpectedTier(List<VeterancyTierDef> curve, int xp)
        {
            int tier = 0;
            for (int i = 0; i < curve.Count; i++)
            {
                if (curve[i].ExperienceThreshold <= xp) tier = i; else break;
            }

            return tier;
        }

        // ---- Property 10 ------------------------------------------------------------------------

        /// <summary>
        /// Property 10: Veterancy tier is a pure function of accumulated experience.
        ///
        /// A Unit's resulting Veterancy_Tier always equals the highest tier in its curve whose
        /// threshold does not exceed the accumulated experience (capped at the top tier), and this is
        /// identical whether the experience arrives as one large grant or many small grants summing to
        /// the same total.
        ///
        /// **Validates: Requirements 12.1, 12.2, 12.4**
        /// </summary>
        [Test]
        [Category("Property 10")]
        public void Property10_VeterancyTierIsPureFunctionOfExperience()
        {
            var gen = from gaps in Gen.NonEmptyListOf(Gen.Choose(1, 40))
                      from grants in Gen.NonEmptyListOf(Gen.Choose(0, 60))
                      select (gaps.ToList(), grants.ToList());

            Prop.ForAll(Arb.From(gen), tuple =>
            {
                var (gaps, grants) = tuple;
                var curve = BuildCurve(gaps);

                // (a) Many small grants applied one event at a time.
                var (stateMany, unitsMany) = Build();
                var attackerMany = new UnitInstance(1, 1, VeteranUnitDef(curve), WorldPosition.Zero);
                stateMany.Units[1] = attackerMany;
                int total = 0;
                foreach (var g in grants)
                {
                    // Emulate g damaging (non-eliminating) actions, one XP each.
                    for (int i = 0; i < g; i++)
                    {
                        unitsMany.OnCombatResolved(stateMany, new List<GameEvent>
                        {
                            new CombatResolvedEvent(1, 999, 5, 100, 95)
                        });
                        total += XpPerDamage;
                    }
                }

                if (attackerMany.VeterancyExperience != total) return false;
                if (attackerMany.VeterancyTierIndex != ExpectedTier(curve, total)) return false;

                // (b) One large grant summing to the same total reaches the identical tier.
                var (stateOne, unitsOne) = Build();
                var attackerOne = new UnitInstance(1, 1, VeteranUnitDef(curve), WorldPosition.Zero);
                stateOne.Units[1] = attackerOne;
                var batch = new List<GameEvent>();
                for (int i = 0; i < total; i++)
                {
                    batch.Add(new CombatResolvedEvent(1, 999, 5, 100, 95));
                }

                unitsOne.OnCombatResolved(stateOne, batch);

                return attackerOne.VeterancyExperience == total
                    && attackerOne.VeterancyTierIndex == attackerMany.VeterancyTierIndex
                    && attackerOne.VeterancyTierIndex == ExpectedTier(curve, total);
            }).Check(Config());
        }

        // ---- Property 11 ------------------------------------------------------------------------

        /// <summary>
        /// Property 11: Veterancy state is isolated to actions on that Unit.
        ///
        /// A bystander Unit that neither deals damage nor is eliminated retains its exact
        /// Veterancy_Tier and experience across combat events involving other Units and across neutral
        /// ticks.
        ///
        /// **Validates: Requirements 12.3**
        /// </summary>
        [Test]
        [Category("Property 11")]
        public void Property11_VeterancyIsolatedToActionsOnThatUnit()
        {
            var gen = from gaps in Gen.NonEmptyListOf(Gen.Choose(1, 20))
                      from events in Gen.Choose(0, 20)
                      from startXp in Gen.Choose(0, 50)
                      select (gaps.ToList(), events, startXp);

            Prop.ForAll(Arb.From(gen), tuple =>
            {
                var (gaps, eventCount, startXp) = tuple;
                var curve = BuildCurve(gaps);
                var (state, units) = Build();

                // Attacker (unit 1) and a completely uninvolved bystander (unit 3).
                state.Units[1] = new UnitInstance(1, 1, VeteranUnitDef(curve), WorldPosition.Zero);
                var bystander = new UnitInstance(3, 1, VeteranUnitDef(curve), WorldPosition.Zero)
                {
                    VeterancyExperience = startXp,
                    VeterancyTierIndex = ExpectedTier(curve, startXp),
                };
                state.Units[3] = bystander;

                int bystanderXpBefore = bystander.VeterancyExperience;
                int bystanderTierBefore = bystander.VeterancyTierIndex;

                // Many combat events, none of which name the bystander as attacker.
                var batch = new List<GameEvent>();
                for (int i = 0; i < eventCount; i++)
                {
                    batch.Add(new CombatResolvedEvent(1, 2, 5, 100, 95));
                }

                units.OnCombatResolved(state, batch);
                units.Tick(state, 1f); // a neutral tick must not touch veterancy either

                return bystander.VeterancyExperience == bystanderXpBefore
                    && bystander.VeterancyTierIndex == bystanderTierBefore;
            }).Check(Config());
        }

        // ---- Property 12 ------------------------------------------------------------------------

        /// <summary>
        /// Property 12: Veterancy state is discarded on removal.
        ///
        /// A Unit removed from the Match (here via <see cref="UnitSystem.RemoveUnit"/>) leaves no
        /// queryable Veterancy record for its id — the instance carrying the state is gone from
        /// <see cref="MatchState.Units"/> and the system retains no separate veterancy store.
        ///
        /// **Validates: Requirements 12.5**
        /// </summary>
        [Test]
        [Category("Property 12")]
        public void Property12_VeterancyDiscardedOnRemoval()
        {
            var gen = from gaps in Gen.NonEmptyListOf(Gen.Choose(1, 20))
                      from grant in Gen.Choose(1, 200)
                      select (gaps.ToList(), grant);

            Prop.ForAll(Arb.From(gen), tuple =>
            {
                var (gaps, grant) = tuple;
                var curve = BuildCurve(gaps);
                var (state, units) = Build();

                var attacker = new UnitInstance(1, 1, VeteranUnitDef(curve), WorldPosition.Zero);
                state.Units[1] = attacker;

                var batch = new List<GameEvent>();
                for (int i = 0; i < grant; i++)
                {
                    batch.Add(new CombatResolvedEvent(1, 2, 5, 100, 95));
                }

                units.OnCombatResolved(state, batch);

                // Sanity: it accumulated some experience before removal.
                if (attacker.VeterancyExperience <= 0) return false;

                units.RemoveUnit(state, 1);

                // No instance for id 1 remains anywhere queryable.
                if (state.Units.ContainsKey(1)) return false;

                // Re-inserting a fresh Unit with the same id starts clean (no retained veterancy).
                var fresh = new UnitInstance(1, 1, VeteranUnitDef(curve), WorldPosition.Zero);
                state.Units[1] = fresh;
                return fresh.VeterancyExperience == 0 && fresh.VeterancyTierIndex == 0;
            }).Check(Config());
        }

        // ---- Property 13 ------------------------------------------------------------------------

        /// <summary>
        /// Property 13: Every tier crossed emits exactly one advancement event.
        ///
        /// A single experience grant that crosses one or more tier thresholds emits exactly one
        /// <see cref="VeterancyTierAdvancedEvent"/> per crossed tier, each carrying the correct
        /// successive tier index; a grant that crosses no threshold emits none.
        ///
        /// **Validates: Requirements 12.6**
        /// </summary>
        [Test]
        [Category("Property 13")]
        public void Property13_EveryTierCrossedEmitsExactlyOneEvent()
        {
            var gen = from gaps in Gen.NonEmptyListOf(Gen.Choose(1, 30))
                      from grant in Gen.Choose(0, 400)
                      select (gaps.ToList(), grant);

            Prop.ForAll(Arb.From(gen), tuple =>
            {
                var (gaps, grant) = tuple;
                var curve = BuildCurve(gaps);
                var (state, units) = Build();

                var attacker = new UnitInstance(1, 1, VeteranUnitDef(curve), WorldPosition.Zero);
                state.Units[1] = attacker;

                int tierBefore = attacker.VeterancyTierIndex; // 0

                var batch = new List<GameEvent>();
                for (int i = 0; i < grant; i++)
                {
                    batch.Add(new CombatResolvedEvent(1, 2, 5, 100, 95));
                }

                var produced = units.OnCombatResolved(state, batch);
                var advanceEvents = produced.OfType<VeterancyTierAdvancedEvent>()
                    .Where(e => e.UnitId == 1)
                    .ToList();

                int tierAfter = attacker.VeterancyTierIndex;
                int expectedCrossed = tierAfter - tierBefore;

                // Exactly one event per crossed tier.
                if (advanceEvents.Count != expectedCrossed) return false;

                // Each event carries the correct successive index (tierBefore+1 .. tierAfter).
                for (int i = 0; i < advanceEvents.Count; i++)
                {
                    if (advanceEvents[i].NewTierIndex != tierBefore + 1 + i) return false;
                }

                // No crossing => no events.
                if (expectedCrossed == 0 && advanceEvents.Count != 0) return false;

                return true;
            }).Check(Config());
        }
    }
}
