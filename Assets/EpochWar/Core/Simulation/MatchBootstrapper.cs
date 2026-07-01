using System;
using System.Collections.Generic;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Core.Systems;

namespace EpochWar.Core.Simulation
{
    /// <summary>
    /// Assembles a ready-to-run Match: it seeds the terrain and the Nations, constructs the
    /// <see cref="MatchSimulation"/> engine (which wires the systems and registers their command
    /// handlers), and drives any AI_Nations through the <em>same</em> authoritative command path a
    /// human client uses (task 13.1, Req 8.5, 12.1, 12.2).
    ///
    /// Seeding delegates to <see cref="VictorySystem.InitializeMatch"/>, so every Nation starts with
    /// its defined starting Resources, starting Units, and the Prehistoric Era, and the Match clock is
    /// reset to a fresh in-progress state (Req 12.1). Each tick the bootstrapper first asks every
    /// registered <see cref="IAiController"/> for its Nation's commands and enqueues them via
    /// <see cref="MatchSimulation.EnqueueCommand"/> — the identical entry point human intents use — then
    /// advances the engine one fixed step. Because AI and human commands are queued through one path
    /// and validated by one router, an AI command and an equivalent human command resolve identically
    /// (Req 8.5, Property 31).
    ///
    /// The bootstrapper owns no gameplay rules itself; it is the composition root that puts the seeded
    /// state, the engine, and the AI controllers together.
    /// </summary>
    public sealed class MatchBootstrapper
    {
        // Keyed by Nation id so AI controllers are consulted in a deterministic, id-ordered sequence.
        private readonly SortedDictionary<int, IAiController> _aiControllers
            = new SortedDictionary<int, IAiController>();

        private MatchBootstrapper(MatchSimulation simulation)
        {
            Simulation = simulation;
        }

        /// <summary>The assembled simulation engine driving the seeded Match.</summary>
        public MatchSimulation Simulation { get; }

        /// <summary>Convenience accessor for the engine's authoritative Match state.</summary>
        public MatchState State => Simulation.State;

        /// <summary>The number of AI controllers currently registered.</summary>
        public int AiControllerCount => _aiControllers.Count;

        /// <summary>
        /// Seeds a Match and builds its engine (Req 12.1). Creates a <see cref="MatchState"/> over the
        /// supplied <paramref name="terrain"/>, seeds every Nation described by <paramref name="seeds"/>
        /// via <see cref="VictorySystem.InitializeMatch"/>, and constructs the
        /// <see cref="MatchSimulation"/> over the seeded state using <paramref name="catalog"/> and
        /// <paramref name="config"/> (defaults applied when null). Returns the bootstrapper wrapping the
        /// ready engine; register AI controllers with <see cref="AddAiController"/> before ticking.
        /// </summary>
        public static MatchBootstrapper Create(
            ICatalog catalog,
            TerrainVolume terrain,
            IEnumerable<NationSeed> seeds,
            SimulationConfig config = null)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (seeds == null) throw new ArgumentNullException(nameof(seeds));

            var state = new MatchState(terrain ?? new TerrainVolume());

            // Seed nations/terrain and reset the Match clock/lifecycle (Req 12.1).
            VictorySystem.InitializeMatch(state, seeds);

            var simulation = new MatchSimulation(state, catalog, config);
            return new MatchBootstrapper(simulation);
        }

        /// <summary>
        /// Registers an <paramref name="controller"/> that produces commands for its AI_Nation each
        /// tick, routed through the same path as human commands (Req 8.5). The controller's Nation must
        /// exist in the seeded Match; registering a second controller for the same Nation replaces the
        /// first. Returns this bootstrapper for chaining.
        /// </summary>
        public MatchBootstrapper AddAiController(IAiController controller)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));

            if (!State.Nations.ContainsKey(controller.NationId))
            {
                throw new ArgumentException(
                    $"No seeded nation {controller.NationId} to attach an AI controller to.",
                    nameof(controller));
            }

            _aiControllers[controller.NationId] = controller;
            return this;
        }

        /// <summary>
        /// Enqueues a command from a human Player (or any external source) onto the engine's single
        /// command path (Req 8.5). Identical to the path AI commands take in <see cref="Tick"/>.
        /// </summary>
        public void EnqueueCommand(ICommand command) => Simulation.EnqueueCommand(command);

        /// <summary>
        /// Advances the Match one fixed step of <paramref name="fixedDt"/> seconds (Req 8.5, 12.2).
        ///
        /// First consults each registered AI controller in Nation-id order for the commands its Nation
        /// wishes to issue this tick and enqueues them through <see cref="MatchSimulation.EnqueueCommand"/>
        /// — the same entry point human intents use — then advances the engine, which applies all queued
        /// commands (human and AI alike) through the one authoritative router before running the systems.
        /// Eliminated Nations' controllers are skipped. Returns the ordered events produced this step.
        /// </summary>
        public IReadOnlyList<GameEvent> Tick(float fixedDt)
        {
            if (_aiControllers.Count > 0)
            {
                foreach (var entry in _aiControllers)
                {
                    // Skip controllers whose Nation is gone or eliminated — the router would reject
                    // their commands anyway (Req 8.2), but skipping avoids the wasted dispatch.
                    if (!State.Nations.TryGetValue(entry.Key, out var nation) || nation.Eliminated)
                    {
                        continue;
                    }

                    var commands = entry.Value.ProduceCommands(State, State.TickCount);
                    if (commands == null)
                    {
                        continue;
                    }

                    foreach (var command in commands)
                    {
                        if (command != null)
                        {
                            Simulation.EnqueueCommand(command);
                        }
                    }
                }
            }

            return Simulation.Tick(fixedDt);
        }
    }
}
