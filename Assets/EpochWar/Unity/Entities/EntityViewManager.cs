using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Core.Systems;
using EpochWar.Unity.Bootstrap;

namespace EpochWar.Unity.Entities
{
    /// <summary>
    /// Reconciles the authoritative core entity collections (<see cref="MatchState.Units"/> and
    /// <see cref="MatchState.Structures"/>) with their scene <see cref="UnitView"/>/<see cref="StructureView"/>
    /// GameObjects each simulation tick (task 15.1, Req 8.3), and drives the Era-scaled visual detail
    /// (Req 7) and large-scale battle readability (Req 8) behaviour of the Entity_View_System.
    ///
    /// <para><b>Reconciliation.</b> The core owns the truth; this manager is the "dumb" presentation
    /// reconciler. It subscribes to <see cref="SimulationDriver.Ticked"/> and, after every tick, performs
    /// a spawn/mirror/despawn pass:
    /// <list type="bullet">
    ///   <item>instantiates a view for any core entity that has appeared (a recruited Unit, a placed
    ///   Structure) from the configured prefab and binds it;</item>
    ///   <item>calls <c>Bind</c> on every live view so positions/health/construction mirror the latest
    ///   state;</item>
    ///   <item>destroys the view for any entity that has left the Match (a killed Unit, a destroyed
    ///   Structure).</item>
    /// </list></para>
    ///
    /// <para><b>Visual_Detail_Tier (Req 7).</b> On each spawn/mirror it assigns every Unit and Structure
    /// view a Visual_Detail_Tier resolved from its content-author override
    /// (<see cref="UnitDef.VisualDetailTier"/>/<see cref="StructureDef.VisualDetailTier"/>, itself flowed
    /// from the authored <c>UnitAsset</c>/<c>StructureAsset</c> override field, Req 7.4) when that value is
    /// within the valid range, or the Era-derived default (<see cref="DefaultVisualDetailTierForEra"/>)
    /// otherwise. An unset (negative/zero sentinel) or out-of-range tier therefore falls back to the
    /// Era-derived default without failing to render (Req 7.5). The Era default is non-decreasing by Era
    /// and equal for same-Era entries of the same classification (Req 7.1, 7.2, 7.3).</para>
    ///
    /// <para><b>Large-scale readability (Req 8).</b> Each frame (<see cref="LateUpdate"/>, since the camera
    /// moves per frame rather than per tick) it:
    /// <list type="bullet">
    ///   <item>counts the Units within the camera frustum and, once that density exceeds a configurable
    ///   threshold — or the smoothed frame rate dips below a configured floor — reduces per-Unit rendering
    ///   detail (disables shadow casting) to protect the frame rate (Req 8.1);</item>
    ///   <item>swaps a Unit to a simplified Nation-coloured billboard marker at/beyond a far-zoom
    ///   camera-distance threshold (Req 8.2) and restores full detail below it (Req 8.3);</item>
    ///   <item>keeps a hysteresis buffer between the simplify (enter) and restore (exit) distances plus a
    ///   minimum per-Unit re-toggle interval so a Unit's representation cannot flip more than once within
    ///   that interval while the camera hovers near the threshold (Req 8.4).</item>
    /// </list></para>
    ///
    /// Because it reconciles against the whole collection each tick it is robust to any event it might
    /// miss, and it works identically on the Host and on clients that render replicated state. It reads
    /// state and the camera; it never writes back into the core.
    /// </summary>
    public sealed class EntityViewManager : MonoBehaviour
    {
        /// <summary>The lowest valid (richest-floor) Visual_Detail_Tier value (Req 7.1: a positive integer).</summary>
        public const int MinVisualDetailTier = 1;

        /// <summary>
        /// The highest valid Visual_Detail_Tier value. Equals the number of <see cref="Era"/> stages so the
        /// Era-derived default (one tier per Era) spans exactly <see cref="MinVisualDetailTier"/>..this.
        /// </summary>
        public const int MaxVisualDetailTier = 9;

        [SerializeField]
        [Tooltip("Drives the simulation; the manager reconciles after each of its ticks.")]
        private SimulationDriver _driver;

        [SerializeField]
        [Tooltip("Prefab instantiated for each core unit. Should carry a UnitView component.")]
        private UnitView _unitPrefab;

        [SerializeField]
        [Tooltip("Prefab instantiated for each core structure. Should carry a StructureView component.")]
        private StructureView _structurePrefab;

