using Unity.Collections;
using Unity.Netcode;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Core.Systems;

namespace EpochWar.Unity.Net
{
    /// <summary>
    /// Discriminates the concrete <see cref="GameEvent"/> a <see cref="NetGameEvent"/> re-encodes for
    /// replication. Terrain modifications are intentionally absent: they are replicated by the
    /// dedicated <see cref="TerrainDeltaReplicator"/> as deterministic effect deltas (Req 6.5), not
    /// through this general gameplay-event stream.
    /// </summary>
    public enum NetGameEventKind : byte
    {
        Unknown = 0,
        ResourceChanged = 1,
        UnitRecruited = 2,
        UnitEliminated = 3,
        EraAdvanced = 4,
        NationEliminated = 5,
        MatchEnded = 6,
    }

    /// <summary>
    /// The wire form of a resolved <see cref="GameEvent"/> broadcast from the Host to every client
    /// each tick (Req 8.2). This is the "event-broadcast" half of the state-reflection design: after
    /// the authoritative simulation resolves a tick, the Host drains the ordered
    /// <see cref="GameEvent"/>s and forwards a compact, flattened re-encoding of each so client
    /// presentation can mirror the change (HUD amounts, spawned/removed views, era unlocks,
    /// end-of-match summary — Req 2.6, 7.4, 12.3).
    ///
    /// <para>Like <see cref="CommandEnvelope"/>, a single flattened value type carries a
    /// <see cref="NetGameEventKind"/> tag plus a superset of fields whose meaning depends on the tag;
    /// this keeps the Core events free of any Netcode dependency. It captures a representative set of
    /// events; unmapped event types are simply not forwarded (they carry no client-visible payload in
    /// this scope), and new kinds extend the same switch in <see cref="TryEncode"/>. Consumers read
    /// the event via <see cref="CommandRpcRouter.EventsReplicated"/>.</para>
    /// </summary>
    public struct NetGameEvent : INetworkSerializable
    {
        public NetGameEventKind Kind;

        // NationId meaning per kind:
        //   ResourceChanged/UnitRecruited/UnitEliminated/EraAdvanced -> owning Nation
        //   NationEliminated -> eliminated Nation; MatchEnded -> winning Nation
        public int NationId;

        // IntA per kind:
        //   ResourceChanged -> (int)ResourceType
        //   UnitRecruited/UnitEliminated -> UnitId
        //   EraAdvanced -> (int)FromEra
        //   NationEliminated -> deploying (by) Nation id
        //   MatchEnded -> (int)VictoryPath
        public int IntA;

        // IntB per kind:
        //   UnitRecruited -> StructureId
        //   EraAdvanced -> (int)ToEra
        public int IntB;

        // FloatA/FloatB per kind:
        //   ResourceChanged -> NewAmount / Capacity
        public float FloatA;
        public float FloatB;

        // Long payload:
        //   MatchEnded -> CompletionTick
        public long LongA;

        // StrA per kind:
        //   UnitRecruited/UnitEliminated -> UnitDef id
        //   NationEliminated -> weapon TechnologyId
        public FixedString64Bytes StrA;

        // Cell per kind:
        //   UnitRecruited -> spawn cell
        public NetCoord Cell;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref NationId);

