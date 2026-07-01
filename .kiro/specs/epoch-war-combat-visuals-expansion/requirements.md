# Requirements Document

## Introduction

This document defines the requirements for the **Combat & Visuals Expansion** to Epoch War, the existing Unity real-time strategy game. This expansion builds additively on top of the approved and implemented base specification (`epoch-war-game`) and does not restate or modify the base requirements. All base Glossary terms (Nation, Unit, Battalion, Structure, Terrain_Cell, Terrain_System, Unit_System, Combat resolution within Unit_System, Base_System, Civ_System, Tech_System, Resource_System, UI_System, Network_System, Victory_System, Game_Client, Match, Era, Host) are reused as defined in the base spec without redefinition.

The expansion delivers two additive pillars, in priority order:

1. **Visuals + Graphics Settings** — a Unity-side (EpochWar.Unity) upgrade adopting the Universal Render Pipeline, a full graphics settings system, advanced textured/material-based terrain rendering with destruction VFX, combat/destruction visual effects, atmospheric effects, Era-scaled unit visual detail, and large-scale battle readability. This pillar SHALL NOT introduce any UnityEngine dependency into the engine-free EpochWar.Core simulation.
2. **Combat Depth** — an engine-free (EpochWar.Core) extension of the existing Combat resolution, Unit_System, and Terrain_System adding flanking, cover, area-of-effect damage, veterancy and unit abilities, fog of war/vision, and artillery/indirect fire.

**Future work (explicitly out of scope for this expansion):** formations, morale/retreat mechanics, and naval/air era-escalation mechanics are not addressed by this document and carry no acceptance criteria here; they remain candidates for a future expansion.

## Glossary

New terms introduced by this expansion. All other capitalized terms follow the definitions in the base `epoch-war-game` requirements.md Glossary.

- **Combat_System**: The existing engine-free subsystem within EpochWar.Core that resolves attack/defense interactions between Units and applies resulting damage; extended by this expansion to support flanking, cover, area-of-effect, and indirect fire.
- **Graphics_Settings_System**: The Unity-side subsystem that exposes, applies, and persists all graphics and performance configuration options through UI Toolkit.
- **Quality_Preset**: A named, ordered graphics configuration bundle (Low, Medium, High, Ultra) that sets a consistent combination of individual graphics options.
- **Post_Processing_Effect**: An individually toggleable screen-space visual effect, including Bloom, Ambient_Occlusion, Color_Grading, Motion_Blur, and Anti_Aliasing.
- **Terrain_Renderer**: The Unity-side subsystem that meshes and renders Terrain_Cells, including material assignment, lighting integration, and destruction-related visual effects; distinct from the engine-free Terrain_System that owns terrain state.
- **Cell_Material**: A defined material (including diffuse, normal map, and PBR surface parameters) assignable to a Terrain_Cell and rendered by the Terrain_Renderer.
- **Destructive_Force_Threshold**: A defined minimum destructive-force value a weapon effect must reach or exceed, when removing Terrain_Cells, for the Terrain_Renderer to render a crater decal at the affected location.
- **VFX_System**: The Unity-side subsystem that plays combat and destruction particle and cinematic effects, including explosions, muzzle flashes, projectile trails, unit death effects, structure collapse effects, and Doomsday_Weapon deployment effects.
- **Atmosphere_System**: The Unity-side subsystem that renders skybox, fog, ambient lighting mood, and weather visual effects.
- **Entity_View_System**: The Unity-side subsystem (encompassing UnitView and StructureView rendering) that manages visual representation detail, Era-scaled visual richness, and level-of-detail/instancing behavior for Units and Structures.
- **Visual_Detail_Tier**: An Era-linked classification that determines the richness of the visual representation assigned to a Unit or Structure by the Entity_View_System.
- **Vision_System**: The engine-free EpochWar.Core subsystem that computes each Nation's currently visible Terrain_Cells, Units, and Structures based on sight sources, and manages fog-of-war state.
- **Sight_Radius**: The distance from a Unit or Structure within which that Unit or Structure grants vision to its owning Nation.
- **Last_Known_Position**: The most recent visible position recorded for an enemy Unit or Structure that has since left a Nation's current vision.
- **Flank**: A relative facing classification (front, side, or rear) of a defending Unit with respect to an attacking Unit's position at the moment of an attack.
- **Flanking_Bonus**: An attack modifier applied when an attacking Unit's Flank classification against a defending Unit is side or rear.
- **Cover**: A defensive classification granted to a Unit whose position places it in, on, or behind a qualifying Terrain_Cell, Cell_Material, or Structure.
- **Cover_Bonus**: A defense modifier applied to a Unit that currently qualifies for Cover.
- **Area_Effect**: A damage delivery pattern in which a single attack applies damage to every Unit and Structure within a defined radius of an impact point, rather than to a single target.
- **Veterancy**: An experience-based progression track maintained per Unit that advances as the Unit participates in combat.
- **Veterancy_Tier**: A named rank (for example Recruit, Veteran, Elite) within a Unit's Veterancy track that grants defined stat bonuses once reached.
- **Unit_Ability**: A defined, activatable action available to specific Unit types beyond movement and basic attack, which a Player triggers explicitly and which may be subject to a cooldown or resource cost.
- **Indirect_Fire**: An attack mode in which a Unit or Structure damages a target beyond direct line-of-sight or beyond direct-fire range using an arcing or delayed projectile.
- **Artillery_Unit**: A Unit or Structure type capable of Indirect_Fire.
- **Spotting**: The act of a Nation having current vision, via the Vision_System, of a location required for an Artillery_Unit belonging to that Nation to target that location with Indirect_Fire.

