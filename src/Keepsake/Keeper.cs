using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace Keepsake
{
    /// <summary>
    /// The pins while the game runs: pinning, unpinning, setting your value, and following changes
    /// made anywhere else, such as a config manager, so the pin always holds what you last chose.
    ///
    /// Values are compared and stored in their serialized form, the text the cfg file holds. For a
    /// setting a server controls, that is still your own value while the server's is in use, since
    /// ServerSync and Jotunn both keep the local value in the file. So a server handing its value
    /// over never reads as a change of yours.
    ///
    /// Lookups use what is in memory. The file is read again, if it was edited, only at the points
    /// that call Sync: opening the panel, and before every write, so a hand edit is never written
    /// over. Checking the file on every lookup would cost a file system call per listed row.
    /// </summary>
    public static class Keeper
    {
        private static List<Pin> _pins = new List<Pin>();
        private static Dictionary<string, Pin> _byId = new Dictionary<string, Pin>();
        private static string _stamp;
        private static bool _loaded;
        private static bool _unreadable;
        private static readonly HashSet<ConfigFile> Followed = new HashSet<ConfigFile>();

        /// <summary>True while Keepsake writes a setting itself, which is not a change to follow.</summary>
        private static bool _writing;

        /// <summary>Called when a setting changed elsewhere, so an open panel can redraw.</summary>
        public static Action Changed;

        public static IReadOnlyList<Pin> Pins
        {
            get
            {
                if (!_loaded) Sync();
                return _pins;
            }
        }

        public static Pin Find(string id)
        {
            if (!_loaded) Sync();
            return id != null && _byId.TryGetValue(id, out var pin) ? pin : null;
        }

        /// <summary>Reads the pins file again if it was edited since it was last read.</summary>
        public static void Sync()
        {
            var stamp = PinFile.Stamp();
            if (_loaded && (stamp == null || stamp == _stamp)) return;

            var read = PinFile.Read();
            _loaded = true;

            // Unreadable: keep what is in memory, never write over the file, and try again at the
            // next sync.
            _unreadable = read == null;
            if (_unreadable) return;

            _pins = read;
            _stamp = stamp;
            Index();

            if (_keptAtLaunch == null) _keptAtLaunch = new HashSet<string>(_byId.Keys);
        }

        /// <summary>Keeps the setting at the value it has now. Returns why not, or null.</summary>
        public static string Pin(Setting setting)
        {
            Sync();
            if (Find(setting.Id) != null) return null;
            if (setting.LeftToBindrune) return BindruneKeepsKeys;

            var value = setting.Current;
            if (value == null) return "this setting's value could not be read";
            if (!PinFile.Storable(value)) return "this value has line breaks or tabs in it, which Keepsake cannot store";

            var profile = ProfileOf(setting);
            _pins.Add(new Pin
            {
                File = setting.File,
                Section = setting.Section,
                Key = setting.Key,
                Value = value,
                Profile = profile != null && PinFile.Storable(profile) ? profile : value,
            });

            Save();
            return null;
        }

        /// <summary>The settings kept when the pins file was first read, before anything changed them.</summary>
        private static HashSet<string> _keptAtLaunch;

        /// <summary>The profile's value of each setting released this session, for keeping it again.</summary>
        private static readonly Dictionary<string, string> Released = new Dictionary<string, string>();

        /// <summary>
        /// The profile's value of a setting about to be kept, or null when it is not known. It may
        /// already hold a value of yours, set in a config manager before keeping it, so the value
        /// now is not the answer. The value it had at launch is, unless it was kept at launch, when
        /// the preloader had already put your value in; then only a release this session tells.
        /// </summary>
        private static string ProfileOf(Setting setting)
        {
            if (Released.TryGetValue(setting.Id, out var released)) return released;
            if (_keptAtLaunch != null && _keptAtLaunch.Contains(setting.Id)) return null;
            return Session.InitialOf(setting.Id);
        }

        /// <summary>
        /// Stops keeping the setting and puts the profile's value back, so it reads the way it
        /// will after the next sync anyway.
        /// </summary>
        public static void Unpin(string id)
        {
            Sync();
            var pin = Find(id);
            if (pin == null) return;

            _pins.Remove(pin);
            Save();
            if (pin.Profile != null) Released[id] = pin.Profile;

            // A keybind Bindrune looks after is its to set, so releasing one only lets go of it.
            var setting = SettingIndex.Find(id);
            if (setting != null && !setting.LeftToBindrune && pin.Profile != null && setting.Current != pin.Profile)
                Write(setting, pin.Profile);
        }

        public const string BindruneKeepsKeys = "Bindrune keeps your keybinds. Set this key as yours in Bindrune to keep it.";

        /// <summary>Sets your value for a kept setting. Returns why not, or null.</summary>
        public static string SetValue(Setting setting, string text)
        {
            Sync();
            var pin = Find(setting.Id);
            if (pin == null) return "keep this setting first";
            if (setting.LeftToBindrune) return BindruneKeepsKeys;

            text = (text ?? "").Trim();
            try
            {
                // SetSerializedValue swallows a bad value with only a log line, so check first.
                TomlTypeConverter.ConvertToValue(text, setting.Entry.SettingType);
            }
            catch (Exception)
            {
                return $"\"{text}\" is not a valid value for {setting.Key}";
            }

            Write(setting, text);

            // The setting may have adjusted it, for example to fit its allowed range.
            var now = setting.Current ?? text;
            if (!PinFile.Storable(now)) return "this value has line breaks or tabs in it, which Keepsake cannot store";

            pin.Value = now;
            Save();
            return null;
        }

        /// <summary>
        /// Puts every pin back into the settings that are loaded, for pins the preloader could not
        /// place because the cfg file or its line did not exist yet. Also starts following every
        /// cfg file seen so far. Returns how many pins have no loaded setting.
        /// </summary>
        public static int Reconcile()
        {
            Sync();
            SettingIndex.Refresh();

            int missing = 0;
            var dirty = false;

            foreach (var pin in _pins)
            {
                var setting = SettingIndex.Find(pin.Id);
                if (setting == null)
                {
                    missing++;
                    continue;
                }

                if (setting.LeftToBindrune) continue;

                var current = setting.Current;
                if (current == null || current == pin.Value) continue;

                // Whatever the setting holds before your value goes in is the profile's.
                if (pin.Profile != current) dirty = true;
                pin.Profile = current;

                Write(setting, pin.Value);

                // A value edited into the pins file by hand may be one the setting refuses.
                if (setting.Current != pin.Value)
                {
                    Plugin.WarnOnce($"Keepsake: {setting.ModName} [{pin.Section}] {pin.Key} does not accept your value " +
                                    $"{pin.Value}, so it stays at {setting.Current}.");
                    continue;
                }

                Plugin.Log.LogInfo($"Keepsake: kept your value for {setting.ModName} [{pin.Section}] {pin.Key}: {pin.Value} (it was {current}).");
            }

            if (dirty) Save();
            return missing;
        }

        /// <summary>A key Bindrune holds as yours, matched to the setting it belongs to.</summary>
        public sealed class BindruneKeep
        {
            public Setting Setting;
            public string Yours;
            public string Profile;
        }

        /// <summary>
        /// The keys Bindrune holds as yours that nothing applies while it is not loaded: those in
        /// use, for keybind settings that are loaded and not kept here already. Empty while
        /// Bindrune is loaded, since it looks after them itself then. Bindrune names a setting by
        /// its mod's GUID, section and key, so each loaded keybind is named the same way and
        /// matched whole rather than taking Bindrune's names apart.
        /// </summary>
        public static List<BindruneKeep> BindruneKeys()
        {
            var found = new List<BindruneKeep>();
            if (SettingIndex.BindruneLoaded) return found;

            var keys = BindruneLink.ReadKeys().Where(k => k.Active).ToList();
            if (keys.Count == 0) return found;

            var byBindruneId = new Dictionary<string, Setting>();
            foreach (var setting in SettingIndex.All.Where(s => s.IsKeybind))
                byBindruneId[$"cfg:{setting.ModGuid}:{setting.Section}:{setting.Key}"] = setting;

            foreach (var key in keys)
            {
                if (!byBindruneId.TryGetValue(key.Id, out var setting) || Find(setting.Id) != null) continue;

                var yours = AsSetting(key.Yours, setting);
                if (yours == null || !PinFile.Storable(yours)) continue;

                found.Add(new BindruneKeep { Setting = setting, Yours = yours, Profile = AsSetting(key.Profile, setting) });
            }

            return found;
        }

        /// <summary>
        /// Keeps every key BindruneKeys finds, at the key Bindrune held, with the key it recorded as
        /// the profile's. Bindrune's own file is left as it is. Returns how many were taken over.
        /// </summary>
        public static int TakeOverFromBindrune()
        {
            Sync();

            var taken = 0;
            foreach (var keep in BindruneKeys())
            {
                Write(keep.Setting, keep.Yours);

                var value = keep.Setting.Current;
                if (value == null || !PinFile.Storable(value)) continue;

                _pins.Add(new Pin
                {
                    File = keep.Setting.File,
                    Section = keep.Setting.Section,
                    Key = keep.Setting.Key,
                    Value = value,
                    Profile = keep.Profile != null && PinFile.Storable(keep.Profile) ? keep.Profile : value,
                });
                taken++;

                Plugin.Log.LogInfo($"Keepsake: took over {value} for {keep.Setting.ModName} [{keep.Setting.Section}] {keep.Setting.Key} from Bindrune.");
            }

            if (taken > 0) Save();
            return taken;
        }

        /// <summary>
        /// A key in the form Bindrune stores it, which is how BepInEx writes a KeyboardShortcut,
        /// in the form the setting stores it. A setting of a single KeyCode takes the main key.
        /// Null when it cannot be read.
        /// </summary>
        private static string AsSetting(string stored, Setting setting)
        {
            try
            {
                var shortcut = string.IsNullOrEmpty(stored) || stored == "none"
                    ? KeyboardShortcut.Empty
                    : KeyboardShortcut.Deserialize(stored);

                return setting.Entry.SettingType == typeof(KeyCode)
                    ? TomlTypeConverter.ConvertToString(shortcut.MainKey, typeof(KeyCode))
                    : TomlTypeConverter.ConvertToString(shortcut, typeof(KeyboardShortcut));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Starts following changes to a cfg file's settings. Handed to SettingIndex.FileFound.</summary>
        public static void Follow(ConfigFile config)
        {
            if (!Followed.Add(config)) return;
            config.SettingChanged += OnSettingChanged;
        }

        private static void OnSettingChanged(object sender, SettingChangedEventArgs args)
        {
            if (_writing) return;

            try
            {
                var entry = args?.ChangedSetting;
                var file = SettingIndex.FileOf(entry?.ConfigFile);
                if (file == null) return;

                var id = PinFile.IdOf(file, entry.Definition.Section, entry.Definition.Key);
                Session.Touch(id);
                Changed?.Invoke();

                if (Find(id) == null) return;

                // What Bindrune writes to a keybind is its own choice, not one to keep here.
                if (SettingIndex.Find(entry)?.LeftToBindrune == true) return;

                var now = entry.GetSerializedValue();
                if (!PinFile.Storable(now)) return;

                // A write follows, so take in any hand edit first and look the pin up again.
                Sync();
                var pin = Find(id);
                if (pin == null || pin.Value == now) return;

                pin.Value = now;
                Save();
                Plugin.Log.LogInfo($"Keepsake: your value for {pin.File} [{pin.Section}] {pin.Key} is now {now}.");
            }
            catch (Exception ex)
            {
                Plugin.WarnOnce($"Keepsake: following a setting change failed: {ex.Message}", ex);
            }
        }

        private static void Write(Setting setting, string serialized)
        {
            _writing = true;
            try
            {
                setting.Entry.SetSerializedValue(serialized);

                // A file that saves on every change has just written itself. One that does not is
                // saved here, so the cfg file holds the value as well.
                var file = setting.Entry.ConfigFile;
                if (file != null && !file.SaveOnConfigSet) file.Save();
            }
            catch (Exception ex)
            {
                Plugin.WarnOnce($"Keepsake: could not write {setting.ModName} [{setting.Section}] {setting.Key}: {ex.Message}", ex);
            }
            finally
            {
                _writing = false;
            }
        }

        private static void Save()
        {
            Index();
            if (_unreadable)
            {
                Plugin.WarnOnce("Keepsake: keepsake.pins could not be read, so changes are not saved to it until it can.");
                return;
            }

            PinFile.Write(_pins);
            _stamp = PinFile.Stamp();
        }

        private static void Index()
        {
            var byId = new Dictionary<string, Pin>();
            foreach (var pin in _pins) byId[pin.Id] = pin;
            _byId = byId;
        }
    }
}
