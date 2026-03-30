using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using ValheimVoiceClient.Audio;

namespace ValheimVoiceClient.Network
{
    /// <summary>
    /// UDP transport for voice chat. Connects to the standalone ValheimVoiceServer
    /// instead of using Valheim's ZRoutedRpc. This means:
    ///   - Unreliable UDP: lost packets are dropped, not retransmitted (good for voice)
    ///   - No bandwidth cap from Valheim's networking
    ///   - Server does proximity filtering so we only receive nearby audio
    ///   - Zero impact on game traffic
    /// </summary>
    public class UdpVoiceTransport : MonoBehaviour
    {
        private UdpClient _udp;
        private IPEndPoint _serverEndpoint;
        private MicrophoneCapture _micCapture;
        private AudioPlaybackManager _playbackManager;
        private Thread _receiveThread;
        private volatile bool _running;
        private float _lastPositionUpdate;
        private bool _connected;

        // Receive queue: audio thread can't call Unity APIs, so we queue packets
        private readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> _incomingPackets =
            new System.Collections.Concurrent.ConcurrentQueue<byte[]>();

        public void Initialize(MicrophoneCapture micCapture, AudioPlaybackManager playbackManager)
        {
            _micCapture = micCapture;
            _playbackManager = playbackManager;

            _micCapture.OnAudioChunkReady += OnLocalAudioChunk;
        }

        private void Update()
        {
            // Connect when we join a world
            if (!_connected && Player.m_localPlayer != null && Game.instance != null)
            {
                Connect();
            }

            // Disconnect when we leave
            if (_connected && (Player.m_localPlayer == null || Game.instance == null))
            {
                Disconnect();
            }

            if (!_connected) return;

            // Send position updates when not speaking (so the server knows where we are)
            float now = Time.unscaledTime;
            if (now - _lastPositionUpdate >= Plugin.PositionUpdateInterval.Value)
            {
                SendPositionUpdate();
                _lastPositionUpdate = now;
            }

            // Process incoming voice packets on the main thread
            while (_incomingPackets.TryDequeue(out byte[] packet))
            {
                ProcessIncomingVoice(packet);
            }
        }

        private void Connect()
        {
            try
            {
                string address = Plugin.VoiceServerAddress.Value;

                // Auto-detect: use the Valheim server's address if not configured
                if (string.IsNullOrEmpty(address) && ZNet.instance != null)
                {
                    // Try to get the server's address from the current connection
                    var serverPeer = ZNet.instance.GetServerPeer();
                    if (serverPeer != null && serverPeer.m_socket != null)
                    {
                        string hostName = serverPeer.m_socket.GetHostName();
                        if (!string.IsNullOrEmpty(hostName))
                        {
                            // hostName might be "IP:port" format
                            int colonIdx = hostName.LastIndexOf(':');
                            address = colonIdx > 0 ? hostName.Substring(0, colonIdx) : hostName;
                        }
                    }
                }

                if (string.IsNullOrEmpty(address))
                {
                    address = "127.0.0.1";
                    Plugin.Log.LogWarning("Could not detect voice server address, defaulting to localhost.");
                }

                int port = Plugin.VoiceServerPort.Value;

                _serverEndpoint = new IPEndPoint(IPAddress.Parse(address), port);
                _udp = new UdpClient();
                _udp.Connect(_serverEndpoint);

                _running = true;
                _receiveThread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "VoiceReceive"
                };
                _receiveThread.Start();

                _connected = true;
                Plugin.Log.LogInfo($"Connected to voice server at {address}:{port}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Failed to connect to voice server: {e.Message}");
                _connected = false;
            }
        }

        private void Disconnect()
        {
            if (!_connected) return;

            _running = false;
            _connected = false;

            // Send disconnect packet
            try
            {
                if (_udp != null && Player.m_localPlayer != null)
                {
                    byte[] packet = new byte[9];
                    packet[0] = 0x03; // Disconnect
                    Buffer.BlockCopy(BitConverter.GetBytes(Player.m_localPlayer.GetPlayerID()), 0, packet, 1, 8);
                    _udp.Send(packet, packet.Length);
                }
            }
            catch { }

            _udp?.Close();
            _udp = null;
            _receiveThread = null;

            Plugin.Log.LogInfo("Disconnected from voice server.");
        }

        private void OnDestroy()
        {
            if (_micCapture != null)
                _micCapture.OnAudioChunkReady -= OnLocalAudioChunk;
            Disconnect();
        }

