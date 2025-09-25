using ChatServer.Net.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ChatServer
{
    class Client
    {
        public string Username { get; set; }
        public Guid UID { get; set; }
        public TcpClient ClientSocket { get; set; }
        PacketReader _packetReadeer;

        public Client(TcpClient client)
        {
            ClientSocket = client;
            UID = Guid.NewGuid();
            _packetReadeer = new PacketReader(ClientSocket.GetStream());
            var opcode = _packetReadeer.ReadByte();
            Username = _packetReadeer.ReadMessage();
            Console.WriteLine($"[{DateTime.Now}]: Client has connected with the username: {Username}");
            Task.Run(() => Process());
        }

        void Process()
        {
            while (true)
            {
                try
                {
                    var opcode = _packetReadeer.ReadByte();
                    switch (opcode)
                    {
                        case 5:
                            var msg = _packetReadeer.ReadMessage();
                            Console.WriteLine($"[{DateTime.Now}]: Message Received! {msg}");
                            Program.BroadcastMessage($"[{DateTime.Now}]: [{Username}]: {msg}");
                            break;

                        case 10:
                            var uid = _packetReadeer.ReadMessage();
                            Program.HandleDisconnect(uid);
                            ClientSocket.Close();
                            return;

                        default:
                            break;
                    }

                }

                catch (Exception)
                {
                    Console.WriteLine($"[{UID.ToString()}]: Disconnected");
                    Program.BroadcastDisconnect(UID.ToString());
                    ClientSocket.Close();
                    break;
                }

            }
        }
    }
}