## Requirements

### Requirement 1 — Universal Render Pipeline Adoption

**User Story:** As a Player, I want the game to render using a modern rendering pipeline, so that subsequent visual upgrades (lighting, materials, post-processing) are supported.

#### Acceptance Criteria

1. THE Game_Client SHALL render all Terrain, Unit, and Structure visuals using a Universal Render Pipeline Renderer Asset configured as the project's active Render Pipeline Asset.
2. THE Terrain_Renderer SHALL use, for every rendered Terrain_Cell surface, a lit shader that declares Universal Render Pipeline as its supported render pipeline.
3. THE Entity_View_System SHALL use, for every rendered Unit and Structure visual representation, a material whose assigned shader declares Universal Render Pipeline as its supported render pipeline.
4. THE EpochWar.Core assembly SHALL contain no reference to the UnityEngine namespace, to the Universal Render Pipeline package (com.unity.render-pipelines.universal), or to any namespace provided by that package.

### Requirement 2 — Graphics Settings System

**User Story:** As a Player, I want a full graphics settings menu, so that I can tune visual quality and performance to match my hardware.

#### Acceptance Criteria

1. THE Graphics_Settings_System SHALL expose, through UI Toolkit, controls for Quality_Preset, resolution, shadow quality, each individual Post_Processing_Effect, VSync, render/view distance, texture quality, and particle density, WHERE shadow quality and texture quality are each selectable from the same four-level ordered scale as Quality_Preset (Low, Medium, High, Ultra), and render/view distance and particle density are each adjustable within a minimum and maximum bound, with the maximum equal to the value defined for the Ultra Quality_Preset and the minimum equal to the value defined for the Low Quality_Preset.
2. WHEN a Player selects a Quality_Preset, THE Graphics_Settings_System SHALL set resolution, shadow quality, every Post_Processing_Effect, render/view distance, texture quality, and particle density to the values defined for that Quality_Preset, overriding any previously applied individual changes to those settings.
3. THE Graphics_Settings_System SHALL define the ordered Quality_Preset set of Low, Medium, High, and Ultra such that, for every setting listed in Criterion 2, each successive preset in that order applies a value that is no lower in visual quality and no lower in rendering cost than the preceding preset, with Low corresponding to the minimum supported values and Ultra corresponding to the maximum values supported on high-end PC hardware.
4. WHEN a Player changes an individual graphics setting after selecting a Quality_Preset, THE Graphics_Settings_System SHALL apply the individual change without reverting the other settings defined by that Quality_Preset.
5. WHEN a Player changes a graphics setting that does not require an application restart, THE Graphics_Settings_System SHALL apply the change immediately without requiring an application restart.
6. WHEN a Player changes a graphics setting that requires an application restart to take effect, THE Graphics_Settings_System SHALL persist the change immediately, display a notice to the Player indicating that a restart is required before the change takes effect, and apply the change the next time the Game_Client starts.
7. WHEN a Player changes any graphics setting, THE Graphics_Settings_System SHALL persist the resulting configuration so that it is restored automatically the next time the Game_Client starts.
8. IF a persisted graphics setting value is invalid or unreadable when the Game_Client starts, THEN THE Graphics_Settings_System SHALL apply the Low Quality_Preset, display a notice to the Player indicating that graphics settings were reset to the Low Quality_Preset due to invalid saved data, and continue startup.

