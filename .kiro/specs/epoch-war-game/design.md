# Design Document — Epoch War

## Overview

Epoch War is a real-time strategy game built in **Unity (C#)** where each Nation advances from the Prehistoric Era to the Space Era across fully destructible/diggable 3D terrain, pursuing one of three victory paths (Annihilation, Peace, Ascension). This document defines a practical, buildable Unity architecture covering scene/system organization, the simulation model, data schemas, terrain representation, host-authoritative networking via **Netcode for GameObjects (NGO)**, and the UI architecture, and it maps every one of the 12 requirements to concrete components.

### Design Principles

1. **Authoritative simulation, presentation layer is "dumb".** The Host owns all mutable Match state. Clients send *command intents*; the Host validates and applies them; resulting state replicates back. This satisfies the networking requirements and keeps every gameplay rule in one testable place (Req 8).
2. **Plain-C# simulation core, thin MonoBehaviour shell.** All gameplay rules (resources, tech, combat, victory) live in plain C# classes (POCOs) with no `UnityEngine` dependencies. MonoBehaviours act as adapters that drive the core each tick and mirror its state to the scene/UI. This makes the core unit-/property-testable with EditMode tests (no Play loop required).
3. **Data-driven catalogs as ScriptableObjects.** Units, structures, technologies, resources, and eras are authored as `ScriptableObject` definitions so balance/content can change without code edits.
4. **Deterministic, fixed-tick simulation.** The core advances on a fixed simulation step (e.g., 20 Hz) so combat, construction, production, and population growth are reproducible and timestampable for victory tie-breaks (Req 11.4).

## Architecture

### High-Level Layering

```
+--------------------------------------------------------------+
|  Presentation (Unity)                                        |
|  - HUD / Info Panels / Zoom Detail View (UI Toolkit)         |
|  - Entity GameObjects (rendering, animation, VFX)            |
|  - TerrainRenderer (mesh from voxel/cell data)               |
+----------------------------↑----------------------------------+
                             | reads replicated state, emits intents
+----------------------------↓----------------------------------+
|  Sync Layer (Netcode for GameObjects)                        |
|  - MatchNetworkManager (host election, connection lifecycle) |
|  - CommandRpcRouter (ServerRpc intents, replicated state)    |
+----------------------------↑----------------------------------+
                             | applies validated commands
+----------------------------↓----------------------------------+
|  Simulation Core (plain C#, no UnityEngine)                  |
|  - MatchState (nations, entities, terrain, clock)            |
|  - Systems: Tech, Resource, Unit, Base, Civ, Terrain,        |
|             Combat, Victory                                  |
|  - Command pipeline: validate -> apply -> events             |
+----------------------------↑----------------------------------+
                             | authored content
+----------------------------↓----------------------------------+
|  Content (ScriptableObjects): Eras, Techs, Units,            |
|  Structures, Resources, Governance, Doomsday/Wonder/Colony   |
+--------------------------------------------------------------+
```

### Scene & Project Organization

```
Assets/
  EpochWar/
    Core/                  # plain C# simulation (asmdef: EpochWar.Core, no UnityEngine ref)
      State/               # MatchState, Nation, Entity, TerrainVolume
      Systems/             # TechSystem, ResourceSystem, ... VictorySystem
      Commands/            # ICommand definitions + CommandResult
      Math/                # deterministic fixed-point / RNG helpers
    Unity/                 # MonoBehaviour adapters (asmdef: EpochWar.Unity)
      Bootstrap/           # MatchBootstrapper, SimulationDriver
      Net/                 # MatchNetworkManager, CommandRpcRouter, NetState
      Entities/            # UnitView, StructureView, TerrainRenderer
      UI/                  # HudController, InfoPanel, ZoomDetailView (UI Toolkit)
      Content/             # ScriptableObject definitions + authored assets
    Tests/
      EditMode/            # property + unit tests against EpochWar.Core
      PlayMode/            # integration tests (networking, terrain sync)
    Scenes/
      Boot.unity           # main menu / lobby
      Match.unity          # the playable match scene
```

Two assembly definitions enforce the dependency boundary: `EpochWar.Core` has **no** reference to `UnityEngine`, so the simulation can be exercised by EditMode property tests directly.

### Simulation Loop

`SimulationDriver` (a MonoBehaviour on the Host) accumulates `Time.deltaTime` and calls `MatchState.Tick(fixedDt)` a fixed number of times per second. Each tick runs systems in a fixed order:

```
1. ResourceSystem.Tick      (production cycles, capacity caps)
2. CivSystem.Tick           (population growth)
3. BaseSystem.Tick          (construction progress, completion)
4. UnitSystem.Tick          (build queues, movement, combat resolution)
5. TerrainSystem.Tick       (apply queued modifications, support checks)
6. VictorySystem.Tick       (evaluate the three victory conditions)
```

Commands received during the frame are validated and applied **before** the tick systems run, so all state mutation is ordered and reproducible.

## Command Pipeline (the single authoritative path)

Every state change — from a human Player or an AI_Nation — flows through one pipeline (Req 8.2, 8.5).

```csharp
public interface ICommand
{
    int IssuingNationId { get; }
}

public readonly struct CommandResult
{
    public bool Accepted { get; }
    public string RejectReason { get; }     // null when accepted
    public IReadOnlyList<GameEvent> Events { get; }

    public static CommandResult Reject(string reason) => /* ... */;
    public static CommandResult Accept(params GameEvent[] events) => /* ... */;
}

// Each system exposes validate+apply for the commands it owns.
public interface ICommandHandler<T> where T : ICommand
{
    CommandResult Handle(T command, MatchState state);
}
```

- **Human Players** produce intents on their client; the client sends a `ServerRpc` carrying a serialized `ICommand`. The Host deserializes and dispatches it.
- **AI_Nations** run only on the Host and produce the *same* `ICommand` instances dispatched through the *same* router — there is no separate AI mutation path (Req 8.5).

```csharp
public sealed class CommandRouter
{
    public CommandResult Dispatch(ICommand command, MatchState state)
    {
        // 1. ownership/turn checks  2. delegate to the owning system handler
        // 3. on Accept: state is mutated and GameEvents are queued for replication/UI
    }
}
```

This design guarantees that validation (affordability, availability, population, placement legality) is identical regardless of the command's origin, which is the backbone of the consistency the requirements demand.

## Data Models

### Eras & Content Catalog

```csharp
public enum Era
{
    Prehistoric = 0, Ancient = 1, Classical = 2, Medieval = 3,
    Industrial = 4, Modern = 5, Information = 6, Futuristic = 7, Space = 8
}
```

The ordered Era set (Req 1.1) is the natural order of this enum. Content is authored as ScriptableObjects so each definition is tagged with the Era at which it unlocks:

```csharp
[CreateAssetMenu(menuName = "EpochWar/Technology")]
public sealed class TechnologyDef : ScriptableObject
{
    public string Id;
    public Era Era;
    public ResourceCost ResearchCost;           // Research resource amount
    public List<TechnologyDef> Prerequisites;
    public List<UnitDef> UnlocksUnits;
    public List<StructureDef> UnlocksStructures;
    public List<ResourceType> UnlocksResources;
    public TechCategory Category;               // Normal | DoomsdayWeapon | PeaceArchPrereq | ColonyShip
}

[CreateAssetMenu(menuName = "EpochWar/Unit")]
public sealed class UnitDef : ScriptableObject
{
    public string Id;
    public Era Era;
    public ResourceCost Cost;
    public float BuildTimeSeconds;
    public int PopulationCost;
    public int MaxHealth;
    public int Attack;
    public int Defense;
    public float MoveSpeed;
    public UnitRole Role;                        // Worker | Soldier | Vehicle | Aircraft | ColonyShip
}

[CreateAssetMenu(menuName = "EpochWar/Structure")]
public sealed class StructureDef : ScriptableObject
{
    public string Id;
    public Era Era;
    public ResourceCost Cost;
    public float BuildTimeSeconds;
    public int PopulationCost;
    public int MaxHealth;
    public Vector2Int Footprint;                 // cells occupied on the terrain grid
    public StructureFunction Function;           // ResourceExtractor | Barracks | ResearchLab | Defense | Wonder
    public bool IsPeaceArch;
}
```

### Runtime State

```csharp
public sealed class MatchState
{
    public long TickCount;                       // monotonic clock for victory timestamps
    public MatchStatus Status;                   // InProgress | Ended
    public MatchOutcome Outcome;                 // null until ended
    public Dictionary<int, Nation> Nations;
    public Dictionary<int, UnitInstance> Units;
    public Dictionary<int, StructureInstance> Structures;
    public TerrainVolume Terrain;
}

public sealed class Nation
{
    public int Id;
    public bool IsAI;
    public bool Eliminated;
    public Era CurrentEra;
    public HashSet<string> CompletedTechIds;     // Req 1.7
    public Dictionary<string, float> ResearchProgress;
    public Dictionary<ResourceType, ResourceStore> Resources;  // Req 2.1
    public int Population;
    public int PopulationCapacity;               // Req 5.1
    public List<GovernanceOption> ActiveGovernance;
    public Dictionary<int, Battalion> Battalions;
}

public struct ResourceStore
{
    public float Amount;
    public float Capacity;                       // <= 0 means uncapped
}

public sealed class UnitInstance
{
    public int Id;
    public int OwnerNationId;
    public UnitDef Def;
    public int Health;                           // Req 3.6
    public Vector3 Position;
    public int? BattalionId;
    public UnitOrder CurrentOrder;
}

public sealed class Battalion
{
    public int Id;
    public string Name;
    public HashSet<int> MemberUnitIds;           // Req 3.3
}

public sealed class StructureInstance
{
    public int Id;
    public int OwnerNationId;
    public StructureDef Def;
    public int Health;
    public CellCoord Origin;
    public float ConstructionProgress;           // seconds accumulated
    public bool IsOperational;                   // Req 4.3 / 4.4
}
```

### Terrain Representation

The battlefield is a 3D grid of **Terrain_Cells** (a voxel/cell field). A dense chunked array keeps lookups O(1) and is friendly to deterministic modification and meshing.

```csharp
public enum CellMaterial : byte { Empty = 0, Soil, Rock, Sand, Reinforced }

public struct TerrainCell
{
    public CellMaterial Material;   // Empty == dug out / destroyed
    public byte Integrity;          // remaining hardness for partial damage
}

public sealed class TerrainVolume
{
    public Vector3Int Dimensions;                 // X, Y(up), Z in cells
    private TerrainCell[] _cells;                 // flat, chunked for meshing

    public TerrainCell Get(CellCoord c);
    public bool IsSolid(CellCoord c);
    public IReadOnlyList<CellCoord> ApplyEffect(TerrainEffect effect); // returns modified cells
    public bool IsSupported(CellCoord footprintOrigin, Vector2Int footprint);
}

public struct TerrainEffect
{
    public CellCoord Center;
    public int Radius;            // area
    public int Depth;             // how far down it carves
    public int Power;             // integrity removed; > material hardness => Empty
}
```

- **Rendering:** `TerrainRenderer` rebuilds chunk meshes (marching-cubes or greedy voxel meshing) only for chunks whose cells changed.
- **Pathfinding:** A navigation grid is derived from the top solid surface; when cells change, only affected nav nodes are recomputed (Req 6.3). Movement uses A* over walkable surface nodes; flying units (`Aircraft`/`ColonyShip`) ignore ground nav.
- **Support:** structures/units reference the cells beneath them; `TerrainSystem` checks support after each modification batch and applies the configured consequence on loss (Req 6.4).

## Components & Systems (Requirement Mapping)

### TechSystem — Requirement 1, 9.1, 10.1, 11.1
- Validates research selection: prerequisites complete (1.3), affordability (1.2/1.6), and deducts Research while accumulating progress.
- Computes Era-advancement availability when all techs required for the next Era are complete (1.4) and, on advance, unlocks every Era-tagged Unit/Structure/Resource (1.5).
- Gates special techs by Era: Doomsday weapons (9.1), Colony Ship (11.1); Peace Arch prerequisite techs feed Base availability (10.1).
- Stores `CurrentEra` and `CompletedTechIds` on the `Nation` for the Match duration (1.7).

### ResourceSystem — Requirement 2
- Per-Nation, per-type `ResourceStore` (2.1). Production cycles add output capped at capacity, discarding overflow (2.2, 2.5). Affordable costs are deducted atomically (2.3); unaffordable costs rejected with no mutation (2.4). Emits change events the UI consumes (2.6).

### UnitSystem & CombatSystem — Requirement 3, 9.2, 11.2
- Recruit queues at the issuing Structure; on build-time completion, spawns the Unit (3.1). Movement issues path orders toward reachable destinations (3.2). Battalion grouping/commanding/auto-removal (3.3–3.5). Maintains detailed attributes (3.6). Combat applies a damage formula from attacker attack vs defender defense, clamped at zero (3.7). Executes Doomsday deployment effects (9.2) and the Colony Ship colonization sequence (11.2).

```csharp
public static int ComputeDamage(int attack, int defense) => Math.Max(1, attack - defense / 2);
// applied as: defender.Health = Math.Max(0, defender.Health - ComputeDamage(...));
```

### BaseSystem — Requirement 4, 10.1–10.4
- Validates placement against terrain occupancy/validity and unlock set (4.1, 4.2, 4.6); tracks construction progress to operational (4.3, 4.4); removes destroyed structures (4.5). Owns Peace Arch availability, placement, construction, and destruction-before-completion handling (10.1–10.4), signalling the VictorySystem on completion.

### CivSystem — Requirement 5
- Tracks population/capacity (5.1); grows population over time toward capacity when food suffices and never exceeds capacity (5.2, 5.3); rejects recruit/construct commands that exceed available population (5.4); applies governance modifiers to production/attributes (5.5).

### TerrainSystem — Requirement 6
- Applies queued `TerrainEffect`s (6.2), recomputes affected navigation nodes (6.3), runs support checks and applies consequences (6.4). Modifications are produced only on the Host and replicated (6.5, see Networking).

### UI_System — Requirement 7
- Built with **UI Toolkit**. `HudController` binds the persistent control surface to the local Nation's resources/era/population (7.1). `InfoPanel` renders the selected entity's attributes and refreshes on change events (7.2, 7.4). `ZoomDetailView` renders a close-up of a selected Unit with its full attribute set via a dedicated render-texture camera (7.3). Command controls (recruit/place/research/form-battalion) bind their `enabled` state to the corresponding action's availability predicate (7.5).

### Network_System — Requirement 8, 6.5
- **Netcode for GameObjects**, host-authoritative. `MatchNetworkManager` handles host election and connection lifecycle; exactly one Host is authoritative (8.3). Client intents arrive as `ServerRpc`s and resolved state replicates via `NetworkVariable`/snapshot (8.2). Supports 2-human competitive and human(s)+AI co-op configurations (8.1). On disconnect, remaining clients are notified and the Match continues for connected Nations (8.4). AI commands use the same authoritative pipeline (8.5). Terrain modifications replicate as compact cell-delta messages (6.5).

### VictorySystem — Requirements 9, 10, 11, 12
- Each tick, evaluates: all-opponents-eliminated (Annihilation, 9.3, 9.4), Peace Arch completion (10.3), Colony Ship colonization completion (11.3). Records a completion `TickCount` per satisfied condition; if multiple resolve in the same step, awards the earliest recorded time (11.4). Initializes nations at match start (12.1), keeps the Match in progress while unsatisfied (12.2), and on any satisfied condition ends the Match and presents the outcome and summary (12.3, 12.4).

## Error Handling

- **Command rejection is a first-class result, not an exception.** Every handler returns `CommandResult.Reject(reason)` for affordability (1.6, 2.4), population (5.4), placement (4.2), and availability (1.3) failures, leaving state untouched. The reason is surfaced to the issuing client's UI.
- **Invariant guards in EditMode tests.** Core invariants (population <= capacity, resource <= capacity, health >= 0) are asserted by property tests; violations indicate logic bugs rather than runtime conditions.
- **Network resilience.** Disconnects are caught by NGO callbacks; the Host marks the Nation as disconnected (optionally AI-takeover) and broadcasts a notification; the simulation never blocks on an absent client (8.4).
- **Terrain edge safety.** `TerrainEffect` regions are clamped to volume bounds; out-of-range cell access returns `Empty`/non-solid rather than throwing.
- **Deterministic RNG.** A seeded PRNG in `EpochWar.Core/Math` is used for any randomized resolution so Host and tests are reproducible.

## Testing Strategy

- **Unit/EditMode tests** cover specific examples and structural facts (Era ordering 1.1, attribute schema 3.6, HUD content 7.1, end summary 12.4).
- **Property tests (FsCheck for .NET)** validate the universal properties below, each running >= 100 generated iterations, tagged `Feature: epoch-war-game, Property N: <text>`, executed against `EpochWar.Core` with no Unity Play loop.
- **PlayMode/integration tests** cover networking and terrain sync (6.5, 8.1–8.4) with a host+client harness — these are explicitly *not* property tests.

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Research selection deducts cost and starts progress

*For any* Nation and any available Technology whose Research cost does not exceed the Nation's Research balance, selecting it for research reduces the Research balance by exactly the cost and leaves the Technology with accumulating (non-decreasing) progress.

**Validates: Requirements 1.2**

### Property 2: Unmet prerequisites imply unavailable

*For any* Technology and any set of completed Technologies, the Technology is available for selection if and only if all of its prerequisite Technologies are in the completed set.

**Validates: Requirements 1.3**

### Property 3: Era advancement is gated by completed requirements

*For any* Nation, the advancement action to the next Era is enabled if and only if the Nation's completed Technology set contains every Technology required for that next Era.

**Validates: Requirements 1.4**

### Property 4: Era advancement unlocks all designated content

*For any* target Era, after a Nation advances to it, every Unit type, Structure type, and Resource type designated for that Era (and all earlier Eras) is present in the Nation's unlocked set.

**Validates: Requirements 1.5**

### Property 5: Unaffordable research is rejected without state change

*For any* Nation and Technology whose Research cost exceeds the Nation's Research balance, the research request is rejected and the Nation's balance and research progress are unchanged.

**Validates: Requirements 1.6**

### Property 6: Tech state survives a serialization round-trip

*For any* Nation tech state (current Era and completed Technology set), deserializing its serialized form yields an equal current Era and equal completed Technology set.

**Validates: Requirements 1.7**

### Property 7: Resource independence across types

*For any* Nation and any change applied to one Resource type, the stored quantities of all other Resource types remain unchanged.

**Validates: Requirements 2.1**

### Property 8: Production adds output capped at capacity

*For any* Resource store with a pre-production amount and any non-negative produced quantity, the post-production amount equals the minimum of the capacity and (pre-amount + produced), and never exceeds the capacity.

**Validates: Requirements 2.2, 2.5**

### Property 9: Affordable cost is deducted exactly

*For any* multi-Resource cost that does not exceed a Nation's stored quantities, applying the cost reduces each affected Resource by exactly its cost component.

**Validates: Requirements 2.3**

### Property 10: Unaffordable cost is rejected without state change

*For any* Resource cost that exceeds the Nation's stored quantity in at least one Resource type, the action is rejected and all stored quantities remain unchanged.

**Validates: Requirements 2.4**

### Property 11: Recruitment deducts cost and produces the unit after build time

*For any* unlocked Unit type whose cost the Nation can afford, issuing a recruit command deducts the Resource cost and, after the Unit's build time elapses in simulation, produces exactly one Unit of that type at the issuing Structure.

**Validates: Requirements 3.1**

### Property 12: Movement reaches a reachable destination

*For any* Unit and any destination reachable over the current navigable terrain, executing the movement command produces a navigable path whose final node is the destination.

**Validates: Requirements 3.2**

### Property 13: Battalion membership is stable until disband or elimination

*For any* set of two or more Units grouped into a Battalion, the Battalion's membership equals that set until the Player disbands it or members are eliminated.

**Validates: Requirements 3.3**

### Property 14: Battalion commands reach every surviving member

*For any* Battalion and any command issued to it, every surviving member Unit receives that command.

**Validates: Requirements 3.4**

### Property 15: Zero-health units are fully removed

*For any* Unit whose health reaches zero, the Unit is absent from the Match's unit set and absent from the membership of every Battalion.

**Validates: Requirements 3.5**

### Property 16: Combat damage formula and clamping

*For any* attacking and defending Unit, combat reduces the defender's health by an amount derived from the attacker's attack and the defender's defense, the resulting health is never negative, and greater attacker attack never produces less damage.

**Validates: Requirements 3.7**

### Property 17: Valid placement deducts cost and begins construction

*For any* unlocked Structure placed on a valid, unoccupied Terrain location the Nation can afford, the Resource cost is deducted and a construction-in-progress Structure is created at that location.

**Validates: Requirements 4.1**

### Property 18: Invalid placement is rejected without state change

*For any* Structure placement targeting an occupied or invalid Terrain location, the placement is rejected and the Nation's Resources are unchanged.

**Validates: Requirements 4.2**

### Property 19: Construction completion enables the structure

*For any* Structure, once accumulated construction time reaches its build time, the Structure becomes operational with its functions enabled.

**Validates: Requirements 4.3**

### Property 20: Under-construction structures have disabled functions

*For any* Structure whose accumulated construction time is less than its build time, its production and command functions are disabled.

**Validates: Requirements 4.4**

### Property 21: Zero-health structures are removed and disabled

*For any* Structure whose health reaches zero, the Structure is absent from the Match and its functions are disabled.

**Validates: Requirements 4.5**

### Property 22: Placeable structures are exactly the unlocked set

*For any* Nation state, the set of placeable Structure types is exactly the set unlocked by the Nation's current Era and completed Technologies.

**Validates: Requirements 4.6**

### Property 23: Population growth is bounded by capacity

*For any* Nation and any sequence of simulation ticks, the population count never exceeds the population capacity, and when capacity exceeds count and food is sufficient the count grows over time toward the capacity.

**Validates: Requirements 5.2, 5.3**

### Property 24: Commands exceeding available population are rejected

*For any* recruit or construct command whose population requirement exceeds the Nation's available population, the command is rejected and population usage is unchanged.

**Validates: Requirements 5.4**

### Property 25: Governance options apply their defined modifiers

*For any* selected governance option, the option's defined modifier is applied to the affected Resource production or Unit attributes.

**Validates: Requirements 5.5**

### Property 26: Terrain effects modify exactly the targeted region

*For any* terrain effect applied at any position with a defined area and depth, exactly the Terrain_Cells within the computed region are altered or removed, and all cells outside the region are unchanged.

**Validates: Requirements 6.2**

### Property 27: Cell removal keeps pathfinding consistent

*For any* removed Terrain_Cell, the navigation graph used for pathfinding reflects the modified terrain (newly opened cells become traversable and removed support becomes non-traversable).

**Validates: Requirements 6.3**

### Property 28: Loss of support applies the defined consequence

*For any* Structure or Unit whose supporting Terrain_Cell is removed, the defined consequence is applied to that Structure or Unit.

**Validates: Requirements 6.4**

### Property 29: Info panel content includes all entity attributes

*For any* selected Unit, Battalion, or Structure, the information panel's view-model contains every detailed attribute of that entity.

**Validates: Requirements 7.2**

### Property 30: Command controls are enabled exactly when actions are available

*For any* Nation state, each command control (recruit, place Structure, initiate research, form Battalion) is enabled if and only if its corresponding action is currently available.

**Validates: Requirements 7.5**

### Property 31: AI and human commands share one authoritative path

*For any* command, whether issued by a human Player or an AI_Nation, it is validated and applied through the identical authoritative pipeline, producing the same resulting state as an equivalent command from the other source.

**Validates: Requirements 8.5**

### Property 32: Doomsday weapons are gated by Era

*For any* Nation, a Doomsday_Weapon is available for research if and only if the Nation's current Era is at least the Era designated for that Doomsday_Weapon.

**Validates: Requirements 9.1**

### Property 33: Deploying a doomsday weapon executes its elimination effect

*For any* targeted opposing Nation, completing a Doomsday_Weapon and paying its deployment cost executes the weapon's defined elimination effect against that target.

**Validates: Requirements 9.2**

### Property 34: Resolved elimination marks the target eliminated

*For any* targeted Nation whose Doomsday_Weapon elimination effect fully resolves, that Nation is marked eliminated.

**Validates: Requirements 9.3**

### Property 35: Sole survivor wins by Annihilation and ends the Match

*For any* Match in which all opposing Nations are eliminated, the surviving Nation is declared the Annihilation victor and the Match ends.

**Validates: Requirements 9.4**

### Property 36: Peace Arch availability is gated by prerequisite techs

*For any* Nation, the Peace_Arch is available for placement if and only if the Nation has completed all of the Peace_Arch's prerequisite Technologies.

**Validates: Requirements 10.1**

### Property 37: Placing the Peace Arch begins construction and pays cost

*For any* Nation that can afford the Peace_Arch and places it validly, its Resource cost is deducted and construction begins.

**Validates: Requirements 10.2**

### Property 38: Completing the Peace Arch wins by Peace and ends the Match

*For any* Nation whose Peace_Arch construction completes, that Nation is declared the Peace victor and the Match ends.

**Validates: Requirements 10.3**

### Property 39: Destroying an incomplete Peace Arch withholds victory

*For any* Peace_Arch destroyed before construction completes, no Peace victory is awarded to its owner.

**Validates: Requirements 10.4**

### Property 40: Colony Ship availability is gated by the Space Era

*For any* Nation, the Colony_Ship is available if and only if the Nation has reached the Space Era.

**Validates: Requirements 11.1**

### Property 41: Completing a Colony Ship begins the colonization sequence

*For any* Nation that completes a Colony_Ship and pays its launch cost, the defined colonization sequence begins.

**Validates: Requirements 11.2**

### Property 42: Completing colonization wins by Ascension and ends the Match

*For any* Nation whose Colony_Ship completes its colonization sequence, that Nation is declared the Ascension victor and the Match ends.

**Validates: Requirements 11.3**

### Property 43: Simultaneous victories resolve to the earliest completion

*For any* set of victory conditions satisfied within the same resolution step, victory is awarded to the Nation whose condition completed at the earliest recorded time.

**Validates: Requirements 11.4**

### Property 44: Match start initializes every Nation correctly

*For any* Match start with any number of Nations, each Nation is initialized with its defined starting Resources, starting Units, and the Prehistoric Era.

**Validates: Requirements 12.1**

### Property 45: No satisfied condition keeps the Match in progress

*For any* Match state in which no victory condition is satisfied, the Victory_System leaves the Match status as in-progress.

**Validates: Requirements 12.2**

### Property 46: Any satisfied condition ends the Match with an outcome

*For any* Match state in which a victory condition is satisfied, the Match status becomes ended and a populated outcome (winning Nation and satisfied victory path) is produced.

**Validates: Requirements 12.3**
