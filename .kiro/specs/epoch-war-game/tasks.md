# Implementation Plan: Epoch War

## Overview

This plan converts the layered Unity (C#) design into incremental coding tasks. Work begins in the plain-C# simulation core (`EpochWar.Core`, no `UnityEngine` reference) so all gameplay rules are unit- and property-testable with FsCheck under EditMode, then layers the MonoBehaviour presentation shell, Netcode for GameObjects networking, ScriptableObject content, terrain rendering, and UI Toolkit UI on top. Each step builds on the previous one and ends by wiring components into the authoritative simulation loop so there is no orphaned code.

Property-based tests (FsCheck) are marked optional with `*` and each references the specific correctness property and requirements clause it validates. PlayMode integration tests for networking/terrain sync are explicitly not property tests.

## Tasks

- [x] 1. Set up project structure and deterministic core foundation
  - [x] 1.1 Create assembly definitions, folder layout, and core primitives
    - Create `Assets/EpochWar/` tree with `Core/`, `Unity/`, `Tests/`, `Scenes/` and the `EpochWar.Core` asmdef with NO `UnityEngine` reference plus the `EpochWar.Unity` asmdef referencing it
    - Add `Core/Math/`: seeded deterministic PRNG and fixed-point/clamp helpers
    - Add core value types in `Core/State/`: `Era` enum (Prehistoric..Space ordered), `ResourceType`, `ResourceCost`, `CellCoord`, `Vector3Int`-equivalent struct
    - _Requirements: 1.1_
  - [x]* 1.2 Set up EditMode test project and FsCheck integration
    - Create `Tests/EditMode` asmdef referencing `EpochWar.Core` and the FsCheck/NUnit packages
    - Add a smoke property test to confirm the harness runs >= 100 iterations
    - _Requirements: 1.1_

- [x] 2. Define content catalog and runtime state models
  - [x] 2.1 Implement plain-C# content definition types and catalog lookup
    - Add POCO definition records in `Core/State/Content/`: `TechnologyDef`, `UnitDef`, `StructureDef`, `ResourceDef`, `EraDef`, `GovernanceOption` with `TechCategory`, `UnitRole`, `StructureFunction` enums and `IsPeaceArch`/Era tags
    - Add an `ICatalog` lookup abstraction (by id, by Era) so systems resolve definitions without Unity types
    - _Requirements: 1.5, 3.6, 9.1, 10.1, 11.1_
  - [x] 2.2 Implement runtime state models
    - Add `MatchState` (TickCount, Status, Outcome, Nations, Units, Structures, Terrain), `Nation`, `ResourceStore`, `UnitInstance`, `StructureInstance`, `Battalion` per the design schema
    - _Requirements: 2.1, 3.3, 3.6, 4.3, 5.1, 1.7_
  - [x]* 2.3 Write unit tests for Era ordering and attribute schema
    - Assert the Era set order (1.1) and that `UnitInstance`/`UnitDef` expose health, attack, defense, move speed, and Era of origin (3.6)
    - _Requirements: 1.1, 3.6_

- [x] 3. Implement the single authoritative command pipeline
  - [x] 3.1 Implement command pipeline and router
    - Add `ICommand`, `GameEvent`, `CommandResult` (Accept/Reject with reason + events), `ICommandHandler<T>`, and `CommandRouter` with ownership/turn checks and per-system handler delegation
    - Reject is a returned result, never an exception
    - _Requirements: 8.2, 8.5_
  - [x]* 3.2 Write unit tests for router dispatch and rejection-as-result
    - Verify accepted commands mutate state and queue events; rejected commands leave state untouched
    - _Requirements: 8.2_

- [x] 4. Implement ResourceSystem
  - [x] 4.1 Implement resource tracking, production, and cost handling
    - Per-Nation per-type `ResourceStore`; production adds output capped at capacity discarding overflow; affordable costs deducted atomically; unaffordable costs rejected with no mutation; emit change events for UI
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6_
  - [x]* 4.2 Write property tests for ResourceSystem
    - **Property 7: Resource independence across types** — **Validates: Requirements 2.1**
    - **Property 8: Production adds output capped at capacity** — **Validates: Requirements 2.2, 2.5**
    - **Property 9: Affordable cost is deducted exactly** — **Validates: Requirements 2.3**
    - **Property 10: Unaffordable cost is rejected without state change** — **Validates: Requirements 2.4**

- [x] 5. Implement TechSystem
  - [x] 5.1 Implement research, prerequisites, and Era advancement
    - Validate prerequisites and affordability, deduct Research and accumulate progress; compute next-Era availability when all required techs complete; on advance unlock all Era-tagged Units/Structures/Resources; gate Doomsday/Colony Ship by Era and feed Peace Arch prerequisites; persist `CurrentEra`/`CompletedTechIds`
    - _Requirements: 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 9.1, 10.1, 11.1_
  - [x]* 5.2 Write property tests for TechSystem
    - **Property 1: Research selection deducts cost and starts progress** — **Validates: Requirements 1.2**
    - **Property 2: Unmet prerequisites imply unavailable** — **Validates: Requirements 1.3**
    - **Property 3: Era advancement is gated by completed requirements** — **Validates: Requirements 1.4**
    - **Property 4: Era advancement unlocks all designated content** — **Validates: Requirements 1.5**
    - **Property 5: Unaffordable research is rejected without state change** — **Validates: Requirements 1.6**
    - **Property 6: Tech state survives a serialization round-trip** — **Validates: Requirements 1.7**
    - **Property 32: Doomsday weapons are gated by Era** — **Validates: Requirements 9.1**
    - **Property 36: Peace Arch availability is gated by prerequisite techs** — **Validates: Requirements 10.1**
    - **Property 40: Colony Ship availability is gated by the Space Era** — **Validates: Requirements 11.1**

- [x] 6. Implement CivSystem
  - [x] 6.1 Implement population growth, capacity, and governance modifiers
    - Track population/capacity; grow toward capacity over time when food suffices and never exceed it; reject recruit/construct commands exceeding available population; apply governance modifiers to production/attributes
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_
  - [x]* 6.2 Write property tests for CivSystem
    - **Property 23: Population growth is bounded by capacity** — **Validates: Requirements 5.2, 5.3**
    - **Property 24: Commands exceeding available population are rejected** — **Validates: Requirements 5.4**
    - **Property 25: Governance options apply their defined modifiers** — **Validates: Requirements 5.5**

- [x] 7. Implement terrain volume and pathfinding core
  - [x] 7.1 Implement TerrainVolume, effects, and support queries
    - Add `CellMaterial`, `TerrainCell`, `TerrainVolume` (chunked flat array, `Get`/`IsSolid`), `TerrainEffect` (center/radius/depth/power), `ApplyEffect` returning modified cells with bounds clamping, and `IsSupported(footprint)`
    - _Requirements: 6.1, 6.2_
  - [x] 7.2 Implement navigation grid and pathfinding
    - Derive a walkable nav grid from the top solid surface; A* over walkable nodes with flying units ignoring ground nav; recompute only affected nav nodes when cells change
    - _Requirements: 3.2, 6.3_
  - [x]* 7.3 Write property tests for terrain core
    - **Property 26: Terrain effects modify exactly the targeted region** — **Validates: Requirements 6.2**
    - **Property 27: Cell removal keeps pathfinding consistent** — **Validates: Requirements 6.3**

- [x] 8. Implement TerrainSystem tick and support consequences
  - [x] 8.1 Implement queued effect application and support checks
    - `TerrainSystem.Tick` applies queued `TerrainEffect`s, runs support checks after each batch, and applies the configured consequence to unsupported Structures/Units
    - _Requirements: 6.2, 6.4_
  - [x]* 8.2 Write property test for support loss
    - **Property 28: Loss of support applies the defined consequence** — **Validates: Requirements 6.4**

- [x] 9. Implement UnitSystem and CombatSystem
  - [x] 9.1 Implement recruitment and build queues
    - Recruit command queues at the issuing Structure, deducts cost, and spawns exactly one Unit at that Structure after build time elapses in simulation
    - _Requirements: 3.1_
  - [x]* 9.2 Write property test for recruitment
    - **Property 11: Recruitment deducts cost and produces the unit after build time** — **Validates: Requirements 3.1**
  - [x] 9.3 Implement unit movement orders
    - Issue path orders toward reachable destinations using the nav grid; advance positions each tick
    - _Requirements: 3.2_
  - [x]* 9.4 Write property test for movement
    - **Property 12: Movement reaches a reachable destination** — **Validates: Requirements 3.2**
  - [x] 9.5 Implement battalion grouping, commanding, and removal
    - Create named Battalions retained until disband/elimination; apply Battalion commands to surviving members; remove zero-health units from the Match and from any Battalion
    - _Requirements: 3.3, 3.4, 3.5_
  - [x]* 9.6 Write property tests for battalions and unit removal
    - **Property 13: Battalion membership is stable until disband or elimination** — **Validates: Requirements 3.3**
    - **Property 14: Battalion commands reach every surviving member** — **Validates: Requirements 3.4**
    - **Property 15: Zero-health units are fully removed** — **Validates: Requirements 3.5**
  - [x] 9.7 Implement combat damage resolution
    - Resolve combat with `ComputeDamage(attack, defense)` clamped so health is never negative and greater attack never yields less damage
    - _Requirements: 3.7_
  - [x]* 9.8 Write property test for combat
    - **Property 16: Combat damage formula and clamping** — **Validates: Requirements 3.7**
  - [x] 9.9 Implement Doomsday deployment and Colony Ship colonization sequence
    - Execute the Doomsday weapon elimination effect against a targeted Nation on completion/payment; begin the Colony Ship colonization sequence on completion/launch payment
    - _Requirements: 9.2, 11.2_
  - [x]* 9.10 Write property tests for special operations
    - **Property 33: Deploying a doomsday weapon executes its elimination effect** — **Validates: Requirements 9.2**
    - **Property 41: Completing a Colony Ship begins the colonization sequence** — **Validates: Requirements 11.2**

- [x] 10. Implement BaseSystem
  - [x] 10.1 Implement placement, construction, removal, and Peace Arch
    - Validate placement against terrain occupancy/validity and unlock set; deduct cost and begin construction; disable functions while building and enable on completion; remove destroyed structures; own Peace Arch availability/placement/construction and withhold victory if destroyed before completion; signal VictorySystem on completion
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 10.1, 10.2, 10.4_
  - [x]* 10.2 Write property tests for BaseSystem
    - **Property 17: Valid placement deducts cost and begins construction** — **Validates: Requirements 4.1**
    - **Property 18: Invalid placement is rejected without state change** — **Validates: Requirements 4.2**
    - **Property 19: Construction completion enables the structure** — **Validates: Requirements 4.3**
    - **Property 20: Under-construction structures have disabled functions** — **Validates: Requirements 4.4**
    - **Property 21: Zero-health structures are removed and disabled** — **Validates: Requirements 4.5**
    - **Property 22: Placeable structures are exactly the unlocked set** — **Validates: Requirements 4.6**
    - **Property 37: Placing the Peace Arch begins construction and pays cost** — **Validates: Requirements 10.2**
    - **Property 39: Destroying an incomplete Peace Arch withholds victory** — **Validates: Requirements 10.4**

- [x] 11. Checkpoint - Ensure all core system tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 12. Implement VictorySystem
  - [x] 12.1 Implement victory evaluation, match init, and outcome
    - Each tick evaluate Annihilation (mark eliminated, sole-survivor wins), Peace Arch completion, and Colony Ship colonization completion; record per-condition completion `TickCount` and award the earliest on simultaneous resolution; initialize Nations at match start (starting Resources/Units/Prehistoric Era); keep Match in progress while unsatisfied; on satisfaction end the Match and populate the outcome/summary
    - _Requirements: 9.3, 9.4, 10.3, 11.3, 11.4, 12.1, 12.2, 12.3_
  - [x]* 12.2 Write property tests for VictorySystem
    - **Property 34: Resolved elimination marks the target eliminated** — **Validates: Requirements 9.3**
    - **Property 35: Sole survivor wins by Annihilation and ends the Match** — **Validates: Requirements 9.4**
    - **Property 38: Completing the Peace Arch wins by Peace and ends the Match** — **Validates: Requirements 10.3**
    - **Property 42: Completing colonization wins by Ascension and ends the Match** — **Validates: Requirements 11.3**
    - **Property 43: Simultaneous victories resolve to the earliest completion** — **Validates: Requirements 11.4**
    - **Property 44: Match start initializes every Nation correctly** — **Validates: Requirements 12.1**
    - **Property 45: No satisfied condition keeps the Match in progress** — **Validates: Requirements 12.2**
    - **Property 46: Any satisfied condition ends the Match with an outcome** — **Validates: Requirements 12.3**

- [x] 13. Wire the fixed-tick simulation loop and AI command path
  - [x] 13.1 Implement MatchState.Tick orchestration and bootstrap
    - Add `MatchState.Tick(fixedDt)` running systems in order (Resource, Civ, Base, Unit, Terrain, Victory) after applying validated commands; add a `MatchBootstrapper` that seeds nations/terrain and routes AI_Nation commands through the same `CommandRouter`
    - _Requirements: 8.5, 12.1, 12.2_
  - [x]* 13.2 Write property test for unified command path
    - **Property 31: AI and human commands share one authoritative path** — **Validates: Requirements 8.5**

- [x] 14. Implement host-authoritative networking (Netcode for GameObjects)
  - [x] 14.1 Implement MatchNetworkManager host election and connection lifecycle
    - Host election with exactly one authoritative Host; support 2-human competitive and human(s)+AI co-op configurations; on disconnect notify remaining clients and continue the Match for connected Nations
    - _Requirements: 8.1, 8.3, 8.4_
  - [x] 14.2 Implement CommandRpcRouter and state replication
    - Client intents arrive as `ServerRpc`s carrying serialized `ICommand`s, dispatched on the Host; resolved state replicates via `NetworkVariable`/snapshot; AI runs only on the Host through the same router
    - _Requirements: 8.2, 8.5_
  - [x] 14.3 Implement terrain modification replication
    - Replicate terrain modifications as compact cell-delta messages to all clients
    - _Requirements: 6.5_
  - [x]* 14.4 Write PlayMode integration tests for networking and terrain sync
    - Host+client harness verifying command propagation, host authority, disconnect continuation, and terrain delta sync (not property tests)
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 6.5_

- [x] 15. Implement presentation shell and content assets
  - [x] 15.1 Implement SimulationDriver and entity view adapters
    - `SimulationDriver` MonoBehaviour accumulates `Time.deltaTime` and drives `MatchState.Tick` on the Host; `UnitView`/`StructureView` mirror core state to scene GameObjects
    - _Requirements: 8.3_
  - [x] 15.2 Implement TerrainRenderer chunk meshing
    - Rebuild chunk meshes only for changed chunks from the voxel/cell field
    - _Requirements: 6.1, 6.2_
  - [x] 15.3 Author ScriptableObject content assets and Core conversion
    - Add `ScriptableObject` authoring wrappers (Eras, Techs, Units, Structures, Resources, Governance, Doomsday/Wonder/Colony) that convert to the `Core` POCO catalog consumed by systems
    - _Requirements: 1.5, 2.1, 3.6, 4.6_

- [x] 16. Implement UI Toolkit interface
  - [x] 16.1 Implement HUD and selection info panel
    - `HudController` binds the persistent control surface to the local Nation's resources/era/population and refreshes within 1s of change; `InfoPanel` view-model contains every attribute of a selected Unit/Battalion/Structure and refreshes on change events
    - _Requirements: 7.1, 7.2, 7.4_
  - [x]* 16.2 Write property test for info panel content
    - **Property 29: Info panel content includes all entity attributes** — **Validates: Requirements 7.2**
  - [x] 16.3 Implement command controls and availability predicates
    - Bind recruit/place-Structure/initiate-research/form-Battalion controls' enabled state to core availability predicates (enabled iff the action is currently available)
    - _Requirements: 7.5_
  - [x]* 16.4 Write property test for command control availability
    - **Property 30: Command controls are enabled exactly when actions are available** — **Validates: Requirements 7.5**
  - [x] 16.5 Implement zoom-in unit detail view
    - `ZoomDetailView` renders a close-up of a selected Unit via a dedicated render-texture camera with its full attribute set
    - _Requirements: 7.3_

- [x] 17. Implement match lifecycle presentation and wiring
  - [x] 17.1 Implement end-of-match summary and scene wiring
    - Present winning Nation, satisfied victory path, and end-of-match summary to every connected Player; wire `Boot.unity` (lobby) and `Match.unity` (play) to the bootstrapper, network manager, driver, renderer, and UI
    - _Requirements: 12.3, 12.4_
  - [x]* 17.2 Write unit test for end-of-match summary
    - Verify the summary view-model contains the winning Nation and satisfied victory path
    - _Requirements: 12.4_

- [x] 18. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for a faster MVP; they cover property tests, unit tests, and integration tests.
- All gameplay logic lives in `EpochWar.Core` (no `UnityEngine`) so property tests run under EditMode with no Play loop.
- Property tests use FsCheck (>= 100 iterations each) and are tagged `Feature: epoch-war-game, Property N`.
- PlayMode integration tests (task 14.4) validate networking and terrain sync and are intentionally not property tests.
- Each task references specific requirement clauses for traceability; checkpoints ensure incremental validation.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "2.1"] },
    { "id": 2, "tasks": ["2.2"] },
    { "id": 3, "tasks": ["2.3", "3.1", "7.1"] },
    { "id": 4, "tasks": ["3.2", "4.1", "5.1", "6.1", "7.2"] },
    { "id": 5, "tasks": ["4.2", "5.2", "6.2", "7.3", "8.1", "9.1", "9.3", "9.5", "9.7", "10.1"] },
    { "id": 6, "tasks": ["8.2", "9.2", "9.4", "9.6", "9.8", "9.9", "10.2", "12.1"] },
    { "id": 7, "tasks": ["9.10", "12.2", "13.1"] },
    { "id": 8, "tasks": ["13.2", "14.1", "15.2", "15.3"] },
    { "id": 9, "tasks": ["14.2"] },
    { "id": 10, "tasks": ["14.3", "15.1"] },
    { "id": 11, "tasks": ["14.4", "16.1", "16.3", "16.5"] },
    { "id": 12, "tasks": ["16.2", "16.4", "17.1"] },
    { "id": 13, "tasks": ["17.2"] }
  ]
}
```