### Requirement 3 — Advanced Terrain Material Rendering

**User Story:** As a Player, I want the terrain to look like real materials with proper lighting instead of flat colored cubes, so that the battlefield looks visually convincing.

#### Acceptance Criteria

1. THE Terrain_Renderer SHALL render each Terrain_Cell using the Cell_Material assigned to that Terrain_Cell's terrain type.
2. THE Terrain_Renderer SHALL apply a diffuse texture and a normal map defined by each Cell_Material to the corresponding rendered Terrain_Cell surfaces.
3. WHEN a Terrain_Cell's terrain type changes, THE Terrain_Renderer SHALL update the rendered Cell_Material for that Terrain_Cell within the same chunk mesh rebuild that already occurs for terrain modification.
4. THE Terrain_Renderer SHALL render Terrain_Cells using lit shading such that the rendered surface brightness and color of each Terrain_Cell respond to the intensity and direction of ambient and directional light sources, consistent with standard Universal Render Pipeline lit-shader behavior.
5. IF a Terrain_Cell's terrain type has no Cell_Material assigned, THEN THE Terrain_Renderer SHALL render that Terrain_Cell using a defined fallback Cell_Material and SHALL complete the chunk mesh rebuild without failure.

### Requirement 4 — Terrain Destruction Visual Effects

**User Story:** As a Player, I want terrain destruction to look visually intense, so that combat feels impactful on the battlefield.

#### Acceptance Criteria

1. WHEN a weapon effect or excavation action modifies one or more Terrain_Cells, THE Terrain_Renderer SHALL spawn a dust-and-debris particle effect at the location of the modification.
2. WHEN a weapon effect removes Terrain_Cells at or above the defined Destructive_Force_Threshold value, THE Terrain_Renderer SHALL render a crater decal at the resulting excavation site.
3. IF a weapon effect removes Terrain_Cells below the defined Destructive_Force_Threshold value, THEN THE Terrain_Renderer SHALL NOT render a crater decal at the affected location.
4. WHEN a weapon effect applies to a Terrain_Cell without fully removing it, THE Terrain_Renderer SHALL render a scorch mark decal on the affected Terrain_Cell surface.
5. IF an excavation action modifies or removes Terrain_Cells without an accompanying weapon effect, THEN THE Terrain_Renderer SHALL NOT render a crater decal or scorch mark decal at the affected location.
6. WHEN a dust-and-debris particle effect is spawned, THE Terrain_Renderer SHALL remove or fade the particle effect within 5 seconds of being spawned.

### Requirement 5 — Combat and Destruction Visual Effects

**User Story:** As a Player, I want combat actions and destruction to be visually represented with distinct effects, so that I can read battle events at a glance.

#### Acceptance Criteria

