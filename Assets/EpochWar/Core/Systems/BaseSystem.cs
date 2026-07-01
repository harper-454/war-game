using System;
using System.Collections.Generic;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// The authoritative owner of Structure placement, construction, removal, and the Peace_Arch
    /// wonder (Requirement 4, plus Req 10.1-10.4).
    ///
    /// Responsibilities:
    /// <list type="bullet">
    /// <item>Placement (<see cref="PlaceStructureCommand"/>): validates that the Structure type
    /// exists and is unlocked for the Nation (Req 4.6) — or, for the Peace_Arch, that its
    /// prerequisite Technologies are complete (Req 10.1) — that the target terrain is inside the
    /// volume, supported, and not already occupied (Req 4.2), and that the Nation can afford the
    /// Resource cost (Req 4.1) and the population cost (Req 5.4). On acceptance it deducts both
    /// costs, marks the footprint cells occupied, and creates an under-construction
    /// <see cref="StructureInstance"/> at the location, emitting a <see cref="StructurePlacedEvent"/>
    /// (Req 4.1, 10.2). A rejected placement mutates nothing (Req 4.2, Property 18).</item>
    /// <item>Construction: each <see cref="Tick"/> accumulates construction time on every
    /// under-construction Structure and, once it reaches the build time, marks the Structure
    /// operational so its functions are enabled (Req 4.3); while building, the operational flag stays
    /// false so production/command functions are disabled (Req 4.4). A completed Peace_Arch
    /// additionally emits a <see cref="PeaceArchCompletedEvent"/> signalling the Victory_System to
    /// resolve the Peace victory (Req 10.3).</item>
    /// <item>Removal (<see cref="RemoveStructure"/>): a Structure reduced to zero health is removed
    /// from the Match, its footprint cells freed, and its construction population released (Req 4.5);
    /// <see cref="Tick"/> sweeps such Structures before advancing construction so a Peace_Arch
    /// destroyed before completion is removed without ever completing, withholding the Peace victory
    /// from its owner (Req 10.4, Property 39).</item>
    /// <item>Peace_Arch ownership: exposes <see cref="IsPeaceArchAvailable"/> (availability gating,
    /// Req 10.1) and <see cref="HasCompletedPeaceArch"/> (the completion query the Victory_System
    /// consumes, Req 10.3).</item>
    /// </list>
    ///
    /// Per-Match Structure state lives on the <see cref="MatchState"/>; the system keeps only derived
    /// bookkeeping — the set of occupied terrain cells for occupancy checks and the set of Nations
    /// whose Peace_Arch has completed — both reproducible from the fixed-step ticks so results are
    /// deterministic on the Host and in headless tests. It resolves content through the injected
    /// <see cref="ICatalog"/>, pays costs through the injected <see cref="ResourceSystem"/> /
    /// <see cref="CivSystem"/>, and gates unlocks/Peace_Arch availability through the injected
    /// <see cref="TechSystem"/>. Following the pipeline contract, nothing here throws to signal a
    /// rejected command.
    /// </summary>
    public sealed class BaseSystem : ICommandHandler<PlaceStructureCommand>
    {
        private readonly ICatalog _catalog;
        private readonly ResourceSystem _resources;
        private readonly CivSystem _civ;
        private readonly TechSystem _tech;

        // Occupied footprint cells keyed by cell, mapping to the owning Structure id, so placement can
        // reject overlaps in O(footprint) (Req 4.2). Derived state, kept in sync as Structures are
        // placed and removed; seeded Structures are folded in lazily via EnsureRegistered.
        private readonly Dictionary<CellCoord, int> _occupiedCells = new Dictionary<CellCoord, int>();

        // The footprint cells each tracked Structure occupies, so removal can free exactly those cells.
        private readonly Dictionary<int, List<CellCoord>> _structureFootprints
            = new Dictionary<int, List<CellCoord>>();

        // Nations whose Peace_Arch has completed construction (Req 10.3). A Peace_Arch destroyed
        // before completion is never added here, which is how the Peace victory is withheld (Req 10.4).
        private readonly HashSet<int> _completedPeaceArchNations = new HashSet<int>();

        private int _nextStructureId;

        /// <summary>
        /// Creates the system.
        /// </summary>
        /// <param name="catalog">Resolves Structure definitions referenced by placement commands.</param>
        /// <param name="resourceSystem">Pays construction costs atomically (Req 4.1).</param>
        /// <param name="civSystem">Reserves/releases construction population (Req 5.4).</param>
        /// <param name="techSystem">
        /// Gates the placeable Structure set (Req 4.6) and Peace_Arch availability (Req 10.1).
        /// </param>
        public BaseSystem(
            ICatalog catalog,
            ResourceSystem resourceSystem,
            CivSystem civSystem,
            TechSystem techSystem)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _resources = resourceSystem ?? throw new ArgumentNullException(nameof(resourceSystem));
            _civ = civSystem ?? throw new ArgumentNullException(nameof(civSystem));
            _tech = techSystem ?? throw new ArgumentNullException(nameof(techSystem));
        }

        /// <summary>
        /// Registers this system's command handler with the single authoritative
        /// <paramref name="router"/> so place-Structure intents from human Players and AI_Nations
        /// alike flow through the identical pipeline (Req 8.2, 8.5).
        /// </summary>
        public void RegisterHandlers(CommandRouter router)
        {
            if (router == null)
            {
                throw new ArgumentNullException(nameof(router));
            }

            router.Register<PlaceStructureCommand>(this);
        }

        // ==================================================================
        // Placement (Req 4.1, 4.2, 4.6, 10.1, 10.2)
        // ==================================================================

        /// <summary>
        /// Validates and applies a place-Structure command (Req 4.1, 4.2, 4.6, 5.4, 10.1, 10.2).
        /// Rejection leaves all state untouched (Property 18); on acceptance the Resource and
        /// population costs are deducted, the footprint is marked occupied, and an under-construction
        /// Structure is created, emitting the cost-change events plus a <see cref="StructurePlacedEvent"/>.
        /// </summary>
        public CommandResult Handle(PlaceStructureCommand command, MatchState state)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (state == null) throw new ArgumentNullException(nameof(state));

            // The router has already confirmed the issuing nation exists and is not eliminated.
            var nation = state.Nations[command.IssuingNationId];

            EnsureRegistered(state);

            if (!_catalog.TryGetStructure(command.StructureId, out var def))
            {
                return CommandResult.Reject($"Unknown structure type \"{command.StructureId}\".");
            }

            // Availability: the Peace_Arch is gated by its prerequisite techs (Req 10.1); every other
            // Structure by the Nation's unlocked set (Req 4.6, Property 22).
            if (def.IsPeaceArch)
            {
                if (!_tech.IsPeaceArchAvailable(nation))
                {
                    return CommandResult.Reject(
                        "The Peace_Arch is not yet available: its prerequisite technologies are incomplete.");
                }
            }
            else if (!_tech.IsStructureUnlocked(nation, def.Id))
            {
                return CommandResult.Reject($"Structure type \"{def.Id}\" is not unlocked.");
            }

            // Terrain occupancy/validity: the footprint must lie inside the volume, rest on supported
            // ground, and not overlap another Structure (Req 4.2, Property 18).
            if (!TryComputeFootprint(state, def, command.Origin, out var footprintCells, out var invalidReason))
            {
                return CommandResult.Reject(invalidReason);
            }

            // Validate both cost gates before mutating anything so a partially-affordable placement
            // changes nothing (Req 4.1 / 5.4, Property 18).
            if (!_resources.CanAfford(nation, def.Cost))
            {
                return CommandResult.Reject($"Insufficient resources to place \"{def.Id}\".");
            }

            if (!_civ.HasAvailablePopulation(nation, def.PopulationCost))
            {
                return CommandResult.Reject($"Insufficient population to construct \"{def.Id}\".");
            }

            var events = new List<GameEvent>();

            // Both deductions are guaranteed to succeed after the pre-checks above.
            _resources.TryDeduct(nation, def.Cost, out var costEvents);
            events.AddRange(costEvents);

            _civ.TryConsumePopulation(nation, def.PopulationCost, out var popEvents);
            events.AddRange(popEvents);

            int structureId = AllocateStructureId(state);
            var structure = new StructureInstance(structureId, nation.Id, def, command.Origin);

            // Instant-build structures (zero or negative build time) complete on the next tick, so a
            // freshly placed Structure always begins under construction with functions disabled (Req 4.4).
            state.Structures[structureId] = structure;
            Occupy(structureId, footprintCells);

            events.Add(new StructurePlacedEvent(nation.Id, structureId, def.Id, command.Origin, def.IsPeaceArch));

            return CommandResult.Accept(events.ToArray());
        }

        // ==================================================================
        // Simulation tick: construction progress + destroyed-structure sweep
        // ==================================================================

        /// <summary>
        /// Advances the system by <paramref name="deltaSeconds"/>: first sweeps any zero-health
        /// Structures from the Match (Req 4.5) — so a Peace_Arch destroyed this step is removed before
        /// it can complete (Req 10.4) — then accumulates construction time on every under-construction
        /// Structure, marking it operational once its build time elapses (Req 4.3) and emitting a
        /// <see cref="StructureConstructionCompletedEvent"/> (plus a <see cref="PeaceArchCompletedEvent"/>
        /// for a completed Peace_Arch, Req 10.3). A non-positive delta still runs the destroyed sweep
        /// but performs no construction progress. Returns the ordered events produced.
        /// </summary>
        public IReadOnlyList<GameEvent> Tick(MatchState state, float deltaSeconds)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            EnsureRegistered(state);

            var events = new List<GameEvent>();

            SweepDestroyedStructures(state, events);

            if (deltaSeconds > 0f)
            {
                AdvanceConstruction(state, deltaSeconds, events);
            }

            return events;
        }

        private void AdvanceConstruction(MatchState state, float deltaSeconds, List<GameEvent> events)
        {
            foreach (var structureId in SortedStructureIds(state))
            {
                if (!state.Structures.TryGetValue(structureId, out var structure))
                {
                    continue;
                }

                if (structure.IsOperational)
                {
                    continue;
                }

                structure.ConstructionProgress += deltaSeconds;

                float buildTime = structure.Def?.BuildTimeSeconds ?? 0f;
                if (structure.ConstructionProgress < buildTime)
                {
                    continue;
                }

                // Construction time elapsed: enable the Structure's functions (Req 4.3, 4.4).
                structure.ConstructionProgress = buildTime < 0f ? 0f : buildTime;
                structure.IsOperational = true;

                bool isPeaceArch = structure.Def?.IsPeaceArch ?? false;
                events.Add(new StructureConstructionCompletedEvent(
                    structure.OwnerNationId, structure.Id, structure.Def?.Id, isPeaceArch));

                if (isPeaceArch)
                {
                    // Signal the Victory_System to resolve the Peace victory (Req 10.3).
                    _completedPeaceArchNations.Add(structure.OwnerNationId);
                    events.Add(new PeaceArchCompletedEvent(structure.OwnerNationId, structure.Id));
                }
            }
        }

        private void SweepDestroyedStructures(MatchState state, List<GameEvent> events)
        {
            List<int> destroyed = null;
            foreach (var structure in state.Structures.Values)
            {
                if (structure.Health <= 0)
                {
                    (destroyed ??= new List<int>()).Add(structure.Id);
                }
            }

            if (destroyed == null)
            {
                return;
            }

            destroyed.Sort();
            foreach (var structureId in destroyed)
            {
                events.AddRange(RemoveStructure(state, structureId));
            }
        }

        // ==================================================================
        // Removal (Req 4.5, 10.4)
        // ==================================================================

        /// <summary>
        /// Removes the Structure <paramref name="structureId"/> from the Match, frees its footprint
        /// cells, and releases the population it consumed for construction back to the owning Nation
        /// (Req 4.5). A Peace_Arch removed before it became operational is an incomplete wonder whose
        /// Peace victory is withheld — it is never recorded as completed (Req 10.4) — and the emitted
        /// <see cref="StructureRemovedEvent"/> flags it as such. Returns the events produced (the
        /// removal event plus any population-release event). An unknown structure id yields no events.
        /// </summary>
        public IReadOnlyList<GameEvent> RemoveStructure(MatchState state, int structureId)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            if (!state.Structures.TryGetValue(structureId, out var structure))
            {
                return Array.Empty<GameEvent>();
            }

            state.Structures.Remove(structureId);
            Free(structureId);

            bool isPeaceArch = structure.Def?.IsPeaceArch ?? false;

            // A destroyed incomplete Peace_Arch must never count toward victory (Req 10.4). Because
            // completion records the Nation in _completedPeaceArchNations only from Tick, an incomplete
            // Peace_Arch is inherently absent from that set, so its removal withholds the Peace victory.
            bool wasIncompletePeaceArch = isPeaceArch && !structure.IsOperational;

            var events = new List<GameEvent>();

            if (state.Nations.TryGetValue(structure.OwnerNationId, out var nation)
                && structure.Def != null)
            {
                events.AddRange(_civ.ReleasePopulation(nation, structure.Def.PopulationCost));
            }

            events.Add(new StructureRemovedEvent(
                structure.OwnerNationId, structureId, structure.Def?.Id, wasIncompletePeaceArch));

            return events;
        }

        // ==================================================================
        // Peace_Arch / availability queries (for the UI and Victory_System)
        // ==================================================================

        /// <summary>
        /// Returns true when the Peace_Arch is available for placement by <paramref name="nation"/> —
        /// i.e. every prerequisite Technology is complete (Req 10.1, Property 36). Delegates to the
        /// <see cref="TechSystem"/>. Pure query — never mutates state.
        /// </summary>
        public bool IsPeaceArchAvailable(Nation nation)
        {
            if (nation == null) throw new ArgumentNullException(nameof(nation));
            return _tech.IsPeaceArchAvailable(nation);
        }

        /// <summary>
        /// Returns true when a given Structure type is currently placeable by
        /// <paramref name="nation"/>: the Peace_Arch iff its prerequisites are complete (Req 10.1),
        /// every other Structure iff it is in the Nation's unlocked set (Req 4.6, Property 22). An
        /// unknown Structure type is not placeable. Pure query — safe for UI availability predicates
        /// (Req 7.5).
        /// </summary>
        public bool IsStructurePlaceable(Nation nation, string structureId)
        {
            if (nation == null) throw new ArgumentNullException(nameof(nation));

            if (!_catalog.TryGetStructure(structureId, out var def))
            {
                return false;
            }

            return def.IsPeaceArch ? _tech.IsPeaceArchAvailable(nation) : _tech.IsStructureUnlocked(nation, def.Id);
        }

        /// <summary>
        /// Returns true when <paramref name="nationId"/>'s Peace_Arch has completed construction — the
        /// condition the Victory_System resolves as a Peace victory (Req 10.3). A Peace_Arch destroyed
        /// before completion never satisfies this (Req 10.4). Pure query — never mutates state.
        /// </summary>
        public bool HasCompletedPeaceArch(int nationId) => _completedPeaceArchNations.Contains(nationId);

        // ==================================================================
        // Occupancy bookkeeping
        // ==================================================================

        /// <summary>
        /// Computes the footprint cells a Structure of <paramref name="def"/> anchored at
        /// <paramref name="origin"/> would occupy and validates the placement against terrain
        /// occupancy/validity (Req 4.2): the footprint extent must be positive, every cell must lie
        /// inside the terrain volume, the footprint must rest on supported ground, and no cell may be
        /// occupied by an existing Structure. On success returns the footprint cells; on failure
        /// returns a human-readable <paramref name="reason"/>.
        /// </summary>
        private bool TryComputeFootprint(
            MatchState state,
            StructureDef def,
            CellCoord origin,
            out List<CellCoord> footprintCells,
            out string reason)
        {
            footprintCells = null;

            int width = def.FootprintWidth;
            int length = def.FootprintLength;

            if (width <= 0 || length <= 0)
            {
                reason = $"Structure type \"{def.Id}\" has an invalid footprint.";
                return false;
            }

            var terrain = state.Terrain;
            var cells = new List<CellCoord>(width * length);

            for (int dz = 0; dz < length; dz++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    var cell = new CellCoord(origin.X + dx, origin.Y, origin.Z + dz);

                    if (!cell.IsInside(terrain.Dimensions))
                    {
                        reason = $"Placement at {origin} extends outside the terrain volume.";
                        return false;
                    }

                    if (_occupiedCells.ContainsKey(cell))
                    {
                        reason = $"Terrain cell {cell} is already occupied by another structure.";
                        return false;
                    }

                    cells.Add(cell);
                }
            }

            // The footprint must rest on supported terrain — nothing may float (Req 4.2, 6.4).
            if (!terrain.IsSupported(origin, new Int3(width, 0, length)))
            {
                reason = $"Placement at {origin} is not supported by the terrain beneath it.";
                return false;
            }

            footprintCells = cells;
            reason = null;
            return true;
        }

        private void Occupy(int structureId, List<CellCoord> footprintCells)
        {
            _structureFootprints[structureId] = footprintCells;
            foreach (var cell in footprintCells)
            {
                _occupiedCells[cell] = structureId;
            }
        }

        private void Free(int structureId)
        {
            if (!_structureFootprints.TryGetValue(structureId, out var footprintCells))
            {
                return;
            }

            foreach (var cell in footprintCells)
            {
                if (_occupiedCells.TryGetValue(cell, out var owner) && owner == structureId)
                {
                    _occupiedCells.Remove(cell);
                }
            }

            _structureFootprints.Remove(structureId);
        }

        /// <summary>
        /// Folds any Structures present in <paramref name="state"/> but not yet tracked (e.g. seeded
        /// at match start) into the occupancy bookkeeping, so occupancy checks account for them. Idempotent.
        /// </summary>
        private void EnsureRegistered(MatchState state)
        {
            foreach (var structure in state.Structures.Values)
            {
                if (_structureFootprints.ContainsKey(structure.Id))
                {
                    continue;
                }

                int width = structure.Def?.FootprintWidth ?? 0;
                int length = structure.Def?.FootprintLength ?? 0;
                if (width <= 0 || length <= 0)
                {
                    // Nothing sensible to occupy; still record an empty footprint so it is not rescanned.
                    _structureFootprints[structure.Id] = new List<CellCoord>();
                    continue;
                }

                var cells = new List<CellCoord>(width * length);
                for (int dz = 0; dz < length; dz++)
                {
                    for (int dx = 0; dx < width; dx++)
                    {
                        cells.Add(new CellCoord(structure.Origin.X + dx, structure.Origin.Y, structure.Origin.Z + dz));
                    }
                }

                Occupy(structure.Id, cells);

                // Fold an already-operational seeded Peace_Arch into the completed set so the
                // Victory_System sees it (Req 10.3).
                if ((structure.Def?.IsPeaceArch ?? false) && structure.IsOperational)
                {
                    _completedPeaceArchNations.Add(structure.OwnerNationId);
                }
            }
        }

        // ==================================================================
        // Helpers
        // ==================================================================

        /// <summary>The number of terrain cells currently occupied by tracked Structures.</summary>
        public int OccupiedCellCount => _occupiedCells.Count;

        /// <summary>
        /// Allocates a Match-unique, monotonically increasing Structure id, never reusing an id
        /// currently present in <paramref name="state"/>, so placed Structures never collide with
        /// seeded ones or with each other across ticks.
        /// </summary>
        private int AllocateStructureId(MatchState state)
        {
            int max = 0;
            foreach (var id in state.Structures.Keys)
            {
                if (id > max)
                {
                    max = id;
                }
            }

            if (_nextStructureId <= max)
            {
                _nextStructureId = max + 1;
            }

            return _nextStructureId++;
        }

        /// <summary>Returns the Match's structure ids sorted ascending for deterministic iteration.</summary>
        private static List<int> SortedStructureIds(MatchState state)
        {
            var ids = new List<int>(state.Structures.Keys);
            ids.Sort();
            return ids;
        }
    }
}
