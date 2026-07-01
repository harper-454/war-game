using System;
using System.Collections.Generic;
using EpochWar.Core.State;

namespace EpochWar.Core.Navigation
{
    /// <summary>
    /// The ground navigation grid derived from a <see cref="TerrainVolume"/>'s top solid surface
    /// (design "Terrain Representation" — Pathfinding; Req 3.2, 6.3).
    ///
    /// The battlefield is a 3D voxel/cell field, but ground units only ever stand on the highest
    /// solid cell of each vertical column. The nav grid collapses the volume to a 2D field of
    /// <em>columns</em> keyed by <c>(X, Z)</c>: for each column it stores the <see cref="SurfaceHeight"/>
    /// — the <c>Y</c> index of the topmost solid <see cref="TerrainCell"/>, or <c>-1</c> when the
    /// column has no solid cell at all. A column is <see cref="IsWalkable"/> exactly when it has a
    /// solid surface, and a ground unit standing there occupies the empty cell directly above it
    /// (see <see cref="TryGetStandingCell"/>).
    ///
    /// Adjacent columns are traversable only when the step between their surfaces is at most
    /// <see cref="MaxStepHeight"/> cells, so digging a deep pit or blasting away a ridge (via
    /// <see cref="TerrainVolume.ApplyEffect"/>) can sever a route. When cells change, callers pass
    /// the modified coordinates to <see cref="Recompute"/>, which rebuilds <em>only</em> the surface
    /// of the touched columns rather than the whole grid (Req 6.3).
    ///
    /// The grid is purely derived from the volume and holds no game state of its own; it is
    /// deterministic and engine-free so <see cref="Pathfinder"/> produces identical results on the
    /// Host and in headless property tests. Flying units (<c>Aircraft</c>/<c>ColonyShip</c>) bypass
    /// this grid entirely — see <see cref="Pathfinder"/>.
    /// </summary>
    public sealed class NavGrid
    {
        /// <summary>Sentinel <see cref="SurfaceHeight"/> for a column with no solid cell (not walkable).</summary>
        public const int NoSurface = -1;

        private readonly int[] _surface;
        private readonly int _dimX;
        private readonly int _dimY;
        private readonly int _dimZ;

        /// <summary>The dimensions (in cells) of the volume this grid was derived from.</summary>
        public Int3 Dimensions { get; }

        /// <summary>
        /// The maximum difference in surface height, in cells, between two horizontally adjacent
        /// columns that a ground unit may still traverse. A larger value lets units climb steeper
        /// terrain; the default of <c>1</c> models walking up or down a single cell step.
        /// </summary>
        public int MaxStepHeight { get; }

        /// <summary>The number of columns in the grid (<c>Dimensions.X * Dimensions.Z</c>).</summary>
        public int ColumnCount => _surface.Length;

        /// <summary>
        /// Builds a nav grid from <paramref name="volume"/> by scanning every column for its topmost
        /// solid cell (Req 6.3). <paramref name="maxStepHeight"/> bounds how far up/down a unit may
        /// step between adjacent columns (clamped to at least zero; zero means only equal-height
        /// neighbours connect).
        /// </summary>
        public NavGrid(TerrainVolume volume, int maxStepHeight = 1)
        {
            if (volume == null)
            {
                throw new ArgumentNullException(nameof(volume));
            }

            Dimensions = volume.Dimensions;
            _dimX = Dimensions.X > 0 ? Dimensions.X : 0;
            _dimY = Dimensions.Y > 0 ? Dimensions.Y : 0;
            _dimZ = Dimensions.Z > 0 ? Dimensions.Z : 0;
            MaxStepHeight = maxStepHeight < 0 ? 0 : maxStepHeight;

            _surface = new int[_dimX * _dimZ];

            for (int z = 0; z < _dimZ; z++)
            {
                for (int x = 0; x < _dimX; x++)
                {
                    _surface[ColumnIndex(x, z)] = ScanSurface(volume, x, z);
                }
            }
        }

        /// <summary>
        /// The flat index of the column at <paramref name="x"/>,<paramref name="z"/> using X-fastest
        /// ordering. The caller is responsible for bounds checking (see <see cref="InBoundsColumn"/>).
        /// This ordering is also used as the deterministic tie-breaker in <see cref="Pathfinder"/>.
        /// </summary>
        public int ColumnIndex(int x, int z) => x + (_dimX * z);

        /// <summary>True when <paramref name="x"/>,<paramref name="z"/> is a valid column in the grid.</summary>
        public bool InBoundsColumn(int x, int z)
            => x >= 0 && x < _dimX && z >= 0 && z < _dimZ;

