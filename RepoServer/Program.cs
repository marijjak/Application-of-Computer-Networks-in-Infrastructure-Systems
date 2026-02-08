
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Biblioteka;

namespace RepoServer
{
    internal class Program
    {
        private const int UDP_PORT = 15011;
        private const int TCP_PORT = 50001;
        private const int MAX_KLIJENATA = 10;

        private static readonly Repozitorijum _repozitorijum = new Repozitorijum();
        private static readonly UpravljačZahtevima _upravljač = new UpravljačZahtevima(_repozitorijum);

        static void Main(string[] args)
        {
            Thread udpThread = new Thread(UDPLoginLoop) { IsBackground = true };
            udpThread.Start();

            _upravljač.PokreniTCPServer(TCP_PORT, MAX_KLIJENATA);
        }

        private static void UDPLoginLoop()
        {
            Socket udpServer = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            IPEndPoint udpEP = new IPEndPoint(IPAddress.Any, UDP_PORT);
            udpServer.Bind(udpEP);
            udpServer.Blocking = false;

            Console.WriteLine($"[UDP] Prijava aktivna na {udpEP}");

            EndPoint senderEP = new IPEndPoint(IPAddress.Any, 0);
            byte[] buffer = new byte[2048];

            while (true)
            {
                try
                {
                    var read = new System.Collections.Generic.List<Socket> { udpServer };
                    Socket.Select(read, null, null, 500_000); // 0.5s
                    if (read.Count == 0) continue;

                    int bytes = udpServer.ReceiveFrom(buffer, ref senderEP);
                    string msg = Encoding.UTF8.GetString(buffer, 0, bytes);

                    if (msg.StartsWith("PRIJAVA|", StringComparison.OrdinalIgnoreCase))
                    {
                        string user = msg.Split('|')[1].Trim();
                        Console.WriteLine($"[UDP] PRIJAVA od {user} ({senderEP})");

                 
                        string lista = _repozitorijum.FormatirajListuDatotekaZaUDP();
                        string reply = $"OK|{TCP_PORT}|{lista}";
                        udpServer.SendTo(Encoding.UTF8.GetBytes(reply), senderEP);
                    }
                    else
                    {
                        udpServer.SendTo(Encoding.UTF8.GetBytes("ERR|Nepoznata UDP komanda"), senderEP);
                    }
                }
                catch (SocketException)
                {
                  
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UDP] Greska: {ex.Message}");
                }
            }
        }
    }
}
