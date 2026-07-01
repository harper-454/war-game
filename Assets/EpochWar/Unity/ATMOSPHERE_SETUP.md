# Atmosphere & Entity-View Setup — EpochWar.Unity

This note lists the **manual Unity Editor authoring** required to complete the atmospheric/weather
effects (Requirement 6), Era-scaled visual detail (Requirement 7), and large-scale battle readability
(Requirement 8) of the Visuals pillar. All C# that consumes these authored assets already exists:

- `Assets/EpochWar/Unity/Entities/AtmosphereController.cs` — skybox, ambient, distance fog, weather.
- `Assets/EpochWar/Unity/Entities/EntityViewManager.cs` — Visual_Detail_Tier assignment + LOD/silhouette.
- `Assets/EpochWar/Unity/Entities/UnitView.cs` / `StructureView.cs` — optional per-tier detail variants.

Every reference below is a serialized field. Any field left unset degrades gracefully — a missing skybox
falls back to the default, a missing weather prefab is logged and skipped without blocking Match start
(Req 6.5), and a missing marker prefab is replaced by a generated camera-facing quad.

## 1. AtmosphereController (Req 6)

Add the **AtmosphereController** component to the match scene (e.g. on an `Atmosphere` GameObject). It is
bound on Match start by `MatchSceneController.BindPresentation` (offline and networked paths alike) and,
by default, also applies itself on `Start`.

### Environments (Req 6.1, 6.2, 6.6)

Populate the **Environments** list; each entry is an `EnvironmentPreset`:

- **Environment Id** — the id selected via the controller's **Environment Id** field (blank selects the
  first entry). Since the engine-free `MatchState` carries no environment concept, the environment is a
  scene-authoring choice.
- **Skybox** — a **URP-compatible skybox material** (e.g. *Skybox/Procedural* or *Skybox/6 Sided*). When
  left unset, the controller applies the component's **Default Skybox** (Req 6.2).
- **Ambient Color** / **Ambient Intensity** — the ambient-lighting mood **predefined for that skybox**
  (Req 6.6). Applied via `RenderSettings.ambientMode = Flat` (colour scaled by intensity).
- **Fog Color** / **Fog Density** — distance-fog colour and a **`[0, 1]`** density (0 = none, 1 = maximum,
  Req 6.3). The `[0,1]` value is mapped linearly onto the component's **Max Fog Density** (the real
  exponential `RenderSettings.fogDensity`). URP renders the built-in RenderSettings fog.
- **Weather** — the environment's weather conditions (see below).

### Defaults (Req 6.2)

Assign the component's **Default Skybox** and the default ambient/fog fields — these render when the
selected environment configures no skybox or no environment matches.

### Weather (Req 6.4, 6.5)

Each `WeatherEffectConfig` in an environment's **Weather** list has:

- **Condition Name** — e.g. `Rain`, `Snow`, `Sandstorm`.
- **Effect Prefab** — a particle/VFX prefab played for the condition. **May be left unset**: the
  controller logs a warning and continues the Match without it (Req 6.5).
- **Duration Seconds** — how long the condition stays active; `<= 0` means it plays until explicitly
  deactivated via `DeactivateWeather`/`DeactivateAllWeather` (Req 6.4).

Optionally assign a **Weather Root** transform to parent spawned weather instances.

> **URP fog note:** URP honours the legacy `RenderSettings` fog, skybox, and ambient settings, so the
> controller drives those directly. No per-environment Volume override is required for distance fog.

## 2. Era-scaled Visual_Detail_Tier (Req 7)

The tier is resolved automatically by `EntityViewManager` from each entity's
`UnitDef.VisualDetailTier` / `StructureDef.VisualDetailTier`:

- **Authored override (Req 7.4):** set **Visual Detail Tier** on a `UnitAsset` / `StructureAsset`. The
  default value `-1` means *unset* — the Entity_View_System derives the tier from the entity's Era.
- **Era-derived default (Req 7.1–7.3):** `EntityViewManager.DefaultVisualDetailTierForEra` maps each Era
  to a non-decreasing tier (Prehistoric = 1 … Space = 9); same-Era entries share a tier.
- **Fallback (Req 7.5):** an unset (`-1`/`0`) or out-of-range value falls back to the Era default without
  failing to render.

Optionally author **Detail Tier Variants** on `UnitView` / `StructureView` — an array of child
GameObjects ordered by ascending tier (index 0 = tier 1). The view activates the variant matching the
assigned tier and deactivates the rest. Leaving the array empty simply records the tier (graceful).

## 3. Large-scale readability / LOD (Req 8)

On the **EntityViewManager** component:

- **Camera** — the battlefield camera used for density and far-zoom distance. Defaults to `Camera.main`.
- **Density Threshold** — the visible-Unit count above which per-Unit detail is reduced (shadow casting
  disabled) to protect the frame rate (Req 8.1).
- **Min Frame Rate** — the frame-rate floor (fps); when the smoothed frame rate dips below it, detail
  reduction is forced regardless of count (Req 8.1).
- **Far Zoom Enter Distance** / **Far Zoom Exit Distance** — the camera-to-Unit distances at which a Unit
  swaps to a simplified marker (enter, Req 8.2) and restores full detail (exit, Req 8.3). **Exit must be
  below enter** to form the anti-flicker hysteresis buffer (Req 8.4); the component clamps it defensively.
- **Min Re Toggle Interval** — minimum seconds between simplify↔full flips for a single Unit (Req 8.4).
- **Unit Marker Prefab** — optional simplified silhouette prefab. When unset, a camera-facing quad is
  generated. The marker is tinted by the Unit's **Nation colour** via a `MaterialPropertyBlock`.
- **Marker Height** — local height above the Unit at which the marker sits.
- **Nation Colors** — per-Nation marker colours indexed by Nation id (Req 8.2). Ids beyond the list get a
  deterministic generated hue.

> The LOD pass runs in `LateUpdate` (camera-driven, per frame) while entity spawn/mirror/despawn stays
> tick-driven, so the readability behaviour tracks camera movement smoothly and never writes into
> `MatchState`.
