using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;

namespace Keepsake
{
    /// <summary>A kept file left as it is at launch, waiting for you to choose between it and your copy.</summary>
    public sealed class WaitingFile
    {
        /// <summary>Relative to BepInEx/config, with forward slashes.</summary>
        public string Path;

        /// <summary>Whether you chose your copy, which goes back in at the next launch.</summary>
        public bool PutBack;
    }

    /// <summary>What keepsake.session holds: when the game last started and closed with Keepsake, and the kept files waiting for an answer.</summary>
    public sealed class SessionState
    {
        /// <summary>When the preloader last ran, in UTC.</summary>
        public DateTime? Started;

        /// <summary>When the plugin last saw the game close, in UTC.</summary>
        public DateTime? Closed;

        /// <summary>What saw the game close: which of the plugin's hooks ran last. Written for the log, never decided on.</summary>
        public string ClosedBy;

        public readonly List<WaitingFile> Waiting = new List<WaitingFile>();

        public WaitingFile WaitingFor(string path) =>
            Waiting.FirstOrDefault(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// BepInEx/keepsake.session: when the game last started and closed with Keepsake, and the kept
    /// files waiting for an answer in the panel. The preloader reads it at launch to tell a file a
    /// mod wrote from one a profile sync replaced, and writes the start. The plugin writes the
    /// close, and the answers. See KeptFiles.Settle.
    ///
    /// Outside config, with an extension no profile sync picks up, like the other Keepsake files.
    /// </summary>
    public static class SessionFile
    {
        public const string Version = "# keepsake session v1";

        private const string VersionPrefix = "# keepsake session v";

        private static readonly string[] Header =
        {
            Version,
            "# When the game last started and closed with Keepsake, in UTC, and under [files] the kept",
            "# files in BepInEx/config waiting for an answer in the panel: the path, then ask, or put back",
            "# when your copy goes back in at the next launch. Tab separated.",
        };

        public static string FilePath => Path.Combine(Paths.BepInExRootPath, "keepsake.session");

        /// <summary>
        /// What the file holds. A missing file holds nothing. Null for a file that cannot be read
        /// or is of another version, which is then never written over.
        /// </summary>
        public static SessionState Read()
        {
            string[] lines;
            try
            {
                if (!File.Exists(FilePath)) return new SessionState();
                lines = File.ReadAllLines(FilePath);
            }
            catch (Exception ex)
            {
                PinFile.Log?.LogWarning($"Keepsake: could not read {Path.GetFileName(FilePath)}: {ex.Message}");
                return null;
            }

            var state = Parse(lines);
            if (state == null)
                PinFile.Log?.LogWarning($"Keepsake: {Path.GetFileName(FilePath)} was written by a newer Keepsake, so it is left as it is.");
            return state;
        }

        /// <summary>What the lines of the file hold, or null for a file of another version.</summary>
        public static SessionState Parse(IEnumerable<string> lines)
        {
            var state = new SessionState();
            string section = null;

            foreach (var raw in lines)
            {
                if (raw.StartsWith(VersionPrefix) && raw.Trim() != Version) return null;
                if (raw.Trim().Length == 0 || raw.StartsWith("#")) continue;

                var line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line;
                    continue;
                }

                var parts = raw.Split('\t').Select(p => p.Trim()).ToArray();
                if (section == null)
                {
                    if (parts.Length < 2) continue;
                    if (parts[0] == "started") state.Started = TimeOf(parts[1]);
                    else if (parts[0] == "closed")
                    {
                        state.Closed = TimeOf(parts[1]);
                        state.ClosedBy = parts.Length > 2 ? parts[2] : null;
                    }
                }
                else if (section == "[files]")
                {
                    var path = KeptFiles.Normalise(parts[0]);
                    if (path == null || state.WaitingFor(path) != null) continue;
                    state.Waiting.Add(new WaitingFile { Path = path, PutBack = parts.Length > 1 && parts[1] == "put back" });
                }
            }

            return state;
        }

        public static string[] Format(SessionState state)
        {
            var lines = new List<string>(Header);
            if (state.Started != null) lines.Add("started\t" + TextOf(state.Started.Value));
            if (state.Closed != null) lines.Add("closed\t" + TextOf(state.Closed.Value) + (state.ClosedBy != null ? "\t" + state.ClosedBy : ""));

            lines.Add("");
            lines.Add("[files]");
            lines.AddRange(state.Waiting
                .OrderBy(w => w.Path, StringComparer.OrdinalIgnoreCase)
                .Select(w => w.Path + "\t" + (w.PutBack ? "put back" : "ask")));
            return lines.ToArray();
        }

        public static bool Write(SessionState state)
        {
            try
            {
                PinFile.ReplaceText(FilePath, string.Join(Environment.NewLine, Format(state)) + Environment.NewLine);
                return true;
            }
            catch (Exception ex)
            {
                PinFile.Log?.LogError($"Keepsake: could not save {Path.GetFileName(FilePath)}: {ex.Message}");
                return false;
            }
        }

        private static string TextOf(DateTime utc) => utc.ToString("o", CultureInfo.InvariantCulture);

        private static DateTime? TimeOf(string text) =>
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time)
                ? time
                : (DateTime?)null;

        /// <summary>
        /// When the last game of this profile stopped writing its BepInEx log, in UTC, or null when
        /// there is no log. Only meaningful before BepInEx opens the log for this launch, which it
        /// does after every patcher has run, so the preloader can ask and the plugin cannot. Every
        /// game of the profile writes it, with Keepsake or without, and a profile sync never does.
        /// </summary>
        public static DateTime? LogEnd()
        {
            DateTime? end = null;
            try
            {
                // BepInEx moves on to LogOutput.log.1 and up when the log is held by another game.
                foreach (var log in Directory.GetFiles(Paths.BepInExRootPath, "LogOutput.log*"))
                {
                    var name = Path.GetFileName(log);
                    if (name != "LogOutput.log" && !name.StartsWith("LogOutput.log.")) continue;
                    var time = File.GetLastWriteTimeUtc(log);
                    if (end == null || time > end) end = time;
                }
            }
            catch (Exception ex)
            {
                PinFile.Log?.LogWarning($"Keepsake: could not read when the last game ended: {ex.Message}");
            }
            return end;
        }
    }
}
