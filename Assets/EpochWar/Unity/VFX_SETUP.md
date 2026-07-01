# Terrain Materials & VFX Setup — EpochWar.Unity

This note lists the **manual Unity Editor authoring** required to complete the Terrain-material
rendering (Requirement 3), terrain destruction VFX (Requirement 4), and combat/destruction VFX
(Requirement 5) for the Visuals pillar. All C# that consumes these authored assets already exists in
the project:

- `Assets/EpochWar/Unity/Entities/TerrainRenderer.cs` — Cell_Material submesh rendering + terrain
  destruction VFX hooks.
- `Assets/EpochWar/Unity/Entities/EffectPool.cs` — pooled particle/decal lifetime management.
- `Assets/EpochWar/Unity/Entities/VfxSystem.cs` — combat/destruction/Doomsday VFX from simulation events.

Every reference below is a serialized field. Any field left unset degrades gracefully (the renderer/
system logs or silently skips that one effect and continues) — an unwired prefab never breaks a Match.

## 1. Cell_Material assets (Req 3.1, 3.2)

For each terrain `CellMaterial` — `Soil`, `Rock`, `Sand`, `Reinforced` — author a **URP Lit** material
with a **Base Map** (diffuse texture) and a **Normal Map** assigned:

1. **Assets → Create → Material**, set its shader to **Universal Render Pipeline/Lit**.
2. Assign a diffuse texture to *Base Map* and a normal texture to *Normal Map* (enable the Normal Map
   checkbox). Tangents are recalculated by the mesher, so the normal map lights correctly.
3. On the **TerrainRenderer** component, populate the **Cell Materials** list with one entry per terrain
   type (`Terrain` = the `CellMaterial`, `Material` = the authored material).
4. Assign a **Fallback Cell Material** (used for any terrain type with no entry, Req 3.5). The legacy
   single **Terrain Material** field is the last-resort fallback if even that is unset.

### How materials layer onto the existing mesher

The chunked cubic mesher is preserved. Faces are now grouped **by their cell's `CellMaterial` into one
submesh per material**; the chunk's `MeshRenderer.sharedMaterials` array is assigned the resolved
Cell_Material per submesh (with fallback substitution). The mesher also emits per-face UVs (for the
diffuse map) and recalculates tangents (for the normal map). Because every dirty-chunk rebuild re-reads
each cell's current material and re-assigns submesh materials, a cell whose terrain type changes is
re-materialised **within the same chunk rebuild the `TerrainModifiedEvent` already triggers** (Req 3.3,
3.4) — no new event or code path. A per-material vertex-color tint is layered on top so terrain types
remain visually distinct even before textures are authored.

## 2. EffectPool (Req 4.6, 5.8)

1. Add a GameObject to the match scene with the **EffectPool** component.
2. (Optional) assign a **Pool Root** transform for idle instances; defaults to the pool's own transform.
3. Leave **Scale Particles By Density** enabled so spawned particle emission tracks
   `GraphicsSettingsController.ParticleDensity`.

The pool renders no assets itself — callers pass a prefab, a world position, and a per-effect **lifetime
(removal ceiling)**. An `Update`-driven timer returns each instance to its per-prefab idle stack the
moment its lifetime elapses (spawn/return pooling, never destroy-and-leak). Ceilings enforced by callers:

- **Dust/debris** (TerrainRenderer): `<= 5s` (Req 4.6).
- **Standard combat/destruction** (VfxSystem): `3s` (Req 5.8).
- **Doomsday deployment** (VfxSystem): `4–10s`, ceiling `10s` (Req 5.7, 5.8).

## 3. Terrain destruction VFX (Req 4)

On the **TerrainRenderer** component:

- **Effect Pool** → the EffectPool from step 2.
- **Dust Debris Effect** → a particle prefab; spawned at **every** terrain modification (Req 4.1),
  registered at the **Dust Debris Lifetime Seconds** ceiling (clamped `<= 5s`, Req 4.6).
