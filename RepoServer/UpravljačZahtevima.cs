
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using Biblioteka;

namespace RepoServer
{
    internal class UpravljačZahtevima
    {
        private readonly Repozitorijum _repo;

        private readonly List<Socket> _tcpKlijenti = new List<Socket>();
        private Socket _tcpServer;

        public UpravljačZahtevima(Repozitorijum repo)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        }

        public void PokreniTCPServer(int port, int maxKlijenata)
        {
            _tcpServer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint tcpEP = new IPEndPoint(IPAddress.Any, port);

            _tcpServer.Bind(tcpEP);
            _tcpServer.Listen(maxKlijenata);
            _tcpServer.Blocking = false;

            Console.WriteLine($"[TCP] Server slusa na {tcpEP}");

            byte[] buffer = new byte[8192];

            while (true)
            {
                try
                {
                    List<Socket> checkRead = new List<Socket>();
                    List<Socket> checkError = new List<Socket>();

                    if (_tcpKlijenti.Count < maxKlijenata)
                        checkRead.Add(_tcpServer);

                    checkError.Add(_tcpServer);

                    foreach (var c in _tcpKlijenti)
                    {
                        checkRead.Add(c);
                        checkError.Add(c);
                    }

                    Socket.Select(checkRead, null, checkError, 1_000_000);

                    if (checkError.Count > 0)
                    {
                        foreach (var s in checkError)
                        {
                            Console.WriteLine($"[TCP] Greska na {s.LocalEndPoint}, zatvaram...");
                            SafeCloseAndRemove(s);
                        }
                    }

                    foreach (var s in checkRead)
                    {
                        if (s == _tcpServer)
                        {
                            Socket client = _tcpServer.Accept();
                            client.Blocking = false;
                            _tcpKlijenti.Add(client);
                            Console.WriteLine($"[TCP] Klijent povezan: {client.RemoteEndPoint}");
                            continue;
                        }

                        int bytes = s.Receive(buffer);
                        if (bytes == 0)
                        {
                            Console.WriteLine("[TCP] Klijent prekinuo vezu");
                            SafeCloseAndRemove(s);
                            continue;
                        }

                        Zahtev zahtev = Deserialize<Zahtev>(buffer, bytes);
                        Console.WriteLine($"[TCP] Primljen zahtev: {zahtev}");

                        Odgovor val = ValidirajZahtev(zahtev);
                        if (val != null)
                        {
                            s.Send(Serialize(val));
                            continue;
                        }

                       
                        object odgovorObj = _repo.Obradi(zahtev);

                        s.Send(Serialize(odgovorObj));
                    }
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"[TCP] Socket greska: {ex.SocketErrorCode}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TCP] Greska: {ex.Message}");
                }
            }
        }

        private static Odgovor ValidirajZahtev(Zahtev z)
        {
            if (z == null)
                return new Odgovor { Uspeh = false, Poruka = "Neispravan zahtev (null)" };

            if (string.IsNullOrWhiteSpace(z.KlijentKorisnickoIme))
                return new Odgovor { Uspeh = false, Poruka = "Neispravan zahtev (nema korisnickog imena)" };

            if (!Enum.IsDefined(typeof(Operacija), z.Operacija))
                return new Odgovor { Uspeh = false, Poruka = "Neispravan zahtev (nepoznata operacija)" };

            if (z.Operacija != Operacija.Statistika && string.IsNullOrWhiteSpace(z.Putanja))
                return new Odgovor { Uspeh = false, Poruka = "Neispravan zahtev (nema putanje/naziva)" };

            if (z.Operacija == Operacija.Izmena && z.Datoteka != null)
            {
                if (string.IsNullOrWhiteSpace(z.Datoteka.Naziv) ||
                    string.IsNullOrWhiteSpace(z.Datoteka.Autor) ||
                    string.IsNullOrWhiteSpace(z.Datoteka.PoslednjaPromena) ||
                    z.Datoteka.Sadrzaj == null)
                {
                    return new Odgovor { Uspeh = false, Poruka = "Neispravna Datoteka u zahtevu" };
                }
            }

            return null;
        }

        private void SafeCloseAndRemove(Socket s)
        {
            try { s.Close(); } catch { }
            _tcpKlijenti.Remove(s);
        }

        private static T Deserialize<T>(byte[] data, int len) where T : class
        {

            using (MemoryStream ms = new MemoryStream(data, 0, len))
            {
                BinaryFormatter bf = new BinaryFormatter();
                return bf.Deserialize(ms) as T;
            }

        }

        private static byte[] Serialize(object obj)
        {

            using (MemoryStream ms = new MemoryStream())
            {
                BinaryFormatter bf = new BinaryFormatter();
                bf.Serialize(ms, obj);
                return ms.ToArray();
            }

        }
    }
}
