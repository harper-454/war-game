using System;
using System.IO;
using UnityEngine;

namespace EpochWar.Unity.UI
{
    /// <summary>
    /// Persists a <see cref="GraphicsSettingsViewModel"/> to a JSON file and restores it on startup,
    /// with the resilient fallback the requirements mandate (Req 2.7, 2.8).
    ///
    /// <para><b>Persistence (Req 2.7).</b> <see cref="Save"/> serializes the full view-model — every
    /// setting, not just the preset selection — to a JSON file under
    /// <c>Application.persistentDataPath</c>, so the exact configuration (including individual overrides
    /// applied after a preset) is restored the next time the Game_Client starts. The
    /// <see cref="GraphicsSettingsController"/> calls <see cref="Save"/> after every change.</para>
    ///
    /// <para><b>Startup fallback (Req 2.8).</b> <see cref="Load"/> attempts to read and deserialize the
    /// persisted file. On <em>any</em> failure — a missing/unreadable file that is present but corrupt,
    /// malformed JSON, or a value outside its valid range — it does not throw; instead it applies the
    /// <see cref="QualityPreset.Low"/> preset, sets <see cref="LastLoadWasReset"/>, raises the one-time
    /// <see cref="SettingsReset"/> notice, and returns a usable view-model so startup continues. A file
    /// that simply does not exist yet (first launch) is not a reset: it yields the default
    /// first-launch preset with no notice.</para>
    ///
    /// <para>The parse/validation core is factored into the static, engine-light
    /// <see cref="TryDeserialize"/> so the fallback decision can be reasoned about independently of file
    /// I/O.</para>
    /// </summary>
    public sealed class GraphicsSettingsStore
    {
        /// <summary>The default file name used under <see cref="Application.persistentDataPath"/>.</summary>
        public const string DefaultFileName = "graphics-settings.json";

        /// <summary>The preset applied on the very first launch, when no file exists yet.</summary>
        public const QualityPreset DefaultFirstLaunchPreset = QualityPreset.High;

        private readonly string _filePath;
        private readonly IGraphicsPresetTable _presetTable;

        /// <summary>
        /// True when the most recent <see cref="Load"/> fell back to the Low preset because the persisted
        /// data was invalid or unreadable (Req 2.8). Reset by each <see cref="Load"/> call.
        /// </summary>
        public bool LastLoadWasReset { get; private set; }

        /// <summary>
        /// Raised exactly once per <see cref="Load"/> that falls back to the Low preset, carrying the
        /// Player-facing notice text (Req 2.8). The controller subscribes to surface a UI notice.
        /// </summary>
        public event Action<string> SettingsReset;

        /// <summary>The notice text shown when settings are reset to Low due to invalid saved data (Req 2.8).</summary>
        public static string ResetNoticeText =>
            "Graphics settings were reset to the Low preset because the saved settings were invalid.";

        /// <summary>The absolute path of the JSON file this store reads and writes.</summary>
        public string FilePath => _filePath;

        /// <summary>
        /// Creates a store writing to <paramref name="filePath"/> (defaults to
        /// <c>Application.persistentDataPath/graphics-settings.json</c>) and reading preset bundles/bounds
        /// from <paramref name="presetTable"/> (defaults to <see cref="DefaultGraphicsPresetTable"/>).
        /// </summary>
        public GraphicsSettingsStore(string filePath = null, IGraphicsPresetTable presetTable = null)
        {
            _presetTable = presetTable ?? DefaultGraphicsPresetTable.Instance;
            _filePath = string.IsNullOrEmpty(filePath)
                ? Path.Combine(Application.persistentDataPath, DefaultFileName)
                : filePath;
        }

        // ------------------------------------------------------------------
        // Persist (Req 2.7)
        // ------------------------------------------------------------------

        /// <summary>
        /// Serializes <paramref name="viewModel"/> to the JSON file, creating the directory if needed.
        /// Returns true on success; on an I/O failure it logs and returns false rather than throwing, so
        /// a transient write error never crashes the settings UI.
        /// </summary>
        public bool Save(GraphicsSettingsViewModel viewModel)
        {
            if (viewModel == null)
            {
                throw new ArgumentNullException(nameof(viewModel));
            }

            try
            {
                var dto = GraphicsSettingsDto.FromViewModel(viewModel);
                string json = JsonUtility.ToJson(dto, prettyPrint: true);

                string directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_filePath, json);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[GraphicsSettingsStore] Failed to persist graphics settings: {ex.Message}");
                return false;
            }
        }

