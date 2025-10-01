using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using ChatClient.Net;

namespace ChatServer
{
    class Program
    {
        static List<Client> _users;
        static TcpListener _listener;
        static string _logFile = $"server_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        static object _logLock = new object();

        static void Main(string[] args)
        {
            Console.WriteLine("=== Chat Server ===");
            Console.Write("Enter IP Address (default 0.0.0.0): ");
            var ipInput = Console.ReadLine();
            var ip = string.IsNullOrWhiteSpace(ipInput) ? "0.0.0.0" : ipInput;

            Console.Write("Enter Port (default 7891): ");
            var portInput = Console.ReadLine();
            var port = string.IsNullOrWhiteSpace(portInput) ? 7891 : int.Parse(portInput);

            _users = new List<Client>();

            try
            {
                _listener = new TcpListener(IPAddress.Parse(ip), port);
                _listener.Start();

                Log($"Server started on {ip}:{port}");
                Console.WriteLine($"Server listening on {ip}:{port}");
                Console.WriteLine("Press Ctrl+C to stop the server\n");

                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    ShutdownServer();
                };

                while (true)
                {
                    try
                    {
                        var tcpClient = _listener.AcceptTcpClient();
                        var client = new Client(tcpClient);
                        _users.Add(client);

                        Log($"New connection from {tcpClient.Client.RemoteEndPoint}");
                        BroadcastConnection();
                    }
                    catch (Exception ex)
                    {
                        Log($"Error accepting client: {ex.Message}", true);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Fatal error: {ex.Message}", true);
                Console.WriteLine($"Server error: {ex.Message}");
            }
        }

        static void BroadcastConnection()
        {
            try
            {
                foreach (var user in _users.ToList())
                {
                    foreach (var usr in _users.ToList())
                    {
                        try
                        {
                            var protocol = Protocol.CreateJoin(usr.Username, usr.UID.ToString());
                            user.SendProtocol(protocol);
                        }
                        catch (Exception ex)
                        {
                            Log($"Error broadcasting to {user.Username}: {ex.Message}", true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in BroadcastConnection: {ex.Message}", true);
            }
        }

        public static void BroadcastMessage(Protocol protocol)
        {
            try
            {
                Log($"Broadcasting message from {protocol.From}: {protocol.Text}");

                foreach (var user in _users.ToList())
                {
                    try
                    {
                        if (user.ClientSocket?.Connected == true)
                        {
                            user.SendProtocol(protocol);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Error sending to {user.Username}: {ex.Message}", true);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in BroadcastMessage: {ex.Message}", true);
            }
        }

        public static void SendPrivateMessage(Protocol protocol)
        {
            try
            {
                Log($"Private message from {protocol.From} to {protocol.To}");

                var recipient = _users.FirstOrDefault(u => u.Username == protocol.To);
                var sender = _users.FirstOrDefault(u => u.Username == protocol.From);

                if (recipient != null && recipient.ClientSocket?.Connected == true)
                {
                    recipient.SendProtocol(protocol);

                    // Send confirmation to sender
                    if (sender != null && sender.ClientSocket?.Connected == true)
                    {
                        sender.SendProtocol(protocol);
                    }
                }
                else
                {
                    // User not found or offline
                    if (sender != null)
                    {
                        var errorProtocol = Protocol.CreateSystem($"User '{protocol.To}' is not online");
                        sender.SendProtocol(errorProtocol);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in SendPrivateMessage: {ex.Message}", true);
            }
        }

        public static void BroadcastTyping(Protocol protocol)
        {
            try
            {
                foreach (var user in _users.ToList())
                {
                    if (user.Username != protocol.From && user.ClientSocket?.Connected == true)
                    {
                        user.SendProtocol(protocol);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in BroadcastTyping: {ex.Message}", true);
            }
        }

        public static void HandleDisconnect(string uid)
        {
            try
            {
                var disconnectedUser = _users.FirstOrDefault(x => x.UID.ToString() == uid);
                if (disconnectedUser != null)
                {
                    Log($"User {disconnectedUser.Username} disconnected gracefully");

                    _users.Remove(disconnectedUser);

                    var protocol = Protocol.CreateLeave(disconnectedUser.Username, uid);

                    foreach (var user in _users.ToList())
                    {
                        try
                        {
                            if (user.ClientSocket?.Connected == true)
                            {
                                user.SendProtocol(protocol);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Error notifying {user.Username} of disconnect: {ex.Message}", true);
                        }
                    }

                    try
                    {
                        disconnectedUser.ClientSocket?.Close();
                        disconnectedUser.ClientSocket?.Dispose();
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in HandleDisconnect: {ex.Message}", true);
            }
        }

        public static void HandleConnectionLost(Client client)
        {
            try
            {
                Log($"Connection lost from {client.Username}");

                _users.Remove(client);

                var protocol = Protocol.CreateLeave(client.Username, client.UID.ToString());

                foreach (var user in _users.ToList())
                {
                    try
                    {
                        if (user.ClientSocket?.Connected == true)
                        {
                            user.SendProtocol(protocol);
                        }
                    }
                    catch { }
                }

                try
                {
                    client.ClientSocket?.Close();
                    client.ClientSocket?.Dispose();
                }
                catch { }
            }
            catch (Exception ex)
            {
                Log($"Error in HandleConnectionLost: {ex.Message}", true);
            }
        }

        static void ShutdownServer()
        {
            Log("Server shutting down...");
            Console.WriteLine("\nShutting down server...");

            var shutdownProtocol = Protocol.CreateSystem("Server is shutting down");

            foreach (var user in _users.ToList())
            {
                try
                {
                    user.SendProtocol(shutdownProtocol);
                    user.ClientSocket?.Close();
                }
                catch { }
            }

            _listener?.Stop();
            Log("Server stopped");
            Console.WriteLine("Server stopped");
            Environment.Exit(0);
        }

        public static void Log(string message, bool isError = false)
        {
            lock (_logLock)
            {
                try
                {
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    var logMessage = $"[{timestamp}] {(isError ? "ERROR: " : "")}{message}";

                    Console.WriteLine(logMessage);
                    File.AppendAllText(_logFile, logMessage + Environment.NewLine);
                }
                catch { }
            }
        }
    }
}