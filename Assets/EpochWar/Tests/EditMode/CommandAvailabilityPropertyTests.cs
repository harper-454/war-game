using System.Collections.Generic;
using System.Linq;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Core.Systems;
using EpochWar.Unity.UI;
using FsCheck;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Property-based test for the UI command-availability source of truth
    /// (<see cref="CommandAvailability"/>), validating the universal correctness property from
    /// design.md:
    ///
    /// <para><b>Property 30 — Command controls are enabled exactly when actions are available
    /// (Req 7.5).</b> <i>For any Nation state, each command control (recruit, place Structure,
    /// initiate research, form Battalion) is enabled if and only if its corresponding action is
    /// currently available.</i></para>
    ///
    /// The property is universally quantified over generated Nation/Match states and exercised for at
    /// least the design-mandated minimum of 100 generated cases (see design.md, "Testing Strategy").
    /// Every test is tagged <c>Feature: epoch-war-game</c> and <c>Property 30</c>.
    ///
    /// <para><b>Cross-check strategy.</b> For each command kind, a generated state is fed to the
    /// <see cref="CommandAvailability"/> predicate <em>and</em> to the authoritative command pipeline
    /// (a <see cref="CommandRouter"/> with the real system handlers registered). The availability
    /// predicate is a pure query evaluated first; the equivalent command is then dispatched, and the
    /// test asserts <c>available == dispatch.Accepted</c> — the exact "iff" of Property 30.</para>
    ///
    /// <para><see cref="CommandAvailability"/> is deliberately location/target independent (a
    /// placement cell or recruit spot is a per-click argument resolved at issue time, not a gate on
    /// the persistent control). To make the "iff" exact, every generated scenario keeps that
    /// per-click dimension trivially valid — solid, unoccupied terrain for placement; an existing,
    /// owned, operational Structure for recruitment — so the only variation left is the availability
    /// axis the control actually represents (unlocked + affordable + enough population + not
    /// eliminated). All other axes (ownership, operational state, prerequisites, duplicates) are still
    /// varied because both the predicate and the handler honour them identically.</para>
    ///
    /// <para>Because <see cref="CommandAvailability"/> is an intentionally <c>UnityEngine</c>-free
    /// class, this runs with no Unity Play loop.</para>
    ///
    /// NOTE (asmdef): in a real Unity project this EditMode test requires the test asmdef
    /// (<c>EpochWar.Tests.EditMode</c>) to reference <c>EpochWar.Unity</c> in addition to
    /// <c>EpochWar.Core</c>, because it exercises <see cref="CommandAvailability"/>.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    public sealed class CommandAvailabilityPropertyTests
    {
        // Every property in this feature runs at least this many generated cases (>= 100 required).
        private const int MinimumIterations = 200;

        private static void Check(Property property)
        {
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;
            property.Check(config);
        }

        private static TerrainVolume SolidTerrain(int x = 16, int y = 4, int z = 16)
            => new TerrainVolume(new Int3(x, y, z), CellMaterial.Rock);

        /// <summary>Bundles the systems, router, availability query, state and nation for one case.</summary>
        private sealed class World
        {
            public ResourceSystem Resources;
            public CivSystem Civ;
            public TechSystem Tech;
            public BaseSystem Base;
            public UnitSystem Units;
            public CommandRouter Router;
            public CommandAvailability Availability;
            public MatchState State;
            public Nation Nation;
        }

        private static World NewWorld(InMemoryCatalog catalog, Era era, int population, bool eliminated)
        {
            var resources = new ResourceSystem();
            var civ = new CivSystem(resources);
            var tech = new TechSystem(catalog, resources);
            var baseSys = new BaseSystem(catalog, resources, civ, tech);
            var units = new UnitSystem(catalog, resources, civ);

            var router = new CommandRouter();
            tech.RegisterHandlers(router);
            baseSys.RegisterHandlers(router);
            units.RegisterHandlers(router);

            var availability = new CommandAvailability(catalog, resources, tech, civ, baseSys);

            var nation = new Nation(1, currentEra: era)
            {
                Population = population,
                PopulationCapacity = 1000000,
                Eliminated = eliminated,
            };

            var state = new MatchState(SolidTerrain());
            state.Nations[nation.Id] = nation;

            return new World
            {
                Resources = resources,
                Civ = civ,
                Tech = tech,
                Base = baseSys,
                Units = units,
                Router = router,
                Availability = availability,
                State = state,
                Nation = nation,
            };
        }

        // ==================================================================
        // Property 30 (Recruit): CanRecruit iff a RecruitUnitCommand is accepted.
        // ==================================================================

        /// <summary>
        /// The recruit control is enabled iff a recruit command would be accepted: the producing
        /// Structure exists, is owned and operational, the Unit type is unlocked, and both the
        /// Resource and population costs are met (Req 7.5, mirroring the UnitSystem handler).
        ///
        /// **Validates: Requirements 7.5**
        /// </summary>
        [Test]
        [Category("Property 30")]
        public void Property30_Recruit_AvailableIffCommandAccepted()
        {
            var gen =
                from nationEra in Gen.Choose(0, 8)
                from unitEra in Gen.Choose(0, 8)
                from costMetal in Gen.Choose(0, 60)
                from stock in Gen.Choose(0, 60)
                from popCost in Gen.Choose(0, 20)
                from popAvail in Gen.Choose(0, 20)
                from operational in Gen.Choose(0, 1)
                from ownedBySelf in Gen.Choose(0, 1)
                from eliminated in Gen.Choose(0, 1)
                select new { nationEra, unitEra, costMetal, stock, popCost, popAvail, operational, ownedBySelf, eliminated };

            Check(Prop.ForAll(Arb.From(gen), g =>
            {
                var unitCost = g.costMetal > 0 ? ResourceCost.Single(ResourceType.Metal, g.costMetal) : ResourceCost.Free;
                var unitDef = new UnitDef(
                    "u", (Era)g.unitEra, unitCost, buildTimeSeconds: 1f, populationCost: g.popCost,
                    maxHealth: 100, attack: 10, defense: 5, moveSpeed: 1f, role: UnitRole.Soldier);
                var structDef = new StructureDef(
                    "barracks", Era.Prehistoric, ResourceCost.Free, 0f, 0, 100, 1, 1, StructureFunction.Barracks);

                var catalog = new InMemoryCatalog(units: new[] { unitDef }, structures: new[] { structDef });
                var w = NewWorld(catalog, (Era)g.nationEra, g.popAvail, g.eliminated == 1);

                w.Resources.Produce(w.Nation, ResourceType.Metal, g.stock);

                // A pre-existing Structure (never built via the pipeline) with generated owner/operational.
                int ownerId = g.ownedBySelf == 1 ? w.Nation.Id : w.Nation.Id + 99;
                const int structureId = 1;
                w.State.Structures[structureId] = new StructureInstance(
                    structureId, ownerId, structDef, new CellCoord(0, 0, 0))
                {
                    IsOperational = g.operational == 1,
                };

                bool available = w.Availability.CanRecruit(w.State, w.Nation, structureId, "u");
                var result = w.Router.Dispatch(new RecruitUnitCommand(w.Nation.Id, structureId, "u"), w.State);

                return available == result.Accepted;
            }));
        }

        // ==================================================================
        // Property 30 (Place Structure): CanPlaceStructure iff a PlaceStructureCommand is accepted.
        // ==================================================================

        /// <summary>
        /// The place-Structure control is enabled iff a placement command would be accepted for a
        /// valid, unoccupied cell: the type is unlocked and both the Resource and population costs are
        /// met (Req 7.5, mirroring the BaseSystem handler). Terrain validity for the specific cell is
        /// held constant (solid, unoccupied) so it never confounds the "iff".
        ///
        /// **Validates: Requirements 7.5**
        /// </summary>
        [Test]
        [Category("Property 30")]
        public void Property30_PlaceStructure_AvailableIffCommandAccepted()
        {
            var gen =
                from nationEra in Gen.Choose(0, 8)
                from structEra in Gen.Choose(0, 8)
                from costStone in Gen.Choose(0, 60)
                from stock in Gen.Choose(0, 60)
                from popCost in Gen.Choose(0, 20)
                from popAvail in Gen.Choose(0, 20)
                from eliminated in Gen.Choose(0, 1)
                from ox in Gen.Choose(0, 15)
                from oz in Gen.Choose(0, 15)
                select new { nationEra, structEra, costStone, stock, popCost, popAvail, eliminated, ox, oz };

            Check(Prop.ForAll(Arb.From(gen), g =>
            {
                var cost = g.costStone > 0 ? ResourceCost.Single(ResourceType.Stone, g.costStone) : ResourceCost.Free;
                var structDef = new StructureDef(
                    "s", (Era)g.structEra, cost, buildTimeSeconds: 1f, populationCost: g.popCost,
                    maxHealth: 100, footprintWidth: 1, footprintLength: 1, function: StructureFunction.Barracks);

                var catalog = new InMemoryCatalog(structures: new[] { structDef });
                var w = NewWorld(catalog, (Era)g.nationEra, g.popAvail, g.eliminated == 1);

                w.Resources.Produce(w.Nation, ResourceType.Stone, g.stock);

                bool available = w.Availability.CanPlaceStructure(w.Nation, "s");
                var result = w.Router.Dispatch(
                    new PlaceStructureCommand(w.Nation.Id, "s", new CellCoord(g.ox, 0, g.oz)), w.State);

                return available == result.Accepted;
            }));
        }

        // ==================================================================
        // Property 30 (Research): CanResearch iff a StartResearchCommand is accepted.
        // ==================================================================

        /// <summary>
        /// The initiate-research control is enabled iff a research command would be accepted: the
        /// Technology is available (Era reached, prerequisites complete), not already completed or in
        /// progress, and its Research cost is affordable (Req 7.5, mirroring the TechSystem handler).
        ///
        /// **Validates: Requirements 7.5**
        /// </summary>
        [Test]
        [Category("Property 30")]
        public void Property30_Research_AvailableIffCommandAccepted()
        {
            var gen =
                from nationEra in Gen.Choose(0, 8)
                from techEra in Gen.Choose(0, 8)
                from costResearch in Gen.Choose(0, 60)
                from stock in Gen.Choose(0, 60)
                from preComplete in Gen.Choose(0, 1)
                from preProgress in Gen.Choose(0, 1)
                from eliminated in Gen.Choose(0, 1)
                select new { nationEra, techEra, costResearch, stock, preComplete, preProgress, eliminated };

            Check(Prop.ForAll(Arb.From(gen), g =>
            {
                var researchCost = g.costResearch > 0
                    ? ResourceCost.Single(ResourceType.Research, g.costResearch)
                    : ResourceCost.Free;
                var techDef = new TechnologyDef("t", (Era)g.techEra, researchCost);

                var catalog = new InMemoryCatalog(technologies: new[] { techDef });
                var w = NewWorld(catalog, (Era)g.nationEra, population: 0, eliminated: g.eliminated == 1);

                w.Resources.Produce(w.Nation, ResourceType.Research, g.stock);

                if (g.preComplete == 1)
                {
                    w.Nation.CompletedTechIds.Add("t");
                }

                if (g.preProgress == 1)
                {
                    w.Nation.ResearchProgress["t"] = 0f;
                }

                bool available = w.Availability.CanResearch(w.Nation, "t");
                var result = w.Router.Dispatch(new StartResearchCommand(w.Nation.Id, "t"), w.State);

                return available == result.Accepted;
            }));
        }

        // ==================================================================
        // Property 30 (Form Battalion): CanFormBattalion iff a FormBattalionCommand is accepted.
        // ==================================================================

        /// <summary>
        /// The form-Battalion control is enabled iff a form-Battalion command would be accepted: at
        /// least two distinct, living Units owned by the Nation are referenced (Req 7.5, mirroring the
        /// UnitSystem handler). Duplicate ids and non-existent (ghost) ids are honoured identically by
        /// both the predicate and the handler.
        ///
        /// **Validates: Requirements 7.5**
        /// </summary>
        [Test]
        [Category("Property 30")]
        public void Property30_FormBattalion_AvailableIffCommandAccepted()
        {
            var unitSpecGen =
                from owned in Gen.Choose(0, 1)
                from health in Gen.Choose(-5, 100)
                select new { owned, health };

            var gen =
                from specs in Gen.ArrayOf(unitSpecGen)
                from eliminated in Gen.Choose(0, 1)
                from includeDuplicate in Gen.Choose(0, 1)
                from includeGhost in Gen.Choose(0, 1)
                select new { specs, eliminated, includeDuplicate, includeGhost };

            Check(Prop.ForAll(Arb.From(gen), g =>
            {
                var catalog = new InMemoryCatalog();
                var w = NewWorld(catalog, Era.Prehistoric, population: 0, eliminated: g.eliminated == 1);

                var def = new UnitDef(
                    "member", Era.Prehistoric, ResourceCost.Free, 0f, 0, 100, 1, 1, 1f, UnitRole.Soldier);

                var unitIds = new List<int>();
                for (int i = 0; i < g.specs.Length; i++)
                {
                    int unitId = i + 1;
                    int ownerId = g.specs[i].owned == 1 ? w.Nation.Id : w.Nation.Id + 99;
                    w.State.Units[unitId] = new UnitInstance(unitId, ownerId, def, WorldPosition.Zero)
                    {
                        Health = g.specs[i].health,
                    };
                    unitIds.Add(unitId);
                }

                // Optionally reference a duplicate id and a non-existent id to exercise dedup/ghost
                // handling; both the predicate and the handler must treat these identically.
                if (g.includeDuplicate == 1 && unitIds.Count > 0)
                {
                    unitIds.Add(unitIds[0]);
                }

                if (g.includeGhost == 1)
                {
                    unitIds.Add(999999);
                }

                bool available = w.Availability.CanFormBattalion(w.State, w.Nation, unitIds);
                var result = w.Router.Dispatch(new FormBattalionCommand(w.Nation.Id, "Bn", unitIds), w.State);

                return available == result.Accepted;
            }));
        }
    }
}