        [SerializeField]
        [Tooltip("Optional parent for spawned unit views; defaults to this transform.")]
        private Transform _unitRoot;

        [SerializeField]
        [Tooltip("Optional parent for spawned structure views; defaults to this transform.")]
        private Transform _structureRoot;

        [Header("Battle readability / LOD (Req 8)")]
        [SerializeField]
        [Tooltip("Battlefield camera used for density and far-zoom distance. Defaults to Camera.main when unset.")]
        private Camera _camera;

        [SerializeField]
        [Tooltip("Visible-Unit count within the camera view above which per-Unit detail is reduced (Req 8.1).")]
        [Min(0)]
        private int _densityThreshold = 60;

        [SerializeField]
        [Tooltip("Frame-rate floor (fps) the density LOD targets; below it, detail reduction is forced (Req 8.1).")]
        [Min(1)]
        private float _minFrameRate = 30f;

        [SerializeField]
        [Tooltip("Camera-to-Unit distance at or beyond which a Unit swaps to a simplified marker (Req 8.2).")]
        [Min(0f)]
        private float _farZoomEnterDistance = 60f;

        [SerializeField]
        [Tooltip("Camera-to-Unit distance below which full detail is restored. Must be < the enter distance "
            + "to form the anti-flicker hysteresis buffer (Req 8.3, 8.4).")]
        [Min(0f)]
        private float _farZoomExitDistance = 45f;

        [SerializeField]
        [Tooltip("Minimum seconds between simplify<->full toggles for a single Unit, preventing flicker (Req 8.4).")]
        [Min(0f)]
        private float _minReToggleInterval = 1f;

        [SerializeField]
        [Tooltip("Optional prefab used as the simplified far-zoom marker. A camera-facing quad is built when unset.")]
        private GameObject _unitMarkerPrefab;

        [SerializeField]
        [Tooltip("Local height above the Unit at which the simplified marker is placed.")]
        private float _markerHeight = 1f;

        [SerializeField]
        [Tooltip("Per-Nation marker colours indexed by Nation id (Req 8.2). A hue is generated for ids beyond the list.")]
        private Color[] _nationColors =
        {
            new Color(0.25f, 0.5f, 1f, 1f),
            new Color(1f, 0.3f, 0.25f, 1f),
        };

        private readonly Dictionary<int, UnitView> _unitViews = new Dictionary<int, UnitView>();
        private readonly Dictionary<int, StructureView> _structureViews = new Dictionary<int, StructureView>();

        // Fog-of-war display wiring (Req 14.4-14.6, 14.9). When set, enemy views are positioned/hidden
        // through the VisionSystem's display-position query from the LOCAL Nation's perspective. When
        // unset (null vision or negative Nation id), every view simply mirrors its current position.
        private VisionSystem _vision;
        private int _localNationId = -1;

        // Per-Unit level-of-detail bookkeeping (Req 8).
        private readonly Dictionary<int, UnitLod> _unitLod = new Dictionary<int, UnitLod>();

        // Reused scratch buffers so the per-tick reconcile allocates nothing steady-state.
        private readonly List<int> _removalScratch = new List<int>();

        // Smoothed frame rate for the frame-rate floor (Req 8.1).
        private float _smoothedFps;

        // Cached URP/built-in colour property ids for tinting markers via a property block.
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock _markerColorBlock;

        // ------------------------------------------------------------------
        // Visual_Detail_Tier resolution (Req 7) — pure, engine-free helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// The Era-derived default Visual_Detail_Tier (Req 7.1, 7.2, 7.3): a positive integer that is
        /// non-decreasing by <see cref="Era"/> and identical for entries of the same Era. Implemented as
        /// <c>MinVisualDetailTier + Era ordinal</c> so Prehistoric maps to <see cref="MinVisualDetailTier"/>
        /// and Space maps to <see cref="MaxVisualDetailTier"/>. Total for any input: a negative/undefined
        /// ordinal clamps to the minimum tier and any ordinal beyond the last Era clamps to the maximum, so
        /// the caller always receives a valid tier and never fails to render (Req 7.5).
        /// </summary>
        public static int DefaultVisualDetailTierForEra(Era era)
        {
            int ordinal = (int)era;
            if (ordinal < 0)
            {
                return MinVisualDetailTier;
            }

            int tier = MinVisualDetailTier + ordinal;
            return tier > MaxVisualDetailTier ? MaxVisualDetailTier : tier;
        }

