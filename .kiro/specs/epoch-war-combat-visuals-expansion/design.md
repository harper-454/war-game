# Design Document — Epoch War: Combat & Visuals Expansion

## Overview

This document defines the technical design for the **Combat & Visuals Expansion**, an additive upgrade to the existing, implemented Epoch War codebase (`epoch-war-game`). The expansion delivers two independent pillars in priority order. The **Visuals + Graphics Settings** pillar (Requirements 1–8) upgrades `EpochWar.Unity` to the Universal Render Pipeline, adds a full graphics settings menu, textured/lit terrain rendering with destruction VFX, combat/destruction visual effects, atmospheric rendering, Era-scaled visual detail, and large-scale battle readability — all of it strictly Unity-side presentation work. The **Combat Depth** pillar (Requirements 9–15) extends the engine-free `EpochWar.Core` simulation with flanking, cover, area-of-effect damage, veterancy, unit abilities, fog of war/vision, and artillery/indirect fire, layered onto the existing `CombatSystem`, `UnitSystem`, and `TerrainSystem` without breaking any base-spec behavior.

Every addition in this document is designed to **extend** existing types, systems, and the existing fixed-tick simulation loop rather than replace them. No base-spec requirement, command, event, or public method signature is removed or changed in an incompatible way; existing base-spec property tests (Properties 1–46 in `epoch-war-game/design.md`) remain valid unchanged. The `EpochWar.Core` / `EpochWar.Unity` assembly boundary established by the base design is preserved exactly: the Combat Depth pillar's new logic lives entirely in `EpochWar.Core` with zero `UnityEngine` references, and the Visuals pillar's new logic lives entirely in `EpochWar.Unity`, reading replicated/local simulation state without ever mutating simulation truth.

## Design Principles

1. **Extend, don't replace.** Every new capability is added as new fields, new POCOs, new command handlers, or new systems composed alongside the existing `CombatSystem`, `UnitSystem`, `TerrainSystem`, and `MatchSimulation`. No existing method signature, event type, or command type from the base spec is changed incompatibly.
2. **The Core/Unity assembly boundary is hard and non-negotiable.** All of Requirements 9–15 (Combat Depth) is implemented in `EpochWar.Core` with no `UnityEngine` reference, matching Requirement 1.4/9.4-analog constraints already enforced by the base spec's asmdef split. All of Requirements 1–8 (Visuals) is implemented in `EpochWar.Unity` and never introduces a new dependency from `EpochWar.Core` back into Unity or the URP package.
3. **Positional and geometric combat math is deterministic fixed-point/integer arithmetic.** Flanking angle comparisons, cover distance checks, and Area_Effect radius checks use `EpochWar.Core.Math.Fixed` (the same deterministic fixed-point type `WorldPosition` already uses) so that combat resolution remains reproducible across Host and any future replay/verification tooling — no `float`/`double` trigonometry drift.
4. **Unity-side visual systems are presentation-only.** `GraphicsSettingsController`, `VfxSystem`, `AtmosphereController`, and the updated `TerrainRenderer`/`EntityViewManager` read replicated `MatchState`/local settings and never write back into `MatchState`, `UnitInstance`, `StructureInstance`, or any Core type. Visual effects are keyed off `GameEvent`s already flowing out of `MatchSimulation.Tick`, not off direct polling of Core internals.
5. **Systems own logic; state containers own data — consistent with the base design's split.** New per-Nation/per-Unit data (Veterancy, cooldowns, vision sets, Last_Known_Position) is added to existing state containers (`UnitInstance`, `Nation`) where the data is a durable attribute of that entity, and to new system-owned lookup structures where the data is a computed/derived cache (visible-cell sets, in-flight indirect-fire projectiles).

## Architecture

### Updated Layering Diagram

```
+--------------------------------------------------------------+
|  Presentation (Unity) — EXTENDED                              |
|  - HUD / Info Panels / Zoom Detail View (existing)           |
|  - GraphicsSettingsController + GraphicsSettingsViewModel NEW |
|  - EntityViewManager: LOD/silhouette + Visual_Detail_Tier NEW |
|  - TerrainRenderer: Cell_Material textures/normals UPDATED   |
|  - VfxSystem + EffectPool (combat/destruction VFX)  NEW      |
|  - AtmosphereController (skybox/fog/weather/ambient) NEW     |
+----------------------------↑----------------------------------+
                             | reads replicated state + GameEvents, emits intents
+----------------------------↓----------------------------------+
|  Sync Layer (NGO) — unchanged from base spec                 |
+----------------------------↑----------------------------------+
                             | applies validated commands
+----------------------------↓----------------------------------+
|  Simulation Core (plain C#, no UnityEngine) — EXTENDED        |
|  - CombatSystem: flanking, cover, AoE, indirect fire  UPDATED|
|  - UnitSystem: veterancy XP hook, ability activation  UPDATED|
|  - TerrainSystem: Cover qualification query           UPDATED|
|  - VisionSystem (per-Nation visible-cell + LKP)       NEW    |
|  - CoverClassifier (static pure classification helper)NEW    |
|  - FlankClassifier (static pure angle classification) NEW    |
|  - Commands/: ActivateAbilityCommand, IndirectFireCommand NEW|
+----------------------------↑----------------------------------+
                             | authored content
+----------------------------↓----------------------------------+
|  Content (ScriptableObjects) — EXTENDED                       |
|  - UnitAsset/StructureAsset: Visual_Detail_Tier field NEW     |
|  - UnitAsset: ability list, veterancy curve, artillery NEW    |
|  - GraphicsPresetAsset (Low/Med/High/Ultra bundles)   NEW    |
+--------------------------------------------------------------+
```

