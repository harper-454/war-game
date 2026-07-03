# Requirements Document

## Introduction

This document defines the requirements for the **Completion & Expansion** release of Epoch War, the existing Unity (C#) real-time strategy game. This is the third, strictly additive specification for the project. It builds on two approved and implemented specs and does not restate or modify anything already covered by them:

1. **`epoch-war-game`** (base) — the deterministic, engine-free `EpochWar.Core` simulation covering Era/Technology progression (Prehistoric→Space), the Resource economy, Unit and Battalion management, Base building, Civilization management, destructible/diggable voxel terrain, the UI Toolkit HUD, host-authoritative Netcode-for-GameObjects multiplayer, and the three victory paths (Annihilation, Peace, Ascension).
2. **`epoch-war-combat-visuals-expansion`** — Universal Render Pipeline adoption, the Graphics_Settings_System, terrain materials and destruction VFX, combat/destruction VFX, the Atmosphere_System (skybox/fog/weather), Era-scaled visual detail, large-scale battle readability, and combat depth (flanking, cover, area-of-effect, veterancy, unit abilities, the Vision_System/fog of war, and artillery/indirect fire).

All capitalized terms defined in the two prior specs' Glossaries are reused here **by reference** and are not redefined. These include, among others: Game_Client, Match, Nation, Player, AI_Nation, Era, Tech_System, Resource_System, Resource, Unit, Battalion, Unit_System, Base_System, Structure, Civ_System, Terrain_System, Terrain_Cell, UI_System, Network_System, Host, Victory_System, Combat_System, Vision_System, Graphics_Settings_System, Cell_Material, Atmosphere_System, Terrain_Renderer, VFX_System, Entity_View_System, and the content catalog (the ScriptableObject → engine-free Core POCO pattern, e.g. UnitDef, StructureDef, ResourceDef, EraDef).

This expansion completes the game by filling the remaining gaps across five pillars: **(1) procedural world/planet generation, (2) modifiable/customizable units, (3) rich 3D spatial audio, (4) a fully developed skirmish setup, and (5) a unified settings & options system** beyond graphics.

### Cross-Cutting Architectural Constraints

The following constraints apply to every requirement in this document and are also expressed as concrete acceptance criteria where relevant:

- **Deterministic core**: Any generation, customization, or gameplay-affecting computation that influences Match state MUST be deterministic and reproducible from a defined seed, using the existing deterministic primitives in `EpochWar.Core` (fixed-point math and the deterministic random source) rather than floating-point or `System.Random`/`UnityEngine.Random`.
- **No engine dependency in Core**: No type, field, or subsystem introduced by this expansion into `EpochWar.Core` SHALL reference the `UnityEngine` namespace or any Unity package namespace.
- **Content pipeline**: Every new content type introduced by this expansion SHALL flow through the existing ScriptableObject → engine-free Core POCO catalog pattern, so that content authors can define content without modifying code.
- **Network model**: Match-affecting generation results SHALL be reproduced identically across all Game_Clients by sharing the seed and generation parameters through the existing Network_System rather than by streaming full generated terrain, and SHALL integrate with the existing voxel Terrain_System and NavGrid/Pathfinder.

### Out of Scope

Campaign/single-player narrative modes, matchmaking/ranked services, replay recording and playback, spectator mode, and modding-package distribution are not addressed by this document and carry no acceptance criteria here; they remain candidates for future work.

## Glossary

New terms introduced by this expansion. All other capitalized terms follow the definitions in the two prior specs.

- **World_Generator**: The engine-free `EpochWar.Core` subsystem that deterministically produces a battlefield or planet layout — terrain, biomes, features, resource deposits, and starting locations — from a Generation_Seed and a set of Generation_Parameters.
- **Generation_Seed**: An integer value that fully determines the output of the World_Generator for a given set of Generation_Parameters.
- **Generation_Parameters**: The complete set of inputs (Map_Size, biome configuration, Nation count, symmetry mode, resource density, and feature density) that, together with a Generation_Seed, determine a generated layout.
- **Biome**: A named environmental classification (for example Grassland, Desert, Tundra, Volcanic, or Alien) that maps to a defined set of Cell_Materials, terrain feature weights, and Ambience audio, assigned to regions of a generated layout.
- **Terrain_Feature**: A generated landform placed by the World_Generator into the voxel Terrain_System, including elevation variation, water bodies, and impassable obstacles.
- **Resource_Deposit**: A generated, harvestable concentration of a specific Resource type placed at a location in a generated layout.
- **Starting_Location**: A generated position assigned to a single Nation at which that Nation's starting Units and Structures are placed at Match start.
- **Map_Size**: A named, ordered layout dimension option (for example Small, Medium, Large) that sets the generated layout's width and depth in Terrain_Cells.
- **Symmetry_Mode**: A Generation_Parameter setting that governs how equitably Starting_Locations, Resource_Deposits, and Terrain_Features are distributed among Nations (for example Mirrored or Balanced).
- **Planet_Profile**: A content-defined Biome and Generation_Parameter template used by the World_Generator to produce an Ascension target planet.
- **Unit_Loadout**: A named configuration that assigns a Module to each Loadout_Slot defined by a Unit type.
- **Loadout_Slot**: A named, typed customization point on a Unit type (for example Weapon, Armor, or Utility) that accepts a compatible Module.
- **Module**: A content-defined, equippable component (for example a weapon, armor package, or utility system) that occupies one Loadout_Slot and contributes a set of Customization_Modifiers.
- **Customization_Modifier**: A defined adjustment to a base UnitDef attribute (for example attack value, defense value, movement speed, or Sight_Radius) contributed by an equipped Module.
- **Unit_Variant**: A per-Nation, content-defined preset of a Unit type that pairs a base Unit type with a default Unit_Loadout and optional Nation-specific Customization_Modifiers.
- **Customized_Unit_Profile**: The resolved, effective attribute set for a Unit, computed by applying the Customization_Modifiers of its Unit_Loadout on top of its base UnitDef attributes.
- **Audio_Director**: The engine-free `EpochWar.Core`-configurable subsystem that decides, from Match state and content-defined audio configuration, which sounds, music, and ambience should play; distinct from the Unity-side Audio_System that performs playback.
- **Audio_System**: The Unity-side subsystem that plays spatialized sound, music, and ambience, routes output through the Audio_Mixer, and applies audio settings.
- **Sound_Event**: A content-defined, triggerable audio cue (for example weapon fire, terrain destruction, structure completion, or UI action) with an assigned Volume_Bus and spatialization setting.
- **Audio_Mixer**: The Unity-side mixing graph through which all game audio is routed to output.
- **Volume_Bus**: A named, independently adjustable audio channel within the Audio_Mixer; the defined set is Master, Music, SFX, UI, Ambience, and Voice.
- **Adaptive_Music_State**: A classification of the current Match situation (for example Peace, Economy, or Battle) used by the Audio_Director to select the music track set to play.
- **Ambience**: Continuous environmental audio associated with a Biome and modulated by the Atmosphere_System's active weather condition.
- **Audio_Settings**: The persisted configuration for per-Volume_Bus levels and audio playback options.
- **Skirmish_Setup**: The pre-match configuration screen through which a Player defines a Match before it starts.
- **Match_Configuration**: The complete, serializable description of a Match produced by Skirmish_Setup — including the selected or generated layout with its Generation_Seed and Generation_Parameters, the participating Nations and their human/AI assignment, AI_Difficulty levels, Team assignments, enabled victory conditions, and starting Resources and Era — that is consumed by the MatchBootstrapper and Network_System.
- **AI_Difficulty**: A named, ordered level (for example Easy, Normal, Hard) assigned to an AI_Nation that governs its behavioral configuration.
- **Team**: A named grouping of one or more Nations that share a victory outcome and are not valid attack targets for one another.
- **Options_System**: The unified Unity-side subsystem that exposes, applies, persists, and restores all non-graphics configuration — Audio_Settings, Gameplay_Settings, Control_Bindings, and Accessibility_Options — alongside the existing Graphics_Settings_System.
- **Gameplay_Settings**: Persisted options governing non-visual gameplay presentation and input behavior (for example camera scroll speed, edge-scroll toggle, and measurement units).
- **Control_Binding**: A mapping between a named in-game Action and one or more input controls.
- **Action**: A named, remappable in-game command (for example Select, Move, Attack, Form_Battalion, or Activate_Ability) that a Control_Binding targets.
- **Accessibility_Options**: Persisted options that adjust presentation for accessibility (for example colorblind-safe Nation palettes, UI text scale, and hold-versus-toggle input mode).

## Requirements

### Requirement 1 — Seeded Deterministic World Generation

**User Story:** As a Player, I want battlefield maps to be generated from a seed, so that a given seed always produces the same map and matches are reproducible and fair.

#### Acceptance Criteria

1. WHEN the World_Generator is invoked with a Generation_Seed and a set of Generation_Parameters, THE World_Generator SHALL produce a generated layout consisting of Terrain_Features, Biome assignments, Resource_Deposits, and Starting_Locations.
2. WHEN the World_Generator is invoked two or more times with an identical Generation_Seed and identical Generation_Parameters, THE World_Generator SHALL produce byte-for-byte identical generated layouts on every invocation.
3. THE World_Generator SHALL compute every random choice in the generation process using the deterministic random source and fixed-point math provided by EpochWar.Core, and SHALL NOT use floating-point arithmetic or any non-deterministic random source for generation decisions that affect the generated layout.
4. THE World_Generator SHALL reside within the EpochWar.Core assembly and SHALL contain no reference to the UnityEngine namespace or to any Unity package namespace.
5. IF the World_Generator is invoked with a set of Generation_Parameters that is incomplete or outside the defined valid ranges, THEN THE World_Generator SHALL reject the invocation with a descriptive error and SHALL NOT produce a partial generated layout.

### Requirement 2 — Biomes and Terrain Feature Generation

**User Story:** As a Player, I want generated maps to have varied biomes, elevation, water, and obstacles, so that each battlefield feels distinct and tactically interesting.

#### Acceptance Criteria

1. THE World_Generator SHALL assign each region of the generated layout to exactly one Biome drawn from the Biome set defined by the active Generation_Parameters.
2. THE World_Generator SHALL populate the generated layout into the existing voxel Terrain_System such that every generated Terrain_Cell has a terrain type for which the Terrain_Renderer can resolve a Cell_Material.
3. THE World_Generator SHALL generate Terrain_Features including elevation variation, water bodies, and impassable obstacles, placing each feature at Terrain_Cell coordinates within the generated layout's bounds.
4. WHEN generation completes, THE World_Generator SHALL produce, for the generated Terrain_Features, the passability data required by the NavGrid so that the Pathfinder routes Units around impassable obstacles and water without further generation input.
5. IF a Biome referenced by the Generation_Parameters has no Cell_Material mapping defined in the content catalog, THEN THE World_Generator SHALL reject the invocation with a descriptive error rather than generating unrenderable terrain.

### Requirement 3 — Resource Deposit Placement

**User Story:** As a Player, I want harvestable resources placed across the map, so that expansion and map control are economically meaningful.

#### Acceptance Criteria

1. THE World_Generator SHALL place Resource_Deposits of defined Resource types across the generated layout at a density determined by the resource density value in the Generation_Parameters.
2. THE World_Generator SHALL place each Resource_Deposit only on a Terrain_Cell that is passable and not occupied by a Terrain_Feature obstacle or water body.
3. WHERE the Symmetry_Mode in the Generation_Parameters designates a fair distribution, THE World_Generator SHALL place, for each Nation, an equal count of Resource_Deposits of each Resource type within that Nation's defined starting region.
4. THE World_Generator SHALL guarantee that each Nation's Starting_Location has at least the defined minimum count of Resource_Deposits of each Resource type required for that Nation's economy within the defined starting-region radius.

### Requirement 4 — Fair and Symmetric Starting Locations

**User Story:** As a Player, I want each nation to start from a fair position, so that no player has an inherent map-generation advantage.

#### Acceptance Criteria

1. THE World_Generator SHALL generate exactly one Starting_Location for each Nation specified in the Generation_Parameters.
2. THE World_Generator SHALL place each Starting_Location on contiguous passable terrain large enough to accommodate that Nation's starting Units and Structures without overlapping a Terrain_Feature obstacle or water body.
3. WHERE the Symmetry_Mode is Mirrored, THE World_Generator SHALL position the Starting_Locations such that the terrain, elevation, and Resource_Deposit layout within each Nation's starting region is congruent, under the mirror or rotational transform defined for the Nation count, to that of every other Nation.
4. THE World_Generator SHALL separate every pair of Starting_Locations by at least the defined minimum starting distance measured in Terrain_Cells.
5. IF the World_Generator cannot satisfy the Starting_Location placement constraints for the given Generation_Parameters within the defined attempt limit, THEN THE World_Generator SHALL reject the invocation with a descriptive error rather than returning an unfair layout.

### Requirement 5 — Map Size Options

**User Story:** As a Player, I want to choose the size of the map, so that I can play quick small matches or large-scale wars.

#### Acceptance Criteria

1. THE World_Generator SHALL accept a Map_Size selected from the defined ordered set of Map_Size options.
2. WHEN a Map_Size is selected, THE World_Generator SHALL generate a layout whose width and depth in Terrain_Cells equal the dimensions defined for that Map_Size.
3. THE World_Generator SHALL define each successive Map_Size in the ordered set with a Terrain_Cell area no smaller than the preceding Map_Size.
4. THE World_Generator SHALL provide the Map_Size options as content-catalog entries that a content author can define without modifying code.

### Requirement 6 — Ascension Target Planet Generation

**User Story:** As a Player pursuing Ascension, I want the target planet to be procedurally generated, so that colonization destinations are varied yet reproducible.

#### Acceptance Criteria

1. WHEN an Ascension target planet is required for a Match, THE World_Generator SHALL generate the planet layout from a Planet_Profile and a Generation_Seed using the same deterministic generation process defined for battlefield layouts.
2. THE World_Generator SHALL produce, for a given Planet_Profile and Generation_Seed, a byte-for-byte identical planet layout on every invocation.
3. THE World_Generator SHALL provide Planet_Profiles as content-catalog entries that a content author can define without modifying code.
4. THE World_Generator SHALL derive the Ascension target planet's Generation_Seed from the Match's Match_Configuration so that every Game_Client in the Match generates an identical target planet.

### Requirement 7 — Networked Generation Synchronization

**User Story:** As a Player in a multiplayer match, I want every player to see the same generated map, so that the match is consistent across all clients.

#### Acceptance Criteria

1. WHEN a Match begins, THE Network_System SHALL distribute the Generation_Seed and Generation_Parameters from the Match_Configuration to every connected Game_Client.
2. WHEN a Game_Client receives the Generation_Seed and Generation_Parameters, THE Game_Client SHALL produce the generated layout locally by invoking the World_Generator, rather than receiving streamed terrain data for the full layout from the Host.
3. THE Host SHALL remain authoritative for all subsequent runtime terrain modifications, which continue to synchronize through the Terrain_System synchronization already defined in the base spec.
4. IF a Game_Client's locally generated layout does not match the Host's layout for the shared Generation_Seed and Generation_Parameters, THEN THE Network_System SHALL report a synchronization error and SHALL NOT start the Match for that Game_Client.

### Requirement 8 — Unit Customization Loadouts

**User Story:** As a Player, I want to customize my units with different weapons, armor, and modules, so that I can tailor my forces to my strategy.

#### Acceptance Criteria

1. THE Unit_System SHALL define, for each customizable Unit type, a set of Loadout_Slots, each with a slot type that determines which Modules are compatible.
2. WHEN a Player assigns a Module to a Loadout_Slot of a Unit type and that Module's slot type matches the Loadout_Slot's type, THE Unit_System SHALL record the assignment in that Unit type's Unit_Loadout.
3. IF a Player attempts to assign a Module to a Loadout_Slot whose slot type is incompatible with that Module, THEN THE Unit_System SHALL reject the assignment and retain the existing Unit_Loadout.
4. THE Unit_System SHALL provide Loadout_Slots, Modules, and Customization_Modifiers as content-catalog entries that a content author can define without modifying code.
5. WHERE a Loadout_Slot is not assigned a Module, THE Unit_System SHALL treat that Loadout_Slot as contributing no Customization_Modifiers when resolving the Customized_Unit_Profile.

### Requirement 9 — Per-Nation Unit Variants

**User Story:** As a Player, I want each nation to have distinct unit variants, so that different nations feel unique to play.

#### Acceptance Criteria

1. THE Unit_System SHALL provide Unit_Variants as content-catalog entries, each pairing a base Unit type with a default Unit_Loadout and optional Nation-specific Customization_Modifiers, that a content author can define without modifying code.
2. WHEN a Nation recruits a Unit of a type for which that Nation has a defined Unit_Variant, THE Unit_System SHALL produce the Unit using that Unit_Variant's default Unit_Loadout and Nation-specific Customization_Modifiers.
3. IF a Nation recruits a Unit of a type for which that Nation has no defined Unit_Variant, THEN THE Unit_System SHALL produce the Unit using the base Unit type's default Unit_Loadout.

### Requirement 10 — Deterministic Customization Stat Resolution

**User Story:** As a Player, I want unit customization to reliably change unit stats and display those stats, so that my customization choices have clear, consistent effects.

#### Acceptance Criteria

1. WHEN the Unit_System resolves a Unit's Customized_Unit_Profile, THE Unit_System SHALL apply every Customization_Modifier contributed by that Unit's Unit_Loadout on top of the base UnitDef attributes to compute the effective attack value, defense value, movement speed, and Sight_Radius.
2. THE Unit_System SHALL compute every Customized_Unit_Profile within the EpochWar.Core assembly using fixed-point math, with no reference to the UnityEngine namespace, such that the resolved profile is identical across all Game_Clients for the same Unit_Loadout and base Unit type.
3. WHEN a Unit's Customized_Unit_Profile is used in combat resolution, THE Combat_System SHALL use the effective attribute values from the Customized_Unit_Profile rather than the base UnitDef attribute values.
4. WHEN a Player views a customized Unit in the information panel or the zoom-in detail view, THE UI_System SHALL display the effective attribute values from that Unit's Customized_Unit_Profile and the list of Modules equipped in that Unit's Unit_Loadout.
5. WHEN a Player recruits a Unit with a selected Unit_Loadout, THE Unit_System SHALL route the recruit action, including the selected Unit_Loadout, through the existing command pipeline so that the Host applies it through the same authoritative state path used for all commands.

### Requirement 11 — Spatialized 3D Combat and World Audio

**User Story:** As a Player, I want to hear units, combat, and destruction positioned in 3D space, so that I can locate battlefield events by sound.

#### Acceptance Criteria

1. WHEN a Sound_Event configured as spatialized is triggered by a Unit, Structure, combat action, or terrain modification, THE Audio_System SHALL play that Sound_Event as a 3D positional sound at the world location of the triggering entity or event.
2. THE Audio_System SHALL attenuate each spatialized Sound_Event's playback volume as a function of the distance between the listener position and the Sound_Event's world location, using the falloff curve defined for that Sound_Event, such that volume decreases as distance increases and reaches zero at or beyond the Sound_Event's defined maximum audible distance.
3. THE Audio_Director SHALL select which Sound_Events to trigger from Match state and content-defined audio configuration within the EpochWar.Core-configurable layer, without referencing the UnityEngine namespace.
4. WHERE the count of simultaneously playing spatialized Sound_Events would exceed the defined maximum concurrent voice count, THE Audio_System SHALL play the highest-priority Sound_Events up to that maximum and SHALL suppress the remaining lower-priority Sound_Events.
5. WHEN a spatialized Sound_Event finishes playing, THE Audio_System SHALL release the voice it occupied so that it becomes available for subsequent Sound_Events.

### Requirement 12 — Adaptive Music

**User Story:** As a Player, I want the music to respond to what is happening in the match, so that peaceful building and intense battles feel different.

#### Acceptance Criteria

1. THE Audio_Director SHALL classify the current Adaptive_Music_State from Match state, distinguishing at minimum a Peace/Economy state from a Battle state.
2. WHEN the Adaptive_Music_State transitions from one state to another, THE Audio_System SHALL transition playback from the current music track set to the music track set defined for the new Adaptive_Music_State using a crossfade over the defined transition duration.
3. WHILE the Adaptive_Music_State remains unchanged, THE Audio_System SHALL continue playing the music track set defined for that Adaptive_Music_State without abrupt restarts.
4. IF no music track set is defined for a classified Adaptive_Music_State, THEN THE Audio_System SHALL continue playing the current music track set and SHALL NOT interrupt playback.

### Requirement 13 — Biome and Weather Ambience

**User Story:** As a Player, I want ambient environmental sound tied to the terrain and weather, so that the world feels alive.

#### Acceptance Criteria

1. WHEN a Match begins, THE Audio_System SHALL play the Ambience track defined for the predominant Biome of the generated layout.
2. WHEN the listener position moves such that the predominant Biome around the listener changes, THE Audio_System SHALL crossfade the Ambience to the track defined for the new Biome over the defined transition duration.
3. WHERE the Atmosphere_System reports an active weather condition, THE Audio_System SHALL modulate or layer the Ambience with the audio defined for that weather condition for the duration the weather condition remains active.
4. IF no Ambience track is defined for a Biome, THEN THE Audio_System SHALL play no Ambience for that Biome and SHALL NOT interrupt other audio.

### Requirement 14 — Audio Mixer and Volume Buses

**User Story:** As a Player, I want separate volume controls for music, effects, and other sounds, so that I can balance the audio to my preference.

#### Acceptance Criteria

1. THE Audio_System SHALL route all game audio through the Audio_Mixer via the defined Volume_Buses: Master, Music, SFX, UI, Ambience, and Voice.
2. THE Audio_System SHALL assign each Sound_Event, music track, and Ambience track to exactly one of the non-Master Volume_Buses defined in Criterion 1.
3. WHEN a Player adjusts the level of a non-Master Volume_Bus, THE Audio_System SHALL apply the adjusted level to all audio routed through that Volume_Bus without altering the levels of the other Volume_Buses.
4. WHEN a Player adjusts the Master Volume_Bus level, THE Audio_System SHALL scale the effective output level of every other Volume_Bus by the Master level.
5. WHEN a Player sets a Volume_Bus level to its minimum value, THE Audio_System SHALL silence all audio routed through that Volume_Bus.

### Requirement 15 — Audio Settings Persistence

**User Story:** As a Player, I want my audio settings to be saved, so that I do not have to reconfigure them every time I launch the game.

#### Acceptance Criteria

1. WHEN a Player changes any Audio_Settings value, THE Options_System SHALL persist the resulting Audio_Settings so that they are restored automatically the next time the Game_Client starts.
2. WHEN the Game_Client starts, THE Options_System SHALL apply the persisted Audio_Settings to the Audio_Mixer before any Sound_Event, music track, or Ambience track plays.
3. IF a persisted Audio_Settings value is invalid or unreadable when the Game_Client starts, THEN THE Options_System SHALL apply the defined default Audio_Settings, display a notice indicating that audio settings were reset to defaults due to invalid saved data, and continue startup.

### Requirement 16 — Skirmish Setup and Match Configuration

**User Story:** As a Player, I want a pre-match setup screen, so that I can configure exactly the match I want before it starts.

#### Acceptance Criteria

1. THE Skirmish_Setup SHALL allow a Player to select an existing map or request a generated layout, and WHERE a generated layout is requested, THE Skirmish_Setup SHALL allow the Player to enter or randomize a Generation_Seed and to set the Generation_Parameters including Map_Size, Biome configuration, Symmetry_Mode, and resource density.
2. THE Skirmish_Setup SHALL allow a Player to set the number of participating Nations within the defined supported range and to assign each Nation as either human-controlled or AI-controlled.
3. WHERE a Nation is assigned as AI-controlled, THE Skirmish_Setup SHALL allow the Player to select that AI_Nation's AI_Difficulty from the defined ordered set.
4. THE Skirmish_Setup SHALL allow a Player to assign the participating Nations to Teams consistent with the base spec's competitive and cooperative-versus-AI configurations.
5. THE Skirmish_Setup SHALL allow a Player to enable or disable each of the three victory conditions (Annihilation, Peace, Ascension) independently, provided at least one victory condition remains enabled.
6. THE Skirmish_Setup SHALL allow a Player to set the starting Resources and starting Era applied to every Nation at Match start.
7. IF a Player attempts to start a Match with a configuration in which fewer than two Nations are present, no victory condition is enabled, or any required field is unset, THEN THE Skirmish_Setup SHALL prevent the Match from starting and SHALL indicate which configuration constraint is unmet.

### Requirement 17 — Match Bootstrapping from Configuration

**User Story:** As a Player, I want my chosen setup to actually drive the match, so that the game I configured is the game I play.

#### Acceptance Criteria

1. WHEN a Player starts a Match from Skirmish_Setup, THE Skirmish_Setup SHALL produce a single serializable Match_Configuration containing the Generation_Seed, Generation_Parameters, Nation roster with human/AI assignment, AI_Difficulty levels, Team assignments, enabled victory conditions, and starting Resources and Era.
2. WHEN a Match begins, THE MatchBootstrapper SHALL initialize the Match from the Match_Configuration, applying the configured starting Resources, starting Era, Nation roster, Team assignments, and enabled victory conditions.
3. WHEN the Victory_System evaluates victory during a Match, THE Victory_System SHALL evaluate only the victory conditions enabled in that Match's Match_Configuration.
4. WHEN a Match is a multiplayer Match, THE Network_System SHALL distribute the Match_Configuration to every connected Game_Client before the Match begins so that all Game_Clients bootstrap from an identical Match_Configuration.
5. WHERE two or more Nations belong to the same Team, THE Victory_System SHALL treat a satisfied victory condition for any member Nation as a shared victory for every member Nation of that Team.

### Requirement 18 — Unified Options System

**User Story:** As a Player, I want a single options menu for audio, gameplay, controls, and accessibility, so that I can configure the whole game in one place.

#### Acceptance Criteria

1. THE Options_System SHALL expose, through UI Toolkit, configuration controls for Audio_Settings, Gameplay_Settings, Control_Bindings, and Accessibility_Options, alongside the existing Graphics_Settings_System.
2. WHEN a Player changes a Gameplay_Settings or Accessibility_Options value that does not require an application restart, THE Options_System SHALL apply the change immediately without requiring an application restart.
3. WHEN a Player changes any Gameplay_Settings, Accessibility_Options, or Control_Bindings value, THE Options_System SHALL persist the resulting configuration so that it is restored automatically the next time the Game_Client starts.
4. WHEN a Player selects the reset-to-defaults control for a settings category, THE Options_System SHALL restore every setting in that category to its defined default value and persist the restored configuration.
5. IF a persisted Gameplay_Settings, Accessibility_Options, or Control_Bindings value is invalid or unreadable when the Game_Client starts, THEN THE Options_System SHALL apply the defined default value for the affected setting, display a notice indicating that the affected setting was reset to its default due to invalid saved data, and continue startup.

### Requirement 19 — Control and Keybind Remapping

**User Story:** As a Player, I want to remap my controls, so that I can play with an input scheme that suits me.

#### Acceptance Criteria

1. THE Options_System SHALL present each remappable Action together with its current Control_Binding.
2. WHEN a Player assigns an input control to an Action and that input control is not already bound to another Action, THE Options_System SHALL update that Action's Control_Binding to the assigned input control.
3. IF a Player assigns an input control that is already bound to a different Action, THEN THE Options_System SHALL prompt the Player to confirm reassignment, and WHEN the Player confirms, THE Options_System SHALL remove the input control from the previously bound Action and assign it to the selected Action.
4. WHILE a Player has not confirmed a conflicting reassignment described in Criterion 3, THE Options_System SHALL leave both the previously bound Action and the selected Action unchanged.
5. WHEN a Player selects the reset-to-defaults control for Control_Bindings, THE Options_System SHALL restore every Action's Control_Binding to its defined default input control.

### Requirement 20 — Accessibility Options

**User Story:** As a Player with accessibility needs, I want options that adapt the game's presentation and input, so that I can play comfortably.

#### Acceptance Criteria

1. WHERE a colorblind-safe Nation palette is selected in Accessibility_Options, THE UI_System SHALL render Nation-distinguishing colors using that palette across every UI element and Entity_View_System representation that conveys Nation identity by color.
2. WHEN a Player adjusts the UI text scale in Accessibility_Options, THE UI_System SHALL apply the selected text scale to the HUD, information panels, and menus without truncating displayed text.
3. WHERE the hold-versus-toggle input mode in Accessibility_Options is set to toggle, THE Options_System SHALL treat each affected Action as toggled by a single input activation rather than requiring the input control to be held.
4. THE Options_System SHALL persist every Accessibility_Options value so that it is restored automatically the next time the Game_Client starts.
