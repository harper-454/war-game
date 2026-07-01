using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Unity.Net;

namespace EpochWar.Tests.PlayMode
{
    /// <summary>
    /// PlayMode integration tests for host-authoritative networking (task 14.4). These verify the
    /// observable multiplayer behaviour of the real networking components over a live Netcode for
    /// GameObjects Host + client session — host authority (Req 8.3), client → Host command
    /// propagation with reflection back to clients (Req 8.2), and continuation of the Match after a
    /// client disconnects (Req 8.4). They are explicitly integration tests, not property tests.
    ///
    /// <para>Requires the Unity Editor and <c>com.unity.netcode.gameobjects</c> (with its
    /// <c>TestHelpers</c>) to compile and run; they cannot execute in the engine-free sandbox.</para>
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    [Category("PlayMode integration: networking")]
    public sealed class NetworkCommandAndAuthorityTests : NetworkedMatchTestBase
    {
        /// <summary>
        /// Host authority (Req 8.3): only the Host advances the simulation. The Host binds its driver
        /// with authority and ticks the authoritative Match forward, while the client's driver is
        /// bound read-only and never advances its local mirror — so the Host's tick count climbs and
        /// the client's stays at its initial value.
        /// </summary>
        [UnityTest]
        public IEnumerator HostAuthority_OnlyHostAdvancesSimulation()
        {
            yield return SpawnMatchObject();

            // Exactly one authoritative Host; the client is not.
            Assert.IsTrue(HostManager.IsAuthoritativeHost, "The server instance must be the authoritative Host.");
            Assert.IsFalse(ClientManager.IsAuthoritativeHost, "The client instance must not be authoritative.");

            // The driver bindings encode the authority split (Req 8.3).
            Assert.IsTrue(HostDriver.HasAuthority, "Host driver must be bound with authority.");
            Assert.IsFalse(ClientDriver.HasAuthority, "Client driver must be bound read-only.");
            Assert.IsTrue(HostDriver.IsTicking, "Host driver must be advancing the simulation.");
            Assert.IsFalse(ClientDriver.IsTicking, "Client driver must never advance the simulation.");

            long clientStartTick = ClientManager.Match.State.TickCount;

            // Let real frames elapse so the Host's fixed-tick loop runs several steps.
            yield return WaitForConditionOrTimeOut(() => HostManager.Match.State.TickCount >= clientStartTick + 3);
            AssertOnTimeout("Timed out waiting for the Host to advance the authoritative simulation.");

            // The client mirror never ticked itself: its tick count is unchanged by local advancement.
            Assert.AreEqual(
                clientStartTick,
                ClientManager.Match.State.TickCount,
                "The client must not advance the simulation locally (host authority, Req 8.3).");
        }

        /// <summary>
        /// Command propagation (Req 8.2): a client's command intent is sent to the Host as a
        /// <c>ServerRpc</c> (<see cref="CommandRpcRouter.SubmitLocalCommand"/> →
        /// <see cref="CommandRpcRouter.SubmitCommandServerRpc"/>), applied on the authoritative Host,
        /// and its resolved effects are reflected back to clients as replicated gameplay events.
        ///
        /// The client (which owns <see cref="NetworkedMatchTestBase.ClientNationId"/>) submits a
        /// <see cref="StartResearchCommand"/>. On the Host the research cost is deducted from that
        /// Nation's store; the client receives the corresponding replicated
        /// <see cref="NetGameEventKind.ResourceChanged"/> event for its Nation.
        /// </summary>
        [UnityTest]
        public IEnumerator ClientCommand_IsAppliedOnHost_AndReflectedToClients()
        {
            yield return SpawnMatchObject();

            // Capture events replicated to the client so we can assert the change came back (Req 8.2).
            var replicatedForClientNation = new List<NetGameEvent>();
            ClientRouter.EventsReplicated += events =>
            {
                foreach (NetGameEvent e in events)
                {
                    if (e.NationId == ClientNationId)
                    {
                        replicatedForClientNation.Add(e);
                    }
                }
            };

            float researchBefore = ResearchAmount(HostManager.Match.State, ClientNationId);
            Assert.Greater(researchBefore, 0f, "Client Nation must start with Research to spend.");

            // The client submits a command for the Nation it controls; this rides a ServerRpc to the Host.
            ClientRouter.SubmitLocalCommand(new StartResearchCommand(ClientNationId, ResearchableTechId));

            // The Host applies it on its next authoritative tick: the Research store is deducted.
            yield return WaitForConditionOrTimeOut(() =>
                ResearchAmount(HostManager.Match.State, ClientNationId) < researchBefore);
            AssertOnTimeout("Timed out waiting for the Host to apply the client's research command (Req 8.2).");

            // The resolved change is reflected back to the client as a replicated event (Req 8.2).
            yield return WaitForConditionOrTimeOut(() => HasResourceChanged(replicatedForClientNation));
            AssertOnTimeout("Timed out waiting for the client to receive the reflected resource change (Req 8.2).");

            Assert.Less(
                ResearchAmount(HostManager.Match.State, ClientNationId),
                researchBefore,
                "The client's research command must be applied authoritatively on the Host.");
        }

