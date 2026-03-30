using HarmonyLib;

namespace ValheimProxChat
{
    /// <summary>
    /// Harmony patches to hook into Valheim's game lifecycle.
    /// These use string-based method names because Start/Awake/OnDestroy are
    /// inherited MonoBehaviour methods not directly visible via nameof().
    /// </summary>
    public static class Patches
    {
        /// <summary>
        /// Log when a game session starts (world loaded).
        /// </summary>
        [HarmonyPatch(typeof(Game), "Start")]
        public static class GameStartPatch
        {
            public static void Postfix()
            {
                Plugin.Log.LogInfo("Game session started - voice chat ready.");
            }
        }

        /// <summary>
        /// Clean up when the game session ends.
        /// </summary>
        [HarmonyPatch(typeof(Game), "OnDestroy")]
        public static class GameDestroyPatch
        {
            public static void Prefix()
            {
                Plugin.Log.LogInfo("Game session ending - cleaning up voice chat.");
            }
        }

        /// <summary>
        /// Notify when ZNet connects (multiplayer session established).
        /// </summary>
        [HarmonyPatch(typeof(ZNet), "Awake")]
        public static class ZNetAwakePatch
        {
            public static void Postfix(ZNet __instance)
            {
                Plugin.Log.LogInfo($"ZNet initialized. Server: {__instance.IsServer()}");
            }
        }
    }
}
