using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Biblioteka;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace RepoServer
{
    internal class Program
    {
        // UDP (prijava)
        private const int UDP_PORT = 15011;

        // TCP (upravljanje zahtevima)
        private const int TCP_PORT = 50001;
        private const int MAX_KLIJENATA = 10;

        // Repozitorijum: lista datoteka na serveru
        private static readonly List<Datoteka> _repo = new List<Datoteka>();

        // Statistika: ko je kreirao koje fajlove
        private static readonly Dictionary<string, HashSet<string>> _kreiranoPoKlijentu =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        // Istorija zahteva po klijentu
        private static readonly Dictionary<string, List<Zahtev>> _istorija =
            new Dictionary<string, List<Zahtev>>(StringComparer.OrdinalIgnoreCase);

        // Aktivni TCP klijenti
        private static readonly List<Socket> _tcpKlijenti = new List<Socket>();

        static void Main(string[] args)
        {
            // 1) UDP thread za PRIJAVA
            Thread udpThread = new Thread(UDPLoginLoop);
            udpThread.IsBackground = true;
            udpThread.Start();

            // 2) TCP server loop za zahteve
            TCPServerLoop();
        }

        private static void UDPLoginLoop()
        {
            Socket udpServer = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            IPEndPoint udpEP = new IPEndPoint(IPAddress.Any, UDP_PORT);
            udpServer.Bind(udpEP);
            udpServer.Blocking = false;

            Console.WriteLine($"[UDP] Prijava aktivna na {udpEP}");

            EndPoint senderEP = new IPEndPoint(IPAddress.Any, 0);
            byte[] buffer = new byte[1024];

            while (true)
            {
                try
                {
                    List<Socket> read = new List<Socket> { udpServer };
                    Socket.Select(read, null, null, 500_000); // 0.5s

                    if (read.Count == 0) continue;

                    int bytes = udpServer.ReceiveFrom(buffer, ref senderEP);
                    string msg = Encoding.UTF8.GetString(buffer, 0, bytes);

                    // Ocekivano: "PRIJAVA|korisnickoIme"
                    if (msg.StartsWith("PRIJAVA|", StringComparison.OrdinalIgnoreCase))
                    {
                        string user = msg.Split('|')[1].Trim();
                        Console.WriteLine($"[UDP] PRIJAVA od {user} ({senderEP})");

                        string reply = $"OK|{TCP_PORT}";
                        byte[] data = Encoding.UTF8.GetBytes(reply);
                        udpServer.SendTo(data, senderEP);
                    }
                    else
                    {
                        string reply = "ERR|Nepoznata UDP komanda";
                        udpServer.SendTo(Encoding.UTF8.GetBytes(reply), senderEP);
                    }
                }
                catch (SocketException)
                {
                    // nonblocking -> normalno da ponekad nema podataka
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UDP] Greska: {ex.Message}");
                }
            }
        }

        private static void TCPServerLoop()
        {
            Socket tcpServer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint tcpEP = new IPEndPoint(IPAddress.Any, TCP_PORT);

            tcpServer.Bind(tcpEP);
            tcpServer.Listen(MAX_KLIJENATA);
            tcpServer.Blocking = false;

            Console.WriteLine($"[TCP] Server slusa na {tcpEP}");

            byte[] buffer = new byte[4096];

            while (true)
            {
                try
                {
                    List<Socket> checkRead = new List<Socket>();
                    List<Socket> checkError = new List<Socket>();

                    // prihvati nove klijente
                    if (_tcpKlijenti.Count < MAX_KLIJENATA)
                        checkRead.Add(tcpServer);

                    checkError.Add(tcpServer);

                    foreach (var c in _tcpKlijenti)
                    {
                        checkRead.Add(c);
                        checkError.Add(c);
                    }

                    Socket.Select(checkRead, null, checkError, 1_000_000); // 1s

                    // greske
                    if (checkError.Count > 0)
                    {
                        foreach (var s in checkError)
                        {
                            Console.WriteLine($"[TCP] Greska na {s.LocalEndPoint}, zatvaram...");
                            SafeCloseAndRemove(s);
                        }
                    }

                    // citanje
                    foreach (var s in checkRead)
                    {
                        if (s == tcpServer)
                        {
                            Socket client = tcpServer.Accept();
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

                        // sacuvaj u istoriju
                        if (!_istorija.ContainsKey(zahtev.KlijentKorisnickoIme))
                            _istorija[zahtev.KlijentKorisnickoIme] = new List<Zahtev>();
                        _istorija[zahtev.KlijentKorisnickoIme].Add(zahtev);

                        // obradi
                        object odgovorObj = ObradiZahtev(zahtev);

                        byte[] outData = Serialize(odgovorObj);
                        s.Send(outData);
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

        private static object ObradiZahtev(Zahtev z)
        {
            switch (z.Operacija)
            {
                case Operacija.Citanje:
                    return ObradiCitanje(z);

                case Operacija.Izmena:
                    return ObradiIzmenu(z);

                case Operacija.Uklanjanje:
                    return ObradiUklanjanje(z);

                case Operacija.Statistika:
                    return ObradiStatistiku(z);

                default:
                    return new Odgovor { Uspeh = false, Poruka = "Nepoznata operacija" };
            }
        }

        private static Odgovor ObradiCitanje(Zahtev z)
        {
            Datoteka d = _repo.Find(x => string.Equals(x.Naziv, z.Putanja, StringComparison.OrdinalIgnoreCase));
            if (d == null)
                return new Odgovor { Uspeh = false, Poruka = "DATOTEKA_NE_POSTOJI", Datoteka = null };

            return new Odgovor { Uspeh = true, Poruka = "OK", Datoteka = d };
        }

        private static Odgovor ObradiIzmenu(Zahtev z)
        {
            if (z.Datoteka == null)
                return new Odgovor { Uspeh = false, Poruka = "Nedostaje datoteka u zahtevu" };

            // ako postoji -> zamena, ako ne postoji -> kreiranje
            Datoteka postojeca = _repo.Find(x => string.Equals(x.Naziv, z.Putanja, StringComparison.OrdinalIgnoreCase));
            bool nova = (postojeca == null);

            if (nova)
            {
                _repo.Add(z.Datoteka);

                if (!_kreiranoPoKlijentu.ContainsKey(z.KlijentKorisnickoIme))
                    _kreiranoPoKlijentu[z.KlijentKorisnickoIme] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                _kreiranoPoKlijentu[z.KlijentKorisnickoIme].Add(z.Datoteka.Naziv);

                return new Odgovor { Uspeh = true, Poruka = "KREIRANO", Datoteka = null };
            }
            else
            {
                postojeca.Sadrzaj = z.Datoteka.Sadrzaj;
                postojeca.Autor = z.Datoteka.Autor;
                postojeca.PoslednjaPromena = z.Datoteka.PoslednjaPromena;

                return new Odgovor { Uspeh = true, Poruka = "IZMENJENO", Datoteka = null };
            }
        }

        private static Odgovor ObradiUklanjanje(Zahtev z)
        {
            Datoteka d = _repo.Find(x => string.Equals(x.Naziv, z.Putanja, StringComparison.OrdinalIgnoreCase));
            if (d == null)
                return new Odgovor { Uspeh = false, Poruka = "DATOTEKA_NE_POSTOJI" };

            _repo.Remove(d);
            return new Odgovor { Uspeh = true, Poruka = "OBRISANO" };
        }

        private static Statistika ObradiStatistiku(Zahtev z)
        {
            Statistika st = new Statistika();
            st.KlijentKorisnickoIme = z.KlijentKorisnickoIme;
            st.UkupanBrojDatotekaNaRepozitorijumu = _repo.Count;

            if (_kreiranoPoKlijentu.TryGetValue(z.KlijentKorisnickoIme, out var set))
                st.NaziviDatotekaKojeJeKreirao.AddRange(set);

            return st;
        }

        private static void SafeCloseAndRemove(Socket s)
        {
            try { s.Close(); } catch { }
            _tcpKlijenti.Remove(s);
        }

        private static T Deserialize<T>(byte[] data, int len) where T : class
        {
#pragma warning disable SYSLIB0011
            using (MemoryStream ms = new MemoryStream(data, 0, len))
            {
                BinaryFormatter bf = new BinaryFormatter();
                return bf.Deserialize(ms) as T;
            }
#pragma warning restore SYSLIB0011
        }

        private static byte[] Serialize(object obj)
        {
#pragma warning disable SYSLIB0011
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryFormatter bf = new BinaryFormatter();
                bf.Serialize(ms, obj);
                return ms.ToArray();
            }
#pragma warning restore SYSLIB0011
        }
    }
}