        // ------------------------------------------------------------------
        // Restore (Req 2.8)
        // ------------------------------------------------------------------

        /// <summary>
        /// Reads and restores the persisted view-model. When the file is missing (first launch) the
        /// <see cref="DefaultFirstLaunchPreset"/> is applied with no reset notice. When the file is
        /// present but unreadable, malformed, or holds an out-of-range value, the Low preset is applied,
        /// <see cref="LastLoadWasReset"/> is set, and <see cref="SettingsReset"/> is raised once (Req 2.8).
        /// Never throws; always returns a usable view-model so startup continues.
        /// </summary>
        public GraphicsSettingsViewModel Load()
        {
            LastLoadWasReset = false;

            string json;
            try
            {
                if (!File.Exists(_filePath))
                {
                    // No saved data yet — a clean first launch, not a reset.
                    return new GraphicsSettingsViewModel(_presetTable, DefaultFirstLaunchPreset);
                }

                json = File.ReadAllText(_filePath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // The file exists but could not be read — treat as invalid/unreadable saved data.
                Debug.LogWarning($"[GraphicsSettingsStore] Could not read graphics settings: {ex.Message}");
                return FallBackToLow();
            }

            if (TryDeserialize(json, _presetTable, out GraphicsSettingsViewModel viewModel))
            {
                return viewModel;
            }

            return FallBackToLow();
        }

        private GraphicsSettingsViewModel FallBackToLow()
        {
            LastLoadWasReset = true;
            var viewModel = new GraphicsSettingsViewModel(_presetTable, QualityPreset.Low);
            SettingsReset?.Invoke(ResetNoticeText);
            return viewModel;
        }

        // ------------------------------------------------------------------
        // Parse + validation core
        // ------------------------------------------------------------------

        /// <summary>
        /// Attempts to parse <paramref name="json"/> into a fully validated view-model bound to
        /// <paramref name="presetTable"/>. Returns false (without throwing) when the JSON is malformed,
        /// deserializes to null, or holds any value outside its valid range — the exact conditions that
        /// trigger the Req 2.8 fallback. The restored view-model reproduces the persisted values
        /// exactly, including individual overrides layered on top of a preset.
        /// </summary>
        public static bool TryDeserialize(
            string json,
            IGraphicsPresetTable presetTable,
            out GraphicsSettingsViewModel viewModel)
        {
            viewModel = null;
            if (presetTable == null || string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            GraphicsSettingsDto dto;
            try
            {
                dto = JsonUtility.FromJson<GraphicsSettingsDto>(json);
            }
            catch (Exception)
            {
                // JsonUtility throws ArgumentException on structurally invalid JSON.
                return false;
            }

            if (dto == null || !dto.IsValid(presetTable))
            {
                return false;
            }

            viewModel = dto.ToViewModel(presetTable);
            return true;
        }
    }

    /// <summary>
    /// The serializable on-disk representation of a <see cref="GraphicsSettingsViewModel"/>. Uses only
    /// JSON-friendly primitives and enums (no dictionaries) so Unity's <see cref="JsonUtility"/> can
    /// round-trip it, and validates every field against its valid range for the Req 2.8 fallback.
    /// </summary>
    [Serializable]
    public sealed class GraphicsSettingsDto
    {
        public QualityPreset Preset;
        public int ResolutionWidth;
        public int ResolutionHeight;
        public int RefreshRateHz;
        public QualityPreset ShadowQuality;
        public bool Bloom;
        public bool AmbientOcclusion;
        public bool MotionBlur;
        public bool ColorGrading;
        public bool AntiAliasing;
        public bool VSync;
        public float RenderViewDistance;
        public QualityPreset TextureQuality;
        public float ParticleDensity;

        /// <summary>Captures the full state of <paramref name="viewModel"/> into a serializable DTO.</summary>
        public static GraphicsSettingsDto FromViewModel(GraphicsSettingsViewModel viewModel)
        {
            return new GraphicsSettingsDto
            {
                Preset = viewModel.Preset,
                ResolutionWidth = viewModel.Resolution.Width,
                ResolutionHeight = viewModel.Resolution.Height,
                RefreshRateHz = viewModel.Resolution.RefreshRateHz,
                ShadowQuality = viewModel.ShadowQuality,
                Bloom = viewModel.Bloom,
                AmbientOcclusion = viewModel.AmbientOcclusion,
                MotionBlur = viewModel.MotionBlur,
                ColorGrading = viewModel.ColorGrading,
                AntiAliasing = viewModel.AntiAliasing,
                VSync = viewModel.VSync,
                RenderViewDistance = viewModel.RenderViewDistance,
                TextureQuality = viewModel.TextureQuality,
                ParticleDensity = viewModel.ParticleDensity,
            };
        }

        /// <summary>
        /// True when every field is within its valid range: all three preset-scale enums are defined
        /// values, the resolution is strictly positive, and the render/view distance and particle
        /// density lie within the Low..Ultra bounds of <paramref name="table"/> (Req 2.8).
        /// </summary>
        public bool IsValid(IGraphicsPresetTable table)
        {
            if (!IsDefinedPreset(Preset) || !IsDefinedPreset(ShadowQuality) || !IsDefinedPreset(TextureQuality))
            {
                return false;
            }

            if (ResolutionWidth <= 0 || ResolutionHeight <= 0 || RefreshRateHz <= 0)
            {
                return false;
            }

            if (float.IsNaN(RenderViewDistance) || float.IsNaN(ParticleDensity)
                || float.IsInfinity(RenderViewDistance) || float.IsInfinity(ParticleDensity))
            {
                return false;
            }

            return WithinBounds(RenderViewDistance, table, v => v.RenderViewDistance)
                   && WithinBounds(ParticleDensity, table, v => v.ParticleDensity);
        }

        /// <summary>
        /// Rebuilds a view-model that reproduces this DTO exactly: it starts from the persisted preset
        /// (so preset-covered defaults are established) and then re-applies each persisted field as an
        /// individual change, preserving any overrides the Player layered on top of the preset.
        /// </summary>
        public GraphicsSettingsViewModel ToViewModel(IGraphicsPresetTable table)
        {
            var viewModel = new GraphicsSettingsViewModel(table, Preset);
            viewModel.ApplyIndividualChange(
                GraphicsSettingField.Resolution,
                new ResolutionSetting(ResolutionWidth, ResolutionHeight, RefreshRateHz));
            viewModel.ApplyIndividualChange(GraphicsSettingField.ShadowQuality, ShadowQuality);
            viewModel.ApplyIndividualChange(GraphicsSettingField.TextureQuality, TextureQuality);
            viewModel.ApplyIndividualChange(GraphicsSettingField.Bloom, Bloom);
            viewModel.ApplyIndividualChange(GraphicsSettingField.AmbientOcclusion, AmbientOcclusion);
            viewModel.ApplyIndividualChange(GraphicsSettingField.MotionBlur, MotionBlur);
            viewModel.ApplyIndividualChange(GraphicsSettingField.ColorGrading, ColorGrading);
            viewModel.ApplyIndividualChange(GraphicsSettingField.AntiAliasing, AntiAliasing);
            viewModel.ApplyIndividualChange(GraphicsSettingField.VSync, VSync);
            viewModel.ApplyIndividualChange(GraphicsSettingField.RenderViewDistance, RenderViewDistance);
            viewModel.ApplyIndividualChange(GraphicsSettingField.ParticleDensity, ParticleDensity);
            return viewModel;
        }

        private static bool IsDefinedPreset(QualityPreset preset)
            => preset == QualityPreset.Low
               || preset == QualityPreset.Medium
               || preset == QualityPreset.High
               || preset == QualityPreset.Ultra;

        private static bool WithinBounds(
            float value, IGraphicsPresetTable table, Func<GraphicsPresetValues, float> selector)
        {
            float min = selector(table.Get(QualityPreset.Low));
            float max = selector(table.Get(QualityPreset.Ultra));
            if (min > max)
            {
                (min, max) = (max, min);
            }

            return value >= min && value <= max;
        }
    }
}
