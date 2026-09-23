using System.IO;

namespace Keepsake
{
    /// <summary>
    /// A cfg file as text, read the way BepInEx's ConfigFile.Reload reads it: lines are trimmed,
    /// # starts a comment, [name] starts a section, and a setting is everything before the first
    /// = as its key and everything after it as its value. When a setting appears twice, the last
    /// one is the one BepInEx keeps, so it is the one found here too.
    ///
    /// Only the one line of each setting is replaced. Comments, order and line endings stay as
    /// they were.
    /// </summary>
    public sealed class CfgText
    {
        private readonly string[] _lines;
        private readonly string _newline;
        private bool _changed;

        private CfgText(string text)
        {
            _newline = text.Contains("\r\n") ? "\r\n" : "\n";
            _lines = text.Replace("\r\n", "\n").Split('\n');
        }

        public static CfgText Load(string path) => new CfgText(File.ReadAllText(path));

        public bool Changed => _changed;

        /// <summary>The value the file holds for a setting, or false when it has no such line.</summary>
        public bool TryGet(string section, string key, out string value)
        {
            var index = Find(section, key, out value);
            return index >= 0;
        }

        /// <summary>Sets a setting's value. False when the file has no line for it.</summary>
        public bool Set(string section, string key, string value)
        {
            var index = Find(section, key, out var current);
            if (index < 0) return false;
            if (current == value) return true;

            _lines[index] = key + " = " + value;
            _changed = true;
            return true;
        }

        /// <summary>Writes the file back, swapped in whole so a crash cannot leave half a file.</summary>
        public void Save(string path) => PinFile.ReplaceText(path, string.Join(_newline, _lines));

        private int Find(string section, string key, out string value)
        {
            value = null;
            var found = -1;
            var current = string.Empty;

            for (var i = 0; i < _lines.Length; i++)
            {
                var text = _lines[i].Trim();
                if (text.StartsWith("#")) continue;

                if (text.StartsWith("[") && text.EndsWith("]"))
                {
                    current = text.Substring(1, text.Length - 2);
                    continue;
                }

                if (current != section) continue;

                var parts = text.Split(new[] { '=' }, 2);
                if (parts.Length != 2 || parts[0].Trim() != key) continue;

                found = i;
                value = parts[1].Trim();
            }

            return found;
        }
    }
}