### File-Level Additions by Directory

```
Assets/EpochWar/
  Core/
    State/
      UnitInstance.cs                 # UPDATED: +Facing, +VeterancyTier, +VeterancyXp, +AbilityCooldowns
      Content/
        UnitDef.cs                    # UPDATED: +SightRadius, +AbilityDefs, +VeterancyCurve, +IsArtillery,
                                       #          +IndirectFireRange, +IndirectFireFlightDelay,
                                       #          +AreaEffectRadius, +VisualDetailTier
        StructureDef.cs               # UPDATED: +SightRadius, +VisualDetailTier
        VeterancyTierDef.cs           # NEW: tier name, xp threshold, attack/defense bonus
        UnitAbilityDef.cs             # NEW: id, cooldown seconds, resource cost, effect descriptor
      Facing.cs                       # NEW: enum { Front, Side, Rear } + FacingDirection value type
      VisionState.cs                  # NEW: per-Nation visible-cell set + Last_Known_Position map
    Systems/
      CombatSystem.cs                 # UPDATED: flanking/cover/AoE/indirect-fire resolution
      UnitSystem.cs                   # UPDATED: veterancy XP hook, ActivateAbilityCommand handler
      TerrainSystem.cs                # UPDATED: exposes ICoverQualification query per Terrain_Cell
      VisionSystem.cs                 # NEW: per-Nation visible-cell computation, LKP lifecycle
      CoverClassifier.cs              # NEW: static pure Cell_Material/elevation cover classification
      FlankClassifier.cs              # NEW: static pure angle-threshold flank classification
    Commands/
      ActivateAbilityCommand.cs       # NEW
      IndirectFireCommand.cs          # NEW
    Simulation/
      MatchSimulation.cs              # UPDATED: inserts VisionSystem.Tick, CombatSystem.Tick
  Unity/
    UI/
      GraphicsSettingsController.cs   # NEW
      GraphicsSettingsViewModel.cs    # NEW
      GraphicsSettingsStore.cs        # NEW (persistence: JSON file, validated on load)
      InfoPanelViewModel.cs           # UPDATED: +veterancy tier display, +ability controls/cooldowns
      CommandAvailability.cs          # UPDATED: +ability activation availability predicate
    Entities/
      TerrainRenderer.cs              # UPDATED: Cell_Material diffuse+normal, fallback material, decals
      EntityViewManager.cs            # UPDATED: Visual_Detail_Tier, density LOD, far-zoom silhouette
      VfxSystem.cs                    # NEW: spawns/cleans up combat+destruction+indirect-fire VFX
      EffectPool.cs                   # NEW: pooled particle/decal lifetime management
      AtmosphereController.cs         # NEW: skybox/fog/weather/ambient lighting
    Content/
      UnitAsset.cs                    # UPDATED: +VisualDetailTier override, +AbilityDefs, +VeterancyCurve
      StructureAsset.cs               # UPDATED: +VisualDetailTier override
      GraphicsPresetAsset.cs          # NEW: authored Low/Medium/High/Ultra bundles
```

### Updated Simulation Loop

`MatchSimulation.Tick(fixedDt)` gains two new steps. `VisionSystem.Tick` is inserted **after** `UnitSystem.Tick` and `TerrainSystem.Tick` and **before** `VictorySystem.Tick`, because Requirement 14.7 requires vision recomputation whenever an owned Unit or Structure "moves, is created, or is removed" — and all three of those triggers can occur during `UnitSystem.Tick` (movement, build-queue completion spawning, `RemoveUnit` from combat) or `TerrainSystem.Tick` (support-loss removal). Placing `VisionSystem.Tick` after both guarantees it observes the final entity positions/existence for the tick before `VictorySystem` evaluates outcomes that may depend on visibility-derived UI state.

`CombatSystem` gains a `Tick(MatchState state, Fixed dt)` method for the first time (previously it was purely a called-on-demand resolver with no tick of its own) to advance **in-flight Indirect_Fire projectiles** — a list of pending `(targetLocation, remainingFlightTime, attackerAttack, areaEffectRadius, ...)` entries, following the same pattern as `UnitSystem`'s existing `AdvanceBuildQueue`/`AdvanceColonization`. This was chosen over a separate `IndirectFireSystem` because the in-flight-projectile resolution *is* combat resolution (it ends by calling the same defender-lookup-and-damage-apply logic `ResolveAttack` already uses for AoE), so keeping it inside `CombatSystem` avoids a second system needing access to the same `_civ`/`_units` dependencies `CombatSystem` already holds. `CombatSystem.Tick` runs immediately after `UnitSystem.Tick` (so a projectile that finishes its flight this tick can still be resolved before `TerrainSystem`/`VisionSystem` observe the resulting deaths/removals in the same tick).

Updated fixed order:

```
1. ApplyQueuedCommands        (unchanged — now also routes ActivateAbilityCommand, IndirectFireCommand)
2. TechSystem.Tick            (unchanged)
3. CivSystem.Tick             (unchanged)
4. BaseSystem.Tick            (unchanged)
5. UnitSystem.Tick            (unchanged, now also drives veterancy XP hook internally on combat events)
6. CombatSystem.Tick          (NEW — advances in-flight Indirect_Fire projectiles, resolves on arrival)
7. TerrainSystem.Tick         (unchanged)
8. VisionSystem.Tick          (NEW — recomputes per-Nation visible-cell sets, re-evaluates LKP)
9. VictorySystem.Tick         (unchanged)
10. TickCount++                (unchanged)
11. drain + return events      (unchanged)
```

