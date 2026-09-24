using System;
using System.Collections.Generic;
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
        public const string Version = "0.1.0";

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

            var harmony = new Harmony(Guid);
            StartMenuKeys.Patch(harmony);

            Log.LogInfo("Keepsake loaded.");
        }

        private void OnDestroy()
        {
            KeepsakePanel.Close(quietly: true);
        }

        private void Update()
        {
            try
            {
                ReconcileOnce();

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
