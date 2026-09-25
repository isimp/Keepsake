using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    /// Changes followed from elsewhere are written together once they stop coming (see Tick).
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

        /// <summary>Called with a setting's id when it changed elsewhere, so an open panel can redraw.</summary>
        public static Action<string> Changed;

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

            // Values followed since the last write are newer than the file.
            foreach (var id in Pending.Keys.ToList())
            {
                if (_byId.TryGetValue(id, out var pin)) pin.Value = Pending[id];
                else Pending.Remove(id);
            }

            if (_keptAtLaunch == null) _keptAtLaunch = new HashSet<string>(_byId.Keys);
        }

        /// <summary>Keeps the setting at the value it has now. Returns why not, or null.</summary>
        public static string Pin(Setting setting)
        {
            Sync();
            if (Find(setting.Id) != null) return null;

            var problem = Add(setting);
            if (problem != null) return problem;
            if (Save()) return null;

            Remove(setting.Id);
            return CannotSave;
        }

        /// <summary>Why an action changed nothing, when keepsake.pins could not be read or written.</summary>
        public const string CannotSave = "keepsake.pins could not be read or saved, so nothing changed; see the log";

        /// <summary>
        /// Keeps every one of these settings at the value it has now, in one write. Those already
        /// kept, and those that cannot be, are passed over. Returns how many were kept.
        /// </summary>
        public static int PinAll(IEnumerable<Setting> settings)
        {
            Sync();

            var added = new List<string>();
            foreach (var setting in settings)
                if (Find(setting.Id) == null && Add(setting) == null) added.Add(setting.Id);

            if (added.Count == 0 || Save()) return added.Count;

            foreach (var id in added) Remove(id);
            return 0;
        }

        /// <summary>Takes a pin out of memory again, after its keeping could not be saved.</summary>
        private static void Remove(string id)
        {
            if (!_byId.TryGetValue(id, out var pin)) return;
            _pins.Remove(pin);
            Index();
        }

        /// <summary>Whether Keep would take the setting: loaded, not kept, not Bindrune's, and storable.</summary>
        public static bool CanPin(Setting setting)
        {
            if (setting == null || Find(setting.Id) != null || setting.LeftToBindrune) return false;
            return PinFile.Storable(setting.Current);
        }

        /// <summary>Adds a pin in memory, leaving the saving to the caller. Returns why not, or null.</summary>
        private static string Add(Setting setting)
        {
            if (Find(setting.Id) != null) return null;
            if (setting.LeftToBindrune) return BindruneKeepsKeys;

            var value = setting.Current;
            if (value == null) return "this setting's value could not be read";
            if (!PinFile.Storable(value)) return "this value has line breaks or tabs in it, which Keepsake cannot store";

            var profile = ProfileOf(setting);
            var pin = new Pin
            {
                File = setting.File,
                Section = setting.Section,
                Key = setting.Key,
                Value = value,
                Profile = profile != null && PinFile.Storable(profile) ? profile : value,
            };
            _pins.Add(pin);
            _byId[pin.Id] = pin;
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
        public static string Unpin(string id) => UnpinAll(new[] { id }) > 0 || Find(id) == null ? null : CannotSave;

        /// <summary>Releases every one of these settings, in one write. Returns how many were kept, 0 when the release could not be saved.</summary>
        public static int UnpinAll(IEnumerable<string> ids)
        {
            Sync();

            var released = new List<Pin>();
            foreach (var id in ids)
            {
                var pin = Find(id);
                if (pin == null) continue;

                _pins.Remove(pin);
                _byId.Remove(id);
                released.Add(pin);
            }

            if (released.Count == 0) return 0;
            if (!Save())
            {
                _pins.AddRange(released);
                Index();
                return 0;
            }

            var answered = false;
            foreach (var pin in released)
            {
                if (pin.Profile != null) Released[pin.Id] = pin.Profile;
                answered |= Changes.Waiting.RemoveAll(c => c.Id == pin.Id) > 0;
                answered |= Changes.Quiet.RemoveAll(q => q.Id == pin.Id) > 0;

                // A keybind Bindrune looks after is its to set, so releasing one only lets go of
                // it. A setting no mod has bound has nothing to write to; its cfg file keeps your
                // value until the next profile sync.
                var setting = SettingIndex.Find(pin.Id);
                if (setting != null && !setting.LeftToBindrune && pin.Profile != null && setting.Current != pin.Profile)
                    Write(setting, pin.Profile);
            }

            if (answered) SaveChanges();
            return released.Count;
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

            if (!PinFile.Storable(text)) return "this value has line breaks or tabs in it, which Keepsake cannot store";

            // Your value is saved before the setting takes it, so a value that cannot be saved is
            // not given to the mod either.
            var before = pin.Value;
            pin.Value = text;
            if (!Save())
            {
                pin.Value = before;
                return CannotSave;
            }

            Write(setting, text);

            // The setting may have adjusted it, for example to fit its allowed range.
            var now = setting.Current ?? text;
            if (now != text && PinFile.Storable(now))
            {
                pin.Value = now;
                Save();
            }

            // Choosing a value here is an answer to a profile change as much as Keep mine is.
            Answered(setting.Id);
            return null;
        }

        // ---------- the profile's changes ----------

        private static ChangeState _changes;
        private static bool _changesUnreadable;

        /// <summary>
        /// Kept settings whose profile's value changed while you kept your own, waiting for an
        /// answer, found by the preloader or by Reconcile for a value it had to put in later; and
        /// the settings made quiet, whose changes are not asked about. Read from keepsake.changes
        /// once, and written back whenever either changes.
        /// </summary>
        private static ChangeState Changes
        {
            get
            {
                if (_changes != null) return _changes;
                var read = ProfileChanges.Read();
                _changesUnreadable = read == null;
                _changes = read ?? new ChangeState();
                return _changes;
            }
        }

        /// <summary>What the profile changed about a kept setting while you kept yours, or null.</summary>
        public static ProfileChange ProfileChangeOf(string id)
        {
            if (id == null || Find(id) == null) return null;
            return Changes.Waiting.FirstOrDefault(c => c.Id == id);
        }

        /// <summary>The ids of kept settings whose profile's value changed while you kept yours.</summary>
        public static List<string> ProfileChanged() => Changes.Waiting.Select(c => c.Id).Where(id => Find(id) != null).ToList();

        /// <summary>Whether the profile's changes to a kept setting are recorded without asking.</summary>
        public static bool IsQuiet(string id) => id != null && Changes.IsQuiet(id);

        /// <summary>
        /// Stops or starts asking about the profile's changes to a kept setting. The profile's value
        /// is recorded either way, so releasing it always puts back the latest one. Making it quiet
        /// also answers a change waiting for it, keeping your value.
        /// </summary>
        public static void SetQuiet(string id, bool quiet)
        {
            var pin = Find(id);
            if (pin == null || IsQuiet(id) == quiet) return;

            if (quiet)
            {
                Changes.Quiet.Add(QuietSetting.Of(pin));
                Changes.Waiting.RemoveAll(c => c.Id == id);
            }
            else Changes.Quiet.RemoveAll(q => q.Id == id);

            SaveChanges();
        }

        private static void NoteChange(Pin pin, string from, string to)
        {
            if (ProfileChanges.Merge(Changes, new[] { ProfileChange.Of(pin, from, to) })) SaveChanges();
        }

        /// <summary>A change answered: it is taken out, and stays out at the next launch.</summary>
        private static void Answered(string id)
        {
            if (Changes.Waiting.RemoveAll(c => c.Id == id) > 0) SaveChanges();
        }

        private static void SaveChanges()
        {
            // Settings still kept are told by the pins in memory, which are not to be trusted while
            // the pins file cannot be read: every change would look like one of a released setting.
            if (_changesUnreadable || _unreadable) return;
            ProfileChanges.KeepOnly(Changes, _byId.Keys);
            ProfileChanges.Write(Changes);
            SpareCopy.Follow();
        }

        /// <summary>Takes the profile's new value as yours, and the setting stays kept.</summary>
        public static string UseProfiles(Setting setting)
        {
            var pin = Find(setting.Id);
            if (pin?.Profile == null) return "the profile's value is not known";
            return SetValue(setting, pin.Profile);
        }

        /// <summary>Stays with your value, and the setting no longer shows as changed by the profile.</summary>
        public static void KeepMine(string id) => Answered(id);

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
                if (pin.Profile != current)
                {
                    if (pin.Profile != null) NoteChange(pin, pin.Profile, current);
                    dirty = true;
                }
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
            if (SettingIndex.BindruneLoaded) return new List<BindruneKeep>();

            var keys = BindruneLink.ReadKeys();
            if (keys.Count == 0) return new List<BindruneKeep>();

            var slots = SettingIndex.All
                .Where(s => s.IsKeybind && Find(s.Id) == null)
                .Select(s => new KeybindSlot<Setting>
                {
                    BindruneId = Keepsake.BindruneKeys.IdOf(s.ModGuid, s.Section, s.Key),
                    IsKeyCode = s.Entry.SettingType == typeof(KeyCode),
                    Setting = s,
                });

            return Keepsake.BindruneKeys.Match(keys, slots)
                .Where(m => PinFile.Storable(m.Yours))
                .Select(m => new BindruneKeep { Setting = m.Setting, Yours = m.Yours, Profile = m.Profile })
                .ToList();
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

        /// <summary>Starts following changes to a cfg file's settings. Handed to SettingIndex.FileFound.</summary>
        public static void Follow(ConfigFile config)
        {
            if (!Followed.Add(config)) return;
            config.SettingChanged += OnSettingChanged;
        }

        /// <summary>The game's main thread, set as the plugin starts. Zero, as in the tests, counts every thread as main.</summary>
        public static int MainThread;

        /// <summary>Setting changes raised on another thread, waiting for the main thread. See OnSettingChanged.</summary>
        private static readonly ConcurrentQueue<ConfigEntryBase> OffThread = new ConcurrentQueue<ConfigEntryBase>();

        private static void OnSettingChanged(object sender, SettingChangedEventArgs args)
        {
            if (_writing) return;

            var entry = args?.ChangedSetting;
            if (entry == null) return;

            // A mod that reloads its cfg file from a file watcher of its own, without handing the
            // event to the main thread, changes its settings on the watcher's thread. What follows
            // touches lists the panel reads and asks the game for the time, so it waits for the
            // main thread, which takes it in at the next Tick.
            if (MainThread != 0 && Thread.CurrentThread.ManagedThreadId != MainThread)
            {
                OffThread.Enqueue(entry);
                return;
            }

            FollowChange(entry);
        }

        private static void FollowChange(ConfigEntryBase entry)
        {
            try
            {
                // Settings seen before carry their id; building it again for every change would
                // cost a string for a mod that writes a setting every frame.
                var id = SettingIndex.Find(entry)?.Id;
                if (id == null)
                {
                    var file = SettingIndex.FileOf(entry.ConfigFile);
                    if (file == null) return;
                    id = PinFile.IdOf(file, entry.Definition.Section, entry.Definition.Key);
                }

                Session.Touch(id);
                Changed?.Invoke(id);

                var pin = Find(id);
                if (pin == null) return;

                // What Bindrune writes to a keybind is its own choice, not one to keep here.
                if (SettingIndex.Find(entry)?.LeftToBindrune == true) return;

                var now = entry.GetSerializedValue();
                if (!PinFile.Storable(now) || pin.Value == now) return;

                // A config manager's slider sets its setting in every frame it moves, so the value
                // is taken at once and the file is written once the changes stop. See Tick.
                pin.Value = now;
                Pending[id] = now;
                _followedSinceTick = true;
            }
            catch (Exception ex)
            {
                Plugin.WarnOnce($"Keepsake: following a setting change failed: {ex.Message}", ex);
            }
        }

        /// <summary>Values followed from changes elsewhere and not written to the pins file yet, by id.</summary>
        private static readonly Dictionary<string, string> Pending = new Dictionary<string, string>();

        /// <summary>How long after the last followed change the pins file is written.</summary>
        public const float FollowDelay = 1f;

        private static bool _followedSinceTick;
        private static float _flushAt;

        /// <summary>
        /// Called every frame with the time. Writes the values followed from changes elsewhere once
        /// none has come in for FollowDelay seconds.
        /// </summary>
        public static void Tick(float now)
        {
            while (OffThread.TryDequeue(out var entry)) FollowChange(entry);

            if (_followedSinceTick)
            {
                _followedSinceTick = false;
                _flushAt = now + FollowDelay;
            }

            if (Pending.Count > 0 && now >= _flushAt) Flush();
        }

        /// <summary>Writes any followed values still waiting, such as when the game closes.</summary>
        public static void Flush()
        {
            if (Pending.Count == 0) return;

            // Takes in any hand edit first; Sync puts the waiting values back on top of it.
            Sync();
            Save();
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

        /// <summary>Writes the pins in memory to keepsake.pins. Returns false when it could not, or must not because the file could not be read.</summary>
        private static bool Save()
        {
            Index();
            if (_unreadable)
            {
                Plugin.WarnOnce("Keepsake: keepsake.pins could not be read, so changes are not saved to it until it can.");
                return false;
            }

            if (!PinFile.Write(_pins)) return false;
            _stamp = PinFile.Stamp();
            SpareCopy.Follow();

            // Every value in memory is in the file now, followed ones included.
            foreach (var id in Pending.Keys)
                if (_byId.TryGetValue(id, out var pin))
                    Plugin.Log.LogInfo($"Keepsake: your value for {pin.File} [{pin.Section}] {pin.Key} is now {pin.Value}.");
            Pending.Clear();
            return true;
        }

        /// <summary>Forgets everything, as at launch. For the tests, which run many launches in one process.</summary>
        internal static void Reset()
        {
            foreach (var config in Followed) config.SettingChanged -= OnSettingChanged;
            Followed.Clear();
            _pins = new List<Pin>();
            _byId = new Dictionary<string, Pin>();
            _stamp = null;
            _loaded = false;
            _unreadable = false;
            _keptAtLaunch = null;
            Released.Clear();
            _changes = null;
            _changesUnreadable = false;
            Pending.Clear();
            _followedSinceTick = false;
            _flushAt = 0f;
            Changed = null;
            while (OffThread.TryDequeue(out _)) { }
            MainThread = 0;
        }

        private static void Index()
        {
            var byId = new Dictionary<string, Pin>();
            foreach (var pin in _pins) byId[pin.Id] = pin;
            _byId = byId;
        }
    }
}
