using System;

namespace EpochWar.Unity.UI
{
    /// <summary>
    /// The ordered, named graphics configuration bundle scale (Req 2.1, 2.3). The enum order is the
    /// authoritative quality/cost order: each successive value is defined to apply settings that are
    /// no lower in visual quality and no lower in rendering cost than the preceding value, with
    /// <see cref="Low"/> the minimum supported values and <see cref="Ultra"/> the maximum values
    /// supported on high-end PC hardware. This same four-level scale is reused for the individually
    /// selectable shadow-quality and texture-quality settings (Req 2.1).
    /// </summary>
    public enum QualityPreset
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Ultra = 3,
    }

    /// <summary>
    /// Identifies exactly one configurable graphics setting (Req 2.1). Used by
    /// <see cref="GraphicsSettingsViewModel.ApplyIndividualChange"/> to mutate a single field, and by
    /// <see cref="GraphicsSettingsViewModelExtensions.RequiresRestart"/> to distinguish
    /// immediate-effect settings (Req 2.5) from settings that only take effect on the next launch
    /// (Req 2.6).
    /// </summary>
    public enum GraphicsSettingField
    {
        Preset = 0,
        Resolution = 1,
        ShadowQuality = 2,
        Bloom = 3,
        AmbientOcclusion = 4,
        MotionBlur = 5,
        ColorGrading = 6,
        AntiAliasing = 7,
        VSync = 8,
        RenderViewDistance = 9,
        TextureQuality = 10,
        ParticleDensity = 11,
    }

    /// <summary>
    /// An engine-free description of a screen resolution (width/height in pixels and refresh rate in
    /// Hz). Kept free of <c>UnityEngine.Resolution</c> so the view-model and preset data remain
    /// testable and serializable without the engine. The presentation layer converts this to a
    /// platform resolution when it is (re)applied.
    /// </summary>
    [Serializable]
    public struct ResolutionSetting : IEquatable<ResolutionSetting>
    {
        public int Width;
        public int Height;
        public int RefreshRateHz;

        public ResolutionSetting(int width, int height, int refreshRateHz)
        {
            Width = width;
            Height = height;
            RefreshRateHz = refreshRateHz;
        }

        /// <summary>True when width, height, and refresh rate are all strictly positive.</summary>
        public bool IsValid => Width > 0 && Height > 0 && RefreshRateHz > 0;

        public bool Equals(ResolutionSetting other)
            => Width == other.Width && Height == other.Height && RefreshRateHz == other.RefreshRateHz;

        public override bool Equals(object obj) => obj is ResolutionSetting other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + Width;
                hash = (hash * 31) + Height;
                hash = (hash * 31) + RefreshRateHz;
                return hash;
            }
        }

        public override string ToString() => $"{Width}x{Height}@{RefreshRateHz}Hz";
    }

    /// <summary>
    /// The immutable set of values a single <see cref="QualityPreset"/> applies (Req 2.2, 2.3). These
    /// are exactly the settings a preset overwrites: resolution, shadow quality, every
    /// Post_Processing_Effect, render/view distance, texture quality, and particle density. VSync and
    /// the current preset selection are intentionally <em>not</em> part of a preset bundle (per Req 2.2
    /// the preset does not set VSync), so they are held only on the view-model.
    ///
    /// This is a plain, engine-free data holder (no <c>UnityEngine</c> dependency) authored either by
    /// the built-in <see cref="DefaultGraphicsPresetTable"/> or by a content author through
    /// <c>GraphicsPresetAsset</c>.
    /// </summary>
    public sealed class GraphicsPresetValues
    {
        public ResolutionSetting Resolution { get; }
        public QualityPreset ShadowQuality { get; }
        public bool Bloom { get; }
        public bool AmbientOcclusion { get; }
        public bool MotionBlur { get; }
        public bool ColorGrading { get; }
        public bool AntiAliasing { get; }
        public float RenderViewDistance { get; }
        public QualityPreset TextureQuality { get; }
        public float ParticleDensity { get; }

        public GraphicsPresetValues(
            ResolutionSetting resolution,
            QualityPreset shadowQuality,
            bool bloom,
            bool ambientOcclusion,
            bool motionBlur,
            bool colorGrading,
            bool antiAliasing,
            float renderViewDistance,
            QualityPreset textureQuality,
            float particleDensity)
        {
            Resolution = resolution;
            ShadowQuality = shadowQuality;
            Bloom = bloom;
            AmbientOcclusion = ambientOcclusion;
            MotionBlur = motionBlur;
            ColorGrading = colorGrading;
            AntiAliasing = antiAliasing;
            RenderViewDistance = renderViewDistance;
            TextureQuality = textureQuality;
            ParticleDensity = particleDensity;
        }
    }

    /// <summary>
    /// A lookup from <see cref="QualityPreset"/> to the concrete <see cref="GraphicsPresetValues"/> it
    /// applies. Implemented by the built-in <see cref="DefaultGraphicsPresetTable"/> (used as the
    /// engine-free fallback and for tests) and by <c>GraphicsPresetAsset.ToTable()</c> (authored
    /// values). Kept engine-free so <see cref="GraphicsSettingsViewModel"/> stays testable.
    /// </summary>
    public interface IGraphicsPresetTable
    {
        /// <summary>Returns the values for <paramref name="preset"/>; never returns null.</summary>
        GraphicsPresetValues Get(QualityPreset preset);
    }

    /// <summary>
    /// An <see cref="IGraphicsPresetTable"/> backed by four explicit <see cref="GraphicsPresetValues"/>
    /// bundles. An unrecognised enum value falls back to the <see cref="QualityPreset.Low"/> bundle so
    /// the table is total.
    /// </summary>
    public sealed class InMemoryGraphicsPresetTable : IGraphicsPresetTable
    {
        private readonly GraphicsPresetValues _low;
        private readonly GraphicsPresetValues _medium;
        private readonly GraphicsPresetValues _high;
        private readonly GraphicsPresetValues _ultra;

        public InMemoryGraphicsPresetTable(
            GraphicsPresetValues low,
            GraphicsPresetValues medium,
            GraphicsPresetValues high,
            GraphicsPresetValues ultra)
        {
            _low = low ?? throw new ArgumentNullException(nameof(low));
            _medium = medium ?? throw new ArgumentNullException(nameof(medium));
            _high = high ?? throw new ArgumentNullException(nameof(high));
            _ultra = ultra ?? throw new ArgumentNullException(nameof(ultra));
        }

        /// <inheritdoc />
        public GraphicsPresetValues Get(QualityPreset preset)
        {
            switch (preset)
            {
                case QualityPreset.Ultra: return _ultra;
                case QualityPreset.High: return _high;
                case QualityPreset.Medium: return _medium;
                default: return _low;
            }
        }
    }

    /// <summary>
    /// The built-in preset table used when no authored <c>GraphicsPresetAsset</c> is supplied and by
    /// the startup fallback (Req 2.8). Its four bundles satisfy the monotonic quality/cost ordering of
    /// Req 2.3: every setting is non-decreasing in quality/cost from Low → Medium → High → Ultra, with
    /// Low the minimum values and Ultra the maximum. Exposed as a singleton so callers share one
    /// immutable instance.
    /// </summary>
    public static class DefaultGraphicsPresetTable
    {
        /// <summary>The shared built-in preset table instance.</summary>
        public static readonly IGraphicsPresetTable Instance = Build();

        private static IGraphicsPresetTable Build()
        {
            var low = new GraphicsPresetValues(
                resolution: new ResolutionSetting(1280, 720, 60),
                shadowQuality: QualityPreset.Low,
                bloom: false,
                ambientOcclusion: false,
                motionBlur: false,
                colorGrading: false,
                antiAliasing: false,
                renderViewDistance: 100f,
                textureQuality: QualityPreset.Low,
                particleDensity: 0.25f);

            var medium = new GraphicsPresetValues(
                resolution: new ResolutionSetting(1600, 900, 60),
                shadowQuality: QualityPreset.Medium,
                bloom: true,
                ambientOcclusion: false,
                motionBlur: false,
                colorGrading: true,
                antiAliasing: false,
                renderViewDistance: 200f,
                textureQuality: QualityPreset.Medium,
                particleDensity: 0.5f);

            var high = new GraphicsPresetValues(
                resolution: new ResolutionSetting(1920, 1080, 60),
                shadowQuality: QualityPreset.High,
                bloom: true,
                ambientOcclusion: true,
                motionBlur: false,
                colorGrading: true,
                antiAliasing: true,
                renderViewDistance: 350f,
                textureQuality: QualityPreset.High,
                particleDensity: 0.75f);

            var ultra = new GraphicsPresetValues(
                resolution: new ResolutionSetting(2560, 1440, 60),
                shadowQuality: QualityPreset.Ultra,
                bloom: true,
                ambientOcclusion: true,
                motionBlur: true,
                colorGrading: true,
                antiAliasing: true,
                renderViewDistance: 500f,
                textureQuality: QualityPreset.Ultra,
                particleDensity: 1f);

            return new InMemoryGraphicsPresetTable(low, medium, high, ultra);
        }
    }

    /// <summary>
    /// The engine-light, testable in-memory model of the Player's current graphics configuration
    /// (Req 2.1-2.4), following the same "pure view-model" convention as
    /// <see cref="InfoPanelViewModel"/>: it holds no <c>UnityEngine</c> dependency, so its
    /// preset-application and individual-change logic can be verified directly without a Play loop
    /// (the design classifies Req 2 as INTEGRATION, but keeping this class pure lets the core logic be
    /// unit-checked in EditMode).
    ///
    /// <para>The view-model holds the current value for every setting exposed by the
    /// Graphics_Settings_System (Req 2.1): the selected <see cref="Preset"/>, <see cref="Resolution"/>,
    /// <see cref="ShadowQuality"/>, each individual Post_Processing_Effect
    /// (<see cref="Bloom"/>/<see cref="AmbientOcclusion"/>/<see cref="MotionBlur"/>/<see cref="ColorGrading"/>/<see cref="AntiAliasing"/>),
    /// <see cref="VSync"/>, <see cref="RenderViewDistance"/>, <see cref="TextureQuality"/>, and
    /// <see cref="ParticleDensity"/>.</para>
    ///
    /// <para><see cref="ApplyPreset"/> overwrites every preset-covered field from the bound
    /// <see cref="IGraphicsPresetTable"/> (Req 2.2), while <see cref="ApplyIndividualChange"/> mutates
    /// exactly one field and leaves the others — including those a preset set — untouched (Req 2.4).
    /// The <see cref="GraphicsSettingsController"/> observes the mutations and applies them to the
    /// engine; the <c>GraphicsSettingsStore</c> serializes this model to disk.</para>
    /// </summary>
    public sealed class GraphicsSettingsViewModel
    {
        private readonly IGraphicsPresetTable _presetTable;

        /// <summary>The currently selected Quality_Preset (the last preset the Player chose).</summary>
        public QualityPreset Preset { get; private set; }

        /// <summary>The current screen resolution setting (restart-required, Req 2.6).</summary>
        public ResolutionSetting Resolution { get; private set; }

        /// <summary>The current shadow quality on the four-level scale (Req 2.1).</summary>
        public QualityPreset ShadowQuality { get; private set; }

        /// <summary>Whether the Bloom Post_Processing_Effect is enabled.</summary>
        public bool Bloom { get; private set; }

        /// <summary>Whether the Ambient_Occlusion Post_Processing_Effect is enabled.</summary>
        public bool AmbientOcclusion { get; private set; }

        /// <summary>Whether the Motion_Blur Post_Processing_Effect is enabled.</summary>
        public bool MotionBlur { get; private set; }

        /// <summary>Whether the Color_Grading Post_Processing_Effect is enabled.</summary>
        public bool ColorGrading { get; private set; }

        /// <summary>Whether the Anti_Aliasing Post_Processing_Effect is enabled.</summary>
        public bool AntiAliasing { get; private set; }

        /// <summary>Whether vertical sync is enabled (restart-required, Req 2.6).</summary>
        public bool VSync { get; private set; }

        /// <summary>The current render/view distance, bounded by the Low and Ultra preset values (Req 2.1).</summary>
        public float RenderViewDistance { get; private set; }

        /// <summary>The current texture quality on the four-level scale (Req 2.1).</summary>
        public QualityPreset TextureQuality { get; private set; }

        /// <summary>The current particle density, bounded by the Low and Ultra preset values (Req 2.1).</summary>
        public float ParticleDensity { get; private set; }

        /// <summary>The preset table this view-model reads bundles and min/max bounds from.</summary>
        public IGraphicsPresetTable PresetTable => _presetTable;

        /// <summary>
        /// Creates a view-model bound to <paramref name="presetTable"/> (defaults to the built-in
        /// <see cref="DefaultGraphicsPresetTable"/>) and initialised to the given
        /// <paramref name="initialPreset"/> (Low by default). VSync starts disabled — it is not a
        /// preset-covered field.
        /// </summary>
        public GraphicsSettingsViewModel(
            IGraphicsPresetTable presetTable = null,
            QualityPreset initialPreset = QualityPreset.Low)
        {
            _presetTable = presetTable ?? DefaultGraphicsPresetTable.Instance;
            VSync = false;
            ApplyPreset(initialPreset);
        }

        /// <summary>
        /// Overwrites every preset-covered field — resolution, shadow quality, all Post_Processing_Effects,
        /// render/view distance, texture quality, and particle density — with the values defined for
        /// <paramref name="preset"/>, and records <paramref name="preset"/> as the current selection.
        /// Any previously applied individual changes to those fields are discarded (Req 2.2, 2.3).
        /// VSync is intentionally left unchanged, as it is not part of a preset bundle (Req 2.2).
        /// </summary>
        public void ApplyPreset(QualityPreset preset)
        {
            GraphicsPresetValues values = _presetTable.Get(preset);

            Preset = preset;
            Resolution = values.Resolution;
            ShadowQuality = values.ShadowQuality;
            Bloom = values.Bloom;
            AmbientOcclusion = values.AmbientOcclusion;
            MotionBlur = values.MotionBlur;
            ColorGrading = values.ColorGrading;
            AntiAliasing = values.AntiAliasing;
            RenderViewDistance = values.RenderViewDistance;
            TextureQuality = values.TextureQuality;
            ParticleDensity = values.ParticleDensity;
        }

        /// <summary>
        /// Mutates exactly one setting, identified by <paramref name="field"/>, to
        /// <paramref name="value"/>, leaving every other field untouched — including fields a previously
        /// applied <see cref="ApplyPreset"/> set (Req 2.4). Numeric fields
        /// (<see cref="RenderViewDistance"/>, <see cref="ParticleDensity"/>) are clamped to the
        /// <see cref="QualityPreset.Low"/>..<see cref="QualityPreset.Ultra"/> bounds defined by the preset
        /// table (Req 2.1). Passing <see cref="GraphicsSettingField.Preset"/> is treated as a preset
        /// selection and delegates to <see cref="ApplyPreset"/> (which by definition overwrites the
        /// preset-covered fields).
        /// </summary>
        /// <param name="field">Which single setting to change.</param>
        /// <param name="value">
        /// The new value, of the type matching <paramref name="field"/>: a
        /// <see cref="ResolutionSetting"/> for <see cref="GraphicsSettingField.Resolution"/>, a
        /// <see cref="QualityPreset"/> for the shadow/texture/preset fields, a <see cref="bool"/> for the
        /// Post_Processing_Effect toggles and VSync, and a <see cref="float"/> (or any convertible
        /// numeric) for the render/view distance and particle density.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="value"/> is the wrong type for <paramref name="field"/>.</exception>
        public void ApplyIndividualChange(GraphicsSettingField field, object value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            switch (field)
            {
                case GraphicsSettingField.Preset:
                    ApplyPreset(ToPreset(value, field));
                    break;

                case GraphicsSettingField.Resolution:
                    if (!(value is ResolutionSetting resolution))
                    {
                        throw WrongType(field, "ResolutionSetting");
                    }

                    Resolution = resolution;
                    break;

                case GraphicsSettingField.ShadowQuality:
                    ShadowQuality = ToPreset(value, field);
                    break;

                case GraphicsSettingField.TextureQuality:
                    TextureQuality = ToPreset(value, field);
                    break;

                case GraphicsSettingField.Bloom:
                    Bloom = ToBool(value, field);
                    break;

                case GraphicsSettingField.AmbientOcclusion:
                    AmbientOcclusion = ToBool(value, field);
                    break;

                case GraphicsSettingField.MotionBlur:
                    MotionBlur = ToBool(value, field);
                    break;

                case GraphicsSettingField.ColorGrading:
                    ColorGrading = ToBool(value, field);
                    break;

                case GraphicsSettingField.AntiAliasing:
                    AntiAliasing = ToBool(value, field);
                    break;

                case GraphicsSettingField.VSync:
                    VSync = ToBool(value, field);
                    break;

                case GraphicsSettingField.RenderViewDistance:
                    RenderViewDistance = ClampToBounds(ToFloat(value, field), v => v.RenderViewDistance);
                    break;

                case GraphicsSettingField.ParticleDensity:
                    ParticleDensity = ClampToBounds(ToFloat(value, field), v => v.ParticleDensity);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown graphics setting field.");
            }
        }

        // ------------------------------------------------------------------
        // Strongly-typed convenience mutators (each is a single-field change)
        // ------------------------------------------------------------------

        /// <summary>Sets the resolution (restart-required, Req 2.6) without touching other fields.</summary>
        public void SetResolution(ResolutionSetting resolution)
            => ApplyIndividualChange(GraphicsSettingField.Resolution, resolution);

        /// <summary>Sets VSync (restart-required, Req 2.6) without touching other fields.</summary>
        public void SetVSync(bool enabled) => ApplyIndividualChange(GraphicsSettingField.VSync, enabled);

        /// <summary>Sets shadow quality without touching other fields.</summary>
        public void SetShadowQuality(QualityPreset level)
            => ApplyIndividualChange(GraphicsSettingField.ShadowQuality, level);

        /// <summary>Sets texture quality without touching other fields.</summary>
        public void SetTextureQuality(QualityPreset level)
            => ApplyIndividualChange(GraphicsSettingField.TextureQuality, level);

        /// <summary>Sets a Post_Processing_Effect toggle without touching other fields.</summary>
        public void SetPostProcessingEffect(GraphicsSettingField effect, bool enabled)
        {
            switch (effect)
            {
                case GraphicsSettingField.Bloom:
                case GraphicsSettingField.AmbientOcclusion:
                case GraphicsSettingField.MotionBlur:
                case GraphicsSettingField.ColorGrading:
                case GraphicsSettingField.AntiAliasing:
                    ApplyIndividualChange(effect, enabled);
                    break;
                default:
                    throw new ArgumentException($"{effect} is not a Post_Processing_Effect toggle.", nameof(effect));
            }
        }

        /// <summary>Sets the render/view distance (clamped to bounds) without touching other fields.</summary>
        public void SetRenderViewDistance(float distance)
            => ApplyIndividualChange(GraphicsSettingField.RenderViewDistance, distance);

        /// <summary>Sets the particle density (clamped to bounds) without touching other fields.</summary>
        public void SetParticleDensity(float density)
            => ApplyIndividualChange(GraphicsSettingField.ParticleDensity, density);

        // ------------------------------------------------------------------
        // Bounds / validation helpers
        // ------------------------------------------------------------------

        /// <summary>The minimum (Low-preset) value for a bounded numeric field (Req 2.1).</summary>
        public float MinBound(Func<GraphicsPresetValues, float> selector)
            => selector(_presetTable.Get(QualityPreset.Low));

        /// <summary>The maximum (Ultra-preset) value for a bounded numeric field (Req 2.1).</summary>
        public float MaxBound(Func<GraphicsPresetValues, float> selector)
            => selector(_presetTable.Get(QualityPreset.Ultra));

        private float ClampToBounds(float value, Func<GraphicsPresetValues, float> selector)
        {
            float min = MinBound(selector);
            float max = MaxBound(selector);
            if (min > max)
            {
                (min, max) = (max, min);
            }

            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        private static QualityPreset ToPreset(object value, GraphicsSettingField field)
        {
            if (value is QualityPreset preset)
            {
                return preset;
            }

            throw WrongType(field, "QualityPreset");
        }

        private static bool ToBool(object value, GraphicsSettingField field)
        {
            try
            {
                return Convert.ToBoolean(value);
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is FormatException)
            {
                throw WrongType(field, "bool");
            }
        }

        private static float ToFloat(object value, GraphicsSettingField field)
        {
            try
            {
                return Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is FormatException)
            {
                throw WrongType(field, "float");
            }
        }

        private static ArgumentException WrongType(GraphicsSettingField field, string expected)
            => new ArgumentException($"Setting '{field}' expects a value of type {expected}.", nameof(field));
    }

    /// <summary>
    /// Static helpers describing how each <see cref="GraphicsSettingField"/> is applied. Kept separate
    /// from the view-model so the classification is shared by the controller (which decides between the
    /// immediate-effect path of Req 2.5 and the persist-and-defer path of Req 2.6) and any test.
    /// </summary>
    public static class GraphicsSettingsViewModelExtensions
    {
        /// <summary>
        /// True when a change to <paramref name="field"/> requires an application restart to take effect
        /// (Req 2.6). Resolution and VSync cannot be hot-swapped on every target platform, so they are
        /// persisted immediately and deferred to the next launch; every other setting is applied
        /// immediately (Req 2.5).
        /// </summary>
        public static bool RequiresRestart(this GraphicsSettingField field)
            => field == GraphicsSettingField.Resolution || field == GraphicsSettingField.VSync;
    }
}
