using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimProxChat.Audio;

namespace ValheimProxChat.Network
{
    /// <summary>
    /// Handles sending and receiving voice data over Valheim's ZRoutedRpc network system.
    ///
    /// ZRoutedRpc runs over SteamNetworking which is reliable and ordered (TCP-like).
    /// This means late retransmissions can cause delay spikes for real-time audio.
    /// To mitigate this:
    ///   - Packets include a timestamp so receivers can drop stale audio
    ///   - We skip sending entirely when no players are within voice range
    ///   - Per-packet overhead is minimized (no redundant player name on every packet)
    /// </summary>
    public class VoiceNetwork : MonoBehaviour
    {
        private const string RpcVoiceData = "ValheimProxChat_VoiceData";

        // Packet version byte — increment if the packet format changes
        private const byte PacketVersion = 2;

        // Maximum age (in seconds) of a voice packet before it's dropped.
        // Since ZRoutedRpc is reliable/ordered, TCP retransmissions can deliver
        // stale packets late. Playing old audio causes jarring delay spikes.
        private const float MaxPacketAge = 0.5f;

        private MicrophoneCapture _micCapture;
        private AudioPlaybackManager _playbackManager;
        private bool _registered;

        // Cache of known player names by ID, so we don't need to send the name
        // in every single packet (50/sec). Sender includes name periodically,
        // receiver caches it.
        private readonly Dictionary<long, string> _knownPlayerNames = new Dictionary<long, string>();
        private float _lastNameBroadcast;
        private const float NameBroadcastInterval = 2.0f; // Send name every 2 seconds

        public void Initialize(MicrophoneCapture micCapture, AudioPlaybackManager playbackManager)
        {
            _micCapture = micCapture;
            _playbackManager = playbackManager;

            _micCapture.OnAudioChunkReady += OnLocalAudioChunk;
        }

        private void Update()
        {
            if (!_registered && ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.Register<ZPackage>(RpcVoiceData, OnReceiveVoiceData);
                _registered = true;
                Plugin.Log.LogInfo("Voice chat network registered.");
            }

            if (_registered && ZRoutedRpc.instance == null)
            {
                _registered = false;
                _knownPlayerNames.Clear();
            }
        }

        private void OnDestroy()
        {
            if (_micCapture != null)
                _micCapture.OnAudioChunkReady -= OnLocalAudioChunk;
        }

        /// <summary>
        /// Called when the local microphone has a compressed audio chunk ready to send.
        /// </summary>
        private void OnLocalAudioChunk(byte[] compressedData, int sampleRate)
        {
            if (!_registered || ZRoutedRpc.instance == null) return;
            if (Player.m_localPlayer == null) return;

            // Optimization: don't send voice data if no other players are within hearing range.
            // This avoids flooding the server with packets that every receiver will just discard.
            Vector3 myPos = Player.m_localPlayer.transform.position;
            if (!AnyPlayersInRange(myPos, Configuration.MaxVoiceDistance.Value))
                return;

            // Determine whether to include the player name in this packet.
            // Sending the name every packet wastes ~20+ bytes * 50 packets/sec.
            // Instead, send it every few seconds. Receivers cache it.
            float now = Time.unscaledTime;
            bool includeName = (now - _lastNameBroadcast) >= NameBroadcastInterval;

            ZPackage pkg = new ZPackage();
            pkg.Write(PacketVersion);
            pkg.Write(Player.m_localPlayer.GetPlayerID());

            // Flags byte: bit 0 = name included
            byte flags = 0;
            if (includeName) flags |= 0x01;
            pkg.Write(flags);

            if (includeName)
            {
                pkg.Write(Player.m_localPlayer.GetPlayerName());
                _lastNameBroadcast = now;
            }

            pkg.Write(sampleRate);
            pkg.Write(compressedData);

            // Position for distance calculation
            pkg.Write(myPos.x);
            pkg.Write(myPos.y);
            pkg.Write(myPos.z);

            // Timestamp for stale packet detection (sender's local time)
            pkg.Write(now);

            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcVoiceData, pkg);
        }

