using UnityEngine;

namespace ValheimProxChat
{
    /// <summary>
    /// Tracks player positions and provides utility methods for proximity calculations.
    /// Also handles Harmony patches for hooking into Valheim's player lifecycle.
    /// </summary>
    public class PlayerTracker : MonoBehaviour
    {
        public static PlayerTracker Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
        }

        /// <summary>
        /// Find the Player component closest to a given world position, excluding the local player.
        /// Returns null if no player is within range.
        /// </summary>
        public Player FindClosestPlayer(Vector3 position, float maxRange)
        {
            Player closest = null;
            float closestDist = maxRange;

            foreach (Player p in Player.GetAllPlayers())
            {
                if (p == Player.m_localPlayer) continue;

                float dist = Vector3.Distance(p.transform.position, position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = p;
                }
            }

            return closest;
        }

        /// <summary>
        /// Try to find a player by their character name.
        /// </summary>
        public Player FindPlayerByName(string name)
        {
            foreach (Player p in Player.GetAllPlayers())
            {
                if (p.GetPlayerName() == name)
                    return p;
            }
            return null;
        }
    }
}
