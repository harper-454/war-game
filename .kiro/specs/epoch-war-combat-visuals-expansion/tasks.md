# Implementation Plan: Epoch War — Combat & Visuals Expansion

## Overview

This plan converts the Combat & Visuals Expansion design into incremental coding tasks, additive to the existing implemented `epoch-war-game` codebase. It is organized into two pillars that map exactly to the design's hard `EpochWar.Core`/`EpochWar.Unity` assembly boundary:

- **Visuals + Graphics Settings pillar (Requirements 1–8, tasks 1–10)** — entirely `EpochWar.Unity`. Presented first below, matching the design's stated priority order (Visuals first, then Combat Depth).
- **Combat Depth pillar (Requirements 9–15, tasks 11–25)** — entirely `EpochWar.Core`, with a final Unity wiring task (24) that connects the new Core data to UI/VFX.

The two pillars are independent per the design (Design Principle 2) and do not block each other — the Task Dependency Graph at the end of this document shows them advancing in parallel waves, converging only at the final checkpoint and at task 24 (which necessarily reads both new Core state and existing/new Unity VFX plumbing).

Property-based tests (FsCheck, EditMode, `EpochWar.Core` only) are marked optional with `*` and each references the specific correctness property (1–26) and requirements clause it validates, per `design.md`'s Correctness Properties section. PlayMode/integration tests for all Unity-side visuals work (Requirements 1–8) are explicitly **not** property tests, consistent with the design's prework classification of those requirements as INTEGRATION/SMOKE/EXAMPLE. A final regression task re-runs the base spec's Properties 1–46 unmodified to confirm no behavioral regression.

## Tasks

### Visuals + Graphics Settings Pillar (Requirements 1–8) — `EpochWar.Unity` only

- [ ] 1. Adopt Universal Render Pipeline
  - [ ] 1.1 Configure URP Renderer Asset and pipeline settings
    - Add/configure a URP Renderer Asset and Pipeline Asset and set it as the project's active Render Pipeline Asset; assign URP-declared lit shaders to existing Terrain, Unit, and Structure materials
    - _Requirements: 1.1, 1.2, 1.3_
  - [ ]* 1.2 Write PlayMode/integration test for URP pipeline configuration
    - Verify the active `RenderPipelineAsset` is the URP asset and that Terrain/Unit/Structure materials' shaders declare URP support; verify (via asmdef inspection) `EpochWar.Core` has no `UnityEngine` or URP package reference (not a property test)
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

- [ ] 2. Implement the Graphics Settings System
  - [ ] 2.1 Implement `GraphicsSettingsViewModel`
    - Hold current values for Quality_Preset, resolution, shadow quality, each individual Post_Processing_Effect, VSync, render/view distance, texture quality, and particle density
    - Implement `ApplyPreset(QualityPreset)` overwriting every preset-covered field, and `ApplyIndividualChange(field, value)` mutating exactly one field without touching the rest
    - _Requirements: 2.1, 2.2, 2.3, 2.4_
  - [ ] 2.2 Implement `GraphicsSettingsController` (UI Toolkit) with immediate and restart-required application
    - Bind UI controls to the view-model; apply immediate-effect changes to the URP `RenderPipelineAsset`/`QualitySettings`/`Volume` profile at once; for restart-required changes, persist immediately and show a restart-notice UI element without applying until next launch
    - _Requirements: 2.5, 2.6_
  - [ ] 2.3 Implement `GraphicsSettingsStore` persistence and startup fallback
    - Persist the view-model to a JSON file on every change; on `Game_Client` start, deserialize the persisted file, and on any parse failure or out-of-range value fall back to the Low preset, raise a one-time "settings reset" notice, and continue startup
    - _Requirements: 2.7, 2.8_
  - [ ]* 2.4 Write PlayMode/integration tests for the Graphics Settings System
    - Verify preset application overwrites all covered fields, an individual change after a preset leaves the rest untouched, a persist-then-reload round-trip restores values, and a corrupted/out-of-range persisted file falls back to Low with a notice (not property tests)
    - _Requirements: 2.2, 2.3, 2.4, 2.7, 2.8_