1. WHEN a Unit fires a ranged attack, THE VFX_System SHALL render a muzzle flash effect at the firing Unit's weapon origin.
2. WHEN a Unit fires a ranged attack, THE VFX_System SHALL render a projectile trail effect that travels along the attack's flight path from the firing Unit to the impact location.
3. WHEN a non-explosive attack impacts its target, THE VFX_System SHALL render an impact particle effect at the impact location.
4. WHEN an explosive attack impacts its target, THE VFX_System SHALL render an explosion particle effect at the impact location.
5. WHEN a Unit's health reaches zero, THE VFX_System SHALL render a unit death effect at that Unit's location.
6. WHEN a Structure's health reaches zero, THE VFX_System SHALL render a structure destruction and collapse effect at that Structure's location.
7. WHEN a Doomsday_Weapon is activated to eliminate its target, THE VFX_System SHALL render a dedicated deployment effect at the target's location, distinct in appearance from the impact and explosion effects specified in Criteria 3 and 4, lasting between 4 and 10 seconds.
8. THE VFX_System SHALL remove each visual effect specified in this section from the scene no later than 3 seconds after the effect is first rendered, except that the Doomsday_Weapon deployment effect specified in Criterion 7 SHALL be removed no later than 10 seconds after it is first rendered.

### Requirement 6 — Atmospheric and Weather Effects

**User Story:** As a Player, I want the battlefield to have atmospheric mood, so that the world feels alive rather than sterile.

#### Acceptance Criteria

1. WHEN a Match begins, THE Atmosphere_System SHALL render the skybox configured for that Match's environment.
2. IF no skybox is configured for a Match's environment, THEN THE Atmosphere_System SHALL render a default skybox for that Match.
3. THE Atmosphere_System SHALL render a distance fog effect whose density is set to a value between 0.0 (no fog) and 1.0 (maximum fog) as configured per Match environment.
4. WHERE a weather condition is configured for a Match, WHEN that weather condition becomes active, THE Atmosphere_System SHALL render the corresponding weather visual effect and SHALL continue rendering it until the condition's configured duration ends or the condition is deactivated.
5. IF the visual effect asset for a configured weather condition is unavailable, THEN THE Atmosphere_System SHALL render the Match without that weather effect and SHALL NOT block the Match from starting.
6. THE Atmosphere_System SHALL apply the ambient lighting color and intensity preset that is predefined for the currently rendered skybox.

### Requirement 7 — Era-Scaled Unit and Structure Visual Detail

**User Story:** As a Player, I want units and structures from later eras to look more advanced, so that technological progress is visually apparent.

#### Acceptance Criteria

1. THE Entity_View_System SHALL assign a Visual_Detail_Tier, expressed as a positive integer where a higher value indicates a richer visual representation, to each Unit and Structure visual representation based on that Unit's or Structure's Era of origin.
2. IF a Unit or Structure originates from a later Era than another Unit or Structure of the same Unit or Structure classification category, THEN THE Entity_View_System SHALL assign that later-Era Unit or Structure a Visual_Detail_Tier value greater than or equal to the earlier-Era counterpart's Visual_Detail_Tier value.
3. IF a Unit or Structure originates from the same Era as another Unit or Structure of the same classification category, THEN THE Entity_View_System SHALL assign both Units or Structures the same Visual_Detail_Tier value.
4. THE Entity_View_System SHALL provide a Visual_Detail_Tier configuration field on each Unit and Structure content entry that a content author can set without modifying code.
5. IF a Unit or Structure content entry has no Visual_Detail_Tier configured or specifies a Visual_Detail_Tier value outside the defined valid range, THEN THE Entity_View_System SHALL assign that Unit or Structure a defined default Visual_Detail_Tier value and SHALL NOT fail to render that Unit or Structure.

### Requirement 8 — Large-Scale Battle Readability

**User Story:** As a Player, I want to be able to read the state of a huge battle even when zoomed far out, so that I can command large-scale wars effectively.

#### Acceptance Criteria

