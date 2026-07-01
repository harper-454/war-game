using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Core.Systems;
using EpochWar.Unity.Bootstrap;

namespace EpochWar.Unity.UI
{
    /// <summary>
    /// The zoom-in unit detail view: a close-up rendering of a selected Unit shown alongside its full
    /// attribute set (Req 7.3) using UI Toolkit.
    ///
    /// A dedicated <see cref="Camera"/> renders into an off-screen <see cref="RenderTexture"/> that is
    /// displayed in a UI Toolkit <see cref="Image"/> element, framing only the selected Unit's scene
    /// object rather than the main battlefield camera's wide view. Beside the render, the panel lists
    /// the Unit's complete attribute set — reusing the same engine-free
    /// <see cref="InfoPanelViewModel.ForUnit"/> snapshot the information panel uses (Req 7.2, 7.3) — so
    /// the close-up and its data stay consistent.
    ///
    /// The view is hidden until <see cref="Show(UnitInstance, Transform)"/> activates it for a
    /// selected Unit and its scene <see cref="Transform"/>. While visible it re-frames the dedicated
    /// camera on the (possibly moving) target each <c>LateUpdate</c> and, when bound to a
    /// <see cref="SimulationDriver"/>, refreshes the attribute list every tick so the displayed values
    /// track changes (Req 7.4). The dedicated camera is disabled while hidden so it costs nothing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ZoomDetailView : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The UIDocument whose rootVisualElement hosts the zoom detail view.")]
        private UIDocument _document;

        [SerializeField]
        [Tooltip("Dedicated camera that renders the close-up into the render texture. Created if left empty.")]
        private Camera _camera;

        [SerializeField]
        [Tooltip("Drives the simulation; the attribute list refreshes after each tick while visible.")]
        private SimulationDriver _driver;

        [SerializeField]
        [Tooltip("Width/height of the render texture the dedicated camera renders into.")]
        private Vector2Int _renderTextureSize = new Vector2Int(256, 256);

        [SerializeField]
        [Tooltip("Distance from the framed unit at which the dedicated camera sits.")]
        private float _framingDistance = 3f;

        [SerializeField]
        [Tooltip("Vertical offset above the framed unit's pivot the camera looks at.")]
        private float _framingHeight = 1f;

        private RenderTexture _renderTexture;
        private VisualElement _root;
        private Image _preview;
        private Label _titleLabel;
        private VisualElement _rows;

        private Transform _target;
        private int _selectedUnitId;
        private bool _visible;
        private bool _built;
        private bool _subscribed;

        // Fog-of-war display resolver (Req 14.5, 14.6, 14.9): a framed enemy Unit's shown coordinates
        // come from the VisionSystem, and a hidden-with-no-LKP enemy closes the view.
        private VisionSystem _vision;
        private int _localNationId = -1;

        /// <summary>True while the zoom detail view is showing a Unit.</summary>
        public bool IsVisible => _visible;

        /// <summary>The most recently built view-model for the framed Unit (never null).</summary>
        public InfoPanelViewModel ViewModel { get; private set; } = InfoPanelViewModel.Empty;

        /// <summary>Binds the driver whose tick refreshes the attribute list while visible.</summary>
        public void Bind(SimulationDriver driver)
        {
            UnsubscribeFromDriver();
            _driver = driver;
            if (isActiveAndEnabled)
            {
                SubscribeToDriver();
            }
        }

        /// <summary>
        /// Binds the driver plus the fog-of-war context (Req 14.5, 14.6, 14.9): the
        /// <paramref name="localNationId"/> viewer and <paramref name="visionSystem"/> used to resolve a
        /// framed enemy Unit's shown coordinates (or to close the view when it becomes hidden with no
        /// Last_Known_Position). A null vision system / negative id keeps the pre-fog behaviour.
        /// </summary>
        public void Bind(SimulationDriver driver, int localNationId, VisionSystem visionSystem)
        {
            _localNationId = localNationId;
            _vision = visionSystem;
            Bind(driver);
        }

        // ------------------------------------------------------------------
        // Show / hide (Req 7.3)
        // ------------------------------------------------------------------

        /// <summary>
        /// Activates the zoom detail view for <paramref name="unit"/>, framing the dedicated camera on
        /// its scene object <paramref name="target"/> and populating the full attribute set (Req 7.3).
        /// A null unit hides the view.
        /// </summary>
        public void Show(UnitInstance unit, Transform target)
        {
            if (unit == null)
            {
                Hide();
                return;
            }

            EnsureBuilt();

            _selectedUnitId = unit.Id;
            _target = target;
            _visible = true;

            if (_camera != null)
            {
                _camera.enabled = true;
            }

            if (_root != null)
            {
                _root.style.display = DisplayStyle.Flex;
            }

            FrameTarget();
            RefreshAttributes(unit);
        }

        /// <summary>Hides the zoom detail view and disables the dedicated camera.</summary>
        public void Hide()
        {
            _visible = false;
            _target = null;

            if (_camera != null)
            {
                _camera.enabled = false;
            }

            if (_root != null)
            {
                _root.style.display = DisplayStyle.None;
            }
        }

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        private void OnEnable()
        {
            EnsureBuilt();
            SubscribeToDriver();
            if (!_visible)
            {
                Hide();
            }
        }

