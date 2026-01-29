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
      
        private const int UDP_PORT = 15011;

     
        private const int TCP_PORT = 50001;
        private const int MAX_KLIJENATA = 10;

       
        private static readonly List<Datoteka> _repo = new List<Datoteka>();

    
        private static readonly Dictionary<string, HashSet<string>> _kreiranoPoKlijentu =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        
        private static readonly Dictionary<string, List<Zahtev>> _istorija =
            new Dictionary<string, List<Zahtev>>(StringComparer.OrdinalIgnoreCase);

        
        private static readonly List<Socket> _tcpKlijenti = new List<Socket>();

      
        private static readonly Dictionary<string, string> _lockOwner =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    
        private static readonly Dictionary<string, Queue<Zahtev>> _pending =
            new Dictionary<string, Queue<Zahtev>>(StringComparer.OrdinalIgnoreCase);


        static void Main(string[] args)
        {
           
            Thread udpThread = new Thread(UDPLoginLoop);
            udpThread.IsBackground = true;
            udpThread.Start();

           
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

                   
                    if (_tcpKlijenti.Count < MAX_KLIJENATA)
                        checkRead.Add(tcpServer);

                    checkError.Add(tcpServer);

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

                        Odgovor val = ValidirajZahtev(zahtev);
                        if (val != null)
                        {
                            byte[] outBad = Serialize(val);
                            s.Send(outBad);
                            continue;
                        }

                        if (!_istorija.ContainsKey(zahtev.KlijentKorisnickoIme))
                            _istorija[zahtev.KlijentKorisnickoIme] = new List<Zahtev>();
                        _istorija[zahtev.KlijentKorisnickoIme].Add(zahtev);

                    
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

        private static Odgovor ObradiCitanje(Zahtev z)
        {
            if (_lockOwner.TryGetValue(z.Putanja, out string owner) &&
    !string.Equals(owner, z.KlijentKorisnickoIme, StringComparison.OrdinalIgnoreCase))
            {
                Enqueue(z.Putanja, z);
                return new Odgovor { Uspeh = false, Poruka = "ZAKLJUCANO - citanje dodato u red" };
            }

            Datoteka d = _repo.Find(x => string.Equals(x.Naziv, z.Putanja, StringComparison.OrdinalIgnoreCase));
            if (d == null)
                return new Odgovor { Uspeh = false, Poruka = "DATOTEKA_NE_POSTOJI", Datoteka = null };

            return new Odgovor { Uspeh = true, Poruka = "OK", Datoteka = d };
        }

        private static Odgovor ObradiIzmenu(Zahtev z)
        {
            string naziv = z.Putanja;

            
            if (z.Datoteka == null)
            {
                if (_lockOwner.TryGetValue(naziv, out string owner) &&
                    !string.Equals(owner, z.KlijentKorisnickoIme, StringComparison.OrdinalIgnoreCase))
                {
                    Enqueue(naziv, z);
                    return new Odgovor { Uspeh = false, Poruka = "ZAKLJUCANO - zahtev dodat u red" };
                }

                _lockOwner[naziv] = z.KlijentKorisnickoIme;

                Datoteka postojeca = _repo.Find(x => string.Equals(x.Naziv, naziv, StringComparison.OrdinalIgnoreCase));

                if (postojeca == null)
                {
                    postojeca = new Datoteka
                    {
                        Naziv = naziv,
                        Autor = z.KlijentKorisnickoIme,
                        PoslednjaPromena = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        Sadrzaj = ""
                    };
                }

                return new Odgovor { Uspeh = true, Poruka = "LOCKED - posalji izmenjenu datoteku", Datoteka = postojeca };
            }

            if (_lockOwner.TryGetValue(naziv, out string owner2) &&
                !string.Equals(owner2, z.KlijentKorisnickoIme, StringComparison.OrdinalIgnoreCase))
            {
                Enqueue(naziv, z);
                return new Odgovor { Uspeh = false, Poruka = "ZAKLJUCANO - zahtev dodat u red" };
            }

            _lockOwner[naziv] = z.KlijentKorisnickoIme;

            Datoteka d = _repo.Find(x => string.Equals(x.Naziv, naziv, StringComparison.OrdinalIgnoreCase));
            bool nova = (d == null);

            if (nova)
            {
                _repo.Add(z.Datoteka);

                if (!_kreiranoPoKlijentu.ContainsKey(z.KlijentKorisnickoIme))
                    _kreiranoPoKlijentu[z.KlijentKorisnickoIme] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                _kreiranoPoKlijentu[z.KlijentKorisnickoIme].Add(z.Datoteka.Naziv);
            }
            else
            {
                d.Sadrzaj = z.Datoteka.Sadrzaj;
                d.Autor = z.Datoteka.Autor;
                d.PoslednjaPromena = z.Datoteka.PoslednjaPromena;
            }

            _lockOwner.Remove(naziv);
            ProcessPending(naziv);

            return new Odgovor { Uspeh = true, Poruka = nova ? "KREIRANO" : "IZMENJENO" };
        }


        private static Odgovor ObradiUklanjanje(Zahtev z)
        {
            if (_lockOwner.TryGetValue(z.Putanja, out string owner) &&
    !string.Equals(owner, z.KlijentKorisnickoIme, StringComparison.OrdinalIgnoreCase))
            {
                Enqueue(z.Putanja, z);
                return new Odgovor { Uspeh = false, Poruka = "ZAKLJUCANO - brisanje dodato u red" };
            }

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
        private static void Enqueue(string naziv, Zahtev z)
        {
            if (!_pending.ContainsKey(naziv))
                _pending[naziv] = new Queue<Zahtev>();

            _pending[naziv].Enqueue(z);
            Console.WriteLine($"[QUEUE] Dodat zahtev u red za {naziv}: {z}");
        }

        private static void ProcessPending(string naziv)
        {
            if (!_pending.ContainsKey(naziv) || _pending[naziv].Count == 0) return;

            Console.WriteLine($"[QUEUE] Obrada reda za {naziv}, zahteva: {_pending[naziv].Count}");

            while (_pending[naziv].Count > 0)
            {
                Zahtev next = _pending[naziv].Dequeue();

             
                if (next.Operacija == Operacija.Izmena && next.Datoteka == null)
                {
                    Console.WriteLine($"[QUEUE] Preskacem LOCK zahtev (klijent mora ponovo): {next}");
                    continue;
                }

                object result = ObradiZahtev(next);
                Console.WriteLine($"[QUEUE] Izvrsen zahtev iz reda: {next} -> {result}");
            }
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
