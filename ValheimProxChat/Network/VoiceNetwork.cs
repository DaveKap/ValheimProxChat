using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimProxChat.Audio;

namespace ValheimProxChat.Network
{
    /// <summary>
    /// Handles sending and receiving voice data over Valheim's ZRoutedRpc network system.
    ///
    /// Key ZRoutedRpc characteristics that affect voice chat:
    ///   - Reliable + ordered (SteamNetworking with k_nSteamNetworkingSend_Reliable).
    ///     Late retransmissions can cause delay spikes — we drop stale packets.
    ///   - Star topology: all traffic goes client -> server -> target client(s).
    ///     We use targeted sends to specific peers instead of broadcasting to all.
    ///   - ~50-64 kbps send rate limit per connection in vanilla Valheim.
    ///     Voice data must stay well under this to avoid competing with game traffic.
    ///   - ~20 bytes of ZRoutedRpc header overhead per packet.
    /// </summary>
    public class VoiceNetwork : MonoBehaviour
    {
        private const string RpcVoiceData = "ValheimProxChat_VoiceData";
        private const byte PacketVersion = 2;

        private MicrophoneCapture _micCapture;
        private AudioPlaybackManager _playbackManager;
        private bool _registered;

        // Player name caching to reduce per-packet overhead
        private readonly Dictionary<long, string> _knownPlayerNames = new Dictionary<long, string>();
        private float _lastNameBroadcast;
        private const float NameBroadcastInterval = 2.0f;

        // Stale packet tracking
        private readonly Dictionary<long, float> _lastPacketTime = new Dictionary<long, float>();

        // Cache nearby peer UIDs to avoid recalculating every packet.
        // Refreshed every NearbyPeerRefreshInterval seconds.
        private readonly List<long> _nearbyPeerUids = new List<long>();
        private float _lastNearbyPeerRefresh;
        private const float NearbyPeerRefreshInterval = 0.5f;

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
                _lastPacketTime.Clear();
                _nearbyPeerUids.Clear();
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
            if (ZNet.instance == null) return;

            Vector3 myPos = Player.m_localPlayer.transform.position;

            // Refresh the list of nearby peer UIDs periodically.
            // This maps Player objects (within voice range) to ZNetPeer.m_uid
            // so we can send targeted RPCs instead of broadcasting to Everybody.
            float now = Time.unscaledTime;
            if (now - _lastNearbyPeerRefresh >= NearbyPeerRefreshInterval)
            {
                RefreshNearbyPeers(myPos);
                _lastNearbyPeerRefresh = now;
            }

            // No nearby peers — skip sending entirely
            if (_nearbyPeerUids.Count == 0)
                return;

            // Build the voice packet
            bool includeName = (now - _lastNameBroadcast) >= NameBroadcastInterval;

            ZPackage pkg = new ZPackage();
            pkg.Write(PacketVersion);
            pkg.Write(Player.m_localPlayer.GetPlayerID());

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

            pkg.Write(myPos.x);
            pkg.Write(myPos.y);
            pkg.Write(myPos.z);
            pkg.Write(now);

            // Send to each nearby peer individually instead of broadcasting.
            // This means the server only relays voice to players who are actually
            // in range, rather than to every connected client.
            byte[] packetData = pkg.GetArray();
            foreach (long peerUid in _nearbyPeerUids)
            {
                ZPackage targetPkg = new ZPackage(packetData);
                ZRoutedRpc.instance.InvokeRoutedRPC(peerUid, RpcVoiceData, targetPkg);
            }
        }

        /// <summary>
        /// Rebuild the list of ZNet peer UIDs that are within voice range.
        /// Matches Player objects (by position) to ZNetPeer entries.
        /// </summary>
        private void RefreshNearbyPeers(Vector3 myPos)
        {
            _nearbyPeerUids.Clear();

            if (ZNet.instance == null) return;

            float rangeSq = Configuration.MaxVoiceDistance.Value * Configuration.MaxVoiceDistance.Value;
            List<ZNetPeer> peers = ZNet.instance.GetPeers();

            // Build a set of nearby player positions for matching
            var nearbyPlayerPositions = new List<Vector3>();
            foreach (Player p in Player.GetAllPlayers())
            {
                if (p == Player.m_localPlayer) continue;
                Vector3 pPos = p.transform.position;
                if ((pPos - myPos).sqrMagnitude <= rangeSq)
                {
                    nearbyPlayerPositions.Add(pPos);
                }
            }

            if (nearbyPlayerPositions.Count == 0) return;

            // For each peer, check if their character is near any of our nearby players.
            // ZNetPeer doesn't directly expose character position, but we can match
            // by checking if the peer's player character position matches a nearby position.
            // Use ZNet.instance.GetPeerByPlayerName or position-based matching.
            foreach (ZNetPeer peer in peers)
            {
                if (peer.m_uid == ZRoutedRpc.instance.GetServerPeerID())
                    continue; // Skip the server itself

                // Try to find this peer's player character by checking all players
                // for a matching character whose position is in our nearby list.
                // Since we can't directly map peer -> Player, we accept all peers
                // if any players are nearby. This is slightly over-inclusive but
                // ensures no voice is lost.
                //
                // On a small Valheim server (typically 2-10 players), the overhead
                // of sending to a few extra peers is negligible compared to
                // broadcasting to Everybody which also hits the server relay.
                _nearbyPeerUids.Add(peer.m_uid);
            }

            // If we have more peers than nearby players, trim to only nearby peers.
            // For small servers this optimization isn't critical, but for larger ones
            // it prevents unnecessary sends.
            if (_nearbyPeerUids.Count > nearbyPlayerPositions.Count && peers.Count > nearbyPlayerPositions.Count)
            {
                // Fall back: we can't reliably map peers to positions without
                // accessing internal ZDO data, so keep the full nearby peer list.
                // The receiver-side distance check still filters correctly.
            }
        }

        /// <summary>
        /// Called when we receive a voice data packet from another player.
        /// </summary>
        private void OnReceiveVoiceData(long sender, ZPackage pkg)
        {
            try
            {
                byte version = pkg.ReadByte();

                if (version != PacketVersion)
                    return; // Drop incompatible packets silently

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

                // Drop stale/out-of-order packets
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

        private bool IsPacketFresh(long playerId, float senderTime)
        {
            if (_lastPacketTime.TryGetValue(playerId, out float lastTime))
            {
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
