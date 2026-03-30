using System;
using UnityEngine;
using ValheimProxChat.Audio;

namespace ValheimProxChat.Network
{
    /// <summary>
    /// Handles sending and receiving voice data over Valheim's ZRoutedRpc network system.
    /// Voice packets are sent as routed RPCs to all peers, then filtered by distance on the receiving end.
    /// </summary>
    public class VoiceNetwork : MonoBehaviour
    {
        private const string RpcVoiceData = "ValheimProxChat_VoiceData";

        private MicrophoneCapture _micCapture;
        private AudioPlaybackManager _playbackManager;
        private bool _registered;

        public void Initialize(MicrophoneCapture micCapture, AudioPlaybackManager playbackManager)
        {
            _micCapture = micCapture;
            _playbackManager = playbackManager;

            _micCapture.OnAudioChunkReady += OnLocalAudioChunk;
        }

        private void Update()
        {
            // Register RPC when ZRoutedRpc becomes available (after connecting to a world)
            if (!_registered && ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.Register<ZPackage>(RpcVoiceData, OnReceiveVoiceData);
                _registered = true;
                Plugin.Log.LogInfo("Voice chat network registered.");
            }

            // Unregister if we've disconnected
            if (_registered && ZRoutedRpc.instance == null)
            {
                _registered = false;
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

            // Build the network packet
            ZPackage pkg = new ZPackage();
            pkg.Write(Player.m_localPlayer.GetPlayerID());
            pkg.Write(Player.m_localPlayer.GetPlayerName());
            pkg.Write(sampleRate);
            pkg.Write(compressedData);

            // Write our position so receivers can calculate distance
            Vector3 pos = Player.m_localPlayer.transform.position;
            pkg.Write(pos.x);
            pkg.Write(pos.y);
            pkg.Write(pos.z);

            // Send to all peers (ZRoutedRpc.Everybody)
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcVoiceData, pkg);
        }

        /// <summary>
        /// Called when we receive a voice data packet from another player.
        /// </summary>
        private void OnReceiveVoiceData(long sender, ZPackage pkg)
        {
            try
            {
                long playerId = pkg.ReadLong();
                string playerName = pkg.ReadString();
                int sampleRate = pkg.ReadInt();
                byte[] compressedData = pkg.ReadByteArray();
                float posX = pkg.ReadSingle();
                float posY = pkg.ReadSingle();
                float posZ = pkg.ReadSingle();

                // Don't play back our own voice
                if (Player.m_localPlayer != null && playerId == Player.m_localPlayer.GetPlayerID())
                    return;

                Vector3 senderPosition = new Vector3(posX, posY, posZ);

                // Calculate distance from local player
                if (Player.m_localPlayer == null) return;
                float distance = Vector3.Distance(Player.m_localPlayer.transform.position, senderPosition);

                float maxDistance = Configuration.MaxVoiceDistance.Value;
                if (distance > maxDistance) return; // Too far away, don't play

                // Calculate volume based on distance (0-1 range for AudioSource)
                float volume = CalculateProximityVolume(distance);

                // Decompress audio
                float[] samples = AudioCompression.Decompress(compressedData, 0, compressedData.Length);

                // Hand off to the playback manager
                _playbackManager.PlayVoiceChunk(playerId, playerName, senderPosition, samples, sampleRate, volume);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Error processing voice packet: {e.Message}");
            }
        }

        /// <summary>
        /// Calculate volume based on distance using linear falloff between fade start and max distance.
        /// </summary>
        private static float CalculateProximityVolume(float distance)
        {
            float fadeStart = Configuration.FadeStartDistance.Value;
            float maxDistance = Configuration.MaxVoiceDistance.Value;

            if (distance <= fadeStart) return 1f;
            if (distance >= maxDistance) return 0f;

            // Linear falloff
            return 1f - (distance - fadeStart) / (maxDistance - fadeStart);
        }
    }
}
