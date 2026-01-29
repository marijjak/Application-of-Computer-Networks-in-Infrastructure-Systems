using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Biblioteka
{
    [Serializable]
    public class Datoteka
    {
        public string Naziv { get; set; }              
        public string Autor { get; set; }              
        public string PoslednjaPromena { get; set; }
        public string Sadrzaj { get; set; }
        public override string ToString()
        {
            return $"{Naziv} | {Autor} | poslednja: {PoslednjaPromena}";
        }
    }
}
