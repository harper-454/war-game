# Requirements Document

## Introduction

Epoch War is a highly interactive real-time strategy war game built in Unity (C#) in which each player guides a single nation from the dawn of prehistory through the modern day and into a futuristic/space age. Players gather resources, advance through technological eras, construct bases, manage their civilization, recruit units, and assemble battalions to wage large-scale wars across fully destructible and diggable 3D terrain. The game supports competitive online play against another human player and cooperative play against AI-controlled nations.

Three parallel victory paths are available in every match, and any player may pursue any combination of them:
- **Annihilation** — develop and deploy a doomsday-class weapon, cyber-hack, or bio-agent that eliminates an opposing nation.
- **Peace** — construct the Peace Arch wonder and sustain it to completion.
- **Ascension** — be the first nation to colonize a new planet.

This document defines the requirements for the full feature set across all systems and all eras as a single deliverable.

## Glossary

- **Game_Client**: The Unity application instance running on a single player's machine.
- **Match**: A single playthrough session involving two or more nations that ends when a victory condition is met or the session is abandoned.
- **Nation**: A player-controlled or AI-controlled faction with its own resources, territory, units, and technology state.
- **Player**: A human participant controlling one Nation.
- **AI_Nation**: A Nation controlled by the game's artificial intelligence rather than a human Player.
- **Era**: A defined stage of technological advancement; the ordered set is Prehistoric, Ancient, Classical, Medieval, Industrial, Modern, Information, Futuristic, and Space.
- **Tech_System**: The subsystem that manages research, technology unlocks, and Era advancement.
- **Technology**: A researchable item that unlocks units, structures, resources, or abilities.
- **Resource_System**: The subsystem that tracks production, storage, and consumption of resources.
- **Resource**: A quantifiable economic input (for example Food, Wood, Stone, Metal, Energy, Research, or Exotic_Matter).
- **Unit**: An individual controllable game entity such as a worker, soldier, vehicle, or aircraft.
- **Battalion**: A named, persistent grouping of Units that can be commanded as one entity.
- **Unit_System**: The subsystem that manages Unit creation, movement, combat, and detailed attributes.
- **Base_System**: The subsystem that manages placement, construction, and operation of structures.
- **Structure**: A buildable installation such as a resource extractor, barracks, research lab, defensive tower, or wonder.
- **Civ_System**: The subsystem that manages population, governance, and Nation-wide modifiers.
- **Terrain_System**: The subsystem that manages the 3D landscape, including destruction and excavation.
- **Terrain_Cell**: The smallest addressable volume of terrain that can be modified.
- **UI_System**: The subsystem that renders control surfaces, info panels, and the zoom-in detail view.
- **Network_System**: The subsystem that synchronizes Match state across connected Game_Clients.
- **Host**: The Game_Client that is authoritative for a given Match.
- **Victory_System**: The subsystem that evaluates and resolves the three victory conditions.
- **Doomsday_Weapon**: An Annihilation-path capability (weapon, cyber-hack, or bio-agent) that can eliminate a Nation.
- **Peace_Arch**: A wonder Structure whose completion satisfies the Peace victory condition.
- **Colony_Ship**: The Space-Era unit or project whose successful planetary colonization satisfies the Ascension victory condition.

## Requirements

### Requirement 1 — Era and Technology Progression

**User Story:** As a Player, I want to research technologies and advance my Nation through historical and futuristic eras, so that I can unlock progressively more powerful units, structures, and capabilities.

#### Acceptance Criteria

1. THE Tech_System SHALL present every Nation with an ordered Era set of Prehistoric, Ancient, Classical, Medieval, Industrial, Modern, Information, Futuristic, and Space.
2. WHEN a Player selects an available Technology for research, THE Tech_System SHALL deduct the Technology's Research cost from the Nation's Resource balance and begin accumulating research progress.
3. WHILE a Technology has unmet prerequisite Technologies, THE Tech_System SHALL mark that Technology as unavailable for selection.
4. WHEN a Nation completes all Technologies required to advance to the next Era, THE Tech_System SHALL enable an Era advancement action for that Nation.
5. WHEN a Nation advances to a new Era, THE Tech_System SHALL unlock every Unit type, Structure type, and Resource type designated for that Era.
6. IF a Player attempts to research a Technology whose Research cost exceeds the Nation's available Research Resource, THEN THE Tech_System SHALL reject the request and retain the existing research state.
7. THE Tech_System SHALL persist each Nation's current Era and completed Technology set for the duration of the Match.

### Requirement 2 — Resource Economy

**User Story:** As a Player, I want to gather, store, and spend multiple resource types, so that I can fund my military, construction, and research.

#### Acceptance Criteria

1. THE Resource_System SHALL track a separate stored quantity for each Resource type owned by each Nation.
2. WHEN a resource-producing Structure or Unit completes a production cycle, THE Resource_System SHALL add the produced quantity to the owning Nation's stored quantity for that Resource type.
3. WHEN a Player initiates an action with a defined Resource cost and the Nation holds at least that cost, THE Resource_System SHALL deduct the cost from the Nation's stored quantities.
4. IF a Player initiates an action whose Resource cost exceeds the Nation's stored quantities, THEN THE Resource_System SHALL reject the action and leave stored quantities unchanged.
5. WHERE a Resource type defines a storage capacity, THE Resource_System SHALL cap the stored quantity at that capacity and discard production that exceeds the capacity.
6. THE Resource_System SHALL update the displayed stored quantity for each Resource type within 1 second of any change to that quantity.

### Requirement 3 — Unit and Battalion Management

**User Story:** As a Player, I want to recruit detailed units and organize them into battalions, so that I can command large forces precisely during wars.

#### Acceptance Criteria

1. WHEN a Player issues a recruit command for an unlocked Unit type and the Nation holds the required Resources, THE Unit_System SHALL deduct the Resource cost and produce the Unit at the issuing Structure after the Unit's build time elapses.
2. WHEN a Player selects one or more Units and issues a movement command to a reachable destination, THE Unit_System SHALL move the selected Units toward the destination along a navigable path.
3. WHEN a Player groups two or more selected Units into a Battalion, THE Unit_System SHALL create a named Battalion that retains its membership until the Player disbands it or all member Units are eliminated.
4. WHEN a Player issues a command to a Battalion, THE Unit_System SHALL apply the command to every surviving member Unit of that Battalion.
5. WHEN a Unit's health reaches zero, THE Unit_System SHALL remove the Unit from the Match and remove it from any Battalion of which it is a member.
6. THE Unit_System SHALL maintain, for each Unit, detailed attributes including health, attack value, defense value, movement speed, and Era of origin.
7. WHEN combat occurs between opposing Units, THE Unit_System SHALL reduce each defending Unit's health by an amount derived from the attacking Unit's attack value and the defending Unit's defense value.

### Requirement 4 — Base Building

**User Story:** As a Player, I want to place and construct structures on the terrain, so that I can build out my base, economy, and defenses.

#### Acceptance Criteria

1. WHEN a Player places an unlocked Structure on a valid Terrain location and the Nation holds the required Resources, THE Base_System SHALL deduct the Resource cost and begin construction at that location.
2. IF a Player attempts to place a Structure on terrain that is occupied or invalid for that Structure type, THEN THE Base_System SHALL reject the placement and retain the Nation's Resources.
3. WHEN a Structure's construction time elapses, THE Base_System SHALL mark the Structure as operational and enable its functions.
4. WHILE a Structure is under construction, THE Base_System SHALL disable that Structure's production and command functions.
5. WHEN a Structure's health reaches zero, THE Base_System SHALL remove the Structure from the Match and disable its functions.
6. THE Base_System SHALL restrict the set of placeable Structure types for each Nation to those unlocked by the Nation's current Era and completed Technologies.

### Requirement 5 — Civilization Management

**User Story:** As a Player, I want to manage my civilization's population and governance, so that my economy and military scale as my Nation grows.

#### Acceptance Criteria

1. THE Civ_System SHALL track a current population count and a population capacity for each Nation.
2. WHEN a Nation's population capacity increases and food production is sufficient, THE Civ_System SHALL increase the population count over time up to the population capacity.
3. WHILE a Nation's population count equals its population capacity, THE Civ_System SHALL prevent further population growth until the capacity increases.
4. IF a Player issues a recruit or construct command that would require more population than the Nation's available population, THEN THE Civ_System SHALL reject the command.
5. WHERE a governance or civic option is selected, THE Civ_System SHALL apply the option's defined modifiers to the affected Resource production or Unit attributes.

### Requirement 6 — Destructible and Diggable Terrain

**User Story:** As a Player, I want the 3D landscape to be destructible and diggable, so that combat and engineering reshape the battlefield.

#### Acceptance Criteria

1. THE Terrain_System SHALL represent the battlefield as a 3D volume composed of addressable Terrain_Cells.
2. WHEN a weapon effect or excavation action targets a Terrain_Cell, THE Terrain_System SHALL alter or remove that Terrain_Cell according to the effect's defined area and depth.
3. WHEN a Terrain_Cell is removed, THE Terrain_System SHALL update unit pathfinding so that Units route according to the modified terrain.
4. IF a Structure or Unit loses its supporting terrain due to terrain modification, THEN THE Terrain_System SHALL apply the defined consequence to that Structure or Unit.
5. THE Terrain_System SHALL synchronize every terrain modification across all connected Game_Clients in the Match.

### Requirement 7 — Control Surfaces and Information Panels

**User Story:** As a Player, I want rich control surfaces, information panels, and zoom-in unit detail, so that I can monitor and command my Nation effectively.

#### Acceptance Criteria

1. THE UI_System SHALL display the Nation's current stored quantity for each Resource type, current Era, and population count on a persistent control surface.
2. WHEN a Player selects a Unit, Battalion, or Structure, THE UI_System SHALL display an information panel listing the selected entity's detailed attributes.
3. WHEN a Player activates the zoom-in detail view on a selected Unit, THE UI_System SHALL display a close-up rendering of that Unit together with its full attribute set.
4. WHEN an entity's displayed attribute changes, THE UI_System SHALL update the corresponding information panel within 1 second.
5. THE UI_System SHALL provide command controls for recruiting Units, placing Structures, initiating research, and forming Battalions that are enabled only when the corresponding action is currently available.

### Requirement 8 — Multiplayer and Networking

**User Story:** As a Player, I want to play online competitively against a friend or cooperatively against AI nations, so that I can share large-scale matches with others.

#### Acceptance Criteria

1. THE Network_System SHALL support a Match containing two human-controlled Nations in a competitive configuration and a Match containing one or more human-controlled Nations cooperating against one or more AI_Nations.
2. WHEN a Player issues a command that changes Match state, THE Network_System SHALL propagate the resulting state change to every connected Game_Client.
3. WHILE a Match is in progress, THE Network_System SHALL designate one Game_Client as the Host authoritative for resolving Match state.
4. IF a connected Game_Client loses its network connection during a Match, THEN THE Network_System SHALL notify the remaining Game_Clients and continue the Match for the connected Nations.
5. WHEN an AI_Nation takes a turn or acts in real time, THE Network_System SHALL apply the AI_Nation's actions through the same authoritative state path used for human Players.

### Requirement 9 — Annihilation Victory

**User Story:** As a Player, I want to develop and deploy a doomsday weapon, cyber-hack, or bio-agent, so that I can win by eliminating an opposing Nation.

#### Acceptance Criteria

1. WHERE the Annihilation path is pursued, THE Tech_System SHALL make a Doomsday_Weapon available for research only after the Nation reaches the Era designated for that Doomsday_Weapon.
2. WHEN a Player completes research of a Doomsday_Weapon and pays its deployment cost, THE Unit_System SHALL execute the Doomsday_Weapon's defined elimination effect against the targeted opposing Nation.
3. WHEN a Doomsday_Weapon's elimination effect fully resolves against a targeted Nation, THE Victory_System SHALL mark that targeted Nation as eliminated.
4. WHEN all opposing Nations are eliminated, THE Victory_System SHALL declare the surviving Nation the Annihilation victor and end the Match.

### Requirement 10 — Peace Victory

**User Story:** As a Player, I want to build the Peace Arch, so that I can win the Match through a peaceful objective.

#### Acceptance Criteria

1. WHERE the Peace path is pursued, THE Base_System SHALL make the Peace_Arch available for placement only after the Nation completes the Peace_Arch's prerequisite Technologies.
2. WHEN a Player places the Peace_Arch and pays its Resource cost, THE Base_System SHALL begin construction of the Peace_Arch.
3. WHEN construction of the Peace_Arch completes, THE Victory_System SHALL declare the owning Nation the Peace victor and end the Match.
4. IF the Peace_Arch is destroyed before construction completes, THEN THE Victory_System SHALL withhold the Peace victory for the owning Nation.

### Requirement 11 — Ascension Victory

**User Story:** As a Player, I want to colonize a new planet, so that I can win the Match by being the first Nation to ascend.

#### Acceptance Criteria

1. WHERE the Ascension path is pursued, THE Tech_System SHALL make the Colony_Ship available only after the Nation reaches the Space Era.
2. WHEN a Player completes a Colony_Ship and pays its launch cost, THE Unit_System SHALL begin the Colony_Ship's defined colonization sequence.
3. WHEN a Nation's Colony_Ship completes its colonization sequence, THE Victory_System SHALL declare that Nation the Ascension victor and end the Match.
4. IF more than one Nation would satisfy a victory condition within the same resolution step, THEN THE Victory_System SHALL award victory to the Nation whose condition completed at the earliest recorded time.

### Requirement 12 — Match Lifecycle

**User Story:** As a Player, I want matches to start, run, and conclude with a clear outcome, so that every session has a defined beginning and end.

#### Acceptance Criteria

1. WHEN a Match begins, THE Game_Client SHALL initialize each Nation with its starting Resources, starting Units, and the Prehistoric Era.
2. WHILE a Match is in progress and no victory condition is met, THE Victory_System SHALL continue the Match.
3. WHEN any victory condition is satisfied, THE Victory_System SHALL end the Match and present the outcome to every connected Player.
4. WHEN a Match ends, THE Game_Client SHALL display the winning Nation, the satisfied victory path, and an end-of-match summary.