`MatchSimulation`'s constructor is updated to construct `VisionSystem` and pass it (and `CombatSystem`) any dependencies needed (`VisionSystem` needs read access to `MatchState.Units`/`Structures`/`Terrain`; `CombatSystem` already holds `_civ`/`_units` references from the base spec and gains no new constructor dependency for its `Tick` method).

## Data Models

```csharp
namespace EpochWar.Core.State
{
    public enum Facing { North, East, South, West } // matches WorldPosition's fixed-point axes;
                                                      // FacingDirection below is the general-angle form

    public readonly struct FacingDirection
    {
        public Fixed AngleDegrees; // [0, 360), deterministic fixed-point angle
    }

    public enum Flank { Front, Side, Rear }

    public sealed class UnitInstance
    {
        // --- existing fields unchanged ---
        public int Id;
        public int OwnerNationId;
        public UnitDef Def;
        public int Health;
        public WorldPosition Position;
        public int? BattalionId;
        public UnitOrder CurrentOrder;

        // --- NEW for Requirement 9 (flanking) ---
        public FacingDirection Facing;                 // updated whenever CurrentOrder's movement direction changes

        // --- NEW for Requirement 12 (veterancy) ---
        public int VeterancyTierIndex;                 // 0-based index into UnitDef.VeterancyCurve; 0 = base/no tier
        public int VeterancyExperience;                 // accumulated XP, never decreases while the Unit exists

        // --- NEW for Requirement 13 (unit abilities) ---
        public Dictionary<string, Fixed> AbilityRemainingCooldown; // keyed by UnitAbilityDef.Id; absent/0 = ready
    }
}

namespace EpochWar.Core.State.Content
{
    public sealed class VeterancyTierDef
    {
        public string Id;               // e.g. "Recruit", "Veteran", "Elite"
        public int ExperienceThreshold; // accumulated XP required to reach this tier
        public int AttackBonus;
        public int DefenseBonus;
    }

    public sealed class UnitAbilityDef
    {
        public string Id;
        public Fixed CooldownSeconds;
        public ResourceCost Cost;              // reuses base spec's ResourceCost type
        public AbilityEffectKind EffectKind;    // e.g. Heal, Buff, Bombard, Cloak — resolved by UnitSystem
    }

    public sealed class UnitDef // existing fields unchanged; additions below
    {
        // --- existing fields: Id, Era, Cost, BuildTimeSeconds, PopulationCost, MaxHealth,
        //     Attack, Defense, MoveSpeed, Role, LaunchCost ---

        // --- NEW for Requirement 14 (vision) ---
        public Fixed SightRadius;

        // --- NEW for Requirement 13 (unit abilities) ---
        public List<UnitAbilityDef> AbilityDefs = new();

        // --- NEW for Requirement 12 (veterancy) ---
        public List<VeterancyTierDef> VeterancyCurve = new(); // ordered ascending by ExperienceThreshold

        // --- NEW for Requirement 15 (artillery/indirect fire) ---
        public bool IsArtillery;
        public Fixed IndirectFireRange;         // max range; 0/unused when IsArtillery is false
        public Fixed DirectFireRange;           // below this, direct fire applies instead of indirect
        public Fixed IndirectFireFlightDelay;   // seconds between accepted command and impact
        public Fixed AreaEffectRadius;          // 0 = single-target; >0 = Area_Effect (Req 11, reused by Req 15.6)

        // --- NEW for Requirement 7 (visual detail tier; Core-testable default derivation) ---
        public int VisualDetailTier;            // derived-by-Era default, or content-author override (see Req 7 mapping)
    }

    public sealed class StructureDef // existing fields unchanged; additions below
    {
        // --- NEW for Requirement 14 (vision) ---
        public Fixed SightRadius;

        // --- NEW for Requirement 7 ---
        public int VisualDetailTier;
    }
}

namespace EpochWar.Core.State
{
    // Owned and mutated exclusively by VisionSystem; NOT stored on Nation, because it is a
    // fully-derived cache recomputed every VisionSystem.Tick (Design Principle 5) rather than a
    // durable attribute of the Nation the way CompletedTechIds/Resources are.
    public sealed class VisionState
    {
        public HashSet<CellCoord> VisibleCells = new();
        public Dictionary<int, bool> EnemyVisibility = new();        // enemyUnitOrStructureId -> currently visible
        public Dictionary<int, WorldPosition> LastKnownPosition = new(); // enemyId -> LKP, present only while hidden
    }
}

namespace EpochWar.Core.Commands
{
    public sealed class ActivateAbilityCommand : ICommand
    {
        public int IssuingNationId { get; init; }
        public int UnitId;
        public string AbilityId;
        public WorldPosition? TargetPosition; // null for self/no-target abilities
    }

    public sealed class IndirectFireCommand : ICommand
    {
        public int IssuingNationId { get; init; }
        public int ArtilleryUnitId;
        public WorldPosition TargetLocation;
    }
}
```

**Design decision — where vision state lives:** `VisionState` is a new type owned and Tick-mutated by `VisionSystem`, keyed by Nation id inside `VisionSystem` itself (a `Dictionary<int, VisionState>`), rather than added as fields directly on `Nation`. This mirrors the base design's principle that systems own *logic* while `MatchState`/`Nation` own *data*, but is refined here: `Nation`'s existing fields (`Resources`, `CompletedTechIds`, `Battalions`) are all durable data that persist unless explicitly changed by a command — vision, by contrast, is fully recomputed from scratch every tick from current entity positions (Requirement 14.1/14.7/14.8), so it is a derived cache, not durable Nation data, and belongs with the system that derives it. `Last_Known_Position`, however, *is* durable (it persists across many ticks until overwritten or discarded per 14.9), so it is stored inside `VisionState` rather than recomputed, and `VisionSystem` is responsible for adding/removing entries at exactly the transition/removal moments Requirement 14.3/14.9 specify.

