using Unity.Netcode;
using EpochWar.Core.State;
using EpochWar.Core.Systems;

namespace EpochWar.Unity.Net
{
    /// <summary>
    /// The compact wire form of a single terrain modification for replication to clients (Req 6.5).
    ///
    /// <para><b>Why the effect, not a raw cell list?</b> A <see cref="TerrainModifiedEvent"/> carries
    /// both the <see cref="TerrainEffect"/> that produced it <em>and</em> the exact list of modified
    /// <see cref="CellCoord"/>s. The effect is a fixed six-int descriptor (center x/y/z, radius,
    /// depth, power) regardless of how many cells it carved, so it is the most compact possible
    /// cell-delta message — and because <see cref="TerrainVolume.ApplyEffect"/> is deterministic and
    /// lives in the engine-free Core, a client that starts from the identical seeded volume and
    /// replays the identical ordered sequence of effects reproduces the exact same modified cells the
    /// Host computed. The Host applies effects only inside its authoritative tick; clients apply them
    /// only from this replicated stream, so each effect is applied exactly once per machine and the
    /// two volumes stay bit-identical.</para>
    ///
    /// <para><see cref="ModifiedCellCount"/> is carried purely as a lightweight integrity check /
    /// diagnostic: after re-applying the effect a client can compare the count it produced against
    /// the Host's and log a divergence. It is not required to reconstruct the delta.</para>
    /// </summary>
    public struct NetTerrainDelta : INetworkSerializable
    {
        /// <summary>Center cell of the carved region.</summary>
        public NetCoord Center;

        /// <summary>Horizontal (X/Z) radius of the affected disc, in cells.</summary>
        public int Radius;

        /// <summary>How many cells downward the effect carves.</summary>
        public int Depth;

        /// <summary>Integrity removed from each affected cell.</summary>
        public int Power;

        /// <summary>Number of cells the Host actually changed (integrity check only).</summary>
        public int ModifiedCellCount;

        /// <summary>Builds the wire delta from a Host-side <see cref="TerrainModifiedEvent"/>.</summary>
        public static NetTerrainDelta Of(TerrainModifiedEvent modified)
        {
            TerrainEffect e = modified.Effect;
            return new NetTerrainDelta
            {
                Center = NetCoord.Of(e.Center),
                Radius = e.Radius,
                Depth = e.Depth,
                Power = e.Power,
                ModifiedCellCount = modified.ModifiedCount,
            };
        }

        /// <summary>Rebuilds the Core <see cref="TerrainEffect"/> so the client can replay it locally.</summary>
        public TerrainEffect ToEffect()
            => new TerrainEffect(Center.ToCellCoord(), Radius, Depth, Power);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Center);
            serializer.SerializeValue(ref Radius);
            serializer.SerializeValue(ref Depth);
            serializer.SerializeValue(ref Power);
            serializer.SerializeValue(ref ModifiedCellCount);
        }
    }

    /// <summary>
    /// A single tick's ordered batch of <see cref="NetTerrainDelta"/>s, replicated in one
    /// <c>ClientRpc</c> so all of a tick's terrain changes apply together and in the order the Host
    /// produced them. The array is length-prefixed and hand-serialized for the same reason as
    /// <see cref="NetGameEventBatch"/>.
    /// </summary>
    public struct NetTerrainDeltaBatch : INetworkSerializable
    {
        public NetTerrainDelta[] Deltas;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            int count = Deltas?.Length ?? 0;
            serializer.SerializeValue(ref count);

            if (serializer.IsReader)
            {
                Deltas = new NetTerrainDelta[count < 0 ? 0 : count];
            }

            for (int i = 0; i < Deltas.Length; i++)
            {
                serializer.SerializeValue(ref Deltas[i]);
            }
        }
    }
}
