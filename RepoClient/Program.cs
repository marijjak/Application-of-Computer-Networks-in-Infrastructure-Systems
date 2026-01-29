using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Biblioteka;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace RepoClient
{
    internal class Program
    {
        private const int UDP_PORT = 15011;

        static void Main(string[] args)
        {
            Console.Write("Unesi korisnicko ime: ");
            string username = Console.ReadLine().Trim();

            
            int tcpPort = UDPPrijava(username);
            if (tcpPort <= 0)
            {
                Console.WriteLine("Prijava nije uspela.");
                Console.ReadKey();
                return;
            }

            Socket tcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint serverEP = new IPEndPoint(IPAddress.Loopback, tcpPort);

            try
            {
                tcp.Connect(serverEP);
                Console.WriteLine($"Povezan na TCP server {serverEP}");
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Ne mogu da se povezem na TCP: {ex.SocketErrorCode}");
                Console.ReadKey();
                return;
            }

            while (true)
            {
                Console.WriteLine("\n--- MENI ---");
                Console.WriteLine("\nIzbor opcije:");
                Console.WriteLine("1) Citanje datoteke");
                Console.WriteLine("2) Izmena/Kreiranje datoteke");
                Console.WriteLine("3) Uklanjanje datoteke");
                Console.WriteLine("4) Statistika");
                Console.WriteLine("0) Izlaz");
                Console.Write("Izbor: ");

                string izbor = Console.ReadLine().Trim();
                if (izbor == "0") break;

                Zahtev z = new Zahtev
                {
                    KlijentKorisnickoIme = username,
                    Vreme = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                if (izbor == "1")
                {
                    z.Operacija = Operacija.Citanje;
                    Console.Write("Unesi naziv datoteke: ");
                    z.Putanja = Console.ReadLine().Trim();
                    z.Datoteka = null;

                    object resp = PosaljiIPrihvati(tcp, z);
                    PrikaziOdgovor(resp);
                }
                else if (izbor == "2")
                {
                    Console.Write("Unesi naziv datoteke: ");
                    string naziv = Console.ReadLine().Trim();

                
                    Zahtev lockReq = new Zahtev
                    {
                        KlijentKorisnickoIme = username,
                        Vreme = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        Operacija = Operacija.Izmena,
                        Putanja = naziv,
                        Datoteka = null
                    };

                    object lockRespObj = PosaljiIPrihvati(tcp, lockReq);

                    if (!(lockRespObj is Odgovor lockResp))
                    {
                        Console.WriteLine("Neocekivan odgovor servera.");
                        continue;
                    }

                    Console.WriteLine(lockResp.ToString());

                
                    if (!lockResp.Uspeh)
                        continue;

              
                    Datoteka trenutna = lockResp.Datoteka;
                    if (trenutna == null)
                    {
                        Console.WriteLine("Server nije vratio datoteku.");
                        continue;
                    }

                    Console.WriteLine("\n--- Trenutni sadrzaj ---");
                    Console.WriteLine(trenutna.Sadrzaj);

                    Console.WriteLine("\nUnesi NOVI sadrzaj (jedna linija):");
                    string noviSadrzaj = Console.ReadLine();

             
                    Zahtev commitReq = new Zahtev
                    {
                        KlijentKorisnickoIme = username,
                        Vreme = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        Operacija = Operacija.Izmena,
                        Putanja = naziv,
                        Datoteka = new Datoteka
                        {
                            Naziv = naziv,
                            Autor = username,
                            PoslednjaPromena = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            Sadrzaj = noviSadrzaj
                        }
                    };

                    object commitRespObj = PosaljiIPrihvati(tcp, commitReq);
                    PrikaziOdgovor(commitRespObj);
                }

                else if (izbor == "3")
                {
                    z.Operacija = Operacija.Uklanjanje;
                    Console.Write("Unesi naziv datoteke: ");
                    z.Putanja = Console.ReadLine().Trim();
                    z.Datoteka = null;

                    object resp = PosaljiIPrihvati(tcp, z);
                    PrikaziOdgovor(resp);
                }
                else if (izbor == "4")
                {
                    z.Operacija = Operacija.Statistika;
                    z.Putanja = "";
                    z.Datoteka = null;

                    object resp = PosaljiIPrihvati(tcp, z);
                    PrikaziOdgovor(resp);
                }
                else
                {
                    Console.WriteLine("Nepoznat izbor.");
                }
            }

            tcp.Close();
            Console.WriteLine("Klijent zavrsio.");
            Console.ReadKey();
        }

        private static int UDPPrijava(string username)
        {
            Socket udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            IPEndPoint serverUdpEP = new IPEndPoint(IPAddress.Loopback, UDP_PORT);

            string prijava = $"PRIJAVA|{username}";
            byte[] data = Encoding.UTF8.GetBytes(prijava);

            try
            {
                udp.SendTo(data, serverUdpEP);

                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                byte[] buffer = new byte[1024];
                udp.ReceiveTimeout = 3000;

                int bytes = udp.ReceiveFrom(buffer, ref from);
                string msg = Encoding.UTF8.GetString(buffer, 0, bytes);

                if (msg.StartsWith("OK|"))
                {
                    int tcpPort = int.Parse(msg.Split('|')[1]);
                    Console.WriteLine($"UDP prijava OK. TCP port = {tcpPort}");
                    return tcpPort;
                }

                Console.WriteLine($"UDP prijava neuspesna: {msg}");
                return -1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UDP greska: {ex.Message}");
                return -1;
            }
            finally
            {
                udp.Close();
            }
        }

        private static object PosaljiIPrihvati(Socket tcp, Zahtev z)
        {
#pragma warning disable SYSLIB0011
            byte[] outData;
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryFormatter bf = new BinaryFormatter();
                bf.Serialize(ms, z);
                outData = ms.ToArray();
            }

            tcp.Send(outData);

            byte[] buffer = new byte[8192];
            int bytes = tcp.Receive(buffer);

            using (MemoryStream ms = new MemoryStream(buffer, 0, bytes))
            {
                BinaryFormatter bf = new BinaryFormatter();
                return bf.Deserialize(ms);
            }
#pragma warning restore SYSLIB0011
        }

        private static void PrikaziOdgovor(object obj)
        {
            if (obj is Odgovor o)
            {
                Console.WriteLine(o.ToString());
                if (o.Uspeh && o.Datoteka != null)
                {
                    Console.WriteLine("--- Datoteka ---");
                    Console.WriteLine(o.Datoteka.ToString());
                    Console.WriteLine($"Sadrzaj: {o.Datoteka.Sadrzaj}");
                }
            }
            else if (obj is Statistika s)
            {
                Console.WriteLine("--- STATISTIKA ---");
                Console.WriteLine($"Klijent: {s.KlijentKorisnickoIme}");
                Console.WriteLine($"Ukupno datoteka: {s.UkupanBrojDatotekaNaRepozitorijumu}");
                Console.WriteLine("Kreirao:");
                if (s.NaziviDatotekaKojeJeKreirao.Count == 0) Console.WriteLine("(nema)");
                foreach (var n in s.NaziviDatotekaKojeJeKreirao)
                    Console.WriteLine($"- {n}");
            }
            else
            {
                Console.WriteLine("Nepoznat odgovor.");
            }
        }
    }
}
