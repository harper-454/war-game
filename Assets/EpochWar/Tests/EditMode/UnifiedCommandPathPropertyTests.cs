using System;
using System.Collections.Generic;
using System.Linq;
using EpochWar.Core.Commands;
using EpochWar.Core.Simulation;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Core.Systems;
using FsCheck;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Property-based test for the single authoritative command pipeline (Requirement 8.5),
    /// covering the universal property defined in design.md ("Correctness Properties"):
    ///
    /// <list type="bullet">
    /// <item>Property 31 — AI and human commands share one authoritative path (Req 8.5).</item>
    /// </list>
    ///
    /// The design keeps <em>one</em> <see cref="CommandRouter"/> inside the
    /// <see cref="MatchSimulation"/> and routes both human client intents (enqueued directly via
    /// <see cref="MatchSimulation.EnqueueCommand"/>) and AI_Nation intents (produced by an
    /// <see cref="IAiController"/> and enqueued by <see cref="MatchBootstrapper.Tick"/>) through it.
    /// Because there is exactly one validate → apply → events path, an AI command and an equivalent
    /// human command must produce the same resulting <see cref="MatchState"/>.
    ///
    /// The test verifies this end to end: for each generated command it seeds two identical Matches
    /// and applies the command two ways —
    /// <list type="number">
    /// <item>(human) enqueued directly on the engine of a Match whose issuing Nation is a human
    /// Player, then advanced;</item>
    /// <item>(AI) produced by a <see cref="DelegateAiController"/> for the <em>same</em> Nation id in
    /// a Match whose issuing Nation is an AI_Nation, and enqueued by the bootstrapper, then
    /// advanced.</item>
    /// </list>
    /// Both Matches are ticked the same number of fixed steps and their resulting authoritative state
    /// (resource balances, tech/era progression, population, units, structures, battalions) must be
    /// equivalent. The generated commands span every system that owns a handler (research, era
    /// advancement, recruitment, battalion forming, movement) and include intentionally
    /// invalid/unaffordable variants so the property is exercised for both accepted and rejected
    /// commands.
    ///
    /// The catalog is built in-test from plain <see cref="EpochWar.Core"/> POCOs (no
    /// <c>UnityEngine</c> / <c>ContentSeed</c> dependency), matching the engine-free EditMode
    /// convention. Every property runs at least <see cref="MinimumIterations"/> generated cases.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    public sealed class UnifiedCommandPathPropertyTests
    {
        // The minimum number of generated cases every property in this feature must run.
        private const int MinimumIterations = 100;

        // The Nation whose command is issued from a human path in one Match and an AI path in the
        // other. A second (opponent) Nation keeps the Annihilation victory condition from resolving
        // vacuously so ticking never ends the Match under test.
        private const int SubjectNationId = 1;
        private const int OpponentNationId = 2;

        // A pre-seeded operational Structure the recruit command targets (barracks id).
        private const int BarracksId = 500;

        // Terrain surface convention: an 8x4x8 solid volume has its top solid layer at y=3.
        private const int SurfaceY = 3;

        private static readonly int[] KindRange = { 0, 1, 2, 3, 4 };

        // The tech ids a StartResearch command may target: several affordable/available Prehistoric
        // techs, an unaffordably-expensive one, an Era-gated one, and a non-existent one, so both
        // acceptance and every flavour of rejection are exercised.
        private static readonly string[] TechIds =
            { "toolmaking", "fire", "wheel", "expensive", "future", "does-not-exist" };

        private static void CheckAtLeast100<T>(Arbitrary<T> arb, Func<T, bool> body)
        {
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;
            Prop.ForAll(arb, body).Check(config);
        }

        /// <summary>
        /// Property 31: AI and human commands share one authoritative path.
        /// For any command, whether issued by a human Player or an AI_Nation, it is validated and
        /// applied through the identical authoritative pipeline, producing the same resulting state
        /// as an equivalent command from the other source.
        ///
        /// **Validates: Requirements 8.5**
        /// </summary>
        [Test]
        [Category("Property 31: AI and human commands share one authoritative path")]
        public void Property31_HumanAndAiCommand_ProduceEquivalentState()
        {
            var gen =
                from kind in Gen.Elements(KindRange)
                from techId in Gen.Elements(TechIds)
                from research in Gen.Choose(0, 60)
                from unitCount in Gen.Choose(0, 3)
                from destX in Gen.Choose(0, 7)
                from destZ in Gen.Choose(0, 7)
                select new CommandSpec(kind, techId, research, unitCount, destX, destZ);

            CheckAtLeast100(gen.ToArbitrary(), spec =>
            {
                // Two identically-seeded Matches. The only intended difference is the source of the
                // command: a human Player in one, an AI_Nation in the other.
                var human = BuildMatch(spec, subjectIsAi: false);
                var ai = BuildMatch(spec, subjectIsAi: true);

                // Build ONE command instance from the (identical) seeded state and feed it to both
                // paths, so the two runs differ only in how the command reaches the router.
                var command = BuildCommand(spec, human.State, SubjectNationId);

                // Human path: enqueue the intent directly on the engine (as a networked client's
                // intent would arrive), with no AI controllers registered.
                human.EnqueueCommand(command);

                // AI path: a controller for the SAME Nation id emits the equivalent command on the
                // first tick; the bootstrapper enqueues it through the same entry point.
                ai.AddAiController(new DelegateAiController(
                    SubjectNationId,
                    (state, tick) => tick == 0
                        ? new[] { BuildCommand(spec, state, SubjectNationId) }
                        : Array.Empty<ICommand>()));

                // Advance both Matches the same number of fixed steps. The command lands on tick 0 in
                // both; the extra ticks let any time-based consequence (research/build progress,
                // movement) resolve identically.
                for (int i = 0; i < 3; i++)
                {
                    human.Tick(1f);
                    ai.Tick(1f);
                }

                return AreEquivalent(human.State, ai.State);
            });
        }

        // ------------------------------------------------------------------
        // Match assembly (identical seed for both the human and AI runs)
        // ------------------------------------------------------------------

        private static MatchBootstrapper BuildMatch(CommandSpec spec, bool subjectIsAi)
        {
            var catalog = BuildCatalog();
            // Each Match gets its own (mutable) terrain instance so the two runs never share state.
            var terrain = new TerrainVolume(new Int3(8, 4, 8), CellMaterial.Soil);

            var seeds = new[]
            {
                new NationSeed(
                    nationId: SubjectNationId,
                    isAI: subjectIsAi,
                    startingResources: BuildResources(spec.StartingResearch),
                    startingUnits: BuildStartingUnits(catalog),
                    startingPopulation: 10,
                    startingPopulationCapacity: 20),
                new NationSeed(
                    nationId: OpponentNationId,
                    isAI: false,
                    startingResources: BuildResources(spec.StartingResearch),
                    startingPopulation: 10,
                    startingPopulationCapacity: 20),
            };

            var bootstrapper = MatchBootstrapper.Create(catalog, terrain, seeds);

            // Pre-place an operational barracks for the subject Nation so the recruit command has a
            // valid, owned, operational producing Structure to target (Req 3.1). Added identically to
            // every Match.
            var barracks = new StructureInstance(
                BarracksId, SubjectNationId, BarracksDef(), new CellCoord(2, SurfaceY, 2))
            {
                IsOperational = true,
            };
            bootstrapper.State.Structures[barracks.Id] = barracks;

            return bootstrapper;
        }

        private static IReadOnlyDictionary<ResourceType, ResourceStore> BuildResources(int research)
            => new Dictionary<ResourceType, ResourceStore>
            {
                [ResourceType.Research] = new ResourceStore(research, 0f),
                [ResourceType.Food] = new ResourceStore(100f, 0f),
            };

        private static IReadOnlyList<UnitSeed> BuildStartingUnits(ICatalog catalog)
        {
            var soldier = catalog.GetUnit("soldier");
            return new[]
            {
                new UnitSeed(soldier, new CellCoord(2, SurfaceY + 1, 2)),
                new UnitSeed(soldier, new CellCoord(3, SurfaceY + 1, 2)),
            };
        }

        // ------------------------------------------------------------------
        // Command construction from a spec (resolved against the seeded state)
        // ------------------------------------------------------------------

        private static ICommand BuildCommand(CommandSpec spec, MatchState refState, int nationId)
        {
            switch (spec.Kind)
            {
                case 0: // Research (TechSystem) — accepted or rejected depending on tech/affordability.
                    return new StartResearchCommand(nationId, spec.TechId);

                case 1: // Era advancement (TechSystem).
                    return new AdvanceEraCommand(nationId);

                case 2: // Recruitment (UnitSystem) at the pre-seeded barracks.
                    return new RecruitUnitCommand(nationId, BarracksId, "soldier");

                case 3: // Form battalion (UnitSystem) from the Nation's own units.
                    return new FormBattalionCommand(nationId, "Alpha", SubjectUnitIds(refState, nationId, spec.UnitCount));

                default: // Move (UnitSystem) the Nation's units toward a destination cell.
                    return new MoveCommand(
                        nationId,
                        SubjectUnitIds(refState, nationId, spec.UnitCount),
                        new CellCoord(spec.DestX, SurfaceY, spec.DestZ));
            }
        }

        // The first <paramref name="count"/> unit ids owned by the Nation, in ascending id order.
        // Because both Matches are seeded identically these ids are identical across the two runs.
        private static int[] SubjectUnitIds(MatchState state, int nationId, int count)
            => state.Units.Values
                .Where(u => u.OwnerNationId == nationId)
                .Select(u => u.Id)
                .OrderBy(id => id)
                .Take(count)
                .ToArray();

        // ------------------------------------------------------------------
        // Catalog (built from engine-free POCOs; no ContentSeed / UnityEngine)
        // ------------------------------------------------------------------

        private static ICatalog BuildCatalog()
        {
            var techs = new[]
            {
                new TechnologyDef("toolmaking", Era.Prehistoric, ResourceCost.Single(ResourceType.Research, 10f)),
                new TechnologyDef("fire", Era.Prehistoric, ResourceCost.Single(ResourceType.Research, 20f)),
                new TechnologyDef("wheel", Era.Prehistoric, ResourceCost.Single(ResourceType.Research, 15f)),
                new TechnologyDef("expensive", Era.Prehistoric, ResourceCost.Single(ResourceType.Research, 100000f)),
                new TechnologyDef("future", Era.Modern, ResourceCost.Single(ResourceType.Research, 5f)),
            };

            var units = new[] { SoldierDef() };
            var structures = new[] { BarracksDef() };

            return new InMemoryCatalog(technologies: techs, units: units, structures: structures);
        }

        private static UnitDef SoldierDef()
            => new UnitDef(
                id: "soldier",
                era: Era.Prehistoric,
                cost: ResourceCost.Single(ResourceType.Food, 5f),
                buildTimeSeconds: 2f,
                populationCost: 1,
                maxHealth: 30,
                attack: 5,
                defense: 2,
                moveSpeed: 1f,
                role: UnitRole.Soldier);

        private static StructureDef BarracksDef()
            => new StructureDef(
                "barracks", Era.Prehistoric, ResourceCost.Free,
                buildTimeSeconds: 0f, populationCost: 0, maxHealth: 100,
                footprintWidth: 1, footprintLength: 1, function: StructureFunction.Barracks);

        // ------------------------------------------------------------------
        // Structural equivalence of two authoritative MatchStates
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns true when the two authoritative states are gameplay-equivalent: identical clock,
        /// lifecycle/outcome, and identical per-Nation economy/tech/population, units, structures, and
        /// battalions. The only field intentionally ignored is <see cref="Nation.IsAI"/>, which is the
        /// controlled difference between the two runs and carries no gameplay effect (Req 8.5).
        /// </summary>
        private static bool AreEquivalent(MatchState a, MatchState b)
        {
            if (a.TickCount != b.TickCount) return false;
            if (a.Status != b.Status) return false;
            if (!OutcomesEqual(a.Outcome, b.Outcome)) return false;

            if (!SameKeys(a.Nations.Keys, b.Nations.Keys)) return false;
            foreach (var kvp in a.Nations)
            {
                if (!NationsEquivalent(kvp.Value, b.Nations[kvp.Key])) return false;
            }

            if (!SameKeys(a.Units.Keys, b.Units.Keys)) return false;
            foreach (var kvp in a.Units)
            {
                if (!UnitsEquivalent(kvp.Value, b.Units[kvp.Key])) return false;
            }

            if (!SameKeys(a.Structures.Keys, b.Structures.Keys)) return false;
            foreach (var kvp in a.Structures)
            {
                if (!StructuresEquivalent(kvp.Value, b.Structures[kvp.Key])) return false;
            }

            return true;
        }

        private static bool OutcomesEqual(MatchOutcome a, MatchOutcome b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            return a.WinningNationId == b.WinningNationId
                && a.Path == b.Path
                && a.CompletionTick == b.CompletionTick;
        }

        private static bool NationsEquivalent(Nation a, Nation b)
        {
            if (a.CurrentEra != b.CurrentEra) return false;
            if (a.Eliminated != b.Eliminated) return false;
            if (a.Population != b.Population) return false;
            if (a.PopulationCapacity != b.PopulationCapacity) return false;

            if (!a.CompletedTechIds.SetEquals(b.CompletedTechIds)) return false;

            if (!DictsEqual(a.ResearchProgress, b.ResearchProgress)) return false;

            // Resource stores: same set of types, each with identical amount and capacity.
            if (a.Resources.Count != b.Resources.Count) return false;
            foreach (var kvp in a.Resources)
            {
                if (!b.Resources.TryGetValue(kvp.Key, out var store)) return false;
                if (store.Amount != kvp.Value.Amount) return false;
                if (store.Capacity != kvp.Value.Capacity) return false;
            }

            // Battalions: same ids, names, and member sets.
            if (!SameKeys(a.Battalions.Keys, b.Battalions.Keys)) return false;
            foreach (var kvp in a.Battalions)
            {
                var other = b.Battalions[kvp.Key];
                if (kvp.Value.Name != other.Name) return false;
                if (!kvp.Value.MemberUnitIds.SetEquals(other.MemberUnitIds)) return false;
            }

            return true;
        }

        private static bool UnitsEquivalent(UnitInstance a, UnitInstance b)
        {
            if (a.OwnerNationId != b.OwnerNationId) return false;
            if (a.Def?.Id != b.Def?.Id) return false;
            if (a.Health != b.Health) return false;
            if (a.Position != b.Position) return false;
            if (a.BattalionId != b.BattalionId) return false;
            return OrdersEquivalent(a.CurrentOrder, b.CurrentOrder);
        }

        private static bool OrdersEquivalent(UnitOrder a, UnitOrder b)
        {
            if (a.Kind != b.Kind) return false;
            if (a.Destination != b.Destination) return false;
            if (a.WaypointIndex != b.WaypointIndex) return false;
            if (a.Path.Count != b.Path.Count) return false;
            for (int i = 0; i < a.Path.Count; i++)
            {
                if (a.Path[i] != b.Path[i]) return false;
            }

            return true;
        }

        private static bool StructuresEquivalent(StructureInstance a, StructureInstance b)
        {
            if (a.OwnerNationId != b.OwnerNationId) return false;
            if (a.Def?.Id != b.Def?.Id) return false;
            if (a.Health != b.Health) return false;
            if (a.Origin != b.Origin) return false;
            if (a.ConstructionProgress != b.ConstructionProgress) return false;
            return a.IsOperational == b.IsOperational;
        }

        private static bool SameKeys<T>(IEnumerable<T> a, IEnumerable<T> b)
        {
            var setA = new HashSet<T>(a);
            var setB = new HashSet<T>(b);
            return setA.SetEquals(setB);
        }

        private static bool DictsEqual(
            IReadOnlyDictionary<string, float> a, IReadOnlyDictionary<string, float> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var kvp in a)
            {
                if (!b.TryGetValue(kvp.Key, out var value)) return false;
                if (value != kvp.Value) return false;
            }

            return true;
        }

        /// <summary>The generated command scenario: which command to issue and its parameters.</summary>
        private readonly struct CommandSpec
        {
            public int Kind { get; }
            public string TechId { get; }
            public int StartingResearch { get; }
            public int UnitCount { get; }
            public int DestX { get; }
            public int DestZ { get; }

            public CommandSpec(int kind, string techId, int startingResearch, int unitCount, int destX, int destZ)
            {
                Kind = kind;
                TechId = techId;
                StartingResearch = startingResearch;
                UnitCount = unitCount;
                DestX = destX;
                DestZ = destZ;
            }
        }
    }
}