1. WHEN the number of visible Units within the Player's camera view exceeds a defined density threshold (a configurable maximum Unit count for the current camera distance), THE Entity_View_System SHALL reduce per-Unit rendering detail for those Units such that the overall frame rate does not fall below a defined minimum frame rate (e.g., 30 frames per second).
2. WHILE the camera distance is at or beyond a defined far-zoom threshold (a configurable camera distance value), THE Entity_View_System SHALL render Units using a simplified representation (a reduced-detail visual form, such as a marker or silhouette) that indicates each Unit's owning Nation through a distinct visual attribute (e.g., a Nation-specific color or emblem).
3. WHEN the camera distance transitions from at-or-beyond the far-zoom threshold to below the far-zoom threshold, THE Entity_View_System SHALL restore full per-Unit rendering detail for the affected Units.
4. THE Entity_View_System SHALL maintain a defined buffer margin between the far-zoom threshold values that trigger simplification and restoration, such that a Unit's rendering representation does not toggle between simplified and full detail more than once within a defined minimum time interval (e.g., 1 second) while camera distance fluctuates near the threshold.

### Requirement 9 — Flanking and Positional Combat Bonuses

**User Story:** As a Player, I want attacks against a unit's flank or rear to be more effective, so that positioning and maneuver matter in combat.

#### Acceptance Criteria

1. WHEN an attack is resolved and the attacking Unit's position at that moment is classified as the defending Unit's side Flank, THE Combat_System SHALL apply a defined Flanking_Bonus to that attack's damage calculation.
2. WHEN an attack is resolved and the attacking Unit's position at that moment is classified as the defending Unit's rear Flank, THE Combat_System SHALL apply a Flanking_Bonus to that attack's damage calculation that is no lower than the side-Flank Flanking_Bonus.
3. WHEN an attack is resolved and the attacking Unit's position at that moment is classified as the defending Unit's front Flank, THE Combat_System SHALL apply no Flanking_Bonus to that attack's damage calculation.
4. THE Combat_System SHALL classify a defending Unit's Flank relative to an attacking Unit, at the moment an attack is resolved, by comparing the angle between the defending Unit's current facing direction and the direction from the defending Unit to the attacking Unit's current position against a defined front-arc angle threshold and a defined side-arc angle threshold, such that every possible angle value maps to exactly one of front, side, or rear.

### Requirement 10 — Terrain and Cover Bonuses

**User Story:** As a Player, I want units positioned in cover to be harder to hit effectively, so that terrain and structures provide tactical value.

#### Acceptance Criteria

1. WHEN a defending Unit's current position qualifies for Cover based on a defined Cover-qualifying Cell_Material or a defined Cover-qualifying Terrain_Cell elevation at that position, THE Combat_System SHALL apply a Cover_Bonus to that Unit's defense value for the duration that the Unit occupies the qualifying position.
2. WHEN a defending Unit's current position has a Structure lying on the direct line between the attacking Unit's current position and the defending Unit's current position at the moment of an attack, THE Combat_System SHALL apply a Cover_Bonus to that Unit's defense value for that attack.
3. WHEN a defending Unit moves off a terrain- or elevation-qualifying Cover position described in Criterion 1, THE Combat_System SHALL remove the associated Cover_Bonus from that Unit's defense value.
4. THE Terrain_System SHALL expose, for each Terrain_Cell, the Cover qualification data required by the Combat_System to evaluate Cover_Bonus eligibility.
5. WHEN both the terrain- or elevation-based Cover_Bonus described in Criterion 1 and the Structure-based Cover_Bonus described in Criterion 2 apply to the same attack, THE Combat_System SHALL apply only the greater of the two Cover_Bonus values to that attack, rather than applying both cumulatively.

### Requirement 11 — Area-of-Effect Damage

**User Story:** As a Player, I want certain weapons to damage every unit in an area, so that area-denial and splash-damage weapons are viable.

#### Acceptance Criteria

