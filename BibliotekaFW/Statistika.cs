using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Biblioteka
{
    [Serializable]
    public class Statistika
    {
        public string KlijentKorisnickoIme { get; set; }
        public List<string> NaziviDatotekaKojeJeKreirao { get; set; } = new List<string>();
        public int UkupanBrojDatotekaNaRepozitorijumu { get; set; }
    }
}