- [ ] 3. Upgrade Terrain Renderer material rendering
  - [ ] 3.1 Author Cell_Material assets and wire per-CellMaterial lookup with fallback
    - Add Cell_Material assets (diffuse texture + normal map, URP-lit shader) per terrain type; `TerrainRenderer` looks up the assigned Cell_Material per rendered cell and substitutes a defined fallback Cell_Material when a terrain type has none assigned, always completing the chunk mesh rebuild
    - Extend the existing `TerrainModifiedEvent` chunk-rebuild handler to reassign the rendered Cell_Material when a Terrain_Cell's terrain type changes
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_
  - [ ]* 3.2 Write PlayMode/integration tests for terrain material rendering
    - Verify correct Cell_Material assignment per terrain type, fallback rendering (not a skipped cell) when unassigned, and chunk mesh rebuild completing on a terrain type change (not property tests)
    - _Requirements: 3.1, 3.3, 3.5_

- [ ] 4. Implement EffectPool
  - [ ] 4.1 Implement pooled particle/decal lifetime management
    - Implement spawn/return pooling and automatic removal of pooled effect instances once a configurable per-category lifetime elapses
    - _Requirements: 4.6, 5.8_
  - [ ]* 4.2 Write PlayMode/integration tests for EffectPool removal timing
    - Verify pooled effects are removed at their configured ceiling and are correctly returned to the pool rather than destroyed-and-leaked (not property tests)
    - _Requirements: 4.6, 5.8_

- [ ] 5. Implement terrain destruction visual effects
  - [ ] 5.1 Hook dust/debris, crater, and scorch-mark VFX into TerrainRenderer's existing OnTicked handler
    - Spawn a dust/debris effect at every Terrain_Cell modification via EffectPool; spawn a crater decal when removed-cell destructive force meets/exceeds Destructive_Force_Threshold, and no crater decal otherwise; render a scorch-mark decal when cells are damaged but not removed; suppress both decals for excavation-only modifications with no correlated weapon effect
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6_
  - [ ]* 5.2 Write PlayMode/integration tests for terrain destruction VFX
    - Verify the crater/no-crater threshold boundary, scorch-mark rendering on partial damage, decal suppression on unweaponized excavation, and dust/debris removal within 5 seconds (not property tests)
    - _Requirements: 4.2, 4.3, 4.4, 4.5, 4.6_

- [ ] 6. Implement VfxSystem for combat and destruction effects
  - [ ] 6.1 Implement VfxSystem event subscriptions and effect spawning
    - Subscribe to `CombatResolvedEvent`, Unit/Structure zero-health removal events, and Doomsday deployment events; spawn a muzzle flash and projectile-trail effect on ranged attacks, an impact or explosion effect on arrival keyed by the attack's explosiveness flag, a death/collapse effect on zero health, and a dedicated 4–10 second Doomsday deployment effect distinct from impact/explosion effects
    - Register standard combat/destruction effects with EffectPool at a 3-second removal ceiling and the Doomsday effect at a 10-second ceiling
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8_
  - [ ]* 6.2 Write PlayMode/integration tests for VfxSystem
    - Verify effect spawn per event type, explosive-vs-impact branching, Doomsday effect distinctness and duration, and the 3s/10s removal ceilings (not property tests)
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8_

- [ ] 7. Implement AtmosphereController
  - [ ] 7.1 Implement skybox, fog, ambient, and weather rendering
    - Apply the configured (or a defined default) skybox and matching ambient lighting preset on Match start; drive URP distance fog density from a per-environment `[0.0, 1.0]` value; activate/deactivate configured weather visual effects for their configured duration; log and continue the Match without blocking start when a weather effect asset is unavailable
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6_
  - [ ]* 7.2 Write PlayMode/integration tests for AtmosphereController
    - Verify default-skybox fallback, fog density mapping across the configured range, weather effect activation/deactivation timing, and continue-without-blocking on a missing weather asset (not property tests)
    - _Requirements: 6.2, 6.3, 6.4, 6.5_

- [ ] 8. Author visuals content assets
  - [ ] 8.1 Add Visual_Detail_Tier fields to UnitAsset/StructureAsset and author GraphicsPresetAsset bundles
    - Add a `Visual_Detail_Tier` override field to `UnitAsset` and `StructureAsset` with Core-conversion wiring to `UnitDef.VisualDetailTier`/`StructureDef.VisualDetailTier`; author `GraphicsPresetAsset` entries for the Low/Medium/High/Ultra bundles consumed by `GraphicsSettingsViewModel.ApplyPreset`
    - _Requirements: 2.3, 7.4_
  - [ ] 8.2 Update ContentSeed with Cell_Material and Visual_Detail_Tier authoring data
    - Assign the authored Cell_Material references per seeded terrain type and set Visual_Detail_Tier values on existing seeded Unit/Structure content entries
    - _Requirements: 3.1, 7.4_

