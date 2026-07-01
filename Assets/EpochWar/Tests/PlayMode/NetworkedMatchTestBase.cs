using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using EpochWar.Core.Simulation;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Core.Systems;
using EpochWar.Unity.Bootstrap;
using EpochWar.Unity.Net;

namespace EpochWar.Tests.PlayMode
{
    /// <summary>
    /// Shared host + client harness for the networking / terrain-sync PlayMode integration tests
    /// (task 14.4, Req 8.1, 8.2, 8.3, 8.4, 6.5).
    ///
    /// <para>These are <b>not</b> property tests. They stand up a real Netcode for GameObjects
    /// multi-instance session — a Host (server + local client) plus one connected client — using the
    /// package's <see cref="NetcodeIntegrationTest"/> tooling, spawn the game's real networking
    /// components (<see cref="MatchNetworkManager"/>, <see cref="CommandRpcRouter"/>,
    /// <see cref="TerrainDeltaReplicator"/>) plus the <see cref="SimulationDriver"/> on a replicated
    /// <see cref="NetworkObject"/>, and assert the observable networked behaviour end to end.</para>
    ///
    /// <para><b>How the game object is wired.</b> In production the <see cref="MatchNetworkManager"/>
    /// lives on a scene <c>NetworkObject</c> whose serialized references (driver, command router,
    /// terrain replicator) and <c>MatchFactory</c> are assigned by the match-scene wiring before the
    /// object spawns. The tests reproduce that with a tiny <see cref="TestMatchWiring"/> component on
    /// the same prefab: its <c>Awake</c> (which Netcode runs on every instance — Host and client —
    /// before <see cref="NetworkBehaviour.OnNetworkSpawn"/>) assigns the sibling components into the
    /// manager's serialized fields and installs an identical, deterministic <c>MatchFactory</c>. Both
    /// the Host's authoritative Match and each client's read-only mirror are therefore seeded from the
    /// exact same terrain and Nations, which is what lets the deterministic terrain replay (Req 6.5)
    /// and the gameplay-event stream stay consistent across peers.</para>
    ///
    /// <para>Because the sandbox has no Unity Editor and no <c>com.unity.netcode.gameobjects</c>
    /// package, this file cannot be compiled or executed here; it is authored to compile and run
    /// under the Unity Editor with Netcode for GameObjects (and its TestHelpers) installed.</para>
    /// </summary>
    public abstract class NetworkedMatchTestBase : NetcodeIntegrationTest
    {
        // One Host + one remote client is the minimum that exercises host authority, cross-peer
        // command propagation, a disconnect, and terrain replication (Req 8.1 two-human competitive).
        protected override int NumberOfClients => 1;

        // ---- Deterministic seed constants shared by every test's Match ----

        /// <summary>Nation assigned to the Host (join order: Host is client 0 → lowest human nation).</summary>
        protected const int HostNationId = 1;

        /// <summary>Nation assigned to the single remote client.</summary>
        protected const int ClientNationId = 2;

        /// <summary>A Prehistoric tech every seeded Nation can afford to research.</summary>
        protected const string ResearchableTechId = "toolmaking";

        // An 8x4x8 solid soil volume; the top solid layer is y = 3.
        protected const int SurfaceY = 3;

        private GameObject _prefab;

        // Populated by <see cref="SpawnMatchObject"/> once the replicated object exists on both peers.
        protected MatchNetworkManager HostManager { get; private set; }
        protected MatchNetworkManager ClientManager { get; private set; }
        protected SimulationDriver HostDriver { get; private set; }
        protected SimulationDriver ClientDriver { get; private set; }
        protected CommandRpcRouter HostRouter { get; private set; }
        protected CommandRpcRouter ClientRouter { get; private set; }
        protected TerrainDeltaReplicator HostReplicator { get; private set; }
        protected TerrainDeltaReplicator ClientReplicator { get; private set; }

