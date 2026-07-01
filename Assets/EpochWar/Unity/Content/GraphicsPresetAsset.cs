using System;
using UnityEngine;
using EpochWar.Unity.UI;

namespace EpochWar.Unity.Content
{
    /// <summary>
    /// A ScriptableObject authoring bundle that defines the Low/Medium/High/Ultra
    /// <see cref="QualityPreset"/> values consumed by <see cref="GraphicsSettingsViewModel.ApplyPreset"/>
    /// (Req 2.2, 2.3, 8.1).
    ///
    /// <para>A designer authors one asset holding the four ordered preset blocks; the graphics settings
    /// UI builds a <see cref="GraphicsSettingsViewModel"/> from this asset's <see cref="ToTable"/> so
    /// selecting a preset overwrites every preset-covered field with the authored values. Following the
    /// same authoring→engine-free-POCO convention as <see cref="UnitAsset"/>/<see cref="StructureAsset"/>,
    /// this asset exposes no <c>UnityEngine</c> type to the settings logic — <see cref="ToTable"/>
    /// produces the plain <see cref="IGraphicsPresetTable"/> the view-model consumes.</para>
    ///
    /// <para>The design's Req 2.3 monotonicity constraint (each successive preset is no lower in quality
    /// or cost) is an authoring responsibility; <see cref="Validate"/> is provided so an author can be
    /// warned in the Editor when a block breaks the non-decreasing render/view-distance or
    /// particle-density ordering, and the built-in <see cref="DefaultGraphicsPresetTable"/> is always a
    /// valid reference to copy from.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "EpochWar/Graphics Preset", fileName = "GraphicsPresets")]
    public sealed class GraphicsPresetAsset : ScriptableObject
    {
        [Tooltip("Values applied by the Low Quality_Preset (the minimum supported values, Req 2.3).")]
        [SerializeField] private PresetBlock _low = PresetBlock.Defaults(QualityPreset.Low);

        [Tooltip("Values applied by the Medium Quality_Preset.")]
        [SerializeField] private PresetBlock _medium = PresetBlock.Defaults(QualityPreset.Medium);

        [Tooltip("Values applied by the High Quality_Preset.")]
        [SerializeField] private PresetBlock _high = PresetBlock.Defaults(QualityPreset.High);

        [Tooltip("Values applied by the Ultra Quality_Preset (the maximum high-end PC values, Req 2.3).")]
        [SerializeField] private PresetBlock _ultra = PresetBlock.Defaults(QualityPreset.Ultra);

        /// <summary>
        /// Converts the four authored blocks into the engine-free <see cref="IGraphicsPresetTable"/> the
        /// <see cref="GraphicsSettingsViewModel"/> consumes.
        /// </summary>
        public IGraphicsPresetTable ToTable()
        {
            return new InMemoryGraphicsPresetTable(
                _low.ToValues(),
                _medium.ToValues(),
                _high.ToValues(),
                _ultra.ToValues());
        }

        /// <summary>
        /// Checks the authored blocks against the Req 2.3 ordering (render/view distance and particle
        /// density non-decreasing from Low → Ultra, and each resolution's pixel count non-decreasing).
        /// Returns true when the ordering holds; otherwise returns false and fills
        /// <paramref name="error"/> with the first violation found. Intended for an Editor validation
        /// pass — it does not throw so a partially authored asset can still be inspected.
        /// </summary>
        public bool Validate(out string error)
        {
            var blocks = new[] { _low, _medium, _high, _ultra };
            var names = new[] { "Low", "Medium", "High", "Ultra" };

            for (int i = 1; i < blocks.Length; i++)
            {
                if (blocks[i].RenderViewDistance < blocks[i - 1].RenderViewDistance)
                {
                    error = $"{names[i]} render/view distance is lower than {names[i - 1]} (Req 2.3).";
                    return false;
                }

                if (blocks[i].ParticleDensity < blocks[i - 1].ParticleDensity)
                {
                    error = $"{names[i]} particle density is lower than {names[i - 1]} (Req 2.3).";
                    return false;
                }

                long thisPixels = (long)blocks[i].ResolutionWidth * blocks[i].ResolutionHeight;
                long prevPixels = (long)blocks[i - 1].ResolutionWidth * blocks[i - 1].ResolutionHeight;
                if (thisPixels < prevPixels)
                {
                    error = $"{names[i]} resolution has fewer pixels than {names[i - 1]} (Req 2.3).";
                    return false;
                }
            }

            error = null;
            return true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Validate(out string error))
            {
                Debug.LogWarning($"[GraphicsPresetAsset:{name}] {error}", this);
            }
        }
#endif

