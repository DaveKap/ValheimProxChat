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

            // Enable live config reloading — changes to the .cfg file apply immediately
            Config.SaveOnConfigSet = true;
            SetupFileWatcher();

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

        private System.IO.FileSystemWatcher _configWatcher;

        private void SetupFileWatcher()
        {
            var configFile = Config.ConfigFilePath;
            var configDir = System.IO.Path.GetDirectoryName(configFile);
            var configFileName = System.IO.Path.GetFileName(configFile);

            _configWatcher = new System.IO.FileSystemWatcher(configDir, configFileName);
            _configWatcher.Changed += (sender, args) =>
            {
                // Reload on the next frame since file events come from a background thread
                _pendingConfigReload = true;
            };
            _configWatcher.EnableRaisingEvents = true;
            _configWatcher.NotifyFilter = System.IO.NotifyFilters.LastWrite;

            Log.LogInfo("Config file watcher active — edit the .cfg file and changes apply live.");
        }

        private volatile bool _pendingConfigReload;

        private void Update()
        {
            if (_pendingConfigReload)
            {
                _pendingConfigReload = false;
                Config.Reload();
                Log.LogInfo("Configuration reloaded from file.");
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();

            if (_configWatcher != null)
            {
                _configWatcher.EnableRaisingEvents = false;
                _configWatcher.Dispose();
                _configWatcher = null;
            }
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
