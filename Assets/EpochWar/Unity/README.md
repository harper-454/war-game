# EpochWar.Unity — Scene wiring & GameObject setup

This folder holds the MonoBehaviour presentation/adapter shell for Epoch War. All gameplay rules live
in the engine-free `EpochWar.Core` assembly; the components here **drive** and **mirror** that core.

Because Unity `.unity` scene files are serialized YAML that is error-prone to hand-author, the two
scenes described in the design (`Boot.unity`, `Match.unity`) are provided here as **composition-root
code** plus this setup guide. A developer creates the two scenes in the Unity Editor and drops the
components below onto GameObjects, then assigns the serialized references. No scene YAML is committed.

> This project cannot be compiled in this environment (no Unity Editor). The C# here is authored to be
> idiomatic and compile under an Editor with **Netcode for GameObjects (NGO)** and the **UI Toolkit**
> packages installed. Unity-side compilation and the scene setup below are required to run it.

## Key components (task 17.1)

| Component | Type | Role |
|-----------|------|------|
| `UI/EndOfMatchSummary` | pure C# (no `UnityEngine`) | Immutable view-model of the outcome: winning Nation, satisfied `VictoryPath`, completion tick. Unit-testable (task 17.2). |
| `UI/EndOfMatchController` | `MonoBehaviour` + `UIDocument` | Shows the summary overlay to **every** connected Player when the Match ends. |
| `Bootstrap/MatchSceneController` | `MonoBehaviour` | Composition root for `Match.unity`: seeds the catalog + terrain + Nations, builds the `MatchBootstrapper`, and binds every presentation/networking component. |
| `Bootstrap/BootController` | `MonoBehaviour` + `UIDocument` | Lobby menu for `Boot.unity`: pick mode, Host/Join, load the match scene. |
| `Bootstrap/LobbyConfig` | static | Carries the lobby's mode selection across the scene load. |

## Boot.unity (lobby)

Create these GameObjects:

1. **NetworkManager** (from NGO)
   - `NetworkManager` component + a transport (e.g. `UnityTransport`).
   - Register **both** scenes in the NGO scene setup / Build Settings: `Boot` and `Match`.
   - This object is kept alive across the scene load by NGO.
2. **Lobby**
   - `UIDocument` (assign a `PanelSettings` asset).
   - `BootController`.
     - `Match Scene Name` = `Match` (must match the Build Settings name).
     - `Network Manager` (optional): leave empty to use `NetworkManager.Singleton`, or point at a
       `MatchNetworkManager` if you keep one persistent.
     - `Selected Mode` = default lobby selection.

Flow: **Host** starts the NGO host and calls `NetworkManager.SceneManager.LoadScene("Match")` so every
client is synchronized into the match scene. **Join** starts a client; NGO moves it into the Host's
match scene automatically. The chosen `NetworkMatchMode` is stored in `LobbyConfig` and read by the
`MatchSceneController` when it seeds the Match.

> **Mode determinism across processes.** `LobbyConfig` is a process-local static, so it carries the
> Player's pick within a single process (the Host, or an in-Editor host+client, or offline). For a
> multi-process networked Match every peer must seed the *same* mode, since the human/AI Nation split
> derives from the seeds. Ensure this by either (a) authoring the same `Mode` on the
> `MatchNetworkManager` in the shared build so both peers default identically, or (b) replicating the
> selected mode (e.g. a server-owned `NetworkVariable`) and feeding it into `BuildMatch`. The current
> `MatchSceneController.BuildMatch` prefers `LobbyConfig` when set and otherwise uses the mode the
> manager supplies.

## Match.unity (play)

Create these GameObjects and assign references in the Inspector:

1. **Simulation**
   - `SimulationDriver` (the fixed-tick driver; 20 Hz default).
2. **Net** — a **NetworkObject** (scene object so it spawns on match-scene load):
   - `MatchNetworkManager` — assign its `Driver`, `Command Router`, `Terrain Replicator`, and `Mode`.
   - `CommandRpcRouter`.
   - `TerrainDeltaReplicator`.
   - Keeping these in `Match.unity` (rather than persistent from `Boot`) guarantees the
     `MatchSceneController` has set `MatchNetworkManager.MatchFactory` in its `Awake` **before** the
     manager's `OnNetworkSpawn` builds and binds the Match.
3. **MatchScene**
   - `MatchSceneController` — assign:
     - `Driver`, `Network Manager`, `Command Router` (the three above).
     - `Hud`, `Info Panel`, `Command Controls`, `Zoom Detail View`, `End Of Match`, `Entity Views`,
       `Terrain Renderer` (the components below).
     - `Terrain Dimensions` (e.g. `48 x 8 x 48`), `Terrain Fill` (e.g. `Soil`).
     - `Starting Population` / `Starting Population Capacity`.
     - Offline quick-start: tick `Auto Start Offline` (+ `Offline Local Nation Id`, `Offline Mode`)
       to run the match with **no networking** for iteration.
4. **Entities**
   - `EntityViewManager` — assign the `Unit Prefab` (carries `UnitView`) and `Structure Prefab`
     (carries `StructureView`), and optional unit/structure root transforms.
   - `TerrainRenderer` — assign a `Terrain Material` (uses vertex colors per cell material).
5. **UI** (one `UIDocument` per controller, each with a `PanelSettings`; they may share one document
   if you prefer, since each builds into `rootVisualElement`):
   - `HudController`
   - `InfoPanelController`
   - `CommandControlsController`
   - `ZoomDetailView`
   - `EndOfMatchController`

### Binding flow (who binds what)

- On the Host and every client, `MatchNetworkManager.OnNetworkSpawn` calls
  `MatchSceneController.BuildMatch(mode)` (via `MatchFactory`), then binds the `SimulationDriver`
  (authoritative on the Host, read-only on clients).
- When this peer's Nation is known, `MatchNetworkManager.LocalNationAssigned` fires and
  `MatchSceneController.BindPresentation(localNationId)` binds the HUD, info panel, command controls,
  zoom detail view, entity views, terrain renderer, and the end-of-match overlay to the driver.
- The `EndOfMatchController` shows the summary from the Host's `SimulationDriver.State.Outcome` /
  `MatchEndedEvent` on the Host, and from the replicated `CommandRpcRouter.MatchClockChanged` snapshot
  on clients — so it appears for every connected Player (Req 12.3, 12.4).

### Offline (no networking)

For quick iteration, put a `SimulationDriver`, the UI controllers, `EntityViewManager`,
`TerrainRenderer`, and a `MatchSceneController` in `Match.unity`, tick **Auto Start Offline** on the
`MatchSceneController`, and press Play. It builds a local authoritative Match and binds everything
without any NGO objects.