**Design decision — Facing representation:** rather than only exposing `Facing` (the four cardinal directions used by movement), `UnitInstance` also carries a general-purpose `FacingDirection` (a fixed-point angle in degrees) because Requirement 9.4 requires angle-threshold comparisons against an arbitrary attacker direction, not just cardinal-direction comparisons. `FacingDirection` is updated by `UnitSystem` whenever a Unit's movement order changes its direction of travel (a Unit facing its last movement direction is the simplest deterministic rule consistent with "current facing direction" in 9.4, and requires no new Player-facing command).

## Components and Interfaces

### CombatSystem — Requirements 9, 10, 11, 15
- `FlankClassifier.Classify(FacingDirection defenderFacing, WorldPosition defenderPos, WorldPosition attackerPos, Fixed frontArcDegrees, Fixed sideArcDegrees) -> Flank`: pure static function; computes the deterministic fixed-point angle between defender facing and defender→attacker direction, and maps every angle in `[0, 360)` to exactly one of `Front`/`Side`/`Rear` using the configured arc thresholds (Req 9.4).
- `ResolveAttack` (existing method, extended): after computing `EffectiveAttack`/`EffectiveDefense` as today, it now (a) calls `FlankClassifier.Classify` and adds the configured `Flanking_Bonus` to the attack's damage input when the result is `Side` or `Rear`, applying no bonus for `Front`, and the rear-bonus constant is configured to be `>=` the side-bonus constant (Req 9.1–9.3); (b) queries `CoverClassifier`/`TerrainSystem`'s cover-qualification data for the defender's current cell plus a structure-on-the-line-of-fire check, and applies the **greater** of the terrain/elevation `Cover_Bonus` and the structure `Cover_Bonus` to `EffectiveDefense` — never both (Req 10.1, 10.2, 10.5).
- `ResolveAreaAttack(MatchState state, int attackerUnitId, WorldPosition impactPoint, Fixed radius) -> events`: new method for Req 11; finds every `UnitInstance`/`StructureInstance` whose occupied space's nearest point is within `radius` of `impactPoint` (including the attacker's own Nation's entities), and for each independently invokes the same effective-attack/damage/clamp logic `ResolveAttack` uses (full, unreduced damage per target, honoring each target's own defense/cover), applying `FlankClassifier` only for `UnitInstance` targets and never for `StructureInstance` targets (Req 11.1–11.3).
- `Tick(MatchState state, Fixed dt)`: new method for Req 15; advances each pending in-flight `IndirectFireCommand` entry's remaining flight time by `dt`; when an entry's remaining time reaches zero it resolves damage at the stored target location (via `ResolveAreaAttack` when `AreaEffectRadius > 0`, otherwise a direct single-point resolution), **regardless of the issuing Nation's current Spotting status at resolution time** (Req 15.5).
- `IndirectFireCommandHandler.Handle`: validates `IsArtillery`, that `TargetLocation` is within `IndirectFireRange` and beyond `DirectFireRange`, and that the issuing Nation currently has Spotting (via `VisionSystem`) on `TargetLocation`; on success, enqueues a pending entry for `CombatSystem.Tick` with `IndirectFireFlightDelay` as the initial remaining time and returns `CommandResult.Accept`; on either failure it returns a distinct `CommandResult.Reject` reason ("out of range" vs "no spotting") with no state change (Req 15.1–15.4).

### TerrainSystem — Requirement 10.4
- `CoverClassifier.IsCoverQualifying(CellMaterial material, int cellElevation, int comparisonElevation) -> bool`: new static pure function; a cell qualifies for cover when its `CellMaterial` is one of the configured cover-qualifying materials (e.g. `Rock`, `Reinforced`) or when `cellElevation` exceeds `comparisonElevation` by at least a configured margin. `TerrainSystem` exposes a thin query method, `GetCoverQualification(CellCoord defenderCell, CellCoord attackerCell) -> bool`, that calls `CoverClassifier` against `TerrainVolume.Get(...)` — this is the exact data `CombatSystem.ResolveAttack` consumes to satisfy Req 10.4; no new mutable state is added to `TerrainVolume` itself since cover qualification is fully derived from existing `CellMaterial`/coordinate data.

### UnitSystem — Requirements 12, 13
- Existing `RemoveUnit`, called from `CombatSystem` on elimination and from `TerrainSystem` on support loss, now also discards that unit's veterancy fields as part of unit teardown (Req 12.5) — no separate cleanup call site needed since `RemoveUnit` is already the single removal path used by both systems.
- New internal hook `OnCombatResolved(CombatResolvedEvent evt)` (invoked by `UnitSystem.Tick` after draining `CombatResolvedEvent`s each tick, following the existing event-drain pattern) adds the configured damage-dealt or elimination experience value to the attacking Unit's `VeterancyExperience`, then advances `VeterancyTierIndex` to the highest tier in `UnitDef.VeterancyCurve` whose `ExperienceThreshold` does not exceed the new total, repeating across every tier crossed in one grant and never exceeding the highest defined tier index (Req 12.1, 12.2, 12.4); each tier crossed during this call emits a new `VeterancyTierAdvancedEvent(unitId, newTierIndex)` for the UI to consume (Req 12.6 — Core half only).
- New `ActivateAbilityCommandHandler.Handle`: looks up the target `UnitInstance` and its `Def.AbilityDefs` entry matching `AbilityId`; if `AbilityRemainingCooldown` for that ability is `<= 0` and the owning Nation's resources meet the ability's `Cost`, it executes the ability's `AbilityEffectKind` effect, deducts the cost via the existing `ResourceSystem` cost-deduction path, sets `AbilityRemainingCooldown[AbilityId] = CooldownSeconds`, and returns `CommandResult.Accept`; otherwise it returns `CommandResult.Reject` with a reason distinguishing `"cooldown-active"` from `"insufficient-resources"` and leaves cooldown/resource state unchanged (Req 13.2, 13.3). `UnitSystem.Tick` decrements every non-zero entry in every Unit's `AbilityRemainingCooldown` by `dt`, clamped at zero (feeds the Req 13.4 UI display; the >=1/sec UI refresh cadence itself is a Unity-side `InfoPanelController` polling concern, not Core logic).

