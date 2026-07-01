using System;
using System.Collections.Generic;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// The authoritative owner of the Match lifecycle and the three parallel victory conditions
    /// (Requirements 9.3-9.4, 10.3, 11.3-11.4, 12.1-12.3).
    ///
    /// Responsibilities:
    /// <list type="bullet">
    /// <item>Match initialization (<see cref="InitializeMatch"/> / <see cref="SeedNation"/>): seeds
    /// each Nation with its starting Resources, starting Units, and the Prehistoric Era, and resets
    /// the Match clock/lifecycle to a fresh in-progress state (Req 12.1).</item>
    /// <item>Per-tick evaluation (<see cref="Tick"/>): after the other systems have run for the step,
    /// evaluates the three victory conditions —
    /// <list type="number">
    /// <item>Annihilation: when all opposing Nations are eliminated a sole survivor wins (Req 9.3,
    /// 9.4). Eliminated status is set by the <see cref="UnitSystem"/> when a Doomsday_Weapon resolves
    /// (Req 9.2/9.3); this system consumes the <see cref="Nation.Eliminated"/> flag rather than
    /// re-emitting the elimination event.</item>
    /// <item>Peace: when a Nation's Peace_Arch has completed construction (Req 10.3), queried through
    /// <see cref="BaseSystem.HasCompletedPeaceArch"/> — a Peace_Arch destroyed before completion never
    /// satisfies this (Req 10.4).</item>
    /// <item>Ascension: when a Nation's Colony_Ship colonization sequence has completed (Req 11.3),
    /// queried through <see cref="UnitSystem.IsColonizationComplete"/>.</item>
    /// </list>
    /// It records the simulation tick on which each (path, nation) condition is first observed
    /// satisfied and, if several are satisfied, awards victory to the one with the earliest recorded
    /// completion tick (Req 11.4). On the first satisfied condition it ends the Match — setting
    /// <see cref="MatchState.Status"/> to <see cref="MatchStatus.Ended"/> and populating
    /// <see cref="MatchState.Outcome"/> — and emits a single <see cref="MatchEndedEvent"/> carrying the
    /// outcome summary (Req 9.4, 10.3, 11.3, 12.3). While nothing is satisfied the Match is left in
    /// progress (Req 12.2), and once ended the system is idempotent.</item>
    /// </list>
    ///
    /// The system keeps only derived bookkeeping — the first-observed completion tick per satisfied
    /// (path, nation) pair — which is reproducible from the fixed-step ticks, so results are
    /// deterministic on the Host and in headless tests. It reads completion status through the injected
    /// <see cref="BaseSystem"/> and <see cref="UnitSystem"/> and never throws to signal an in-progress
    /// Match.
    /// </summary>
    public sealed class VictorySystem
    {
        private readonly BaseSystem _baseSystem;
        private readonly UnitSystem _unitSystem;

        // First simulation tick on which each (path, nation) victory condition was observed satisfied,
        // so a simultaneous resolution can award the earliest recorded completion (Req 11.4). Derived,
        // reproducible from the ticks; entries are only ever added, never revised to a later tick.
        private readonly Dictionary<VictoryCandidate, long> _completionTicks
            = new Dictionary<VictoryCandidate, long>();

        /// <summary>
        /// Creates the system.
        /// </summary>
        /// <param name="baseSystem">
        /// Supplies <see cref="BaseSystem.HasCompletedPeaceArch"/> to resolve the Peace victory
        /// (Req 10.3).
        /// </param>
        /// <param name="unitSystem">
        /// Supplies <see cref="UnitSystem.IsColonizationComplete"/> to resolve the Ascension victory
        /// (Req 11.3).
        /// </param>
        public VictorySystem(BaseSystem baseSystem, UnitSystem unitSystem)
        {
            _baseSystem = baseSystem ?? throw new ArgumentNullException(nameof(baseSystem));
            _unitSystem = unitSystem ?? throw new ArgumentNullException(nameof(unitSystem));
        }

        // ==================================================================
        // Match initialization (Req 12.1)
        // ==================================================================

        /// <summary>
        /// Initializes a Match at the start of play (Req 12.1): resets the simulation clock and
        /// lifecycle to a fresh in-progress state with no outcome, then seeds every Nation described by
        /// <paramref name="seeds"/> with its starting Resources, starting Units, and the Prehistoric
        /// Era. Any Nations/Units already present in <paramref name="state"/> are cleared first so the
        /// result depends only on the supplied seeds. Returns the created Nations keyed by id.
        /// </summary>
        public static IReadOnlyDictionary<int, Nation> InitializeMatch(
            MatchState state,
            IEnumerable<NationSeed> seeds)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (seeds == null) throw new ArgumentNullException(nameof(seeds));

            // Reset lifecycle: a freshly initialized Match starts in progress at tick zero with no
            // resolved outcome (Req 12.1, 12.2).
            state.TickCount = 0;
            state.Status = MatchStatus.InProgress;
            state.Outcome = null;
            state.Nations.Clear();
            state.Units.Clear();

            foreach (var seed in seeds)
            {
                SeedNation(state, seed);
            }

            return state.Nations;
        }

        /// <summary>
        /// Seeds a single Nation into <paramref name="state"/> at match start (Req 12.1): creates the
        /// Nation at the Prehistoric Era (unless the seed overrides the starting Era), sets its
        /// population count/capacity, stocks its starting Resource stores, and spawns its starting
        /// Units at their designated cells. Unit ids are allocated Match-uniquely so seeded Units never
        /// collide with each other or with any already present. Returns the created Nation.
        /// </summary>
        public static Nation SeedNation(MatchState state, NationSeed seed)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (seed == null) throw new ArgumentNullException(nameof(seed));

            var nation = new Nation(seed.NationId, seed.IsAI, seed.StartingEra)
            {
                Population = seed.StartingPopulation,
                PopulationCapacity = seed.StartingPopulationCapacity,
            };

            // Stock the starting Resource stores; each type is tracked independently (Req 2.1, 12.1).
            if (seed.StartingResources != null)
            {
                foreach (var kvp in seed.StartingResources)
                {
                    nation.Resources[kvp.Key] = kvp.Value;
                }
            }

            state.Nations[nation.Id] = nation;

            // Spawn the starting Units at their cells (Req 12.1). Ids are allocated against the whole
            // Match so multiple seeded Nations do not collide.
            if (seed.StartingUnits != null)
            {
                foreach (var unitSeed in seed.StartingUnits)
                {
                    if (unitSeed.Def == null)
                    {
                        continue;
                    }

                    int unitId = AllocateUnitId(state);
                    var unit = new UnitInstance(
                        unitId,
                        nation.Id,
                        unitSeed.Def,
                        WorldPosition.FromCell(unitSeed.Cell));
                    state.Units[unitId] = unit;
                }
            }

            return nation;
        }

        // ==================================================================
        // Per-tick victory evaluation (Req 9.3-9.4, 10.3, 11.3-11.4, 12.2-12.3)
        // ==================================================================

        /// <summary>
        /// Evaluates the three victory conditions for the current simulation tick and, on the first
        /// satisfied condition, ends the Match (Req 9.4, 10.3, 11.3, 12.2, 12.3).
        ///
        /// Once the Match has ended this is a no-op (the outcome is immutable). Otherwise it records
        /// the current <see cref="MatchState.TickCount"/> as the completion tick for every newly
        /// satisfied (path, nation) condition, and if any condition has been satisfied it selects the
        /// winner with the earliest recorded completion tick — breaking a same-tick tie deterministically
        /// by victory path then Nation id (Req 11.4) — sets <see cref="MatchState.Status"/> to
        /// <see cref="MatchStatus.Ended"/>, populates <see cref="MatchState.Outcome"/>, and returns a
        /// single <see cref="MatchEndedEvent"/>. When no condition is satisfied the Match is left in
        /// progress and no events are produced (Req 12.2). Returns the events produced.
        /// </summary>
        public IReadOnlyList<GameEvent> Tick(MatchState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            // Once resolved, the outcome never changes (Req 12.3); further ticks do nothing.
            if (state.Status == MatchStatus.Ended)
            {
                return Array.Empty<GameEvent>();
            }

            RecordSatisfiedConditions(state);

            if (_completionTicks.Count == 0)
            {
                // No victory condition satisfied: the Match continues (Req 12.2).
                return Array.Empty<GameEvent>();
            }

            // Award the earliest recorded completion; ties within a tick resolve deterministically
            // (Req 11.4).
            var winner = SelectEarliest();

            state.Status = MatchStatus.Ended;
            state.Outcome = new MatchOutcome(winner.NationId, winner.Path, _completionTicks[winner]);

            return new GameEvent[]
            {
                new MatchEndedEvent(winner.NationId, winner.Path, _completionTicks[winner]),
            };
        }

        /// <summary>
        /// Records the current tick as the first-observed completion tick for every victory condition
        /// currently satisfied and not already recorded (Req 11.4). Evaluates Annihilation (sole
        /// survivor among the non-eliminated Nations, Req 9.3/9.4), Peace (Peace_Arch complete,
        /// Req 10.3), and Ascension (colonization complete, Req 11.3).
        /// </summary>
        private void RecordSatisfiedConditions(MatchState state)
        {
            long tick = state.TickCount;

            // ---- Annihilation (Req 9.3, 9.4) ----
            // A sole survivor wins only when every opposing Nation has been eliminated. This requires
            // at least two Nations to have existed so a single-Nation match never triggers a vacuous
            // victory at tick zero.
            if (state.Nations.Count >= 2)
            {
                int survivorId = -1;
                int survivorCount = 0;
                foreach (var nation in state.Nations.Values)
                {
                    if (!nation.Eliminated)
                    {
                        survivorCount++;
                        if (survivorCount == 1)
                        {
                            survivorId = nation.Id;
                        }
                    }
                }

                if (survivorCount == 1)
                {
                    Record(new VictoryCandidate(VictoryPath.Annihilation, survivorId), tick);
                }
            }

            // ---- Peace (Req 10.3) and Ascension (Req 11.3) ----
            // Only a surviving Nation can claim a victory; an eliminated Nation's wonder/colony no
            // longer counts.
            foreach (var nation in state.Nations.Values)
            {
                if (nation.Eliminated)
                {
                    continue;
                }

                if (_baseSystem.HasCompletedPeaceArch(nation.Id))
                {
                    Record(new VictoryCandidate(VictoryPath.Peace, nation.Id), tick);
                }

                if (_unitSystem.IsColonizationComplete(nation.Id))
                {
                    Record(new VictoryCandidate(VictoryPath.Ascension, nation.Id), tick);
                }
            }
        }

        /// <summary>Records <paramref name="tick"/> as the completion tick for <paramref name="candidate"/> the first time it is seen.</summary>
        private void Record(VictoryCandidate candidate, long tick)
        {
            if (!_completionTicks.ContainsKey(candidate))
            {
                _completionTicks[candidate] = tick;
            }
        }

        /// <summary>
        /// Selects the satisfied victory condition to award: the one with the earliest recorded
        /// completion tick, breaking ties deterministically by victory path (Annihilation, then Peace,
        /// then Ascension) and finally by Nation id (Req 11.4).
        /// </summary>
        private VictoryCandidate SelectEarliest()
        {
            bool haveBest = false;
            VictoryCandidate best = default;
            long bestTick = long.MaxValue;

            foreach (var entry in _completionTicks)
            {
                var candidate = entry.Key;
                long tick = entry.Value;

                if (!haveBest || IsBetter(candidate, tick, best, bestTick))
                {
                    best = candidate;
                    bestTick = tick;
                    haveBest = true;
                }
            }

            return best;
        }

        /// <summary>
        /// True when (<paramref name="candidate"/>, <paramref name="tick"/>) should be preferred over
        /// the current best: earlier completion tick wins; on an equal tick the lower victory path
        /// ordinal wins; on an equal path the lower Nation id wins. This total, deterministic order
        /// guarantees a single well-defined winner (Req 11.4).
        /// </summary>
        private static bool IsBetter(VictoryCandidate candidate, long tick, VictoryCandidate best, long bestTick)
        {
            if (tick != bestTick)
            {
                return tick < bestTick;
            }

            if (candidate.Path != best.Path)
            {
                return candidate.Path < best.Path;
            }

            return candidate.NationId < best.NationId;
        }

        // ==================================================================
        // Helpers
        // ==================================================================

        /// <summary>
        /// Allocates a Match-unique, monotonically increasing Unit id, never reusing an id currently
        /// present in <paramref name="state"/>, so seeded Units never collide with each other.
        /// </summary>
        private static int AllocateUnitId(MatchState state)
        {
            int max = -1;
            foreach (var id in state.Units.Keys)
            {
                if (id > max)
                {
                    max = id;
                }
            }

            return max + 1;
        }

        // ------------------------------------------------------------------
        // Internal value types
        // ------------------------------------------------------------------

        /// <summary>
        /// A satisfied victory condition keyed by the winning path and Nation, used as the key of the
        /// completion-tick record so each (path, nation) pair is timestamped exactly once.
        /// </summary>
        private readonly struct VictoryCandidate : IEquatable<VictoryCandidate>
        {
            public VictoryPath Path { get; }
            public int NationId { get; }

            public VictoryCandidate(VictoryPath path, int nationId)
            {
                Path = path;
                NationId = nationId;
            }

            public bool Equals(VictoryCandidate other) => Path == other.Path && NationId == other.NationId;

            public override bool Equals(object obj) => obj is VictoryCandidate other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((int)Path * 397) ^ NationId;
                }
            }
        }
    }

    /// <summary>
    /// The starting configuration for one Nation at match start (Req 12.1): its identity, its starting
    /// Resource stores, its starting Units, its starting population, and its starting Era (Prehistoric
    /// by default). Consumed by <see cref="VictorySystem.InitializeMatch"/> /
    /// <see cref="VictorySystem.SeedNation"/> to build the authoritative initial <see cref="MatchState"/>.
    /// </summary>
    public sealed class NationSeed
    {
        /// <summary>The stable per-Match id assigned to the seeded Nation.</summary>
        public int NationId { get; }

        /// <summary>Whether the seeded Nation is AI-controlled.</summary>
        public bool IsAI { get; }

        /// <summary>The Era the Nation begins at; defaults to <see cref="Era.Prehistoric"/> (Req 12.1).</summary>
        public Era StartingEra { get; }

        /// <summary>The starting population count.</summary>
        public int StartingPopulation { get; }

        /// <summary>The starting population capacity.</summary>
        public int StartingPopulationCapacity { get; }

        /// <summary>The starting Resource stores keyed by type; may be empty/null for none.</summary>
        public IReadOnlyDictionary<ResourceType, ResourceStore> StartingResources { get; }

        /// <summary>The starting Units to spawn for the Nation; may be empty/null for none.</summary>
        public IReadOnlyList<UnitSeed> StartingUnits { get; }

        public NationSeed(
            int nationId,
            bool isAI = false,
            IReadOnlyDictionary<ResourceType, ResourceStore> startingResources = null,
            IReadOnlyList<UnitSeed> startingUnits = null,
            int startingPopulation = 0,
            int startingPopulationCapacity = 0,
            Era startingEra = Era.Prehistoric)
        {
            NationId = nationId;
            IsAI = isAI;
            StartingResources = startingResources;
            StartingUnits = startingUnits;
            StartingPopulation = startingPopulation;
            StartingPopulationCapacity = startingPopulationCapacity;
            StartingEra = startingEra;
        }
    }

    /// <summary>
    /// A single starting Unit for a <see cref="NationSeed"/>: the Unit definition to spawn and the
    /// terrain cell to place it at (Req 12.1).
    /// </summary>
    public readonly struct UnitSeed
    {
        /// <summary>The definition of the Unit to spawn.</summary>
        public UnitDef Def { get; }

        /// <summary>The terrain cell the Unit is placed at.</summary>
        public CellCoord Cell { get; }

        public UnitSeed(UnitDef def, CellCoord cell)
        {
            Def = def;
            Cell = cell;
        }
    }
}