- [ ] 9. Implement Entity View System detail tier and battle readability
  - [ ] 9.1 Implement Visual_Detail_Tier assignment and fallback in EntityViewManager
    - Resolve each rendered Unit/Structure's Visual_Detail_Tier from its content-author override or an Era-derived default; substitute a defined default tier for unset or out-of-range values without failing to render
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5_
  - [ ]* 9.2 Write unit tests for the Era-derived default Visual_Detail_Tier lookup
    - One example per Era boundary confirming a non-decreasing tier by Era within the same classification category, and equal tiers for same-Era entries (classified EXAMPLE per the design prework — not a property test)
    - _Requirements: 7.1, 7.2, 7.3_
  - [ ] 9.3 Implement density-based LOD and far-zoom silhouette swapping with hysteresis
    - Reduce per-Unit rendering detail once visible-Unit density exceeds a configurable threshold, targeting a minimum frame-rate floor; swap to a simplified Nation-colored marker representation at/beyond a far-zoom distance threshold and restore full detail below it; maintain a hysteresis buffer between thresholds plus a minimum 1-second per-Unit re-toggle interval
    - _Requirements: 8.1, 8.2, 8.3, 8.4_
  - [ ]* 9.4 Write PlayMode/integration tests for LOD and far-zoom readability
    - Verify detail reduction above the density threshold, simplify/restore behavior at the far-zoom threshold, and the hysteresis/re-toggle interval preventing flicker (not property tests)
    - _Requirements: 8.1, 8.2, 8.3, 8.4_

- [ ] 10. Checkpoint - Ensure all Visuals pillar tests pass
  - Ensure all tests pass, ask the user if questions arise.

### Combat Depth Pillar (Requirements 9–15) — `EpochWar.Core`, independent of the Visuals pillar

- [ ] 11. Add Combat Depth data models
  - [ ] 11.1 Add Facing, FacingDirection, Flank, and VisionState types
    - Add the `Facing` enum, `FacingDirection` struct (fixed-point `AngleDegrees`), and `Flank` enum in `Core/State/Facing.cs`; add `VisionState` (`VisibleCells`, `EnemyVisibility`, `LastKnownPosition`) in `Core/State/VisionState.cs`
    - _Requirements: 9.4, 14.1, 14.2, 14.3_
  - [ ] 11.2 Extend UnitInstance, UnitDef, and StructureDef
    - Add `Facing` (FacingDirection), `VeterancyTierIndex`, `VeterancyExperience`, `AbilityRemainingCooldown` to `UnitInstance`; add `SightRadius`, `AbilityDefs`, `VeterancyCurve`, `IsArtillery`, `IndirectFireRange`, `DirectFireRange`, `IndirectFireFlightDelay`, `AreaEffectRadius`, `VisualDetailTier` to `UnitDef`; add `SightRadius`, `VisualDetailTier` to `StructureDef`
    - _Requirements: 9.4, 10.1, 11.1, 12.1, 12.3, 13.1, 14.1, 15.1, 15.6_
  - [ ] 11.3 Add VeterancyTierDef and UnitAbilityDef content POCOs
    - Add `VeterancyTierDef` (Id, ExperienceThreshold, AttackBonus, DefenseBonus) and `UnitAbilityDef` (Id, CooldownSeconds, Cost, EffectKind) in `Core/State/Content/`
    - _Requirements: 12.2, 13.1, 13.2_
  - [ ] 11.4 Add ActivateAbilityCommand and IndirectFireCommand
    - Add both `ICommand` implementations in `Core/Commands/`
    - _Requirements: 13.2, 15.1, 15.2_
  - [ ]* 11.5 Write unit tests for new data model defaults and schema
    - Assert default `VeterancyTierIndex`/`VeterancyExperience`/empty `AbilityRemainingCooldown` on a freshly constructed `UnitInstance`, and that `UnitDef`/`StructureDef` expose all new fields
    - _Requirements: 12.1, 12.3, 13.1, 14.1_