        /// <summary>
        /// Returns true if any other player is within the given range.
        /// </summary>
        private static bool AnyPlayersInRange(Vector3 position, float range)
        {
            float rangeSq = range * range;
            foreach (Player p in Player.GetAllPlayers())
            {
                if (p == Player.m_localPlayer) continue;
                // Use sqrMagnitude to avoid sqrt per player
                if ((p.transform.position - position).sqrMagnitude <= rangeSq)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Called when we receive a voice data packet from another player.
        /// </summary>
        private void OnReceiveVoiceData(long sender, ZPackage pkg)
        {
            try
            {
                byte version = pkg.ReadByte();

                // Handle both old (v1) and new (v2) packet formats during transition
                if (version != PacketVersion)
                {
                    HandleLegacyPacket(version, pkg);
                    return;
                }

                long playerId = pkg.ReadLong();
                byte flags = pkg.ReadByte();
                bool hasName = (flags & 0x01) != 0;

                string playerName = null;
                if (hasName)
                {
                    playerName = pkg.ReadString();
                    _knownPlayerNames[playerId] = playerName;
                }
                else
                {
                    _knownPlayerNames.TryGetValue(playerId, out playerName);
                }

                // Fall back to looking up the player object if we don't have a cached name
                if (playerName == null)
                {
                    foreach (Player p in Player.GetAllPlayers())
                    {
                        if (p.GetPlayerID() == playerId)
                        {
                            playerName = p.GetPlayerName();
                            _knownPlayerNames[playerId] = playerName;
                            break;
                        }
                    }
                    if (playerName == null)
                        playerName = $"Player_{playerId}";
                }

                int sampleRate = pkg.ReadInt();
                byte[] compressedData = pkg.ReadByteArray();
                float posX = pkg.ReadSingle();
                float posY = pkg.ReadSingle();
                float posZ = pkg.ReadSingle();
                float senderTime = pkg.ReadSingle();

                // Don't play back our own voice
                if (Player.m_localPlayer != null && playerId == Player.m_localPlayer.GetPlayerID())
                    return;

                // Drop stale packets. Since ZRoutedRpc is reliable (TCP-like),
                // retransmitted packets can arrive late. Playing old audio at
                // the wrong time is worse than dropping it.
                // We can't compare sender time directly (different clocks), but
                // we can track the latest timestamp per sender and drop anything older.
                if (!IsPacketFresh(playerId, senderTime))
                    return;

                Vector3 senderPosition = new Vector3(posX, posY, posZ);

                if (Player.m_localPlayer == null) return;
                float distance = Vector3.Distance(Player.m_localPlayer.transform.position, senderPosition);

                float maxDistance = Configuration.MaxVoiceDistance.Value;
                if (distance > maxDistance) return;

                float volume = CalculateProximityVolume(distance);

                float[] samples = AudioCompression.Decompress(compressedData, 0, compressedData.Length);

                _playbackManager.PlayVoiceChunk(playerId, playerName, senderPosition, samples, sampleRate, volume);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Error processing voice packet: {e.Message}");
            }
        }

        /// <summary>
        /// Handle packets from older mod versions that use a different format.
        /// The old format started with a long (playerId), whose first byte would be
        /// non-zero and != PacketVersion, so we can distinguish them.
        /// </summary>
        private void HandleLegacyPacket(byte firstByte, ZPackage pkg)
        {
            // Old v1 format: playerId(long), playerName(string), sampleRate(int),
            //                compressedData(byte[]), posX/Y/Z(float)
            // The firstByte we already read was the first byte of the playerId long.
            // We can't reliably reconstruct it, so just drop legacy packets.
            // Players will need to update together.
        }

        // Track the latest timestamp seen per sender to detect out-of-order packets
        private readonly Dictionary<long, float> _lastPacketTime = new Dictionary<long, float>();

        /// <summary>
        /// Returns true if this packet is newer than the last one from this sender.
        /// Drops out-of-order packets that arrived late due to TCP retransmission.
        /// </summary>
        private bool IsPacketFresh(long playerId, float senderTime)
        {
            if (_lastPacketTime.TryGetValue(playerId, out float lastTime))
            {
                // Drop packets older than the last one we processed.
                // Allow a small tolerance for float precision.
                if (senderTime < lastTime - 0.001f)
                    return false;
            }
            _lastPacketTime[playerId] = senderTime;
            return true;
        }

        private static float CalculateProximityVolume(float distance)
        {
            float fadeStart = Configuration.FadeStartDistance.Value;
            float maxDistance = Configuration.MaxVoiceDistance.Value;

            if (distance <= fadeStart) return 1f;
            if (distance >= maxDistance) return 0f;

            return 1f - (distance - fadeStart) / (maxDistance - fadeStart);
        }
    }
}
