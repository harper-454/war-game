using UnityEngine;
using UnityEngine.Rendering;
using EpochWar.Unity.UI;

namespace EpochWar.Unity.Rendering
{
    /// <summary>
    /// The project's single, documented reference to the Universal Render Pipeline
    /// <see cref="RenderPipelineAsset"/>s the Game_Client renders with (Req 1.1), authored as a
    /// ScriptableObject so a designer wires the URP assets in the Editor and code never hard-codes an
    /// asset path.
    ///
    /// <para>URP's pipeline asset is what makes URP the project's <em>active</em> render pipeline: it is
    /// assigned in <b>Project Settings → Graphics → Scriptable Render Pipeline Settings</b> and per
    /// quality level in <b>Project Settings → Quality</b>. Because the concrete
    /// <c>UniversalRenderPipelineAsset</c> type lives in the URP package assembly, this config stores the
    /// asset as its engine-core base type <see cref="RenderPipelineAsset"/> — so this file compiles
    /// whether or not the URP package assembly is referenced, while still holding exactly the URP asset a
    /// designer assigns. See <c>URP_SETUP.md</c> for the manual Editor steps that create these assets.</para>
    ///
    /// <para>The config also exposes a per-<see cref="QualityPreset"/> pipeline asset so the graphics
    /// settings system can swap the active pipeline when the shadow/texture/quality preset changes
    /// (Req 2.2, 2.5), and a defensive <see cref="ApplyAsActivePipeline"/> that assigns the pipeline at
    /// runtime for scenes that assemble outside the Editor's project-settings flow.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "EpochWar/URP Pipeline Config", fileName = "UrpPipelineConfig")]
    public sealed class UrpPipelineConfig : ScriptableObject
    {
        [Tooltip("The URP Pipeline Asset used as the project's active Render Pipeline Asset (Req 1.1). " +
                 "Assign the UniversalRenderPipelineAsset created per URP_SETUP.md.")]
        [SerializeField] private RenderPipelineAsset _activePipeline;

        [Header("Optional per-quality pipeline assets (Req 2.2, 2.5)")]
        [Tooltip("URP Pipeline Asset for the Low quality preset. Falls back to the active pipeline when unset.")]
        [SerializeField] private RenderPipelineAsset _low;

        [Tooltip("URP Pipeline Asset for the Medium quality preset. Falls back to the active pipeline when unset.")]
        [SerializeField] private RenderPipelineAsset _medium;

        [Tooltip("URP Pipeline Asset for the High quality preset. Falls back to the active pipeline when unset.")]
        [SerializeField] private RenderPipelineAsset _high;

        [Tooltip("URP Pipeline Asset for the Ultra quality preset. Falls back to the active pipeline when unset.")]
        [SerializeField] private RenderPipelineAsset _ultra;

        /// <summary>The URP pipeline asset that should be the project's active Render Pipeline Asset (Req 1.1).</summary>
        public RenderPipelineAsset ActivePipeline => _activePipeline;

        /// <summary>
        /// The URP pipeline asset configured for <paramref name="preset"/>, or <see cref="ActivePipeline"/>
        /// when no dedicated asset is assigned for that preset. Never returns null unless nothing at all
        /// is assigned.
        /// </summary>
        public RenderPipelineAsset GetPipelineAsset(QualityPreset preset)
        {
            RenderPipelineAsset asset;
            switch (preset)
            {
                case QualityPreset.Ultra: asset = _ultra; break;
                case QualityPreset.High: asset = _high; break;
                case QualityPreset.Medium: asset = _medium; break;
                default: asset = _low; break;
            }

            return asset != null ? asset : _activePipeline;
        }

        /// <summary>
        /// Assigns this config's active pipeline as the runtime render pipeline via
        /// <see cref="GraphicsSettings.defaultRenderPipeline"/> and the current
        /// <see cref="QualitySettings"/> level (Req 1.1). Normally the Editor's project-settings
        /// assignment covers this; call at runtime only when a scene must guarantee URP is active without
        /// relying on project settings (e.g. an automated PlayMode/integration harness). A no-op when no
        /// active pipeline is assigned.
        /// </summary>
        public void ApplyAsActivePipeline()
        {
            if (_activePipeline == null)
            {
                Debug.LogWarning($"[UrpPipelineConfig:{name}] No active pipeline assigned; cannot apply URP at runtime.");
                return;
            }

            GraphicsSettings.defaultRenderPipeline = _activePipeline;
            QualitySettings.renderPipeline = _activePipeline;
        }

        /// <summary>
        /// Assigns the pipeline asset configured for <paramref name="preset"/> as the current quality
        /// level's render pipeline (Req 2.2, 2.5). Used by the graphics settings system when the Player
        /// changes a preset that maps to a distinct URP pipeline asset. A no-op when the preset resolves
        /// to no asset.
        /// </summary>
        public void ApplyPipelineForPreset(QualityPreset preset)
        {
            RenderPipelineAsset asset = GetPipelineAsset(preset);
            if (asset == null)
            {
                return;
            }

            QualitySettings.renderPipeline = asset;
        }
    }
}