        protected override void OnServerAndClientsCreated()
        {
            // A network prefab registered on the server and every client (helper from the base class).
            _prefab = CreateNetworkObjectPrefab("EpochWarMatchNet");
            _prefab.AddComponent<SimulationDriver>();
            _prefab.AddComponent<CommandRpcRouter>();
            _prefab.AddComponent<TerrainDeltaReplicator>();
            _prefab.AddComponent<MatchNetworkManager>();

            // Wires the manager's serialized references + MatchFactory before OnNetworkSpawn on both
            // the Host and the client, mirroring the real match-scene wiring.
            _prefab.AddComponent<TestMatchWiring>();

            base.OnServerAndClientsCreated();
        }

        /// <summary>
        /// Spawns the networking object on the Host and waits until it has replicated to the client,
        /// then resolves the per-peer component references used by the tests.
        /// </summary>
        protected IEnumerator SpawnMatchObject()
        {
            GameObject serverInstance = SpawnObject(_prefab, m_ServerNetworkManager);
            var serverObject = serverInstance.GetComponent<NetworkObject>();
            ulong objectId = serverObject.NetworkObjectId;

            NetworkManager client = m_ClientNetworkManagers[0];

            // Wait for the object to exist on the client and for both peers to have assigned the
            // local Nation (the Host assigns nations as clients connect).
            yield return WaitForConditionOrTimeOut(() =>
                client.SpawnManager.SpawnedObjects.ContainsKey(objectId)
                && serverObject.GetComponent<MatchNetworkManager>().Match != null);
            AssertOnTimeout("Timed out waiting for the match object to replicate to the client.");

            HostManager = serverObject.GetComponent<MatchNetworkManager>();
            HostDriver = serverObject.GetComponent<SimulationDriver>();
            HostRouter = serverObject.GetComponent<CommandRpcRouter>();
            HostReplicator = serverObject.GetComponent<TerrainDeltaReplicator>();

            NetworkObject clientObject = client.SpawnManager.SpawnedObjects[objectId];
            ClientManager = clientObject.GetComponent<MatchNetworkManager>();
            ClientDriver = clientObject.GetComponent<SimulationDriver>();
            ClientRouter = clientObject.GetComponent<CommandRpcRouter>();
            ClientReplicator = clientObject.GetComponent<TerrainDeltaReplicator>();

            // Both mirrors must be built before the tests inspect them.
            yield return WaitForConditionOrTimeOut(() =>
                HostManager.Match != null && ClientManager.Match != null);
            AssertOnTimeout("Timed out waiting for the client's local Match mirror to be built.");
        }

        // ------------------------------------------------------------------
        // Deterministic Match assembly shared by the Host and the client mirror.
        // Both peers MUST produce an identical seeded Match for terrain replay (Req 6.5) and the
        // event stream to stay consistent.
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds a fully seeded two-human competitive Match: an 8x4x8 soil volume and two human
        /// Nations, each with research/food resources and a starting soldier so neither is
        /// immediately eliminated (which would end the Match by Annihilation before the test runs).
        /// </summary>
        public static MatchBootstrapper BuildBootstrapper(NetworkMatchMode mode)
        {
            ICatalog catalog = BuildCatalog();
            var terrain = new TerrainVolume(new Int3(8, 4, 8), CellMaterial.Soil);

            var seeds = new[]
            {
                new NationSeed(
                    nationId: HostNationId,
                    isAI: false,
                    startingResources: BuildResources(),
                    startingUnits: new[] { new UnitSeed(catalog.GetUnit("soldier"), new CellCoord(2, SurfaceY + 1, 2)) },
                    startingPopulation: 10,
                    startingPopulationCapacity: 20),
                new NationSeed(
                    nationId: ClientNationId,
                    isAI: false,
                    startingResources: BuildResources(),
                    startingUnits: new[] { new UnitSeed(catalog.GetUnit("soldier"), new CellCoord(5, SurfaceY + 1, 5)) },
                    startingPopulation: 10,
                    startingPopulationCapacity: 20),
            };

            return MatchBootstrapper.Create(catalog, terrain, seeds);
        }

