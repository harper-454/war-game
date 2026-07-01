using System;
using System.Collections.Generic;
using EpochWar.Core.Commands;
using EpochWar.Core.Math;
using EpochWar.Core.Navigation;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Core.Systems;

namespace EpochWar.Core.Simulation
{
    /// <summary>
    /// The fixed-tick simulation engine that owns the single authoritative <see cref="CommandRouter"/>
    /// and every gameplay system, and advances the whole Match one deterministic step at a time
    /// (design "Simulation Loop"; task 13.1, Req 8.5, 12.2).
    ///
    /// The design keeps gameplay rules in the systems rather than on the <see cref="MatchState"/> data
    /// container, so orchestration lives here: the engine constructs and holds one instance of each
    /// system (Resource, Tech, Civ, Base, Unit, Combat, Terrain, Victory), registers the systems'
    /// command handlers on the router, and exposes a single <see cref="Tick"/> that, each fixed step:
    /// <list type="number">
    /// <item>applies every queued command through the router — the identical validate→apply→events
    /// path a human or AI command takes (Req 8.2, 8.5);</item>
    /// <item>runs the systems in the fixed order Resource → Tech → Civ → Base → Unit → Terrain →
    /// Victory so all state mutation is ordered and reproducible (design "Simulation Loop");</item>
    /// <item>increments <see cref="MatchState.TickCount"/>;</item>
    /// <item>drains and returns the ordered <see cref="GameEvent"/>s produced this step for the
    /// networking/UI layers to replicate and consume.</item>
    /// </list>
    ///
    /// The same system instances back both the command handlers and the tick calls, so per-system
    /// state (build queues, construction/occupancy, colonization, terrain effect queue, victory
    /// timestamps) is consistent across the frame. Commands are queued via <see cref="EnqueueCommand"/>
    /// from a single path shared by human clients and AI controllers; nothing here throws to signal a
    /// rejected command — a rejection simply leaves state unchanged and produces no events.
    /// </summary>
    public sealed class MatchSimulation
    {
        private readonly CommandRouter _router;
        private readonly List<ICommand> _pendingCommands = new List<ICommand>();

        private readonly ResourceSystem _resourceSystem;
        private readonly TechSystem _techSystem;
        private readonly CivSystem _civSystem;
        private readonly BaseSystem _baseSystem;
        private readonly UnitSystem _unitSystem;
        private readonly CombatSystem _combatSystem;
        private readonly TerrainSystem _terrainSystem;
        private readonly VisionSystem _visionSystem;
        private readonly VictorySystem _victorySystem;
        private readonly NavGrid _navGrid;

        /// <summary>
        /// Builds the engine over an already-initialized <paramref name="state"/> (typically seeded by
        /// <see cref="VictorySystem.InitializeMatch"/> via the <see cref="MatchBootstrapper"/>),
        /// resolving content through <paramref name="catalog"/> and tuning the systems with
        /// <paramref name="config"/> (defaults applied when null). Constructing the engine wires every
        /// system and registers its command handlers on the router, so it is ready to accept commands
        /// and tick immediately.
        /// </summary>
        public MatchSimulation(MatchState state, ICatalog catalog, SimulationConfig config = null)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            config = config ?? SimulationConfig.Default;

            _router = new CommandRouter();

            // A nav grid derived from the current terrain surface; the TerrainSystem recomputes only
            // the touched columns after each modification batch (Req 6.3).
            _navGrid = new NavGrid(State.Terrain, config.NavMaxStepHeight);

