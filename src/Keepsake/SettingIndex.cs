using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace Keepsake
{
    /// <summary>What decides a setting's value while you are connected to a server.</summary>
    public enum ServerControl
    {
        None,

        /// <summary>
        /// ServerSync hands the server's value over while you are connected to a server that runs
        /// the mod. Your own value stays in the cfg file throughout and is back when you leave.
        /// </summary>
        ServerSync,

        /// <summary>Jotunn's admin-only settings, which behave the same way.</summary>
        Jotunn,
    }

    /// <summary>A mod with settings: its name, version, cfg file and how many settings it has.</summary>
    public sealed class ModInfo
    {
        public string Name;
        public string Version;
        public string File;
        public int Count;
    }

    /// <summary>One setting of a loaded mod, as the panel lists it.</summary>
    public sealed class Setting
    {
        public ConfigEntryBase Entry;
        public string File;
        public string ModName;
        public string ModVersion;
        public string ModGuid;
        public string Id;
        public ServerControl Server;

        public string Section => Entry.Definition.Section;
        public string Key => Entry.Definition.Key;
        public string Description => Entry.Description?.Description ?? "";

        public bool IsKeybind => Entry.SettingType == typeof(KeyCode) || Entry.SettingType == typeof(KeyboardShortcut);

        /// <summary>A keybind while Bindrune is loaded, which Keepsake leaves to it. See BindruneLink.</summary>
        public bool LeftToBindrune => IsKeybind && SettingIndex.BindruneLoaded;

        /// <summary>Everything the search looks in, lowercased once.</summary>
        public string SearchText;

        private string _default;
        private bool _defaultRead;

        /// <summary>The mod's default, written the way the cfg file stores it. Null if unreadable.</summary>
        public string Default
        {
            get
            {
                if (_defaultRead) return _default;
                _defaultRead = true;
                try
                {
                    _default = TomlTypeConverter.ConvertToString(Entry.DefaultValue, Entry.SettingType);
                }
                catch (Exception)
                {
                    _default = null;
                }
                return _default;
            }
        }

        /// <summary>The value as the cfg file stores it, or null if it cannot be read.</summary>
        public string Current
        {
            get
            {
                try
                {
                    return Entry.GetSerializedValue();
                }
                catch (Exception ex)
                {
                    Plugin.WarnOnce($"Keepsake: could not read {ModName} [{Section}] {Key}: {ex.Message}", ex);
                    return null;
                }
            }
        }
    }

    /// <summary>
    /// Every setting of every loaded plugin whose cfg file sits under BepInEx/config. Read again on
    /// each refresh, since mods bind settings at different times, some only when a world loads.
    /// A setting seen before keeps its object, so the text the search looks in is built once.
    /// </summary>
    public static class SettingIndex
    {
        private static readonly Dictionary<string, Setting> ById = new Dictionary<string, Setting>();
        private static readonly Dictionary<ConfigEntryBase, Setting> ByEntry = new Dictionary<ConfigEntryBase, Setting>();
        private static readonly Dictionary<ConfigFile, string> Files = new Dictionary<ConfigFile, string>();

        public static List<Setting> All { get; private set; } = new List<Setting>();

        /// <summary>Every mod with settings, by name.</summary>
        public static List<string> Mods { get; private set; } = new List<string>();

        /// <summary>Called for each cfg file the first time a refresh sees it.</summary>
        public static Action<ConfigFile> FileFound;

        /// <summary>Called for each setting the first time a refresh sees it.</summary>
        public static Action<Setting> SettingFound;

        public static Setting Find(string id) => id != null && ById.TryGetValue(id, out var setting) ? setting : null;

        public static Setting Find(ConfigEntryBase entry) => entry != null && ByEntry.TryGetValue(entry, out var setting) ? setting : null;

        /// <summary>Whether Bindrune is loaded this session.</summary>
        public static bool BindruneLoaded =>
            Chainloader.PluginInfos.TryGetValue(BindruneLink.Guid, out var info) && info?.Instance != null;

        /// <summary>The file a loaded config lives in, relative to BepInEx/config, or null.</summary>
        public static string FileOf(ConfigFile config) =>
            config != null && Files.TryGetValue(config, out var file) ? file : null;

        public static void Refresh()
        {
            var all = new List<Setting>();
            ById.Clear();

            foreach (var info in Chainloader.PluginInfos.Values.ToList())
            {
                var config = info?.Instance?.Config;
                if (config == null) continue;

                if (!Files.TryGetValue(config, out var file))
                {
                    file = PinFile.Relative(config.ConfigFilePath);
                    Files[config] = file;
                    if (file != null) FileFound?.Invoke(config);
                }
                if (file == null) continue;

                var modName = info.Metadata?.Name ?? file;

                KeyValuePair<ConfigDefinition, ConfigEntryBase>[] entries;
                try
                {
                    entries = config.ToArray();
                }
                catch (Exception ex)
                {
                    // A mod binding a setting on another thread mid-read; the next refresh sees it.
                    Plugin.WarnOnce($"Keepsake: could not read the settings of {modName}: {ex.Message}", ex);
                    continue;
                }

                foreach (var pair in entries)
                {
                    var entry = pair.Value;
                    if (entry == null) continue;

                    if (!ByEntry.TryGetValue(entry, out var setting))
                    {
                        setting = new Setting
                        {
                            Entry = entry,
                            File = file,
                            ModName = modName,
                            ModVersion = info.Metadata?.Version?.ToString() ?? "",
                            ModGuid = info.Metadata?.GUID ?? "",
                            Id = PinFile.IdOf(file, entry.Definition.Section, entry.Definition.Key),
                            Server = ServerOf(entry),
                        };
                        setting.SearchText = (modName + " " + file + " " + setting.Section + " " + setting.Key + " " +
                                              setting.Description).ToLowerInvariant();

                        ByEntry[entry] = setting;
                        SettingFound?.Invoke(setting);
                    }

                    if (ById.ContainsKey(setting.Id)) continue;
                    ById[setting.Id] = setting;
                    all.Add(setting);
                }
            }

            All = all
                .OrderBy(s => s.ModName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Section, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Mods = All.Select(s => s.ModName).Distinct().ToList();

            var infos = new Dictionary<string, ModInfo>();
            foreach (var setting in All)
            {
                if (!infos.TryGetValue(setting.ModName, out var info))
                {
                    info = new ModInfo { Name = setting.ModName, Version = setting.ModVersion, File = setting.File };
                    infos[setting.ModName] = info;
                }
                info.Count++;
            }
            _mods = infos;
        }

        private static Dictionary<string, ModInfo> _mods = new Dictionary<string, ModInfo>();

        /// <summary>A mod by name, as the header over its settings describes it, or null.</summary>
        public static ModInfo Mod(string name) => name != null && _mods.TryGetValue(name, out var info) ? info : null;

        /// <summary>
        /// Reads the sync libraries' own switches rather than the "Synced with Server" text in a
        /// description, which also matches "Not Synced with Server". ServerSync adds its entry
        /// object to the description's tags and turns SynchronizedConfig off for local settings;
        /// Jotunn marks its synced settings IsAdminOnly on a ConfigurationManagerAttributes tag.
        /// Both copies are compiled into each mod, so they are matched by name.
        /// </summary>
        private static ServerControl ServerOf(ConfigEntryBase entry)
        {
            var tags = entry.Description?.Tags;
            if (tags == null) return ServerControl.None;

            foreach (var tag in tags)
            {
                if (tag == null) continue;
                var type = tag.GetType();

                if (DerivesFrom(type, "OwnConfigEntryBase"))
                {
                    if (ReadBool(tag, type, "SynchronizedConfig") == true) return ServerControl.ServerSync;
                    continue;
                }

                if (type.Name == "ConfigurationManagerAttributes" && ReadBool(tag, type, "IsAdminOnly") == true)
                    return ServerControl.Jotunn;
            }

            return ServerControl.None;
        }

        private static bool DerivesFrom(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
                if (t.Name == name) return true;
            return false;
        }

        private static bool? ReadBool(object target, Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                for (var t = type; t != null; t = t.BaseType)
                {
                    var field = t.GetField(name, flags | BindingFlags.DeclaredOnly);
                    if (field != null && field.FieldType == typeof(bool)) return (bool)field.GetValue(target);

                    var property = t.GetProperty(name, flags | BindingFlags.DeclaredOnly);
                    if (property != null && property.PropertyType == typeof(bool)) return (bool)property.GetValue(target, null);
                }
            }
            catch (Exception ex)
            {
                Plugin.WarnOnce($"Keepsake: could not read {name} on {type.FullName}: {ex.Message}", ex);
            }

            return null;
        }
    }
}
