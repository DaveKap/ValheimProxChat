using System.Net;

namespace ValheimVoiceServer
{
    /// <summary>
    /// Tracks a connected voice chat client.
    /// </summary>
    public class ConnectedClient
    {
        public long PlayerId { get; set; }
        public string PlayerName { get; set; }
        public IPEndPoint Endpoint { get; set; }
        public float PosX { get; set; }
        public float PosY { get; set; }
        public float PosZ { get; set; }
        public double LastSeenTime { get; set; }
        public bool IsSpeaking { get; set; }

        public float DistanceSquaredTo(ConnectedClient other)
        {
            float dx = PosX - other.PosX;
            float dy = PosY - other.PosY;
            float dz = PosZ - other.PosZ;
            return dx * dx + dy * dy + dz * dz;
        }

        public float DistanceTo(ConnectedClient other)
        {
            return (float)System.Math.Sqrt(DistanceSquaredTo(other));
        }
    }
}
