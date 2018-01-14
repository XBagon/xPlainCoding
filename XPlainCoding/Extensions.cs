using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XPlainCoding
{
    static class Extensions
    {
        public static string ReplaceFirstOccurrence(this string Source, string Find, string Replace)
        {
            int Place = Source.IndexOf(Find);
            string result = Source.Remove(Place, Find.Length).Insert(Place, Replace);
            return result;
        }

        public static T[] Plus<T>(this T[] array0, T[] array1)
        {
            var array = new T[array0.Length + array1.Length];
            for (int i = 0; i < array0.Length; i++)
            {
                array[i] = array0[i];
            }
            for (int i = 0; i < array1.Length; i++)
            {
                array[i+array0.Length] = array1[i];
            }
            return array;
        }
    }
}
