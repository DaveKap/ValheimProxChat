using System.Collections.Generic;
using UnityEngine;

namespace ValheimProxChat.Audio
{
    /// <summary>
    /// Manages per-player AudioSource instances for voice playback.
    /// Each remote player gets a dedicated AudioSource with a streaming AudioClip.
    /// Uses a tight circular buffer with read/write tracking for low-latency playback.
    /// </summary>
    public class AudioPlaybackManager : MonoBehaviour
    {
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
            public bool HasStartedPlaying;
            public int SamplesBuffered;
        }

        private readonly Dictionary<long, PlayerAudio> _playerAudios = new Dictionary<long, PlayerAudio>();

        // Keep buffer small for low latency — 2 seconds is plenty
        private const int BufferSizeSeconds = 2;
        private const float CleanupInactiveAfter = 30f;

        // Minimum samples to buffer before starting playback (~60ms at any sample rate)
        // This prevents starting playback before enough data exists to avoid immediate underrun
        private const float MinBufferBeforePlaySeconds = 0.06f;

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

        public string GetPlayerName(long playerId)
        {
            return _playerAudios.TryGetValue(playerId, out var pa) ? pa.PlayerName : null;
        }

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

            pa.AudioObject.transform.position = position;
            pa.Source.volume = volume;

            // Write samples into the circular buffer
            WriteSamplesToBuffer(pa, samples);

            // Start playback once we've buffered enough to avoid underruns
            if (!pa.HasStartedPlaying)
            {
                int minSamples = (int)(sampleRate * MinBufferBeforePlaySeconds);
                if (pa.SamplesBuffered >= minSamples)
                {
                    pa.Source.Play();
                    pa.HasStartedPlaying = true;
                }
            }
        }

        private PlayerAudio CreatePlayerAudio(long playerId, string playerName, int sampleRate)
        {
            var go = new GameObject($"VoicePlayback_{playerName}_{playerId}");
            go.transform.SetParent(transform);

            var source = go.AddComponent<AudioSource>();
            source.spatialBlend = 0f;
            source.loop = true;
            source.playOnAwake = false;
            source.priority = 0;
            source.reverbZoneMix = Configuration.ReverbMix.Value;
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
                LastPosition = Vector3.zero,
                HasStartedPlaying = false,
                SamplesBuffered = 0
            };

            source.clip = AudioClip.Create(
                $"voice_{playerId}",
                bufferSize,
                1,
                sampleRate,
                true,
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
            pa.SamplesBuffered += samples.Length;

            // Cap tracked buffered amount to buffer size
            if (pa.SamplesBuffered > bufLen)
                pa.SamplesBuffered = bufLen;
        }

        private void OnAudioRead(PlayerAudio pa, float[] data)
        {
            int bufLen = pa.CircularBuffer.Length;
            for (int i = 0; i < data.Length; i++)
            {
                if (pa.SamplesBuffered <= 0)
                {
                    // Underrun: output silence
                    data[i] = 0f;
                }
                else
                {
                    data[i] = pa.CircularBuffer[pa.ReadPosition];
                    pa.CircularBuffer[pa.ReadPosition] = 0f;
                    pa.ReadPosition = (pa.ReadPosition + 1) % bufLen;
                    pa.SamplesBuffered--;
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
                if (now - pa.LastActiveTime > 0.3f)
                {
                    pa.IsSpeaking = false;
                }

                // If stopped speaking, stop the source and reset so next speech starts fresh
                if (!pa.IsSpeaking && pa.HasStartedPlaying && pa.SamplesBuffered <= 0)
                {
                    pa.Source.Stop();
                    pa.HasStartedPlaying = false;
                    pa.WritePosition = 0;
                    pa.ReadPosition = 0;
                }

                // Clean up long-inactive player audio objects
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
