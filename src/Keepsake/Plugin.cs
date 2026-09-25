using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Keepsake.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Keepsake
{
    [BepInPlugin(Guid, "Keepsake", Version)]
    [BepInDependency("com.jotunn.jotunn")]
    [BepInProcess("valheim.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "isimp.Keepsake";
        public const string Version = "0.4.2";

        public static ManualLogSource Log;

        private static readonly HashSet<string> Warned = new HashSet<string>();

        /// <summary>
        /// Reports a failure that was caught and carried on from. The first one warns and the rest
        /// go to debug level, so a failure in code that runs every frame cannot bury the log. The
        /// call's own file and line tell one site from another.
        /// </summary>
        /// <param name="detail">An exception whose stack is worth having, written only with the warning.</param>
        public static void WarnOnce(string message, Exception detail = null,
            [CallerFilePath] string file = null, [CallerLineNumber] int line = 0)
        {
            if (Warned.Add(file + ":" + line)) Log.LogWarning(detail != null ? message + "\n" + detail : message);
            else Log.LogDebug(message);
        }

        private ConfigEntry<KeyboardShortcut> _openKey;
        private static ConfigEntry<bool> _playSounds;
        private ConfigEntry<bool> _profileChangeNotice;
        private static ConfigEntry<float> _panelWidth;
        private static ConfigEntry<float> _panelHeight;

        /// <summary>Whether the panel marks what it did with the game's interface sounds.</summary>
        public static bool PlaySounds => _playSounds == null || _playSounds.Value;

        /// <summary>Panel size, remembered across sessions once you drag the corner.</summary>
        public static Vector2 PanelSize
        {
            get => new Vector2(_panelWidth?.Value ?? 1320f, _panelHeight?.Value ?? 820f);
            set
            {
                if (_panelWidth == null || _panelHeight == null) return;
                _panelWidth.Value = value.x;
                _panelHeight.Value = value.y;
            }
        }

        private bool _reconciled;
        private bool _reconcileInWorld;
        private float _reconcileAt;

        private void Awake()
        {
            Log = Logger;
            Keeper.MainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            PinFile.Log = Logger;
            SettingIndex.FileFound = Keeper.Follow;
            SettingIndex.SettingFound = Session.Note;

            _openKey = Config.Bind("General", "OpenKey", new KeyboardShortcut(KeyCode.Home),
                "Opens the Keepsake panel.");
            _panelWidth = Config.Bind("Panel", "Width", 1320f,
                new ConfigDescription("Panel width in pixels. Set by dragging the corner handle.",
                    new AcceptableValueRange<float>(1100f, 3840f)));
            _panelHeight = Config.Bind("Panel", "Height", 820f,
                new ConfigDescription("Panel height in pixels. Set by dragging the corner handle.",
                    new AcceptableValueRange<float>(560f, 2160f)));
            _playSounds = Config.Bind("Panel", "PlaySounds", true,
                "Play the game's interface sounds when the panel opens and closes, and when a setting is kept, released or given a new value.");
            _profileChangeNotice = Config.Bind("General", "ProfileChangeNotice", true,
                "Say on screen, once your character appears, when the profile changed values you keep, or kept files were left as they are, and those wait for an answer in the panel.");

            var harmony = new Harmony(Guid);
            StartMenuKeys.Patch(harmony);

            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

            Log.LogInfo("Keepsake loaded.");
        }

        private void OnDestroy()
        {
            // Your values first: closing the panel as the game shuts down may fail with the GUI
            // already gone.
            FlushQuietly("destroy");

            try
            {
                KeepsakePanel.Close(quietly: true);
            }
            catch (Exception ex)
            {
                WarnOnce($"Keepsake: closing the panel failed: {ex.Message}", ex);
            }
        }

        private void OnApplicationQuit() => FlushQuietly("quit");

        /// <summary>
        /// The runtime's exit event, later than the game's own hooks where the game raises it at
        /// all. Kept files are copied once more there, so a mod that writes its file late still
        /// counts as having written it while the game ran.
        /// </summary>
        private static void OnProcessExit(object sender, EventArgs e)
        {
            try
            {
                // Only the files: what the rest writes may be gone from the game by now.
                FileKeeper.Close("exit");
                SpareCopy.Update();
            }
            catch (Exception)
            {
                // Nothing is left to report to by now. The launch treats a close it was not told
                // about as one it did not see, which is the safe side.
            }
        }

        /// <summary>Writes the values followed from a config manager that are still waiting, copies of kept files, and the spare copy, as the game closes.</summary>
        private static void FlushQuietly(string by)
        {
            try
            {
                GameClose.Run(by);
            }
            catch (Exception ex)
            {
                WarnOnce($"Keepsake: saving your latest values failed: {ex.Message}", ex);
            }
        }

        private void Update()
        {
            try
            {
                Keeper.Tick(Time.realtimeSinceStartup);
                ReconcileOnce();
                CheckWaitingOnce();
                NoticeProfileChangesOnce();

                // While a key is being set, every key belongs to that, the open key and Escape too.
                if (KeyCapture.Active)
                {
                    KeyCapture.Tick();
                    return;
                }

                if (KeepsakePanel.IsOpen) KeepsakePanel.Tick();

                // Escape still closes the panel while typing, but a letter belongs to the text
                // field, not to the open key.
                if (KeepsakePanel.Typing)
                {
                    if (Input.GetKeyDown(KeyCode.Escape)) KeepsakePanel.Close();
                    return;
                }

                if (KeepsakePanel.IsOpen && Input.GetKeyDown(KeyCode.Escape)) KeepsakePanel.Close();
                else if (_openKey.Value.IsDown() && !TypingElsewhere()) KeepsakePanel.Toggle();
            }
            catch (Exception ex)
            {
                WarnOnce($"Keepsake: input check failed: {ex.Message}", ex);
            }
        }

        private bool _waitingChecked;
        private float _waitingCheckAt;

        private bool _noticeShown;
        private float _noticeAt;

        /// <summary>
        /// Profile changes to kept settings, and kept files the launch left as they are, show only
        /// in the panel, so once a session, a few seconds after your character appears and the
        /// kept values bound late are in place, a line in the corner says how many wait for an answer.
        /// </summary>
        private void NoticeProfileChangesOnce()
        {
            if (_noticeShown || Player.m_localPlayer == null || MessageHud.instance == null) return;

            if (_noticeAt == 0f) _noticeAt = Time.realtimeSinceStartup + 5f;
            if (Time.realtimeSinceStartup < _noticeAt) return;
            _noticeShown = true;

            // Said whatever the setting, since it happens only when a mod manager replaced the profile.
            if (SpareCopy.RestoredThisLaunch)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft,
                    "Keepsake: the profile was replaced by an update, so what you kept came back from its spare copy.");

            if (!_profileChangeNotice.Value) return;

            var count = Keeper.ProfileChanged().Count;
            var files = FileKeeper.WaitingCount;
            if (count == 0 && files == 0) return;

            var what = new List<string>();
            if (count > 0) what.Add($"the profile changed {(count == 1 ? "a value" : count + " values")} you keep");
            if (files > 0) what.Add($"{(files == 1 ? "a kept file waits" : files + " kept files wait")} for an answer in Files");

            MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft,
                $"Keepsake: {string.Join(", and ", what.ToArray())}. Press {OpenKeyLabel()} to look.");
        }

        /// <summary>The open key as the keyboard labels it, modifiers first.</summary>
        private string OpenKeyLabel()
        {
            var shortcut = _openKey.Value;
            return string.Join(" + ", shortcut.Modifiers.OrderBy(k => (int)k).Select(KeyLabels.Of)
                .Concat(new[] { KeyLabels.Of(shortcut.MainKey) }).ToArray());
        }

        /// <summary>
        /// Keybinds kept here before Bindrune was installed wait for Bindrune to take them over,
        /// and nothing keeps them until it does. Bindrune does that when it puts its own keys
        /// back, at the start menu and again once a world has loaded, so any still waiting a while
        /// after your character appears were not taken: an older Bindrune, most likely. Said once
        /// a session in the log, since the panel only shows it to someone who looks.
        /// </summary>
        private void CheckWaitingOnce()
        {
            if (_waitingChecked || Player.m_localPlayer == null) return;

            if (_waitingCheckAt == 0f) _waitingCheckAt = Time.realtimeSinceStartup + 10f;
            if (Time.realtimeSinceStartup < _waitingCheckAt) return;
            _waitingChecked = true;

            if (!SettingIndex.BindruneLoaded) return;

            // Bindrune takes keybinds out of the pins file after Keepsake last read it, so what is
            // in memory may still list the ones it took.
            Keeper.Sync();

            var waiting = Keeper.Pins.Count(p => SettingIndex.Find(p.Id)?.IsKeybind == true);
            if (waiting == 0) return;

            Log.LogWarning($"Keepsake: {waiting} kept keybind(s) are waiting for Bindrune to take them over, and nothing " +
                           "keeps them until it does. Update Bindrune, or set these keys as yours in Bindrune and release them in Keepsake.");
        }

        /// <summary>
        /// True while a text field outside the panel has the keyboard, such as the game's chat or
        /// console. The open key is a plain key, and one that text fields use themselves.
        /// </summary>
        private static bool TypingElsewhere()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected == null) return false;

            var input = selected.GetComponent<InputField>();
            if (input != null && input.isFocused) return true;

            var tmp = selected.GetComponent<TMP_InputField>();
            return tmp != null && tmp.isFocused;
        }

        /// <summary>
        /// Puts back kept values the preloader could not place, because their cfg file or line did
        /// not exist before the mod first ran, and starts following changes to every mod's
        /// settings. Once at the start menu, when every plugin has loaded, and once more shortly
        /// after your character first appears if some kept settings were still missing, since
        /// some mods bind settings only when a world loads. Opening the panel does it again.
        /// </summary>
        private void ReconcileOnce()
        {
            if (_reconciled) return;

            if (_reconcileInWorld)
            {
                // A logout before the pass ran starts the wait again on the next character.
                if (Player.m_localPlayer == null)
                {
                    _reconcileAt = 0f;
                    return;
                }

                if (_reconcileAt == 0f) _reconcileAt = Time.realtimeSinceStartup + 2f;
                if (Time.realtimeSinceStartup < _reconcileAt) return;

                Keeper.Reconcile();
                _reconciled = true;
                return;
            }

            if (FejdStartup.instance == null && Player.m_localPlayer == null) return;

            if (Keeper.Reconcile() == 0) _reconciled = true;
            else _reconcileInWorld = true;
        }
    }
}
