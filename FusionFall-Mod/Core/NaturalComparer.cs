using System;
using System.Collections.Generic;

namespace FusionFall_Mod.Core
{
    /// <summary>
    /// Натуральный компаратор строк без учета регистра и с поддержкой чисел.
    /// </summary>
    internal sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new NaturalComparer();

        public int Compare(string? first, string? second)
        {
            if (ReferenceEquals(first, second)) return 0;
            if (first is null) return -1;
            if (second is null) return 1;

            int i = 0;
            int j = 0;
            while (i < first.Length && j < second.Length)
            {
                char cx = first[i];
                char cy = second[j];
                bool dx = char.IsDigit(cx);
                bool dy = char.IsDigit(cy);

                if (dx && dy)
                {
                    long vx = 0;
                    long vy = 0;
                    int si = i;
                    int sj = j;
                    while (i < first.Length && char.IsDigit(first[i]))
                    {
                        vx = vx * 10 + (first[i] - '0');
                        i++;
                    }
                    while (j < second.Length && char.IsDigit(second[j]))
                    {
                        vy = vy * 10 + (second[j] - '0');
                        j++;
                    }
                    int cmpNum = vx.CompareTo(vy);
                    if (cmpNum != 0) return cmpNum;

                    int lenX = i - si;
                    int lenY = j - sj;
                    if (lenX != lenY) return lenX.CompareTo(lenY);
                    continue;
                }

                int cmp = char.ToUpperInvariant(cx).CompareTo(char.ToUpperInvariant(cy));
                if (cmp != 0) return cmp;

                i++;
                j++;
            }
            return (first.Length - i).CompareTo(second.Length - j);
        }
    }
}