        /// <summary>
        /// Resolves the Visual_Detail_Tier for a rendered entity (Req 7.1, 7.4, 7.5): returns
        /// <paramref name="authoredTier"/> when it is a content-author override within the valid range
        /// [<see cref="MinVisualDetailTier"/>, <see cref="MaxVisualDetailTier"/>], otherwise the
        /// Era-derived default. An unset sentinel (the assets author -1, and the Core default is 0) or an
        /// out-of-range value therefore falls back to the Era default rather than being used verbatim.
        /// </summary>
        public static int ResolveVisualDetailTier(int authoredTier, Era era)
        {
            if (authoredTier >= MinVisualDetailTier && authoredTier <= MaxVisualDetailTier)
            {
                return authoredTier;
            }

            return DefaultVisualDetailTierForEra(era);
        }

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        private void OnEnable()
        {
            if (_driver != null)
            {
                _driver.Ticked += OnTicked;
            }
        }

        private void OnDisable()
        {
            if (_driver != null)
            {
                _driver.Ticked -= OnTicked;
            }
        }

        /// <summary>
        /// Assigns the driver at runtime (e.g. from the match bootstrap wiring) and (re)subscribes to
        /// its tick event. Performs an immediate reconcile so the scene reflects the current state.
        /// </summary>
        public void Bind(SimulationDriver driver)
        {
            if (_driver != null)
            {
                _driver.Ticked -= OnTicked;
            }

            _driver = driver;

            if (_driver != null && isActiveAndEnabled)
            {
                _driver.Ticked += OnTicked;
            }

            Reconcile();
        }

        /// <summary>
        /// Assigns the driver plus the fog-of-war display context (Req 14.4-14.6, 14.9): the
        /// <paramref name="localNationId"/> viewer and the <paramref name="visionSystem"/> that resolves
        /// each enemy entity's display position. With this context, enemy Unit/Structure views show the
        /// Last_Known_Position while hidden-with-LKP, the current position while visible, and are
        /// suppressed while hidden with no LKP. Passing a null vision system or negative Nation id
        /// reverts to plain current-position mirroring.
        /// </summary>
        public void Bind(SimulationDriver driver, int localNationId, VisionSystem visionSystem)
        {
            _localNationId = localNationId;
            _vision = visionSystem;
            Bind(driver);
        }

        private void OnTicked(IReadOnlyList<GameEvent> events) => Reconcile();

        /// <summary>
        /// Runs one spawn/mirror/despawn pass against the current core state. Public so scene wiring or
        /// tests can force an immediate refresh without waiting for a tick.
        /// </summary>
        public void Reconcile()
        {
            var state = _driver != null ? _driver.State : null;
            if (state == null)
            {
                return;
            }

            ReconcileUnits(state);
            ReconcileStructures(state);
        }

        private void ReconcileUnits(MatchState state)
        {
            // Spawn/mirror.
            foreach (var pair in state.Units)
            {
                if (!_unitViews.TryGetValue(pair.Key, out var view))
                {
                    if (_unitPrefab == null)
                    {
                        continue;
                    }

                    view = Instantiate(_unitPrefab, _unitRoot != null ? _unitRoot : transform);
                    view.name = $"Unit_{pair.Key}";
                    _unitViews[pair.Key] = view;
                }

                view.Bind(pair.Value);
                AssignUnitVisualDetailTier(view, pair.Value.Def);
                ApplyUnitVisionDisplay(view, pair.Value);
            }

            // Despawn views whose core unit is gone (Req 3.5).
            _removalScratch.Clear();
            foreach (var id in _unitViews.Keys)
            {
                if (!state.Units.ContainsKey(id))
                {
                    _removalScratch.Add(id);
                }
            }

            foreach (var id in _removalScratch)
            {
                if (_unitViews.TryGetValue(id, out var view) && view != null)
                {
                    // The marker is a child of the view, so destroying the view removes it too.
                    Destroy(view.gameObject);
                }

                _unitViews.Remove(id);
                _unitLod.Remove(id);
            }
        }

        private void ReconcileStructures(MatchState state)
        {
            foreach (var pair in state.Structures)
            {
                if (!_structureViews.TryGetValue(pair.Key, out var view))
                {
                    if (_structurePrefab == null)
                    {
                        continue;
                    }

                    view = Instantiate(_structurePrefab, _structureRoot != null ? _structureRoot : transform);
                    view.name = $"Structure_{pair.Key}";
                    _structureViews[pair.Key] = view;
                }

                view.Bind(pair.Value);
                AssignStructureVisualDetailTier(view, pair.Value.Def);
                ApplyStructureVisionDisplay(view, pair.Value);
            }

            // Despawn views whose core structure is gone (Req 4.5).
            _removalScratch.Clear();
            foreach (var id in _structureViews.Keys)
            {
                if (!state.Structures.ContainsKey(id))
                {
                    _removalScratch.Add(id);
                }
            }

            foreach (var id in _removalScratch)
            {
                if (_structureViews.TryGetValue(id, out var view) && view != null)
                {
                    Destroy(view.gameObject);
                }

                _structureViews.Remove(id);
            }
        }

