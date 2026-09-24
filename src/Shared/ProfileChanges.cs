using System;
using System.Collections.Generic;
using System.Linq;

namespace Keepsake
{
    /// <summary>A kept setting whose profile's value changed since the last launch.</summary>
    public sealed class ProfileChange
    {
        public string Id;

        /// <summary>The profile's value recorded before, written the way the cfg file stores it.</summary>
        public string From;

        /// <summary>The profile's value now.</summary>
        public string To;
    }

    /// <summary>
    /// Hands the profile changes the preloader found to the plugin. Both run in the same
    /// application domain, one before the other, but each carries its own copy of this code, so
    /// the list travels as plain strings in the domain's data rather than in a static field. It
    /// lasts for this session only; nothing about it is written to disk.
    /// </summary>
    public static class ProfileChanges
    {
        private const string Slot = "isimp.Keepsake.ProfileChanges";

        /// <summary>
        /// Adds to what is handed over already. A second copy of the preloader, installed twice by
        /// hand or by a mod manager, runs after the first has put your values back and finds
        /// nothing, which must not wipe out what the first one found.
        /// </summary>
        public static void Hand(IEnumerable<ProfileChange> changes)
        {
            var list = changes.Select(c => new[] { c.Id, c.From, c.To }).ToArray();
            if (list.Length == 0) return;

            var before = AppDomain.CurrentDomain.GetData(Slot) as string[][];
            AppDomain.CurrentDomain.SetData(Slot, before != null ? before.Concat(list).ToArray() : list);
        }

        /// <summary>The changes handed over, once: the slot is emptied as it is read.</summary>
        public static List<ProfileChange> Take()
        {
            var list = AppDomain.CurrentDomain.GetData(Slot) as string[][];
            AppDomain.CurrentDomain.SetData(Slot, null);
            if (list == null) return new List<ProfileChange>();

            return list
                .Where(c => c != null && c.Length == 3 && c[0] != null)
                .Select(c => new ProfileChange { Id = c[0], From = c[1], To = c[2] })
                .ToList();
        }
    }
}