- [ ] 12. Implement FlankClassifier and CoverClassifier pure helpers
  - [ ] 12.1 Implement FlankClassifier.Classify
    - Static pure function computing the deterministic fixed-point angle between the defender's facing and the defender→attacker direction, mapping every angle in `[0, 360)` to exactly one of Front/Side/Rear using configured arc thresholds
    - _Requirements: 9.4_
  - [ ]* 12.2 Write property test for FlankClassifier
    - **Property 3: Flank classification is total and mutually exclusive** — **Validates: Requirements 9.4**
    - FsCheck, >= 100 iterations, tagged `Feature: epoch-war-combat-visuals-expansion, Property 3`
  - [ ] 12.3 Implement CoverClassifier.IsCoverQualifying
    - Static pure function: a cell qualifies for Cover when its `CellMaterial` is one of the configured cover-qualifying materials, or when its elevation exceeds a comparison elevation by a configured margin
    - _Requirements: 10.1_
  - [ ]* 12.4 Write unit tests for CoverClassifier boundary cases
    - Cover-qualifying material at equal elevation, non-qualifying material at equal elevation, and exactly-at-margin elevation boundary
    - _Requirements: 10.1_

- [ ] 13. Extend TerrainSystem with a cover-qualification query
  - [ ] 13.1 Implement TerrainSystem.GetCoverQualification
    - Thin query method calling `CoverClassifier` against `TerrainVolume.Get(...)` for a defender-cell/attacker-cell pair; no new mutable state added to `TerrainVolume`
    - _Requirements: 10.4_
  - [ ]* 13.2 Write unit tests for GetCoverQualification query correctness
    - Verify the query returns qualifying for a cover-qualifying material or elevation margin and non-qualifying otherwise, matching `CoverClassifier` directly
    - _Requirements: 10.4_

- [ ] 14. Extend CombatSystem with flanking and cover in ResolveAttack
  - [ ] 14.1 Apply Flanking_Bonus in ResolveAttack
    - Call `FlankClassifier.Classify` and add the configured Flanking_Bonus to the attack's damage input for Side/Rear classifications, no bonus for Front, with the rear-bonus constant configured `>=` the side-bonus constant
    - _Requirements: 9.1, 9.2, 9.3_
  - [ ]* 14.2 Write property tests for flanking damage bonuses
    - **Property 1: Side-Flank grants a bonus at least matched by Rear-Flank** — **Validates: Requirements 9.1, 9.2**
    - **Property 2: Front-Flank applies no bonus** — **Validates: Requirements 9.3**
    - FsCheck, >= 100 iterations each, tagged `Feature: epoch-war-combat-visuals-expansion, Property 1` / `Property 2`
  - [ ] 14.3 Apply terrain/elevation and structure-on-the-line Cover_Bonus in ResolveAttack
    - Query `CoverClassifier`/`TerrainSystem.GetCoverQualification` for the defender's current cell plus a structure-on-the-line-of-fire check; apply the greater of the terrain/elevation Cover_Bonus and the structure Cover_Bonus to `EffectiveDefense`, never both; remove the terrain/elevation bonus the same tick the defender occupies a non-qualifying position
    - _Requirements: 10.1, 10.2, 10.3, 10.5_
  - [ ]* 14.4 Write property tests for cover bonus tracking and overlap resolution
    - **Property 4: Cover_Bonus tracks current position qualification** — **Validates: Requirements 10.1, 10.3, 10.4**
    - **Property 5: Structure-on-the-line grants a per-attack Cover_Bonus** — **Validates: Requirements 10.2**
    - **Property 6: Overlapping Cover bonuses take the greater, not the sum** — **Validates: Requirements 10.5**
    - FsCheck, >= 100 iterations each, tagged `Feature: epoch-war-combat-visuals-expansion, Property 4` / `Property 5` / `Property 6`

- [ ] 15. Checkpoint - Ensure flanking and cover tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 16. Implement Area_Effect damage resolution
  - [ ] 16.1 Implement CombatSystem.ResolveAreaAttack
    - Find every `UnitInstance`/`StructureInstance` whose occupied space's nearest point is within the Area_Effect radius of the impact point, including the attacker's own Nation's entities; invoke the same full, unreduced per-target effective-attack/damage/defense/cover logic `ResolveAttack` uses, independently per target; apply `FlankClassifier` only to `UnitInstance` targets, never to `StructureInstance` targets
    - _Requirements: 11.1, 11.2, 11.3_
  - [ ]* 16.2 Write property tests for Area_Effect resolution
    - **Property 7: Area_Effect selects exactly the targets within radius, including own-Nation entities** — **Validates: Requirements 11.1**
    - **Property 8: Area_Effect damage is full and independent per target** — **Validates: Requirements 11.2**
    - **Property 9: Area_Effect Flanking applies to Units and never to Structures** — **Validates: Requirements 11.3**
    - FsCheck, >= 100 iterations each, tagged `Feature: epoch-war-combat-visuals-expansion, Property 7` / `Property 8` / `Property 9`

