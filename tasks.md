# Implementation Plan: Epoch War — Completion & Expansion

## Overview

Convert the feature design into a series of incremental, code-generation-ready steps. Each step builds on prior ones and ends by wiring new pieces into the existing simulation and presentation. The plan is strictly additive: engine-free `EpochWar.Core` systems (no `UnityEngine`) come first — content POCOs/catalog interfaces, then the `World_Generator` pipeline, the customization resolver, the `Audio_Director` decision layer, and `Match_Configuration`/`VictorySystem`/`MatchBootstrapper` — each immediately followed by its FsCheck EditMode property tests. Unity-side presentation (`AudioSystem` + mixer/pool/players, `SkirmishSetup`/`Options` UI Toolkit screens, `InfoPanel` updates, `MatchNetworkManager` config distribution + layout-hash verify) follows with PlayMode/integration tests. New Core fields and constructor parameters use behavior-preserving defaults so base Properties 1–46 and combat-visuals Properties 1–26 remain valid unchanged.

**Assembly boundary (non-negotiable):** every `Core/*` task stays engine-free (`EpochWar.Core.asmdef`, no `UnityEngine` / Unity package references); every `Unity/*` task lives in `EpochWar.Unity`. Engine-light view-models live under `EpochWar.Unity.UI` but avoid runtime Unity dependencies so their logic is exercised headless by FsCheck EditMode tests, matching the existing `InfoPanelViewModelPropertyTests` pattern.

**Test placement:** all property tests are added under `Assets/EpochWar/Tests/EditMode` in the `EpochWar.Tests.EditMode` assembly (FsCheck, >= 100 iterations, tagged `Feature: epoch-war-completion-expansion, Property N`). PlayMode/integration tests live in a PlayMode test assembly. Each property test task targets its own dedicated test file.

## Tasks

- [x] 1. Content POCOs, additive state, and catalog interfaces (engine-free)
  - [x] 1.1 Add world-generation content POCOs
    - Create `Core/State/Content/BiomeDef.cs`, `MapSizeDef.cs`, `PlanetProfileDef.cs`, and the `SymmetryMode` enum in `Core/Generation/` (or `Content`), all engine-free
    - `MapSizeDef` carries `Order`, `Width`, `Depth`, `MaxHeight`; `BiomeDef` carries `SurfaceMaterial` (CellMaterial), `SelectionWeight`, nullable `AmbienceId`; `PlanetProfileDef` references biome ids, map size, densities, symmetry
    - _Requirements: 2.1, 2.2, 5.2, 5.3, 5.4, 6.3_

  - [x] 1.2 Add unit-customization content POCOs and extend `UnitDef`
    - Create `Core/State/Content/CustomizationModifier.cs` (with `AttributeTarget`, `ModifierOp` enums), `LoadoutSlotDef.cs` (with `SlotType`), `ModuleDef.cs`, `UnitVariantDef.cs`
    - Extend `Core/State/Content/UnitDef.cs` with an additive `IReadOnlyList<LoadoutSlotDef> LoadoutSlots` defaulting to empty
    - _Requirements: 8.1, 8.4, 9.1, 10.1_

  - [x] 1.3 Add audio content POCOs
    - Create `Core/State/Content/SoundEventDef.cs`, `MusicTrackSetDef.cs`, `AmbienceDef.cs`, and enums `VolumeBus`, `AdaptiveMusicState`, `FalloffKind`
    - `SoundEventDef` carries `Bus`, `Spatialized`, `MaxAudibleDistance` (Fixed), `Falloff`, `Priority`; `AmbienceDef` carries `WeatherLayerByWeatherId` + `CrossfadeSeconds`
    - _Requirements: 11.1, 11.2, 11.4, 12.2, 13.1, 13.3, 14.2_

  - [x] 1.4 Add additive match/customization state POCOs and extend `Nation`/`UnitInstance`
    - Create `Core/State/UnitLoadout.cs` (immutable slot→module map with `Empty`/`With`/`Without`) and `Core/State/CustomizedUnitProfile.cs`
    - Extend `Core/State/Nation.cs` with `TeamId` (default: unique per Nation) and `Core/State/UnitInstance.cs` with nullable `Loadout` and `Profile` (default null → base-stat fallback)
    - _Requirements: 8.5, 10.1, 17.5_

  - [x] 1.5 Add additive catalog interfaces on `InMemoryCatalog`
    - Define engine-free `IWorldGenCatalog`, `ICustomizationCatalog`, `IAudioCatalog` interfaces; implement them on `Core/State/Content/InMemoryCatalog.cs` with new optional constructor collections; leave `ICatalog` unchanged
    - _Requirements: 4.4, 5.4, 8.4, 9.1_

  - [x]* 1.6 Write example/unit tests for content shape and catalog conversions
    - Verify catalog lookups for Map_Size/Biome/Planet_Profile/Module/Loadout_Slot/Unit_Variant/Sound_Event/music/ambience and `MapSizeDef.Order` non-decreasing area
    - _Requirements: 5.3, 5.4, 6.3, 8.1, 8.4, 9.1, 14.2_

