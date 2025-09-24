using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using ChatClient.Net.IO;

namespace ChatClient.Net
{
    class Server
    {
        TcpClient _client;
        public PacketReader PacketReader;
        public event Action connectedEvent;
        public event Action msgReceivedEvent;
        public event Action userDisconnectEvent;
        public bool IsConnected => _client?.Connected ?? false;


        public Server()
        {
            _client = new TcpClient();
        }

        public void ConnectToServer(string username)
        {
            if (_client == null || !_client.Connected)
            {
                _client = new TcpClient(); 
                _client.Connect("127.0.0.1", 7891);

                PacketReader = new PacketReader(_client.GetStream());

                if (!string.IsNullOrEmpty(username))
                {
                    var connectPacket = new PacketBuilder();
                    connectPacket.WriteOpCode(0);
                    connectPacket.WriteMessage(username);
                    _client.Client.Send(connectPacket.GetPacketBytes());
                }

                ReadPackets();
            }
        }


        private void ReadPackets()
        {
            Task.Run(() =>
            {
                try
                {
                    while (IsConnected)
                    {
                        var opcode = PacketReader.ReadByte();
                        switch (opcode)
                        {
                            case 1:
                                connectedEvent?.Invoke();
                                break;
                            case 5:
                                msgReceivedEvent?.Invoke();
                                break;
                            case 10:
                                userDisconnectEvent?.Invoke();
                                break;
                            default:
                                Console.WriteLine("Unknown opcode");
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Connection closed: " + ex.Message);
                }
            });
        }


        public void SendMessageToServer(string message)
        {
            var messagePacket = new PacketBuilder();
            messagePacket.WriteOpCode(5);
            messagePacket.WriteMessage(message);
            _client.Client.Send(messagePacket.GetPacketBytes());
        }

        public void DisconnectFromServer()
        {
            if (_client != null && _client.Connected)
            {
                _client.Close();
                Console.WriteLine("Disconnected from server.");
            }
        }
    }
}
