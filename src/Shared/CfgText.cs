using System.IO;
using System.Text;

namespace Keepsake
{
    /// <summary>
    /// A cfg file as text, read the way BepInEx's ConfigFile.Reload reads it: lines are trimmed,
    /// # starts a comment, [name] starts a section, and a setting is everything before the first
    /// = as its key and everything after it as its value. When a setting appears twice, the last
    /// one is the one BepInEx keeps, so it is the one found here too.
    ///
    /// Only the one line of each setting is replaced. Comments, order, line endings and a byte
    /// order mark stay as they were.
    /// </summary>
    public sealed class CfgText
    {
        private readonly string[] _lines;
        private readonly string _newline;
        private readonly bool _byteOrderMark;
        private bool _changed;

        private CfgText(string text, bool byteOrderMark = false)
        {
            _newline = text.Contains("\r\n") ? "\r\n" : "\n";
            _lines = text.Replace("\r\n", "\n").Split('\n');
            _byteOrderMark = byteOrderMark;
        }

        public static CfgText Load(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            return new CfgText(new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)), bom);
        }

        /// <summary>A cfg file from text already in hand.</summary>
        public static CfgText Parse(string text) => new CfgText(text);

        /// <summary>The file as it would be saved.</summary>
        public string Text => string.Join(_newline, _lines);

        public bool Changed => _changed;

        /// <summary>The value the file holds for a setting, or false when it has no such line.</summary>
        public bool TryGet(string section, string key, out string value)
        {
            var index = Find(section, key, out value);
            return index >= 0;
        }

        /// <summary>
        /// The type of a setting as BepInEx notes it in the comments above the line, such as
        /// "KeyCode", or null when the file has no such line or no such note.
        /// </summary>
        public string TypeOf(string section, string key)
        {
            const string note = "# Setting type:";

            var index = Find(section, key, out _);
            for (var i = index - 1; i >= 0; i--)
            {
                var text = _lines[i].Trim();
                if (!text.StartsWith("#")) break;
                if (text.StartsWith(note)) return text.Substring(note.Length).Trim();
            }

            return null;
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
        public void Save(string path) => PinFile.ReplaceText(path, Text, _byteOrderMark);

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