- [x] 2. World_Generator deterministic primitives (engine-free)
  - [x] 2.1 Implement `GenerationSeed` with sub-stream derivation
    - Create `Core/Generation/GenerationSeed.cs` holding a `ulong Value` and `Mix(seed, stageId)` splitmix-style sub-stream derivation over `DeterministicRandom`
    - _Requirements: 1.3, 6.4_

  - [x] 2.2 Implement float-free `ValueNoise`
    - Create `Core/Generation/ValueNoise.cs`: hashed-lattice value noise + fractal octave sum entirely in `Fixed`, normalized by total amplitude; no `float`/`double`
    - _Requirements: 1.3_

  - [x] 2.3 Implement `GenerationParameters` with validation
    - Create `Core/Generation/GenerationParameters.cs` (MapSize, biomes, NationCount, Symmetry, densities, WaterLevel, MaxPlacementAttempts, MinStartSeparationCells, StartRegionRadius, MinDepositsPerType)
    - Implement `Validate(IWorldGenCatalog)` enforcing completeness, in-range values, and that every referenced Biome has a Cell_Material mapping
    - _Requirements: 1.5, 2.5_

  - [x] 2.4 Implement `GeneratedLayout` and `GenerationResult` data types
    - Create `Core/Generation/GeneratedLayout.cs` (Dimensions, per-column height/biome/material/passable, BlockedColumns, Deposits, StartingLocations) plus `ResourceDepositPlacement`/`StartingLocation` structs and `Core/Generation/GenerationResult.cs` (Success/Failure)
    - _Requirements: 1.1_

  - [x] 2.5 Implement `LayoutCodec` canonical serialization and hash
    - Create `Core/Generation/LayoutCodec.cs`: `Serialize` in fixed canonical column order and `Hash` (FNV-1a over serialized bytes)
    - _Requirements: 1.2, 7.4_

- [x] 3. World_Generator stages and pipeline (engine-free)
  - [x] 3.1 Implement `BiomeAssigner`
    - Create `Core/Generation/BiomeAssigner.cs`: scatter biome seed points from a sub-stream, assign each column to nearest point by integer squared distance with deterministic tie-break, weighted `BiomeDef` selection
    - _Requirements: 2.1_

  - [x] 3.2 Implement `FeaturePlacer`
    - Create `Core/Generation/FeaturePlacer.cs`: elevation→cell height, water bodies below water level, obstacles from obstacle-density noise on non-water land; record water + obstacle columns as `BlockedColumns` within bounds
    - _Requirements: 2.3_

  - [x] 3.3 Implement `SymmetryTransform`
    - Create `Core/Generation/SymmetryTransform.cs`: Mirrored/rotational transform derivation for a given Nation count and the Balanced mode helper
    - _Requirements: 4.3_

  - [x] 3.4 Implement `ResourceDepositPlacer`
    - Create `Core/Generation/ResourceDepositPlacer.cs`: place deposits at count scaled by `ResourceDensity`, only on passable non-water non-obstacle columns; Balanced equalizes per-Nation/per-type counts; enforce per-type minimums within `StartRegionRadius`
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

  - [x] 3.5 Implement `StartingLocationPlacer`
    - Create `Core/Generation/StartingLocationPlacer.cs`: one Starting_Location per Nation on contiguous passable footprint, pairwise separation >= `MinStartSeparationCells`, Mirrored places one region and derives the rest via `SymmetryTransform`; on exhausting `MaxPlacementAttempts` signal failure
    - _Requirements: 4.1, 4.2, 4.4, 4.5_

  - [x] 3.6 Implement `WorldGenerator.Generate` and `GeneratePlanet` pipeline
    - Create `Core/Generation/WorldGenerator.cs`: validate first (else `Failure`), run the five ordered stages, return `Success(GeneratedLayout)` or `Failure(reason)` (never partial); `GeneratePlanet(PlanetProfileDef, GenerationSeed)` delegates through the same pipeline with the profile's parameters
    - _Requirements: 1.1, 1.4, 6.1, 6.2, 6.4_

  - [x] 3.7 Implement `GeneratedLayout.ToTerrainVolume`
    - Add `ToTerrainVolume()` to populate a `TerrainVolume` — each column solid to its elevation using the Biome's surface `CellMaterial` so the Terrain_Renderer can always resolve a material; raise obstacle columns as tall solid spires
    - _Requirements: 2.2_

  - [x] 3.8 Add additive blocked-column overload to `NavGrid`
    - Add `NavGrid(TerrainVolume, int maxStepHeight, IReadOnlyCollection<CellCoord> blockedColumns)` making `IsWalkable`/`CanTraverse` return false for blocked columns; leave the existing constructor and base behavior untouched
    - _Requirements: 2.4_

