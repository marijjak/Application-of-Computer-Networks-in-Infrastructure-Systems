
using System;
using System.Collections.Generic;
using System.Linq;
using Biblioteka;

namespace RepoServer
{
    internal class Repozitorijum
    {
   
        private readonly List<Datoteka> _repo = new List<Datoteka>();

      
        private readonly List<Zahtev> _aktivniZahtevi = new List<Zahtev>();

        private readonly Dictionary<string, HashSet<string>> _kreiranoPoKlijentu =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);


        private readonly Dictionary<string, List<Zahtev>> _istorija =
            new Dictionary<string, List<Zahtev>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> _lockOwner =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Queue<Zahtev>> _pending =
            new Dictionary<string, Queue<Zahtev>>(StringComparer.OrdinalIgnoreCase);

        private readonly object _sync = new object();

        public object Obradi(Zahtev z)
        {
            lock (_sync)
            {
            
                if (!_istorija.ContainsKey(z.KlijentKorisnickoIme))
                    _istorija[z.KlijentKorisnickoIme] = new List<Zahtev>();
                _istorija[z.KlijentKorisnickoIme].Add(z);

            
                _aktivniZahtevi.Add(z);
                try
                {
                    switch (z.Operacija)
                    {
                        case Operacija.Citanje: return ObradiCitanje(z);
                        case Operacija.Izmena: return ObradiIzmenu(z);
                        case Operacija.Uklanjanje: return ObradiUklanjanje(z);
                        case Operacija.Statistika: return ObradiStatistiku(z);
                        default: return new Odgovor { Uspeh = false, Poruka = "Nepoznata operacija" };
                    }
                }
                finally
                {
                    _aktivniZahtevi.Remove(z);
                }
            }
        }

        public string FormatirajListuDatotekaZaUDP()
        {
            lock (_sync)
            {
                if (_repo.Count == 0) return "(prazno)";
          
                return string.Join("|", _repo.Select(d => $"{d.Naziv};{d.Autor};{d.PoslednjaPromena}"));
            }
        }

        private Odgovor ObradiCitanje(Zahtev z)
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

        private Odgovor ObradiIzmenu(Zahtev z)
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

                return new Odgovor
                {
                    Uspeh = true,
                    Poruka = "LOCKED - posalji izmenjenu datoteku",
                    Datoteka = postojeca
                };
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

        private Odgovor ObradiUklanjanje(Zahtev z)
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

        private Statistika ObradiStatistiku(Zahtev z)
        {
            Statistika st = new Statistika
            {
                KlijentKorisnickoIme = z.KlijentKorisnickoIme,
                UkupanBrojDatotekaNaRepozitorijumu = _repo.Count
            };

            if (_kreiranoPoKlijentu.TryGetValue(z.KlijentKorisnickoIme, out var set))
                st.NaziviDatotekaKojeJeKreirao.AddRange(set);

            return st;
        }

        private void Enqueue(string naziv, Zahtev z)
        {
            if (!_pending.ContainsKey(naziv))
                _pending[naziv] = new Queue<Zahtev>();

            _pending[naziv].Enqueue(z);
            Console.WriteLine($"[QUEUE] Dodat zahtev u red za {naziv}: {z}");
        }

        private void ProcessPending(string naziv)
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

                object result = Obradi(next); 
                Console.WriteLine($"[QUEUE] Izvrsen zahtev iz reda: {next} -> {result}");
            }
        }
    }
}