- [ ] 17. Implement Unit veterancy progression
  - [ ] 17.1 Implement UnitSystem.OnCombatResolved veterancy XP hook and tier advancement
    - Invoke from `UnitSystem.Tick` after draining `CombatResolvedEvent`s each tick; add the configured damage-dealt or elimination experience value to the attacking Unit's `VeterancyExperience`; advance `VeterancyTierIndex` across every tier crossed in one grant, capped at the highest defined tier; emit one `VeterancyTierAdvancedEvent` per tier crossed
    - _Requirements: 12.1, 12.2, 12.4, 12.6_
  - [ ] 17.2 Discard veterancy state on Unit removal
    - Extend the existing `RemoveUnit` teardown path (already called by CombatSystem elimination and TerrainSystem support loss) to discard `VeterancyTierIndex`/`VeterancyExperience` for the removed Unit's id
    - _Requirements: 12.3, 12.5_
  - [ ]* 17.3 Write property tests for veterancy progression
    - **Property 10: Veterancy tier is a pure function of accumulated experience** — **Validates: Requirements 12.1, 12.2, 12.4**
    - **Property 11: Veterancy state is isolated to actions on that Unit** — **Validates: Requirements 12.3**
    - **Property 12: Veterancy state is discarded on removal** — **Validates: Requirements 12.5**
    - **Property 13: Every tier crossed emits exactly one advancement event** — **Validates: Requirements 12.6**
    - FsCheck, >= 100 iterations each, tagged `Feature: epoch-war-combat-visuals-expansion, Property 10` / `Property 11` / `Property 12` / `Property 13`

- [ ] 18. Implement Unit abilities
  - [ ] 18.1 Implement ActivateAbilityCommandHandler
    - Look up the target `UnitInstance` and its `Def.AbilityDefs` entry matching `AbilityId`; when `AbilityRemainingCooldown` is `<= 0` and the owning Nation's resources meet the ability's cost, execute the ability's `AbilityEffectKind` effect, deduct the cost via the existing `ResourceSystem` path, and set `AbilityRemainingCooldown[AbilityId]` to the full cooldown duration; otherwise reject with a reason distinguishing `"cooldown-active"` from `"insufficient-resources"` and leave cooldown/resource state unchanged
    - _Requirements: 13.1, 13.2, 13.3_
  - [ ] 18.2 Implement per-tick ability cooldown decrement
    - `UnitSystem.Tick` decrements every non-zero entry in every Unit's `AbilityRemainingCooldown` by `dt`, clamped at zero
    - _Requirements: 13.4_
  - [ ]* 18.3 Write property tests for unit abilities
    - **Property 14: Available abilities exactly match the Unit type's defined list** — **Validates: Requirements 13.1**
    - **Property 15: Ability activation under valid preconditions executes, deducts cost, and starts cooldown** — **Validates: Requirements 13.2**
    - **Property 16: Ability activation under invalid preconditions is rejected without state change** — **Validates: Requirements 13.3**
    - **Property 17: Remaining cooldown decreases monotonically to zero and never goes negative** — **Validates: Requirements 13.4**
    - FsCheck, >= 100 iterations each, tagged `Feature: epoch-war-combat-visuals-expansion, Property 14` / `Property 15` / `Property 16` / `Property 17`

