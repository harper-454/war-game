using System;
using Unity.Netcode;
using EpochWar.Core.State;

namespace EpochWar.Unity.Net
{
    /// <summary>
    /// The compact, replicated snapshot of Match lifecycle state (Req 8.2, 12.3).
    ///
    /// This is the payload of the Host-owned <c>NetworkVariable</c> on the
    /// <see cref="CommandRpcRouter"/>: Netcode automatically replicates it to every client whenever
    /// the Host mutates it, giving clients an authoritative view of the simulation clock and the
    /// end-of-match outcome without a bespoke RPC (the "NetworkVariable/snapshot" half of the state
    /// reflection design). Granular per-entity changes ride the event-broadcast stream instead
    /// (<see cref="NetGameEvent"/>); this variable is the always-current headline.
    ///
    /// It is an unmanaged struct (only blittable fields) so it satisfies
    /// <c>NetworkVariable&lt;T&gt;</c>'s constraints directly, and it implements
    /// <see cref="IEquatable{T}"/> so Netcode's dirty-check only replicates real changes.
    /// </summary>
    public struct NetMatchClock : INetworkSerializable, IEquatable<NetMatchClock>
    {
        /// <summary>The authoritative <see cref="MatchState.TickCount"/> as of the last Host tick.</summary>
        public long TickCount;

        /// <summary>The Match lifecycle status (mirrors <see cref="MatchStatus"/>).</summary>
        public byte Status;

        /// <summary>True once the Match has ended and <see cref="WinningNationId"/>/<see cref="Path"/> are populated.</summary>
        public bool HasOutcome;

        /// <summary>The winning Nation id when <see cref="HasOutcome"/> is true (Req 12.4).</summary>
        public int WinningNationId;

        /// <summary>The satisfied <see cref="VictoryPath"/> when <see cref="HasOutcome"/> is true (Req 12.4).</summary>
        public byte Path;

        /// <summary>The tick the winning condition completed, for the earliest-completion tie-break (Req 11.4).</summary>
        public long CompletionTick;

        public MatchStatus StatusEnum => (MatchStatus)Status;

        public VictoryPath PathEnum => (VictoryPath)Path;

        /// <summary>Builds an in-progress clock snapshot for the given tick.</summary>
        public static NetMatchClock InProgress(long tick) => new NetMatchClock
        {
            TickCount = tick,
            Status = (byte)MatchStatus.InProgress,
            HasOutcome = false,
        };

        /// <summary>Builds an ended clock snapshot mirroring a resolved <see cref="MatchOutcome"/>.</summary>
        public static NetMatchClock Ended(long tick, MatchOutcome outcome) => new NetMatchClock
        {
            TickCount = tick,
            Status = (byte)MatchStatus.Ended,
            HasOutcome = outcome != null,
            WinningNationId = outcome?.WinningNationId ?? 0,
            Path = (byte)(outcome?.Path ?? VictoryPath.Annihilation),
            CompletionTick = outcome?.CompletionTick ?? tick,
        };

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref TickCount);
            serializer.SerializeValue(ref Status);
            serializer.SerializeValue(ref HasOutcome);
            serializer.SerializeValue(ref WinningNationId);
            serializer.SerializeValue(ref Path);
            serializer.SerializeValue(ref CompletionTick);
        }

        public bool Equals(NetMatchClock other)
            => TickCount == other.TickCount
               && Status == other.Status
               && HasOutcome == other.HasOutcome
               && WinningNationId == other.WinningNationId
               && Path == other.Path
               && CompletionTick == other.CompletionTick;

        public override bool Equals(object obj) => obj is NetMatchClock other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + TickCount.GetHashCode();
                hash = (hash * 31) + Status;
                hash = (hash * 31) + HasOutcome.GetHashCode();
                hash = (hash * 31) + WinningNationId;
                hash = (hash * 31) + Path;
                hash = (hash * 31) + CompletionTick.GetHashCode();
                return hash;
            }
        }
    }
}
