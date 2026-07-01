using System.Collections.Generic;
using UnityEngine;
using EpochWar.Core.Simulation;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Core.Systems;
using EpochWar.Unity.Content;
using EpochWar.Unity.Entities;
using EpochWar.Unity.Net;
using EpochWar.Unity.UI;

namespace EpochWar.Unity.Bootstrap
{
    /// <summary>
    /// The composition root for the playable <c>Match.unity</c> scene: it assembles the engine-free
    /// simulation (content catalog + terrain + Nation seeds → <see cref="MatchBootstrapper"/>) and
    /// wires it to every presentation and networking component so a Match is runnable end to end
    /// (task 17.1, Req 12.3, 12.4).
    ///
    /// <para><b>What it builds.</b> <see cref="BuildMatch"/> produces a ready-to-run
    /// <see cref="MatchBootstrapper"/> for a given <see cref="NetworkMatchMode"/>: it builds the
    /// standard <see cref="ContentSeed"/> catalog, a solid <see cref="TerrainVolume"/>, and a default
    /// set of <see cref="NationSeed"/>s (two Nations with starting resources, population, and a couple
    /// of starting Units each), then — for a co-op Match — registers a passive
    /// <see cref="DelegateAiController"/> for the AI Nation so its commands travel the same
    /// authoritative path as human intents (Req 8.5).</para>
    ///
    /// <para><b>How it wires.</b> In networked play the <see cref="MatchNetworkManager"/> calls this
    /// controller's <see cref="BuildMatch"/> through its <see cref="MatchNetworkManager.MatchFactory"/>
    /// on every peer, binds the <see cref="SimulationDriver"/> (authoritative on the Host, read-only on
    /// clients), and raises <see cref="MatchNetworkManager.LocalNationAssigned"/> once this peer's Nation
    /// is known — at which point this controller binds the HUD, info panel, command controls, zoom
    /// detail view, entity views, terrain renderer, and the <see cref="EndOfMatchController"/> to the
    /// driver and the local Nation. For quick iteration it also supports an <em>offline</em> mode that
    /// builds and binds a local authoritative Match with no networking.</para>
    ///
    /// <para>This component owns no gameplay rules; it is pure assembly/wiring. See the Unity folder
    /// README for the GameObject layout a developer drops these components onto in <c>Match.unity</c>.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchSceneController : MonoBehaviour
    {
        [Header("Simulation / Networking")]
        [SerializeField]
        [Tooltip("Drives the fixed-tick simulation. Bound by the network manager (or by this controller offline).")]
        private SimulationDriver _driver;

        [SerializeField]
        [Tooltip("Owns host election, Match assembly, and connection lifecycle. Optional in offline mode.")]
        private MatchNetworkManager _networkManager;

        [SerializeField]
        [Tooltip("Routes command intents and replicates resolved state. Optional in offline mode.")]
        private CommandRpcRouter _commandRouter;

        [Header("Presentation")]
        [SerializeField] private HudController _hud;
        [SerializeField] private InfoPanelController _infoPanel;
        [SerializeField] private CommandControlsController _commandControls;
        [SerializeField] private ZoomDetailView _zoomDetailView;
        [SerializeField] private EndOfMatchController _endOfMatch;
        [SerializeField] private EntityViewManager _entityViews;
        [SerializeField] private TerrainRenderer _terrainRenderer;
        [SerializeField] private VfxSystem _vfxSystem;
        [SerializeField] private AtmosphereController _atmosphere;

        [Header("Terrain seed")]
        [SerializeField]
        [Tooltip("Terrain volume size in cells: X, Y (up), Z.")]
        private Vector3Int _terrainDimensions = new Vector3Int(48, 8, 48);

        [SerializeField]
        [Tooltip("Material every terrain cell starts as (the battlefield ground).")]
        private CellMaterial _terrainFill = CellMaterial.Soil;

        [Header("Match seed")]
        [SerializeField]
        [Tooltip("Starting population for each seeded Nation.")]
        private int _startingPopulation = 5;

        [SerializeField]
        [Tooltip("Starting population capacity for each seeded Nation.")]
        private int _startingPopulationCapacity = 20;

        [Header("Offline (no networking) quick-start")]
        [SerializeField]
        [Tooltip("When true this controller builds and binds a local authoritative Match on Start with no networking.")]
        private bool _autoStartOffline = false;

        [SerializeField]
        [Tooltip("The Nation id the local Player controls in offline mode.")]
        private int _offlineLocalNationId = 0;

        [SerializeField]
        [Tooltip("The mode to seed in offline mode.")]
        private NetworkMatchMode _offlineMode = NetworkMatchMode.CompetitiveTwoHuman;

        // The catalog used to build the active Match; retained so command-availability predicates can
        // resolve definitions. Set whenever BuildMatch runs (on host and client alike).
        private ICatalog _catalog;
        private MatchBootstrapper _activeMatch;
        private bool _subscribedToAssignment;
        private bool _presentationBound;

        /// <summary>The catalog backing the currently assembled Match (null until <see cref="BuildMatch"/> runs).</summary>
        public ICatalog Catalog => _catalog;

        /// <summary>The assembled Match (null until <see cref="BuildMatch"/> runs).</summary>
        public MatchBootstrapper ActiveMatch => _activeMatch;

        private void Awake()
        {
            ResolveReferences();

            // Supply the Match assembly function to the network manager so every peer seeds the same
            // deterministic Match (Req 8.1, 6.5). The manager invokes this on spawn and binds the driver.
            if (_networkManager != null)
            {
                _networkManager.MatchFactory = BuildMatch;
                if (!_subscribedToAssignment)
                {
                    _networkManager.LocalNationAssigned += OnLocalNationAssigned;
                    _subscribedToAssignment = true;
                }
            }
        }

        private void Start()
        {
            if (_autoStartOffline)
            {
                StartOffline();
            }
        }

        private void Update()
        {
            // Lazily bind presentation once the networked Match is assembled and the driver is bound,
            // even for peers that never receive a Nation assignment (spectators) or where the assignment
            // callback fired before this controller was ready. This guarantees every connected Player —
            // including spectators — sees the shared state and the end-of-match summary (Req 12.3).
            if (_presentationBound || _networkManager == null)
            {
                return;
            }

            if (_networkManager.Match != null && _driver != null && _driver.IsBound)
            {
                BindPresentation(_networkManager.LocalNationId);
            }
        }

        private void OnDestroy()
        {
            if (_networkManager != null && _subscribedToAssignment)
            {
                _networkManager.LocalNationAssigned -= OnLocalNationAssigned;
                _subscribedToAssignment = false;
            }
        }

        // ------------------------------------------------------------------
        // Match assembly (the MatchNetworkManager.MatchFactory delegate)
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds a ready-to-run <see cref="MatchBootstrapper"/> for <paramref name="mode"/>: the
        /// standard content catalog, a solid terrain volume, and the default Nation seeds, plus a
        /// passive AI controller for the AI Nation in a co-op Match (Req 8.1, 8.5, 12.1). Retains the
        /// catalog for command-availability wiring.
        /// </summary>
        public MatchBootstrapper BuildMatch(NetworkMatchMode mode)
        {
            // The lobby's pick (if any) overrides the mode the caller supplied, so Boot.unity's
            // selection flows into the seeded Match; otherwise the caller's mode is used as-is.
            NetworkMatchMode effectiveMode = LobbyConfig.HasSelection ? LobbyConfig.SelectedMode : mode;

            _catalog = ContentSeed.BuildStandardCatalog();
            TerrainVolume terrain = BuildTerrain();
            List<NationSeed> seeds = BuildNationSeeds(effectiveMode, _catalog);

            var bootstrapper = MatchBootstrapper.Create(_catalog, terrain, seeds);

            // In a co-op Match the second Nation is AI; route its (currently passive) commands through
            // the same authoritative path human intents take (Req 8.5). Real AI behaviour is layered in
            // later; a passive controller keeps the unified path exercised without unbalancing the seed.
            if (effectiveMode == NetworkMatchMode.CooperativeVsAi)
            {
                bootstrapper.AddAiController(new DelegateAiController(nationId: 1));
            }

            _activeMatch = bootstrapper;
            return bootstrapper;
        }

        /// <summary>Builds the battlefield terrain volume from the configured dimensions/fill.</summary>
        private TerrainVolume BuildTerrain()
        {
            var dims = new Int3(
                Mathf.Max(1, _terrainDimensions.x),
                Mathf.Max(1, _terrainDimensions.y),
                Mathf.Max(1, _terrainDimensions.z));

            return new TerrainVolume(dims, _terrainFill);
        }

        /// <summary>
        /// Builds the default two-Nation seed set (Req 12.1). Nation 0 is always human. Nation 1 is
        /// human in a competitive Match and AI in a co-op Match. Each Nation gets starting resources,
        /// population, and a worker + warrior placed near opposite corners of the battlefield.
        /// </summary>
        private List<NationSeed> BuildNationSeeds(NetworkMatchMode mode, ICatalog catalog)
        {
            int groundY = Mathf.Max(0, Mathf.Max(1, _terrainDimensions.y) - 1);
            int maxX = Mathf.Max(1, _terrainDimensions.x) - 1;
            int maxZ = Mathf.Max(1, _terrainDimensions.z) - 1;

            var nation0 = new NationSeed(
                nationId: 0,
                isAI: false,
                startingResources: BuildStartingResources(),
                startingUnits: BuildStartingUnits(catalog, new CellCoord(2, groundY, 2)),
                startingPopulation: _startingPopulation,
                startingPopulationCapacity: _startingPopulationCapacity);

            bool nation1IsAi = mode == NetworkMatchMode.CooperativeVsAi;
            var nation1 = new NationSeed(
                nationId: 1,
                isAI: nation1IsAi,
                startingResources: BuildStartingResources(),
                startingUnits: BuildStartingUnits(
                    catalog, new CellCoord(Mathf.Max(0, maxX - 2), groundY, Mathf.Max(0, maxZ - 2))),
                startingPopulation: _startingPopulation,
                startingPopulationCapacity: _startingPopulationCapacity);

            return new List<NationSeed> { nation0, nation1 };
        }

        /// <summary>The starting Resource stores for a seeded Nation (capacities mirror the standard catalog).</summary>
        private static Dictionary<ResourceType, ResourceStore> BuildStartingResources()
        {
            return new Dictionary<ResourceType, ResourceStore>
            {
                { ResourceType.Food, new ResourceStore(200f, 500f) },
                { ResourceType.Wood, new ResourceStore(200f, 500f) },
                { ResourceType.Stone, new ResourceStore(100f, 500f) },
                { ResourceType.Metal, new ResourceStore(50f, 500f) },
                { ResourceType.Energy, new ResourceStore(0f, 1000f) },
                // Research is uncapped so long-term tech investment is never wasted.
                { ResourceType.Research, new ResourceStore(0f, 0f) },
                { ResourceType.ExoticMatter, new ResourceStore(0f, 200f) },
            };
        }

        /// <summary>
        /// Builds the starting Units for a Nation near <paramref name="origin"/>: a Prehistoric worker
        /// and warrior when those definitions exist in the catalog. Missing definitions are skipped so
        /// a trimmed catalog still seeds a valid (possibly unit-less) Nation.
        /// </summary>
        private static List<UnitSeed> BuildStartingUnits(ICatalog catalog, CellCoord origin)
        {
            var units = new List<UnitSeed>();

            if (catalog.TryGetUnit("unit_gatherer", out var gatherer))
            {
                units.Add(new UnitSeed(gatherer, origin));
            }

            if (catalog.TryGetUnit("unit_warrior", out var warrior))
            {
                units.Add(new UnitSeed(warrior, new CellCoord(origin.X + 1, origin.Y, origin.Z)));
            }

            return units;
        }

        // ------------------------------------------------------------------
        // Offline quick-start (no networking)
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds a local authoritative Match and binds the driver + presentation with no networking,
        /// so <c>Match.unity</c> can be played/iterated standalone. Safe to call once; a networked
        /// session should use the <see cref="MatchNetworkManager"/> instead.
        /// </summary>
        public void StartOffline()
        {
            ResolveReferences();

            MatchBootstrapper match = BuildMatch(_offlineMode);
            if (_driver != null)
            {
                _driver.Bind(match, hasAuthority: true);
                _driver.Play();
            }

            BindPresentation(_offlineLocalNationId);
        }

        // ------------------------------------------------------------------
        // Presentation wiring
        // ------------------------------------------------------------------

        private void OnLocalNationAssigned(int localNationId) => BindPresentation(localNationId);

        /// <summary>
        /// Binds every presentation component to the (already driver-bound) simulation and the local
        /// Nation. Idempotent: each component's <c>Bind</c> re-subscribes safely, so a repeated
        /// assignment simply refreshes the wiring for the new local Nation id.
        /// </summary>
        public void BindPresentation(int localNationId)
        {
            if (_driver == null)
            {
                return;
            }

            _presentationBound = true;

            // The command-availability predicates read the live systems of the assembled simulation so
            // the control bar's enabled state matches the authoritative handlers exactly (Req 7.5).
            CommandAvailability availability = BuildAvailability();

            if (_hud != null)
            {
                _hud.Bind(_driver, localNationId, _catalog);
            }

            if (_infoPanel != null)
            {
                // Pass the ability-activation collaborators so the info panel can render selectable
                // ability controls whose enabled state binds to the shared availability predicate and
                // whose clicks issue ActivateAbilityCommand on the authoritative path (Req 13.1-13.4).
                // The VisionSystem lets it resolve an enemy selection's shown position through the fog of
                // war (Req 14.5, 14.6, 14.9).
                _infoPanel.Bind(_driver, _commandControls, availability, localNationId, ResolveVisionSystem());
            }

            if (_commandControls != null && _commandRouter != null && availability != null)
            {
                _commandControls.Bind(_driver, _commandRouter, availability, localNationId);
            }

            if (_zoomDetailView != null)
            {
                // Fog-of-war context so a framed enemy Unit's shown coordinates come from the
                // VisionSystem (Req 14.5, 14.6, 14.9).
                _zoomDetailView.Bind(_driver, localNationId, ResolveVisionSystem());
            }

            if (_entityViews != null)
            {
                // Thread the VisionSystem + local Nation so enemy Unit/Structure views resolve their
                // displayed position (current / Last_Known_Position / suppressed) through the fog of war
                // (Req 14.4-14.6, 14.9).
                _entityViews.Bind(_driver, localNationId, ResolveVisionSystem());
            }

            if (_terrainRenderer != null)
            {
                _terrainRenderer.Bind(_driver);
            }

            if (_vfxSystem != null)
            {
                _vfxSystem.Bind(_driver);
            }

            // Apply the Match environment's skybox/ambient/fog/weather on Match start (Req 6). The
            // atmosphere is global (RenderSettings) and needs no driver; it is applied here so it runs on
            // both the offline quick-start and the networked assignment paths, alongside the other systems.
            if (_atmosphere != null)
            {
                _atmosphere.ApplyForMatchStart();
            }

            if (_endOfMatch != null)
            {
                _endOfMatch.Bind(_driver, _commandRouter, localNationId);
            }
        }

        /// <summary>
        /// Builds the shared <see cref="CommandAvailability"/> helper from the active simulation's
        /// systems and catalog, or returns null when no Match is assembled yet.
        /// </summary>
        private CommandAvailability BuildAvailability()
        {
            MatchBootstrapper match = _activeMatch
                ?? (_networkManager != null ? _networkManager.Match : null);

            if (match == null || _catalog == null)
            {
                return null;
            }

            MatchSimulation sim = match.Simulation;
            return new CommandAvailability(
                _catalog,
                sim.ResourceSystem,
                sim.TechSystem,
                sim.CivSystem,
                sim.BaseSystem);
        }

        /// <summary>
        /// Resolves the assembled Match's <see cref="VisionSystem"/> (from the offline/active match or
        /// the network manager's match), or null when no Match is assembled yet. The Entity_View_System
        /// uses it to display enemy positions through the fog of war (Req 14.4-14.6, 14.9).
        /// </summary>
        private VisionSystem ResolveVisionSystem()
        {
            MatchBootstrapper match = _activeMatch
                ?? (_networkManager != null ? _networkManager.Match : null);

            return match?.Simulation?.VisionSystem;
        }

        private void ResolveReferences()
        {
            if (_driver == null)
            {
                _driver = FindObjectOfType<SimulationDriver>();
            }

            if (_networkManager == null)
            {
                _networkManager = FindObjectOfType<MatchNetworkManager>();
            }

            if (_commandRouter == null)
            {
                _commandRouter = FindObjectOfType<CommandRpcRouter>();
            }
        }
    }
}