1. WHERE a Unit's attack is defined with an Area_Effect radius, THE Combat_System SHALL apply that attack's damage to every Unit and Structure, including Units and Structures belonging to the attacking Player's own Nation, whose occupied space has a nearest point within the Area_Effect radius of the attack's impact point.
2. WHERE a Unit's attack is defined with an Area_Effect radius, THE Combat_System SHALL apply that attack's full, unreduced damage value independently to each Unit and Structure within the Area_Effect radius, honoring each target's individual defense value and, for Units, each Unit's individual Cover_Bonus, without dividing or splitting the damage value among the affected targets.
3. WHERE a Unit's attack is defined with an Area_Effect radius, THE Combat_System SHALL apply Flanking_Bonus evaluation to each Unit (and not to Structures) affected by that Area_Effect attack using that Unit's position relative to the attack's impact point.

### Requirement 12 — Unit Veterancy and Experience

**User Story:** As a Player, I want my units to gain experience and become more powerful veterans, so that keeping units alive is rewarded.

#### Acceptance Criteria

1. WHEN a Unit deals damage to or eliminates an opposing Unit or Structure, THE Unit_System SHALL add the defined experience value for that action to that Unit's Veterancy track.
2. WHEN a Unit's accumulated experience reaches or exceeds the threshold defined for the next Veterancy_Tier, THE Unit_System SHALL advance that Unit to the next Veterancy_Tier and apply the stat bonuses defined for that Veterancy_Tier, repeating this advancement for each successive Veterancy_Tier whose threshold the accumulated experience also reaches or exceeds.
3. THE Unit_System SHALL persist each Unit's current Veterancy_Tier and accumulated experience for the lifetime of that Unit within the Match.
4. IF a Unit has already reached the highest defined Veterancy_Tier and continues to accumulate experience, THEN THE Unit_System SHALL retain that experience on the Unit's Veterancy track without advancing the Unit beyond the highest Veterancy_Tier.
5. WHEN a Unit is destroyed or otherwise permanently removed from the Match, THE Unit_System SHALL discard that Unit's Veterancy state.
6. WHEN a Unit advances to a new Veterancy_Tier, THE UI_System SHALL present an observable indication of that Unit's new Veterancy_Tier to the Unit's owning Player.

### Requirement 13 — Unit Abilities

**User Story:** As a Player, I want some units to have activatable special abilities, so that combat has tactical depth beyond basic attacks and movement.

#### Acceptance Criteria

1. WHERE a Unit type defines one or more Unit_Abilities, THE Unit_System SHALL make each defined Unit_Ability available for activation on Units of that type, and THE UI_System SHALL present each available Unit_Ability as a selectable control on the corresponding Unit's information panel.
2. WHEN a Player activates a Unit_Ability on a selected Unit and that Unit_Ability's cooldown has fully elapsed and the Unit's available resources meet or exceed that Unit_Ability's resource cost, THE Unit_System SHALL execute the Unit_Ability's defined effect, deduct that Unit_Ability's resource cost from the Unit's resource pool, and begin that Unit_Ability's cooldown period.
3. IF a Player attempts to activate a Unit_Ability whose cooldown has not fully elapsed or whose resource cost exceeds the Unit's available resources, THEN THE Unit_System SHALL reject the activation, THE UI_System SHALL display a message indicating the reason for rejection (cooldown active or insufficient resources), and THE Unit_Ability's cooldown state and the Unit's resource pool SHALL remain unchanged.
4. WHILE a Unit_Ability's cooldown period is active, THE UI_System SHALL display the remaining cooldown duration, in whole seconds, for that Unit_Ability on the corresponding Unit's information panel, updating the displayed value at least once per second.

### Requirement 14 — Fog of War, Vision, and Detection

**User Story:** As a Player, I want limited vision of the battlefield based on my units' sight, so that scouting and positioning matter and I cannot see the entire map at all times.

#### Acceptance Criteria