            // Construct the systems in dependency order (design "Components & Systems"). Each stateful
            // system is created exactly once so its handler and its Tick share the same instance.
            _resourceSystem = new ResourceSystem();
            _techSystem = new TechSystem(Catalog, _resourceSystem);
            _civSystem = new CivSystem(_resourceSystem, config.PopulationGrowthPerSecond, config.FoodThreshold);
            _baseSystem = new BaseSystem(Catalog, _resourceSystem, _civSystem, _techSystem);
            _unitSystem = new UnitSystem(
                Catalog, _resourceSystem, _civSystem,
                config.ColonizationDurationSeconds, config.NavMaxStepHeight);
            _terrainSystem = new TerrainSystem(_navGrid, config.SupportLossConsequence, config.SupportLossDamage);
            _visionSystem = new VisionSystem();
            // Pass the authoritative BaseSystem (so Area_Effect / Indirect_Fire structure removal frees
            // footprint cells and construction population via the same path as any other removal) and
            // the VisionSystem (so the IndirectFireCommand handler can validate the issuing Nation's
            // Spotting on the target). Both are additive optional CombatSystem dependencies — the base
            // spec's single-target ResolveAttack only ever damages Units, so wiring them here does not
            // change any base-spec combat behavior (Properties 1-46 remain green).
            _combatSystem = new CombatSystem(_civSystem, _unitSystem, _terrainSystem, _baseSystem, _visionSystem);
            _victorySystem = new VictorySystem(_baseSystem, _unitSystem);

            // Register every system's command handlers on the single authoritative router so human and
            // AI commands share one path (Req 8.2, 8.5). Resource/Civ/Terrain/Victory are driven by the
            // tick and by other systems, not by their own commands. CombatSystem registers
            // IndirectFireCommand (Req 15.1) and UnitSystem registers ActivateAbilityCommand (Req 13.2),
            // so both new commands traverse the identical ownership/turn-check dispatch path.
            _techSystem.RegisterHandlers(_router);
            _baseSystem.RegisterHandlers(_router);
            _unitSystem.RegisterHandlers(_router);
            _combatSystem.RegisterHandlers(_router);
        }

        /// <summary>The authoritative Match state this engine advances.</summary>
        public MatchState State { get; }

        /// <summary>The content catalog the systems resolve definitions through.</summary>
        public ICatalog Catalog { get; }

        /// <summary>The single authoritative command router shared by every system.</summary>
        public CommandRouter Router => _router;

        /// <summary>The number of commands queued and awaiting the next <see cref="Tick"/>.</summary>
        public int PendingCommandCount => _pendingCommands.Count;

        // ---- System accessors (for content wiring, tests, and the presentation layer) ----

        public ResourceSystem ResourceSystem => _resourceSystem;
        public TechSystem TechSystem => _techSystem;
        public CivSystem CivSystem => _civSystem;
        public BaseSystem BaseSystem => _baseSystem;
        public UnitSystem UnitSystem => _unitSystem;
        public CombatSystem CombatSystem => _combatSystem;
        public TerrainSystem TerrainSystem => _terrainSystem;
        public VisionSystem VisionSystem => _visionSystem;
        public VictorySystem VictorySystem => _victorySystem;
        public NavGrid NavGrid => _navGrid;

        /// <summary>
        /// Queues <paramref name="command"/> to be validated and applied on the next <see cref="Tick"/>
        /// (Req 8.5). This is the single entry point used identically by human clients (whose intents
        /// arrive over the network) and by AI controllers on the Host, so both traverse the same
        /// authoritative pipeline. Queuing never mutates state; validation and application happen when
        /// the queued command is dispatched during the tick.
        /// </summary>
        public void EnqueueCommand(ICommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            _pendingCommands.Add(command);
        }

        /// <summary>
        /// Immediately dispatches <paramref name="command"/> through the authoritative router against
        /// the current state, bypassing the pending queue, and returns the result. Provided for tests
        /// and callers that need the per-command <see cref="CommandResult"/> (e.g. to surface a
        /// rejection reason); the resulting accepted events are still drained by the next
        /// <see cref="Tick"/>. Prefer <see cref="EnqueueCommand"/> for the normal per-frame path.
        /// </summary>
        public CommandResult Dispatch(ICommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            return _router.Dispatch(command, State);
        }

