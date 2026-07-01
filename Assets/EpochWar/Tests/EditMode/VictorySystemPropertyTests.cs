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
    /// Property-based tests for <see cref="VictorySystem"/> (Requirements 9, 10, 11, 12), validating
    /// the universal correctness properties from design.md, each exercised for at least the
    /// design-mandated minimum of 100 generated iterations (see design.md, "Testing Strategy").
    ///
    /// Covered properties, tagged <c>Feature: epoch-war-game, Property N</c>:
    /// <list type="bullet">
    ///   <item>Property 34 — Resolved elimination marks the target eliminated (Req 9.3).</item>
    ///   <item>Property 35 — Sole survivor wins by Annihilation and ends the Match (Req 9.4).</item>
    ///   <item>Property 38 — Completing the Peace Arch wins by Peace and ends the Match (Req 10.3).</item>
    ///   <item>Property 42 — Completing colonization wins by Ascension and ends the Match (Req 11.3).</item>
    ///   <item>Property 43 — Simultaneous victories resolve to the earliest completion (Req 11.4).</item>
    ///   <item>Property 44 — Match start initializes every Nation correctly (Req 12.1).</item>
    ///   <item>Property 45 — No satisfied condition keeps the Match in progress (Req 12.2).</item>
    ///   <item>Property 46 — Any satisfied condition ends the Match with an outcome (Req 12.3).</item>
    /// </list>
    ///
    /// The Peace and Ascension conditions are driven end-to-end: a Peace_Arch is advanced to
    /// completion through <see cref="BaseSystem.Tick"/> (which populates
    /// <see cref="BaseSystem.HasCompletedPeaceArch"/>), and a launched Colony_Ship's colonization
    /// sequence is advanced to completion through <see cref="UnitSystem.Tick"/> (which populates
    /// <see cref="UnitSystem.IsColonizationComplete"/>). Tests target the engine-free
    /// <c>EpochWar.Core</c> assembly with no Unity Play loop.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    public sealed class VictorySystemPropertyTests
    {
        // The minimum number of generated cases every property in this feature must run.
        private const int MinimumIterations = 100;

        // A generous upper bound on the number of fixed steps a construction/colonization scenario
        // may take to resolve, so a driven tick loop can never spin forever.
        private const int MaxTicks = 512;

        private static Configuration Config()
        {
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;
            return config;
        }

        // ------------------------------------------------------------------
        // Shared construction helpers
        // ------------------------------------------------------------------

        private static VictorySystem BuildVictory(
            ICatalog catalog,
            out BaseSystem baseSystem,
            out UnitSystem unitSystem,
            float colonizationDurationSeconds = 0f)
        {
            var resources = new ResourceSystem();
            var civ = new CivSystem(resources);
            var tech = new TechSystem(catalog, resources);
            baseSystem = new BaseSystem(catalog, resources, civ, tech);
            unitSystem = new UnitSystem(catalog, resources, civ, colonizationDurationSeconds);
            return new VictorySystem(baseSystem, unitSystem);
        }

        private static StructureDef PeaceArchDef(float buildTimeSeconds)
            => new StructureDef(
                "peace-arch",
                Era.Prehistoric,
                ResourceCost.Free,
                buildTimeSeconds,
                populationCost: 0,
                maxHealth: 100,
                footprintWidth: 1,
                footprintLength: 1,
                function: StructureFunction.Wonder,
                isPeaceArch: true);

        private static UnitDef ColonyShipDef()
            => new UnitDef(
                "colony-ship",
                Era.Space,
                ResourceCost.Free,
                buildTimeSeconds: 0f,
                populationCost: 0,
                maxHealth: 100,
                attack: 0,
                defense: 0,
                moveSpeed: 0f,
                role: UnitRole.ColonyShip,
                launchCost: ResourceCost.Free);

        // ==================================================================
        // Property 34: Resolved elimination marks the target eliminated (Req 9.3)
        // ==================================================================

        /// <summary>
        /// Property 34: Resolved elimination marks the target eliminated.
        ///
        /// For any targeted Nation whose Doomsday_Weapon elimination effect fully resolves, that
        /// Nation is marked eliminated (and its Units/Structures are removed from the Match).
        ///
        /// **Validates: Requirements 9.3**
        /// </summary>
        [Test]
        [Category("Property 34")]
        public void Property34_ResolvedEliminationMarksTargetEliminated()
        {
            var scenarios = from deployCost in Gen.Choose(0, 100)
                            from targetUnits in Gen.Choose(0, 5)
                            from targetStructures in Gen.Choose(0, 5)
                            select (deployCost, targetUnits, targetStructures);

            Prop.ForAll(Arb.From(scenarios), tuple =>
            {
                var (deployCost, targetUnits, targetStructures) = tuple;

                var cost = ResourceCost.Single(ResourceType.Metal, deployCost);
                var doomsday = new TechnologyDef(
                    "dday",
                    Era.Modern,
                    ResourceCost.Free,
                    category: TechCategory.DoomsdayWeapon,
                    deploymentCost: cost);
                var catalog = new InMemoryCatalog(technologies: new[] { doomsday });

                var resources = new ResourceSystem();
                var civ = new CivSystem(resources);
                var unitSystem = new UnitSystem(catalog, resources, civ);

                var state = new MatchState();

                const int attackerId = 1;
                const int targetId = 2;

                var attacker = new Nation(attackerId);
                attacker.CompletedTechIds.Add("dday");
                // Fund the deployment cost so the effect fully resolves.
                if (deployCost > 0)
                {
                    resources.Produce(attacker, ResourceType.Metal, deployCost);
                }

                var target = new Nation(targetId);
                state.Nations[attackerId] = attacker;
                state.Nations[targetId] = target;

                var soldierDef = new UnitDef(
                    "soldier", Era.Prehistoric, ResourceCost.Free, 0f, 0, 10, 5, 5, 1f, UnitRole.Soldier);
                var hutDef = new StructureDef(
                    "hut", Era.Prehistoric, ResourceCost.Free, 0f, 0, 10, 1, 1, StructureFunction.Barracks);

                for (int i = 0; i < targetUnits; i++)
                {
                    int id = 100 + i;
                    state.Units[id] = new UnitInstance(
                        id, targetId, soldierDef, WorldPosition.FromCell(new CellCoord(i, 0, 0)));
                }

                for (int i = 0; i < targetStructures; i++)
                {
                    int id = 200 + i;
                    state.Structures[id] = new StructureInstance(id, targetId, hutDef, new CellCoord(i, 0, 0));
                }

                var result = unitSystem.Handle(
                    new DeployDoomsdayCommand(attackerId, "dday", targetId), state);

                if (!result.Accepted)
                {
                    return false;
                }

                // The target is marked eliminated (Req 9.3, Property 34) ...
                if (!target.Eliminated)
                {
                    return false;
                }

                // ... and the elimination event is emitted for the target.
                bool eliminatedEvent = result.Events.OfType<NationEliminatedEvent>()
                    .Any(e => e.EliminatedNationId == targetId);
                if (!eliminatedEvent)
                {
                    return false;
                }

                // ... with all of the target's forces removed from the Match.
                bool anyTargetUnit = state.Units.Values.Any(u => u.OwnerNationId == targetId);
                bool anyTargetStructure = state.Structures.Values.Any(s => s.OwnerNationId == targetId);
                return !anyTargetUnit && !anyTargetStructure;
            }).Check(Config());
        }

        // ==================================================================
        // Property 35: Sole survivor wins by Annihilation and ends the Match (Req 9.4)
        // ==================================================================

        /// <summary>
        /// Property 35: Sole survivor wins by Annihilation and ends the Match.
        ///
        /// For any Match in which all opposing Nations are eliminated, the surviving Nation is
        /// declared the Annihilation victor and the Match ends.
        ///
        /// **Validates: Requirements 9.4**
        /// </summary>
        [Test]
        [Category("Property 35")]
        public void Property35_SoleSurvivorWinsByAnnihilationAndEndsMatch()
        {
            var scenarios = from nationCount in Gen.Choose(2, 6)
                            from survivorIndex in Gen.Choose(0, 5)
                            from tick in Gen.Choose(0, 1000)
                            select (nationCount, survivorIndex, tick);

            Prop.ForAll(Arb.From(scenarios), tuple =>
            {
                var (nationCount, survivorIndexRaw, tick) = tuple;
                int survivorIndex = survivorIndexRaw % nationCount;

                var catalog = new InMemoryCatalog();
                var victory = BuildVictory(catalog, out _, out _);

                var state = new MatchState { TickCount = tick };
                for (int i = 0; i < nationCount; i++)
                {
                    var nation = new Nation(i) { Eliminated = i != survivorIndex };
                    state.Nations[i] = nation;
                }

                var events = victory.Tick(state);

                if (state.Status != MatchStatus.Ended || state.Outcome == null)
                {
                    return false;
                }

                if (state.Outcome.Path != VictoryPath.Annihilation
                    || state.Outcome.WinningNationId != survivorIndex)
                {
                    return false;
                }

                return events.OfType<MatchEndedEvent>()
                    .Any(e => e.WinningNationId == survivorIndex && e.Path == VictoryPath.Annihilation);
            }).Check(Config());
        }

        // ==================================================================
        // Property 38: Completing the Peace Arch wins by Peace and ends the Match (Req 10.3)
        // ==================================================================

        /// <summary>
        /// Property 38: Completing the Peace Arch wins by Peace and ends the Match.
        ///
        /// For any Nation whose Peace_Arch construction completes, that Nation is declared the Peace
        /// victor and the Match ends. The Peace_Arch is driven to completion through the real
        /// <see cref="BaseSystem.Tick"/> construction path.
        ///
        /// **Validates: Requirements 10.3**
        /// </summary>
        [Test]
        [Category("Property 38")]
        public void Property38_CompletingThePeaceArchWinsByPeaceAndEndsMatch()
        {
            var scenarios = from buildTime in Gen.Choose(1, 12)
                            from nationId in Gen.Choose(0, 9)
                            select (buildTime, nationId);

            Prop.ForAll(Arb.From(scenarios), tuple =>
            {
                var (buildTime, nationId) = tuple;

                var catalog = new InMemoryCatalog();
                var victory = BuildVictory(catalog, out var baseSystem, out _);

                var state = new MatchState();
                state.Nations[nationId] = new Nation(nationId);

                // Seed an under-construction Peace_Arch; a single Nation means Annihilation can never
                // fire, isolating the Peace path.
                state.Structures[1] = new StructureInstance(
                    1, nationId, PeaceArchDef(buildTime), new CellCoord(0, 0, 0));

                bool ended = DriveUntilEnded(state, () =>
                {
                    baseSystem.Tick(state, 1f);
                    victory.Tick(state);
                });

                if (!ended)
                {
                    return false;
                }

                return state.Outcome != null
                       && state.Outcome.Path == VictoryPath.Peace
                       && state.Outcome.WinningNationId == nationId;
            }).Check(Config());
        }

        // ==================================================================
        // Property 42: Completing colonization wins by Ascension and ends the Match (Req 11.3)
        // ==================================================================

        /// <summary>
        /// Property 42: Completing colonization wins by Ascension and ends the Match.
        ///
        /// For any Nation whose Colony_Ship completes its colonization sequence, that Nation is
        /// declared the Ascension victor and the Match ends. The colonization sequence is launched
        /// and driven to completion through the real <see cref="UnitSystem"/> path.
        ///
        /// **Validates: Requirements 11.3**
        /// </summary>
        [Test]
        [Category("Property 42")]
        public void Property42_CompletingColonizationWinsByAscensionAndEndsMatch()
        {
            var scenarios = from duration in Gen.Choose(1, 12)
                            from nationId in Gen.Choose(0, 9)
                            select (duration, nationId);

            Prop.ForAll(Arb.From(scenarios), tuple =>
            {
                var (duration, nationId) = tuple;

                var catalog = new InMemoryCatalog();
                var victory = BuildVictory(catalog, out _, out var unitSystem, duration);

                var state = new MatchState();
                state.Nations[nationId] = new Nation(nationId, currentEra: Era.Space);

                state.Units[1] = new UnitInstance(
                    1, nationId, ColonyShipDef(), WorldPosition.FromCell(new CellCoord(0, 0, 0)));

                var launch = unitSystem.Handle(new LaunchColonyShipCommand(nationId, 1), state);
                if (!launch.Accepted)
                {
                    return false;
                }

                bool ended = DriveUntilEnded(state, () =>
                {
                    unitSystem.Tick(state, 1f);
                    victory.Tick(state);
                });

                if (!ended)
                {
                    return false;
                }

                return state.Outcome != null
                       && state.Outcome.Path == VictoryPath.Ascension
                       && state.Outcome.WinningNationId == nationId;
            }).Check(Config());
        }

        // ==================================================================
        // Property 43: Simultaneous victories resolve to the earliest completion (Req 11.4)
        // ==================================================================

        /// <summary>
        /// Property 43: Simultaneous victories resolve to the earliest completion.
        ///
        /// Nation 1 pursues Peace (a Peace_Arch completing after <c>peaceTicks</c> steps) and Nation 2
        /// pursues Ascension (colonization completing after <c>ascendTicks</c> steps). Driving both
        /// systems each fixed step, the Match ends for whichever condition completes at the earliest
        /// recorded tick; a same-tick tie resolves deterministically to the lower victory path
        /// ordinal (Peace before Ascension).
        ///
        /// **Validates: Requirements 11.4**
        /// </summary>
        [Test]
        [Category("Property 43")]
        public void Property43_SimultaneousVictoriesResolveToEarliestCompletion()
        {
            var scenarios = from peaceTicks in Gen.Choose(1, 10)
                            from ascendTicks in Gen.Choose(1, 10)
                            select (peaceTicks, ascendTicks);

            Prop.ForAll(Arb.From(scenarios), tuple =>
            {
                var (peaceTicks, ascendTicks) = tuple;

                var catalog = new InMemoryCatalog();
                var victory = BuildVictory(catalog, out var baseSystem, out var unitSystem, ascendTicks);

                var state = new MatchState();

                const int peaceNation = 1;
                const int ascendNation = 2;

                state.Nations[peaceNation] = new Nation(peaceNation);
                state.Nations[ascendNation] = new Nation(ascendNation, currentEra: Era.Space);

                // Nation 1: a Peace_Arch that completes after peaceTicks steps of dt = 1.
                state.Structures[1] = new StructureInstance(
                    1, peaceNation, PeaceArchDef(peaceTicks), new CellCoord(0, 0, 0));

                // Nation 2: a launched Colony_Ship whose colonization completes after ascendTicks steps.
                state.Units[1] = new UnitInstance(
                    1, ascendNation, ColonyShipDef(), WorldPosition.FromCell(new CellCoord(5, 0, 0)));
                var launch = unitSystem.Handle(new LaunchColonyShipCommand(ascendNation, 1), state);
                if (!launch.Accepted)
                {
                    return false;
                }

                bool ended = DriveUntilEnded(state, () =>
                {
                    baseSystem.Tick(state, 1f);
                    unitSystem.Tick(state, 1f);
                    victory.Tick(state);
                });

                if (!ended || state.Outcome == null)
                {
                    return false;
                }

                // The earliest completion wins; a tie favours the lower victory-path ordinal (Peace).
                int expectedTick = System.Math.Min(peaceTicks, ascendTicks);
                bool peaceWins = peaceTicks <= ascendTicks;
                int expectedWinner = peaceWins ? peaceNation : ascendNation;
                VictoryPath expectedPath = peaceWins ? VictoryPath.Peace : VictoryPath.Ascension;

                return state.Outcome.WinningNationId == expectedWinner
                       && state.Outcome.Path == expectedPath
                       && state.Outcome.CompletionTick == expectedTick;
            }).Check(Config());
        }

        // ==================================================================
        // Property 44: Match start initializes every Nation correctly (Req 12.1)
        // ==================================================================

        /// <summary>
        /// A generated match-start configuration: some number of Nations, each with per-type starting
        /// Resource amounts and a count of starting Units to spawn.
        /// </summary>
        public sealed class InitScenario
        {
            public int NationCount;
            public List<int> Food;
            public List<int> Wood;
            public List<int> UnitCounts;

            public override string ToString()
                => $"InitScenario(nations={NationCount})";
        }

        private static Arbitrary<InitScenario> InitScenarios()
        {
            var gen = from n in Gen.Choose(1, 5)
                      from food in Gen.Sequence(Enumerable.Range(0, n).Select(_ => Gen.Choose(0, 500)))
                      from wood in Gen.Sequence(Enumerable.Range(0, n).Select(_ => Gen.Choose(0, 500)))
                      from units in Gen.Sequence(Enumerable.Range(0, n).Select(_ => Gen.Choose(0, 3)))
                      select new InitScenario
                      {
                          NationCount = n,
                          Food = food.ToList(),
                          Wood = wood.ToList(),
                          UnitCounts = units.ToList(),
                      };

            return Arb.From(gen);
        }

        /// <summary>
        /// Property 44: Match start initializes every Nation correctly.
        ///
        /// For any Match start with any number of Nations, each Nation is initialized with its
        /// defined starting Resources, starting Units, and the Prehistoric Era, and the Match begins
        /// in progress at tick zero with no outcome.
        ///
        /// **Validates: Requirements 12.1**
        /// </summary>
        [Test]
        [Category("Property 44")]
        public void Property44_MatchStartInitializesEveryNationCorrectly()
        {
            var workerDef = new UnitDef(
                "worker", Era.Prehistoric, ResourceCost.Free, 0f, 0, 5, 0, 0, 1f, UnitRole.Worker);

            Prop.ForAll(InitScenarios(), scenario =>
            {
                var seeds = new List<NationSeed>();
                for (int i = 0; i < scenario.NationCount; i++)
                {
                    var resources = new Dictionary<ResourceType, ResourceStore>
                    {
                        { ResourceType.Food, new ResourceStore(scenario.Food[i]) },
                        { ResourceType.Wood, new ResourceStore(scenario.Wood[i]) },
                    };

                    var units = new List<UnitSeed>();
                    for (int u = 0; u < scenario.UnitCounts[i]; u++)
                    {
                        units.Add(new UnitSeed(workerDef, new CellCoord(u, 0, 0)));
                    }

                    seeds.Add(new NationSeed(i, startingResources: resources, startingUnits: units));
                }

                // Start from a dirty state to confirm initialization resets the lifecycle.
                var state = new MatchState { TickCount = 99, Status = MatchStatus.Ended };
                VictorySystem.InitializeMatch(state, seeds);

                if (state.Status != MatchStatus.InProgress || state.TickCount != 0 || state.Outcome != null)
                {
                    return false;
                }

                if (state.Nations.Count != scenario.NationCount)
                {
                    return false;
                }

                for (int i = 0; i < scenario.NationCount; i++)
                {
                    if (!state.Nations.TryGetValue(i, out var nation))
                    {
                        return false;
                    }

                    // Prehistoric Era at start (Req 12.1).
                    if (nation.CurrentEra != Era.Prehistoric)
                    {
                        return false;
                    }

                    // Starting Resources match exactly.
                    if (nation.Resources[ResourceType.Food].Amount != scenario.Food[i]
                        || nation.Resources[ResourceType.Wood].Amount != scenario.Wood[i])
                    {
                        return false;
                    }

                    // Starting Units spawned for this Nation.
                    int spawned = state.Units.Values.Count(un => un.OwnerNationId == i);
                    if (spawned != scenario.UnitCounts[i])
                    {
                        return false;
                    }
                }

                return true;
            }).Check(Config());
        }

        // ==================================================================
        // Property 45: No satisfied condition keeps the Match in progress (Req 12.2)
        // ==================================================================

        /// <summary>
        /// Property 45: No satisfied condition keeps the Match in progress.
        ///
        /// For any Match state in which no victory condition is satisfied (all Nations alive, no
        /// Peace_Arch completed, no colonization completed), the Victory_System leaves the Match
        /// status in-progress with no outcome and no events across any number of ticks.
        ///
        /// **Validates: Requirements 12.2**
        /// </summary>
        [Test]
        [Category("Property 45")]
        public void Property45_NoSatisfiedConditionKeepsMatchInProgress()
        {
            var scenarios = from nationCount in Gen.Choose(1, 5)
                            from ticks in Gen.Choose(1, 20)
                            select (nationCount, ticks);

            Prop.ForAll(Arb.From(scenarios), tuple =>
            {
                var (nationCount, ticks) = tuple;

                var catalog = new InMemoryCatalog();
                var victory = BuildVictory(catalog, out _, out _);

                var state = new MatchState();
                for (int i = 0; i < nationCount; i++)
                {
                    // All Nations alive: no Annihilation, no Peace_Arch, no colonization.
                    state.Nations[i] = new Nation(i);
                }

                for (int t = 1; t <= ticks; t++)
                {
                    state.TickCount = t;
                    var events = victory.Tick(state);

                    if (state.Status != MatchStatus.InProgress
                        || state.Outcome != null
                        || events.Count != 0)
                    {
                        return false;
                    }
                }

                return true;
            }).Check(Config());
        }

        // ==================================================================
        // Property 46: Any satisfied condition ends the Match with an outcome (Req 12.3)
        // ==================================================================

        /// <summary>
        /// Property 46: Any satisfied condition ends the Match with an outcome.
        ///
        /// For any Match state in which a victory condition is satisfied — Annihilation, Peace, or
        /// Ascension — the Match status becomes ended and a populated outcome (winning Nation and
        /// satisfied victory path) is produced, along with a single <see cref="MatchEndedEvent"/>.
        ///
        /// **Validates: Requirements 12.3**
        /// </summary>
        [Test]
        [Category("Property 46")]
        public void Property46_AnySatisfiedConditionEndsMatchWithOutcome()
        {
            // 0 = Annihilation, 1 = Peace, 2 = Ascension.
            var scenarios = from path in Gen.Choose(0, 2)
                            from param in Gen.Choose(1, 8)
                            select (path, param);

            Prop.ForAll(Arb.From(scenarios), tuple =>
            {
                var (path, param) = tuple;

                switch (path)
                {
                    case 0:
                        return CheckAnnihilationEnds(param);
                    case 1:
                        return CheckPeaceEnds(param);
                    default:
                        return CheckAscensionEnds(param);
                }
            }).Check(Config());
        }

        private static bool CheckAnnihilationEnds(int nationCountRaw)
        {
            int nationCount = System.Math.Max(2, nationCountRaw + 1);

            var catalog = new InMemoryCatalog();
            var victory = BuildVictory(catalog, out _, out _);

            var state = new MatchState();
            const int survivor = 0;
            for (int i = 0; i < nationCount; i++)
            {
                state.Nations[i] = new Nation(i) { Eliminated = i != survivor };
            }

            var events = victory.Tick(state);

            return EndedWith(state, events, survivor, VictoryPath.Annihilation);
        }

        private static bool CheckPeaceEnds(int buildTime)
        {
            var catalog = new InMemoryCatalog();
            var victory = BuildVictory(catalog, out var baseSystem, out _);

            var state = new MatchState();
            const int nationId = 0;
            state.Nations[nationId] = new Nation(nationId);
            state.Structures[1] = new StructureInstance(
                1, nationId, PeaceArchDef(buildTime), new CellCoord(0, 0, 0));

            IReadOnlyList<GameEvent> lastEvents = new GameEvent[0];
            bool ended = DriveUntilEnded(state, () =>
            {
                baseSystem.Tick(state, 1f);
                lastEvents = victory.Tick(state);
            });

            return ended && EndedWith(state, lastEvents, nationId, VictoryPath.Peace);
        }

        private static bool CheckAscensionEnds(int duration)
        {
            var catalog = new InMemoryCatalog();
            var victory = BuildVictory(catalog, out _, out var unitSystem, duration);

            var state = new MatchState();
            const int nationId = 0;
            state.Nations[nationId] = new Nation(nationId, currentEra: Era.Space);
            state.Units[1] = new UnitInstance(
                1, nationId, ColonyShipDef(), WorldPosition.FromCell(new CellCoord(0, 0, 0)));

            if (!unitSystem.Handle(new LaunchColonyShipCommand(nationId, 1), state).Accepted)
            {
                return false;
            }

            IReadOnlyList<GameEvent> lastEvents = new GameEvent[0];
            bool ended = DriveUntilEnded(state, () =>
            {
                unitSystem.Tick(state, 1f);
                lastEvents = victory.Tick(state);
            });

            return ended && EndedWith(state, lastEvents, nationId, VictoryPath.Ascension);
        }

        private static bool EndedWith(
            MatchState state,
            IReadOnlyList<GameEvent> events,
            int winner,
            VictoryPath path)
        {
            if (state.Status != MatchStatus.Ended || state.Outcome == null)
            {
                return false;
            }

            if (state.Outcome.WinningNationId != winner || state.Outcome.Path != path)
            {
                return false;
            }

            return events.OfType<MatchEndedEvent>()
                .Any(e => e.WinningNationId == winner && e.Path == path);
        }

        /// <summary>
        /// Advances a per-step <paramref name="step"/> (incrementing the tick clock each time) until
        /// the Match ends, up to <see cref="MaxTicks"/> steps. Returns true if the Match ended.
        /// </summary>
        private static bool DriveUntilEnded(MatchState state, System.Action step)
        {
            for (int t = 1; t <= MaxTicks; t++)
            {
                state.TickCount = t;
                step();
                if (state.Status == MatchStatus.Ended)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
