# ValheimVoiceServer — Standalone Proximity Voice Chat

A **standalone UDP voice relay server** + **BepInEx client mod** for Valheim proximity voice chat. Runs alongside a Valheim dedicated server to provide low-latency, high-quality voice without impacting game networking.

## Why This Over the ZRoutedRpc Version?

The base ValheimProxChat mod piggybacks on Valheim's built-in `ZRoutedRpc` networking, which is reliable/ordered (TCP-like) and has a ~50-64 kbps per-connection send limit. This causes:
- Late TCP retransmissions creating delay spikes
- Voice competing with game traffic for bandwidth
- Sample rate limited to ~16kHz to stay within budget

This version uses a **dedicated UDP socket** that is completely independent of Valheim's networking:

| Aspect | ZRoutedRpc version | Server version |
|--------|-------------------|----------------|
| Transport | Reliable/ordered (TCP-like) | Unreliable UDP (ideal for voice) |
| Bandwidth | ~50-64 kbps shared with game | Unlimited |
| Late packets | Retransmitted, cause delays | Dropped naturally |
| Proximity filter | Client-side | Server-side (saves bandwidth) |
| Game traffic impact | Competes with ZDO sync | Zero |
| Sample rate | ~16kHz max | 22-48kHz easily |
| Requires | Just the mod | Server process + mod |

## Components

### ValheimVoiceServer (standalone console app)
- Lightweight .NET 6 console application
- Runs on the same machine as the Valheim dedicated server
- Listens on a UDP port (default 9876)
- Receives voice + position data from clients
- Relays voice only to players within proximity range (server-side filtering)
- No Valheim or Unity dependency

### ValheimVoiceClient (BepInEx mod)
- Replaces the ZRoutedRpc transport with direct UDP to the voice server
- Still hooks into Valheim for player position, PTT, UI
- Sends voice + position to the voice server
- Receives pre-filtered voice from nearby players
- Same audio capture/playback pipeline as the base mod

## Quick Start

### Server Setup
1. Build the server:
   ```bash
   dotnet build ValheimVoiceServer/ValheimVoiceServer.csproj -c Release
   ```
2. Copy the output to your server machine
3. Open UDP port 9876 in your firewall
4. Run:
   ```bash
   ./ValheimVoiceServer
   # or with options:
   ./ValheimVoiceServer --port 9876 --distance 50
   ```
5. Or edit `voiceserver.cfg` in the same directory

### Client Setup
1. Build the client mod:
   ```bash
   set "VALHEIM_INSTALL=C:\Program Files (x86)\Steam\steamapps\common\Valheim"
   dotnet build ValheimVoiceClient/ValheimVoiceClient.csproj -c Release
   ```
2. Place `ValheimVoiceClient.dll` in `BepInEx/plugins/`
3. **Remove `ValheimProxChat.dll`** if you had the base mod installed (they conflict)
4. Launch Valheim — the mod auto-detects the voice server address from your Valheim connection
5. Or set `VoiceServerAddress` and `VoiceServerPort` in the config manually

## Server Configuration

Edit `voiceserver.cfg` or pass command-line arguments:

| Setting | Default | CLI Flag | Description |
|---------|---------|----------|-------------|
| port | 9876 | `--port` | UDP listen port |
| maxvoicedistance | 50 | `--distance` | Max hearing distance (meters) |
| fadestartdistance | 5 | `--fade` | Distance where fade begins |
| clienttimeout | 30 | `--timeout` | Seconds before inactive client cleanup |

## Client Configuration

Config file: `BepInEx/config/com.valheimvoiceclient.mod.cfg`

| Setting | Default | Description |
|---------|---------|-------------|
| VoiceServerAddress | (auto) | Voice server IP. Empty = auto-detect from Valheim connection |
| VoiceServerPort | 9876 | Voice server UDP port |
| SampleRate | 22050 | Audio quality — no Valheim bandwidth limit! |
| MicrophoneBoost | 1.5 | Mic input multiplier |
| OutputVolume | 2.0 | Playback gain |
| ReverbMix | 0.0 | Valheim reverb zone mix |
| PushToTalk | true | Require key to transmit |
| PushToTalkKey | v | PTT key |
| TransmitInterval | 0.02 | Packet send interval (20ms) |
| PositionUpdateInterval | 0.25 | Position update interval when silent |

## Network Architecture

```
Player A (BepInEx mod)                Voice Server (UDP)              Player B (BepInEx mod)
    |                                       |                              |
    |-- UDP: voice + position ------------->|                              |
    |                                       |-- distance check A<->B       |
    |                                       |-- in range? relay:           |
    |                                       |---- UDP: voice + volume ---->|
    |                                       |                              |
    |<---- UDP: voice + volume -------------|<- if B speaks & in range     |
```

- All voice traffic bypasses Valheim's networking entirely
- The server calculates proximity volume and sends it with the packet
- Clients only receive audio from players the server determines are nearby
- Position-only updates are sent every 250ms when not speaking

## Firewall

Server operators need to open **one UDP port** (default 9876). Players don't need to open any ports — UDP replies go back through the same NAT mapping.

## License

MIT — see [LICENSE](../LICENSE) for details.

## Disclaimer

This mod is not affiliated with Iron Gate Studio or Coffee Stain Publishing. Valheim is a trademark of Iron Gate AB.
