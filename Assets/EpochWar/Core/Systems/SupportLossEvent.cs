using EpochWar.Core.Commands;
using EpochWar.Core.State;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// The kind of entity that lost its supporting terrain, so consumers can resolve the referenced
    /// id against the right collection on the <see cref="MatchState"/>.
    /// </summary>
    public enum SupportedEntityKind
    {
        /// <summary>The affected entity is a <see cref="StructureInstance"/> (Req 4).</summary>
        Structure = 0,

        /// <summary>The affected entity is a <see cref="UnitInstance"/> (Req 3).</summary>
        Unit = 1,
    }

    /// <summary>
    /// A <see cref="GameEvent"/> announcing that a Structure or Unit lost its supporting terrain due
    /// to a terrain modification and had the configured consequence applied to it (Req 6.4).
    ///
    /// The <see cref="TerrainSystem"/> emits one of these for every entity whose support cells were
    /// removed by the effects applied in a tick. It carries the before/after health and a
    /// <see cref="Destroyed"/> flag so the networking/UI layers can replicate the consequence and
    /// refresh the affected entity (or drop it) without re-deriving the terrain state. The event
    /// reports a consequence that has <em>already</em> been applied; it never itself mutates state.
    /// </summary>
    public sealed class SupportLossEvent : GameEvent
    {
        /// <summary>Whether the affected entity is a Structure or a Unit.</summary>
        public SupportedEntityKind EntityKind { get; }

        /// <summary>The id of the affected Structure or Unit.</summary>
        public int EntityId { get; }

        /// <summary>The id of the Nation that owned the affected entity.</summary>
        public int OwnerNationId { get; }

        /// <summary>The entity's health before the consequence was applied.</summary>
        public int OldHealth { get; }

        /// <summary>The entity's health after the consequence was applied (clamped at zero).</summary>
        public int NewHealth { get; }

        /// <summary>True when the consequence reduced the entity to zero health and removed it from the Match.</summary>
        public bool Destroyed { get; }

        public SupportLossEvent(
            SupportedEntityKind entityKind,
            int entityId,
            int ownerNationId,
            int oldHealth,
            int newHealth,
            bool destroyed)
        {
            EntityKind = entityKind;
            EntityId = entityId;
            OwnerNationId = ownerNationId;
            OldHealth = oldHealth;
            NewHealth = newHealth;
            Destroyed = destroyed;
        }

        public override string ToString()
            => $"SupportLoss({EntityKind}#{EntityId}, owner {OwnerNationId}, "
               + $"hp {OldHealth} -> {NewHealth}{(Destroyed ? ", destroyed" : string.Empty)})";
    }
}
