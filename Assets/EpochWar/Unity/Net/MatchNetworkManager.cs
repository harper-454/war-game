using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using EpochWar.Core.Simulation;
using EpochWar.Core.State;
using EpochWar.Unity.Bootstrap;

namespace EpochWar.Unity.Net
{
    /// <summary>
    /// A replicated, unmanaged record of which connected Game_Client controls which Nation, and
    /// whether that client is currently connected (Req 8.1, 8.4). Held in a <c>NetworkList</c> on the
    /// <see cref="MatchNetworkManager"/> so every client — including late joiners — sees the full
    /// client↔Nation mapping the Host assigned.
    /// </summary>
    public struct ClientNationAssignment : INetworkSerializable, IEquatable<ClientNationAssignment>
    {
        public ulong ClientId;
        public int NationId;
        public bool Connected;

        public ClientNationAssignment(ulong clientId, int nationId, bool connected)
        {
            ClientId = clientId;
            NationId = nationId;
            Connected = connected;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref NationId);
            serializer.SerializeValue(ref Connected);
        }

        public bool Equals(ClientNationAssignment other)
            => ClientId == other.ClientId && NationId == other.NationId && Connected == other.Connected;

        public override bool Equals(object obj) => obj is ClientNationAssignment other && Equals(other);

        public override int GetHashCode() => (ClientId, NationId, Connected).GetHashCode();
    }

    /// <summary>
    /// Owns host election, the Match assembly, and the connection lifecycle for a networked Match
    /// (Req 8.1, 8.3, 8.4).
    ///
    /// <para><b>Host authority (Req 8.3).</b> Netcode for GameObjects elects exactly one authoritative
    /// server; in this game that server is a <em>Host</em> (server + local client) started with
    /// <see cref="StartHost"/>. Only the Host builds and advances the authoritative
    /// <see cref="MatchBootstrapper"/>/<see cref="MatchSimulation"/>: it binds the
    /// <see cref="SimulationDriver"/> with <c>hasAuthority: true</c> so the fixed-tick loop runs on it
    /// alone. Every client also builds a local mirror of the same seeded Match (so its presentation
    /// has a <see cref="MatchState"/> to render) but binds the driver with <c>hasAuthority: false</c>,
    /// so a client never advances the simulation itself — it only applies replicated changes
    /// (terrain deltas and the gameplay-event stream).</para>
    ///
    /// <para><b>Match configurations (Req 8.1).</b> The <see cref="Mode"/> selects a 2-human
    /// competitive Match or a human(s)+AI co-op Match. The seeded Nations that are not AI are the
    /// "human-assignable" Nations; the Host assigns them to connecting clients in join order (Host
    /// first). AI_Nations are seeded and driven only on the Host, through the same authoritative
    /// command path as human intents (Req 8.5) — their controllers are registered on the Host's
    /// bootstrapper by the <see cref="MatchFactory"/>.</para>
    ///
    /// <para><b>Disconnection (Req 8.4).</b> When a client drops, the Host marks its Nation
    /// disconnected in the replicated assignment list, broadcasts a notification to the remaining
    /// clients, optionally hands the Nation to AI, and simply keeps ticking — the simulation never
    /// blocks on an absent client, so the Match continues for the connected Nations.</para>
    ///
    /// <para>The manager is a <see cref="NetworkBehaviour"/> placed on a scene <c>NetworkObject</c>.
    /// It holds no gameplay rules; assembly of the seeded Match (terrain, Nation seeds, AI
    /// controllers, catalog) is delegated to <see cref="MatchFactory"/>, which the lobby/scene wiring
    /// supplies.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchNetworkManager : NetworkBehaviour
    {
        [SerializeField]
        [Tooltip("The driver the Host ticks with authority and clients bind read-only.")]
        private SimulationDriver _driver;

        [SerializeField]
        [Tooltip("Routes client command intents to the Host and replicates resolved gameplay events.")]
        private CommandRpcRouter _commandRouter;

        [SerializeField]
        [Tooltip("Replicates authoritative terrain modifications to clients as compact effect deltas.")]
        private TerrainDeltaReplicator _terrainReplicator;

        [SerializeField]
        [Tooltip("Match configuration: 2-human competitive or human(s) + AI co-op (Req 8.1).")]
        private NetworkMatchMode _mode = NetworkMatchMode.CompetitiveTwoHuman;

        [SerializeField]
        [Tooltip("When a client disconnects, flip its Nation to AI so the Host may drive it (Req 8.4).")]
        private bool _aiTakeoverOnDisconnect = false;

        /// <summary>Nation id used for a connected client that could not be assigned a Nation (spectator).</summary>
        public const int SpectatorNationId = -1;

        // The replicated client↔Nation mapping (authoritative on the Host, read by every client).
        private NetworkList<ClientNationAssignment> _assignments;

        // Host-only fast lookup + the queue of still-unassigned human Nation ids (join-order assignment).
        private readonly Dictionary<ulong, int> _clientToNation = new Dictionary<ulong, int>();
        private readonly Queue<int> _unassignedHumanNations = new Queue<int>();

        // The locally assembled Match: authoritative on the Host, a read-only mirror on clients.
        private MatchBootstrapper _localMatch;

        /// <summary>
        /// Assembles a seeded, ready-to-run Match for the given <see cref="NetworkMatchMode"/>,
        /// registering any AI controllers on the returned bootstrapper. Supplied by the lobby/scene
        /// wiring. Invoked on the Host to build the authoritative Match and on each client to build a
        /// local render mirror; both must seed identical terrain and Nations so deterministic terrain
        /// replay (Req 6.5) and the event stream stay consistent.
        /// </summary>
        public Func<NetworkMatchMode, MatchBootstrapper> MatchFactory { get; set; }

        /// <summary>Raised (on every peer) when the local client's Nation id becomes known.</summary>
        public event Action<int> LocalNationAssigned;

        /// <summary>
        /// Raised on the remaining clients when a Nation's connection state changes: the affected
        /// Nation id and the number of still-connected human Nations (Req 8.4).
        /// </summary>
        public event Action<int, int> NationConnectionChanged;

        /// <summary>The configured Match mode (Req 8.1).</summary>
        public NetworkMatchMode Mode => _mode;

        /// <summary>The assembled Match — authoritative on the Host, a mirror on clients.</summary>
        public MatchBootstrapper Match => _localMatch;

        /// <summary>True on the single authoritative Host (Req 8.3).</summary>
        public bool IsAuthoritativeHost => IsServer;

        /// <summary>The Nation id this peer's local client controls, or <see cref="SpectatorNationId"/> if none yet.</summary>
        public int LocalNationId
        {
            get
            {
                if (_assignments == null)
                {
                    return SpectatorNationId;
                }

                ulong local = NetworkManager != null ? NetworkManager.LocalClientId : 0UL;
                for (int i = 0; i < _assignments.Count; i++)
                {
                    if (_assignments[i].ClientId == local)
                    {
                        return _assignments[i].NationId;
                    }
                }

                return SpectatorNationId;
            }
        }

        private void Awake()
        {
            // NetworkList must be constructed before the object spawns (Awake or field initializer).
            _assignments = new NetworkList<ClientNationAssignment>();
        }

        // ---- Host lifecycle entry points (thin wrappers over the NGO transport) ----

        /// <summary>Starts this peer as the authoritative Host (server + local client) (Req 8.3).</summary>
        public bool StartHost() => NetworkManager.Singleton.StartHost();

        /// <summary>Starts this peer as a client connecting to the Host.</summary>
        public bool StartClient() => NetworkManager.Singleton.StartClient();

        /// <summary>Starts this peer as a dedicated (headless) server.</summary>
        public bool StartServer() => NetworkManager.Singleton.StartServer();

        public override void OnNetworkSpawn()
        {
            // Build the local Match. On the Host this is the authoritative simulation; on a client it
            // is a read-only mirror the presentation renders while replicated deltas/events drive it.
            _localMatch = MatchFactory?.Invoke(_mode);

            if (_driver != null && _localMatch != null)
            {
                // Only the Host advances the simulation (Req 8.3); clients bind read-only.
                _driver.Bind(_localMatch, hasAuthority: IsServer);
            }

            // Let the router/replicator wire themselves to this manager's driver and match.
            if (_commandRouter != null)
            {
                _commandRouter.AttachTo(this, _driver);
            }

            if (_terrainReplicator != null)
            {
                _terrainReplicator.AttachTo(this, _driver);
            }

            _assignments.OnListChanged += OnAssignmentsChanged;

            if (IsServer)
            {
                InitializeHostAssignments();

                NetworkManager.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }

            // Surface the initial local assignment (the Host already knows its own).
            int local = LocalNationId;
            if (local != SpectatorNationId)
            {
                LocalNationAssigned?.Invoke(local);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (_assignments != null)
            {
                _assignments.OnListChanged -= OnAssignmentsChanged;
            }

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }
        }

        // ---- Host-side Nation assignment (Req 8.1) ----

        /// <summary>
        /// Seeds the queue of human-assignable Nations from the authoritative Match (every Nation that
        /// is not an AI_Nation, in ascending id order) and assigns each already-connected client a
        /// Nation in ascending client-id order (the Host, usually client 0, first).
        /// </summary>
        private void InitializeHostAssignments()
        {
            if (_localMatch != null)
            {
                var humanNationIds = new List<int>();
                foreach (KeyValuePair<int, Nation> entry in _localMatch.State.Nations)
                {
                    if (!entry.Value.IsAI)
                    {
                        humanNationIds.Add(entry.Key);
                    }
                }

                humanNationIds.Sort();
                foreach (int id in humanNationIds)
                {
                    _unassignedHumanNations.Enqueue(id);
                }
            }

            var connected = new List<ulong>(NetworkManager.ConnectedClientsIds);
            connected.Sort();
            foreach (ulong clientId in connected)
            {
                AssignNation(clientId);
            }
        }

        /// <summary>
        /// Assigns the next free human Nation to <paramref name="clientId"/> (or a spectator slot when
        /// none remain) and records it in the replicated assignment list. Idempotent per client.
        /// </summary>
        private void AssignNation(ulong clientId)
        {
            if (_clientToNation.ContainsKey(clientId))
            {
                return; // already assigned
            }

            int nationId = _unassignedHumanNations.Count > 0
                ? _unassignedHumanNations.Dequeue()
                : SpectatorNationId;

            _clientToNation[clientId] = nationId;
            _assignments.Add(new ClientNationAssignment(clientId, nationId, connected: true));
        }

        private void OnClientConnected(ulong clientId)
        {
            if (!IsServer)
            {
                return;
            }

            AssignNation(clientId);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer)
            {
                return;
            }

            if (!_clientToNation.TryGetValue(clientId, out int nationId))
            {
                return;
            }

            // Mark the Nation disconnected in the replicated list; keep its id so its forces persist
            // and the Match continues for the connected Nations (Req 8.4).
            for (int i = 0; i < _assignments.Count; i++)
            {
                if (_assignments[i].ClientId == clientId)
                {
                    _assignments[i] = new ClientNationAssignment(clientId, nationId, connected: false);
                    break;
                }
            }

            // Optional AI takeover so the abandoned Nation can still act through the Host's
            // authoritative path; otherwise it simply becomes inert (Req 8.4). Flipping IsAI is a
            // plain data change on the Nation record and does not touch the engine-free Core logic.
            if (_aiTakeoverOnDisconnect
                && nationId != SpectatorNationId
                && _localMatch != null
                && _localMatch.State.Nations.TryGetValue(nationId, out Nation nation))
            {
                nation.IsAI = true;
            }

            // Notify the remaining clients (Req 8.4). The simulation is never stopped here.
            int connectedHumanNations = CountConnectedHumanNations();
            NotifyNationDisconnectedClientRpc(nationId, connectedHumanNations);
        }

        private int CountConnectedHumanNations()
        {
            int count = 0;
            for (int i = 0; i < _assignments.Count; i++)
            {
                if (_assignments[i].Connected && _assignments[i].NationId != SpectatorNationId)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Tells every client that a Nation lost its connection and the Match continues (Req 8.4).
        /// Raises <see cref="NationConnectionChanged"/> so the HUD can flag the absent Nation.
        /// </summary>
        [ClientRpc]
        private void NotifyNationDisconnectedClientRpc(int nationId, int connectedHumanNations)
        {
            NationConnectionChanged?.Invoke(nationId, connectedHumanNations);
        }

        private void OnAssignmentsChanged(NetworkListEvent<ClientNationAssignment> change)
        {
            // When our own assignment appears (or changes) surface the local Nation id to listeners.
            ulong local = NetworkManager != null ? NetworkManager.LocalClientId : 0UL;
            if (change.Value.ClientId == local && change.Value.NationId != SpectatorNationId)
            {
                LocalNationAssigned?.Invoke(change.Value.NationId);
            }
        }

        // ---- Server-side ownership check (anti-spoof for command intents, Req 8.2) ----

        /// <summary>
        /// True when <paramref name="clientId"/> is the connected controller of
        /// <paramref name="nationId"/>. The <see cref="CommandRpcRouter"/> calls this before applying
        /// a client's command so a client cannot issue commands for a Nation it does not control.
        /// </summary>
        public bool ClientOwnsNation(ulong clientId, int nationId)
            => _clientToNation.TryGetValue(clientId, out int owned)
               && owned == nationId
               && nationId != SpectatorNationId;
    }
}
