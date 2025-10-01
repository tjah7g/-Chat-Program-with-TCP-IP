using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ChatClient.Net;

namespace ChatServer
{
    class Client
    {
        public string Username { get; set; }
        public Guid UID { get; set; }
        public TcpClient ClientSocket { get; set; }

        private NetworkStream _stream;
        private bool _isRunning = true;

        public Client(TcpClient client)
        {
            ClientSocket = client;
            UID = Guid.NewGuid();
            _stream = ClientSocket.GetStream();

            Task.Run(() => Process());
        }

        async Task Process()
        {
            try
            {
                // Read initial join message
                var joinProtocol = await ReadProtocolAsync();
                if (joinProtocol != null && joinProtocol.Type == "join")
                {
                    Username = joinProtocol.From;
                    Program.Log($"Client connected: {Username} [{UID}]");
                }

                while (_isRunning && ClientSocket.Connected)
                {
                    try
                    {
                        var protocol = await ReadProtocolAsync();
                        if (protocol == null)
                            break;

                        HandleProtocol(protocol);
                    }
                    catch (Exception ex)
                    {
                        Program.Log($"Error reading from {Username}: {ex.Message}", true);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"Error in client process for {Username}: {ex.Message}", true);
            }
            finally
            {
                Program.HandleConnectionLost(this);
            }
        }

        private void HandleProtocol(Protocol protocol)
        {
            try
            {
                switch (protocol.Type)
                {
                    case "msg":
                        Program.Log($"[{Username}]: {protocol.Text}");
                        var msgProtocol = Protocol.CreateMessage(Username, protocol.Text, UID.ToString());
                        Program.BroadcastMessage(msgProtocol);
                        break;

                    case "pm":
                        Program.Log($"[PM] {Username} -> {protocol.To}: {protocol.Text}");
                        var pmProtocol = Protocol.CreatePrivateMessage(Username, protocol.To, protocol.Text, UID.ToString());
                        Program.SendPrivateMessage(pmProtocol);
                        break;

                    case "leave":
                        Program.Log($"{Username} is leaving");
                        Program.HandleDisconnect(UID.ToString());
                        _isRunning = false;
                        break;

                    case "typing":
                        var typingProtocol = Protocol.CreateTyping(Username, UID.ToString());
                        Program.BroadcastTyping(typingProtocol);
                        break;

                    case "stoptyping":
                        var stopTypingProtocol = Protocol.CreateStopTyping(Username, UID.ToString());
                        Program.BroadcastTyping(stopTypingProtocol);
                        break;

                    default:
                        Program.Log($"Unknown protocol type from {Username}: {protocol.Type}", true);
                        break;
                }
            }
            catch (Exception ex)
            {
                Program.Log($"Error handling protocol from {Username}: {ex.Message}", true);
            }
        }

        private async Task<Protocol> ReadProtocolAsync()
        {
            try
            {
                // Read length prefix (4 bytes)
                var lengthBytes = new byte[4];
                var bytesRead = await _stream.ReadAsync(lengthBytes, 0, 4);

                if (bytesRead == 0)
                    return null;

                var length = BitConverter.ToInt32(lengthBytes, 0);

                if (length <= 0 || length > 1048576) // Max 1MB
                {
                    Program.Log($"Invalid message length from {Username}: {length}", true);
                    return null;
                }

                // Read JSON data
                var jsonBytes = new byte[length];
                var totalRead = 0;

                while (totalRead < length)
                {
                    bytesRead = await _stream.ReadAsync(jsonBytes, totalRead, length - totalRead);
                    if (bytesRead == 0)
                        break;
                    totalRead += bytesRead;
                }

                var json = Encoding.UTF8.GetString(jsonBytes);
                return Protocol.FromJson(json);
            }
            catch (Exception ex)
            {
                Program.Log($"Error reading protocol from {Username}: {ex.Message}", true);
                return null;
            }
        }

        public void SendProtocol(Protocol protocol)
        {
            try
            {
                if (ClientSocket?.Connected == true)
                {
                    var bytes = protocol.ToBytes();
                    _stream.Write(bytes, 0, bytes.Length);
                    _stream.Flush();
                }
            }
            catch (Exception ex)
            {
                Program.Log($"Error sending to {Username}: {ex.Message}", true);
            }
        }
    }
}