        /// <summary>
        /// The <c>Y</c> index of the topmost solid cell in the column, or <see cref="NoSurface"/>
        /// (<c>-1</c>) when the column has no solid cell or lies outside the grid.
        /// </summary>
        public int SurfaceHeight(int x, int z)
            => InBoundsColumn(x, z) ? _surface[ColumnIndex(x, z)] : NoSurface;

        /// <summary>
        /// True when the column at <paramref name="x"/>,<paramref name="z"/> has a solid surface a
        /// ground unit can stand on. Out-of-range columns are never walkable.
        /// </summary>
        public bool IsWalkable(int x, int z) => SurfaceHeight(x, z) != NoSurface;

        /// <summary>Convenience overload of <see cref="IsWalkable(int,int)"/> keyed by a cell coordinate.</summary>
        public bool IsWalkable(CellCoord c) => IsWalkable(c.X, c.Z);

        /// <summary>
        /// Returns, via <paramref name="standing"/>, the cell a ground unit occupies when standing on
        /// the column's surface — the empty cell directly above the top solid cell,
        /// <c>(x, SurfaceHeight + 1, z)</c>. Returns <c>false</c> (and <see cref="CellCoord.Zero"/>)
        /// when the column is not walkable.
        /// </summary>
        public bool TryGetStandingCell(int x, int z, out CellCoord standing)
        {
            int surface = SurfaceHeight(x, z);
            if (surface == NoSurface)
            {
                standing = CellCoord.Zero;
                return false;
            }

            standing = new CellCoord(x, surface + 1, z);
            return true;
        }

        /// <summary>
        /// True when a ground unit may move directly between the two horizontally adjacent columns:
        /// both must be walkable and the difference between their surface heights must not exceed
        /// <see cref="MaxStepHeight"/>. Adjacency itself is enforced by <see cref="Pathfinder"/>;
        /// this method only tests walkability and the step constraint.
        /// </summary>
        public bool CanTraverse(int fromX, int fromZ, int toX, int toZ)
        {
            int fromSurface = SurfaceHeight(fromX, fromZ);
            int toSurface = SurfaceHeight(toX, toZ);

            if (fromSurface == NoSurface || toSurface == NoSurface)
            {
                return false;
            }

            int step = fromSurface - toSurface;
            if (step < 0)
            {
                step = -step;
            }

            return step <= MaxStepHeight;
        }

        /// <summary>
        /// Recomputes the surface height for exactly the columns touched by
        /// <paramref name="changedCells"/> — the modified-cell list returned by
        /// <see cref="TerrainVolume.ApplyEffect"/> — leaving every other column untouched (Req 6.3).
        ///
        /// Each distinct <c>(X, Z)</c> column among the changed cells is rescanned against the same
        /// <paramref name="volume"/>. The method returns one coordinate per column whose surface
        /// actually changed, encoded as <c>(X, newSurfaceHeight, Z)</c> (with
        /// <c>Y = <see cref="NoSurface"/></c> when the column lost its last solid cell), so callers can
        /// invalidate cached routes that touched those nodes. A column whose surface is unchanged is
        /// not reported. Passing <c>null</c> or an empty sequence recomputes nothing.
        /// </summary>
        public IReadOnlyList<CellCoord> Recompute(TerrainVolume volume, IEnumerable<CellCoord> changedCells)
        {
            if (volume == null)
            {
                throw new ArgumentNullException(nameof(volume));
            }

            var changedColumns = new List<CellCoord>();
            if (changedCells == null)
            {
                return changedColumns;
            }

            // Collapse the changed cells to their distinct columns so each is rescanned at most once.
            var seen = new HashSet<int>();
            foreach (var cell in changedCells)
            {
                if (!InBoundsColumn(cell.X, cell.Z))
                {
                    continue;
                }

                int index = ColumnIndex(cell.X, cell.Z);
                if (!seen.Add(index))
                {
                    continue;
                }

                int previous = _surface[index];
                int updated = ScanSurface(volume, cell.X, cell.Z);
                if (updated != previous)
                {
                    _surface[index] = updated;
                    changedColumns.Add(new CellCoord(cell.X, updated, cell.Z));
                }
            }

            return changedColumns;
        }

        /// <summary>
        /// Scans the column top-down and returns the <c>Y</c> of its highest solid cell, or
        /// <see cref="NoSurface"/> when the column is entirely empty.
        /// </summary>
        private int ScanSurface(TerrainVolume volume, int x, int z)
        {
            for (int y = _dimY - 1; y >= 0; y--)
            {
                if (volume.IsSolid(new CellCoord(x, y, z)))
                {
                    return y;
                }
            }

            return NoSurface;
        }
    }
}
