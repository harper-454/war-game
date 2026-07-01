using EpochWar.Core.Commands;
using EpochWar.Core.State;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing movement of a Unit toward, or arrival at, a destination
    /// (Req 3.2).
    ///
    /// The <see cref="UnitSystem"/> emits one with <see cref="Arrived"/> = <c>false</c> when a move
    /// order is issued (the Unit begins travelling along a navigable path) and one with
    /// <see cref="Arrived"/> = <c>true</c> when the Unit reaches its destination during a tick. The
    /// event carries the Unit's owning Nation, the Unit id, the destination cell, and the Unit's
    /// current position so the networking/UI layers can replicate movement without re-deriving the
    /// path.
    /// </summary>
    public sealed class UnitMovedEvent : GameEvent
    {
        /// <summary>The id of the Nation that owns the moving Unit.</summary>
        public int NationId { get; }

        /// <summary>The id of the moving Unit.</summary>
        public int UnitId { get; }

        /// <summary>The destination cell the Unit is moving toward (Req 3.2).</summary>
        public CellCoord Destination { get; }

        /// <summary>The Unit's position at the time of the event.</summary>
        public WorldPosition Position { get; }

        /// <summary>True when the Unit has reached its destination; false when it has just begun moving.</summary>
        public bool Arrived { get; }

        public UnitMovedEvent(int nationId, int unitId, CellCoord destination, WorldPosition position, bool arrived)
        {
            NationId = nationId;
            UnitId = unitId;
            Destination = destination;
            Position = position;
            Arrived = arrived;
        }

        public override string ToString()
            => $"UnitMoved(nation {NationId}, unit #{UnitId} -> {Destination}, {(Arrived ? "arrived" : "moving")})";
    }
}
