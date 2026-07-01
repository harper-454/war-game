# Epoch War

A highly interactive resource & unit management war game — start from the beginning of time, advance through eras, and be the first to achieve victory through military dominance, peaceful ascension, or doomsday deployment.

## Features
- **Full era progression** from Prehistoric to Space Age
- **Deep combat** with flanking, cover, area-of-effect, veterancy, unit abilities
- **Fog of war** with vision, spotting, and last-known-position tracking
- **Artillery & indirect fire** with flight delay and spotting requirements
- **Base building** with structures, population, and resource management
- **3 victory paths**: Annihilation, Peace Arch ascension, Doomsday weapon
- **Multiplayer** (host-authoritative via Unity Netcode for GameObjects)
- **Graphics settings** with Low/Medium/High/Ultra presets
- **URP visuals** with destructible terrain, combat VFX, atmosphere/weather

## Requirements
- Unity **6000.5.2f1** (Unity 6.2)
- Universal Render Pipeline package (`com.unity.render-pipelines.universal`)
- See `BRIDGE_SETUP.md` for CI setup and `Assets/EpochWar/Unity/URP_SETUP.md` for visual setup

## Project Structure
- `Assets/EpochWar/Core/` — Engine-free deterministic game logic (C#, no UnityEngine dependency)
- `Assets/EpochWar/Unity/` — Unity presentation layer (URP, UI Toolkit, Netcode)
- `Assets/EpochWar/Tests/` — FsCheck property-based tests + unit tests
