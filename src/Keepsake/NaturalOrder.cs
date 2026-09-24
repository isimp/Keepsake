using System;
using System.Collections.Generic;

namespace Keepsake
{
    /// <summary>
    /// Orders text the way people count: a run of digits compares as the number it spells, so
    /// "2" comes before "10", and letters compare without regard to case. Used for every list the
    /// panel shows, whose names are often numbered, such as sections like "1 - Settings".
    /// </summary>
    public sealed class NaturalOrder : IComparer<string>
    {
        public static readonly NaturalOrder IgnoreCase = new NaturalOrder();

        public int Compare(string a, string b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            var i = 0;
            var j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (IsDigit(a[i]) && IsDigit(b[j]))
                {
                    var startA = i;
                    var startB = j;
                    while (i < a.Length && IsDigit(a[i])) i++;
                    while (j < b.Length && IsDigit(b[j])) j++;

                    // Leading zeros say nothing about the size; after them, more digits is larger,
                    // and the same number of digits compares digit by digit. No overflow either way.
                    var numberA = a.Substring(startA, i - startA).TrimStart('0');
                    var numberB = b.Substring(startB, j - startB).TrimStart('0');
                    if (numberA.Length != numberB.Length) return numberA.Length.CompareTo(numberB.Length);
                    var digits = string.CompareOrdinal(numberA, numberB);
                    if (digits != 0) return digits;
                    continue;
                }

                var ca = char.ToUpperInvariant(a[i]);
                var cb = char.ToUpperInvariant(b[j]);
                if (ca != cb) return ca.CompareTo(cb);
                i++;
                j++;
            }

            var rest = (a.Length - i).CompareTo(b.Length - j);
            if (rest != 0) return rest;

            // Equal as far as counting goes, such as "01" and "1", or "a" and "A": settled the
            // plain way, so the order never depends on which came first.
            var plain = StringComparer.OrdinalIgnoreCase.Compare(a, b);
            return plain != 0 ? plain : string.CompareOrdinal(a, b);
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';
    }
}