        /// <summary>
        /// Advances the Match by one fixed step of <paramref name="fixedDt"/> seconds and returns the
        /// ordered events produced (design "Simulation Loop", task 13.1).
        ///
        /// The step (1) applies every queued command through the router before any system runs, so all
        /// mutation is ordered (Req 8.2); (2) runs the systems in the fixed order Resource → Tech →
        /// Civ → Base → Unit → Terrain → Victory; (3) increments <see cref="MatchState.TickCount"/>;
        /// and (4) drains and returns the command events plus every system's events in that order.
        /// Once the Match has ended the Victory_System is idempotent, so continuing to tick is safe.
        /// A non-positive <paramref name="fixedDt"/> still applies commands and runs the systems'
        /// state sweeps (e.g. removing zero-health entities) but performs no time-based progress.
        /// </summary>
        public IReadOnlyList<GameEvent> Tick(float fixedDt)
        {
            var events = new List<GameEvent>();

            // (1) Apply queued commands first so their effects are visible to this tick's systems
            // (Req 8.2). Both human and AI commands arrive here through EnqueueCommand (Req 8.5).
            ApplyQueuedCommands(events);

            // (2) Run the systems in the fixed, deterministic order. ResourceSystem has no per-tick
            // driver of its own (production is applied by resource structures / other systems through
            // its Produce API), so the resource step is a no-op placeholder that keeps the documented
            // ordering intact. CombatSystem.Tick now advances in-flight Indirect_Fire projectiles
            // (on-demand engagement resolution still happens via ResolveAttack/ResolveAreaAttack).
            events.AddRange(_techSystem.Tick(State, fixedDt));      // research progress + completion
            events.AddRange(_civSystem.Tick(State, fixedDt));       // population growth
            events.AddRange(_baseSystem.Tick(State, fixedDt));      // construction progress + destroyed sweep
            events.AddRange(_unitSystem.Tick(State, fixedDt));      // build queues, movement, colonization, dead-unit sweep + veterancy XP drain

            // CombatSystem.Tick runs immediately after UnitSystem.Tick (design "Updated fixed order",
            // step 6): it advances in-flight Indirect_Fire projectiles and resolves the arrived ones,
            // producing combat/removal events. Its events are appended right after the unit events so
            // the returned ordering matches the documented fixed order.
            var combatEvents = _combatSystem.Tick(State, Fixed.FromFloat(fixedDt));
            events.AddRange(combatEvents);

            events.AddRange(_terrainSystem.Tick(State));            // queued terrain effects + support checks
            _visionSystem.Tick(State);                             // recompute per-Nation vision + LKP (returns no events)
            events.AddRange(_victorySystem.Tick(State));            // evaluate the three victory conditions

            // Veterancy XP hook wiring (Req 12): feed this tick's combat-resolution events to the
            // UnitSystem so its veterancy hook accrues experience for the attacking Units. Because
            // UnitSystem.Tick has already run this step (step 5) before CombatSystem.Tick (step 6)
            // produced these events, the recorded events are drained on the NEXT tick's
            // UnitSystem.Tick. This one-tick-deferred accrual is deterministic and matches the design's
            // event-drain pattern (RecordCombatEvents buffers; Tick drains). Combat events from the
            // command-application phase (e.g. an ability that resolves an attack) are captured too by
            // filtering the full event list rather than only CombatSystem.Tick's output.
            _unitSystem.RecordCombatEvents(events);

            // (3) Advance the simulation clock.
            State.TickCount++;

            // (4) Return the drained events for the networking/UI layers.
            return events;
        }

        /// <summary>
        /// Dispatches every queued command through the router in the order it was enqueued, then clears
        /// the queue and appends the accepted commands' events (drained from the router) to
        /// <paramref name="events"/>. Rejected commands leave state untouched and contribute no events.
        /// </summary>
        private void ApplyQueuedCommands(List<GameEvent> events)
        {
            if (_pendingCommands.Count == 0)
            {
                return;
            }

            // Snapshot then clear so a handler that (indirectly) enqueues further commands defers them
            // to the next tick rather than mutating the collection mid-iteration.
            var toApply = new List<ICommand>(_pendingCommands);
            _pendingCommands.Clear();

            foreach (var command in toApply)
            {
                _router.Dispatch(command, State);
            }

            events.AddRange(_router.DrainEvents());
        }
    }
}
