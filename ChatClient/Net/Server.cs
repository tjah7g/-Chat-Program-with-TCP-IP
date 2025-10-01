using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChatClient.Net
{
    class Server
    {
        private TcpClient _client;
        public event Action<Protocol> MessageReceived;
        public event Action<string> ConnectionStatusChanged;
        public event Action<string, string> ErrorOccurred;

        public bool IsConnected => _client?.Connected ?? false;
        public string UID { get; set; }
        public string Username { get; set; }

        private CancellationTokenSource _cancellationTokenSource;
        private Timer _typingTimer;
        private bool _isTyping = false;

        public Server()
        {
            _client = new TcpClient();
        }

        public async Task<bool> ConnectToServerAsync(string username, string ipAddress, int port)
        {
            try
            {
                if (_client == null || !_client.Connected)
                {
                    _client = new TcpClient();
                    await _client.ConnectAsync(ipAddress, port);

                    Username = username;
                    _cancellationTokenSource = new CancellationTokenSource();

                    // Start reading packets FIRST
                    _ = Task.Run(() => ReadPacketsAsync(_cancellationTokenSource.Token));

                    // Then send join protocol
                    var joinProtocol = Protocol.CreateJoin(username, "");
                    await SendProtocolAsync(joinProtocol);

                    ConnectionStatusChanged?.Invoke("Connected");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke("Connection Error", $"Failed to connect: {ex.Message}");
                ConnectionStatusChanged?.Invoke("Disconnected");
                return false;
            }
        }

        private async Task ReadPacketsAsync(CancellationToken cancellationToken)
        {
            var stream = _client.GetStream();
            var buffer = new byte[4096];

            try
            {
                while (IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    // Read length prefix (4 bytes)
                    var lengthBytes = new byte[4];
                    var bytesRead = await stream.ReadAsync(lengthBytes, 0, 4, cancellationToken);

                    if (bytesRead == 0)
                        break;

                    var length = BitConverter.ToInt32(lengthBytes, 0);

                    // Read JSON data
                    var jsonBytes = new byte[length];
                    var totalRead = 0;

                    while (totalRead < length)
                    {
                        bytesRead = await stream.ReadAsync(jsonBytes, totalRead, length - totalRead, cancellationToken);
                        if (bytesRead == 0)
                            break;
                        totalRead += bytesRead;
                    }

                    var json = Encoding.UTF8.GetString(jsonBytes);
                    var protocol = Protocol.FromJson(json);

                    // Store UID if this is our join response
                    if (protocol.Type == "join" && protocol.From == Username && string.IsNullOrEmpty(UID))
                    {
                        UID = protocol.UID;
                    }

                    MessageReceived?.Invoke(protocol);
                }
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    ErrorOccurred?.Invoke("Connection Lost", $"Connection error: {ex.Message}");
                    ConnectionStatusChanged?.Invoke("Disconnected");
                }
            }
        }

        public async Task SendMessageAsync(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message))
                    return;

                Protocol protocol;

                // Check for private message command
                if (message.StartsWith("/w "))
                {
                    var parts = message.Substring(3).Split(new[] { ' ' }, 2);
                    if (parts.Length >= 2)
                    {
                        var recipient = parts[0];
                        var text = parts[1];
                        protocol = Protocol.CreatePrivateMessage(Username, recipient, text, UID);
                    }
                    else
                    {
                        ErrorOccurred?.Invoke("Invalid Command", "Usage: /w <username> <message>");
                        return;
                    }
                }
                else
                {
                    protocol = Protocol.CreateMessage(Username, message, UID);
                }

                await SendProtocolAsync(protocol);
                StopTypingIndicator();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke("Send Error", $"Failed to send message: {ex.Message}");
            }
        }

        public async Task SendPrivateMessageAsync(string recipient, string message)
        {
            try
            {
                var protocol = Protocol.CreatePrivateMessage(Username, recipient, message, UID);
                await SendProtocolAsync(protocol);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke("Send Error", $"Failed to send private message: {ex.Message}");
            }
        }

        public void StartTypingIndicator()
        {
            if (!_isTyping)
            {
                _isTyping = true;
                var protocol = Protocol.CreateTyping(Username, UID);
                _ = SendProtocolAsync(protocol);
            }

            // Reset timer
            _typingTimer?.Dispose();
            _typingTimer = new Timer(_ => StopTypingIndicator(), null, 2000, Timeout.Infinite);
        }

        public void StopTypingIndicator()
        {
            if (_isTyping)
            {
                _isTyping = false;
                _typingTimer?.Dispose();
                var protocol = Protocol.CreateStopTyping(Username, UID);
                _ = SendProtocolAsync(protocol);
            }
        }

        private async Task SendProtocolAsync(Protocol protocol)
        {
            try
            {
                if (_client?.Connected == true)
                {
                    var bytes = protocol.ToBytes();
                    await _client.GetStream().WriteAsync(bytes, 0, bytes.Length);
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke("Send Error", ex.Message);
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                if (_client != null && _client.Connected)
                {
                    var protocol = Protocol.CreateLeave(Username, UID);
                    await SendProtocolAsync(protocol);

                    _cancellationTokenSource?.Cancel();
                    _typingTimer?.Dispose();

                    await Task.Delay(100); // Give time for message to send

                    _client?.Close();
                    ConnectionStatusChanged?.Invoke("Disconnected");
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke("Disconnect Error", ex.Message);
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _typingTimer?.Dispose();
            _client?.Close();
            _client?.Dispose();
        }
    }
}