        /// <summary>
        /// The authored value block for a single <see cref="QualityPreset"/>. A serializable struct so it
        /// appears as an inline group in the Inspector; <see cref="ToValues"/> converts it to the
        /// engine-free <see cref="GraphicsPresetValues"/>.
        /// </summary>
        [Serializable]
        public struct PresetBlock
        {
            [Tooltip("Horizontal resolution in pixels.")]
            public int ResolutionWidth;

            [Tooltip("Vertical resolution in pixels.")]
            public int ResolutionHeight;

            [Tooltip("Target refresh rate in Hz.")]
            public int RefreshRateHz;

            [Tooltip("Shadow quality level applied by this preset.")]
            public QualityPreset ShadowQuality;

            [Tooltip("Whether the Bloom Post_Processing_Effect is enabled.")]
            public bool Bloom;

            [Tooltip("Whether the Ambient_Occlusion Post_Processing_Effect is enabled.")]
            public bool AmbientOcclusion;

            [Tooltip("Whether the Motion_Blur Post_Processing_Effect is enabled.")]
            public bool MotionBlur;

            [Tooltip("Whether the Color_Grading Post_Processing_Effect is enabled.")]
            public bool ColorGrading;

            [Tooltip("Whether the Anti_Aliasing Post_Processing_Effect is enabled.")]
            public bool AntiAliasing;

            [Tooltip("Render/view distance applied by this preset.")]
            public float RenderViewDistance;

            [Tooltip("Texture quality level applied by this preset.")]
            public QualityPreset TextureQuality;

            [Tooltip("Particle density (0..1) applied by this preset.")]
            public float ParticleDensity;

            /// <summary>Converts the authored block into an engine-free <see cref="GraphicsPresetValues"/>.</summary>
            public GraphicsPresetValues ToValues()
            {
                return new GraphicsPresetValues(
                    new ResolutionSetting(ResolutionWidth, ResolutionHeight, RefreshRateHz),
                    ShadowQuality,
                    Bloom,
                    AmbientOcclusion,
                    MotionBlur,
                    ColorGrading,
                    AntiAliasing,
                    RenderViewDistance,
                    TextureQuality,
                    ParticleDensity);
            }

            /// <summary>
            /// Seeds a new block from the built-in <see cref="DefaultGraphicsPresetTable"/> for the given
            /// <paramref name="preset"/>, so a freshly created asset already holds a valid, monotonic
            /// configuration a designer can tweak.
            /// </summary>
            public static PresetBlock Defaults(QualityPreset preset)
            {
                GraphicsPresetValues v = DefaultGraphicsPresetTable.Instance.Get(preset);
                return new PresetBlock
                {
                    ResolutionWidth = v.Resolution.Width,
                    ResolutionHeight = v.Resolution.Height,
                    RefreshRateHz = v.Resolution.RefreshRateHz,
                    ShadowQuality = v.ShadowQuality,
                    Bloom = v.Bloom,
                    AmbientOcclusion = v.AmbientOcclusion,
                    MotionBlur = v.MotionBlur,
                    ColorGrading = v.ColorGrading,
                    AntiAliasing = v.AntiAliasing,
                    RenderViewDistance = v.RenderViewDistance,
                    TextureQuality = v.TextureQuality,
                    ParticleDensity = v.ParticleDensity,
                };
            }
        }
    }
}
