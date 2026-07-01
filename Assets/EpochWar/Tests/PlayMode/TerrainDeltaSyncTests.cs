using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using EpochWar.Core.State;
using EpochWar.Core.Systems;

namespace EpochWar.Tests.PlayMode
{
    /// <summary>
    /// PlayMode integration test for terrain delta replication (task 14.4, Req 6.5). It verifies that
    /// a terrain modification produced only inside the authoritative Host tick is replicated to the
    /// client as a compact effect delta and, once the client replays it against its local
    /// <see cref="TerrainVolume"/>, both peers' volumes are cell-for-cell identical.
    ///
    /// <para>This exercises the real <see cref="TerrainDeltaReplicator"/> over a live Netcode for
    /// GameObjects Host + client session: the Host queues a <see cref="TerrainEffect"/> on its
    /// authoritative <see cref="TerrainSystem"/>; its next tick applies the effect, emits a
    /// <see cref="TerrainModifiedEvent"/>, and the replicator broadcasts the delta; the client — whose
    /// driver never ticks — mutates its terrain only by deterministically re-applying the replicated
    /// effect. It is an integration test, not a property test.</para>
    ///
    /// <para>Requires the Unity Editor and <c>com.unity.netcode.gameobjects</c> (with its
    /// <c>TestHelpers</c>) to compile and run; it cannot execute in the engine-free sandbox.</para>
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    [Category("PlayMode integration: terrain sync")]
    public sealed class TerrainDeltaSyncTests : NetworkedMatchTestBase
    {
        /// <summary>
        /// Terrain delta sync (Req 6.5): a host-side terrain modification replicates to the client and
        /// the client's <see cref="TerrainVolume"/> matches the Host's after applying the delta.
        /// </summary>
        [UnityTest]
        public IEnumerator HostTerrainModification_ReplicatesToClient_AndVolumesMatch()
        {
            yield return SpawnMatchObject();

            TerrainVolume hostTerrain = HostManager.Match.State.Terrain;
            TerrainVolume clientTerrain = ClientManager.Match.State.Terrain;

            // Sanity: both peers started from the identical seeded volume (Req 6.5 precondition).
            Assert.IsTrue(
                TerrainVolumesMatch(hostTerrain, clientTerrain),
                "Host and client must start from an identical seeded terrain volume.");

            // Observe the client applying replicated cell deltas so we know when replication landed.
            var clientChangedCells = new List<CellCoord>();
            ClientReplicator.TerrainCellsChanged += cells => clientChangedCells.AddRange(cells);

            // A crater at the surface centre. Power 4 exceeds soil integrity (2), so the cell is emptied.
            var center = new CellCoord(4, SurfaceY, 4);
            var effect = new TerrainEffect(center, radius: 1, depth: 1, power: 4);

            Assert.IsTrue(hostTerrain.IsSolid(center), "The target cell must be solid before the effect.");

            // Terrain is only ever mutated inside the Host's authoritative tick (Req 6.5). The Host
            // driver auto-advances, so the queued effect is applied on the next fixed step.
            HostManager.Match.Simulation.TerrainSystem.QueueEffect(effect);

            // The Host carves the cell authoritatively.
            yield return WaitForConditionOrTimeOut(() => !hostTerrain.IsSolid(center));
            AssertOnTimeout("Timed out waiting for the Host to apply the terrain effect authoritatively (Req 6.5).");

            // The client replays the replicated delta and its own volume converges to the Host's.
            yield return WaitForConditionOrTimeOut(() => !clientTerrain.IsSolid(center));
            AssertOnTimeout("Timed out waiting for the terrain delta to replicate to the client (Req 6.5).");

            Assert.IsTrue(
                clientChangedCells.Contains(center),
                "The client must report the carved cell among its replicated terrain changes (Req 6.5).");

            Assert.IsTrue(
                TerrainVolumesMatch(hostTerrain, clientTerrain),
                "After applying the replicated delta the client's terrain volume must match the Host's (Req 6.5).");
        }
    }
}