- **Crater Decal** → a decal prefab; placed at excavation sites when the effect **removed** cells and its
  `TerrainEffect.Power` is `>=` **Destructive Force Threshold** (Req 4.2); no crater below the threshold
  (Req 4.3).
- **Scorch Decal** → a decal prefab; placed on cells **damaged but not removed** (Req 4.4).
- **Decal Root** → optional parent for spawned decals.
- **Destructive Force Threshold** → the minimum `Power` (default `4`, i.e. Rock integrity) for a crater.

### Crater / scorch / suppression decision logic

For each `TerrainModifiedEvent` the renderer inspects the **post-effect** volume for each modified cell:
a cell that is no longer solid was **removed**; a cell still solid was **damaged**. Then:

- Dust/debris is spawned unconditionally at the modification centroid (dug or blasted).
- **Suppression (Req 4.5):** crater and scorch decals are rendered **only when the tick's event batch
  also contains a weapon/ability effect** (`CombatResolvedEvent`, `StructureCombatResolvedEvent`,
  `IndirectFireImpactEvent`, or `AbilityActivatedEvent`). A `TerrainModifiedEvent` with no such
  correlated event is treated as excavation-only and renders neither decal.
- **Crater:** rendered when `anyRemoved && Effect.Power >= threshold` (at the removed-cell centroid,
  raised to the excavation's top surface).
- **Scorch:** rendered when cells were damaged but not removed (on the top face of the highest damaged
  cell).

Decals are persistent terrain scars (Req 4 bounds only the dust/debris lifetime), so they are
instantiated directly under the decal root rather than pooled.

## 4. Combat & destruction VFX (Req 5)

On the **VfxSystem** component (bound to the driver by `MatchSceneController` alongside the other
presentation systems):

- **Driver** / **Effect Pool** → the scene `SimulationDriver` and the EffectPool from step 2.
- **Muzzle Flash Effect** / **Projectile Trail Effect** → ranged-attack prefabs (Req 5.1, 5.2). If the
  trail prefab has a `LineRenderer` it is stretched from the firing origin to the impact point.
- **Impact Effect** / **Explosion Effect** → non-explosive vs explosive arrival prefabs (Req 5.3, 5.4).
- **Unit Death Effect** / **Structure Collapse Effect** → zero-health removal prefabs (Req 5.5, 5.6).
- **Doomsday Effect** → the dedicated deployment prefab, authored **visually distinct** from
  impact/explosion (Req 5.7).
- **Standard Effect Seconds** (`<= 3s`) / **Doomsday Effect Seconds** (`4–10s`) / **Melee Range
  Threshold** tuning.

### Explosiveness-flag decision (Req 5.3, 5.4)

`CombatResolvedEvent` has no explosiveness field and Core is frozen for this group, so explosiveness is
**derived** presentation-side:

- A direct `CombatResolvedEvent` / `StructureCombatResolvedEvent` is **explosive** when the **attacking
  Unit's `UnitDef.AreaEffectRadius > 0` or `UnitDef.IsArtillery`** — otherwise it renders the impact
  effect.
- Every `IndirectFireImpactEvent` (arcing/indirect fire) is **explosive by definition**.

No field is added to any Core event type.

### Event path & position resolution

The system consumes the same ordered `GameEvent` batch from `SimulationDriver.Ticked` that every other
Unity system uses — it never polls Core internals. Because death/collapse/Doomsday events report an
**already-applied** removal, the entity is gone from `MatchState` when the event is observed; the system
keeps a per-entity last-known-position cache refreshed from live state at the **end** of each tick, so a
removal event this tick resolves against the position captured last tick. The Doomsday effect is anchored
at the centroid of the eliminated Nation's cached forces (falling back to the world origin if none were
cached).

### Ranged vs melee

A resolved attack is treated as ranged (muzzle flash + projectile trail) when its attacker and defender
are separated beyond **Melee Range Threshold**; otherwise only the impact/explosion is rendered. Distance
is used because Core combat events carry no explicit ranged flag and Core is frozen for this group.
