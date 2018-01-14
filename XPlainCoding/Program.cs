using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XPlainCoding
{
    class Program
    {
        static void Main(string[] args)
        {
            while (true)
            {
                Console.WriteLine(new Converter().ConvertToCode(Console.ReadLine()));
            }
        }
    }
}
