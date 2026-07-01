using System;
using System.Collections.Generic;
using EpochWar.Core.Math;
using EpochWar.Core.State;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// The engine-free fog-of-war / vision resolver (Requirement 14).
    ///
    /// <para>
    /// Each <see cref="Tick"/> the system recomputes, for every Nation, the set of visible
    /// Terrain_Cells as the union — over every owned <see cref="UnitInstance"/> and
    /// <see cref="StructureInstance"/> — of the cells within that entity's
    /// <c>Def.SightRadius</c> of the entity's position (Req 14.1, 14.7). It then re-evaluates
    /// every enemy entity's hidden/visible classification against the recomputed set (Req 14.2,
    /// 14.8), maintaining each hidden entity's <see cref="VisionState.LastKnownPosition"/> at the
    /// exact visible→hidden transition tick (Req 14.3) and discarding it on the hidden→visible
    /// transition (Req 14.6) or on permanent removal while hidden (Req 14.9).
    /// </para>
    ///
    /// <para>
    /// <b>Where the state lives (Design Principle 5).</b> The per-Nation
    /// <see cref="VisionState"/> is owned <em>by this system</em> in a
    /// <see cref="Dictionary{TKey,TValue}"/> keyed by Nation id, not stored on
    /// <see cref="Nation"/>: the visible-cell set is a fully-derived cache recomputed from
    /// scratch every tick, so it belongs with the system that derives it.
    /// </para>
    ///
    /// <para>
    /// <b>Geometry (Design Principle 3).</b> All distance math is deterministic fixed-point
    /// (<see cref="Fixed"/>) — no <c>float</c>/<c>double</c>. Consistent with the
    /// <see cref="CombatSystem"/> convention, the distance test is planar (X/Z) squared distance
    /// versus squared radius: a candidate cell <c>(x, y, z)</c> is visible from an entity when the
    /// cell shares the entity's cell elevation <c>y</c> and <c>(dx*dx)+(dz*dz) &lt;= r*r</c>, where
    /// <c>dx</c>/<c>dz</c> are the planar offsets from the entity's continuous position to the
    /// integer cell coordinate (the cell's representative point, matching how the rest of the core
    /// treats a <see cref="CellCoord"/> as an integer world position). Candidate cells are scanned
    /// in a bounded box (the integer ceiling of the radius, plus a one-cell margin covering
    /// fractional positions) around the entity's cell and clamped to the terrain volume, so only
    /// real Terrain_Cells are ever added.
    /// </para>
    ///
    /// <para>
    /// <b>Entity keying.</b> Units and Structures are stored in separate id spaces
    /// (<see cref="MatchState.Units"/> / <see cref="MatchState.Structures"/>) that both allocate
    /// small monotonic integers, so a Unit id and a Structure id can collide. To key the single
    /// <see cref="VisionState.EnemyVisibility"/> / <see cref="VisionState.LastKnownPosition"/>
    /// <c>int</c>-keyed maps without collision, Unit ids are used as-is while Structure ids are
    /// offset by <see cref="StructureKeyOffset"/>. Callers build keys via <see cref="UnitKey"/> /
    /// <see cref="StructureKey"/>, and <see cref="GetDisplayPosition"/> / <see cref="OnEntityRemoved"/>
    /// accept that same key form.
    /// </para>
    ///
    /// <para>
    /// <b>Removal / integration.</b> Because the visible-cell set and classification are recomputed
    /// every tick, a permanently removed entity is simply never re-added to
    /// <see cref="VisionState.EnemyVisibility"/>; its stale <see cref="VisionState.LastKnownPosition"/>
    /// entry is actively pruned at the end of each <see cref="Tick"/> (Req 14.9). The wiring that
    /// calls <see cref="Tick"/> each frame (and may call <see cref="OnEntityRemoved"/> from the
    /// removal event path for immediacy) is added by <c>MatchSimulation</c> in a later task; this
    /// system is self-contained and correct on its own via the per-tick prune.
    /// </para>
    /// </summary>
    public sealed class VisionSystem
    {
        /// <summary>
        /// The offset added to a Structure's id to form its vision-map key, keeping Structure keys
        /// disjoint from Unit keys within the shared <c>int</c>-keyed vision maps. Match entity ids
        /// are small monotonic integers, so this offset is never reached by a real id.
        /// </summary>
        public const int StructureKeyOffset = 1_000_000_000;

        private readonly Dictionary<int, VisionState> _visionByNation = new Dictionary<int, VisionState>();

        // Reused scratch buffers so a Tick allocates nothing per Nation for the prune step.
        private readonly List<int> _pruneScratch = new List<int>();

        /// <summary>Builds the vision-map key for the Unit with id <paramref name="unitId"/>.</summary>
        public static int UnitKey(int unitId) => unitId;

        /// <summary>Builds the vision-map key for the Structure with id <paramref name="structureId"/>.</summary>
        public static int StructureKey(int structureId) => StructureKeyOffset + structureId;

        /// <summary>
        /// Returns the <see cref="VisionState"/> for <paramref name="nationId"/>, creating and
        /// registering an empty one on first access (create-on-demand). Never returns <c>null</c>.
        /// </summary>
        public VisionState GetVisionState(int nationId)
        {
            if (!_visionByNation.TryGetValue(nationId, out var vs))
            {
                vs = new VisionState();
                _visionByNation[nationId] = vs;
            }

            return vs;
        }

        /// <summary>
        /// Recomputes every Nation's visible-cell set and re-evaluates every enemy entity's
        /// hidden/visible classification against it, updating Last_Known_Position at the exact
        /// transition ticks and pruning stale entries for entities no longer present in the Match
        /// (Req 14.1, 14.2, 14.3, 14.6, 14.7, 14.8, 14.9).
        /// </summary>
        public void Tick(MatchState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            Int3 dims = state.Terrain?.Dimensions ?? Int3.Zero;

            foreach (int nationId in state.Nations.Keys)
            {
                VisionState vs = GetVisionState(nationId);

                // (1) Recompute the visible-cell set from scratch (Req 14.1, 14.7).
                vs.VisibleCells.Clear();
                foreach (UnitInstance unit in state.Units.Values)
                {
                    if (unit.OwnerNationId != nationId)
                    {
                        continue;
                    }

                    AddVisibleCells(vs.VisibleCells, unit.Position, SightRadiusOf(unit), dims);
                }

                foreach (StructureInstance structure in state.Structures.Values)
                {
                    if (structure.OwnerNationId != nationId)
                    {
                        continue;
                    }

                    AddVisibleCells(
                        vs.VisibleCells, WorldPosition.FromCell(structure.Origin), SightRadiusOf(structure), dims);
                }

                // (2) Re-classify every enemy entity against the recomputed set (Req 14.2, 14.8),
                //     recording/clearing Last_Known_Position at the transition moments (Req 14.3, 14.6).
                var currentEnemyKeys = new HashSet<int>();
                foreach (UnitInstance unit in state.Units.Values)
                {
                    if (unit.OwnerNationId == nationId)
                    {
                        continue;
                    }

                    int key = UnitKey(unit.Id);
                    currentEnemyKeys.Add(key);
                    ClassifyEnemy(vs, key, unit.Position);
                }

                foreach (StructureInstance structure in state.Structures.Values)
                {
                    if (structure.OwnerNationId == nationId)
                    {
                        continue;
                    }

                    int key = StructureKey(structure.Id);
                    currentEnemyKeys.Add(key);
                    ClassifyEnemy(vs, key, WorldPosition.FromCell(structure.Origin));
                }

                // (3) Prune classification/Last_Known_Position for entities that no longer exist as
                //     enemies of this Nation — i.e. permanently removed from the Match (Req 14.9).
                PruneMissing(vs, currentEnemyKeys);
            }
        }

        /// <summary>
        /// Resolves the position to display for the enemy entity <paramref name="entityKey"/> from
        /// <paramref name="nationId"/>'s perspective — exactly one of three cases (Req 14.4/14.5/14.6):
        /// the <paramref name="currentPosition"/> when the entity is currently visible; the recorded
        /// <see cref="VisionState.LastKnownPosition"/> when it is hidden and one is recorded; and
        /// <c>null</c> (do not display) when it is hidden and none is recorded.
        /// </summary>
        public WorldPosition? GetDisplayPosition(int nationId, int entityKey, WorldPosition currentPosition)
        {
            VisionState vs = GetVisionState(nationId);

            if (vs.EnemyVisibility.TryGetValue(entityKey, out bool visible) && visible)
            {
                return currentPosition;
            }

            if (vs.LastKnownPosition.TryGetValue(entityKey, out WorldPosition lkp))
            {
                return lkp;
            }

            return null;
        }

        /// <summary>
        /// Discards any classification and Last_Known_Position record for <paramref name="entityKey"/>
        /// across every Nation's <see cref="VisionState"/>. Intended for the removal-event path so a
        /// permanently removed entity's stale Last_Known_Position is dropped immediately rather than at
        /// the next <see cref="Tick"/> (Req 14.9). Building the key via <see cref="UnitKey"/> /
        /// <see cref="StructureKey"/> keeps this consistent with the maps' key form.
        /// </summary>
        public void OnEntityRemoved(int entityKey)
        {
            foreach (VisionState vs in _visionByNation.Values)
            {
                vs.EnemyVisibility.Remove(entityKey);
                vs.LastKnownPosition.Remove(entityKey);
            }
        }

        /// <summary>
        /// Adds to <paramref name="visibleCells"/> every real Terrain_Cell (inside
        /// <paramref name="dims"/>) at the entity's cell elevation whose planar (X/Z) distance to
        /// <paramref name="position"/> is within <paramref name="radius"/>. Uses only deterministic
        /// fixed-point arithmetic and the same squared-distance-vs-squared-radius test as the
        /// <see cref="CombatSystem"/> (Req 14.1).
        /// </summary>
        private static void AddVisibleCells(
            HashSet<CellCoord> visibleCells, WorldPosition position, Fixed radius, Int3 dims)
        {
            if (radius < Fixed.Zero)
            {
                return;
            }

            int centerX = position.X.ToInt();
            int centerZ = position.Z.ToInt();
            int cellY = position.Y.ToInt();

            // A one-cell margin beyond the integer ceiling of the radius covers fractional positions;
            // the exact squared-distance test below decides actual membership, so over-scanning is
            // harmless.
            int reach = CeilToInt(radius) + 1;
            Fixed radiusSquared = radius * radius;

            for (int x = centerX - reach; x <= centerX + reach; x++)
            {
                Fixed dx = Fixed.FromInt(x) - position.X;
                Fixed dx2 = dx * dx;

                for (int z = centerZ - reach; z <= centerZ + reach; z++)
                {
                    var cell = new CellCoord(x, cellY, z);
                    if (!cell.IsInside(dims))
                    {
                        continue;
                    }

                    Fixed dz = Fixed.FromInt(z) - position.Z;
                    if (dx2 + (dz * dz) <= radiusSquared)
                    {
                        visibleCells.Add(cell);
                    }
                }
            }
        }

        /// <summary>
        /// Updates <paramref name="vs"/>'s classification for the enemy <paramref name="key"/> at
        /// <paramref name="position"/>: records Last_Known_Position on a visible→hidden transition
        /// (Req 14.3), clears it on a hidden→visible transition (Req 14.6), and stores the new
        /// visibility bit (Req 14.2, 14.8).
        /// </summary>
        private static void ClassifyEnemy(VisionState vs, int key, WorldPosition position)
        {
            CellCoord cell = ToCell(position);
            bool nowVisible = vs.VisibleCells.Contains(cell);
            bool wasVisible = vs.EnemyVisibility.TryGetValue(key, out bool prev) && prev;

            if (wasVisible && !nowVisible)
            {
                // Capture the position as of this exact transition tick (Req 14.3).
                vs.LastKnownPosition[key] = position;
            }
            else if (!wasVisible && nowVisible)
            {
                // Re-sighted: display falls through to current position (Req 14.6).
                vs.LastKnownPosition.Remove(key);
            }

            vs.EnemyVisibility[key] = nowVisible;
        }

        /// <summary>
        /// Removes every <see cref="VisionState.EnemyVisibility"/> and
        /// <see cref="VisionState.LastKnownPosition"/> entry whose key is not in
        /// <paramref name="currentEnemyKeys"/> — i.e. entities permanently removed from the Match
        /// (Req 14.9).
        /// </summary>
        private void PruneMissing(VisionState vs, HashSet<int> currentEnemyKeys)
        {
            _pruneScratch.Clear();
            foreach (int key in vs.EnemyVisibility.Keys)
            {
                if (!currentEnemyKeys.Contains(key))
                {
                    _pruneScratch.Add(key);
                }
            }

            foreach (int key in _pruneScratch)
            {
                vs.EnemyVisibility.Remove(key);
            }

            _pruneScratch.Clear();
            foreach (int key in vs.LastKnownPosition.Keys)
            {
                if (!currentEnemyKeys.Contains(key))
                {
                    _pruneScratch.Add(key);
                }
            }

            foreach (int key in _pruneScratch)
            {
                vs.LastKnownPosition.Remove(key);
            }
        }

        private static Fixed SightRadiusOf(UnitInstance unit) => unit.Def?.SightRadius ?? Fixed.Zero;

        private static Fixed SightRadiusOf(StructureInstance structure) => structure.Def?.SightRadius ?? Fixed.Zero;

        /// <summary>
        /// Converts a continuous <see cref="WorldPosition"/> to the terrain cell that contains it,
        /// truncating each component toward zero — the same convention the <see cref="CombatSystem"/>
        /// uses so an entity's "occupied cell" is computed identically everywhere.
        /// </summary>
        private static CellCoord ToCell(WorldPosition position)
            => new CellCoord(position.X.ToInt(), position.Y.ToInt(), position.Z.ToInt());

        /// <summary>
        /// The integer ceiling of a non-negative <see cref="Fixed"/> radius (0 for non-positive
        /// values). <see cref="Fixed.ToInt"/> truncates toward zero (floor for non-negatives), so a
        /// value with any fractional part rounds up by one.
        /// </summary>
        private static int CeilToInt(Fixed value)
        {
            if (value <= Fixed.Zero)
            {
                return 0;
            }

            int truncated = value.ToInt();
            return Fixed.FromInt(truncated) == value ? truncated : truncated + 1;
        }
    }
}
