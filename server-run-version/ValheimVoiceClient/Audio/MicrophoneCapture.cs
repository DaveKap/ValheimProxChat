using System;
using UnityEngine;

namespace ValheimVoiceClient.Audio
{
    /// <summary>
    /// Captures audio from the system microphone using Unity's Microphone API.
    /// Provides compressed audio chunks ready for network transmission.
    /// </summary>
    public class MicrophoneCapture : MonoBehaviour
    {
        private AudioClip _micClip;
        private string _micDevice;
        private int _sampleRate;
        private int _lastReadPosition;
        private bool _isRecording;

        // Buffer for reading mic samples
        private float[] _readBuffer;

        // Public state
        public bool IsCapturing => _isRecording;
        public bool IsSpeaking { get; private set; }
        public float CurrentLevel { get; private set; }

        /// <summary>
        /// Called when a compressed audio chunk is ready to be sent.
        /// Parameters: compressed data, sample rate.
        /// </summary>
        public event Action<byte[], int> OnAudioChunkReady;

        private float _transmitTimer;

        private void OnEnable()
        {
            StartCapture();
        }

        private void OnDisable()
        {
            StopCapture();
        }

        public void StartCapture()
        {
            if (_isRecording) return;

            _sampleRate = Plugin.SampleRate.Value;
            _micDevice = string.IsNullOrEmpty(Plugin.MicrophoneDevice.Value)
                ? null
                : Plugin.MicrophoneDevice.Value;

            // Check if any microphone is available
            if (Microphone.devices.Length == 0)
            {
                Plugin.Log.LogWarning("No microphone detected. Voice chat will be listen-only.");
                return;
            }

            // Log available devices
            Plugin.Log.LogInfo($"Available microphones: {string.Join(", ", Microphone.devices)}");
            Plugin.Log.LogInfo($"Using microphone: {_micDevice ?? Microphone.devices[0]} at {_sampleRate}Hz");

            // Start recording - 10 second loop buffer
            _micClip = Microphone.Start(_micDevice, true, 10, _sampleRate);
            if (_micClip == null)
            {
                Plugin.Log.LogError("Failed to start microphone recording.");
                return;
            }

            _lastReadPosition = 0;
            _isRecording = true;
            _readBuffer = new float[_sampleRate]; // 1 second buffer
            _transmitTimer = 0f;
        }

        public void StopCapture()
        {
            if (!_isRecording) return;

            Microphone.End(_micDevice);
            _isRecording = false;
            IsSpeaking = false;
            CurrentLevel = 0f;

            if (_micClip != null)
            {
                Destroy(_micClip);
                _micClip = null;
            }
        }

        private void Update()
        {
            if (!_isRecording || _micClip == null) return;

            // Always update mic level for UI, even when voice chat is paused (menu open)
            bool canTransmit = Plugin.IsVoiceChatActive();

            _transmitTimer += Time.unscaledDeltaTime;
            if (_transmitTimer < Plugin.TransmitInterval.Value) return;
            _transmitTimer = 0f;

            // Read available samples from the microphone
            int currentPosition = Microphone.GetPosition(_micDevice);
            if (currentPosition == _lastReadPosition) return;

            int samplesToRead;
            if (currentPosition > _lastReadPosition)
            {
                samplesToRead = currentPosition - _lastReadPosition;
            }
            else
            {
                // Wrapped around the loop buffer
                samplesToRead = (_micClip.samples - _lastReadPosition) + currentPosition;
            }

            if (samplesToRead <= 0) return;

            // Limit chunk size to prevent huge packets
            int maxSamples = (int)(_sampleRate * 0.5f); // Max 500ms per chunk
            if (samplesToRead > maxSamples)
            {
                // Skip ahead if we've fallen behind
                _lastReadPosition = currentPosition - maxSamples;
                if (_lastReadPosition < 0) _lastReadPosition += _micClip.samples;
                samplesToRead = maxSamples;
            }

            // Ensure buffer is large enough
            if (_readBuffer == null || _readBuffer.Length < samplesToRead)
            {
                _readBuffer = new float[samplesToRead];
            }

            _micClip.GetData(_readBuffer, _lastReadPosition);
            _lastReadPosition = currentPosition;

            // Apply microphone boost
            float boost = Plugin.MicrophoneBoost.Value;
            if (Math.Abs(boost - 1f) > 0.001f)
            {
                for (int i = 0; i < samplesToRead; i++)
                {
                    _readBuffer[i] *= boost;
                }
            }

            // Calculate audio level (after boost, for accurate UI display)
            float level = CalculateRmsLevel(_readBuffer, samplesToRead);
            CurrentLevel = level;

            // Don't transmit when menus are open, but keep updating the level above
            if (!canTransmit)
            {
                IsSpeaking = false;
                return;
            }

            // Determine if we should transmit
            bool shouldTransmit = false;

            if (Plugin.PushToTalk.Value)
            {
                KeyCode pttKey = ParseKeyCode(Plugin.PushToTalkKey.Value);
                shouldTransmit = Input.GetKey(pttKey);
            }
            else if (Plugin.VoiceActivation.Value)
            {
                shouldTransmit = level > Plugin.VoiceActivationThreshold.Value;
            }
            else
            {
                // Always on
                shouldTransmit = true;
            }

            IsSpeaking = shouldTransmit;

            if (!shouldTransmit) return;

            // Compress and send
            byte[] compressed = AudioCompression.Compress(_readBuffer, 0, samplesToRead);
            OnAudioChunkReady?.Invoke(compressed, _sampleRate);
        }

        private static float CalculateRmsLevel(float[] samples, int count)
        {
            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                sum += samples[i] * samples[i];
            }
            return Mathf.Sqrt(sum / count);
        }

        private static KeyCode ParseKeyCode(string keyName)
        {
            if (string.IsNullOrEmpty(keyName)) return KeyCode.V;

            try
            {
                // Try direct parse first
                if (System.Enum.TryParse(keyName, true, out KeyCode result))
                    return result;

                // Handle single character keys
                if (keyName.Length == 1)
                {
                    char c = char.ToUpper(keyName[0]);
                    if (System.Enum.TryParse(c.ToString(), out KeyCode charResult))
                        return charResult;
                }
            }
            catch
            {
                // Fallback
            }

            return KeyCode.V;
        }
    }
}