        private static IReadOnlyDictionary<ResourceType, ResourceStore> BuildResources()
            => new Dictionary<ResourceType, ResourceStore>
            {
                [ResourceType.Research] = new ResourceStore(100f, 0f),
                [ResourceType.Food] = new ResourceStore(100f, 0f),
            };

        private static ICatalog BuildCatalog()
        {
            var techs = new[]
            {
                new TechnologyDef(ResearchableTechId, Era.Prehistoric, ResourceCost.Single(ResourceType.Research, 10f)),
            };

            var units = new[]
            {
                new UnitDef(
                    id: "soldier",
                    era: Era.Prehistoric,
                    cost: ResourceCost.Single(ResourceType.Food, 5f),
                    buildTimeSeconds: 2f,
                    populationCost: 1,
                    maxHealth: 30,
                    attack: 5,
                    defense: 2,
                    moveSpeed: 1f,
                    role: UnitRole.Soldier),
            };

            var structures = new[]
            {
                new StructureDef(
                    "barracks", Era.Prehistoric, ResourceCost.Free,
                    buildTimeSeconds: 0f, populationCost: 0, maxHealth: 100,
                    footprintWidth: 1, footprintLength: 1, function: StructureFunction.Barracks),
            };

            return new InMemoryCatalog(technologies: techs, units: units, structures: structures);
        }

        /// <summary>
        /// Deep-compares two <see cref="TerrainVolume"/>s cell-for-cell (material + integrity), used to
        /// assert a client's volume matches the Host's after applying a replicated delta (Req 6.5).
        /// </summary>
        protected static bool TerrainVolumesMatch(TerrainVolume a, TerrainVolume b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            if (a.Dimensions != b.Dimensions)
            {
                return false;
            }

            Int3 d = a.Dimensions;
            for (int y = 0; y < d.Y; y++)
            {
                for (int z = 0; z < d.Z; z++)
                {
                    for (int x = 0; x < d.X; x++)
                    {
                        var c = new CellCoord(x, y, z);
                        TerrainCell ca = a.Get(c);
                        TerrainCell cb = b.Get(c);
                        if (ca.Material != cb.Material || ca.Integrity != cb.Integrity)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }
    }

    /// <summary>
    /// A test-only wiring shim placed on the match prefab. In production the match-scene controller
    /// assigns the <see cref="MatchNetworkManager"/>'s serialized references and its
    /// <see cref="MatchNetworkManager.MatchFactory"/> before the object spawns; here that happens in
    /// <see cref="Awake"/>, which Netcode invokes on every instance (Host and client) before
    /// <see cref="NetworkBehaviour.OnNetworkSpawn"/> runs. The serialized fields are private, so they
    /// are assigned by reflection — the minimal, idiomatic way to reproduce inspector wiring from a
    /// test without altering the production component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TestMatchWiring : MonoBehaviour
    {
        private void Awake()
        {
            var manager = GetComponent<MatchNetworkManager>();
            var driver = GetComponent<SimulationDriver>();
            var router = GetComponent<CommandRpcRouter>();
            var replicator = GetComponent<TerrainDeltaReplicator>();

            SetPrivateField(manager, "_driver", driver);
            SetPrivateField(manager, "_commandRouter", router);
            SetPrivateField(manager, "_terrainReplicator", replicator);
            SetPrivateField(manager, "_mode", NetworkMatchMode.CompetitiveTwoHuman);
            SetPrivateField(manager, "_aiTakeoverOnDisconnect", false);

            // Identical deterministic seed on both peers (Req 6.5). MatchFactory is public/settable.
            manager.MatchFactory = NetworkedMatchTestBase.BuildBootstrapper;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected private field '{fieldName}' on {target.GetType().Name}.");
            field.SetValue(target, value);
        }
    }
}
