using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Core.Systems;
using FsCheck;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Property-based tests for <see cref="TechSystem"/> (Requirement 1, plus the Era/availability
    /// gating of the special victory paths: Req 9.1, 10.1, 11.1).
    ///
    /// Each property is universally quantified over generated inputs and exercised for at least the
    /// design-mandated minimum of 100 generated cases (see design.md, "Testing Strategy"). Every
    /// test is tagged <c>Feature: epoch-war-game</c> and its <c>Property N</c> and carries the
    /// requirement it validates.
    ///
    /// The tests build small in-memory catalogs directly from POCOs via <see cref="InMemoryCatalog"/>
    /// so they run engine-free against <c>EpochWar.Core</c>.
    ///
    /// Property 6 (serialization round-trip): <c>EpochWar.Core</c> exposes no tech-state serializer,
    /// so — per the task's guidance to prefer testing the existing state without adding Core API —
    /// the round-trip is implemented inside the test with <see cref="System.Text.Json"/> over a
    /// small DTO capturing exactly the persisted tech state (CurrentEra + CompletedTechIds).
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    public sealed class TechSystemPropertyTests
    {
        // Every property in this feature runs at least this many generated cases.
        private const int MinimumIterations = 100;

        // A small stable pool of technology ids used to generate prerequisite/requirement subsets.
        private static readonly string[] Pool = { "P0", "P1", "P2", "P3", "P4" };

        // ------------------------------------------------------------------
        // Harness helpers
        // ------------------------------------------------------------------

        private static void Check(Property property)
        {
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;
            property.Check(config);
        }

        /// <summary>Returns the pool ids whose bit is set in <paramref name="mask"/>.</summary>
        private static string[] Subset(int mask) =>
            Pool.Where((_, i) => (mask & (1 << i)) != 0).ToArray();

        private static ResourceSystem Resources() => new ResourceSystem();

        private static Nation NationWithResearch(ResourceSystem resources, float amount)
        {
            var nation = new Nation(1);
            if (amount > 0f)
            {
                resources.Produce(nation, ResourceType.Research, amount);
            }

            return nation;
        }

        private static MatchState StateWith(Nation nation)
        {
            var state = new MatchState();
            state.Nations[nation.Id] = nation;
            return state;
        }

        // ------------------------------------------------------------------
        // Property 1: Research selection deducts cost and starts progress (Req 1.2)
        // ------------------------------------------------------------------

        /// <summary>
        /// For any Nation and any available Technology whose Research cost does not exceed the
        /// Nation's Research balance, selecting it deducts exactly the cost and leaves the
        /// Technology with accumulating (non-decreasing) progress.
        ///
        /// **Validates: Requirements 1.2**
        /// </summary>
        [Test]
        [Category("Property 1")]
        public void Property1_ResearchSelection_DeductsCostAndStartsProgress()
        {
            var gen =
                from cost in Gen.Choose(1, 50)
                from extra in Gen.Choose(0, 50)
                select (cost, extra);

            Check(Prop.ForAll(Arb.From(gen), pair =>
            {
                var (cost, extra) = pair;
                float balance = cost + extra;

                var resources = Resources();
                var nation = NationWithResearch(resources, balance);
                var tech = new TechnologyDef(
                    "TECH", Era.Prehistoric, ResourceCost.Single(ResourceType.Research, cost));
                var catalog = new InMemoryCatalog(technologies: new[] { tech });
                var sys = new TechSystem(catalog, resources);
                var state = StateWith(nation);

                var result = sys.Handle(new StartResearchCommand(nation.Id, "TECH"), state);

                // Deducted exactly the cost.
                bool accepted = result.Accepted;
                bool balanceExact =
                    resources.GetAmount(nation, ResourceType.Research) == extra;

                // Progress started at zero.
                bool started =
                    nation.ResearchProgress.TryGetValue("TECH", out var p0) && p0 == 0f;

                // Progress is accumulating (non-decreasing): one positive tick either advances the
                // progress above zero or completes the research.
                sys.Tick(state, 1f);
                bool progressing =
                    nation.CompletedTechIds.Contains("TECH")
                    || (nation.ResearchProgress.TryGetValue("TECH", out var p1) && p1 > 0f);

                return accepted && balanceExact && started && progressing;
            }));
        }

        // ------------------------------------------------------------------
        // Property 2: Unmet prerequisites imply unavailable (Req 1.3)
        // ------------------------------------------------------------------

        /// <summary>
        /// For any Technology and any set of completed Technologies, the Technology is available for
        /// selection if and only if all of its prerequisite Technologies are in the completed set.
        ///
        /// **Validates: Requirements 1.3**
        /// </summary>
        [Test]
        [Category("Property 2")]
        public void Property2_UnmetPrerequisites_ImplyUnavailable()
        {
            var gen =
                from prereqMask in Gen.Choose(0, 31)
                from completedMask in Gen.Choose(0, 31)
                select (prereqMask, completedMask);

            Check(Prop.ForAll(Arb.From(gen), pair =>
            {
                var (prereqMask, completedMask) = pair;
                var prereqs = Subset(prereqMask);

                // The tech under test is at the starting Era so the Era gate is always satisfied and
                // availability is determined purely by prerequisite completion.
                var tech = new TechnologyDef(
                    "TECH", Era.Prehistoric, ResourceCost.Free, prerequisites: prereqs);
                var catalog = new InMemoryCatalog(technologies: new[] { tech });
                var sys = new TechSystem(catalog, Resources());

                var nation = new Nation(1);
                foreach (var id in Subset(completedMask))
                {
                    nation.CompletedTechIds.Add(id);
                }

                bool expectedAvailable = prereqs.All(nation.CompletedTechIds.Contains);
                bool actualAvailable = sys.IsTechAvailable(nation, "TECH");

                return expectedAvailable == actualAvailable;
            }));
        }

        // ------------------------------------------------------------------
        // Property 3: Era advancement is gated by completed requirements (Req 1.4)
        // ------------------------------------------------------------------

        /// <summary>
        /// For any Nation, advancement to the next Era is enabled if and only if the Nation's
        /// completed Technology set contains every Technology required for that next Era.
        ///
        /// **Validates: Requirements 1.4**
        /// </summary>
        [Test]
        [Category("Property 3")]
        public void Property3_EraAdvancement_GatedByCompletedRequirements()
        {
            var gen =
                from requiredMask in Gen.Choose(0, 31)
                from completedMask in Gen.Choose(0, 31)
                select (requiredMask, completedMask);

            Check(Prop.ForAll(Arb.From(gen), pair =>
            {
                var (requiredMask, completedMask) = pair;
                var required = Subset(requiredMask);

                // Nation starts at Prehistoric; the next Era (Ancient) carries the requirements.
                var ancient = new EraDef(Era.Ancient, "Ancient", required);
                var catalog = new InMemoryCatalog(eras: new[] { ancient });
                var sys = new TechSystem(catalog, Resources());

                var nation = new Nation(1);
                foreach (var id in Subset(completedMask))
                {
                    nation.CompletedTechIds.Add(id);
                }

                bool expected = required.All(nation.CompletedTechIds.Contains);
                bool actual = sys.CanAdvanceEra(nation);

                return expected == actual;
            }));
        }

        // ------------------------------------------------------------------
        // Property 4: Era advancement unlocks all designated content (Req 1.5)
        // ------------------------------------------------------------------

        /// <summary>
        /// For any target Era, after a Nation advances to it, every Unit type, Structure type, and
        /// Resource type designated for that Era (and all earlier Eras) is present in the Nation's
        /// unlocked set.
        ///
        /// **Validates: Requirements 1.5**
        /// </summary>
        [Test]
        [Category("Property 4")]
        public void Property4_EraAdvancement_UnlocksAllDesignatedContent()
        {
            var era = from e in Gen.Choose(0, 8) select (Era)e;

            var gen =
                from targetInt in Gen.Choose(1, 8)
                from u0 in era
                from u1 in era
                from u2 in era
                from s0 in era
                from s1 in era
                from rFood in era
                from rMetal in era
                select new { Target = (Era)targetInt, u0, u1, u2, s0, s1, rFood, rMetal };

            Check(Prop.ForAll(Arb.From(gen), g =>
            {
                var units = new[]
                {
                    Unit("U0", g.u0), Unit("U1", g.u1), Unit("U2", g.u2),
                };
                var structures = new[]
                {
                    Structure("S0", g.s0), Structure("S1", g.s1),
                };
                var resources = new[]
                {
                    new ResourceDef(ResourceType.Food, "Food", g.rFood, 0f),
                    new ResourceDef(ResourceType.Metal, "Metal", g.rMetal, 0f),
                };

                var catalog = new InMemoryCatalog(
                    units: units, structures: structures, resources: resources);
                var sys = new TechSystem(catalog, Resources());

                var nation = new Nation(1);
                while (nation.CurrentEra < g.Target)
                {
                    sys.AdvanceEra(nation);
                }

                var unlockedUnits = sys.GetUnlockedUnitIds(nation);
                var unlockedStructures = sys.GetUnlockedStructureIds(nation);
                var unlockedResources = sys.GetUnlockedResources(nation);

                bool unitsOk = units
                    .Where(u => u.Era <= g.Target)
                    .All(u => unlockedUnits.Contains(u.Id));
                bool structuresOk = structures
                    .Where(s => s.Era <= g.Target)
                    .All(s => unlockedStructures.Contains(s.Id));
                bool resourcesOk = resources
                    .Where(r => r.Era <= g.Target)
                    .All(r => unlockedResources.Contains(r.Type));

                return unitsOk && structuresOk && resourcesOk;
            }));
        }

        // ------------------------------------------------------------------
        // Property 5: Unaffordable research is rejected without state change (Req 1.6)
        // ------------------------------------------------------------------

        /// <summary>
        /// For any Nation and Technology whose Research cost exceeds the Nation's Research balance,
        /// the research request is rejected and the Nation's balance and research progress are
        /// unchanged.
        ///
        /// **Validates: Requirements 1.6**
        /// </summary>
        [Test]
        [Category("Property 5")]
        public void Property5_UnaffordableResearch_RejectedWithoutStateChange()
        {
            var gen =
                from cost in Gen.Choose(1, 50)
                from balance in Gen.Choose(0, cost - 1)
                select (cost, balance);

            Check(Prop.ForAll(Arb.From(gen), pair =>
            {
                var (cost, balance) = pair;

                var resources = Resources();
                var nation = NationWithResearch(resources, balance);
                var tech = new TechnologyDef(
                    "TECH", Era.Prehistoric, ResourceCost.Single(ResourceType.Research, cost));
                var catalog = new InMemoryCatalog(technologies: new[] { tech });
                var sys = new TechSystem(catalog, resources);
                var state = StateWith(nation);

                var result = sys.Handle(new StartResearchCommand(nation.Id, "TECH"), state);

                bool rejected = !result.Accepted;
                bool balanceUnchanged =
                    resources.GetAmount(nation, ResourceType.Research) == balance;
                bool noProgress = nation.ResearchProgress.Count == 0;

                return rejected && balanceUnchanged && noProgress;
            }));
        }

        // ------------------------------------------------------------------
        // Property 6: Tech state survives a serialization round-trip (Req 1.7)
        // ------------------------------------------------------------------

        /// <summary>
        /// For any Nation tech state (current Era and completed Technology set), deserializing its
        /// serialized form yields an equal current Era and equal completed Technology set.
        ///
        /// The round-trip is performed with System.Text.Json over a small DTO (see class summary).
        ///
        /// **Validates: Requirements 1.7**
        /// </summary>
        [Test]
        [Category("Property 6")]
        public void Property6_TechState_SurvivesSerializationRoundTrip()
        {
            var era = from e in Gen.Choose(0, 8) select (Era)e;

            var gen =
                from e in era
                from completedMask in Gen.Choose(0, 31)
                select (e, completedMask);

            Check(Prop.ForAll(Arb.From(gen), pair =>
            {
                var (currentEra, completedMask) = pair;

                var nation = new Nation(1, currentEra: currentEra);
                foreach (var id in Subset(completedMask))
                {
                    nation.CompletedTechIds.Add(id);
                }

                var dto = new TechStateDto
                {
                    CurrentEra = nation.CurrentEra,
                    CompletedTechIds = nation.CompletedTechIds.ToList(),
                };

                string json = JsonSerializer.Serialize(dto);
                var restored = JsonSerializer.Deserialize<TechStateDto>(json);

                bool eraEqual = restored.CurrentEra == nation.CurrentEra;
                bool setEqual = new HashSet<string>(restored.CompletedTechIds)
                    .SetEquals(nation.CompletedTechIds);

                return eraEqual && setEqual;
            }));
        }

        // ------------------------------------------------------------------
        // Property 32: Doomsday weapons are gated by Era (Req 9.1)
        // ------------------------------------------------------------------

        /// <summary>
        /// For any Nation, a Doomsday_Weapon is available for research if and only if the Nation's
        /// current Era is at least the Era designated for that Doomsday_Weapon.
        ///
        /// **Validates: Requirements 9.1**
        /// </summary>
        [Test]
        [Category("Property 32")]
        public void Property32_DoomsdayWeapons_GatedByEra()
        {
            var gen =
                from weaponEra in Gen.Choose(0, 8)
                from nationEra in Gen.Choose(0, 8)
                select (weaponEra, nationEra);

            Check(Prop.ForAll(Arb.From(gen), pair =>
            {
                var (weaponEra, nationEra) = pair;

                var doomsday = new TechnologyDef(
                    "DOOM", (Era)weaponEra, ResourceCost.Free,
                    category: TechCategory.DoomsdayWeapon);
                var catalog = new InMemoryCatalog(technologies: new[] { doomsday });
                var sys = new TechSystem(catalog, Resources());

                var nation = new Nation(1, currentEra: (Era)nationEra);

                bool expected = nationEra >= weaponEra;
                bool actual = sys.IsDoomsdayAvailable(nation, "DOOM");

                return expected == actual;
            }));
        }

        // ------------------------------------------------------------------
        // Property 36: Peace Arch availability is gated by prerequisite techs (Req 10.1)
        // ------------------------------------------------------------------

        /// <summary>
        /// For any Nation, the Peace_Arch is available for placement if and only if the Nation has
        /// completed all of the Peace_Arch's prerequisite Technologies.
        ///
        /// **Validates: Requirements 10.1**
        /// </summary>
        [Test]
        [Category("Property 36")]
        public void Property36_PeaceArchAvailability_GatedByPrerequisiteTechs()
        {
            var gen =
                from prereqMask in Gen.Choose(0, 31)
                from completedMask in Gen.Choose(0, 31)
                select (prereqMask, completedMask);

            Check(Prop.ForAll(Arb.From(gen), pair =>
            {
                var (prereqMask, completedMask) = pair;

                // Each pool tech is tagged as a Peace_Arch prerequisite when its bit is set;
                // the rest are ordinary technologies that must not affect availability.
                var techs = Pool.Select((id, i) => new TechnologyDef(
                    id, Era.Prehistoric, ResourceCost.Free,
                    category: (prereqMask & (1 << i)) != 0
                        ? TechCategory.PeaceArchPrereq
                        : TechCategory.Normal)).ToArray();
                var catalog = new InMemoryCatalog(technologies: techs);
                var sys = new TechSystem(catalog, Resources());

                var nation = new Nation(1);
                foreach (var id in Subset(completedMask))
                {
                    nation.CompletedTechIds.Add(id);
                }

                var prereqs = Subset(prereqMask);
                bool expected = prereqs.All(nation.CompletedTechIds.Contains);
                bool actual = sys.IsPeaceArchAvailable(nation);

                return expected == actual;
            }));
        }

        // ------------------------------------------------------------------
        // Property 40: Colony Ship availability is gated by the Space Era (Req 11.1)
        // ------------------------------------------------------------------

        /// <summary>
        /// For any Nation, the Colony_Ship is available if and only if the Nation has reached the
        /// Space Era.
        ///
        /// **Validates: Requirements 11.1**
        /// </summary>
        [Test]
        [Category("Property 40")]
        public void Property40_ColonyShipAvailability_GatedBySpaceEra()
        {
            var gen = from e in Gen.Choose(0, 8) select (Era)e;

            Check(Prop.ForAll(Arb.From(gen), nationEra =>
            {
                var catalog = new InMemoryCatalog();
                var sys = new TechSystem(catalog, Resources());
                var nation = new Nation(1, currentEra: nationEra);

                bool expected = nationEra >= Era.Space;
                bool actual = sys.IsColonyShipAvailable(nation);

                return expected == actual;
            }));
        }

        // ------------------------------------------------------------------
        // Helpers for content construction
        // ------------------------------------------------------------------

        private static UnitDef Unit(string id, Era era) => new UnitDef(
            id, era, ResourceCost.Free, buildTimeSeconds: 1f, populationCost: 0,
            maxHealth: 10, attack: 1, defense: 1, moveSpeed: 1f, role: UnitRole.Soldier);

        private static StructureDef Structure(string id, Era era) => new StructureDef(
            id, era, ResourceCost.Free, buildTimeSeconds: 1f, populationCost: 0,
            maxHealth: 10, footprintWidth: 1, footprintLength: 1,
            function: StructureFunction.Barracks);

        /// <summary>DTO capturing exactly the persisted tech state for the Property 6 round-trip.</summary>
        private sealed class TechStateDto
        {
            public Era CurrentEra { get; set; }
            public List<string> CompletedTechIds { get; set; } = new List<string>();
        }
    }
}
