using System;
using System.Collections.Generic;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;

namespace EpochWar.Core.Navigation
{
    /// <summary>
    /// The result of a <see cref="Pathfinder"/> query: whether a route was found and, if so, the
    /// ordered sequence of surface cells to traverse and their continuous world waypoints (Req 3.2).
    ///
    /// <see cref="Cells"/> lists the standing cells from the start column through to the destination
    /// column (each the empty cell directly above a column's solid surface, or the direct endpoints
    /// for flying units). <see cref="Waypoints"/> is the same sequence expressed as
    /// <see cref="WorldPosition"/>s so the Unit_System (task 9.3) can advance a Unit smoothly along
    /// the route. When <see cref="Found"/> is <c>false</c> both collections are empty.
    /// </summary>
    public sealed class NavPath
    {
        /// <summary>A canonical "no route" result with empty <see cref="Cells"/>/<see cref="Waypoints"/>.</summary>
        public static readonly NavPath NotFound =
            new NavPath(false, Array.Empty<CellCoord>(), Array.Empty<WorldPosition>());

        /// <summary>True when a route to the destination exists.</summary>
        public bool Found { get; }

        /// <summary>The ordered surface cells from start to destination (empty when not found).</summary>
        public IReadOnlyList<CellCoord> Cells { get; }

        /// <summary>The route expressed as continuous world positions (empty when not found).</summary>
        public IReadOnlyList<WorldPosition> Waypoints { get; }

        /// <summary>The number of waypoints in the route.</summary>
        public int Length => Cells.Count;

        private NavPath(bool found, IReadOnlyList<CellCoord> cells, IReadOnlyList<WorldPosition> waypoints)
        {
            Found = found;
            Cells = cells;
            Waypoints = waypoints;
        }

        /// <summary>
        /// Builds a found path from an ordered list of <paramref name="cells"/>, deriving one
        /// <see cref="WorldPosition"/> waypoint per cell. An empty list yields
        /// <see cref="NotFound"/>.
        /// </summary>
        public static NavPath FromCells(IReadOnlyList<CellCoord> cells)
        {
            if (cells == null || cells.Count == 0)
            {
                return NotFound;
            }

            var waypoints = new WorldPosition[cells.Count];
            for (int i = 0; i < cells.Count; i++)
            {
                waypoints[i] = WorldPosition.FromCell(cells[i]);
            }

            return new NavPath(true, cells, waypoints);
        }
    }

    /// <summary>
    /// Deterministic A* pathfinding over a <see cref="NavGrid"/> (design "Terrain Representation" —
    /// Pathfinding; Req 3.2, 6.3).
    ///
    /// Ground units (<see cref="UnitRole.Worker"/>, <see cref="UnitRole.Soldier"/>,
    /// <see cref="UnitRole.Vehicle"/>) route over the walkable surface columns of the nav grid using
    /// A* with 4-connected (cardinal) movement, unit step cost, and the Manhattan distance heuristic
    /// — which is admissible for a 4-connected grid, so the search returns a shortest route. Flying
    /// units (<see cref="UnitRole.Aircraft"/>, <see cref="UnitRole.ColonyShip"/>) ignore the ground
    /// nav entirely and are given a direct start→destination route (design: "flying units ignore
    /// ground nav").
    ///
    /// The search is fully deterministic: the open set is ordered by <c>f = g + h</c> and ties are
    /// broken by the column's flat index (see <see cref="NavGrid.ColumnIndex"/>), and neighbours are
    /// always expanded in the same fixed order. Given identical inputs it therefore yields an
    /// identical path on the Host and in headless property tests, with no floating-point arithmetic.
    /// The pathfinder is stateless and safe to reuse across queries.
    /// </summary>
    public sealed class Pathfinder
    {
        // Cardinal neighbour offsets, expanded in this fixed order for determinism (-X, +X, -Z, +Z).
        private static readonly int[] NeighbourDx = { -1, 1, 0, 0 };
        private static readonly int[] NeighbourDz = { 0, 0, -1, 1 };