1. THE Vision_System SHALL compute, for each Nation, the set of Terrain_Cells currently visible to that Nation based on the Sight_Radius of every Unit and Structure owned by that Nation.
2. WHILE an enemy Unit or Structure is outside a Nation's currently visible set of Terrain_Cells, THE Vision_System SHALL classify that enemy Unit or Structure as hidden from that Nation.
3. WHEN an enemy Unit or Structure transitions from visible to hidden for a Nation, THE Vision_System SHALL record, as that enemy Unit's or Structure's Last_Known_Position for that Nation, the position of that enemy Unit or Structure at the exact moment of that visible-to-hidden transition.
4. IF a Nation has never had a Last_Known_Position recorded for a given enemy Unit or Structure and that enemy Unit or Structure is currently hidden from that Nation, THEN THE UI_System SHALL NOT display that enemy Unit's or Structure's position on that Nation's UI.
5. WHILE an enemy Unit or Structure remains hidden from a Nation and a Last_Known_Position is recorded for it, THE UI_System SHALL display that enemy Unit's or Structure's Last_Known_Position, rather than its current position, on every UI element that shows Unit and Structure positions for that Nation.
6. WHEN a hidden enemy Unit or Structure re-enters a Nation's currently visible set of Terrain_Cells, THE Vision_System SHALL classify that enemy Unit or Structure as visible to that Nation, and THE UI_System SHALL update the displayed position for that enemy Unit or Structure to its current position on every UI element that shows Unit and Structure positions for that Nation.
7. WHEN a Unit or Structure owned by a Nation moves, is created, or is removed, THE Vision_System SHALL recompute that Nation's currently visible set of Terrain_Cells.
8. WHEN THE Vision_System recomputes a Nation's currently visible set of Terrain_Cells, THE Vision_System SHALL re-evaluate the visible-or-hidden classification of every enemy Unit and Structure for that Nation against the recomputed set.
9. WHEN an enemy Unit or Structure for which a Nation has a recorded Last_Known_Position is destroyed or otherwise permanently removed from the Match while hidden from that Nation, THE Vision_System SHALL discard that Last_Known_Position, and THE UI_System SHALL cease displaying that enemy Unit's or Structure's position on that Nation's UI.

### Requirement 15 — Artillery and Indirect Fire

**User Story:** As a Player, I want artillery units that can bombard targets beyond direct line-of-sight, so that indirect fire and spotting create additional tactical options.

#### Acceptance Criteria

1. WHERE a Unit or Structure type is defined as an Artillery_Unit, THE Combat_System SHALL allow that Artillery_Unit to target any location that is beyond its direct-fire range and within its defined maximum Indirect_Fire range using Indirect_Fire.
2. IF an Artillery_Unit's owning Nation has Spotting on a targeted location and that targeted location is within the Artillery_Unit's maximum Indirect_Fire range, THEN THE Combat_System SHALL accept an Indirect_Fire command issued by that Artillery_Unit against that location.
3. IF an Artillery_Unit's owning Nation lacks Spotting on a targeted location, THEN THE Combat_System SHALL reject the Indirect_Fire command against that location and SHALL present an observable rejection indication to the commanding Player without executing the attack.
4. IF a targeted location is beyond an Artillery_Unit's maximum Indirect_Fire range, THEN THE Combat_System SHALL reject the Indirect_Fire command against that location and SHALL present an observable rejection indication to the commanding Player without executing the attack.
5. WHEN an Indirect_Fire attack is accepted, THE Combat_System SHALL apply the attack's damage to the targeted location after that Artillery_Unit's defined flight delay elapses, rather than immediately, regardless of whether the owning Nation retains Spotting on the targeted location during the flight delay.
6. WHERE an Artillery_Unit's attack defines an Area_Effect radius, WHEN that Indirect_Fire attack resolves, THE Combat_System SHALL apply Area_Effect damage rules at the targeted location.
7. THE VFX_System SHALL render an arcing projectile trail effect, visible to all Nations regardless of Spotting status, for every Indirect_Fire attack for the duration of that attack's flight delay.
