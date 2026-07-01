using EpochWar.Core.Commands;
using EpochWar.Core.State;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that a single Nation's stored quantity for one
    /// <see cref="ResourceType"/> changed (Req 2.6).
    ///
    /// The <see cref="ResourceSystem"/> emits one of these for every store it mutates — whether
    /// through a production cycle (Req 2.2/2.5) or an atomic cost deduction (Req 2.3) — so the
    /// networking layer can replicate the change and the UI can refresh the displayed amount
    /// within its freshness budget (Req 2.6, 7.1/7.4). The event reports state that has
    /// <em>already</em> been applied; it carries the before/after amounts so consumers can render
    /// deltas without re-reading the store.
    ///
    /// <see cref="Delta"/> is the signed change actually applied to the store
    /// (<see cref="NewAmount"/> − <see cref="OldAmount"/>). For production capped at capacity the
    /// delta reflects only the portion that was stored, never the overflow that was discarded.
    /// </summary>
    public sealed class ResourceChangedEvent : GameEvent
    {
        /// <summary>The id of the Nation whose store changed.</summary>
        public int NationId { get; }

        /// <summary>The resource type whose stored quantity changed.</summary>
        public ResourceType ResourceType { get; }

        /// <summary>The stored quantity before the change.</summary>
        public float OldAmount { get; }

        /// <summary>The stored quantity after the change.</summary>
        public float NewAmount { get; }

        /// <summary>The store's capacity at the time of the change (&lt;= 0 means uncapped).</summary>
        public float Capacity { get; }

        public ResourceChangedEvent(
            int nationId,
            ResourceType resourceType,
            float oldAmount,
            float newAmount,
            float capacity)
        {
            NationId = nationId;
            ResourceType = resourceType;
            OldAmount = oldAmount;
            NewAmount = newAmount;
            Capacity = capacity;
        }

        /// <summary>The signed change actually applied to the store (stored portion only).</summary>
        public float Delta => NewAmount - OldAmount;

        public override string ToString()
            => $"ResourceChanged(nation {NationId}, {ResourceType}, "
               + $"{OldAmount:0.##} -> {NewAmount:0.##}, cap {(Capacity <= 0f ? "∞" : Capacity.ToString("0.##"))})";
    }
}
