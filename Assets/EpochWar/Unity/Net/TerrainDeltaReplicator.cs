using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Core.Systems;
using EpochWar.Unity.Bootstrap;

namespace EpochWar.Unity.Net
{
    /// <summary>
    /// Replicates authoritative terrain modifications from the Host to every client as compact
    /// cell-delta messages, keeping each client's <see cref="TerrainVolume"/> in sync (Req 6.5).
    ///
    /// <para><b>Host side.</b> Terrain is mutated only inside the Host's authoritative tick (the
    /// <see cref="TerrainSystem"/> applies queued <see cref="TerrainEffect"/>s). This replicator
    /// listens to the Host's <see cref="SimulationDriver.Ticked"/> stream, picks out each
    /// <see cref="TerrainModifiedEvent"/> that tick, and broadcasts the modifications as a batch of
    /// <see cref="NetTerrainDelta"/>s in one <c>ClientRpc</c>, preserving order.</para>
    ///
    /// <para><b>Client side.</b> A client's driver never ticks (it is bound read-only), so the client
    /// never mutates terrain on its own. When a delta batch arrives it replays each
    /// <see cref="TerrainEffect"/> against its local <see cref="TerrainVolume"/> via the same
    /// deterministic <see cref="TerrainVolume.ApplyEffect"/> the Host used. Because the client started
    /// from an identical seeded volume and applies the identical ordered effects, it reproduces
    /// exactly the cells the Host changed — so the two volumes stay bit-identical without shipping a
    /// per-cell payload. The count the Host actually changed rides along purely as an integrity check.
    /// </para>
    ///
    /// <para>Applying an effect returns the changed <see cref="CellCoord"/>s; the replicator raises
    /// <see cref="TerrainCellsChanged"/> with them so the presentation layer (e.g. the terrain mesher)
    /// can re-mesh exactly the affected chunks on the client — mirroring what the driver's tick does
    /// for the Host.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerrainDeltaReplicator : NetworkBehaviour
    {
        private MatchNetworkManager _match;
        private SimulationDriver _driver;
        private bool _subscribedToDriver;

        // Reused scratch so the per-tick collect allocates only the exact-size batch it sends.
        private readonly List<NetTerrainDelta> _deltaScratch = new List<NetTerrainDelta>();

        /// <summary>
        /// Raised on clients after a replicated batch is applied, with every cell changed this batch,
        /// so the terrain renderer can rebuild only the affected chunks (Req 6.5, presentation).
        /// </summary>
        public event Action<IReadOnlyList<CellCoord>> TerrainCellsChanged;

        /// <summary>
        /// Wires the replicator to the match manager and driver. Called by
        /// <see cref="MatchNetworkManager.OnNetworkSpawn"/>. On the Host this subscribes to the tick
        /// stream so terrain modifications are broadcast as they resolve.
        /// </summary>
        public void AttachTo(MatchNetworkManager match, SimulationDriver driver)
        {
            _match = match;
            _driver = driver;

            if (IsServer && _driver != null && !_subscribedToDriver)
            {
                _driver.Ticked += OnHostTicked;
                _subscribedToDriver = true;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (_subscribedToDriver && _driver != null)
            {
                _driver.Ticked -= OnHostTicked;
                _subscribedToDriver = false;
            }
        }

        /// <summary>
        /// Host-side per-tick hook: extracts this tick's terrain modifications and, if any, replicates
        /// them to all clients as one ordered delta batch (Req 6.5).
        /// </summary>
        private void OnHostTicked(IReadOnlyList<GameEvent> events)
        {
            if (events == null || events.Count == 0)
            {
                return;
            }

            _deltaScratch.Clear();
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i] is TerrainModifiedEvent modified && modified.ModifiedCount > 0)
                {
                    _deltaScratch.Add(NetTerrainDelta.Of(modified));
                }
            }

            if (_deltaScratch.Count == 0)
            {
                return;
            }

            var batch = new NetTerrainDeltaBatch { Deltas = _deltaScratch.ToArray() };
            ApplyTerrainDeltasClientRpc(batch);
        }

        /// <summary>
        /// Applies a replicated batch of terrain deltas on clients by deterministically replaying each
        /// effect against the local <see cref="TerrainVolume"/> (Req 6.5). The Host already applied
        /// these inside its authoritative tick, so it ignores its own broadcast to avoid
        /// double-carving the host-client's volume.
        /// </summary>
        [ClientRpc]
        private void ApplyTerrainDeltasClientRpc(NetTerrainDeltaBatch batch)
        {
            if (IsServer)
            {
                return; // Host already applied these authoritatively.
            }

            MatchState state = _driver != null ? _driver.State : null;
            if (state == null || state.Terrain == null || batch.Deltas == null)
            {
                return;
            }

            var changed = new List<CellCoord>();
            foreach (NetTerrainDelta delta in batch.Deltas)
            {
                // Replay the identical, deterministic effect the Host applied (Req 6.5). ApplyEffect
                // returns exactly the cells it altered, which should match the Host's ModifiedCellCount
                // when the two volumes are in sync.
                IReadOnlyList<CellCoord> modified = state.Terrain.ApplyEffect(delta.ToEffect());
                changed.AddRange(modified);

                if (modified.Count != delta.ModifiedCellCount)
                {
                    // A divergence here means the client's volume drifted from the Host's (e.g. a
                    // missed delta). Surface it loudly rather than letting terrain silently desync.
                    Debug.LogWarning(
                        $"[TerrainDeltaReplicator] Terrain delta mismatch: host changed "
                        + $"{delta.ModifiedCellCount} cells, client changed {modified.Count} "
                        + $"for effect at {delta.Center.ToCellCoord()}.");
                }
            }

            if (changed.Count > 0)
            {
                TerrainCellsChanged?.Invoke(changed);
            }
        }
    }
}
