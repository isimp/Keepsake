using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using Xunit;

namespace Keepsake.Tests
{
    /// <summary>
    /// The tests that share Keepsake's state, and BepInEx's paths, run one after another.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class ProfileCollection
    {
        public const string Name = "profile";
    }

    /// <summary>
    /// A BepInEx profile in a temporary folder, with a launch's worth of Keepsake state: the
    /// pins file, cfg files, and the settings a mod binds in them.
    /// </summary>
    public sealed class TestProfile : IDisposable
    {
        public readonly string Root;
        public string ConfigDir => Path.Combine(Root, "config");

        private readonly List<ConfigFile> _configs = new List<ConfigFile>();

        /// <param name="root">The BepInEx folder, for a profile laid out like a mod manager's; a new temporary folder otherwise.</param>
        public TestProfile(string root = null)
        {
            Root = root ?? Path.Combine(Path.GetTempPath(), "keepsake-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ConfigDir);

            SetPath(nameof(Paths.BepInExRootPath), Root);
            SetPath(nameof(Paths.ConfigPath), ConfigDir);
            SetPath(nameof(Paths.PluginPath), Path.Combine(Root, "plugins"));
            StartPlugin();
        }

        /// <summary>The plugin as a new game starts it, knowing nothing yet.</summary>
        private void StartPlugin()
        {
            Keeper.Reset();
            FileKeeper.Reset();
            Session.Reset();
            SettingIndex.Reset();
            SettingIndex.Sources = Loaded;
            SettingIndex.IsBindruneLoaded = () => false;
            SettingIndex.FileFound = Keeper.Follow;
            SettingIndex.SettingFound = Session.Note;
            Plugin.Warnings.Clear();
        }

        /// <summary>A new game: the preloader's launch, given when the last game's log ended, then the plugin starting afresh.</summary>
        public LaunchResult Launch(DateTime? now = null, DateTime? logEnd = null)
        {
            var result = Launcher.Run(now ?? DateTime.UtcNow, logEnd, () => false);
            StartPlugin();
            return result;
        }

        /// <summary>The game closing, as the plugin sees it.</summary>
        public void Close(DateTime? at = null) => GameClose.Run("test", at);

        /// <summary>Holds a file open so nothing else can read or write it, as a scanner or an editor may, until disposed.</summary>
        public static IDisposable Lock(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        /// <summary>BepInEx sets its paths once as it starts, with no public way in.</summary>
        private static void SetPath(string name, string value) =>
            typeof(Paths).GetProperty(name)!.GetSetMethod(true)!.Invoke(null, new object[] { value });

        public string PinsPath => Path.Combine(Root, "keepsake.pins");

        public void WritePins(params string[] lines) =>
            File.WriteAllLines(PinsPath, new[] { PinFile.Version }.Concat(lines));

        public string[] PinLines() => !File.Exists(PinsPath) ? new string[0] :
            File.ReadAllLines(PinsPath).Where(l => l.Length > 0 && !l.StartsWith("#")).ToArray();

        public string CfgPath(string file) => Path.Combine(ConfigDir, file);

        /// <summary>A cfg file the way BepInEx writes one, with a type note above each setting.</summary>
        public void WriteCfg(string file, string text) => File.WriteAllText(CfgPath(file), text);

        /// <summary>A mod's config, saved on every change like a plugin's own.</summary>
        public ConfigFile Mod(string file)
        {
            var config = new ConfigFile(CfgPath(file), true);
            _configs.Add(config);
            return config;
        }

        /// <summary>Reads every mod's settings, as the plugin does once all of them have loaded.</summary>
        public void Index() => SettingIndex.Refresh();

        /// <summary>The mods of this profile, each named after its cfg file.</summary>
        private IEnumerable<SettingIndex.LoadedConfig> Loaded() =>
            _configs.Select(c => new SettingIndex.LoadedConfig
            {
                Config = c,
                Name = Path.GetFileNameWithoutExtension(c.ConfigFilePath),
                Version = "1.0.0",
                Guid = "isimp." + Path.GetFileNameWithoutExtension(c.ConfigFilePath),
            }).ToList();

        public Setting Setting(string file, string section, string key) =>
            SettingIndex.Find(PinFile.IdOf(file, section, key)) ?? throw new InvalidOperationException($"{key} is not indexed");

        public void Dispose()
        {
            Keeper.Reset();
            try
            {
                Directory.Delete(Root, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
