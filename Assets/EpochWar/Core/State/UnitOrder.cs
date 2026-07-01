using System;
using System.Collections.Generic;

namespace EpochWar.Core.State
{
    /// <summary>
    /// The current standing order a <see cref="UnitInstance"/> is executing (Req 3.2).
    ///
    /// An <see cref="OrderKind.Idle"/> order means the Unit holds position. A
    /// <see cref="OrderKind.Move"/> order carries the movement <see cref="Destination"/> together
    /// with the navigable <see cref="Path"/> (the ordered world waypoints the Unit_System produced
    /// for the Unit from the nav grid, task 9.3) and the <see cref="WaypointIndex"/> of the next
    /// waypoint the Unit is heading toward. The UnitSystem advances the Unit along the path each
    /// tick, incrementing the index as waypoints are reached, and returns the Unit to
    /// <see cref="Idle"/> once the destination is reached.
    ///
    /// Modelled as an immutable value type: advancing progress produces a new order via
    /// <see cref="WithWaypointIndex"/> rather than mutating in place, so it stays cheap to store on
    /// every Unit and safe to compare.
    /// </summary>
    public readonly struct UnitOrder
    {
        /// <summary>The category of standing order.</summary>
        public enum OrderKind
        {
            /// <summary>No active order; the Unit holds position.</summary>
            Idle = 0,

            /// <summary>The Unit is moving toward a destination along <see cref="Path"/>.</summary>
            Move = 1
        }

        /// <summary>The kind of order currently in effect.</summary>
        public OrderKind Kind { get; }

        /// <summary>
        /// The destination terrain cell of a <see cref="OrderKind.Move"/> order (Req 3.2). For an
        /// <see cref="Idle"/> order this is <see cref="CellCoord.Zero"/> and carries no meaning.
        /// </summary>
        public CellCoord Destination { get; }

        /// <summary>
        /// The ordered world waypoints from the Unit's start to its <see cref="Destination"/>, as
        /// produced by the pathfinder (empty for an <see cref="Idle"/> order). Never <c>null</c>.
        /// </summary>
        public IReadOnlyList<WorldPosition> Path { get; }

        /// <summary>
        /// The index into <see cref="Path"/> of the next waypoint the Unit is heading toward. When it
        /// reaches or exceeds <c>Path.Count</c> the destination has been reached and the order
        /// completes.
        /// </summary>
        public int WaypointIndex { get; }

        private static readonly IReadOnlyList<WorldPosition> EmptyPath = Array.Empty<WorldPosition>();

        private UnitOrder(OrderKind kind, CellCoord destination, IReadOnlyList<WorldPosition> path, int waypointIndex)
        {
            Kind = kind;
            Destination = destination;
            Path = path ?? EmptyPath;
            WaypointIndex = waypointIndex;
        }

        /// <summary>The default "no order" state.</summary>
        public static readonly UnitOrder Idle = new UnitOrder(OrderKind.Idle, CellCoord.Zero, EmptyPath, 0);

        /// <summary>
        /// Creates a <see cref="OrderKind.Move"/> order toward <paramref name="destination"/> along
        /// <paramref name="path"/>, starting at the given <paramref name="waypointIndex"/> (defaults to
        /// <c>1</c> so the Unit heads to the first waypoint after its start). A <c>null</c> or empty
        /// path yields <see cref="Idle"/> since there is nowhere to move.
        /// </summary>
        public static UnitOrder Move(CellCoord destination, IReadOnlyList<WorldPosition> path, int waypointIndex = 1)
        {
            if (path == null || path.Count == 0)
            {
                return Idle;
            }

            int index = waypointIndex < 0 ? 0 : waypointIndex;
            return new UnitOrder(OrderKind.Move, destination, path, index);
        }

        /// <summary>
        /// Returns a copy of this move order advanced to <paramref name="waypointIndex"/>, preserving
        /// the destination and path. Calling this on an <see cref="Idle"/> order returns
        /// <see cref="Idle"/>.
        /// </summary>
        public UnitOrder WithWaypointIndex(int waypointIndex)
        {
            if (Kind != OrderKind.Move)
            {
                return Idle;
            }

            int index = waypointIndex < 0 ? 0 : waypointIndex;
            return new UnitOrder(OrderKind.Move, Destination, Path, index);
        }

        public override string ToString()
            => Kind == OrderKind.Move
                ? $"Order(Move -> {Destination}, waypoint {WaypointIndex}/{Path.Count})"
                : "Order(Idle)";
    }
}
