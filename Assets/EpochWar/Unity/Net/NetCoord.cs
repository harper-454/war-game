using Unity.Netcode;
using EpochWar.Core.State;

namespace EpochWar.Unity.Net
{
    /// <summary>
    /// The wire form of a <see cref="CellCoord"/> — three ints packed into an
    /// <see cref="INetworkSerializable"/> value so terrain-cell addresses and command targets can be
    /// carried inside RPC payloads.
    ///
    /// <see cref="CellCoord"/> lives in the engine-free <c>EpochWar.Core</c> assembly and knows
    /// nothing about Netcode, so it cannot itself implement <see cref="INetworkSerializable"/>. This
    /// tiny mirror bridges the boundary: the presentation/networking layer converts to/from
    /// <see cref="CellCoord"/> with <see cref="Of"/> and <see cref="ToCellCoord"/> without touching
    /// the Core type.
    /// </summary>
    public struct NetCoord : INetworkSerializable
    {
        public int X;
        public int Y;
        public int Z;

        public NetCoord(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>Builds the wire form from a Core <see cref="CellCoord"/>.</summary>
        public static NetCoord Of(CellCoord c) => new NetCoord(c.X, c.Y, c.Z);

        /// <summary>Reconstructs the Core <see cref="CellCoord"/> from the wire form.</summary>
        public CellCoord ToCellCoord() => new CellCoord(X, Y, Z);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref X);
            serializer.SerializeValue(ref Y);
            serializer.SerializeValue(ref Z);
        }
    }
}
