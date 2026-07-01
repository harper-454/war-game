using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using EpochWar.Unity.Content;
using EpochWar.Unity.Rendering;

namespace EpochWar.Unity.UI
{
    /// <summary>
    /// The UI Toolkit front end of the Graphics_Settings_System (Req 2.5, 2.6): it binds a control per
    /// setting to a <see cref="GraphicsSettingsViewModel"/>, applies immediate-effect changes to the URP
    /// pipeline / <see cref="QualitySettings"/> / global <see cref="Volume"/> profile at once, and for
    /// restart-required settings persists immediately and shows a restart notice without applying until
    /// the next launch.
    ///
    /// <para><b>Immediate vs restart-required (Req 2.5, 2.6).</b>
    /// <see cref="GraphicsSettingsViewModelExtensions.RequiresRestart"/> classifies each field. Immediate
    /// settings — shadow/texture quality, every Post_Processing_Effect, render/view distance, particle
    /// density, and the preset-driven pipeline swap — are pushed straight to the engine via
    /// <see cref="ApplyImmediateSettings"/>. Restart-required settings — resolution and VSync, which the
    /// platform cannot always hot-swap — are persisted right away and reflected in a restart-notice
    /// element, but the live resolution/VSync is only changed on the next launch by
    /// <see cref="ApplyResolutionAndVSync"/>.</para>
    ///
    /// <para><b>Persistence &amp; fallback (Req 2.7, 2.8).</b> Every change persists through
    /// <see cref="GraphicsSettingsStore"/>. On enable the controller loads the persisted configuration;
    /// if the store had to reset to Low because the saved data was invalid, its one-time
    /// <see cref="GraphicsSettingsStore.SettingsReset"/> notice is surfaced in the reset-notice element.</para>
    ///
    /// <para>Like the other UI controllers in this project, the surface is built entirely in C# (no
    /// authored UXML) so it works with a bare <see cref="UIDocument"/>. All engine-application calls are
    /// null-guarded so the controller degrades gracefully when an optional reference (volume, pipeline
    /// config, camera) is not wired.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class GraphicsSettingsController : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The UIDocument whose rootVisualElement hosts the settings menu. Defaults to the sibling UIDocument.")]
        private UIDocument _document;

        [Header("Content / pipeline")]
        [SerializeField]
        [Tooltip("Optional authored preset bundles. When unset, the built-in default preset table is used.")]
        private GraphicsPresetAsset _presetAsset;

        [SerializeField]
        [Tooltip("The URP pipeline config used to swap the active pipeline per preset (Req 2.2, 2.5).")]
        private UrpPipelineConfig _pipelineConfig;

        [Header("Render targets")]
        [SerializeField]
        [Tooltip("Global post-processing Volume whose profile hosts Bloom/MotionBlur/ColorAdjustments (Req 2.5).")]
        private Volume _postProcessingVolume;

        [SerializeField]
        [Tooltip("The URP renderer feature that implements Ambient Occlusion (SSAO). Toggled on/off (Req 2.5).")]
        private ScriptableRendererFeature _ambientOcclusionFeature;

        [SerializeField]
        [Tooltip("Camera whose far-clip (render/view distance) and anti-aliasing mode are driven by settings.")]
        private Camera _targetCamera;

        [SerializeField]
        [Tooltip("Optional explicit settings file path; defaults to Application.persistentDataPath.")]
        private string _settingsFilePath = string.Empty;

        private IGraphicsPresetTable _presetTable;
        private GraphicsSettingsStore _store;
        private GraphicsSettingsViewModel _viewModel;

        // The resolution/VSync currently live this session; restart-required changes stage new values in
        // the view-model but do not update these until the next launch applies them.
        private ResolutionSetting _appliedResolution;
        private bool _appliedVSync;

        // UI surface
        private VisualElement _root;
        private Label _restartNotice;
        private Label _resetNotice;
        private bool _built;
        private bool _suppressCallbacks;

        /// <summary>The view-model this controller binds to (never null after <see cref="OnEnable"/>).</summary>
        public GraphicsSettingsViewModel ViewModel => _viewModel;

