using Jotunn.Managers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Keepsake.UI
{
    /// <summary>What the middle column lists.</summary>
    public enum Source
    {
        Kept,

        /// <summary>Kept settings whose profile's value changed while you kept yours. Offered while any wait for an answer.</summary>
        ProfileChanged,

        Changed,
        /// <summary>Every setting the search matches. Only offered while searching.</summary>
        All,
        Mod,
    }

    /// <summary>
    /// The one window, in three columns: where to look on the left (your kept settings, what
    /// changed this session, or one mod), that place's settings in the middle, and everything
    /// about the selected setting on the right, which is also where it is kept, released and
    /// edited. The search narrows all three and looks in descriptions too.
    /// </summary>
    public static partial class KeepsakePanel
    {
        private const float Margin = 26f;
        private const float Gap = 12f;
        private const float TopChrome = 110f;
        private const float FooterHeight = 44f;
        private const float LeftWidth = 270f;
        private const float DetailWidth = 420f;

        private static Vector2 _size;

        private static float PanelWidth => Size.x;
        private static float PanelHeight => Size.y;
        private static float BodyHeight => PanelHeight - TopChrome - FooterHeight;
        private static float MiddleWidth => PanelWidth - Margin * 2f - LeftWidth - DetailWidth - Gap * 2f;

        private static Vector2 Size
        {
            get
            {
                if (_size == Vector2.zero)
                    _size = new Vector2(
                        Mathf.Min(Plugin.PanelSize.x, Screen.width * 0.95f),
                        Mathf.Min(Plugin.PanelSize.y, Screen.height * 0.95f));
                return _size;
            }
        }

        private static GameObject _root;
        private static RectTransform _detail;
        private static InputField _search;
        private static Text _summary;
        private static Text _footer;
        private static float _repopulateAt;

        /// <summary>Whether the redraw waiting to happen should start the middle list from the top.</summary>
        private static bool _pendingReset;

        private static bool _rebindNextFrame;

        // Where you were, kept across closing and reopening the panel.
        private static Source _source = Source.Kept;
        private static string _mod;
        private static Source _sourceBeforeSearch = Source.Kept;
        private static string _modBeforeSearch;
        private static string _selectedId;
        private static string _query = "";

        /// <summary>The keys Bindrune holds that Keepsake could take over, read when the panel opens.</summary>
        private static System.Collections.Generic.List<Keeper.BindruneKeep> _bindruneKeys;

        /// <summary>Whether the right column shows the keys from Bindrune rather than a setting.</summary>
        private static bool _showBindruneOffer;

        /// <summary>Beside Close, only while Bindrune holds keys that nothing applies. See BindruneOffer.</summary>
        private static GameObject _bindruneButton;

        private static void RefreshBindruneKeys()
        {
            try
            {
                _bindruneKeys = Keeper.BindruneKeys();
            }
            catch (System.Exception ex)
            {
                Plugin.WarnOnce($"Keepsake: could not read Bindrune's keys: {ex.Message}", ex);
                _bindruneKeys = null;
            }
        }

        /// <summary>What the last action had to say, shown in the footer until the next one.</summary>
        private static string _note;
        private static bool _noteIsProblem;

        public static bool IsOpen => _root != null;

        private static int _closedFrame = -1;

        /// <summary>
        /// Whether the keyboard is the panel's this frame: while it is open, and in the frame it
        /// closed in, so the key that closed it reaches nothing else.
        /// </summary>
        public static bool HoldsKeyboard => IsOpen || Time.frameCount == _closedFrame;

        /// <summary>True while any text field has the keyboard, so keys go to it and not to hotkeys.</summary>
        public static bool Typing
        {
            get
            {
                if (_root == null) return false;
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                var input = selected != null ? selected.GetComponent<InputField>() : null;
                return input != null && input.isFocused;
            }
        }

        public static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public static void Open()
        {
            if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null)
            {
                Plugin.Log.LogWarning("Keepsake: the GUI is not ready yet.");
                return;
            }

            var clock = System.Diagnostics.Stopwatch.StartNew();

            // Mods bind settings at different times, so every open reads them again.
            Keeper.Reconcile();
            _refreshMs = SettingIndex.LastRefreshMs;
            _refreshSorted = SettingIndex.LastRefreshSorted;
            Keeper.Changed = OnChangedElsewhere;
            KeyCapture.Changed = ShowDetail;
            RefreshBindruneKeys();
            _showBindruneOffer = false;

            _note = null;
            _redraws = 0;
            _slowestMs = 0;
            _slowestCount = 0;
            Build();
            GUIManager.BlockInput(true);
            _openMs = clock.Elapsed.TotalMilliseconds;

            if (_root != null) Sfx.Play(Sfx.PanelOpen);
        }

        // What opening the panel cost, logged when it closes.
        private static double _openMs;
        private static double _refreshMs;
        private static bool _refreshSorted;
        private static bool _timingLogged;

        /// <param name="quietly">For the game shutting down, which is no moment to play a sound.</param>
        public static void Close(bool quietly = false)
        {
            Keeper.Changed = null;
            KeyCapture.Cancel();
            KeyCapture.Changed = null;
            if (_root == null) return;

            if (!quietly) Sfx.Play(Sfx.PanelClose);
            _closedFrame = Time.frameCount;
            RememberScroll();
            UnityEngine.Object.Destroy(_root);
            GUIManager.BlockInput(false);
            _root = null;
            _sourceList = null;
            _settingList = null;
            _emptyMessage = null;
            _detail = null;
            _search = null;
            _bindruneButton = null;
            _repopulateAt = 0f;

            // The first time in a session at Info, where a default log file records it; after that
            // at Debug, so an often opened panel does not fill the log.
            var timing = $"Keepsake: the panel opened in {_openMs:0.0} ms, {_refreshMs:0.0} ms of it reading every mod's " +
                         $"settings{(_refreshSorted ? " and sorting them" : "")} and {_firstMs:0.0} ms drawing the lists and " +
                         $"their rows for {_firstCount} settings" +
                         (_redraws > 1
                             ? $"; {_redraws - 1} redraws after that, the slowest {_slowestMs:0.0} ms with {_slowestCount} settings."
                             : ".");
            if (_timingLogged) Plugin.Log.LogDebug(timing);
            else Plugin.Log.LogInfo(timing);
            _timingLogged = true;
        }

        public static void Tick()
        {
            // A new window's scroll views may not know their height until a frame has passed, and
            // the lists size their row pools from it.
            if (_rebindNextFrame)
            {
                _rebindNextFrame = false;
                _sourceList?.Rebind();
                _settingList?.Rebind();
            }

            if (_repopulateAt <= 0f || Time.realtimeSinceStartup < _repopulateAt) return;

            // A redraw of the detail column replaces the value field you are typing in, so one
            // waits until you are done. The lists and the search field are not replaced.
            var detail = !_listsOnly || _detailStale;
            if (detail && Typing && !_search.isFocused)
            {
                _repopulateAt = Time.realtimeSinceStartup + 0.25f;
                return;
            }

            if (detail) Populate(keepScroll: !_pendingReset);
            else RedrawLists();
        }

        /// <summary>
        /// Typing fires a change per keystroke; redraw after a pause instead. A reset request wins
        /// over one that would keep the list where it is.
        /// </summary>
        private static void RequestPopulate(bool reset)
        {
            _pendingReset |= reset;
            _listsOnly = false;
            _repopulateAt = Time.realtimeSinceStartup + 0.15f;
        }

        /// <summary>Whether the redraw waiting is only for changes made elsewhere, which leave the detail column alone.</summary>
        private static bool _listsOnly;

        /// <summary>Whether the selected setting itself changed elsewhere, so the detail column is redrawn too.</summary>
        private static bool _detailStale;

        /// <summary>
        /// A setting changed outside the panel. Some mods write their own settings all the time, so
        /// these redraw at most twice a second and never push back one already waiting. They
        /// redraw the lists only, unless the setting is the one shown on the right: rebuilding the
        /// detail column would close an open dropdown under your cursor for a change elsewhere.
        /// </summary>
        private static void OnChangedElsewhere(string id)
        {
            if (id != null && id == _selectedId) _detailStale = true;
            if (_repopulateAt > 0f) return;
            _listsOnly = true;
            _repopulateAt = Time.realtimeSinceStartup + 0.5f;
        }

        /// <summary>Redraws all three columns.</summary>
        private static void Populate(bool keepScroll)
        {
            if (_root == null) return;

            _repopulateAt = 0f;
            _pendingReset = false;
            _listsOnly = false;
            _detailStale = false;
            PopulateLists(Words(), keepScroll);
            ShowDetail();
            UpdateSummary();
            UpdateBindruneButton();
            ShowNote();
        }

        /// <summary>Redraws what lists settings, where they stay, and leaves the detail column as it is.</summary>
        private static void RedrawLists()
        {
            if (_root == null) return;

            _repopulateAt = 0f;
            _listsOnly = false;
            PopulateLists(Words(), keepScroll: true);
            UpdateSummary();
        }

        private static void UpdateBindruneButton()
        {
            if (_bindruneButton == null) return;

            var count = _bindruneKeys?.Count ?? 0;
            _bindruneButton.SetActive(count > 0);
            var label = _bindruneButton.GetComponentInChildren<Text>();
            if (label != null) label.text = $"From Bindrune ({count})";
        }

        // ---------- construction ----------

        private static void Build()
        {
            _root = GUIManager.Instance.CreateWoodpanel(
                GUIManager.CustomGUIFront.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                PanelWidth, PanelHeight, true);
            _root.name = "KeepsakePanel";

            var title = Label("Keepsake", _root.transform, 200f, 32f, 26, GUIManager.Instance.ValheimOrange, true);
            AnchorLeft(title, Margin, -36f);

            var close = Button("Close", _root.transform, 110f, 32f, () => Close());
            AnchorRight(close, -Margin, -36f);

            _bindruneButton = Button("From Bindrune", _root.transform, 200f, 32f, () =>
            {
                KeyCapture.Cancel();
                _selectedId = null;
                _showBindruneOffer = true;
                _note = null;
                _settingList?.Rebind();
                ShowDetail();
                ShowNote();
            });
            AnchorRight(_bindruneButton, -Margin - 120f, -36f);

            var searchObject = GUIManager.Instance.CreateInputField(_root.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero,
                InputField.ContentType.Standard, "search mods, settings and descriptions...", 16, 420f, 32f);
            AnchorLeft(searchObject, Margin, -80f);
            _search = searchObject.GetComponent<InputField>();
            _search.text = _query;
            _search.onValueChanged.AddListener(OnSearchChanged);

            var clear = Button("x", _root.transform, 32f, 32f, () =>
            {
                if (_search != null) _search.text = "";
            });
            AnchorLeft(clear, Margin + 426f, -80f);

            var summary = Label("", _root.transform, 560f, 32f, 14, Dim);
            AnchorRight(summary, -Margin, -80f);
            _summary = summary.GetComponent<Text>();
            _summary.alignment = TextAnchor.MiddleRight;

            var top = -(TopChrome + BodyHeight / 2f);
            var sources = MakeScrollView(LeftWidth, BodyHeight, new Vector2(Margin + LeftWidth / 2f, top));
            // The middle column opens with a header naming what it lists, above its scroll view,
            // so the three columns still start and end at the same height.
            BuildHeader(Margin + LeftWidth + Gap, -TopChrome);
            var listHeight = BodyHeight - HeaderHeight - 2f;
            var settings = MakeScrollView(MiddleWidth, listHeight,
                new Vector2(Margin + LeftWidth + Gap + MiddleWidth / 2f, -(TopChrome + HeaderHeight + 2f + listHeight / 2f)));
            var detail = MakeScrollView(DetailWidth, BodyHeight,
                new Vector2(PanelWidth - Margin - DetailWidth / 2f, top));
            if (sources == null || settings == null || detail == null) return;

            BuildLists(sources, settings);
            _detail = Stack(detail, 16, 6f);

            var footer = Label("", _root.transform, PanelWidth - Margin * 2f - 60f, 24f, 14, Color.white);
            Anchor(footer, new Vector2(0.5f, 0f), new Vector2(0f, FooterHeight / 2f));
            _footer = footer.GetComponent<Text>();
            _footer.alignment = TextAnchor.MiddleCenter;

            BuildResizeGrip();

            // A new window opens where the last one was left.
            _sourceList.ScrollOffset = _sourceScroll;
            _settingList.ScrollOffset = _settingScroll;
            Populate(keepScroll: true);
            _rebindNextFrame = true;
        }

        // How far both lists were scrolled when their window went away.
        private static float _sourceScroll;
        private static float _settingScroll;

        private static void RememberScroll()
        {
            if (_sourceList != null) _sourceScroll = _sourceList.ScrollOffset;
            if (_settingList != null) _settingScroll = _settingList.ScrollOffset;
        }

        private static void Rebuild()
        {
            RememberScroll();
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            Build();
        }

        private static void BuildResizeGrip()
        {
            var grip = new GameObject("resize", typeof(RectTransform), typeof(Image), typeof(ResizeGrip));
            grip.transform.SetParent(_root.transform, false);

            var rect = (RectTransform)grip.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(26f, 26f);
            rect.anchoredPosition = new Vector2(-6f, 6f);

            grip.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.22f);

            var handler = grip.GetComponent<ResizeGrip>();
            handler.Target = (RectTransform)_root.transform;
            // Narrower than this and the middle column has no room for a value.
            handler.MinSize = new Vector2(1100f, 560f);
            handler.Resized = size =>
            {
                _size = size;
                Plugin.PanelSize = size;
                Rebuild();
            };
        }

        private static void OnSearchChanged(string text)
        {
            // Escape in a text field cancels the edit, and the field puts back what it held when
            // you clicked into it. Escape also closes the panel, so that is not a new search, and
            // neither is anything the field does once the panel is gone: the search stays as typed.
            if (_root == null || Input.GetKeyDown(KeyCode.Escape)) return;

            var was = _query;
            _query = (text ?? "").Trim();

            // A search starts across everything; a mod on the left narrows it from there. Clearing
            // the search goes back to where you were before it.
            if (was.Length == 0 && _query.Length > 0)
            {
                _sourceBeforeSearch = _source;
                _modBeforeSearch = _mod;
                _source = Source.All;
            }
            else if (was.Length > 0 && _query.Length == 0)
            {
                if (_source == Source.All)
                {
                    _source = _sourceBeforeSearch;
                    _mod = _modBeforeSearch;
                }

                // Back to every mod on the left, from the top like the middle list.
                if (_sourceList != null) _sourceList.ScrollOffset = 0f;
            }

            RequestPopulate(reset: true);
        }

        private static string[] Words() =>
            _query.ToLowerInvariant().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

        private static void UpdateSummary()
        {
            if (_summary == null) return;
            var kept = Keeper.Pins.Count;
            _summary.text = $"{SettingIndex.All.Count} settings in {SettingIndex.Mods.Count} mods, {kept} kept";
        }

        /// <summary>Says what an action did, or why it did not.</summary>
        private static void Say(string text, bool problem = false)
        {
            _note = text;
            _noteIsProblem = problem;
            ShowNote();
        }

        private static void ShowNote()
        {
            if (_footer == null) return;

            if (_note != null)
            {
                _footer.text = _note;
                _footer.color = _noteIsProblem ? Problem : Color.white;
                return;
            }

            _footer.text = "<color=#ffc060>Orange</color>: kept at your value.   " +
                           "<color=#6fb0ff>Blue bar</color>: differs from the mod's default.   " +
                           "Quiet: profile changes are not asked about.";
            _footer.color = Dim;
        }
    }
}
