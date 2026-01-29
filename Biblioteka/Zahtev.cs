using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Biblioteka
{
    [Serializable]
    public class Zahtev
    {
        public string Putanja { get; set; }     
        public Operacija Operacija { get; set; }
        public Datoteka Datoteka { get; set; }    

        public string KlijentKorisnickoIme { get; set; } 
        public string Vreme { get; set; }                

        public override string ToString()
        {
            return $"{Vreme} | {KlijentKorisnickoIme} | {Operacija} | {Putanja}";
        }
    }
}
