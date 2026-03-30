using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ValheimVoiceServer
{
    /// <summary>
    /// Core UDP voice relay server. Receives voice packets from clients,
    /// filters by proximity, and relays to nearby players.
    ///
    /// Packet format (client -> server):
    ///   [1 byte]  packet type (0x01 = voice, 0x02 = position update, 0x03 = disconnect)
    ///   [8 bytes] player ID (long)
    ///
    ///   Voice (0x01):
    ///     [4 bytes] player name length + [N bytes] player name (UTF-8)
    ///     [4 bytes] sample rate (int)
    ///     [4 bytes] audio data length + [N bytes] compressed audio
    ///     [4 bytes] posX (float)
    ///     [4 bytes] posY (float)
    ///     [4 bytes] posZ (float)
    ///
    ///   Position update (0x02):
    ///     [4 bytes] posX (float)
    ///     [4 bytes] posY (float)
    ///     [4 bytes] posZ (float)
    ///
    /// Packet format (server -> client):
    ///   [1 byte]  packet type (0x01 = voice)
    ///   [8 bytes] sender player ID
    ///   [4 bytes] player name length + [N bytes] player name (UTF-8)
    ///   [4 bytes] sample rate
    ///   [4 bytes] audio data length + [N bytes] compressed audio
    ///   [4 bytes] posX, posY, posZ (sender position for client-side volume calc)
    ///   [4 bytes] volume (float, pre-calculated proximity volume 0-1)
    /// </summary>
    public class VoiceRelay
    {
        private readonly ServerConfig _config;
        private readonly ConcurrentDictionary<long, ConnectedClient> _clients = new ConcurrentDictionary<long, ConnectedClient>();
        private UdpClient _udp;
        private readonly Stopwatch _uptime = new Stopwatch();

        // Stats
        private long _packetsReceived;
        private long _packetsRelayed;
        private long _packetsDropped;

        public VoiceRelay(ServerConfig config)
        {
            _config = config;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            _udp = new UdpClient(_config.Port);
            _uptime.Start();

            Console.WriteLine($"Listening on UDP port {_config.Port}...");

            // Background task to clean up stale clients and print stats
            _ = Task.Run(() => MaintenanceLoopAsync(ct), ct);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _udp.ReceiveAsync();
                    Interlocked.Increment(ref _packetsReceived);
                    ProcessPacket(result.Buffer, result.RemoteEndPoint);
                }
                catch (SocketException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"Receive error: {e.Message}");
                }
            }

            _udp?.Close();
        }

        private void ProcessPacket(byte[] data, IPEndPoint sender)
        {
            if (data.Length < 9) return; // Minimum: type(1) + playerId(8)

            int offset = 0;
            byte packetType = data[offset++];
            long playerId = BitConverter.ToInt64(data, offset); offset += 8;

            double now = _uptime.Elapsed.TotalSeconds;

            switch (packetType)
            {
                case 0x01: // Voice data
                    HandleVoicePacket(data, offset, playerId, sender, now);
                    break;

                case 0x02: // Position update
                    HandlePositionUpdate(data, offset, playerId, sender, now);
                    break;

                case 0x03: // Disconnect
                    _clients.TryRemove(playerId, out _);
                    Console.WriteLine($"[{now:F1}s] Client disconnected: {playerId}");
                    break;
            }
        }

        private void HandleVoicePacket(byte[] data, int offset, long playerId, IPEndPoint sender, double now)
        {
            // Parse: name(string), sampleRate(int), audioData(byte[]), posX/Y/Z(float)
            if (offset + 4 > data.Length) return;

            int nameLen = BitConverter.ToInt32(data, offset); offset += 4;
            if (nameLen < 0 || nameLen > 256 || offset + nameLen > data.Length) return;
            string playerName = System.Text.Encoding.UTF8.GetString(data, offset, nameLen); offset += nameLen;

            if (offset + 4 > data.Length) return;
            int sampleRate = BitConverter.ToInt32(data, offset); offset += 4;

            if (offset + 4 > data.Length) return;
            int audioLen = BitConverter.ToInt32(data, offset); offset += 4;
            if (audioLen < 0 || audioLen > 65536 || offset + audioLen > data.Length) return;
            int audioOffset = offset; offset += audioLen;

            if (offset + 12 > data.Length) return;
            float posX = BitConverter.ToSingle(data, offset); offset += 4;
            float posY = BitConverter.ToSingle(data, offset); offset += 4;
            float posZ = BitConverter.ToSingle(data, offset); offset += 4;

            // Update or register client
            var client = _clients.GetOrAdd(playerId, _ => new ConnectedClient { PlayerId = playerId });
            client.PlayerName = playerName;
            client.Endpoint = sender;
            client.PosX = posX;
            client.PosY = posY;
            client.PosZ = posZ;
            client.LastSeenTime = now;
            client.IsSpeaking = true;

            // Relay to nearby clients (server-side proximity filtering)
            float maxDistSq = _config.MaxVoiceDistance * _config.MaxVoiceDistance;

            foreach (var kvp in _clients)
            {
                ConnectedClient target = kvp.Value;
                if (target.PlayerId == playerId) continue; // Don't echo back
                if (target.Endpoint == null) continue;

                float distSq = client.DistanceSquaredTo(target);
                if (distSq > maxDistSq) continue;

                float distance = (float)Math.Sqrt(distSq);
                float volume = CalculateProximityVolume(distance);

                // Build relay packet
                byte[] relayPacket = BuildRelayPacket(playerId, playerName, sampleRate,
                    data, audioOffset, audioLen, posX, posY, posZ, volume);

                try
                {
                    _udp.Send(relayPacket, relayPacket.Length, target.Endpoint);
                    Interlocked.Increment(ref _packetsRelayed);
                }
                catch (SocketException)
                {
                    Interlocked.Increment(ref _packetsDropped);
                }
            }
        }

        private void HandlePositionUpdate(byte[] data, int offset, long playerId, IPEndPoint sender, double now)
        {
            if (offset + 12 > data.Length) return;

            float posX = BitConverter.ToSingle(data, offset); offset += 4;
            float posY = BitConverter.ToSingle(data, offset); offset += 4;
            float posZ = BitConverter.ToSingle(data, offset); offset += 4;

            var client = _clients.GetOrAdd(playerId, _ => new ConnectedClient { PlayerId = playerId });
            client.Endpoint = sender;
            client.PosX = posX;
            client.PosY = posY;
            client.PosZ = posZ;
            client.LastSeenTime = now;
            client.IsSpeaking = false;
        }

        private byte[] BuildRelayPacket(long senderId, string senderName, int sampleRate,
            byte[] audioSource, int audioOffset, int audioLen,
            float posX, float posY, float posZ, float volume)
        {
            byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(senderName);

            // type(1) + senderId(8) + nameLen(4) + name(N) + sampleRate(4)
            // + audioLen(4) + audio(N) + posX(4) + posY(4) + posZ(4) + volume(4)
            int totalLen = 1 + 8 + 4 + nameBytes.Length + 4 + 4 + audioLen + 4 + 4 + 4 + 4;
            byte[] packet = new byte[totalLen];
            int offset = 0;

            packet[offset++] = 0x01; // Voice packet
            Buffer.BlockCopy(BitConverter.GetBytes(senderId), 0, packet, offset, 8); offset += 8;
            Buffer.BlockCopy(BitConverter.GetBytes(nameBytes.Length), 0, packet, offset, 4); offset += 4;
            Buffer.BlockCopy(nameBytes, 0, packet, offset, nameBytes.Length); offset += nameBytes.Length;
            Buffer.BlockCopy(BitConverter.GetBytes(sampleRate), 0, packet, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(audioLen), 0, packet, offset, 4); offset += 4;
            Buffer.BlockCopy(audioSource, audioOffset, packet, offset, audioLen); offset += audioLen;
            Buffer.BlockCopy(BitConverter.GetBytes(posX), 0, packet, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(posY), 0, packet, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(posZ), 0, packet, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(volume), 0, packet, offset, 4); offset += 4;

            return packet;
        }

        private float CalculateProximityVolume(float distance)
        {
            if (distance <= _config.FadeStartDistance) return 1f;
            if (distance >= _config.MaxVoiceDistance) return 0f;
            return 1f - (distance - _config.FadeStartDistance) / (_config.MaxVoiceDistance - _config.FadeStartDistance);
        }

        private async Task MaintenanceLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(5000, ct);

                double now = _uptime.Elapsed.TotalSeconds;
                int removed = 0;

                foreach (var kvp in _clients)
                {
                    if (now - kvp.Value.LastSeenTime > _config.ClientTimeoutSeconds)
                    {
                        if (_clients.TryRemove(kvp.Key, out _))
                            removed++;
                    }
                }

                if (removed > 0)
                    Console.WriteLine($"[{now:F1}s] Cleaned up {removed} timed-out client(s).");

                long recv = Interlocked.Read(ref _packetsReceived);
                long relay = Interlocked.Read(ref _packetsRelayed);
                long drop = Interlocked.Read(ref _packetsDropped);
                int clients = _clients.Count;

                Console.WriteLine($"[{now:F1}s] Clients: {clients} | Recv: {recv} | Relayed: {relay} | Dropped: {drop}");
            }
        }
    }
}
