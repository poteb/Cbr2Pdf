using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cbr2Pdf
{
    public static class ExtensionMethods
    {
        public static IEnumerable<IEnumerable<T>> Split<T>(this IEnumerable<T> array, int size)
        {
            for (var i = 0; i < (float)array.Count() / size; i++)
            {
                yield return array.Skip(i * size).Take(size);
            }
        }
    }
}