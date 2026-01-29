using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Biblioteka
{
    [Serializable]
    public class Odgovor
    {
        public bool Uspeh { get; set; }
        public string Poruka { get; set; }
        public Datoteka Datoteka { get; set; } 

        public override string ToString()
        {
            return $"{(Uspeh ? "OK" : "ERR")} | {Poruka}";
        }
    }
}
