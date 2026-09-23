using System.Collections.Generic;
using System.Linq;

namespace Keepsake
{
    /// <summary>
    /// Settings changed while the game runs, by anything other than Keepsake: a config manager,
    /// the mod itself, a hand edit the mod reloaded. Each setting's value is noted the first time
    /// it is seen, and a setting counts as changed while it differs from that.
    ///
    /// Compared in the serialized form, so for a setting a server controls the value compared is
    /// your own, and a server handing its value over does not count as a change.
    /// </summary>
    public static class Session
    {
        private static readonly Dictionary<string, string> Initial = new Dictionary<string, string>();
        private static readonly HashSet<string> Touched = new HashSet<string>();

        /// <summary>Handed to SettingIndex.SettingFound.</summary>
        public static void Note(Setting setting)
        {
            if (!Initial.ContainsKey(setting.Id)) Initial[setting.Id] = setting.Current;
        }

        public static void Touch(string id) => Touched.Add(id);

        /// <summary>The value a setting had when it was first seen, or null.</summary>
        public static string InitialOf(string id) => Initial.TryGetValue(id, out var value) ? value : null;

        public static bool IsChanged(Setting setting) =>
            Touched.Contains(setting.Id) && Initial.TryGetValue(setting.Id, out var initial) && setting.Current != initial;

        public static List<Setting> Changed() =>
            Touched.Select(SettingIndex.Find).Where(s => s != null && IsChanged(s)).ToList();
    }
}
