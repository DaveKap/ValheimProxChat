# ValheimProxChat - Proximity Voice Chat for Valheim

A free, open-source BepInEx mod that adds **proximity-based voice chat** to Valheim. Talk to nearby players using your microphone — the closer they are, the louder you hear them.

## Features

- **Proximity-based voice**: Volume scales with distance — full volume when close, fading to silence at max range
- **Push-to-talk or voice activation**: Choose your preferred input mode
- **Configurable range**: Set the maximum voice distance and fade curve
- **Visual indicators**: See who's speaking with on-screen icons above players
- **Microphone level display**: Real-time volume bar shows your mic input level
- **Low bandwidth**: Mu-law compression reduces audio data by 50% with minimal quality loss
- **Low latency**: 20ms transmit interval with immediate playback — no pre-buffering delay
- **No external servers**: All voice data travels through Valheim's built-in networking (ZRoutedRpc)
- **Live config reload**: Edit the .cfg file while playing — changes apply instantly
- **Low bandwidth mode**: Optional setting for players on slower or overseas connections
- **Fully configurable**: Adjust sample rate, volume, distances, keybinds, and more via BepInEx config

## Requirements

- **Valheim** (Steam version)
- **BepInEx 5** (Denikson's BepInExPack for Valheim)
- A working **microphone** (for transmitting; listen-only works without one)
- **All players** in the session need this mod installed

## Installation

### Manual Installation
1. Install [BepInEx 5 for Valheim](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/)
2. Download the latest `ValheimProxChat.dll` from [Releases](https://github.com/davekap/valheimproxchat/releases)
3. Place `ValheimProxChat.dll` into your `Valheim/BepInEx/plugins/` folder
4. Launch Valheim

### Thunderstore (r2modman)
1. Open r2modman or Thunderstore Mod Manager
2. Search for "ValheimProxChat"
3. Click Install

## Server / Host Setup

ValheimProxChat uses Valheim's built-in `ZRoutedRpc` networking to send voice data. This means **no extra ports, no external voice servers, and no special server-side configuration** are required. Voice packets travel over the same connection players already use to play the game.

### Peer-to-peer (host-and-play)

No extra steps. The hosting player's game client relays RPCs to all connected peers automatically. Just make sure every player has the mod installed.

### Dedicated Server

Dedicated servers relay `ZRoutedRpc` messages between clients, even for RPCs the server itself doesn't "know" about. Because of this:

- **The mod does NOT need to be installed on the dedicated server.** Voice packets will still be relayed between clients.
- **However, installing it on the server is recommended** if you want to guarantee compatibility and make future features (such as server-side muting or admin controls) possible. To install on a dedicated server:
  1. Install BepInEx 5 on the dedicated server (same Denikson pack used by clients)
  2. Place `ValheimProxChat.dll` into the server's `BepInEx/plugins/` folder
  3. Restart the server

### Important Notes for Server Owners

| Topic | Details |
|-------|---------|
| **Player requirement** | All players who want to use voice chat must have the mod installed. Players without it will simply not hear or send voice — there is no disruption to their gameplay. |
| **Bandwidth** | Each speaking player adds ~22 KB/s of traffic (at default 22050 Hz / mu-law settings). For a 10-player server where 2-3 people speak at once, expect an extra ~66 KB/s peak. This is negligible for most hosts. |
| **Reducing bandwidth** | Enable `LowBandwidthMode` in the config (~8 KB/s per speaker), or manually lower `SampleRate` to `8000` and increase `TransmitInterval` to `0.1`. |
| **No open ports needed** | Voice data piggybacks on Valheim's existing game connection. If players can connect to your server, voice chat will work — no firewall or port-forwarding changes required. |
| **Mod version matching** | All clients should run the same version of ValheimProxChat to avoid packet format mismatches. |
| **Enforcing the mod** | Valheim does not natively enforce client-side mods. If you want to require it, use a server-side mod-enforcement plugin or communicate the requirement to your players. |
| **Muting / admin controls** | Not yet implemented. Players can mute themselves by not pressing PTT or disabling their mic. Server-side mute support is planned for a future release. |

## Configuration

After first launch, a config file is created at:
```
Valheim/BepInEx/config/com.valheimproxchat.mod.cfg
```

**Config changes are applied live** — just save the file and they take effect immediately, no restart needed.

### Audio Settings
| Setting | Default | Description |
|---------|---------|-------------|
| MaxVoiceDistance | 50 | Maximum hearing distance (meters) |
| FadeStartDistance | 5 | Distance where volume starts fading |
| MicrophoneBoost | 1.5 | Mic input multiplier (1.0 = no boost) |
| OutputVolume | 2.0 | Playback volume gain (applied to audio samples, not clamped to 1.0) |
| SampleRate | 22050 | Audio quality (8000/16000/22050 Hz) |
| MicrophoneDevice | (empty) | Specific mic device name, or empty for default |
| ReverbMix | 0.0 | Valheim reverb zone mix (0.0 = no reverb, 1.0 = full) |

### Input Settings
| Setting | Default | Description |
|---------|---------|-------------|
| PushToTalk | true | Require holding a key to transmit |
| PushToTalkKey | v | Key to hold (Unity KeyCode name) |
| VoiceActivation | false | Auto-transmit when you speak |
| VoiceActivationThreshold | 0.01 | Minimum level to trigger voice activation |

### Network Settings
| Setting | Default | Description |
|---------|---------|-------------|
| TransmitInterval | 0.02 | Seconds between voice packets (20ms) |
| LowBandwidthMode | false | Forces 8000Hz / 100ms for slower connections (~8 KB/s) |

### UI Settings
| Setting | Default | Description |
|---------|---------|-------------|
| ShowSpeakingIndicator | true | Show icon above speaking players |
| ShowVoiceRange | false | Debug: show voice range circle |

## Building from Source

### Prerequisites
- .NET SDK 6.0+
- Valheim installed with BepInEx 5 (Denikson pack)

### Build Steps

1. Clone this repository
2. Set the `VALHEIM_INSTALL` environment variable to your Valheim directory:
   ```bash
   # Linux
   export VALHEIM_INSTALL="$HOME/.steam/steam/steamapps/common/Valheim"
   dotnet build ValheimProxChat/ValheimProxChat.csproj -c Release

   # Windows (note: quotes around the entire set argument to handle parentheses)
   set "VALHEIM_INSTALL=C:\Program Files (x86)\Steam\steamapps\common\Valheim"
   dotnet build ValheimProxChat\ValheimProxChat.csproj -c Release
   ```
   Or pass it directly to MSBuild:
   ```bash
   dotnet build ValheimProxChat/ValheimProxChat.csproj -c Release -p:ValheimInstall="C:\Program Files (x86)\Steam\steamapps\common\Valheim"
   ```
3. The output DLL will be in `ValheimProxChat/bin/Release/netstandard2.1/`

## How It Works

1. **Microphone Capture**: Uses Unity's `Microphone` API to capture audio from your default input device
2. **Compression**: Audio samples are compressed using mu-law encoding (8-bit), cutting bandwidth in half
3. **Network Transport**: Compressed audio chunks are sent to all players via Valheim's `ZRoutedRpc` system
4. **Distance Filtering**: Receivers discard packets from players beyond `MaxVoiceDistance`
5. **Volume Scaling**: Audio volume is scaled linearly between `FadeStartDistance` (full volume) and `MaxVoiceDistance` (silence)
6. **Playback**: Each remote player gets a dedicated `AudioSource` with a streaming circular buffer

### Bandwidth Usage
At default settings (22050Hz sample rate, mu-law compression, 20ms transmit interval):
- ~22 KB/s per speaking player
- Only transmitted while actively speaking (PTT or voice activation)
- With `LowBandwidthMode = true`: ~8 KB/s per speaker

## Architecture

```
ValheimProxChat/
├── Plugin.cs                    # BepInEx plugin entry point
├── Configuration.cs             # Config bindings
├── Patches.cs                   # Harmony patches for game lifecycle
├── PlayerTracker.cs             # Player position utilities
├── VoiceChatUI.cs               # IMGUI overlay (mic status, speaking indicators)
├── Audio/
│   ├── MicrophoneCapture.cs     # Unity Microphone input handling
│   ├── AudioCompression.cs      # Mu-law encode/decode
│   └── AudioPlaybackManager.cs  # Per-player AudioSource management
└── Network/
    └── VoiceNetwork.cs          # ZRoutedRpc voice packet send/receive
```

## Troubleshooting

- **No microphone detected**: Check that your mic is plugged in and set as default in your OS audio settings
- **Can't hear other players**: Ensure all players have the mod installed and are within `MaxVoiceDistance`
- **Audio is choppy**: Try increasing `TransmitInterval` or lowering `SampleRate`
- **Voice too quiet/loud**: Adjust `MicrophoneBoost` (sender input) and `OutputVolume` (receiver gain). Both are applied to samples, not clamped by Unity.
- **Too much reverb/echo**: Set `ReverbMix` to `0` (the default). This bypasses Valheim's reverb zones on voice audio.
- **Audio delay**: Ensure `TransmitInterval` is low (default `0.02`). If you have an old config file, delete it to regenerate with new defaults.
- **PTT key doesn't work**: Make sure you're not in a menu/console. Check the `PushToTalkKey` config value
- **High bandwidth usage**: Enable `LowBandwidthMode` in the config for slower connections

## License

MIT License — see [LICENSE](LICENSE) for details.

## Contributing

Contributions are welcome! Please open an issue or pull request on GitHub.

## Disclaimer

This mod is not affiliated with Iron Gate Studio or Coffee Stain Publishing. Valheim is a trademark of Iron Gate AB. This is a community-made modification created for personal, non-commercial use.
