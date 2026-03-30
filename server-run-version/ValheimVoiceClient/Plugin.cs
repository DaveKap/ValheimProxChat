using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using ValheimVoiceClient.Audio;
using ValheimVoiceClient.Network;

namespace ValheimVoiceClient
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("valheim.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.valheimvoiceclient.mod";
        public const string PluginName = "ValheimVoiceClient";
        public const string PluginVersion = "1.0.0";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        // Config
        public static ConfigEntry<string> VoiceServerAddress;
        public static ConfigEntry<int> VoiceServerPort;
        public static ConfigEntry<float> MicrophoneBoost;
        public static ConfigEntry<float> OutputVolume;
        public static ConfigEntry<int> SampleRate;
        public static ConfigEntry<string> MicrophoneDevice;
        public static ConfigEntry<float> ReverbMix;
        public static ConfigEntry<bool> PushToTalk;
        public static ConfigEntry<string> PushToTalkKey;
        public static ConfigEntry<bool> VoiceActivation;
        public static ConfigEntry<float> VoiceActivationThreshold;
        public static ConfigEntry<float> TransmitInterval;
        public static ConfigEntry<float> PositionUpdateInterval;
        public static ConfigEntry<bool> ShowSpeakingIndicator;

        private MicrophoneCapture _micCapture;
        private UdpVoiceTransport _transport;
        private AudioPlaybackManager _playbackManager;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            BindConfig();
            Log.LogInfo($"{PluginName} v{PluginVersion} loaded!");
        }

        private void Start()
        {
            _playbackManager = gameObject.AddComponent<AudioPlaybackManager>();
            _micCapture = gameObject.AddComponent<MicrophoneCapture>();
            _transport = gameObject.AddComponent<UdpVoiceTransport>();
            _transport.Initialize(_micCapture, _playbackManager);
        }

        private void BindConfig()
        {
            VoiceServerAddress = Config.Bind(
                "Server", "VoiceServerAddress", "",
                "IP address or hostname of the voice server. Leave empty to auto-detect from Valheim's server connection.");

            VoiceServerPort = Config.Bind(
                "Server", "VoiceServerPort", 9876,
                "UDP port of the voice server.");

            MicrophoneBoost = Config.Bind(
                "Audio", "MicrophoneBoost", 1.5f,
                "Microphone input volume multiplier.");

            OutputVolume = Config.Bind(
                "Audio", "OutputVolume", 2.0f,
                "Playback volume gain applied to received audio samples.");

            SampleRate = Config.Bind(
                "Audio", "SampleRate", 22050,
                "Audio sample rate in Hz. With a dedicated voice server, bandwidth is not constrained by Valheim's limits.");

            MicrophoneDevice = Config.Bind(
                "Audio", "MicrophoneDevice", "",
                "Microphone device name. Empty = default.");

            ReverbMix = Config.Bind(
                "Audio", "ReverbMix", 0.0f,
                "Valheim reverb zone mix on voice audio (0.0 = no reverb).");

            PushToTalk = Config.Bind(
                "Input", "PushToTalk", true,
                "Require holding a key to transmit.");

            PushToTalkKey = Config.Bind(
                "Input", "PushToTalkKey", "v",
                "Push-to-talk key (Unity KeyCode name).");

            VoiceActivation = Config.Bind(
                "Input", "VoiceActivation", false,
                "Auto-transmit when mic level exceeds threshold.");

            VoiceActivationThreshold = Config.Bind(
                "Input", "VoiceActivationThreshold", 0.01f,
                "Voice activation minimum level.");

            TransmitInterval = Config.Bind(
                "Network", "TransmitInterval", 0.02f,
                "Seconds between voice packets (20ms default).");

            PositionUpdateInterval = Config.Bind(
                "Network", "PositionUpdateInterval", 0.25f,
                "Seconds between position-only updates when not speaking.");

            ShowSpeakingIndicator = Config.Bind(
                "UI", "ShowSpeakingIndicator", true,
                "Show icon above speaking players.");
        }

        public static bool IsVoiceChatActive()
        {
            if (Player.m_localPlayer == null) return false;
            if (Game.instance == null) return false;
            if (Console.IsVisible()) return false;
            if (Menu.IsVisible()) return false;
            if (TextInput.IsVisible()) return false;
            if (Minimap.IsOpen()) return false;
            if (InventoryGui.IsVisible()) return false;
            return true;
        }
    }
}