        /// <summary>
        /// The most recently applied particle density (Req 2.1). The VFX/entity-view systems read this to
        /// scale spawned particle counts; exposed statically so those systems need no direct reference to
        /// this controller.
        /// </summary>
        public static float ParticleDensity { get; private set; } = 1f;

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        private void OnEnable()
        {
            _presetTable = _presetAsset != null ? _presetAsset.ToTable() : DefaultGraphicsPresetTable.Instance;
            _store = new GraphicsSettingsStore(
                string.IsNullOrEmpty(_settingsFilePath) ? null : _settingsFilePath, _presetTable);
            _store.SettingsReset += OnSettingsReset;

            // Load persisted config (or fall back to Low on invalid data — Req 2.8), then apply the full
            // configuration for this launch, including the restart-required resolution/VSync (Req 2.6).
            _viewModel = _store.Load();
            EnsureBuilt();
            ApplyAllForLaunch(_viewModel);
            RefreshControls();
        }

        private void OnDisable()
        {
            if (_store != null)
            {
                _store.SettingsReset -= OnSettingsReset;
            }
        }

        private void OnSettingsReset(string notice) => ShowResetNotice(notice);

        // ------------------------------------------------------------------
        // Change handlers (invoked by the UI controls)
        // ------------------------------------------------------------------

        /// <summary>
        /// Applies a Quality_Preset selection (Req 2.2): overwrites every preset-covered field, applies
        /// the immediate settings and pipeline swap at once, persists, and surfaces a restart notice when
        /// the preset's resolution differs from the live one.
        /// </summary>
        public void OnPresetSelected(QualityPreset preset)
        {
            _viewModel.ApplyPreset(preset);
            ApplyImmediateSettings(_viewModel);
            Persist();
            UpdateRestartNotice();
            RefreshControls();
        }

        /// <summary>
        /// Applies a single setting change (Req 2.4). Immediate settings take effect at once (Req 2.5);
        /// restart-required settings (resolution, VSync) are persisted and deferred with a restart notice
        /// (Req 2.6). The change is always persisted (Req 2.7).
        /// </summary>
        public void OnIndividualChange(GraphicsSettingField field, object value)
        {
            _viewModel.ApplyIndividualChange(field, value);

            if (!field.RequiresRestart())
            {
                ApplyImmediateSettings(_viewModel);
            }

            Persist();
            UpdateRestartNotice();
        }

        private void Persist() => _store.Save(_viewModel);

        // ------------------------------------------------------------------
        // Engine application (Req 2.5, 2.6)
        // ------------------------------------------------------------------

        /// <summary>
        /// Applies every immediate-effect setting to the engine at once (Req 2.5): the per-preset URP
        /// pipeline, shadow quality, texture quality, each Post_Processing_Effect, render/view distance,
        /// and particle density. Every step is null-guarded so missing optional references are skipped
        /// rather than throwing.
        /// </summary>
        public void ApplyImmediateSettings(GraphicsSettingsViewModel vm)
        {
            if (vm == null)
            {
                return;
            }

            _pipelineConfig?.ApplyPipelineForPreset(vm.Preset);

            ApplyShadowQuality(vm.ShadowQuality);
            ApplyTextureQuality(vm.TextureQuality);
            ApplyPostProcessing(vm);
            ApplyRenderViewDistance(vm.RenderViewDistance);
            ParticleDensity = vm.ParticleDensity;
        }

        /// <summary>
        /// Applies the full configuration for the current launch, including the restart-required
        /// resolution and VSync, and records them as the live values (Req 2.6). Called once on enable.
        /// </summary>
        public void ApplyAllForLaunch(GraphicsSettingsViewModel vm)
        {
            if (vm == null)
            {
                return;
            }

            ApplyImmediateSettings(vm);
            ApplyResolutionAndVSync(vm);
            _appliedResolution = vm.Resolution;
            _appliedVSync = vm.VSync;
        }

