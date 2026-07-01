using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EpochWar.Unity.Entities
{
    /// <summary>
    /// The Unity-side Atmosphere_System: it applies the skybox, ambient-lighting mood, distance fog, and
    /// weather visual effects for a Match's environment (task 7.1, Req 6).
    ///
    /// <para><b>Match start.</b> <see cref="ApplyForMatchStart"/> (invoked by <c>MatchSceneController</c>
    /// alongside the other presentation systems, and optionally on <see cref="Start"/>) selects the
    /// configured <see cref="EnvironmentPreset"/> for the Match's environment and applies:
    /// <list type="bullet">
    ///   <item>its skybox material, or a defined default skybox when the environment configures none
    ///   (Req 6.1, 6.2);</item>
    ///   <item>the ambient-lighting colour and intensity preset predefined for that skybox (Req 6.6);</item>
    ///   <item>URP distance fog whose density is driven by the environment's <c>[0, 1]</c> value mapped onto
    ///   the configured maximum density (Req 6.3);</item>
    ///   <item>each configured weather visual effect, activated for its configured duration (Req 6.4).</item>
    /// </list></para>
    ///
    /// <para><b>Graceful degradation (Req 6.5).</b> A weather effect whose prefab is unavailable (unset, or
    /// whose instantiation throws) is logged and skipped; the Match still starts and the remaining
    /// atmosphere is applied. No path in this controller throws out of <see cref="ApplyForMatchStart"/>.
    /// URP honours the built-in <see cref="RenderSettings"/> fog/skybox/ambient, so this controller drives
    /// those global settings directly and never writes into <c>MatchState</c> — it is presentation-only.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AtmosphereController : MonoBehaviour
    {
        [Header("Environments (Req 6.1, 6.6)")]
        [SerializeField]
        [Tooltip("Per-environment presets (skybox + ambient + fog + weather). Selected by Environment Id.")]
        private EnvironmentPreset[] _environments = Array.Empty<EnvironmentPreset>();

        [SerializeField]
        [Tooltip("The environment id applied on Match start. Falls back to the first entry, then the default skybox.")]
        private string _environmentId = string.Empty;

        [Header("Defaults (Req 6.2)")]
        [SerializeField]
        [Tooltip("Skybox rendered when the selected environment configures none, or no environment matches (Req 6.2).")]
        private Material _defaultSkybox;

        [SerializeField]
        [Tooltip("Ambient colour applied with the default skybox.")]
        private Color _defaultAmbientColor = new Color(0.55f, 0.57f, 0.62f, 1f);

        [SerializeField]
        [Tooltip("Ambient intensity applied with the default skybox.")]
        [Min(0f)]
        private float _defaultAmbientIntensity = 1f;

        [SerializeField]
        [Tooltip("Fog colour applied with the default skybox.")]
        private Color _defaultFogColor = new Color(0.6f, 0.63f, 0.68f, 1f);

        [SerializeField]
        [Tooltip("Default per-environment fog density in [0, 1] (0 = none, 1 = maximum).")]
        [Range(0f, 1f)]
        private float _defaultFogDensity = 0.1f;

        [Header("Fog mapping (Req 6.3)")]
        [SerializeField]
        [Tooltip("Real exponential fog density mapped to a [0,1] value of 1.0. The [0,1] input scales linearly onto [0, this].")]
        [Min(0f)]
        private float _maxFogDensity = 0.1f;

        [Header("Weather (Req 6.4)")]
        [SerializeField]
        [Tooltip("Optional parent for spawned weather effect instances; defaults to this transform.")]
        private Transform _weatherRoot;

        [SerializeField]
        [Tooltip("When true the controller also applies its environment on Start (in addition to the scene wiring call).")]
        private bool _applyOnStart = true;

        // Weather effects currently playing, counted down toward their configured duration.
        private readonly List<ActiveWeather> _activeWeather = new List<ActiveWeather>();

        private bool _applied;

        private void Start()
        {
            if (_applyOnStart && !_applied)
            {
                ApplyForMatchStart();
            }
        }

        private void Update()
        {
            if (_activeWeather.Count == 0)
            {
                return;
            }

            float dt = Time.deltaTime;
            for (int i = _activeWeather.Count - 1; i >= 0; i--)
            {
                ActiveWeather weather = _activeWeather[i];

                // A destroyed instance (scene teardown) is simply dropped.
                if (weather.Instance == null)
                {
                    _activeWeather.RemoveAt(i);
                    continue;
                }

                if (weather.Infinite)
                {
                    continue;
                }

                weather.Remaining -= dt;
                if (weather.Remaining <= 0f)
                {
                    DestroyWeatherInstance(weather.Instance);
                    _activeWeather.RemoveAt(i);
                }
                else
                {
                    _activeWeather[i] = weather;
                }
            }
        }

        // ------------------------------------------------------------------
        // Public entry points
        // ------------------------------------------------------------------

        /// <summary>
        /// Applies the atmosphere for the configured Match environment (skybox, ambient, fog, weather) on
        /// Match start (Req 6.1–6.6). Selects the <see cref="EnvironmentPreset"/> whose
        /// <see cref="EnvironmentPreset.EnvironmentId"/> matches <c>_environmentId</c>, else the first
        /// configured environment, else the default skybox path (Req 6.2). Never throws (Req 6.5).
        /// </summary>
        public void ApplyForMatchStart()
        {
            _applied = true;
            ApplyEnvironment(ResolveEnvironment());
        }

        /// <summary>
        /// Applies a specific environment by id (Req 6.1). Falls back to the default skybox path when no
        /// environment matches (Req 6.2). Never throws (Req 6.5).
        /// </summary>
        public void ApplyEnvironment(string environmentId)
        {
            _environmentId = environmentId;
            ApplyEnvironment(FindEnvironment(environmentId));
        }

        /// <summary>
        /// Deactivates every currently playing weather effect (Req 6.4, "the condition is deactivated"),
        /// leaving the skybox/ambient/fog in place.
        /// </summary>
        public void DeactivateAllWeather()
        {
            foreach (ActiveWeather weather in _activeWeather)
            {
                DestroyWeatherInstance(weather.Instance);
            }

            _activeWeather.Clear();
        }

        /// <summary>
        /// Deactivates the currently playing weather effect(s) with the given condition name (Req 6.4).
        /// </summary>
        public void DeactivateWeather(string conditionName)
        {
            for (int i = _activeWeather.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_activeWeather[i].ConditionName, conditionName, StringComparison.Ordinal))
                {
                    DestroyWeatherInstance(_activeWeather[i].Instance);
                    _activeWeather.RemoveAt(i);
                }
            }
        }

        // ------------------------------------------------------------------
        // Environment selection
        // ------------------------------------------------------------------

        private string ResolveEnvironment()
        {
            if (!string.IsNullOrEmpty(_environmentId))
            {
                return _environmentId;
            }

            // No explicit selection: use the first configured environment's id if any, else empty (default).
            if (_environments != null && _environments.Length > 0 && _environments[0] != null)
            {
                return _environments[0].EnvironmentId;
            }

            return string.Empty;
        }

        private EnvironmentPreset FindEnvironment(string environmentId)
        {
            if (_environments == null)
            {
                return null;
            }

            foreach (EnvironmentPreset preset in _environments)
            {
                if (preset != null && string.Equals(preset.EnvironmentId, environmentId, StringComparison.Ordinal))
                {
                    return preset;
                }
            }

            // No id match: fall back to the first configured environment if the id was blank, else null so
            // the default skybox path is used (Req 6.2).
            if (string.IsNullOrEmpty(environmentId) && _environments.Length > 0)
            {
                return _environments[0];
            }

            return null;
        }

        // ------------------------------------------------------------------
        // Application (Req 6.1, 6.2, 6.3, 6.6)
        // ------------------------------------------------------------------

        private void ApplyEnvironment(EnvironmentPreset preset)
        {
            ApplySkyboxAndAmbient(preset);
            ApplyFog(preset);
            ApplyWeather(preset);
        }

        private void ApplySkyboxAndAmbient(EnvironmentPreset preset)
        {
            // Skybox: the environment's material, or the defined default when none is configured (Req 6.1, 6.2).
            Material skybox = preset != null && preset.Skybox != null ? preset.Skybox : _defaultSkybox;
            RenderSettings.skybox = skybox;

            // Ambient colour + intensity preset predefined for the rendered skybox (Req 6.6).
            Color ambientColor = preset != null ? preset.AmbientColor : _defaultAmbientColor;
            float ambientIntensity = Mathf.Max(0f, preset != null ? preset.AmbientIntensity : _defaultAmbientIntensity);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ScaleRgb(ambientColor, ambientIntensity);
            RenderSettings.ambientIntensity = ambientIntensity;

            // Rebuild environment lighting so the new skybox/ambient take effect immediately.
            DynamicGI.UpdateEnvironment();
        }

        private void ApplyFog(EnvironmentPreset preset)
        {
            Color fogColor = preset != null ? preset.FogColor : _defaultFogColor;
            float density01 = Mathf.Clamp01(preset != null ? preset.FogDensity : _defaultFogDensity);

            // Req 6.3: density 0 = no fog, 1 = maximum fog. The [0,1] value scales onto the configured
            // real exponential density; URP renders the built-in RenderSettings fog.
            if (density01 <= 0f)
            {
                RenderSettings.fog = false;
                RenderSettings.fogDensity = 0f;
                return;
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = Mathf.Lerp(0f, Mathf.Max(0f, _maxFogDensity), density01);
        }

        // ------------------------------------------------------------------
        // Weather (Req 6.4, 6.5)
        // ------------------------------------------------------------------

        private void ApplyWeather(EnvironmentPreset preset)
        {
            // Starting a fresh environment replaces any previously playing weather.
            DeactivateAllWeather();

            if (preset == null || preset.Weather == null)
            {
                return;
            }

            foreach (WeatherEffectConfig config in preset.Weather)
            {
                if (config == null)
                {
                    continue;
                }

                if (config.EffectPrefab == null)
                {
                    // Req 6.5: a missing weather asset is logged and skipped; the Match is not blocked.
                    Debug.LogWarning(
                        $"[AtmosphereController:{name}] Weather condition '{config.ConditionName}' has no effect "
                        + "prefab assigned; continuing the Match without it (Req 6.5).");
                    continue;
                }

                GameObject instance;
                try
                {
                    Transform parent = _weatherRoot != null ? _weatherRoot : transform;
                    instance = Instantiate(config.EffectPrefab, parent);
                    instance.name = $"Weather_{config.ConditionName}";
                    instance.transform.localPosition = Vector3.zero;
                    instance.SetActive(true);
                }
                catch (Exception ex)
                {
                    // Req 6.5: never let a weather-effect failure block Match start.
                    Debug.LogWarning(
                        $"[AtmosphereController:{name}] Failed to spawn weather condition '{config.ConditionName}': "
                        + $"{ex.Message}. Continuing the Match without it (Req 6.5).");
                    continue;
                }

                // A non-positive duration means the condition lasts the whole Match until deactivated (Req 6.4).
                bool infinite = config.DurationSeconds <= 0f;
                _activeWeather.Add(new ActiveWeather(
                    config.ConditionName, instance, infinite ? 0f : config.DurationSeconds, infinite));
            }
        }

        private static void DestroyWeatherInstance(GameObject instance)
        {
            if (instance != null)
            {
                Destroy(instance);
            }
        }

        // ------------------------------------------------------------------
        // Helpers + serialized data
        // ------------------------------------------------------------------

        private static Color ScaleRgb(Color color, float intensity)
            => new Color(color.r * intensity, color.g * intensity, color.b * intensity, color.a);

        /// <summary>Runtime record for a currently playing weather effect.</summary>
        private struct ActiveWeather
        {
            public ActiveWeather(string conditionName, GameObject instance, float remaining, bool infinite)
            {
                ConditionName = conditionName;
                Instance = instance;
                Remaining = remaining;
                Infinite = infinite;
            }

            public string ConditionName { get; }
            public GameObject Instance { get; }
            public float Remaining { get; set; }
            public bool Infinite { get; }
        }

        /// <summary>
        /// A single Match-environment preset (Req 6.1, 6.3, 6.6): its skybox, the ambient colour/intensity
        /// predefined for that skybox, its distance-fog colour and <c>[0,1]</c> density, and its configured
        /// weather conditions. Authored on the <see cref="AtmosphereController"/> component in the Editor.
        /// </summary>
        [Serializable]
        public sealed class EnvironmentPreset
        {
            [Tooltip("Stable id used to select this environment for a Match.")]
            public string EnvironmentId = string.Empty;

            [Tooltip("Skybox material for this environment. When unset, the controller's default skybox is used (Req 6.2).")]
            public Material Skybox;

            [Tooltip("Ambient lighting colour predefined for this skybox (Req 6.6).")]
            public Color AmbientColor = new Color(0.55f, 0.57f, 0.62f, 1f);

            [Tooltip("Ambient lighting intensity predefined for this skybox (Req 6.6).")]
            [Min(0f)]
            public float AmbientIntensity = 1f;

            [Tooltip("Distance fog colour for this environment.")]
            public Color FogColor = new Color(0.6f, 0.63f, 0.68f, 1f);

            [Tooltip("Distance fog density in [0, 1]: 0 = none, 1 = maximum (Req 6.3).")]
            [Range(0f, 1f)]
            public float FogDensity = 0.1f;

            [Tooltip("Weather conditions configured for this environment (Req 6.4).")]
            public WeatherEffectConfig[] Weather = Array.Empty<WeatherEffectConfig>();
        }

        /// <summary>
        /// A single configured weather condition (Req 6.4): the visual effect prefab to play and how long
        /// the condition lasts. A duration of zero or less means the condition plays until it is
        /// explicitly deactivated. A null <see cref="EffectPrefab"/> is tolerated (Req 6.5).
        /// </summary>
        [Serializable]
        public sealed class WeatherEffectConfig
        {
            [Tooltip("Human-readable condition name (e.g. Rain, Snow, Sandstorm).")]
            public string ConditionName = string.Empty;

            [Tooltip("The weather visual effect prefab. May be left unset — the Match still starts (Req 6.5).")]
            public GameObject EffectPrefab;

            [Tooltip("How long the condition stays active, in seconds. <= 0 means until explicitly deactivated (Req 6.4).")]
            public float DurationSeconds = 30f;
        }
    }
}
