# ValheimProxChat - Proximity Voice Chat for Valheim

A free, open-source BepInEx mod that adds **proximity-based voice chat** to Valheim. Talk to nearby players using your microphone — the closer they are, the louder you hear them.

## Features

- **Proximity-based voice**: Volume scales with distance — full volume when close, fading to silence at max range
- **Push-to-talk or voice activation**: Choose your preferred input mode
- **Configurable range**: Set the maximum voice distance and fade curve
- **Visual indicators**: See who's speaking with on-screen icons above players
- **Microphone level display**: Real-time volume bar shows your mic input level
- **Low bandwidth**: Mu-law compression reduces audio data by 50% with minimal quality loss
- **No external servers**: All voice data travels through Valheim's built-in networking (ZRoutedRpc)
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

## Configuration

After first launch, a config file is created at:
```
Valheim/BepInEx/config/com.valheimproxchat.mod.cfg
```

### Audio Settings
| Setting | Default | Description |
|---------|---------|-------------|
| MaxVoiceDistance | 25 | Maximum hearing distance (meters) |
| FadeStartDistance | 5 | Distance where volume starts fading |
| MicrophoneBoost | 1.0 | Mic input multiplier |
| OutputVolume | 1.0 | Playback volume multiplier |
| SampleRate | 16000 | Audio quality (8000/16000/22050 Hz) |
| MicrophoneDevice | (empty) | Specific mic device name, or empty for default |

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
| TransmitInterval | 0.1 | Seconds between voice packets |

### UI Settings
| Setting | Default | Description |
|---------|---------|-------------|
| ShowSpeakingIndicator | true | Show icon above speaking players |
| ShowVoiceRange | false | Debug: show voice range circle |

## Building from Source

### Prerequisites
- .NET SDK 6.0+ (or Visual Studio / Rider with .NET Framework 4.6.2 targeting pack)
- Valheim installed with BepInEx 5 (Denikson pack)

### Build Steps

1. Clone this repository
2. Set the `VALHEIM_INSTALL` environment variable to your Valheim directory:
   ```bash
   # Linux
   export VALHEIM_INSTALL="$HOME/.steam/steam/steamapps/common/Valheim"

   # Windows
   set VALHEIM_INSTALL=C:\Program Files (x86)\Steam\steamapps\common\Valheim
   ```
3. Build:
   ```bash
   dotnet build ValheimProxChat/ValheimProxChat.csproj -c Release
   ```
4. The output DLL will be in `ValheimProxChat/bin/Release/net462/`

## How It Works

1. **Microphone Capture**: Uses Unity's `Microphone` API to capture audio from your default input device
2. **Compression**: Audio samples are compressed using mu-law encoding (8-bit), cutting bandwidth in half
3. **Network Transport**: Compressed audio chunks are sent to all players via Valheim's `ZRoutedRpc` system
4. **Distance Filtering**: Receivers discard packets from players beyond `MaxVoiceDistance`
5. **Volume Scaling**: Audio volume is scaled linearly between `FadeStartDistance` (full volume) and `MaxVoiceDistance` (silence)
6. **Playback**: Each remote player gets a dedicated `AudioSource` with a streaming circular buffer

### Bandwidth Usage
At default settings (16kHz sample rate, mu-law compression, 100ms transmit interval):
- ~16 KB/s per speaking player
- Only transmitted while actively speaking (PTT or voice activation)

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
- **Voice too quiet/loud**: Adjust `MicrophoneBoost` (input) and `OutputVolume` (output)
- **PTT key doesn't work**: Make sure you're not in a menu/console. Check the `PushToTalkKey` config value

## License

MIT License — see [LICENSE](LICENSE) for details.

## Contributing

Contributions are welcome! Please open an issue or pull request on GitHub.

## Disclaimer

This mod is not affiliated with Iron Gate Studio or Coffee Stain Publishing. Valheim is a trademark of Iron Gate AB. This is a community-made modification created for personal, non-commercial use.
