# Universal Render Pipeline (URP) Setup — EpochWar.Unity

This note lists the **manual Unity Editor steps** required to complete Requirement 1 (Universal Render
Pipeline Adoption). The `.asset`, `.mat`, and package-manifest files that back these steps are authored
in the Editor and cannot be created from code, so this document is the source of truth for the manual
one-time configuration. All C# that consumes these assets already exists in the project:

- `Assets/EpochWar/Unity/Rendering/UrpPipelineConfig.cs` — the project's documented reference to the URP
  `RenderPipelineAsset` (and optional per-quality-preset pipeline assets), with helpers to apply it at
  runtime (`ApplyAsActivePipeline`, `ApplyPipelineForPreset`).
- `Assets/EpochWar/Unity/UI/GraphicsSettingsController.cs` — drives shadow/texture/post-processing/
  render-distance settings against `QualitySettings`, the URP pipeline asset, and the global `Volume`
  profile (Req 2.5), and defers resolution/VSync to the next launch (Req 2.6).

## 1. Add the URP package

In **Window → Package Manager**, install:

- `com.unity.render-pipelines.universal` (Universal RP)

Installing it transitively brings in `com.unity.render-pipelines.core`. These two packages provide the
assemblies now referenced by `EpochWar.Unity.asmdef`:

- `Unity.RenderPipelines.Universal.Runtime` — `UniversalRenderPipelineAsset`, `Bloom`, `MotionBlur`,
  `ColorAdjustments`, `ScriptableRendererFeature`, `UniversalAdditionalCameraData`, `AntialiasingMode`.
- `Unity.RenderPipelines.Core.Runtime` — `Volume`, `VolumeProfile`, `VolumeComponent`.

> If the package is not installed, the two new asmdef references will fail to resolve and
> `EpochWar.Unity` will not compile — install the package **before** opening the project in the Editor.

`EpochWar.Core` must remain free of any UnityEngine or URP reference (Req 1.4); no URP reference is added
to the Core assembly, and none of the new files live under `Assets/EpochWar/Core/`.

## 2. Create the URP Pipeline + Renderer assets

1. **Assets → Create → Rendering → URP Asset (with Universal Renderer)**. This creates two assets: a
   `UniversalRenderPipelineAsset` (the *pipeline* asset) and a `UniversalRendererData` (the *renderer*
   asset). Name them e.g. `EpochWar_URP_Pipeline` and `EpochWar_URP_Renderer`.
2. (Optional, Req 2.2/2.5) Duplicate the pipeline asset once per Quality_Preset — `..._Low`, `..._Medium`,
   `..._High`, `..._Ultra` — and lower/raise shadow distance, shadow resolution, render scale, and MSAA on
   each so the presets map to distinct pipeline assets.

## 3. Make URP the active Render Pipeline

1. **Project Settings → Graphics → Scriptable Render Pipeline Settings**: assign
   `EpochWar_URP_Pipeline` (Req 1.1).
2. **Project Settings → Quality**: for every quality level, assign the matching URP pipeline asset in the
   *Render Pipeline Asset* field (assign the per-preset assets from step 2.2 if created).

## 4. Wire the `UrpPipelineConfig`

1. **Assets → Create → EpochWar → URP Pipeline Config**.
2. Assign `EpochWar_URP_Pipeline` to **Active Pipeline**, and the per-preset assets to Low/Medium/High/
   Ultra if you created them.
3. Reference this config from the `GraphicsSettingsController` component in the settings scene.

## 5. Switch existing materials to URP Lit shaders

URP cannot render Built-in-pipeline shaders. Convert the Terrain, Unit, and Structure materials so their
assigned shader declares URP support (Req 1.2, 1.3):

- **Edit → Rendering → Materials → Convert All Built-in Materials to URP** (bulk), or per material set the
  shader to **Universal Render Pipeline/Lit** (opaque surfaces) or **Universal Render Pipeline/Simple Lit**.
- Terrain `Cell_Material` assets (Req 3) must use a URP-lit shader with a diffuse (Base Map) texture and a
  Normal Map assigned.
- Verify no material still uses `Standard`, `Legacy Shaders/*`, or any Built-in-only shader (they render as
  magenta under URP).

## 6. Global post-processing Volume

1. Add a **Global Volume** GameObject to the match scene (**GameObject → Volume → Global Volume**).
2. Create/assign a `VolumeProfile` and add the overrides the settings UI toggles: **Bloom**, **Motion
   Blur**, **Color Adjustments** (Color Grading).
3. Ambient Occlusion (SSAO) is a **Renderer Feature**: on `EpochWar_URP_Renderer` add the *Screen Space
   Ambient Occlusion* feature.
4. On the `GraphicsSettingsController` component, wire: **Post Processing Volume** → the Global Volume,
   **Ambient Occlusion Feature** → the SSAO renderer feature, **Target Camera** → the main camera (its
   *Post Processing* checkbox must be enabled in its `UniversalAdditionalCameraData`).

## 7. Verify (Req 1.1–1.4)

- Play mode renders with no magenta materials.
- `GraphicsSettings.currentRenderPipeline` is the URP pipeline asset.
- Terrain/Unit/Structure material shaders report URP support.
- `EpochWar.Core.asmdef` still has **no** UnityEngine/URP references (confirm the Core assembly is
  unchanged).