- [x] 4. World generation property tests (FsCheck EditMode)
  - [x]* 4.1 Write property test: complete, structurally-sound layout
    - **Property 2: Every valid generation yields a complete, structurally-sound layout**
    - **Validates: Requirements 1.1, 6.1**

  - [x]* 4.2 Write property test: total biome assignment from parameter set
    - **Property 3: Biome assignment is total and drawn from the parameter set**
    - **Validates: Requirements 2.1**

  - [x]* 4.3 Write property test: every generated cell renderable
    - **Property 4: Every generated cell has a renderable terrain material**
    - **Validates: Requirements 2.2**

  - [x]* 4.4 Write property test: features within bounds
    - **Property 5: Generated features lie within layout bounds**
    - **Validates: Requirements 2.3**

  - [x]* 4.5 Write property test: byte-identical, hash-stable, planet seed pure
    - **Property 1: Seeded generation is byte-identical and hash-stable (battlefield and planet)**
    - **Validates: Requirements 1.2, 1.3, 6.2, 6.4, 7.4**

  - [x]* 4.6 Write property test: Map_Size determines dimensions
    - **Property 13: Selected Map_Size determines layout dimensions**
    - **Validates: Requirements 5.2**

  - [x]* 4.7 Write property test: invalid/unsatisfiable generation rejected without partial layout
    - **Property 12: Invalid or unsatisfiable generation is rejected without a partial layout**
    - **Validates: Requirements 1.5, 2.5, 4.5**

  - [x]* 4.8 Write property test: deposit count monotonic in density
    - **Property 7: Resource-deposit count is monotonic in resource density**
    - **Validates: Requirements 3.1**

  - [x]* 4.9 Write property test: deposits on passable non-water non-obstacle cells
    - **Property 8: Resource deposits occupy only passable, non-water, non-obstacle cells**
    - **Validates: Requirements 3.2**

  - [x]* 4.10 Write property test: minimum resource guarantee per starting region
    - **Property 10: Every starting region meets the minimum resource guarantee**
    - **Validates: Requirements 3.4**

  - [x]* 4.11 Write property test: fair symmetry equalizes deposits and mirrors regions
    - **Property 9: Fair symmetry equalizes per-Nation, per-type deposits and mirrors starting regions**
    - **Validates: Requirements 3.3, 4.3**

  - [x]* 4.12 Write property test: one-per-Nation, spaced, contiguous-passable starts
    - **Property 11: Starting locations are one-per-Nation, spaced, and on contiguous passable ground**
    - **Validates: Requirements 4.1, 4.2, 4.4**

  - [x]* 4.13 Write property test: pathfinding never routes through blocked columns
    - **Property 6: Pathfinding never routes through blocked columns**
    - **Validates: Requirements 2.4**

