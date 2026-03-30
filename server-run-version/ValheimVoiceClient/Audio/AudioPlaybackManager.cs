using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace ValheimVoiceClient.Audio
{
    /// <summary>
    /// Manages per-player AudioSource instances for voice playback.
    /// Each remote player gets a dedicated AudioSource with a streaming AudioClip.
    /// Uses a tight circular buffer with read/write tracking for low-latency playback.
    ///
    /// Thread safety: OnAudioRead is called from Unity's audio thread while
    /// WriteSamplesToBuffer runs on the main thread. SamplesBuffered uses
    /// Interlocked operations to avoid races.
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
            public int SamplesBuffered;
        }

        private readonly Dictionary<long, PlayerAudio> _playerAudios = new Dictionary<long, PlayerAudio>();

        private const int BufferSizeSeconds = 2;
        private const float CleanupInactiveAfter = 30f;
        private const float SpeakingTimeout = 1.0f;
        private const float BufferResetTimeout = 2.0f;

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

            WriteSamplesToBuffer(pa, samples);

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
            source.spatialBlend = 1.0f; // Fully 3D — Unity handles directional panning
            source.rolloffMode = AudioRolloffMode.Custom;
            // Flat rolloff curve: Unity won't attenuate by distance (we do it ourselves
            // via OutputVolume gain in WriteSamplesToBuffer). This lets Unity handle only
            // the directional stereo panning based on the AudioSource's world position.
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff,
                AnimationCurve.Linear(0f, 1f, 1f, 1f));
            source.minDistance = 0f;
            source.maxDistance = 500f;
            source.spread = 60f; // Stereo spread angle in degrees
            source.dopplerLevel = 0f; // Disable doppler for voice
            source.loop = true;
            source.playOnAwake = false;
            source.priority = 0;
            source.reverbZoneMix = Plugin.ReverbMix.Value;
            source.bypassReverbZones = Plugin.ReverbMix.Value <= 0.01f;
            source.bypassEffects = Plugin.ReverbMix.Value <= 0.01f;
            source.bypassListenerEffects = Plugin.ReverbMix.Value <= 0.01f;

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
            float gain = Plugin.OutputVolume.Value;

            for (int i = 0; i < samples.Length; i++)
            {
                pa.CircularBuffer[pa.WritePosition] = samples[i] * gain;
                pa.WritePosition = (pa.WritePosition + 1) % bufLen;
            }

            // Thread-safe increment — OnAudioRead decrements from the audio thread
            Interlocked.Add(ref pa.SamplesBuffered, samples.Length);

            // Cap to buffer size (not critical to be atomic here, just a ceiling)
            if (pa.SamplesBuffered > bufLen)
                pa.SamplesBuffered = bufLen;
        }

        /// <summary>
        /// Called from Unity's audio thread — must be thread-safe.
        /// </summary>
        private void OnAudioRead(PlayerAudio pa, float[] data)
        {
            int bufLen = pa.CircularBuffer.Length;
            for (int i = 0; i < data.Length; i++)
            {
                int remaining = Interlocked.CompareExchange(ref pa.SamplesBuffered, 0, 0);
                if (remaining <= 0)
                {
                    data[i] = 0f;
                }
                else
                {
                    data[i] = pa.CircularBuffer[pa.ReadPosition];
                    pa.CircularBuffer[pa.ReadPosition] = 0f;
                    pa.ReadPosition = (pa.ReadPosition + 1) % bufLen;
                    Interlocked.Decrement(ref pa.SamplesBuffered);
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
                float timeSinceLastData = now - pa.LastActiveTime;

                if (timeSinceLastData > SpeakingTimeout)
                {
                    pa.IsSpeaking = false;
                }

                if (timeSinceLastData > BufferResetTimeout && pa.SamplesBuffered <= 0 && pa.Source.isPlaying)
                {
                    pa.Source.Stop();
                    pa.WritePosition = 0;
                    pa.ReadPosition = 0;
                }

                if (timeSinceLastData > CleanupInactiveAfter)
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
