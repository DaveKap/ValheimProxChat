using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using ValheimProxChat.Audio;
using ValheimProxChat.Network;

namespace ValheimProxChat
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("valheim.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.valheimproxchat.mod";
        public const string PluginName = "ValheimProxChat";
        public const string PluginVersion = "1.0.0";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        private Harmony _harmony;
        private MicrophoneCapture _micCapture;
        private VoiceNetwork _voiceNetwork;
        private AudioPlaybackManager _playbackManager;
        private PlayerTracker _playerTracker;
        private VoiceChatUI _voiceChatUI;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            Configuration.Bind(Config);

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded!");
        }

        private void Start()
        {
            _playbackManager = gameObject.AddComponent<AudioPlaybackManager>();
            _micCapture = gameObject.AddComponent<MicrophoneCapture>();
            _voiceNetwork = gameObject.AddComponent<VoiceNetwork>();
            _playerTracker = gameObject.AddComponent<PlayerTracker>();
            _voiceChatUI = gameObject.AddComponent<VoiceChatUI>();

            _voiceNetwork.Initialize(_micCapture, _playbackManager);
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        /// <summary>
        /// Returns true if the local player is currently in a state where voice chat should be active.
        /// </summary>
        public static bool IsVoiceChatActive()
        {
            // Only active when in-game with a local player
            if (Player.m_localPlayer == null) return false;
            if (Game.instance == null) return false;

            // Don't transmit when the console or a menu is open
            if (Console.IsVisible()) return false;
            if (Menu.IsVisible()) return false;
            if (TextInput.IsVisible()) return false;
            if (Minimap.IsOpen()) return false;
            if (InventoryGui.IsVisible()) return false;

            return true;
        }
    }
}