- [~] 5. Checkpoint — generation core complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Unit customization core (engine-free)
  - [x] 6.1 Implement `LoadoutValidator`
    - Create `Core/Customization/LoadoutValidator.cs`: pure `IsCompatible(LoadoutSlotDef, ModuleDef)` returns true iff `SlotType` matches
    - _Requirements: 8.2, 8.3_

  - [x] 6.2 Implement `CustomizationResolver`
    - Create `Core/Customization/CustomizationResolver.cs`: `Fixed`-only fold `effective = (base + Σ additive) × Π multiplicative` for attack/defense/move speed/Sight_Radius over equipped modules + Nation modifiers; empty loadout equals base
    - _Requirements: 8.5, 10.1, 10.2_

  - [x] 6.3 Implement `UnitCustomizationSystem` and `AssignModuleCommand`
    - Create `Core/Commands/AssignModuleCommand.cs` and `Core/Systems/UnitCustomizationSystem.cs`: per-Nation/per-Unit-type loadout store; record compatible assignment, reject incompatible with `CommandResult.Reject` leaving loadout unchanged, null module clears slot; expose `ResolveProfile` and `ResolveLoadoutForRecruit`
    - _Requirements: 8.2, 8.3, 8.5, 9.2, 9.3_

  - [x] 6.4 Extend `RecruitUnitCommand` and `UnitSystem` recruit path for loadouts
    - Add optional `SelectedLoadout` to `Core/Commands/RecruitUnitCommand.cs`; update the `UnitSystem` recruit handler to resolve by precedence (explicit → Nation recorded → Unit_Variant default → base empty), validate, and spawn `UnitInstance` with `Loadout` + resolved `Profile`; route through the existing `CommandRouter`
    - _Requirements: 9.2, 9.3, 10.5_

  - [x] 6.5 Wire `CombatSystem`/`VisionSystem` to use `CustomizedUnitProfile`
    - Update `Core/Systems/CombatSystem.cs` `ResolveAttack`/`ResolveAreaAttack` to read `EffectiveAttack`/`EffectiveDefense` from `UnitInstance.Profile` (fallback to `Def` when null); feed effective Sight_Radius to `VisionSystem`; veterancy/flank/cover continue to layer on top
    - _Requirements: 10.3_

  - [x]* 6.6 Write property test: compatible assignment recorded
    - **Property 14: Compatible module assignment is recorded**
    - **Validates: Requirements 8.2**

  - [x]* 6.7 Write property test: incompatible assignment rejected without state change
    - **Property 15: Incompatible module assignment is rejected without state change**
    - **Validates: Requirements 8.3**

  - [x]* 6.8 Write property test: profile folds modifiers; empty loadout equals base
    - **Property 16: Profile resolution folds every modifier over the base, and an empty loadout equals the base**
    - **Validates: Requirements 8.5, 10.1**

  - [x]* 6.9 Write property test: profile resolution deterministic and order-independent
    - **Property 17: Profile resolution is deterministic and order-independent**
    - **Validates: Requirements 10.2**

  - [x]* 6.10 Write property test: combat uses effective profile values
    - **Property 18: Combat uses effective profile values rather than base attributes**
    - **Validates: Requirements 10.3**

  - [x]* 6.11 Write property test: recruit loadout precedence through authoritative pipeline
    - **Property 19: Recruit loadout follows the defined precedence through the authoritative pipeline**
    - **Validates: Requirements 9.2, 9.3, 10.5**

- [x] 7. Audio_Director decision layer (engine-free)
  - [x] 7.1 Implement `AttenuationCurve`
    - Create `Core/Audio/AttenuationCurve.cs`: pure `Fixed` `VolumeAt(distance, maxAudibleDistance, kind)` non-increasing in distance, zero at/beyond max
    - _Requirements: 11.2_

  - [x] 7.2 Implement `SoundEventRequest`/`AudioDecisions` data and `VoiceAllocator`
    - Create `Core/Audio/SoundEventRequest.cs` + `AudioDecisions.cs` POCOs and `Core/Audio/VoiceAllocator.cs`: `Select` returns highest-priority spatialized requests up to `maxVoices` with deterministic tie-break, suppressing the rest
    - _Requirements: 11.4_

  - [x] 7.3 Implement `MusicStateClassifier`
    - Create `Core/Audio/MusicStateClassifier.cs`: `Classify` returns `Battle` when combat activity exists in the recent window, else `PeaceEconomy`
    - _Requirements: 11.3, 12.1_

  - [x] 7.4 Implement `AmbienceSelector`
    - Create `Core/Audio/AmbienceSelector.cs`: predominant Biome around the listener cell from the layout biome map → ambience id; a Biome with no Ambience yields "none, do not interrupt"
    - _Requirements: 13.1, 13.4_

  - [x] 7.5 Implement `AudioDirector.Decide`
    - Create `Core/Audio/AudioDirector.cs`: map each triggering `GameEvent` (combat, terrain-modified, structure-completed, UI action) to a `SoundEventDef` → `SoundEventRequest` (world pos, bus, spatialized, priority); emit `MusicStateDecision` (via classifier, "retain current" when no track set) and `AmbienceDecision` (biome + active weather id)
    - _Requirements: 11.1, 11.3, 12.4, 13.3_

  - [x]* 7.6 Write property test: attenuation monotonic and silent beyond max
    - **Property 20: Spatialized attenuation is monotonic and silent beyond maximum distance**
    - **Validates: Requirements 11.2**

  - [x]* 7.7 Write property test: voice budgeting plays highest-priority up to limit
    - **Property 21: Voice budgeting plays the highest-priority sounds up to the limit**
    - **Validates: Requirements 11.4**

  - [x]* 7.8 Write property test: adaptive music classification distinguishes battle/peace
    - **Property 22: Adaptive music state classification distinguishes battle from peace**
    - **Validates: Requirements 11.3, 12.1**

  - [x]* 7.9 Write property test: no track set leaves current music uninterrupted
    - **Property 23: No track set defined leaves the current music uninterrupted**
    - **Validates: Requirements 12.4**

  - [x]* 7.10 Write property test: ambience follows predominant biome; absence yields none
    - **Property 24: Ambience follows the predominant biome; absence yields no ambience**
    - **Validates: Requirements 13.1, 13.4**