- [ ] 19. Checkpoint - Ensure veterancy and abilities tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 20. Implement VisionSystem
  - [ ] 20.1 Implement VisionSystem.Tick visible-cell computation and hidden/visible classification
    - Per Nation, recompute `VisionState.VisibleCells` as the union, over every owned `UnitInstance`/`StructureInstance`, of all Terrain_Cells within that entity's `Def.SightRadius`; update `EnemyVisibility[id]` for every enemy entity; on a true→false transition write `LastKnownPosition[id]` at that exact tick; on a false→true transition remove any `LastKnownPosition[id]` entry
    - _Requirements: 14.1, 14.2, 14.3, 14.7, 14.8_
  - [ ] 20.2 Implement VisionSystem.GetDisplayPosition query
    - Return the entity's current position when visible, the recorded `LastKnownPosition` when hidden with one recorded, and `null` when hidden with none recorded
    - _Requirements: 14.4, 14.5, 14.6_
  - [ ] 20.3 Discard Last_Known_Position on permanent removal while hidden
    - Subscribe to the Unit/Structure removal events already emitted by `UnitSystem.RemoveUnit`/`BaseSystem` structure removal; discard `LastKnownPosition[id]` immediately when a hidden entity with a recorded LKP is permanently removed
    - _Requirements: 14.9_
  - [ ]* 20.4 Write property tests for VisionSystem
    - **Property 18: Visible-cell set equals the union of owned entities' sight radii** — **Validates: Requirements 14.1**
    - **Property 19: Hidden/visible classification is exactly membership in the visible-cell set** — **Validates: Requirements 14.2**
    - **Property 20: Last_Known_Position captures the exact transition-moment position** — **Validates: Requirements 14.3**
    - **Property 21: Displayed position resolves to exactly one of three cases** — **Validates: Requirements 14.4, 14.5, 14.6**
    - **Property 22: Recompute triggers are consistent with a full recomputation** — **Validates: Requirements 14.7, 14.8**
    - **Property 23: Last_Known_Position is discarded on removal-while-hidden** — **Validates: Requirements 14.9**
    - FsCheck, >= 100 iterations each, tagged `Feature: epoch-war-combat-visuals-expansion, Property 18` through `Property 23`

- [ ] 21. Implement artillery and indirect fire
  - [ ] 21.1 Implement IndirectFireCommandHandler
    - Validate `IsArtillery`, that `TargetLocation` is within `IndirectFireRange` and beyond `DirectFireRange`, and that the issuing Nation currently has Spotting (via `VisionSystem`) on `TargetLocation`; on success enqueue a pending entry with `IndirectFireFlightDelay` as the initial remaining time and return Accept; on failure reject with a reason distinguishing "out of range" from "no spotting", with no state change
    - _Requirements: 15.1, 15.2, 15.3, 15.4_
  - [ ] 21.2 Implement CombatSystem.Tick for in-flight Indirect_Fire projectiles
    - Advance each pending entry's remaining flight time by `dt`; when it reaches zero, resolve damage at the stored target location via `ResolveAreaAttack` when `AreaEffectRadius > 0`, otherwise a direct single-point resolution, regardless of the issuing Nation's Spotting status at resolution time
    - _Requirements: 15.5, 15.6_
  - [ ]* 21.3 Write property tests for indirect fire
    - **Property 24: Indirect_Fire acceptance is exactly range-within-bounds and Spotting-present** — **Validates: Requirements 15.1, 15.2, 15.3, 15.4**
    - **Property 25: Indirect_Fire damage resolves after the flight delay regardless of intervening Spotting loss** — **Validates: Requirements 15.5**
    - **Property 26: Resolving Indirect_Fire with a defined Area_Effect radius applies Area_Effect rules at impact** — **Validates: Requirements 15.6**
    - FsCheck, >= 100 iterations each, tagged `Feature: epoch-war-combat-visuals-expansion, Property 24` / `Property 25` / `Property 26`

- [ ] 22. Wire MatchSimulation updates and command routing
  - [ ] 22.1 Update MatchSimulation constructor and Tick order
    - Construct `VisionSystem` with read access to `MatchState.Units`/`Structures`/`Terrain`; insert `CombatSystem.Tick` immediately after `UnitSystem.Tick` and `VisionSystem.Tick` after `TerrainSystem.Tick` and before `VictorySystem.Tick`, per the updated fixed tick order
    - _Requirements: 14.7_
  - [ ] 22.2 Route ActivateAbilityCommand and IndirectFireCommand through the existing CommandRouter
    - Register both new command handlers with the `CommandRouter`'s existing ownership/turn-check dispatch path used by every other command
    - _Requirements: 13.2, 15.1_
  - [ ]* 22.3 Write regression property sweep for base-spec Properties 1–46
    - Re-run all existing `epoch-war-game` EditMode property tests unmodified against the extended `CombatSystem`/`UnitSystem`/`TerrainSystem`/`MatchSimulation` to confirm this expansion introduces no behavioral regression
    - _Requirements: (regression guard — no new requirements clause)_