        private void OnDisable() => UnsubscribeFromDriver();

        private void OnDestroy()
        {
            if (_camera != null)
            {
                _camera.targetTexture = null;
            }

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }
        }

        private void LateUpdate()
        {
            if (_visible)
            {
                FrameTarget();
            }
        }

        private void SubscribeToDriver()
        {
            if (_driver != null && !_subscribed)
            {
                _driver.Ticked += OnTicked;
                _subscribed = true;
            }
        }

        private void UnsubscribeFromDriver()
        {
            if (_driver != null && _subscribed)
            {
                _driver.Ticked -= OnTicked;
            }

            _subscribed = false;
        }

        private void OnTicked(IReadOnlyList<GameEvent> events)
        {
            if (!_visible)
            {
                return;
            }

            var state = _driver != null ? _driver.State : null;
            if (state != null && state.Units.TryGetValue(_selectedUnitId, out var unit))
            {
                RefreshAttributes(unit);
            }
            else
            {
                // The framed Unit left the Match (e.g. destroyed); close the detail view.
                Hide();
            }
        }

        // ------------------------------------------------------------------
        // Camera framing
        // ------------------------------------------------------------------

        private void FrameTarget()
        {
            if (_camera == null || _target == null)
            {
                return;
            }

            Vector3 focus = _target.position + Vector3.up * _framingHeight;
            _camera.transform.position = focus - _target.forward * _framingDistance;
            _camera.transform.LookAt(focus);
        }

        // ------------------------------------------------------------------
        // UI + resource construction (code-built; no UXML required)
        // ------------------------------------------------------------------

        private void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            EnsureRenderTexture();
            EnsureCamera();

            var uiRoot = _document != null ? _document.rootVisualElement : null;
            if (uiRoot == null)
            {
                // Non-UI parts (camera/texture) are ready; the UI is retried on the next Show/OnEnable.
                return;
            }

            _root = new VisualElement { name = "zoom-detail-root" };
            _root.style.position = Position.Absolute;
            _root.style.right = 0;
            _root.style.top = 0;
            _root.style.flexDirection = FlexDirection.Column;
            _root.style.display = DisplayStyle.None;

            _titleLabel = new Label("Unit") { name = "zoom-detail-title" };
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _root.Add(_titleLabel);

            _preview = new Image { name = "zoom-detail-preview", image = _renderTexture };
            _preview.style.width = _renderTextureSize.x;
            _preview.style.height = _renderTextureSize.y;
            _root.Add(_preview);

            _rows = new VisualElement { name = "zoom-detail-rows" };
            _rows.style.flexDirection = FlexDirection.Column;
            _root.Add(_rows);

            uiRoot.Add(_root);
            _built = true;
        }

        private void EnsureRenderTexture()
        {
            if (_renderTexture != null)
            {
                return;
            }

            int width = Mathf.Max(16, _renderTextureSize.x);
            int height = Mathf.Max(16, _renderTextureSize.y);
            _renderTexture = new RenderTexture(width, height, 16) { name = "ZoomDetailRT" };
            _renderTexture.Create();
        }

        private void EnsureCamera()
        {
            if (_camera == null)
            {
                var cameraObject = new GameObject("ZoomDetailCamera");
                cameraObject.transform.SetParent(transform, false);
                _camera = cameraObject.AddComponent<Camera>();
            }

            _camera.targetTexture = _renderTexture;
            _camera.enabled = _visible;
        }

        // ------------------------------------------------------------------
        // Attribute rendering (Req 7.3) — reuses the info-panel view-model
        // ------------------------------------------------------------------

        private void RefreshAttributes(UnitInstance unit)
        {
            // Resolve the shown position through the fog of war for an enemy Unit (Req 14.5, 14.6, 14.9):
            // current while visible, Last_Known_Position while hidden-with-LKP, and close the view when
            // it is hidden with no recorded LKP.
            WorldPosition? displayOverride = null;
            if (_vision != null && _localNationId >= 0 && unit.OwnerNationId != _localNationId)
            {
                WorldPosition? resolved = _vision.GetDisplayPosition(
                    _localNationId, VisionSystem.UnitKey(unit.Id), unit.Position);
                if (!resolved.HasValue)
                {
                    Hide();
                    return;
                }

                displayOverride = resolved.Value;
            }

            ViewModel = InfoPanelViewModel.ForUnit(unit, displayOverride);

            if (!_built || _rows == null)
            {
                return;
            }

            _titleLabel.text = $"{ViewModel.DisplayName} ({ViewModel.EntityId})";

            _rows.Clear();
            foreach (var attribute in ViewModel.Attributes)
            {
                var row = new VisualElement { name = $"zoom-row-{attribute.Name}" };
                row.style.flexDirection = FlexDirection.Row;
                row.style.justifyContent = Justify.SpaceBetween;

                var nameLabel = new Label(attribute.Name);
                nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                nameLabel.style.marginRight = 8;

                var valueLabel = new Label(attribute.Value);

                row.Add(nameLabel);
                row.Add(valueLabel);
                _rows.Add(row);
            }
        }
    }
}
