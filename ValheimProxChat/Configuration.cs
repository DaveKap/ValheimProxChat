using BepInEx.Configuration;

namespace ValheimProxChat
{
    /// <summary>
    /// All user-configurable settings for the proximity voice chat mod.
    /// </summary>
    public static class Configuration
    {
        // Audio settings
        public static ConfigEntry<float> MaxVoiceDistance;
        public static ConfigEntry<float> FadeStartDistance;
        public static ConfigEntry<float> MicrophoneBoost;
        public static ConfigEntry<float> OutputVolume;
        public static ConfigEntry<int> SampleRate;
        public static ConfigEntry<string> MicrophoneDevice;
        public static ConfigEntry<float> ReverbMix;
        public static ConfigEntry<bool> PushToTalk;
        public static ConfigEntry<string> PushToTalkKey;
        public static ConfigEntry<bool> VoiceActivation;
        public static ConfigEntry<float> VoiceActivationThreshold;

        // Network settings
        public static ConfigEntry<float> TransmitInterval;
        public static ConfigEntry<bool> LowBandwidthMode;

        // UI settings
        public static ConfigEntry<bool> ShowSpeakingIndicator;
        public static ConfigEntry<bool> ShowVoiceRange;

        public static void Bind(ConfigFile config)
        {
            // Audio
            MaxVoiceDistance = config.Bind(
                "Audio", "MaxVoiceDistance", 25f,
                "Maximum distance (in meters) at which voice can be heard.");

            FadeStartDistance = config.Bind(
                "Audio", "FadeStartDistance", 5f,
                "Distance at which voice volume starts to fade out.");

            MicrophoneBoost = config.Bind(
                "Audio", "MicrophoneBoost", 1.5f,
                "Multiplier for microphone input volume (1.0 = normal, 1.5 = default boost).");

            OutputVolume = config.Bind(
                "Audio", "OutputVolume", 2.0f,
                "Master volume for received voice audio (0.0 to 3.0). Default 2.0 compensates for compression.");

            SampleRate = config.Bind(
                "Audio", "SampleRate", 22050,
                "Audio sample rate in Hz. Higher = better quality but more bandwidth. Recommended: 8000, 16000, or 22050.");

            MicrophoneDevice = config.Bind(
                "Audio", "MicrophoneDevice", "",
                "Microphone device name. Leave empty to use the default device.");

            ReverbMix = config.Bind(
                "Audio", "ReverbMix", 0.0f,
                "How much of Valheim's reverb zones affect voice audio (0.0 = no reverb, 1.0 = full reverb). Default is 0 for clean voice.");

            // Input
            PushToTalk = config.Bind(
                "Input", "PushToTalk", true,
                "If true, you must hold the push-to-talk key to transmit voice.");

            PushToTalkKey = config.Bind(
                "Input", "PushToTalkKey", "v",
                "Key to hold for push-to-talk. Uses Unity KeyCode names (lowercase).");

            VoiceActivation = config.Bind(
                "Input", "VoiceActivation", false,
                "If true (and PushToTalk is false), voice is transmitted when input exceeds the threshold.");

            VoiceActivationThreshold = config.Bind(
                "Input", "VoiceActivationThreshold", 0.01f,
                "Minimum audio level to trigger voice activation (0.0 to 1.0).");

            // Network
            TransmitInterval = config.Bind(
                "Network", "TransmitInterval", 0.04f,
                "How often (in seconds) voice data packets are sent. Default 0.04 (40ms) for low latency. Increase to 0.1 for slower connections.");

            LowBandwidthMode = config.Bind(
                "Network", "LowBandwidthMode", false,
                "Enable for slower or overseas connections. Reduces sample rate to 8000Hz and transmit interval to 100ms (~8 KB/s per speaker instead of ~22 KB/s).");

            // UI
            ShowSpeakingIndicator = config.Bind(
                "UI", "ShowSpeakingIndicator", true,
                "Show an icon above players who are currently speaking.");

            ShowVoiceRange = config.Bind(
                "UI", "ShowVoiceRange", false,
                "Show a debug circle indicating your voice range.");
        }

        /// <summary>
        /// Returns the effective sample rate, accounting for LowBandwidthMode.
        /// </summary>
        public static int EffectiveSampleRate
        {
            get { return LowBandwidthMode.Value ? 8000 : SampleRate.Value; }
        }

        /// <summary>
        /// Returns the effective transmit interval, accounting for LowBandwidthMode.
        /// </summary>
        public static float EffectiveTransmitInterval
        {
            get { return LowBandwidthMode.Value ? 0.1f : TransmitInterval.Value; }
        }
    }
}