- [ ] 23. Checkpoint - Ensure Combat Depth pillar tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 24. Wire Unity UI and VFX to Combat Depth features
  - [ ] 24.1 Update InfoPanelViewModel for veterancy tier and ability display
    - Display a selected Unit's current Veterancy_Tier and, for each available Unit_Ability, a selectable control showing remaining cooldown updated at least once per second
    - _Requirements: 12.6, 13.1, 13.4_
  - [ ] 24.2 Implement ability activation in CommandControlsController
    - Wire the ability control's selection to issue an `ActivateAbilityCommand` for the selected Unit and chosen Unit_Ability
    - _Requirements: 13.2_
  - [ ] 24.3 Extend CommandAvailability with an ability activation predicate
    - Bind the ability control's enabled state to "cooldown fully elapsed AND resources sufficient", consistent with the existing command-control availability pattern
    - _Requirements: 13.3_
  - [ ] 24.4 Update UI position display for VisionSystem hidden/Last_Known_Position state
    - Every UI element that displays Unit/Structure positions shows the Last_Known_Position while hidden-with-LKP, the current position while visible, and suppresses display while hidden with no LKP, via `VisionSystem.GetDisplayPosition`
    - _Requirements: 14.4, 14.5, 14.6, 14.9_
  - [ ] 24.5 Render Indirect_Fire projectile trail VFX regardless of Spotting
    - Hook VfxSystem to render an arcing projectile trail, visible to all Nations, for every Indirect_Fire attack for the duration of that attack's flight delay
    - _Requirements: 15.7_
  - [ ]* 24.6 Write PlayMode/integration tests for Combat Depth UI and VFX wiring
    - Verify InfoPanel veterancy/ability display and cooldown refresh cadence, ability-control enablement transitions, hidden/LKP/visible position display switching across all position-displaying UI elements, and indirect-fire trail rendering/visibility (not property tests)
    - _Requirements: 12.6, 13.1, 13.3, 13.4, 14.4, 14.5, 14.6, 15.7_

- [ ] 25. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for a faster MVP; they cover property tests, unit tests, and PlayMode/integration tests. Sub-tasks marked `*` are not implemented as part of executing this plan.
- All Combat Depth logic (tasks 11–22) lives in `EpochWar.Core` (no `UnityEngine` reference) so its property tests run under EditMode with no Play loop, exactly matching the base spec's pattern.
- Property tests use FsCheck (>= 100 iterations each) and are tagged `Feature: epoch-war-combat-visuals-expansion, Property N`.
- All Visuals + Graphics Settings tasks (1–10) are verified by PlayMode/integration tests and, where noted, plain unit tests — never property tests — per the design's prework classification of Requirements 1–8 as INTEGRATION, SMOKE, or EXAMPLE.
- Task 22.3 (regression sweep) and task 10/23/25 checkpoints guard against introducing any behavioral regression into the base `epoch-war-game` spec's Properties 1–46.
- Task 24 is the only task that reads from both pillars (new Core veterancy/ability/vision/indirect-fire state and existing/new Unity VFX/UI plumbing); every other task in one pillar has no dependency on the other pillar, matching the design's stated assembly-boundary independence.
- Each task references specific requirement clauses for traceability; checkpoints ensure incremental validation.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "2.1", "4.1", "11.1", "11.2", "11.3", "11.4", "12.3"] },
    { "id": 1, "tasks": ["1.2", "2.2", "2.3", "3.1", "4.2", "6.1", "7.1", "8.1", "11.5", "12.1", "12.4", "13.1", "17.1", "18.1", "20.1"] },
    { "id": 2, "tasks": ["2.4", "3.2", "5.1", "6.2", "7.2", "8.2", "9.1", "12.2", "13.2", "14.1", "14.3", "17.2", "18.2", "20.2", "20.3", "21.1", "24.1", "24.2", "24.3"] },
    { "id": 3, "tasks": ["5.2", "9.2", "9.3", "14.2", "14.4", "16.1", "17.3", "18.3", "20.4", "22.2", "24.4"] },
    { "id": 4, "tasks": ["9.4", "15", "16.2", "21.2"] },
    { "id": 5, "tasks": ["10", "19", "21.3", "22.1", "24.5"] },
    { "id": 6, "tasks": ["22.3", "24.6"] },
    { "id": 7, "tasks": ["23"] },
    { "id": 8, "tasks": ["25"] }
  ]
}
```
