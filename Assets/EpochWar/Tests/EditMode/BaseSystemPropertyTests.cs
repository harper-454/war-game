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
    /// Property-based tests for <see cref="BaseSystem"/> (Requirement 4, plus the Peace_Arch
    /// clauses Req 10.2 / 10.4), validating the universal correctness properties from design.md.
    /// Each property is universally quantified over generated inputs and exercised for at least the
    /// design-mandated minimum of 100 generated cases (see design.md, "Testing Strategy"). Every
    /// test is tagged <c>Feature: epoch-war-game</c> and its <c>Property N</c>, and carries the
    /// requirement it validates.
    ///
    /// Covered properties:
    /// <list type="bullet">
    ///   <item>Property 17 — Valid placement deducts cost and begins construction (Req 4.1).</item>
    ///   <item>Property 18 — Invalid placement is rejected without state change (Req 4.2).</item>
    ///   <item>Property 19 — Construction completion enables the structure (Req 4.3).</item>
    ///   <item>Property 20 — Under-construction structures have disabled functions (Req 4.4).</item>
    ///   <item>Property 21 — Zero-health structures are removed and disabled (Req 4.5).</item>
    ///   <item>Property 22 — Placeable structures are exactly the unlocked set (Req 4.6).</item>
    ///   <item>Property 37 — Placing the Peace_Arch begins construction and pays cost (Req 10.2).</item>
    ///   <item>Property 39 — Destroying an incomplete Peace_Arch withholds victory (Req 10.4).</item>
    /// </list>
    ///
    /// <see cref="BaseSystem"/> depends on a <see cref="ResourceSystem"/> (cost payment),
    /// <see cref="CivSystem"/> (population), and <see cref="TechSystem"/> (unlock/Peace_Arch gating),
    /// so every scenario constructs all four against a small in-memory catalog and a
    /// <see cref="MatchState"/> whose terrain is a solid volume (structures are anchored on the world
    /// floor, so placement is always supported). Tests target the engine-free <c>EpochWar.Core</c>
    /// assembly with no Unity Play loop.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    public sealed class BaseSystemPropertyTests
    {
        // Every property in this feature runs at least this many generated cases.
        private const int MinimumIterations = 100;

        private const string PeacePrereqTechId = "peace_prereq";
        private const string PeaceArchId = "peace_arch";
        private const string BarracksId = "barracks";

        private static void Check(Property property)
        {
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;
            property.Check(config);
        }

        // ------------------------------------------------------------------
        // Content / world builders (fresh per generated case so BaseSystem's
        // derived bookkeeping never leaks across iterations).
        // ------------------------------------------------------------------

        /// <summary>A solid terrain volume; anything anchored at Y=0 rests on the world floor.</summary>
        private static TerrainVolume SolidTerrain(int x = 16, int y = 4, int z = 16)
            => new TerrainVolume(new Int3(x, y, z), CellMaterial.Rock);

        private static StructureDef NormalStructure(
            string id, Era era, ResourceCost cost, float buildTimeSeconds,
            int width = 1, int length = 1, int maxHealth = 100)
            => new StructureDef(
                id, era, cost, buildTimeSeconds, populationCost: 0, maxHealth: maxHealth,
                footprintWidth: width, footprintLength: length, function: StructureFunction.Barracks);

        private static StructureDef PeaceArchStructure(ResourceCost cost, float buildTimeSeconds)
            => new StructureDef(
                PeaceArchId, Era.Prehistoric, cost, buildTimeSeconds, populationCost: 0,
                maxHealth: 100, footprintWidth: 1, footprintLength: 1,
                function: StructureFunction.Wonder, isPeaceArch: true);

        private static TechnologyDef PeacePrereqTech()
            => new TechnologyDef(
                PeacePrereqTechId, Era.Prehistoric, ResourceCost.Free,
                category: TechCategory.PeaceArchPrereq);

        private sealed class World
        {
            public ResourceSystem Resources;
            public CivSystem Civ;
            public TechSystem Tech;
            public BaseSystem Base;
            public MatchState State;
            public Nation Nation;
        }

        private static World NewWorld(InMemoryCatalog catalog, Era era = Era.Prehistoric)
        {
            var resources = new ResourceSystem();
            var civ = new CivSystem(resources);
            var tech = new TechSystem(catalog, resources);
            var baseSys = new BaseSystem(catalog, resources, civ, tech);

            var nation = new Nation(1, currentEra: era)
            {
                // Plenty of population so placement is never gated on population (Req 5.4 is out of
                // scope for these Structure-placement properties).
                Population = 10000,
                PopulationCapacity = 10000,
            };

            var state = new MatchState(SolidTerrain());
            state.Nations[nation.Id] = nation;

            return new World
            {
                Resources = resources,
                Civ = civ,
                Tech = tech,
                Base = baseSys,
                State = state,
                Nation = nation,
            };
        }

        // ==================================================================
        // Property 17: Valid placement deducts cost and begins construction (Req 4.1)
        // ==================================================================

        /// <summary>
        /// For any unlocked Structure placed on a valid, unoccupied Terrain location the Nation can
        /// afford, the Resource cost is deducted exactly and a construction-in-progress Structure is
        /// created at that location.
        ///
        /// **Validates: Requirements 4.1**
        /// </summary>
        [Test]
        [Category("Property 17")]
        public void Property17_ValidPlacement_DeductsCostAndBeginsConstruction()
        {
            var gen =
                from metalCost in Gen.Choose(1, 50)
                from woodCost in Gen.Choose(1, 50)
                from extra in Gen.Choose(0, 50)
                from buildTenths in Gen.Choose(1, 300)
                from ox in Gen.Choose(0, 15)
                from oz in Gen.Choose(0, 15)
                select new { metalCost, woodCost, extra, buildTenths, ox, oz };

            Check(Prop.ForAll(Arb.From(gen), g =>
            {
                var cost = ResourceCost.Of((ResourceType.Metal, g.metalCost), (ResourceType.Wood, g.woodCost));
                var catalog = new InMemoryCatalog(
                    structures: new[] { NormalStructure(BarracksId, Era.Prehistoric, cost, g.buildTenths / 10f) });
                var w = NewWorld(catalog);

                w.Resources.Produce(w.Nation, ResourceType.Metal, g.metalCost + g.extra);
                w.Resources.Produce(w.Nation, ResourceType.Wood, g.woodCost + g.extra);

                var origin = new CellCoord(g.ox, 0, g.oz);
                var result = w.Base.Handle(new PlaceStructureCommand(w.Nation.Id, BarracksId, origin), w.State);

                if (!result.Accepted)
                {
                    return false;
                }

                // Cost deducted exactly: each affected store drops by exactly its component.
                bool costDeducted =
                    w.Resources.GetAmount(w.Nation, ResourceType.Metal) == g.extra
                    && w.Resources.GetAmount(w.Nation, ResourceType.Wood) == g.extra;

                // Exactly one construction-in-progress Structure was created at the location.
                if (w.State.Structures.Count != 1)
                {
                    return false;
                }

                var structure = w.State.Structures.Values.Single();
                bool createdInProgress =
                    !structure.IsOperational
                    && structure.ConstructionProgress == 0f
                    && structure.Def.Id == BarracksId
                    && structure.Origin == origin
                    && structure.OwnerNationId == w.Nation.Id;

                // A placement event was emitted (non-Peace_Arch), and the footprint is occupied.
                bool placedEvent = result.Events
                    .OfType<StructurePlacedEvent>()
                    .Any(e => e.StructureId == structure.Id && !e.IsPeaceArch && e.Origin == origin);

                bool occupied = w.Base.OccupiedCellCount == 1;

                return costDeducted && createdInProgress && placedEvent && occupied;
            }));
        }

        // ==================================================================
        // Property 18: Invalid placement is rejected without state change (Req 4.2)
        // ==================================================================

        /// <summary>
        /// For any Structure placement targeting an occupied or invalid (out-of-bounds) Terrain
        /// location, the placement is rejected and the Nation's Resources (and match structures) are
        /// unchanged.
        ///
        /// **Validates: Requirements 4.2**
        /// </summary>
        [Test]
        [Category("Property 18")]
        public void Property18_InvalidPlacement_RejectedWithoutStateChange()
        {
            var gen =
                from occupiedCase in Gen.Choose(0, 1)   // 0 = out-of-bounds, 1 = occupied overlap
                from metalCost in Gen.Choose(1, 40)
                from ox in Gen.Choose(0, 15)
                from oz in Gen.Choose(0, 15)
                select new { occupiedCase, metalCost, ox, oz };

            Check(Prop.ForAll(Arb.From(gen), g =>
            {
                var cost = ResourceCost.Single(ResourceType.Metal, g.metalCost);
                var catalog = new InMemoryCatalog(
                    structures: new[] { NormalStructure(BarracksId, Era.Prehistoric, cost, 5f) });
                var w = NewWorld(catalog);

                // Enough for several placements, so a rejection is never confused with unaffordability.
                w.Resources.Produce(w.Nation, ResourceType.Metal, (g.metalCost * 10) + 1000);

                if (g.occupiedCase == 0)
                {
                    // Out-of-bounds: origin X sits on the volume's upper (exclusive) edge.
                    var badOrigin = new CellCoord(w.State.Terrain.Dimensions.X, 0, g.oz);

                    float before = w.Resources.GetAmount(w.Nation, ResourceType.Metal);
                    var result = w.Base.Handle(
                        new PlaceStructureCommand(w.Nation.Id, BarracksId, badOrigin), w.State);

                    return !result.Accepted
                           && w.Resources.GetAmount(w.Nation, ResourceType.Metal) == before
                           && w.State.Structures.Count == 0
                           && w.Base.OccupiedCellCount == 0;
                }

                // Occupied overlap: place once validly, then attempt to place on the same cell.
                var origin = new CellCoord(g.ox, 0, g.oz);
                var first = w.Base.Handle(new PlaceStructureCommand(w.Nation.Id, BarracksId, origin), w.State);
                if (!first.Accepted)
                {
                    return false; // the setup placement must succeed for the overlap test to be meaningful
                }

                float afterFirst = w.Resources.GetAmount(w.Nation, ResourceType.Metal);
                int structuresAfterFirst = w.State.Structures.Count;
                int occupiedAfterFirst = w.Base.OccupiedCellCount;

                var second = w.Base.Handle(new PlaceStructureCommand(w.Nation.Id, BarracksId, origin), w.State);

                // The overlapping second placement changes nothing.
                return !second.Accepted
                       && w.Resources.GetAmount(w.Nation, ResourceType.Metal) == afterFirst
                       && w.State.Structures.Count == structuresAfterFirst
                       && w.Base.OccupiedCellCount == occupiedAfterFirst;
            }));
        }

        // ==================================================================
        // Property 19: Construction completion enables the structure (Req 4.3)
        // ==================================================================

        /// <summary>
        /// For any Structure, once accumulated construction time reaches its build time, the
        /// Structure becomes operational with its functions enabled and a completion event is emitted.
        ///
        /// **Validates: Requirements 4.3**
        /// </summary>
        [Test]
        [Category("Property 19")]
        public void Property19_ConstructionCompletion_EnablesTheStructure()
        {
            var gen =
                from buildTenths in Gen.Choose(1, 200)  // 0.1 .. 20.0 s
                from dtTenths in Gen.Choose(1, 50)      // 0.1 .. 5.0 s/step
                select new { buildTenths, dtTenths };

            Check(Prop.ForAll(Arb.From(gen), g =>
            {
                float buildTime = g.buildTenths / 10f;
                float dt = g.dtTenths / 10f;

                var catalog = new InMemoryCatalog(
                    structures: new[] { NormalStructure(BarracksId, Era.Prehistoric, ResourceCost.Free, buildTime) });
                var w = NewWorld(catalog);

                var origin = new CellCoord(1, 0, 1);
                var result = w.Base.Handle(new PlaceStructureCommand(w.Nation.Id, BarracksId, origin), w.State);
                if (!result.Accepted)
                {
                    return false;
                }

                var structure = w.State.Structures.Values.Single();

                // Advance enough fixed steps to guarantee the accumulated time reaches the build time.
                int steps = (int)(buildTime / dt) + 2;
                bool sawCompletion = false;
                for (int i = 0; i < steps; i++)
                {
                    var events = w.Base.Tick(w.State, dt);
                    if (events.OfType<StructureConstructionCompletedEvent>()
                        .Any(e => e.StructureId == structure.Id))
                    {
                        sawCompletion = true;
                    }
                }

                // Once construction time elapsed, the Structure is operational (functions enabled).
                return structure.IsOperational
                       && structure.ConstructionProgress >= buildTime
                       && sawCompletion;
            }));
        }

        // ==================================================================
        // Property 20: Under-construction structures have disabled functions (Req 4.4)
        // ==================================================================

        /// <summary>
        /// For any Structure whose accumulated construction time is less than its build time, its
        /// production/command functions are disabled (it is never operational).
        ///
        /// **Validates: Requirements 4.4**
        /// </summary>
        [Test]
        [Category("Property 20")]
        public void Property20_UnderConstructionStructures_HaveDisabledFunctions()
        {
            // buildTime >= 10 while dt <= 1.0 and ticks <= 8 keeps the accumulated time strictly
            // below the build time, so the Structure must remain under construction throughout.
            var gen =
                from buildTenths in Gen.Choose(100, 500) // 10.0 .. 50.0 s
                from dtTenths in Gen.Choose(1, 10)       // 0.1 .. 1.0 s/step
                from ticks in Gen.Choose(0, 8)
                select new { buildTenths, dtTenths, ticks };

            Check(Prop.ForAll(Arb.From(gen), g =>
            {
                float buildTime = g.buildTenths / 10f;
                float dt = g.dtTenths / 10f;

                var catalog = new InMemoryCatalog(
                    structures: new[] { NormalStructure(BarracksId, Era.Prehistoric, ResourceCost.Free, buildTime) });
                var w = NewWorld(catalog);

                var origin = new CellCoord(2, 0, 3);
                var result = w.Base.Handle(new PlaceStructureCommand(w.Nation.Id, BarracksId, origin), w.State);
                if (!result.Accepted)
                {
                    return false;
                }

                var structure = w.State.Structures.Values.Single();

                for (int i = 0; i < g.ticks; i++)
                {
                    w.Base.Tick(w.State, dt);

                    // While accumulated progress is below the build time, functions stay disabled.
                    if (structure.ConstructionProgress < buildTime && structure.IsOperational)
                    {
                        return false;
                    }
                }

                // The chosen bounds guarantee we never reached the build time.
                return structure.ConstructionProgress < buildTime && !structure.IsOperational;
            }));
        }

        // ==================================================================
        // Property 21: Zero-health structures are removed and disabled (Req 4.5)
        // ==================================================================

        /// <summary>
        /// For any Structure whose health reaches zero, the Structure is absent from the Match, its
        /// footprint is freed, and a removal event is emitted.
        ///
        /// **Validates: Requirements 4.5**
        /// </summary>
        [Test]
        [Category("Property 21")]
        public void Property21_ZeroHealthStructures_AreRemovedAndDisabled()
        {
            var gen =
                from completeFirst in Gen.Choose(0, 1)
                from finalHealth in Gen.Choose(-20, 0)
                from ox in Gen.Choose(0, 15)
                from oz in Gen.Choose(0, 15)
                select new { completeFirst, finalHealth, ox, oz };

            Check(Prop.ForAll(Arb.From(gen), g =>
            {
                var catalog = new InMemoryCatalog(
                    structures: new[] { NormalStructure(BarracksId, Era.Prehistoric, ResourceCost.Free, 3f) });
                var w = NewWorld(catalog);

                var origin = new CellCoord(g.ox, 0, g.oz);
                var result = w.Base.Handle(new PlaceStructureCommand(w.Nation.Id, BarracksId, origin), w.State);
                if (!result.Accepted)
                {
                    return false;
                }

                var structure = w.State.Structures.Values.Single();
                int structureId = structure.Id;

                if (g.completeFirst == 1)
                {
                    // Drive it operational, then destroy it, to cover destroying a live Structure.
                    w.Base.Tick(w.State, 10f);
                }

                // Health reaches zero (or below): the destroyed-structure sweep must remove it.
                structure.Health = g.finalHealth;
                var events = w.Base.Tick(w.State, 1f);

                bool absent = !w.State.Structures.ContainsKey(structureId);
                bool footprintFreed = w.Base.OccupiedCellCount == 0;
                bool removedEvent = events.OfType<StructureRemovedEvent>()
                    .Any(e => e.StructureId == structureId);

                return absent && footprintFreed && removedEvent;
            }));
        }

        // ==================================================================
        // Property 22: Placeable structures are exactly the unlocked set (Req 4.6)
        // ==================================================================

        /// <summary>
        /// For any Nation state, the set of placeable (non-Peace_Arch) Structure types is exactly the
        /// set unlocked by the Nation's current Era: a Structure is placeable if and only if its Era
        /// is at or below the Nation's current Era. An unknown Structure type is never placeable.
        ///
        /// **Validates: Requirements 4.6**
        /// </summary>
        [Test]
        [Category("Property 22")]
        public void Property22_PlaceableStructures_AreExactlyTheUnlockedSet()
        {
            var eraGen = from e in Gen.Choose(0, 8) select (Era)e;

            var gen =
                from target in eraGen
                from s0 in eraGen
                from s1 in eraGen
                from s2 in eraGen
                from s3 in eraGen
                select new { target, s0, s1, s2, s3 };

            Check(Prop.ForAll(Arb.From(gen), g =>
            {
                var defs = new[]
                {
                    NormalStructure("S0", g.s0, ResourceCost.Free, 1f),
                    NormalStructure("S1", g.s1, ResourceCost.Free, 1f),
                    NormalStructure("S2", g.s2, ResourceCost.Free, 1f),
                    NormalStructure("S3", g.s3, ResourceCost.Free, 1f),
                };
                var catalog = new InMemoryCatalog(structures: defs);
                var w = NewWorld(catalog, g.target);

                var unlocked = w.Tech.GetUnlockedStructureIds(w.Nation);

                foreach (var def in defs)
                {
                    // Independent expectation: era-only unlock (no completed techs in this scenario).
                    bool expected = def.Era <= g.target;
                    bool placeable = w.Base.IsStructurePlaceable(w.Nation, def.Id);

                    if (placeable != expected || placeable != unlocked.Contains(def.Id))
                    {
                        return false;
                    }
                }

                // An unknown Structure type is never placeable.
                return !w.Base.IsStructurePlaceable(w.Nation, "does-not-exist");
            }));
        }

        // ==================================================================
        // Property 37: Placing the Peace_Arch begins construction and pays cost (Req 10.2)
        // ==================================================================

        /// <summary>
        /// For any Nation that can afford the Peace_Arch and places it validly (its prerequisite
        /// Technologies complete), its Resource cost is deducted and construction begins — the wonder
        /// starts under construction and is not yet counted as completed.
        ///
        /// **Validates: Requirements 10.2**
        /// </summary>
        [Test]
        [Category("Property 37")]
        public void Property37_PlacingPeaceArch_BeginsConstructionAndPaysCost()
        {
            var gen =
                from stoneCost in Gen.Choose(1, 60)
                from extra in Gen.Choose(0, 60)
                from buildTenths in Gen.Choose(1, 300)
                from ox in Gen.Choose(0, 15)
                from oz in Gen.Choose(0, 15)
                select new { stoneCost, extra, buildTenths, ox, oz };

            Check(Prop.ForAll(Arb.From(gen), g =>
            {
                var cost = ResourceCost.Single(ResourceType.Stone, g.stoneCost);
                var catalog = new InMemoryCatalog(
                    structures: new[] { PeaceArchStructure(cost, g.buildTenths / 10f) },
                    technologies: new[] { PeacePrereqTech() });
                var w = NewWorld(catalog);

                // Complete the Peace_Arch prerequisite so the wonder is available (Req 10.1).
                w.Nation.CompletedTechIds.Add(PeacePrereqTechId);
                w.Resources.Produce(w.Nation, ResourceType.Stone, g.stoneCost + g.extra);

                var origin = new CellCoord(g.ox, 0, g.oz);
                var result = w.Base.Handle(new PlaceStructureCommand(w.Nation.Id, PeaceArchId, origin), w.State);

                if (!result.Accepted)
                {
                    return false;
                }

                bool costDeducted = w.Resources.GetAmount(w.Nation, ResourceType.Stone) == g.extra;

                if (w.State.Structures.Count != 1)
                {
                    return false;
                }

                var arch = w.State.Structures.Values.Single();
                bool beganConstruction =
                    !arch.IsOperational
                    && arch.ConstructionProgress == 0f
                    && arch.Def.IsPeaceArch;

                bool placedEvent = result.Events
                    .OfType<StructurePlacedEvent>()
                    .Any(e => e.StructureId == arch.Id && e.IsPeaceArch);

                // Placement alone does not win the Peace victory — completion happens later (Req 10.3).
                bool notYetCompleted = !w.Base.HasCompletedPeaceArch(w.Nation.Id);

                return costDeducted && beganConstruction && placedEvent && notYetCompleted;
            }));
        }

        // ==================================================================
        // Property 39: Destroying an incomplete Peace_Arch withholds victory (Req 10.4)
        // ==================================================================

        /// <summary>
        /// For any Peace_Arch destroyed before construction completes, no Peace victory is awarded to
        /// its owner: the wonder is removed as an incomplete Peace_Arch, no completion is ever
        /// recorded, and no completion event is emitted.
        ///
        /// **Validates: Requirements 10.4**
        /// </summary>
        [Test]
        [Category("Property 39")]
        public void Property39_DestroyingIncompletePeaceArch_WithholdsVictory()
        {
            // buildTime >= 10 with dt <= 1.0 and preTicks <= 8 keeps progress strictly below the
            // build time, so the wonder is still under construction when it is destroyed.
            var gen =
                from buildTenths in Gen.Choose(100, 500) // 10.0 .. 50.0 s
                from dtTenths in Gen.Choose(1, 10)       // 0.1 .. 1.0 s/step
                from preTicks in Gen.Choose(0, 8)
                select new { buildTenths, dtTenths, preTicks };

            Check(Prop.ForAll(Arb.From(gen), g =>
            {
                float buildTime = g.buildTenths / 10f;
                float dt = g.dtTenths / 10f;

                var catalog = new InMemoryCatalog(
                    structures: new[] { PeaceArchStructure(ResourceCost.Free, buildTime) },
                    technologies: new[] { PeacePrereqTech() });
                var w = NewWorld(catalog);

                w.Nation.CompletedTechIds.Add(PeacePrereqTechId);

                var origin = new CellCoord(4, 0, 4);
                var result = w.Base.Handle(new PlaceStructureCommand(w.Nation.Id, PeaceArchId, origin), w.State);
                if (!result.Accepted)
                {
                    return false;
                }

                var arch = w.State.Structures.Values.Single();
                int archId = arch.Id;

                // Advance construction partway, staying strictly under the build time.
                bool sawCompletionBeforeDestroy = false;
                for (int i = 0; i < g.preTicks; i++)
                {
                    var events = w.Base.Tick(w.State, dt);
                    if (events.OfType<PeaceArchCompletedEvent>().Any())
                    {
                        sawCompletionBeforeDestroy = true;
                    }
                }

                // Pre-destruction invariants: still building, victory not (yet) awarded.
                if (arch.IsOperational
                    || arch.ConstructionProgress >= buildTime
                    || sawCompletionBeforeDestroy
                    || w.Base.HasCompletedPeaceArch(w.Nation.Id))
                {
                    return false;
                }

                // Destroy the incomplete wonder: the sweep runs before construction advances, so it
                // is removed this tick without ever completing.
                arch.Health = 0;
                var tickEvents = w.Base.Tick(w.State, dt);

                bool removedAsIncomplete = tickEvents.OfType<StructureRemovedEvent>()
                    .Any(e => e.StructureId == archId && e.WasIncompletePeaceArch);
                bool noCompletionEmitted = !tickEvents.OfType<PeaceArchCompletedEvent>().Any();
                bool absent = !w.State.Structures.ContainsKey(archId);
                bool victoryWithheld = !w.Base.HasCompletedPeaceArch(w.Nation.Id);

                return removedAsIncomplete && noCompletionEmitted && absent && victoryWithheld;
            }));
        }
    }
}