        private static void AssignUnitVisualDetailTier(UnitView view, UnitDef def)
        {
            if (view == null || def == null)
            {
                return;
            }

            int tier = ResolveVisualDetailTier(def.VisualDetailTier, def.Era);
            if (view.VisualDetailTier != tier)
            {
                view.SetVisualDetailTier(tier);
            }
        }

        private static void AssignStructureVisualDetailTier(StructureView view, StructureDef def)
        {
            if (view == null || def == null)
            {
                return;
            }

            int tier = ResolveVisualDetailTier(def.VisualDetailTier, def.Era);
            if (view.VisualDetailTier != tier)
            {
                view.SetVisualDetailTier(tier);
            }
        }

        // ------------------------------------------------------------------
        // Fog-of-war display position (Req 14.4-14.6, 14.9)
        // ------------------------------------------------------------------

        /// <summary>
        /// Positions or suppresses an enemy Unit view according to the LOCAL Nation's vision (Req
        /// 14.4-14.6, 14.9): shows the current position while visible, the Last_Known_Position while
        /// hidden-with-LKP, and hides the view entirely while hidden with no recorded LKP. Own-Nation
        /// views (and all views when no vision context is wired) always show their current position.
        /// </summary>
        private void ApplyUnitVisionDisplay(UnitView view, UnitInstance unit)
        {
            if (view == null)
            {
                return;
            }

            if (_vision == null || _localNationId < 0 || unit.OwnerNationId == _localNationId)
            {
                SetViewActive(view.gameObject, true);
                return;
            }

            int key = VisionSystem.UnitKey(unit.Id);
            WorldPosition? display = _vision.GetDisplayPosition(_localNationId, key, unit.Position);
            if (display.HasValue)
            {
                view.transform.localPosition = UnitView.ToVector3(display.Value);
                SetViewActive(view.gameObject, true);
            }
            else
            {
                // Hidden with no Last_Known_Position: do not display (Req 14.4, 14.9).
                SetViewActive(view.gameObject, false);
            }
        }

        /// <summary>
        /// Positions or suppresses an enemy Structure view according to the LOCAL Nation's vision, using
        /// the same three-case rule as <see cref="ApplyUnitVisionDisplay"/> (Req 14.4-14.6, 14.9). The
        /// Structure's cell origin is treated as its world position, matching how the VisionSystem
        /// records a Structure's Last_Known_Position.
        /// </summary>
        private void ApplyStructureVisionDisplay(StructureView view, StructureInstance structure)
        {
            if (view == null)
            {
                return;
            }

            if (_vision == null || _localNationId < 0 || structure.OwnerNationId == _localNationId)
            {
                SetViewActive(view.gameObject, true);
                return;
            }

            int key = VisionSystem.StructureKey(structure.Id);
            WorldPosition current = WorldPosition.FromCell(structure.Origin);
            WorldPosition? display = _vision.GetDisplayPosition(_localNationId, key, current);
            if (display.HasValue)
            {
                view.transform.localPosition = UnitView.ToVector3(display.Value);
                SetViewActive(view.gameObject, true);
            }
            else
            {
                SetViewActive(view.gameObject, false);
            }
        }