### VisionSystem — Requirement 14
- `Tick(MatchState state)`: for each Nation, recomputes `VisionState.VisibleCells` as the union, over every owned `UnitInstance`/`StructureInstance`, of all `Terrain_Cell`s within that entity's `Def.SightRadius` (Req 14.1); this recomputation runs unconditionally every tick (cheapest correct implementation satisfying "recompute on move/create/remove," Req 14.7, since a per-entity dirty-flag optimization is a pure performance refinement with no observable behavior difference and is left as an implementation-time optimization, not a design requirement).
- After recomputing `VisibleCells`, iterates every enemy `UnitInstance`/`StructureInstance` and updates `VisionState.EnemyVisibility[id]` to `VisibleCells.Contains(entity.Position.Cell)` (Req 14.2, 14.8); on a `true`→`false` transition it writes `LastKnownPosition[id] = entity.Position` as of that exact tick (Req 14.3); on a `false`→`true` transition it removes any `LastKnownPosition[id]` entry (position resolution then naturally falls through to "current position," satisfying the visible branch of Req 14.6).
- `GetDisplayPosition(int nationId, int enemyId, WorldPosition currentPosition) -> WorldPosition?`: pure query used by the Unity UI layer; returns `currentPosition` when `EnemyVisibility[enemyId]` is true, returns `LastKnownPosition[enemyId]` when false and a LKP is recorded, and returns `null` (meaning: do not display) when false and no LKP is recorded — the single three-way resolution covering Req 14.4, 14.5, and the display half of 14.6.
- Hooked into `UnitSystem.RemoveUnit`/`BaseSystem` structure-removal paths (via a `GameEvent` subscription, consistent with the existing event-driven pattern) to discard `LastKnownPosition[id]` immediately when a hidden entity with a recorded LKP is permanently removed (Req 14.9).