- [ ] 8. Match configuration, bootstrap, and victory core (engine-free)
  - [x] 8.1 Implement `MatchConfiguration`, `NationConfig`, `AiDifficulty`, and `Validate`
    - Create `Core/State/MatchConfiguration.cs` (+ `NationConfig`) and `Core/Simulation/AiDifficulty.cs`; `Validate()` enforces >= 2 Nations, >= 1 enabled victory, all required fields set, returning the specific unmet constraint
    - _Requirements: 16.1, 16.2, 16.3, 16.4, 16.5, 16.6, 16.7, 17.1_

  - [x] 8.2 Implement `MatchConfiguration` serialization round-trip
    - Add canonical serialize/deserialize for `MatchConfiguration` preserving seed, parameters, roster (human/AI, difficulty, team, variant), starting resources, era, enabled victories
    - _Requirements: 17.1_

  - [x] 8.3 Extend `VictorySystem` with enabled-victory filter and team shared victory
    - Update `Core/Systems/VictorySystem.cs`: optional `EnabledVictoryPaths` (default all three) so only enabled conditions evaluate; use `Nation.TeamId` (default unique) so a satisfied condition for any member is a shared team victory and team members are not valid targets; defaults preserve base Properties 44–46
    - _Requirements: 17.3, 17.5_

  - [-] 8.4 Implement `MatchBootstrapper.CreateFromConfiguration`
    - Update `Core/Simulation/MatchBootstrapper.cs`: run `WorldGenerator.Generate` from config seed+params, build `TerrainVolume` + blocked-column `NavGrid` from `GeneratedLayout`, convert roster + `StartingLocation`s into `NationSeed`s (resources, era, human/AI + `AiDifficulty` controller, `TeamId`, starting units at each location), delegate to existing `Create(...)`
    - _Requirements: 17.2_

  - [x]* 8.5 Write property test: configuration validity is the conjunction of start constraints
    - **Property 25: Match_Configuration validity is exactly the conjunction of the start constraints**
    - **Validates: Requirements 16.5, 16.7**

  - [x]* 8.6 Write property test: configuration survives serialization round-trip
    - **Property 26: Match_Configuration survives a serialization round-trip**
    - **Validates: Requirements 17.1**

  - [ ]* 8.7 Write property test: bootstrapping reflects the configuration
    - **Property 27: Bootstrapping reflects the configuration**
    - **Validates: Requirements 17.2**

  - [x]* 8.8 Write property test: only enabled victory conditions are evaluated
    - **Property 28: Only enabled victory conditions are evaluated**
    - **Validates: Requirements 17.3**

  - [x]* 8.9 Write property test: a team victory is shared by all members
    - **Property 29: A team victory is shared by all team members**
    - **Validates: Requirements 17.5**

