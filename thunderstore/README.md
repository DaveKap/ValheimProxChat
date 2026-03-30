# ValheimProxChat

Adds **proximity-based voice chat** to Valheim. Talk to nearby players using your microphone — volume scales with distance.

## Features
- Proximity voice chat with distance-based volume falloff
- Push-to-talk or voice activation modes
- Visual speaking indicators above players
- No external servers — uses Valheim's built-in networking
- Mu-law compression for low bandwidth usage
- Fully configurable via BepInEx config

## Requirements
- All players need this mod installed
- A working microphone (for transmitting)

## Quick Start
1. Install the mod
2. Join a multiplayer world
3. Hold **V** (default) to talk — nearby players will hear you!

## Configuration
Edit `BepInEx/config/com.valheimproxchat.mod.cfg` to customize:
- Voice range (default: 25m)
- Push-to-talk key
- Sample rate and volume
- Voice activation mode

See the [GitHub page](https://github.com/davekap/valheimproxchat) for full documentation.
