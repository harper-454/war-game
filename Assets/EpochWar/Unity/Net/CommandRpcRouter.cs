using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using EpochWar.Core.Commands;
using EpochWar.Core.Simulation;
using EpochWar.Unity.Bootstrap;

namespace EpochWar.Unity.Net
{
    /// <summary>
    /// Carries client command <em>intents</em> to the authoritative Host and reflects the Host's
    /// resolved state back to clients (Req 8.2, 8.5).
    ///
    /// <para><b>Intents in (Req 8.2, 8.5).</b> A client turns a local <see cref="ICommand"/> into a
    /// <see cref="CommandEnvelope"/> and sends it with <see cref="SubmitCommandServerRpc"/>. On the
    /// Host the RPC verifies the sender actually controls the issuing Nation (anti-spoof via
    /// <see cref="MatchNetworkManager.ClientOwnsNation"/>), reconstructs the concrete command, and
    /// enqueues it on the shared <see cref="MatchBootstrapper"/> — the identical
    /// <see cref="EnqueueCommand"/> path an AI_Nation's command takes on the Host (Req 8.5,
    /// Property 31). The Host's own local input skips the wire and enqueues directly through that same
    /// path. AI runs only on the Host (its controllers live on the bootstrapper), so every mutation —
    /// human-remote, human-host, or AI — funnels through one authoritative router.</para>
    ///
    /// <para><b>State out (Req 8.2).</b> Resolved state is reflected two complementary ways:
    /// <list type="bullet">
    /// <item>a Host-owned <c>NetworkVariable&lt;NetMatchClock&gt;</c> that Netcode auto-replicates —
    /// the authoritative headline snapshot (tick, lifecycle status, end-of-match outcome); and</item>
    /// <item>a per-tick <c>ClientRpc</c> that broadcasts the drained <see cref="GameEvent"/>s of that
    /// tick (re-encoded as <see cref="NetGameEvent"/>s) so client presentation can mirror granular
    /// changes. Terrain modifications are excluded here — they are replicated by the
    /// <see cref="TerrainDeltaReplicator"/> (Req 6.5).</item>
    /// </list>
    /// Clients consume the event stream via <see cref="EventsReplicated"/> and the snapshot via
    /// <see cref="MatchClockChanged"/>.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CommandRpcRouter : NetworkBehaviour
    {
        // The authoritative lifecycle snapshot: written only by the Host, readable by everyone, and
        // auto-replicated by Netcode whenever it actually changes (IEquatable dirty-check).
        private readonly NetworkVariable<NetMatchClock> _clock = new NetworkVariable<NetMatchClock>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private MatchNetworkManager _match;
        private SimulationDriver _driver;
        private bool _subscribedToDriver;

        // Reused scratch so the per-tick encode allocates only the exact-size batch it sends.
        private readonly List<NetGameEvent> _encodeScratch = new List<NetGameEvent>();

        /// <summary>
        /// Raised on clients (not the Host) with each tick's ordered, replicated gameplay events so
        /// presentation can mirror the change. The Host already has the authoritative events locally
        /// via the driver, so it does not raise this.
        /// </summary>
        public event Action<IReadOnlyList<NetGameEvent>> EventsReplicated;

        /// <summary>Raised on every peer when the replicated <see cref="NetMatchClock"/> changes (Req 12.3).</summary>
        public event Action<NetMatchClock> MatchClockChanged;

        /// <summary>The latest replicated lifecycle snapshot.</summary>
        public NetMatchClock Clock => _clock.Value;

        /// <summary>
        /// Wires the router to the match manager and driver. Called by
        /// <see cref="MatchNetworkManager.OnNetworkSpawn"/> once both are spawned. On the Host this
        /// also subscribes to the driver's tick so resolved state is replicated each step.
        /// </summary>
        public void AttachTo(MatchNetworkManager match, SimulationDriver driver)
        {
            _match = match;
            _driver = driver;

            if (IsServer && _driver != null && !_subscribedToDriver)
            {
                _driver.Ticked += OnHostTicked;
                _subscribedToDriver = true;

                // Publish the initial snapshot so late-spawned clients start from a correct headline.
                PublishClock();
            }
        }

        public override void OnNetworkSpawn()
        {
            _clock.OnValueChanged += OnClockChanged;
        }

        public override void OnNetworkDespawn()
        {
            _clock.OnValueChanged -= OnClockChanged;

            if (_subscribedToDriver && _driver != null)
            {
                _driver.Ticked -= OnHostTicked;
                _subscribedToDriver = false;
            }
        }

        private void OnClockChanged(NetMatchClock previous, NetMatchClock current)
            => MatchClockChanged?.Invoke(current);

        // ---- Intents in ----

        /// <summary>
        /// Submits a locally produced <see cref="ICommand"/> to the authoritative pipeline (Req 8.5).
        /// On the Host the command is enqueued directly on the shared bootstrapper; on a client it is
        /// flattened to a <see cref="CommandEnvelope"/> and sent to the Host via
        /// <see cref="SubmitCommandServerRpc"/>. Either way it ends up on the one authoritative router.
        /// </summary>
        public void SubmitLocalCommand(ICommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            if (IsServer)
            {
                _match?.Match?.EnqueueCommand(command);
                return;
            }

            SubmitCommandServerRpc(CommandEnvelope.From(command));
        }

        /// <summary>
        /// Receives a client's command intent on the Host, verifies the sender controls the issuing
        /// Nation, reconstructs the concrete command, and enqueues it through the shared authoritative
        /// path (Req 8.2, 8.5). Ownership is not required by Netcode (any client may call it); the
        /// per-Nation ownership check below is the authoritative guard. A spoofed or unmapped command
        /// is silently dropped, leaving state untouched.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void SubmitCommandServerRpc(CommandEnvelope envelope, ServerRpcParams rpcParams = default)
        {
            if (_match == null)
            {
                return;
            }

            ulong sender = rpcParams.Receive.SenderClientId;
            if (!_match.ClientOwnsNation(sender, envelope.IssuingNationId))
            {
                return; // sender does not control this Nation — drop (Req 8.2 authority)
            }

            ICommand command = envelope.ToCommand();
            if (command == null)
            {
                return; // unknown command kind
            }

            // The single authoritative entry point shared with AI commands (Req 8.5).
            _match.Match?.EnqueueCommand(command);
        }

        // ---- State out ----

        /// <summary>
        /// Host-side per-tick hook: refreshes the replicated lifecycle snapshot and broadcasts the
        /// tick's gameplay events to clients (Req 8.2). Terrain events are skipped here — the
        /// <see cref="TerrainDeltaReplicator"/> owns their replication (Req 6.5).
        /// </summary>
        private void OnHostTicked(IReadOnlyList<GameEvent> events)
        {
            PublishClock();

            if (events == null || events.Count == 0)
            {
                return;
            }

            _encodeScratch.Clear();
            for (int i = 0; i < events.Count; i++)
            {
                if (NetGameEvent.TryEncode(events[i], out NetGameEvent net))
                {
                    _encodeScratch.Add(net);
                }
            }

            if (_encodeScratch.Count == 0)
            {
                return; // this tick produced only terrain / unmapped events
            }

            var batch = new NetGameEventBatch { Events = _encodeScratch.ToArray() };
            ApplyEventsClientRpc(batch);
        }

        /// <summary>Mirrors the authoritative Match clock/outcome into the replicated snapshot.</summary>
        private void PublishClock()
        {
            var state = _driver != null ? _driver.State : null;
            if (state == null)
            {
                return;
            }

            _clock.Value = state.Outcome != null
                ? NetMatchClock.Ended(state.TickCount, state.Outcome)
                : NetMatchClock.InProgress(state.TickCount);
        }

        /// <summary>
        /// Delivers a tick's ordered gameplay events to clients. The Host already applied them locally
        /// through the authoritative simulation, so it ignores its own broadcast; clients raise
        /// <see cref="EventsReplicated"/> for presentation to consume.
        /// </summary>
        [ClientRpc]
        private void ApplyEventsClientRpc(NetGameEventBatch batch)
        {
            if (IsServer)
            {
                return; // Host is authoritative; avoid double-applying on the host-client.
            }

            EventsReplicated?.Invoke(batch.Events ?? Array.Empty<NetGameEvent>());
        }
    }
}