- [x] 9. Options and control-binding view-models (engine-light, `EpochWar.Unity.UI`)
  - [x] 9.1 Implement `ControlBindingsViewModel` remap and conflict logic
    - Create `Unity/UI/ControlBindingsViewModel.cs`: present each Action + current binding; assign-unbound updates only that Action; assign-conflicting stays unchanged until confirm, then moves the control; reset restores defaults — as pure logic testable headless
    - _Requirements: 19.1, 19.2, 19.3, 19.4, 19.5_

  - [x] 9.2 Implement `AccessibilityViewModel` hold-vs-toggle logic
    - Create `Unity/UI/AccessibilityViewModel.cs`: toggle mode flips an affected Action's active state on a single activation and back on the next; hold mode active only while held; expose colorblind palette + text-scale selection state
    - _Requirements: 20.3_

  - [x] 9.3 Implement gameplay/accessibility/audio settings view-models with defaults and reset
    - Create `Unity/UI/GameplaySettingsViewModel.cs`, `AudioSettingsViewModel.cs`; add defined defaults and per-category reset-to-defaults to accessibility/gameplay/audio view-models
    - _Requirements: 18.4_

  - [x]* 9.4 Write property test: reset-to-defaults restores exactly the defined defaults
    - **Property 30: Reset-to-defaults restores exactly the defined defaults for a category**
    - **Validates: Requirements 18.4, 19.5**

  - [-]* 9.5 Write property test: rebinding an unbound control updates only that Action
    - **Property 31: Rebinding an unbound control updates only that Action**
    - **Validates: Requirements 19.2**

  - [-]* 9.6 Write property test: conflicting rebinds move on confirm, change nothing until then
    - **Property 32: Conflicting rebinds move the control on confirm and change nothing until then**
    - **Validates: Requirements 19.3, 19.4**

  - [-]* 9.7 Write property test: toggle input mode toggles on a single activation
    - **Property 33: Toggle input mode toggles on a single activation**
    - **Validates: Requirements 20.3**

- [~] 10. Checkpoint — engine-free core and view-model logic complete
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 11. Unity audio playback (`EpochWar.Unity.Audio`)
  - [~] 11.1 Add audio content authoring assets and conversions
    - Create `Unity/Content/SoundEventAsset.cs`, `MusicTrackSetAsset.cs`, `AmbienceAsset.cs` (ScriptableObject → Core POCO) and extend `Unity/Content/ContentDatabase.cs` with the new asset lists + `ToCore()` conversions feeding `IAudioCatalog`
    - _Requirements: 11.1, 12.2, 13.1, 14.2_

  - [~] 11.2 Implement `AudioSystem` mixer routing
    - Create `Unity/Audio/AudioSystem.cs`: subscribe to drained per-tick `GameEvent`s, call `AudioDirector.Decide`, route every clip through the `AudioMixer` on its assigned `Volume_Bus`; Master scales all buses, each non-Master bus adjusts independently, minimum silences a bus (dB conversion)
    - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5_

  - [~] 11.3 Implement `AudioSourcePool`
    - Create `Unity/Audio/AudioSourcePool.cs`: fixed pool sized to the voice budget; play each `VoiceAllocator`-selected spatialized request as a 3D `AudioSource` at the event world position with the Sound_Event falloff; release the voice on completion for reuse
    - _Requirements: 11.1, 11.2, 11.5_

  - [~] 11.4 Implement `AdaptiveMusicPlayer`
    - Create `Unity/Audio/AdaptiveMusicPlayer.cs`: crossfade state machine that crossfades track sets on an `Adaptive_Music_State` transition over the configured duration and loops without abrupt restart while unchanged
    - _Requirements: 12.2, 12.3_

  - [~] 11.5 Implement `AmbiencePlayer`
    - Create `Unity/Audio/AmbiencePlayer.cs`: play predominant-Biome Ambience at Match start, crossfade when the predominant Biome changes, and layer/modulate weather audio while the Atmosphere_System reports active weather
    - _Requirements: 13.1, 13.2, 13.3_

  - [ ]* 11.6 Write PlayMode tests for mixer routing, Master scaling, and mute
    - Verify bus routing, independent non-Master adjustment, Master scaling, and minimum-level silence via `AudioMixer` exposed parameters
    - _Requirements: 14.1, 14.3, 14.4, 14.5_

  - [ ]* 11.7 Write PlayMode tests for 3D playback and voice release/reuse
    - Verify spatialized playback at world position and voice release back to the pool on completion
    - _Requirements: 11.1, 11.5_

  - [ ]* 11.8 Write PlayMode tests for music and ambience crossfades
    - Verify adaptive-music crossfade/no-restart and biome/weather ambience crossfade timing
    - _Requirements: 12.2, 12.3, 13.2, 13.3_

- [x] 12. Unity generation and customization content authoring
  - [x] 12.1 Add world-generation authoring assets and conversions
    - Create `Unity/Content/BiomeAsset.cs`, `MapSizeAsset.cs`, `PlanetProfileAsset.cs` and extend `Unity/Content/ContentDatabase.cs` with their lists + `ToCore()` feeding `IWorldGenCatalog`
    - _Requirements: 2.1, 5.4, 6.3_

  - [x] 12.2 Add customization authoring assets and extend `UnitAsset`
    - Create `Unity/Content/ModuleAsset.cs`, `LoadoutSlotAsset.cs`, `UnitVariantAsset.cs`; extend `Unity/Content/UnitAsset.cs` with Loadout_Slots authoring and `ContentDatabase` conversions feeding `ICustomizationCatalog`
    - _Requirements: 8.4, 9.1_