        /// <summary>
        /// Host authority for command ownership (Req 8.2): a client may not issue commands for a
        /// Nation it does not control. The client submits a command for the <em>Host's</em> Nation;
        /// the Host's ownership guard drops it and that Nation's state is untouched.
        /// </summary>
        [UnityTest]
        public IEnumerator ClientCommand_ForForeignNation_IsRejectedByHostAuthority()
        {
            yield return SpawnMatchObject();

            float hostNationResearchBefore = ResearchAmount(HostManager.Match.State, HostNationId);

            // The client spoofs a command for the Host's Nation — the server-side ownership check drops it.
            ClientRouter.SubmitLocalCommand(new StartResearchCommand(HostNationId, ResearchableTechId));

            // Give the Host several ticks; a legitimately applied command would have deducted Research.
            long start = HostManager.Match.State.TickCount;
            yield return WaitForConditionOrTimeOut(() => HostManager.Match.State.TickCount >= start + 5);
            AssertOnTimeout("Timed out waiting for the Host to tick.");

            Assert.AreEqual(
                hostNationResearchBefore,
                ResearchAmount(HostManager.Match.State, HostNationId),
                "A client command for a Nation it does not control must be dropped (Req 8.2).");
        }

        /// <summary>
        /// Disconnect continuation (Req 8.4): when the client drops, the Host marks its Nation
        /// disconnected, notifies the remaining peers via
        /// <see cref="MatchNetworkManager.NationConnectionChanged"/>, and keeps ticking — the Match
        /// continues for the still-connected Nations rather than stalling on the absent client.
        /// </summary>
        [UnityTest]
        public IEnumerator ClientDisconnect_NotifiesRemainingPeers_AndMatchContinues()
        {
            yield return SpawnMatchObject();

            int notifiedNationId = 0;
            int connectedHumanNations = -1;
            HostManager.NationConnectionChanged += (nationId, connected) =>
            {
                notifiedNationId = nationId;
                connectedHumanNations = connected;
            };

            // Make sure the Host is actively ticking before the disconnect.
            long tickBeforeDisconnect = HostManager.Match.State.TickCount;
            yield return WaitForConditionOrTimeOut(() => HostManager.Match.State.TickCount > tickBeforeDisconnect);
            AssertOnTimeout("Timed out waiting for the Host to begin ticking before disconnect.");

            // Drop the single remote client.
            NetcodeIntegrationTestHelpers.StopOneClient(m_ClientNetworkManagers[0]);

            // The Host notifies remaining peers that the client's Nation lost its connection (Req 8.4).
            yield return WaitForConditionOrTimeOut(() => notifiedNationId == ClientNationId);
            AssertOnTimeout("Timed out waiting for the disconnect notification to reach remaining peers (Req 8.4).");

            Assert.AreEqual(
                ClientNationId,
                notifiedNationId,
                "The disconnected client's Nation must be reported to remaining peers (Req 8.4).");
            Assert.AreEqual(
                1,
                connectedHumanNations,
                "Exactly one human Nation (the Host's) remains connected after the client drops (Req 8.4).");

            // The Match continues: the Host keeps advancing the authoritative simulation afterwards.
            long tickAfterNotify = HostManager.Match.State.TickCount;
            yield return WaitForConditionOrTimeOut(() => HostManager.Match.State.TickCount > tickAfterNotify + 2);
            AssertOnTimeout("Timed out waiting for the Match to continue after the client disconnected (Req 8.4).");

            Assert.Greater(
                HostManager.Match.State.TickCount,
                tickAfterNotify,
                "The Match must continue for connected Nations after a disconnect (Req 8.4).");
        }

        // ---- helpers ----

        private static float ResearchAmount(MatchState state, int nationId)
        {
            Nation nation = state.Nations[nationId];
            return nation.Resources.TryGetValue(ResourceType.Research, out ResourceStore store)
                ? store.Amount
                : 0f;
        }

        private static bool HasResourceChanged(IReadOnlyList<NetGameEvent> events)
        {
            foreach (NetGameEvent e in events)
            {
                if (e.Kind == NetGameEventKind.ResourceChanged)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