        private static void SetViewActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active)
            {
                go.SetActive(active);
            }
        }

        // ------------------------------------------------------------------
        // Level of detail (Req 8) — camera-driven, runs every frame
        // ------------------------------------------------------------------

        private void LateUpdate()
        {
            UpdateLevelOfDetail();
        }

        private void UpdateLevelOfDetail()
        {
            if (_unitViews.Count == 0)
            {
                return;
            }

            Camera cam = ResolveCamera();
            if (cam == null)
            {
                return;
            }

            Vector3 camPos = cam.transform.position;

            // Smoothed frame rate for the frame-rate floor (Req 8.1).
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f)
            {
                float instantFps = 1f / dt;
                _smoothedFps = _smoothedFps <= 0f ? instantFps : Mathf.Lerp(_smoothedFps, instantFps, 0.1f);
            }

            // Density: count Units currently within the camera frustum (Req 8.1).
            Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(cam);
            int visibleCount = 0;
            foreach (KeyValuePair<int, UnitView> pair in _unitViews)
            {
                UnitView v = pair.Value;
                if (v == null)
                {
                    continue;
                }

                var bounds = new Bounds(v.transform.position, Vector3.one);
                if (GeometryUtility.TestPlanesAABB(frustum, bounds))
                {
                    visibleCount++;
                }
            }

            bool densityExceeded = visibleCount > _densityThreshold
                                   || (_smoothedFps > 0f && _smoothedFps < _minFrameRate);

            // The exit distance must sit below the enter distance so there is a hysteresis buffer between
            // simplify and restore (Req 8.4); clamp defensively in case they were authored inverted.
            float enter = _farZoomEnterDistance;
            float exit = Mathf.Min(_farZoomExitDistance, enter);
            float now = Time.unscaledTime;

            foreach (KeyValuePair<int, UnitView> pair in _unitViews)
            {
                UnitView view = pair.Value;
                if (view == null)
                {
                    continue;
                }

                if (!_unitLod.TryGetValue(pair.Key, out UnitLod lod))
                {
                    lod = CreateLod(view);
                    _unitLod[pair.Key] = lod;
                }

                float dist = Vector3.Distance(camPos, view.transform.position);

                // Far-zoom simplify/restore with a hysteresis buffer (Req 8.2, 8.3).
                bool desiredSimplified = lod.Simplified;
                if (!lod.Simplified && dist >= enter)
                {
                    desiredSimplified = true;
                }
                else if (lod.Simplified && dist < exit)
                {
                    desiredSimplified = false;
                }

                // Minimum re-toggle interval (Req 8.4): only flip once the interval has elapsed.
                if (desiredSimplified != lod.Simplified
                    && now - lod.LastSimplifyToggleTime >= _minReToggleInterval)
                {
                    lod.Simplified = desiredSimplified;
                    lod.LastSimplifyToggleTime = now;
                    ApplySimplified(view, lod, desiredSimplified);
                }

                if (lod.Simplified)
                {
                    BillboardMarker(lod, camPos);
                }
                else
                {
                    // Density-driven detail reduction while at full (non-marker) detail (Req 8.1).
                    if (densityExceeded != lod.ReducedDetail)
                    {
                        lod.ReducedDetail = densityExceeded;
                        ApplyReducedDetail(lod, densityExceeded);
                    }
                }
            }
        }

        private Camera ResolveCamera()
        {
            if (_camera != null)
            {
                return _camera;
            }

            _camera = Camera.main;
            return _camera;
        }

        private UnitLod CreateLod(UnitView view)
        {
            var lod = new UnitLod
            {
                Renderers = view.GetComponentsInChildren<Renderer>(includeInactive: true),
                LastSimplifyToggleTime = float.NegativeInfinity,
            };

            lod.ShadowModes = new ShadowCastingMode[lod.Renderers.Length];
            for (int i = 0; i < lod.Renderers.Length; i++)
            {
                lod.ShadowModes[i] = lod.Renderers[i] != null
                    ? lod.Renderers[i].shadowCastingMode
                    : ShadowCastingMode.On;
            }

            return lod;
        }

        private void ApplySimplified(UnitView view, UnitLod lod, bool simplified)
        {
            if (simplified)
            {
                SetRenderersEnabled(lod, false);

                if (lod.Marker == null)
                {
                    lod.Marker = CreateUnitMarker(view);
                }

                if (lod.Marker != null)
                {
                    lod.Marker.SetActive(true);
                }

                // Full-detail renderers are hidden while simplified, so any density detail-reduction is moot.
                lod.ReducedDetail = false;
            }
            else
            {
                if (lod.Marker != null)
                {
                    lod.Marker.SetActive(false);
                }

                SetRenderersEnabled(lod, true);
                RestoreShadowModes(lod);
                lod.ReducedDetail = false;
            }
        }

        private static void SetRenderersEnabled(UnitLod lod, bool enabled)
        {
            if (lod.Renderers == null)
            {
                return;
            }

            foreach (Renderer r in lod.Renderers)
            {
                if (r != null)
                {
                    r.enabled = enabled;
                }
            }
        }

        private static void ApplyReducedDetail(UnitLod lod, bool reduced)
        {
            if (lod.Renderers == null)
            {
                return;
            }

            for (int i = 0; i < lod.Renderers.Length; i++)
            {
                Renderer r = lod.Renderers[i];
                if (r == null)
                {
                    continue;
                }

                // Dropping shadow casting is a cheap, safe per-Unit detail reduction that measurably eases
                // GPU load in a dense battle without hiding the Unit (Req 8.1).
                r.shadowCastingMode = reduced ? ShadowCastingMode.Off : lod.ShadowModes[i];
            }
        }

        private static void RestoreShadowModes(UnitLod lod)
        {
            if (lod.Renderers == null)
            {
                return;
            }

            for (int i = 0; i < lod.Renderers.Length; i++)
            {
                if (lod.Renderers[i] != null)
                {
                    lod.Renderers[i].shadowCastingMode = lod.ShadowModes[i];
                }
            }
        }

        private GameObject CreateUnitMarker(UnitView view)
        {
            GameObject marker;
            if (_unitMarkerPrefab != null)
            {
                marker = Instantiate(_unitMarkerPrefab, view.transform);
            }
            else
            {
                // A camera-facing quad is a lightweight silhouette when no marker prefab is authored.
                marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Collider col = marker.GetComponent<Collider>();
                if (col != null)
                {
                    Destroy(col);
                }

                marker.transform.SetParent(view.transform, worldPositionStays: false);
            }

            marker.name = "LodMarker";
            marker.transform.localPosition = Vector3.up * _markerHeight;

            int nationId = view.Model != null ? view.Model.OwnerNationId : -1;
            ApplyNationColor(marker, ResolveNationColor(nationId));
            return marker;
        }

        private void BillboardMarker(UnitLod lod, Vector3 camPos)
        {
            if (lod.Marker == null)
            {
                return;
            }

            Vector3 toCamera = lod.Marker.transform.position - camPos;
            if (toCamera.sqrMagnitude > 1e-6f)
            {
                lod.Marker.transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
            }
        }

        /// <summary>
        /// The distinct Nation-indicating colour for the simplified marker (Req 8.2): the authored palette
        /// entry for <paramref name="nationId"/>, or a deterministic generated hue for ids beyond the list.
        /// </summary>
        public Color ResolveNationColor(int nationId)
        {
            if (_nationColors != null && _nationColors.Length > 0 && nationId >= 0)
            {
                return _nationColors[nationId % _nationColors.Length];
            }

            // Golden-ratio hue stepping gives well-separated colours for any Nation id.
            float hue = Mathf.Repeat(Mathf.Abs(nationId) * 0.618033989f, 1f);
            return Color.HSVToRGB(hue, 0.7f, 0.95f);
        }

             private void ApplyNationColor(GameObject marker, Color color)
        {
            if (marker == null)
            {
                return;
            }

            _markerColorBlock ??= new MaterialPropertyBlock();

            var renderers = marker.GetComponentsInChildren<Renderer>(includeInactive: true);
            foreach (Renderer r in renderers)
            {
                if (r == null)
                {
                    continue;
                }

                r.GetPropertyBlock(_markerColorBlock);
                _markerColorBlock.SetColor(BaseColorId, color); // URP lit/unlit base colour
                _markerColorBlock.SetColor(ColorId, color);     // built-in / sprite colour
                r.SetPropertyBlock(_markerColorBlock);
            }
        }

        /// <summary>Number of live Unit views (diagnostic/testing aid).</summary>
        public int UnitViewCount => _unitViews.Count;

        /// <summary>Number of live Structure views (diagnostic/testing aid).</summary>
        public int StructureViewCount => _structureViews.Count;

        /// <summary>Per-Unit level-of-detail state for the readability behaviour (Req 8).</summary>
        private sealed class UnitLod
        {
            /// <summary>Renderers of the full-detail view, cached at creation (excludes the marker).</summary>
            public Renderer[] Renderers;

            /// <summary>Original shadow-casting modes, restored when detail reduction is lifted.</summary>
            public ShadowCastingMode[] ShadowModes;

            /// <summary>The lazily-created simplified marker child; null until the first simplify.</summary>
            public GameObject Marker;

            /// <summary>True while the Unit is rendered as the simplified far-zoom marker (Req 8.2).</summary>
            public bool Simplified;

            /// <summary>True while density-driven detail reduction (shadows off) is applied (Req 8.1).</summary>
            public bool ReducedDetail;

            /// <summary>Unscaled time of the last simplify<->full flip, gating the re-toggle interval (Req 8.4).</summary>
            public float LastSimplifyToggleTime;
        }
    }
}