            switch (Kind)
            {
                case NetGameEventKind.ResourceChanged:
                    serializer.SerializeValue(ref IntA);   // ResourceType
                    serializer.SerializeValue(ref FloatA); // NewAmount
                    serializer.SerializeValue(ref FloatB); // Capacity
                    break;

                case NetGameEventKind.UnitRecruited:
                    serializer.SerializeValue(ref IntA); // UnitId
                    serializer.SerializeValue(ref IntB); // StructureId
                    serializer.SerializeValue(ref StrA); // UnitDef id
                    serializer.SerializeValue(ref Cell); // spawn cell
                    break;

                case NetGameEventKind.UnitEliminated:
                    serializer.SerializeValue(ref IntA); // UnitId
                    serializer.SerializeValue(ref StrA); // UnitDef id
                    break;

                case NetGameEventKind.EraAdvanced:
                    serializer.SerializeValue(ref IntA); // FromEra
                    serializer.SerializeValue(ref IntB); // ToEra
                    break;

                case NetGameEventKind.NationEliminated:
                    serializer.SerializeValue(ref IntA); // by-Nation id
                    serializer.SerializeValue(ref StrA); // weapon tech id
                    break;

                case NetGameEventKind.MatchEnded:
                    serializer.SerializeValue(ref IntA);  // VictoryPath
                    serializer.SerializeValue(ref LongA); // CompletionTick
                    break;
            }
        }

        /// <summary>Convenience accessor for a <see cref="NetGameEventKind.ResourceChanged"/> event's resource type.</summary>
        public ResourceType ResourceTypeValue => (ResourceType)IntA;

        /// <summary>
        /// Attempts to flatten a Core <see cref="GameEvent"/> into its wire form. Returns
        /// <c>false</c> for terrain modifications (owned by <see cref="TerrainDeltaReplicator"/>) and
        /// for event types with no client-visible payload in this scope, so the caller skips them.
        /// </summary>
        public static bool TryEncode(GameEvent ev, out NetGameEvent net)
        {
            net = default;

            switch (ev)
            {
                case ResourceChangedEvent e:
                    net = new NetGameEvent
                    {
                        Kind = NetGameEventKind.ResourceChanged,
                        NationId = e.NationId,
                        IntA = (int)e.ResourceType,
                        FloatA = e.NewAmount,
                        FloatB = e.Capacity,
                    };
                    return true;

                case UnitRecruitedEvent e:
                    net = new NetGameEvent
                    {
                        Kind = NetGameEventKind.UnitRecruited,
                        NationId = e.NationId,
                        IntA = e.UnitId,
                        IntB = e.StructureId,
                        StrA = Pack(e.UnitDefId),
                        Cell = NetCoord.Of(e.SpawnCell),
                    };
                    return true;

                case UnitEliminatedEvent e:
                    net = new NetGameEvent
                    {
                        Kind = NetGameEventKind.UnitEliminated,
                        NationId = e.NationId,
                        IntA = e.UnitId,
                        StrA = Pack(e.UnitDefId),
                    };
                    return true;

                case EraAdvancedEvent e:
                    net = new NetGameEvent
                    {
                        Kind = NetGameEventKind.EraAdvanced,
                        NationId = e.NationId,
                        IntA = (int)e.FromEra,
                        IntB = (int)e.ToEra,
                    };
                    return true;

                case NationEliminatedEvent e:
                    net = new NetGameEvent
                    {
                        Kind = NetGameEventKind.NationEliminated,
                        NationId = e.EliminatedNationId,
                        IntA = e.ByNationId,
                        StrA = Pack(e.TechnologyId),
                    };
                    return true;

                case MatchEndedEvent e:
                    net = new NetGameEvent
                    {
                        Kind = NetGameEventKind.MatchEnded,
                        NationId = e.WinningNationId,
                        IntA = (int)e.Path,
                        LongA = e.CompletionTick,
                    };
                    return true;

                default:
                    // TerrainModifiedEvent and any other unmapped event are not forwarded here.
                    return false;
            }
        }

        private static FixedString64Bytes Pack(string value)
        {
            var s = new FixedString64Bytes();
            if (!string.IsNullOrEmpty(value))
            {
                s.Append(value);
            }

            return s;
        }
    }

    /// <summary>
    /// A single tick's ordered batch of <see cref="NetGameEvent"/>s, sent as one <c>ClientRpc</c>
    /// payload so a tick's events arrive and are applied together and in order. The array is
    /// length-prefixed and hand-serialized to avoid any dependency on optional Netcode array
    /// helpers for arrays of <see cref="INetworkSerializable"/> elements.
    /// </summary>
    public struct NetGameEventBatch : INetworkSerializable
    {
        public NetGameEvent[] Events;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            int count = Events?.Length ?? 0;
            serializer.SerializeValue(ref count);

            if (serializer.IsReader)
            {
                Events = new NetGameEvent[count < 0 ? 0 : count];
            }

            for (int i = 0; i < Events.Length; i++)
            {
                serializer.SerializeValue(ref Events[i]);
            }
        }
    }
}