- [ ] 13. Skirmish setup UI (`EpochWar.Unity.UI`)
  - [x] 13.1 Implement `SkirmishSetupViewModel` producing and validating `MatchConfiguration`
    - Create `Unity/UI/SkirmishSetupViewModel.cs`: record map choice/seed/params, Nation count + human/AI, per-AI difficulty, teams, per-victory toggles (>= 1), starting resources/era; build a `MatchConfiguration` and surface the specific unmet constraint blocking start
    - _Requirements: 16.1, 16.2, 16.3, 16.4, 16.5, 16.6, 16.7_

  - [~] 13.2 Implement `SkirmishSetupController` UI Toolkit screen
    - Create `Unity/UI/SkirmishSetupController.cs`: bind the view-model to UI Toolkit controls; on start hand the `MatchConfiguration` to `MatchNetworkManager` for distribution and to `MatchBootstrapper.CreateFromConfiguration`
    - _Requirements: 16.1, 17.4_

  - [ ]* 13.3 Write example tests for skirmish view-model recording
    - Verify nation count, AI difficulty, team assignment, and starting resources/era are recorded into `MatchConfiguration`
    - _Requirements: 16.2, 16.3, 16.4, 16.6_

- [x] 14. Unified options UI and persistence (`EpochWar.Unity.UI`)
  - [x] 14.1 Implement `AudioSettingsStore` with startup mixer application
    - Create `Unity/UI/AudioSettingsStore.cs` mirroring `GraphicsSettingsStore` (JSON DTO, `IsValid`, `TryDeserialize` fallback); persist on change and apply persisted levels to the `AudioMixer` before any clip plays at startup; invalid data falls back to defaults with a one-time notice
    - _Requirements: 15.1, 15.2, 15.3_

  - [x] 14.2 Implement gameplay/accessibility/control-binding persistence stores
    - Create `Unity/UI/GameplaySettingsStore.cs`, accessibility store, and `Unity/UI/ControlBindingsStore.cs` (backed by Unity Input System runtime rebinding); persist on change; invalid/unreadable values fall back to per-setting defaults with a one-time notice and continue startup
    - _Requirements: 18.3, 18.5, 20.4_

  - [x] 14.3 Implement `OptionsController` unified UI Toolkit menu
    - Create `Unity/UI/OptionsController.cs`: expose Audio, Gameplay, Controls, Accessibility alongside the existing `Graphics_Settings_System`; apply non-restart changes immediately; wire per-category reset-to-defaults with persistence
    - _Requirements: 18.1, 18.2, 18.4_

  - [x] 14.4 Apply colorblind palette and text-scale hooks to HUD and `Entity_View_System`
    - Update `Entity_View_System`/HUD to render Nation-distinguishing colors from the selected colorblind-safe palette everywhere color conveys Nation identity, and apply UI text scale to HUD/info panels/menus without truncation
    - _Requirements: 20.1, 20.2_

  - [ ]* 14.5 Write example tests for settings-store deserialization fallback
    - Verify `TryDeserialize` fallback-to-defaults cores for audio/gameplay/accessibility/control stores
    - _Requirements: 15.3, 18.5_

  - [ ]* 14.6 Write PlayMode tests for persistence and startup-order application
    - Verify persistence round-trips and that audio settings apply to the mixer before playback at startup
    - _Requirements: 15.1, 15.2, 18.1, 18.2, 18.3, 20.4_

  - [ ]* 14.7 Write PlayMode tests for colorblind palette and text-scale rendering
    - Verify palette applied across UI and entity views, and text scale applied without truncation
    - _Requirements: 20.1, 20.2_

- [x] 15. Info panel effective-profile display (`EpochWar.Unity.UI`)
  - [x] 15.1 Update `InfoPanelViewModel` for effective profile and equipped modules
    - Update `Unity/UI/InfoPanelViewModel.cs` (and the zoom-in detail view) to display the `CustomizedUnitProfile` effective attributes and the list of equipped Modules
    - _Requirements: 10.4_

  - [ ]* 15.2 Write example tests for info-panel effective-stat content
    - Verify the view-model surfaces effective attributes and equipped-module ids
    - _Requirements: 10.4_