### Graphics_Settings_System (Unity) — Requirement 2
- `GraphicsSettingsViewModel` (UnityEngine-light plain class, following `InfoPanelViewModel`'s testable-view-model convention): holds current values for every setting in Req 2.1 and exposes `ApplyPreset(QualityPreset)` which overwrites every preset-covered field (Req 2.2, 2.3) and `ApplyIndividualChange(field, value)` which mutates exactly one field without touching the rest (Req 2.4).
- `GraphicsSettingsController` (MonoBehaviour, UI Toolkit): binds UI controls to the view-model, applies immediate-effect changes to the URP `RenderPipelineAsset`/`QualitySettings`/`Volume` profile at once (Req 2.5), and for restart-required changes (e.g. resolution/VSync toggles the platform doesn't hot-swap) persists immediately and shows a restart-notice UI element without applying until next launch (Req 2.6).
- `GraphicsSettingsStore`: persists the view-model to a JSON file via `GraphicsSettingsController` on every change (Req 2.7); on `Game_Client` start, attempts to deserialize the persisted file — on any parse failure or an out-of-range value, falls back to the Low preset, raises a one-time "settings reset" UI notice, and continues startup rather than throwing (Req 2.8).

### Terrain_Renderer (Unity) — Requirements 3, 4
- Replaces the current per-`CellMaterial` vertex-color scheme with a per-`CellMaterial` `Cell_Material` asset (diffuse texture + normal map, URP-lit shader) selected per rendered face; `TerrainRenderer` looks up the assigned `Cell_Material` for each cell's `CellMaterial` enum value and falls back to a defined default `Cell_Material` (rendered, not skipped) when a terrain type has none assigned, so chunk mesh rebuilds always complete (Req 3.1, 3.2, 3.5). Chunk rebuild already triggers on `TerrainModifiedEvent` (existing pattern); no new event type needed for Req 3.3. Lit shading response to lighting is inherent to the URP lit shader (Req 3.4, ties to Req 1.2).
- The existing `OnTicked`/`TerrainModifiedEvent` handler gains destruction-VFX hooks: spawns a dust/debris `VfxSystem` effect at every modification (Req 4.1); when the modification's removed-cell power meets/exceeds `Destructive_Force_Threshold`, additionally spawns a crater decal (Req 4.2) and otherwise does not (Req 4.3); when cells are damaged but not removed, renders a scorch-mark decal instead (Req 4.4); excavation-only modifications (no accompanying weapon `GameEvent`, i.e. a `TerrainModifiedEvent` not correlated with a `CombatResolvedEvent`/ability effect) render neither decal (Req 4.5). All spawned dust/debris effects are registered with `EffectPool` for automatic removal within 5 seconds (Req 4.6).

### VfxSystem / EffectPool (Unity) — Requirement 5
- Subscribes to `CombatResolvedEvent`, unit/structure zero-health removal events, and Doomsday deployment events already emitted by Core. For each ranged `CombatResolvedEvent`, spawns a muzzle-flash at the attacker and a projectile-trail animating to the impact point (Req 5.1, 5.2); on arrival spawns either an impact effect or an explosion effect depending on the attack's configured explosiveness flag (Req 5.3, 5.4); on a Unit/Structure health-reaches-zero event spawns the corresponding death/collapse effect (Req 5.5, 5.6); on a Doomsday deployment event spawns a dedicated, visually distinct effect lasting 4–10 seconds (Req 5.7). `EffectPool` enforces a uniform 3-second removal ceiling for every effect in this section except the Doomsday effect, which it removes at up to 10 seconds (Req 5.8).

### AtmosphereController (Unity) — Requirement 6
- On Match start, applies the configured skybox/ambient preset for the Match's environment, or a defined default skybox when none is configured (Req 6.1, 6.2, 6.6); drives URP fog density from a per-environment `[0.0, 1.0]` configuration value (Req 6.3); activates/deactivates configured weather visual effects for their configured duration (Req 6.4), and if a weather effect asset fails to load, logs and continues the Match without blocking start (Req 6.5).

### EntityViewManager (Unity) — Requirements 7, 8
- Computes each Unit/Structure's `Visual_Detail_Tier` from `UnitDef.VisualDetailTier`/`StructureDef.VisualDetailTier` (itself either a content-author override from `UnitAsset`/`StructureAsset`, Req 7.4, or a pure Core-testable default-by-Era derivation — see Req 7 mapping below); an out-of-range or unset tier falls back to a defined default tier without failing to render (Req 7.5).
- Tracks visible-Unit density per camera view and reduces per-Unit rendering detail once a configurable density threshold is exceeded, targeting a minimum frame rate floor (Req 8.1); at/beyond a far-zoom camera-distance threshold, swaps affected Units to a simplified Nation-colored marker representation (Req 8.2) and restores full detail below the threshold (Req 8.3); maintains a hysteresis buffer between the simplify/restore thresholds plus a minimum 1-second re-toggle interval per Unit to prevent flicker (Req 8.4).

## Correctness Properties

### Prework: Testability Analysis for Requirements 1–8 (Visuals + Graphics Settings)

Requirements 1–8 are exclusively Unity-side presentation, rendering, and persistence concerns (Universal Render Pipeline adoption, graphics settings UI/persistence, terrain material rendering, VFX spawn/cleanup, atmosphere, Era-scaled visual tier, LOD/silhouette readability). None of these criteria express a pure-function input/output relationship over `EpochWar.Core` data that varies meaningfully across a large generated input space in a way that would benefit from 100+ randomized iterations; they are, respectively:

- **Req 1 (URP adoption), Req 3 (terrain materials), Req 4 (destruction VFX), Req 5 (combat VFX), Req 6 (atmosphere):** rendering/shader/particle configuration and lifecycle — classified **INTEGRATION** (PlayMode scene inspection: shader assignment, effect spawn/cleanup timing) or **SMOKE** (one-time pipeline-asset/shader-declaration checks). Verified by PlayMode tests and manual visual verification, per the base spec's precedent for treating rendering/networking as non-property-testable.
- **Req 2 (graphics settings):** the persistence/fallback logic (2.7, 2.8) and preset-application logic (2.2–2.4) *do* have pure-function shape (`ApplyPreset`, deserialize-or-fallback), but operate entirely on Unity-side `GraphicsSettingsViewModel`/platform APIs (`QualitySettings`, `Screen`) rather than `EpochWar.Core` logic; the design classifies these as **INTEGRATION**, consistent with Design Principle 4 (Unity-side systems are presentation-only) and the base spec's precedent of using PlayMode tests for Unity-side persistence/config concerns rather than Core property tests.
- **Req 7 (Era-scaled visual tier):** the monotonic-by-Era default-tier derivation function is a candidate pure function, but it is a simple, small-range, mostly-static lookup (tier as a function of Era ordinal) with limited combinatorial input space; the design specifies it as example-based unit tests (one example per Era boundary) rather than a property, consistent with "avoid writing too many [property] tests" for narrow, small-input-space logic — classified **EXAMPLE**.
- **Req 8 (LOD/readability):** density thresholds, far-zoom silhouette swapping, and hysteresis timing are camera/frame-rate-dependent Unity runtime behavior — classified **INTEGRATION** (PlayMode, manual frame-rate profiling).

No Correctness Properties are written for Requirements 1–8. The properties below cover Requirements 9–15 only, per the completed prework and property-reflection analysis.

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Side-Flank grants a bonus at least matched by Rear-Flank

*For any* attacking and defending Unit pair and any defender facing, when the attacker's position is classified as the defender's side Flank, the resolved attack's damage is strictly greater than the same attack resolved with no Flanking_Bonus; and for any pair additionally classified as rear Flank under an equivalent geometry, the rear-Flank damage bonus is never less than the side-Flank damage bonus.

**Validates: Requirements 9.1, 9.2**

### Property 2: Front-Flank applies no bonus

*For any* attacking and defending Unit pair whose geometry classifies as the defender's front Flank, the resolved attack's damage equals the damage computed with no Flanking_Bonus applied.

**Validates: Requirements 9.3**

### Property 3: Flank classification is total and mutually exclusive

*For any* defender facing direction and any attacker position, `FlankClassifier.Classify` returns exactly one of Front, Side, or Rear, and the same inputs always classify to the same result (no angle value is unclassified or ambiguous).

**Validates: Requirements 9.4**

### Property 4: Cover_Bonus tracks current position qualification

*For any* Unit and any sequence of positions it occupies over time, the terrain/elevation Cover_Bonus is applied to that Unit's defense at a given moment if and only if the Terrain_System's exposed cover-qualification data for that Unit's current position at that moment reports qualifying, and the bonus disappears in the same tick the Unit occupies a non-qualifying position.

**Validates: Requirements 10.1, 10.3, 10.4**

### Property 5: Structure-on-the-line grants a per-attack Cover_Bonus

*For any* attacker position, defender position, and Structure position such that the Structure lies on the direct line between attacker and defender at the moment of an attack, that attack's damage calculation includes the Structure-based Cover_Bonus against the defender's defense.

**Validates: Requirements 10.2**

### Property 6: Overlapping Cover bonuses take the greater, not the sum

*For any* attack in which both a terrain/elevation Cover_Bonus and a Structure-based Cover_Bonus apply, the total Cover_Bonus applied to the defender's defense equals the maximum of the two bonus values, and is never equal to their sum when the two values differ.

**Validates: Requirements 10.5**

### Property 7: Area_Effect selects exactly the targets within radius, including own-Nation entities

*For any* impact point, Area_Effect radius, and set of Units and Structures at generated positions (spanning multiple Nations, including the attacker's own), the set of entities that receive Area_Effect damage equals exactly the set whose occupied space has a nearest point within the radius of the impact point, with no exclusion based on owning Nation.

**Validates: Requirements 11.1**

### Property 8: Area_Effect damage is full and independent per target

*For any* set of two or more targets within an Area_Effect radius with differing defense values and Cover_Bonus states, each target's applied damage equals the same value it would receive as a lone target of an equivalent single-target attack (honoring its own defense and Cover_Bonus), and the sum of damage applied across all targets is never divided or reduced to distribute a fixed damage pool.

**Validates: Requirements 11.2**

### Property 9: Area_Effect Flanking applies to Units and never to Structures

*For any* Area_Effect attack affecting a mixed set of Units and Structures, each affected Unit's damage reflects Flank classification computed against the impact point, while no affected Structure's damage is modified by any Flanking_Bonus.

**Validates: Requirements 11.3**

### Property 10: Veterancy tier is a pure function of accumulated experience

*For any* Unit's starting (tier, experience) state and any sequence of experience-granting combat actions applied to it, the resulting Veterancy_Tier always equals the highest tier in the Unit's defined Veterancy_Curve whose experience threshold does not exceed the resulting accumulated experience, capped at the highest defined tier regardless of how much the accumulated experience exceeds that tier's threshold, and this holds identically whether the crossed thresholds are reached via one large grant or many small grants summing to the same total.

**Validates: Requirements 12.1, 12.2, 12.4**

### Property 11: Veterancy state is isolated to actions on that Unit

*For any* Unit and any sequence of simulation ticks and combat actions that do not involve that Unit dealing damage or being eliminated, that Unit's Veterancy_Tier and accumulated experience remain unchanged.

**Validates: Requirements 12.3**

### Property 12: Veterancy state is discarded on removal

*For any* Unit that is destroyed or otherwise permanently removed from the Match, no Veterancy_Tier or experience record for that Unit's id remains queryable afterward.

**Validates: Requirements 12.5**

### Property 13: Every tier crossed emits exactly one advancement event

*For any* experience grant that causes a Unit to cross one or more Veterancy_Tier thresholds in a single application, exactly one `VeterancyTierAdvancedEvent` is emitted per tier crossed, each carrying the correct successive tier index, and no event is emitted when no threshold is crossed.

**Validates: Requirements 12.6**

### Property 14: Available abilities exactly match the Unit type's defined list

*For any* UnitDef with a defined list of Unit_Abilities, every Unit instance of that type reports exactly that set of ability ids as available for activation, no more and no fewer.

**Validates: Requirements 13.1**

### Property 15: Ability activation under valid preconditions executes, deducts cost, and starts cooldown

*For any* Unit, Unit_Ability, and resource pool such that the ability's cooldown has fully elapsed and the pool meets or exceeds the ability's resource cost, activating the ability results in the ability's effect being executed, the pool being reduced by exactly the ability's cost, and the ability's remaining cooldown being set to its full defined duration.

**Validates: Requirements 13.2**

### Property 16: Ability activation under invalid preconditions is rejected without state change

*For any* Unit and Unit_Ability where either the cooldown has not fully elapsed or the resource pool is insufficient, activation is rejected with a reason that distinguishes the cooldown-active case from the insufficient-resources case, and both the ability's cooldown state and the Unit's resource pool remain exactly unchanged.

**Validates: Requirements 13.3**

### Property 17: Remaining cooldown decreases monotonically to zero and never goes negative

*For any* Unit_Ability with an active cooldown and any sequence of elapsed-time advances, the computed remaining cooldown value is non-increasing over time, reaches exactly zero once the full cooldown duration has elapsed, and is never negative.

**Validates: Requirements 13.4**

### Property 18: Visible-cell set equals the union of owned entities' sight radii

*For any* Nation and any set of owned Units and Structures at generated positions with generated Sight_Radius values, the Nation's computed visible-cell set equals exactly the union of all Terrain_Cells within each entity's Sight_Radius of that entity's position.

**Validates: Requirements 14.1**

### Property 19: Hidden/visible classification is exactly membership in the visible-cell set

*For any* Nation, enemy Unit or Structure, and visible-cell set, that enemy entity is classified hidden from the Nation if and only if its occupying Terrain_Cell is outside the Nation's visible-cell set — and this equivalence holds identically whether the entity is newly hidden, newly visible, or unchanged from the previous tick.

**Validates: Requirements 14.2**

### Property 20: Last_Known_Position captures the exact transition-moment position

*For any* enemy Unit or Structure transitioning from visible to hidden for a Nation at a given tick, the Last_Known_Position recorded for that entity equals that entity's position at that exact tick, not its position at any earlier or later tick.

**Validates: Requirements 14.3**

### Property 21: Displayed position resolves to exactly one of three cases

*For any* Nation, enemy Unit or Structure, and vision state, the position resolution for display returns: the entity's current position when it is visible; the recorded Last_Known_Position when it is hidden and a Last_Known_Position is recorded; and no displayable position when it is hidden and no Last_Known_Position has ever been recorded — with exactly one of these three cases applying at any given moment.

**Validates: Requirements 14.4, 14.5, 14.6**

### Property 22: Recompute triggers are consistent with a full recomputation

*For any* owned Unit or Structure move, creation, or removal, the Nation's visible-cell set and every enemy entity's hidden/visible classification immediately after the resulting recompute are identical to the values a fresh, from-scratch computation (per Property 18 and Property 19) would produce given the post-change entity set.

**Validates: Requirements 14.7, 14.8**

### Property 23: Last_Known_Position is discarded on removal-while-hidden

*For any* enemy Unit or Structure that is hidden from a Nation with a recorded Last_Known_Position and is then permanently removed from the Match, that Last_Known_Position record is discarded and is not present for that entity's id afterward.

**Validates: Requirements 14.9**

### Property 24: Indirect_Fire acceptance is exactly range-within-bounds and Spotting-present

*For any* Artillery_Unit, target location, and Spotting state, an Indirect_Fire command against that location is accepted if and only if the target location is beyond the Artillery_Unit's direct-fire range and within its maximum Indirect_Fire range, and the issuing Nation currently has Spotting on that location; every rejection carries an observable reason distinguishing an out-of-range rejection from a no-Spotting rejection, and a rejected command produces no state change.

**Validates: Requirements 15.1, 15.2, 15.3, 15.4**

### Property 25: Indirect_Fire damage resolves after the flight delay regardless of intervening Spotting loss

*For any* accepted Indirect_Fire command and any pattern of Spotting gained or lost by the issuing Nation during the flight window, the attack's damage is applied at exactly the tick corresponding to (acceptance time + the Artillery_Unit's defined flight delay), and the resolution is unaffected by the issuing Nation's Spotting status at or during that window.

**Validates: Requirements 15.5**

### Property 26: Resolving Indirect_Fire with a defined Area_Effect radius applies Area_Effect rules at impact

*For any* Indirect_Fire attack whose Artillery_Unit defines an Area_Effect radius, when that attack resolves, the set of damaged targets and the per-target damage independence at the impact location satisfy the same Area_Effect rules established by Property 7, Property 8, and Property 9.

**Validates: Requirements 15.6**

## Error Handling

This section extends the base design's error-handling list (command rejection as a first-class `CommandResult`, invariant guards via property tests, network resilience, terrain edge safety, deterministic RNG) with the following new failure modes introduced by this expansion:

- **Invalid or unreadable persisted graphics configuration.** `GraphicsSettingsStore` deserialization failures or out-of-range values fall back to the Low `Quality_Preset`, display a one-time notice, and allow startup to continue rather than throwing (Req 2.8).
- **Unknown ability id or unmet ability preconditions.** `ActivateAbilityCommandHandler` returns `CommandResult.Reject` with a reason distinguishing an unknown/unavailable ability, an active cooldown, and insufficient resources; no cooldown or resource state is mutated on rejection (Req 13.3).
- **Indirect_Fire range or Spotting violations.** `IndirectFireCommandHandler` returns `CommandResult.Reject` with a reason distinguishing "target beyond maximum range" from "no Spotting on target location"; no projectile is enqueued and no state changes on rejection (Req 15.3, 15.4).
- **Missing Cell_Material assignment.** `TerrainRenderer` substitutes a defined fallback `Cell_Material` and completes the chunk mesh rebuild rather than skipping the cell or failing the rebuild (Req 3.5).
- **Unavailable weather visual effect asset.** `AtmosphereController` logs and continues the Match without that weather effect rather than blocking Match start (Req 6.5).
- **Out-of-range or unset Visual_Detail_Tier.** `EntityViewManager` substitutes a defined default tier and continues rendering the Unit or Structure rather than failing to render it (Req 7.5).

## Testing Strategy

- **EditMode + FsCheck property tests (>= 100 iterations each)** validate every Correctness Property above (Properties 1–26), executed entirely against `EpochWar.Core` — `FlankClassifier`, `CoverClassifier`, `CombatSystem` (including its new `Tick` for in-flight Indirect_Fire), `UnitSystem`'s veterancy/ability logic, and `VisionSystem` — with no Unity Play loop, following the same pattern the base spec established for Properties 1–46. Each test is tagged `Feature: epoch-war-combat-visuals-expansion, Property N: <property text>`.
- **EditMode unit tests** cover specific examples and edge cases that complement the properties: boundary angle values at the front/side and side/rear arc thresholds (Property 3's boundaries), a Unit exactly at a Veterancy_Tier threshold, an Artillery_Unit target exactly at its maximum range, and the Req 7 default-visual-tier-by-Era lookup (classified EXAMPLE in the prework above, so it is unit-tested rather than property-tested).
- **PlayMode/integration tests and manual verification** cover all of Requirements 1–8 (URP shader/material assignment, graphics settings persistence and preset application against Unity `QualitySettings`, terrain chunk rebuild with textured materials, VFX spawn/cleanup timing, atmosphere/weather rendering, density-based LOD and far-zoom silhouette behavior including the hysteresis/re-toggle timing) — these are explicitly *not* property tests, mirroring how the base spec treated networking and other Unity-side/runtime-environment behavior as integration-tested rather than property-tested.
- **Regression guard.** All existing base-spec EditMode property tests (Properties 1–46 in `epoch-war-game`) continue to run unmodified against the extended `CombatSystem`/`UnitSystem`/`TerrainSystem`/`MatchSimulation`, verifying this expansion introduces no behavioral regression in base-spec functionality.
