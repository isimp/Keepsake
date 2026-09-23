using System;
using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;

namespace Keepsake.UI
{
    /// <summary>
    /// The game's own interface sounds for what the panel does. Every button already clicks through
    /// the ButtonSfx Jotunn gives it; these mark what the click did.
    ///
    /// Found by name through Jotunn, which reaches the game's loaded assets, and kept once found. A
    /// sound that is not loaded yet is not played and is looked for again next time: a missing
    /// sound is never worth more than silence.
    ///
    /// Each sound is a copy of the game's prefab, made under an inactive holder so nothing on it
    /// wakes up before its audio is sent to the game's interface mixer group, which follows the
    /// master and sound effects volume, and made two-dimensional. Its network view is kept from
    /// starting, so only this player hears it, and it is destroyed on a timer in case the prefab
    /// does not tidy up after itself.
    /// </summary>
    public static class Sfx
    {
        public const string PanelOpen = "sfx_gui_inventory_open";
        public const string PanelClose = "sfx_gui_inventory_close";

        /// <summary>Dropping an item into a slot: the setting is put away, safe from syncs.</summary>
        public const string Kept = "sfx_gui_moveitem";

        public const string Released = "sfx_gui_craftitem_workbench_end";

        /// <summary>A quiet tick for a new value on a setting already kept.</summary>
        public const string ValueSet = "sfx_gui_select";

        // Long enough for any interface sound to finish.
        private const float Lifetime = 5f;

        private static readonly Dictionary<string, GameObject> Found = new Dictionary<string, GameObject>();
        private static readonly HashSet<string> Missed = new HashSet<string>();
        private static GameObject _holder;

        public static void Play(string name)
        {
            if (!Plugin.PlaySounds) return;

            try
            {
                if (!Found.TryGetValue(name, out var prefab) || prefab == null)
                {
                    prefab = PrefabManager.Cache.GetPrefab<GameObject>(name);
                    if (prefab == null)
                    {
                        if (Missed.Add(name)) Plugin.Log.LogInfo($"Keepsake: the sound {name} is not loaded yet, so it stays silent for now.");
                        return;
                    }

                    Found[name] = prefab;
                }

                Spawn(prefab);
            }
            catch (Exception ex)
            {
                Plugin.WarnOnce($"Keepsake: could not play {name}: {ex.Message}", ex);
            }
        }

        private static void Spawn(GameObject prefab)
        {
            var was = ZNetView.m_forceDisableInit;
            ZNetView.m_forceDisableInit = true;
            try
            {
                var copy = UnityEngine.Object.Instantiate(prefab, Holder().transform, false);

                var gui = AudioMan.instance != null ? AudioMan.instance.m_guiMixer : null;
                foreach (var source in copy.GetComponentsInChildren<AudioSource>(true))
                {
                    if (gui != null) source.outputAudioMixerGroup = gui;
                    source.spatialBlend = 0f;
                }

                var camera = Utils.GetMainCamera();
                copy.transform.SetParent(null, false);
                copy.transform.position = camera != null ? camera.transform.position : Vector3.zero;

                UnityEngine.Object.Destroy(copy, Lifetime);
            }
            finally
            {
                ZNetView.m_forceDisableInit = was;
            }
        }

        private static GameObject Holder()
        {
            if (_holder != null) return _holder;

            _holder = new GameObject("KeepsakeSounds");
            _holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(_holder);
            return _holder;
        }
    }
}
