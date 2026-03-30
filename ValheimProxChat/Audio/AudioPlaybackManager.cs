using System.Collections.Generic;
using UnityEngine;

namespace ValheimProxChat.Audio
{
    /// <summary>
    /// Manages per-player AudioSource instances for voice playback.
    /// Each remote player gets a dedicated AudioSource with a streaming AudioClip.
    /// </summary>
    public class AudioPlaybackManager : MonoBehaviour
    {
        /// <summary>
        /// Tracks playback state for a single remote player.
        /// </summary>
        private class PlayerAudio
        {
            public long PlayerId;
            public string PlayerName;
            public GameObject AudioObject;
            public AudioSource Source;
            public float[] CircularBuffer;
            public int WritePosition;
            public int ReadPosition;
            public int SampleRate;
            public float LastActiveTime;
            public bool IsSpeaking;
            public Vector3 LastPosition;
        }

        private readonly Dictionary<long, PlayerAudio> _playerAudios = new Dictionary<long, PlayerAudio>();
        private const int BufferSizeSeconds = 5;
        private const float CleanupInactiveAfter = 30f;

        /// <summary>
        /// Returns the set of player IDs currently speaking.
        /// </summary>
        public HashSet<long> GetSpeakingPlayers()
        {
            var speaking = new HashSet<long>();
            foreach (var kvp in _playerAudios)
            {
                if (kvp.Value.IsSpeaking)
                    speaking.Add(kvp.Key);
            }
            return speaking;
        }

        /// <summary>
        /// Get the player name for a given player ID, if known.
        /// </summary>
        public string GetPlayerName(long playerId)
        {
            return _playerAudios.TryGetValue(playerId, out var pa) ? pa.PlayerName : null;
        }

        /// <summary>
        /// Play a chunk of voice audio from a remote player.
        /// </summary>
        public void PlayVoiceChunk(long playerId, string playerName, Vector3 position, float[] samples, int sampleRate, float volume)
        {
            if (!_playerAudios.TryGetValue(playerId, out PlayerAudio pa))
            {
                pa = CreatePlayerAudio(playerId, playerName, sampleRate);
                _playerAudios[playerId] = pa;
            }

            pa.PlayerName = playerName;
            pa.LastActiveTime = Time.unscaledTime;
            pa.IsSpeaking = true;
            pa.LastPosition = position;

            // Update audio source position and volume
            pa.AudioObject.transform.position = position;
            pa.Source.volume = volume;

            // Write samples into the circular buffer
            WriteSamplesToBuffer(pa, samples);

            // Ensure the source is playing
            if (!pa.Source.isPlaying)
            {
                pa.Source.Play();
            }
        }

        private PlayerAudio CreatePlayerAudio(long playerId, string playerName, int sampleRate)
        {
            var go = new GameObject($"VoicePlayback_{playerName}_{playerId}");
            go.transform.SetParent(transform);

            var source = go.AddComponent<AudioSource>();
            source.spatialBlend = 0f; // 2D audio - we handle distance attenuation manually
            source.loop = true;
            source.playOnAwake = false;
            source.priority = 0; // Highest priority for voice
            source.reverbZoneMix = Configuration.ReverbMix.Value; // Default 0 = no reverb
            source.bypassReverbZones = Configuration.ReverbMix.Value <= 0.01f;
            source.bypassEffects = Configuration.ReverbMix.Value <= 0.01f;
            source.bypassListenerEffects = Configuration.ReverbMix.Value <= 0.01f;

            int bufferSize = sampleRate * BufferSizeSeconds;

            var pa = new PlayerAudio
            {
                PlayerId = playerId,
                PlayerName = playerName,
                AudioObject = go,
                Source = source,
                CircularBuffer = new float[bufferSize],
                WritePosition = 0,
                ReadPosition = 0,
                SampleRate = sampleRate,
                LastActiveTime = Time.unscaledTime,
                IsSpeaking = false,
                LastPosition = Vector3.zero
            };

            // Create a streaming AudioClip that reads from our circular buffer
            source.clip = AudioClip.Create(
                $"voice_{playerId}",
                bufferSize,
                1, // Mono
                sampleRate,
                true, // Stream
                (data) => OnAudioRead(pa, data),
                (newPosition) => OnAudioSetPosition(pa, newPosition)
            );

            return pa;
        }

        private void WriteSamplesToBuffer(PlayerAudio pa, float[] samples)
        {
            int bufLen = pa.CircularBuffer.Length;
            for (int i = 0; i < samples.Length; i++)
            {
                pa.CircularBuffer[pa.WritePosition] = samples[i];
                pa.WritePosition = (pa.WritePosition + 1) % bufLen;
            }
        }

        private void OnAudioRead(PlayerAudio pa, float[] data)
        {
            int bufLen = pa.CircularBuffer.Length;
            for (int i = 0; i < data.Length; i++)
            {
                // If read has caught up to write, output silence
                if (pa.ReadPosition == pa.WritePosition)
                {
                    data[i] = 0f;
                }
                else
                {
                    data[i] = pa.CircularBuffer[pa.ReadPosition];
                    pa.CircularBuffer[pa.ReadPosition] = 0f; // Clear after reading
                    pa.ReadPosition = (pa.ReadPosition + 1) % bufLen;
                }
            }
        }

        private void OnAudioSetPosition(PlayerAudio pa, int newPosition)
        {
            pa.ReadPosition = newPosition % pa.CircularBuffer.Length;
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            var toRemove = new List<long>();

            foreach (var kvp in _playerAudios)
            {
                var pa = kvp.Value;

                // Mark as not speaking if no data received recently
                if (now - pa.LastActiveTime > 0.5f)
                {
                    pa.IsSpeaking = false;
                }

                // Clean up inactive player audio objects
                if (now - pa.LastActiveTime > CleanupInactiveAfter)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (long id in toRemove)
            {
                if (_playerAudios.TryGetValue(id, out PlayerAudio pa))
                {
                    if (pa.Source != null) pa.Source.Stop();
                    if (pa.AudioObject != null) Destroy(pa.AudioObject);
                    _playerAudios.Remove(id);
                }
            }
        }

        private void OnDestroy()
        {
            foreach (var kvp in _playerAudios)
            {
                if (kvp.Value.AudioObject != null)
                    Destroy(kvp.Value.AudioObject);
            }
            _playerAudios.Clear();
        }
    }
}