        /// <summary>
        /// Called when the microphone has a compressed audio chunk ready.
        /// Sends it to the voice server via UDP.
        /// </summary>
        private void OnLocalAudioChunk(byte[] compressedData, int sampleRate)
        {
            if (!_connected || _udp == null) return;
            if (Player.m_localPlayer == null) return;

            long playerId = Player.m_localPlayer.GetPlayerID();
            string playerName = Player.m_localPlayer.GetPlayerName();
            Vector3 pos = Player.m_localPlayer.transform.position;

            byte[] nameBytes = Encoding.UTF8.GetBytes(playerName);

            // Packet: type(1) + playerId(8) + nameLen(4) + name(N) + sampleRate(4)
            //         + audioLen(4) + audio(N) + posX(4) + posY(4) + posZ(4)
            int totalLen = 1 + 8 + 4 + nameBytes.Length + 4 + 4 + compressedData.Length + 12;
            byte[] packet = new byte[totalLen];
            int offset = 0;

            packet[offset++] = 0x01; // Voice
            Buffer.BlockCopy(BitConverter.GetBytes(playerId), 0, packet, offset, 8); offset += 8;
            Buffer.BlockCopy(BitConverter.GetBytes(nameBytes.Length), 0, packet, offset, 4); offset += 4;
            Buffer.BlockCopy(nameBytes, 0, packet, offset, nameBytes.Length); offset += nameBytes.Length;
            Buffer.BlockCopy(BitConverter.GetBytes(sampleRate), 0, packet, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(compressedData.Length), 0, packet, offset, 4); offset += 4;
            Buffer.BlockCopy(compressedData, 0, packet, offset, compressedData.Length); offset += compressedData.Length;
            Buffer.BlockCopy(BitConverter.GetBytes(pos.x), 0, packet, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(pos.y), 0, packet, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(pos.z), 0, packet, offset, 4); offset += 4;

            try
            {
                _udp.Send(packet, packet.Length);
            }
            catch (SocketException)
            {
                // UDP send failed — server might be down. Don't spam logs.
            }
        }

        /// <summary>
        /// Send a position-only update so the server knows where we are even when silent.
        /// </summary>
        private void SendPositionUpdate()
        {
            if (!_connected || _udp == null) return;
            if (Player.m_localPlayer == null) return;

            Vector3 pos = Player.m_localPlayer.transform.position;
            long playerId = Player.m_localPlayer.GetPlayerID();

            // Packet: type(1) + playerId(8) + posX(4) + posY(4) + posZ(4)
            byte[] packet = new byte[21];
            int offset = 0;

            packet[offset++] = 0x02; // Position update
            Buffer.BlockCopy(BitConverter.GetBytes(playerId), 0, packet, offset, 8); offset += 8;
            Buffer.BlockCopy(BitConverter.GetBytes(pos.x), 0, packet, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(pos.y), 0, packet, offset, 4); offset += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(pos.z), 0, packet, offset, 4); offset += 4;

            try
            {
                _udp.Send(packet, packet.Length);
            }
            catch (SocketException) { }
        }

        /// <summary>
        /// Background thread: receives UDP packets from the voice server.
        /// </summary>
        private void ReceiveLoop()
        {
            IPEndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

            while (_running)
            {
                try
                {
                    byte[] data = _udp.Receive(ref remoteEp);
                    if (data != null && data.Length > 0)
                    {
                        _incomingPackets.Enqueue(data);
                    }
                }
                catch (SocketException) when (!_running)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch
                {
                    // Ignore transient errors
                }
            }
        }

        /// <summary>
        /// Process a voice packet received from the voice server (on the main thread).
        /// Server already filtered by proximity and included a pre-calculated volume.
        /// </summary>
        private void ProcessIncomingVoice(byte[] data)
        {
            try
            {
                if (data.Length < 9 || data[0] != 0x01) return;

                int offset = 1;
                long senderId = BitConverter.ToInt64(data, offset); offset += 8;

                int nameLen = BitConverter.ToInt32(data, offset); offset += 4;
                if (nameLen < 0 || nameLen > 256 || offset + nameLen > data.Length) return;
                string senderName = Encoding.UTF8.GetString(data, offset, nameLen); offset += nameLen;

                int sampleRate = BitConverter.ToInt32(data, offset); offset += 4;

                int audioLen = BitConverter.ToInt32(data, offset); offset += 4;
                if (audioLen < 0 || offset + audioLen > data.Length) return;
                byte[] audioData = new byte[audioLen];
                Buffer.BlockCopy(data, offset, audioData, 0, audioLen); offset += audioLen;

                if (offset + 16 > data.Length) return;
                float posX = BitConverter.ToSingle(data, offset); offset += 4;
                float posY = BitConverter.ToSingle(data, offset); offset += 4;
                float posZ = BitConverter.ToSingle(data, offset); offset += 4;
                float volume = BitConverter.ToSingle(data, offset); offset += 4;

                Vector3 senderPos = new Vector3(posX, posY, posZ);

                // Decompress and play
                float[] samples = AudioCompression.Decompress(audioData, 0, audioData.Length);

                _playbackManager.PlayVoiceChunk(senderId, senderName, senderPos, samples, sampleRate, volume);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Error processing voice packet: {e.Message}");
            }
        }
    }
}