        /// <summary>
        /// Finds a route for a unit of the given <paramref name="role"/> from <paramref name="start"/>
        /// to <paramref name="destination"/> over <paramref name="grid"/>.
        ///
        /// For flying roles the result is a direct two-point route (or a single point when start and
        /// destination coincide), ignoring the ground grid. For ground roles the columns of
        /// <paramref name="start"/> and <paramref name="destination"/> must both be walkable and a
        /// step-height-respecting route must exist between them; otherwise <see cref="NavPath.NotFound"/>
        /// is returned. Ground route cells are snapped to each column's standing cell (the empty cell
        /// above its solid surface), so the caller need not supply exact surface <c>Y</c> values.
        /// </summary>
        public NavPath FindPath(NavGrid grid, UnitRole role, CellCoord start, CellCoord destination)
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            if (IsFlying(role))
            {
                return FindDirectPath(start, destination);
            }

            return FindGroundPath(grid, start, destination);
        }

        /// <summary>True for roles that bypass the ground nav grid and move in a straight line.</summary>
        public static bool IsFlying(UnitRole role)
            => role == UnitRole.Aircraft || role == UnitRole.ColonyShip;

        /// <summary>
        /// Builds the direct route used by flying units: just the endpoints (collapsed to a single
        /// point when they coincide). Flying movement is unobstructed by terrain.
        /// </summary>
        private static NavPath FindDirectPath(CellCoord start, CellCoord destination)
        {
            if (start == destination)
            {
                return NavPath.FromCells(new[] { start });
            }

            return NavPath.FromCells(new[] { start, destination });
        }

        /// <summary>
        /// Runs deterministic A* over the grid's walkable columns from the start column to the
        /// destination column, returning the standing-cell route or <see cref="NavPath.NotFound"/>.
        /// </summary>
        private static NavPath FindGroundPath(NavGrid grid, CellCoord start, CellCoord destination)
        {
            // Both endpoints must sit on walkable columns for a ground route to exist.
            if (!grid.IsWalkable(start.X, start.Z) || !grid.IsWalkable(destination.X, destination.Z))
            {
                return NavPath.NotFound;
            }

            int startIndex = grid.ColumnIndex(start.X, start.Z);
            int goalIndex = grid.ColumnIndex(destination.X, destination.Z);

            if (startIndex == goalIndex)
            {
                grid.TryGetStandingCell(start.X, start.Z, out CellCoord only);
                return NavPath.FromCells(new[] { only });
            }

            int columnCount = grid.ColumnCount;
            var gScore = new int[columnCount];
            var cameFrom = new int[columnCount];
            var closed = new bool[columnCount];
            for (int i = 0; i < columnCount; i++)
            {
                gScore[i] = int.MaxValue;
                cameFrom[i] = -1;
            }

            gScore[startIndex] = 0;

            var open = new MinHeap(columnCount);
            open.Push(startIndex, Heuristic(grid, start.X, start.Z, destination.X, destination.Z));

            while (open.TryPop(out int currentIndex))
            {
                if (currentIndex == goalIndex)
                {
                    return Reconstruct(grid, cameFrom, startIndex, goalIndex);
                }

                // A column may be queued more than once with improving scores; skip stale entries.
                if (closed[currentIndex])
                {
                    continue;
                }

                closed[currentIndex] = true;

                int currentX = currentIndex % grid.Dimensions.X;
                int currentZ = currentIndex / grid.Dimensions.X;
                int currentG = gScore[currentIndex];

                for (int n = 0; n < NeighbourDx.Length; n++)
                {
                    int nx = currentX + NeighbourDx[n];
                    int nz = currentZ + NeighbourDz[n];

                    if (!grid.InBoundsColumn(nx, nz))
                    {
                        continue;
                    }

                    if (!grid.CanTraverse(currentX, currentZ, nx, nz))
                    {
                        continue;
                    }

                    int neighbourIndex = grid.ColumnIndex(nx, nz);
                    if (closed[neighbourIndex])
                    {
                        continue;
                    }

                    int tentativeG = currentG + 1;
                    if (tentativeG < gScore[neighbourIndex])
                    {
                        cameFrom[neighbourIndex] = currentIndex;
                        gScore[neighbourIndex] = tentativeG;
                        int f = tentativeG + Heuristic(grid, nx, nz, destination.X, destination.Z);
                        open.Push(neighbourIndex, f);
                    }
                }
            }

            return NavPath.NotFound;
        }