        private void ApplyShadowQuality(QualityPreset level)
        {
            QualitySettings.shadowResolution = ToShadowResolution(level);
            QualitySettings.shadowDistance = ToShadowDistance(level);

            if (QualitySettings.renderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.shadowDistance = ToShadowDistance(level);
            }
            else if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset activeUrp)
            {
                activeUrp.shadowDistance = ToShadowDistance(level);
            }
        }

        private static void ApplyTextureQuality(QualityPreset level)
        {
            // globalTextureMipmapLimit: 0 = full resolution, higher = progressively lower resolution.
            QualitySettings.globalTextureMipmapLimit = ToMipmapLimit(level);
        }

        private void ApplyPostProcessing(GraphicsSettingsViewModel vm)
        {
            VolumeProfile profile = _postProcessingVolume != null ? _postProcessingVolume.sharedProfile : null;
            if (profile != null)
            {
                if (profile.TryGet(out Bloom bloom))
                {
                    bloom.active = vm.Bloom;
                }

                if (profile.TryGet(out MotionBlur motionBlur))
                {
                    motionBlur.active = vm.MotionBlur;
                }

                if (profile.TryGet(out ColorAdjustments colorAdjustments))
                {
                    colorAdjustments.active = vm.ColorGrading;
                }
            }

            // Ambient Occlusion (SSAO) is a URP renderer feature, not a volume override.
            _ambientOcclusionFeature?.SetActive(vm.AmbientOcclusion);

            // Anti-aliasing is a per-camera URP setting.
            if (_targetCamera != null)
            {
                UniversalAdditionalCameraData cameraData = _targetCamera.GetUniversalAdditionalCameraData();
                if (cameraData != null)
                {
                    cameraData.antialiasing = vm.AntiAliasing
                        ? AntialiasingMode.SubpixelMorphologicalAntiAliasing
                        : AntialiasingMode.None;
                }
            }
        }

        private void ApplyRenderViewDistance(float distance)
        {
            if (_targetCamera != null)
            {
                _targetCamera.farClipPlane = Mathf.Max(_targetCamera.nearClipPlane + 0.01f, distance);
            }
        }

        /// <summary>
        /// Applies the restart-required resolution and VSync to the platform (Req 2.6). Called only on
        /// launch (from <see cref="ApplyAllForLaunch"/>), never on an in-session change, so a change to
        /// these settings takes effect on the next launch.
        /// </summary>
        public void ApplyResolutionAndVSync(GraphicsSettingsViewModel vm)
        {
            if (vm == null)
            {
                return;
            }

            QualitySettings.vSyncCount = vm.VSync ? 1 : 0;

            ResolutionSetting r = vm.Resolution;
            if (r.IsValid)
            {
#if UNITY_2022_2_OR_NEWER
                Screen.SetResolution(r.Width, r.Height, Screen.fullScreenMode, new RefreshRate
                {
                    numerator = (uint)Mathf.Max(1, r.RefreshRateHz),
                    denominator = 1u,
                });
#else
                Screen.SetResolution(r.Width, r.Height, Screen.fullScreenMode, Mathf.Max(1, r.RefreshRateHz));
#endif
            }
        }

        // ------------------------------------------------------------------
        // Setting → engine value mappings
        // ------------------------------------------------------------------

        private static ShadowResolution ToShadowResolution(QualityPreset level)
        {
            switch (level)
            {
                case QualityPreset.Ultra: return ShadowResolution.VeryHigh;
                case QualityPreset.High: return ShadowResolution.High;
                case QualityPreset.Medium: return ShadowResolution.Medium;
                default: return ShadowResolution.Low;
            }
        }

        private static float ToShadowDistance(QualityPreset level)
        {
            switch (level)
            {
                case QualityPreset.Ultra: return 200f;
                case QualityPreset.High: return 150f;
                case QualityPreset.Medium: return 100f;
                default: return 50f;
            }
        }

        private static int ToMipmapLimit(QualityPreset level)
        {
            switch (level)
            {
                case QualityPreset.Ultra: return 0;
                case QualityPreset.High: return 0;
                case QualityPreset.Medium: return 1;
                default: return 2;
            }
        }

        // ------------------------------------------------------------------
        // Restart / reset notices (Req 2.6, 2.8)
        // ------------------------------------------------------------------

        private void UpdateRestartNotice()
        {
            bool restartNeeded = !_viewModel.Resolution.Equals(_appliedResolution)
                                 || _viewModel.VSync != _appliedVSync;

            if (_restartNotice != null)
            {
                _restartNotice.text = restartNeeded
                    ? "Some changes (resolution, VSync) will take effect the next time you launch the game."
                    : string.Empty;
                _restartNotice.style.display = restartNeeded ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void ShowResetNotice(string notice)
        {
            if (_resetNotice != null)
            {
                _resetNotice.text = notice ?? string.Empty;
                _resetNotice.style.display = string.IsNullOrEmpty(notice) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        // ------------------------------------------------------------------
        // UI construction (code-built; no UXML required)
        // ------------------------------------------------------------------

        private void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            if (_document == null)
            {
                _document = GetComponent<UIDocument>();
            }

            VisualElement uiRoot = _document != null ? _document.rootVisualElement : null;
            if (uiRoot == null)
            {
                return;
            }

            _root = new VisualElement { name = "graphics-settings-root" };
            _root.style.position = Position.Absolute;
            _root.style.left = 0;
            _root.style.top = 0;
            _root.style.minWidth = 320;
            _root.style.flexDirection = FlexDirection.Column;

            var title = new Label("Graphics Settings") { name = "graphics-settings-title" };
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 18;
            title.style.marginBottom = 8;
            _root.Add(title);

            // Preset (Req 2.2)
            var presetField = new EnumField("Quality Preset", QualityPreset.Low) { name = "gs-preset" };
            presetField.RegisterValueChangedCallback(evt =>
            {
                if (!_suppressCallbacks)
                {
                    OnPresetSelected((QualityPreset)evt.newValue);
                }
            });
            _root.Add(presetField);

            // Shadow quality (Req 2.1)
            AddEnumRow("gs-shadow", "Shadow Quality", QualityPreset.Low, GraphicsSettingField.ShadowQuality);

            // Texture quality (Req 2.1)
            AddEnumRow("gs-texture", "Texture Quality", QualityPreset.Low, GraphicsSettingField.TextureQuality);

            // Post-processing toggles (Req 2.1)
            AddToggleRow("gs-bloom", "Bloom", GraphicsSettingField.Bloom);
            AddToggleRow("gs-ao", "Ambient Occlusion", GraphicsSettingField.AmbientOcclusion);
            AddToggleRow("gs-motionblur", "Motion Blur", GraphicsSettingField.MotionBlur);
            AddToggleRow("gs-colorgrading", "Color Grading", GraphicsSettingField.ColorGrading);
            AddToggleRow("gs-antialiasing", "Anti-Aliasing", GraphicsSettingField.AntiAliasing);

            // VSync (restart-required, Req 2.6)
            AddToggleRow("gs-vsync", "VSync", GraphicsSettingField.VSync);

            // Render/view distance (Req 2.1) — bounds come from the preset table.
            AddSliderRow(
                "gs-render-distance", "Render Distance", GraphicsSettingField.RenderViewDistance,
                _viewModel.MinBound(v => v.RenderViewDistance), _viewModel.MaxBound(v => v.RenderViewDistance));

            // Particle density (Req 2.1)
            AddSliderRow(
                "gs-particle-density", "Particle Density", GraphicsSettingField.ParticleDensity,
                _viewModel.MinBound(v => v.ParticleDensity), _viewModel.MaxBound(v => v.ParticleDensity));

            // Resolution (restart-required, Req 2.6) — a compact width x height x hz entry.
            AddResolutionRow();

            _restartNotice = new Label(string.Empty) { name = "gs-restart-notice" };
            _restartNotice.style.display = DisplayStyle.None;
            _restartNotice.style.marginTop = 8;
            _restartNotice.style.whiteSpace = WhiteSpace.Normal;
            _root.Add(_restartNotice);

            _resetNotice = new Label(string.Empty) { name = "gs-reset-notice" };
            _resetNotice.style.display = DisplayStyle.None;
            _resetNotice.style.marginTop = 4;
            _resetNotice.style.whiteSpace = WhiteSpace.Normal;
            _root.Add(_resetNotice);

            uiRoot.Add(_root);
            _built = true;
        }

        private void AddEnumRow(string id, string caption, QualityPreset initial, GraphicsSettingField field)
        {
            var row = NewRow(id);
            var enumField = new EnumField(caption, initial) { name = $"{id}-value" };
            enumField.style.flexGrow = 1f;
            enumField.RegisterValueChangedCallback(evt =>
            {
                if (!_suppressCallbacks)
                {
                    OnIndividualChange(field, (QualityPreset)evt.newValue);
                }
            });
            row.Add(enumField);
            _root.Add(row);
        }

        private void AddToggleRow(string id, string caption, GraphicsSettingField field)
        {
            var row = NewRow(id);
            var toggle = new Toggle(caption) { name = $"{id}-value" };
            toggle.style.flexGrow = 1f;
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (!_suppressCallbacks)
                {
                    OnIndividualChange(field, evt.newValue);
                }
            });
            row.Add(toggle);
            _root.Add(row);
        }

        private void AddSliderRow(string id, string caption, GraphicsSettingField field, float min, float max)
        {
            var row = NewRow(id);
            var slider = new Slider(caption, min, max) { name = $"{id}-value" };
            slider.style.flexGrow = 1f;
            slider.RegisterValueChangedCallback(evt =>
            {
                if (!_suppressCallbacks)
                {
                    OnIndividualChange(field, evt.newValue);
                }
            });
            row.Add(slider);
            _root.Add(row);
        }

        private void AddResolutionRow()
        {
            var row = NewRow("gs-resolution");

            var caption = new Label("Resolution");
            caption.style.marginRight = 8;
            row.Add(caption);

            var widthField = new IntegerField { name = "gs-resolution-width", value = _viewModel.Resolution.Width };
            var heightField = new IntegerField { name = "gs-resolution-height", value = _viewModel.Resolution.Height };
            var hzField = new IntegerField { name = "gs-resolution-hz", value = _viewModel.Resolution.RefreshRateHz };

            void Commit()
            {
                if (_suppressCallbacks)
                {
                    return;
                }

                var resolution = new ResolutionSetting(widthField.value, heightField.value, hzField.value);
                if (resolution.IsValid)
                {
                    OnIndividualChange(GraphicsSettingField.Resolution, resolution);
                }
            }

            widthField.RegisterValueChangedCallback(_ => Commit());
            heightField.RegisterValueChangedCallback(_ => Commit());
            hzField.RegisterValueChangedCallback(_ => Commit());

            row.Add(widthField);
            row.Add(new Label("x"));
            row.Add(heightField);
            row.Add(new Label("@"));
            row.Add(hzField);
            row.Add(new Label("Hz"));
            _root.Add(row);
        }

        private static VisualElement NewRow(string id)
        {
            var row = new VisualElement { name = id };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginBottom = 4;
            return row;
        }

        /// <summary>
        /// Pushes the current view-model values into the UI controls without re-triggering change
        /// callbacks (used after a preset overwrites multiple fields, and on initial load).
        /// </summary>
        private void RefreshControls()
        {
            if (!_built || _root == null)
            {
                return;
            }

            _suppressCallbacks = true;
            try
            {
                SetEnum("gs-preset", _viewModel.Preset);
                SetEnum("gs-shadow-value", _viewModel.ShadowQuality);
                SetEnum("gs-texture-value", _viewModel.TextureQuality);
                SetToggle("gs-bloom-value", _viewModel.Bloom);
                SetToggle("gs-ao-value", _viewModel.AmbientOcclusion);
                SetToggle("gs-motionblur-value", _viewModel.MotionBlur);
                SetToggle("gs-colorgrading-value", _viewModel.ColorGrading);
                SetToggle("gs-antialiasing-value", _viewModel.AntiAliasing);
                SetToggle("gs-vsync-value", _viewModel.VSync);
                SetSlider("gs-render-distance-value", _viewModel.RenderViewDistance);
                SetSlider("gs-particle-density-value", _viewModel.ParticleDensity);
                SetInt("gs-resolution-width", _viewModel.Resolution.Width);
                SetInt("gs-resolution-height", _viewModel.Resolution.Height);
                SetInt("gs-resolution-hz", _viewModel.Resolution.RefreshRateHz);
            }
            finally
            {
                _suppressCallbacks = false;
            }

            UpdateRestartNotice();
        }

        private void SetEnum(string name, Enum value) => _root.Q<EnumField>(name)?.SetValueWithoutNotify(value);

        private void SetToggle(string name, bool value) => _root.Q<Toggle>(name)?.SetValueWithoutNotify(value);

        private void SetSlider(string name, float value) => _root.Q<Slider>(name)?.SetValueWithoutNotify(value);

        private void SetInt(string name, int value) => _root.Q<IntegerField>(name)?.SetValueWithoutNotify(value);
    }
}