- [ ] 16. Networked generation and configuration distribution (`EpochWar.Unity.Net`)
  - [x] 16.1 Implement `NetMatchConfiguration` serialized payload
    - Create `Unity/Net/NetMatchConfiguration.cs`: NGO-serializable payload wrapping the `MatchConfiguration` serialize/deserialize
    - _Requirements: 17.4_

  - [~] 16.2 Implement `MatchNetworkManager` config broadcast and layout-hash verify
    - Update `Unity/Net/MatchNetworkManager.cs`: broadcast the `Match_Configuration` (seed + params) to every client before Match start; each side regenerates locally (no streamed terrain); compare client `LayoutCodec` hash to Host's and report a sync error / do not start on divergence; runtime terrain edits stay Host-authoritative via the existing `TerrainDeltaReplicator`
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 17.4_

  - [ ]* 16.3 Write PlayMode integration tests for config distribution and hash handshake
    - Verify identical bootstrap from a shared `Match_Configuration` and the divergence handshake that blocks start on mismatch
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 17.4_

- [ ] 17. Regression guard
  - [~] 17.1 Confirm additive defaults preserve base and combat-visuals behavior
    - Run the full existing EditMode suite; confirm base Properties 1–46 and combat-visuals Properties 1–26 still pass with `UnitInstance.Loadout`/`Profile` null, unique per-Nation `TeamId`, empty `UnitDef.LoadoutSlots`, all-victories-enabled default, and the untouched base `NavGrid` constructor; add minimal assertions covering the behavior-preserving defaults if any gap is found
    - _Requirements: 10.3, 17.3, 17.5_

- [~] 18. Final checkpoint — full expansion complete
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional test sub-tasks and can be skipped for a faster MVP; they are still scheduled in the dependency graph.
- Every task references specific requirement sub-clauses for traceability; property test tasks additionally name the exact design Correctness Property (1–33) they implement.
- All Correctness Properties 1–33 are covered exactly once (Property 1→4.5, 2→4.1, 3→4.2, 4→4.3, 5→4.4, 6→4.13, 7→4.8, 8→4.9, 9→4.11, 10→4.10, 11→4.12, 12→4.7, 13→4.6, 14→6.6, 15→6.7, 16→6.8, 17→6.9, 18→6.10, 19→6.11, 20→7.6, 21→7.7, 22→7.8, 23→7.9, 24→7.10, 25→8.5, 26→8.6, 27→8.7, 28→8.8, 29→8.9, 30→9.4, 31→9.5, 32→9.6, 33→9.7).
- All 20 requirements are covered: R1–R7 (World_Generator + net sync) in Tasks 2, 3, 4, 16; R8–R10 (customization) in Tasks 1, 6, 12, 15; R11–R14 (audio) in Tasks 7, 11; R15/R18/R19/R20 (options/controls/accessibility) in Tasks 9, 14; R16/R17 (skirmish + bootstrap) in Tasks 8, 13, 16.
- Engine-free Core tasks (Sections 1–8) carry no `UnityEngine` reference; Unity presentation and integration tasks (Sections 11–16) live in `EpochWar.Unity`.
- Checkpoints (Tasks 5, 10, 18) provide incremental validation boundaries; the regression guard (Task 17) protects the additive contract.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3", "1.4", "2.1", "2.2", "7.1"] },
    { "id": 1, "tasks": ["1.5", "2.3", "2.4", "6.1", "6.2", "7.2", "7.3"] },
    { "id": 2, "tasks": ["1.6", "2.5", "3.1", "3.2", "3.3", "6.3", "6.5", "7.4", "8.1", "9.1", "9.2", "9.3"] },
    { "id": 3, "tasks": ["3.4", "3.7", "3.8", "6.4", "7.5", "8.2", "8.3", "12.1", "13.1", "14.1", "14.2", "14.4", "15.1", "16.1"] },
    { "id": 4, "tasks": ["3.5", "6.6", "6.7", "6.8", "6.9", "6.10", "6.11", "7.6", "7.7", "7.8", "7.9", "7.10", "8.5", "8.6", "8.8", "8.9", "9.4", "9.5", "9.6", "9.7", "12.2", "13.3", "14.3", "14.5", "15.2"] },
    { "id": 5, "tasks": ["3.6", "11.1", "14.6", "14.7"] },
    { "id": 6, "tasks": ["4.1", "4.2", "4.3", "4.4", "4.5", "4.6", "4.7", "4.8", "4.9", "4.10", "4.11", "4.12", "4.13", "8.4", "11.3", "11.4", "11.5", "16.2"] },
    { "id": 7, "tasks": ["8.7", "11.2", "13.2", "16.3", "17.1"] },
    { "id": 8, "tasks": ["11.6", "11.7", "11.8"] }
  ]
}
```