        /// <summary>The Manhattan distance heuristic between two columns (admissible for 4-connectivity).</summary>
        private static int Heuristic(NavGrid grid, int x, int z, int goalX, int goalZ)
        {
            int dx = x - goalX;
            if (dx < 0)
            {
                dx = -dx;
            }

            int dz = z - goalZ;
            if (dz < 0)
            {
                dz = -dz;
            }

            return dx + dz;
        }

        /// <summary>
        /// Walks the <paramref name="cameFrom"/> chain back from the goal to the start and returns the
        /// start→goal route as standing cells.
        /// </summary>
        private static NavPath Reconstruct(NavGrid grid, int[] cameFrom, int startIndex, int goalIndex)
        {
            var columns = new List<int>();
            int cursor = goalIndex;
            columns.Add(cursor);
            while (cursor != startIndex)
            {
                cursor = cameFrom[cursor];
                if (cursor < 0)
                {
                    // Should not happen once the goal is reached, but stay safe rather than loop.
                    return NavPath.NotFound;
                }

                columns.Add(cursor);
            }

            columns.Reverse();

            int dimX = grid.Dimensions.X;
            var cells = new CellCoord[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                int index = columns[i];
                int x = index % dimX;
                int z = index / dimX;
                grid.TryGetStandingCell(x, z, out CellCoord standing);
                cells[i] = standing;
            }

            return NavPath.FromCells(cells);
        }

        /// <summary>
        /// A minimal binary min-heap over column indices, ordered by <c>f</c> score with the column
        /// index itself as a deterministic tie-breaker.
        ///
        /// A hand-rolled heap keeps the pathfinder free of any allocation-per-comparison and,
        /// crucially, makes the pop order a total, input-independent function of <c>(f, columnIndex)</c>
        /// so the search is reproducible. Re-pushing a column with a better score is allowed; stale
        /// entries are filtered by the closed-set check in the search loop.
        /// </summary>
        private sealed class MinHeap
        {
            private int[] _column;
            private int[] _f;
            private int _count;

            public MinHeap(int capacityHint)
            {
                int initial = capacityHint > 0 ? capacityHint : 4;
                _column = new int[initial];
                _f = new int[initial];
                _count = 0;
            }

            public void Push(int column, int f)
            {
                if (_count == _column.Length)
                {
                    Grow();
                }

                int i = _count++;
                _column[i] = column;
                _f[i] = f;

                // Sift up.
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (Compare(i, parent) >= 0)
                    {
                        break;
                    }

                    Swap(i, parent);
                    i = parent;
                }
            }

            public bool TryPop(out int column)
            {
                if (_count == 0)
                {
                    column = -1;
                    return false;
                }

                column = _column[0];
                _count--;
                if (_count > 0)
                {
                    _column[0] = _column[_count];
                    _f[0] = _f[_count];
                    SiftDown(0);
                }

                return true;
            }

            private void SiftDown(int i)
            {
                while (true)
                {
                    int left = (2 * i) + 1;
                    int right = (2 * i) + 2;
                    int smallest = i;

                    if (left < _count && Compare(left, smallest) < 0)
                    {
                        smallest = left;
                    }

                    if (right < _count && Compare(right, smallest) < 0)
                    {
                        smallest = right;
                    }

                    if (smallest == i)
                    {
                        break;
                    }

                    Swap(i, smallest);
                    i = smallest;
                }
            }

            // Orders by f, then by column index so ties resolve identically every run.
            private int Compare(int a, int b)
            {
                if (_f[a] != _f[b])
                {
                    return _f[a] < _f[b] ? -1 : 1;
                }

                if (_column[a] != _column[b])
                {
                    return _column[a] < _column[b] ? -1 : 1;
                }

                return 0;
            }

            private void Swap(int a, int b)
            {
                int tc = _column[a];
                _column[a] = _column[b];
                _column[b] = tc;

                int tf = _f[a];
                _f[a] = _f[b];
                _f[b] = tf;
            }

            private void Grow()
            {
                int newSize = _column.Length * 2;
                Array.Resize(ref _column, newSize);
                Array.Resize(ref _f, newSize);
            }
        }
    }
}
