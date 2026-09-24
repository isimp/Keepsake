using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace Keepsake.UI
{
    /// <summary>The left column, where to look, and the middle one, the settings found there.</summary>
    public static partial class KeepsakePanel
    {
        private const float SourceRowHeight = 28f;
        private const float RowHeight = 30f;
        private const float TagColumn = 70f;
        private const float CountColumn = 52f;

        private const float HeaderHeight = 52f;

        private static Text _headerTitle;
        private static Text _headerInfo;
        private static GameObject _keepAllButton;

        /// <summary>What Keep all keeps: the listed changes that can be kept.</summary>
        private static List<Setting> _keepable = new List<Setting>();

        private const float KeepAllWidth = 150f;

        private static VirtualList<SourceItem, SourceView> _sourceList;
        private static VirtualList<SettingItem, SettingView> _settingList;
        private static GameObject _emptyMessage;

        // What redraws cost while the panel is open, logged when it closes.
        private static int _redraws;
        private static double _slowestMs;
        private static int _slowestCount;

        /// <summary>A row in the middle: a loaded setting, or a kept one whose mod has not bound it.</summary>
        private sealed class Row
        {
            public string Id;
            public Setting Setting;
            public Pin Pin;
            public string ModName;
            public string Section;
            public string Key;
        }

        private sealed class SettingItem
        {
            public string Heading;
            public Row Row;
        }

        private sealed class SourceItem
        {
            public string Heading;
            public string Text;
            public Source Source;
            public string Mod;
            public int Count;
            public bool HasKept;
        }

        private sealed class SettingView : RowView
        {
            public Image Background;
            public Button Button;
            public Image Bar;
            public Text Key;
            public RectTransform KeyRect;
            public Text Value;
            public Text Tag;
            public SettingItem Item;
        }

        private sealed class SourceView : RowView
        {
            public Image Background;
            public Button Button;
            public Text Name;
            public Text Count;
            public SourceItem Item;
        }

        // ---------- the header ----------

        /// <summary>A strip over the middle column: what is listed, and a line about it.</summary>
        private static void BuildHeader(float left, float top)
        {
            var strip = new GameObject("header", typeof(RectTransform), typeof(Image));
            strip.transform.SetParent(_root.transform, false);
            var rect = (RectTransform)strip.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(MiddleWidth, HeaderHeight);
            rect.anchoredPosition = new Vector2(left, top);
            var image = strip.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.4f);
            image.raycastTarget = false;

            var title = Label("", strip.transform, 10f, 28f, 19, GUIManager.Instance.ValheimOrange, true);
            _headerTitle = OneLine(title, richText: true);
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.offsetMin = new Vector2(12f, -30f);
            titleRect.offsetMax = new Vector2(-12f, -3f);

            var info = Label("", strip.transform, 10f, 20f, 13, Dim);
            _headerInfo = OneLine(info);
            var infoRect = (RectTransform)info.transform;
            infoRect.anchorMin = new Vector2(0f, 0f);
            infoRect.anchorMax = new Vector2(1f, 0f);
            infoRect.pivot = new Vector2(0f, 0f);
            infoRect.offsetMin = new Vector2(12f, 4f);
            infoRect.offsetMax = new Vector2(-12f, 24f);

            _keepAllButton = Button("Keep all", strip.transform, KeepAllWidth, 32f, KeepAll);
            AnchorRight(_keepAllButton, -10f, -HeaderHeight / 2f);
            _keepAllButton.SetActive(false);
        }

        /// <summary>Keeps every change listed that can be kept, each at the value it has now.</summary>
        private static void KeepAll()
        {
            var settings = _keepable.ToList();
            var kept = 0;
            Act(() =>
            {
                kept = Keeper.PinAll(settings);
                return kept > 0 ? null : "None of these could be kept.";
            }, Sfx.Kept, () => $"{Plural(kept, "setting")} kept at {(kept == 1 ? "its value" : "their values")}. A profile sync leaves {(kept == 1 ? "it" : "them")} alone now.");
        }

        /// <summary>Shows Keep all over a list of changes with something to keep, and makes room for it.</summary>
        private static void UpdateKeepAll(List<Row> rows)
        {
            _keepable = _source == Source.Changed
                ? rows.Select(r => r.Setting).Where(Keeper.CanPin).ToList()
                : new List<Setting>();

            var shown = _keepable.Count > 0;
            if (_keepAllButton != null)
            {
                _keepAllButton.SetActive(shown);
                var label = _keepAllButton.GetComponentInChildren<Text>();
                if (label != null) label.text = $"Keep all ({_keepable.Count})";
            }

            var right = shown ? -(KeepAllWidth + 22f) : -12f;
            foreach (var text in new[] { _headerTitle, _headerInfo })
            {
                var rect = (RectTransform)text.transform;
                rect.offsetMax = new Vector2(right, rect.offsetMax.y);
            }
        }

        private static void UpdateHeader(List<Row> rows, bool searching)
        {
            if (_headerTitle == null || _headerInfo == null) return;
            UpdateKeepAll(rows);

            var mods = rows.Select(r => r.ModName).Distinct().Count();
            var across = $"{Plural(rows.Count, "setting")} in {Plural(mods, "mod")}";

            switch (_source)
            {
                case Source.Mod:
                    var mod = SettingIndex.Mod(_mod);
                    var kept = Keeper.Pins.Count(p => SettingIndex.Find(p.Id)?.ModName == _mod);
                    _headerTitle.text = mod == null || mod.Version.Length == 0
                        ? _mod
                        : $"{_mod}  <size=14><color=#ffffff88>{mod.Version}</color></size>";

                    var total = mod?.Count ?? rows.Count;
                    var count = searching ? $"{rows.Count} of {Plural(total, "setting")} match" : Plural(total, "setting");
                    _headerInfo.text = count + (kept > 0 ? $", {kept} kept" : "") + (mod != null ? $", in {mod.File}" : "");
                    break;

                case Source.Kept:
                    _headerTitle.text = "Kept";
                    _headerInfo.text = rows.Count == 0
                        ? "Settings held at your value through profile syncs"
                        : $"{across}, held at your value through profile syncs";
                    break;

                case Source.ProfileChanged:
                    _headerTitle.text = "Profile changed";
                    _headerInfo.text = rows.Count == 0
                        ? "Kept settings whose profile's value changed since the last launch"
                        : $"{across} kept at your value, whose profile's value changed since the last launch";
                    break;

                case Source.Changed:
                    _headerTitle.text = "Changed this session";
                    _headerInfo.text = $"{across} changed since the game started, by a config manager or the mod itself";
                    break;

                default:
                    _headerTitle.text = "All matches";
                    _headerInfo.text = $"{across} match \"{_query}\"";
                    break;
            }
        }

        private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

        // ---------- the lists ----------

        private static void BuildLists(ScrollRect sources, ScrollRect settings)
        {
            _sourceList = new VirtualList<SourceItem, SourceView>(sources, SourceRowHeight, 4f, CreateSourceView, BindSource);
            _settingList = new VirtualList<SettingItem, SettingView>(settings, RowHeight, 4f, CreateSettingView, BindSetting);

            _emptyMessage = Label("", settings.content, 10f, 10f, 15, Dim);
            var rect = (RectTransform)_emptyMessage.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(12f, -120f);
            rect.offsetMax = new Vector2(-12f, -8f);
            var text = _emptyMessage.GetComponent<Text>();
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            _emptyMessage.SetActive(false);
        }

        /// <summary>
        /// Works out what both lists show. The search runs once over every setting, and the counts
        /// on the left and the list in the middle are both taken from that one pass.
        /// </summary>
        private static void PopulateLists(string[] words, bool keepScroll)
        {
            if (_sourceList == null || _settingList == null) return;

            var clock = Stopwatch.StartNew();
            var searching = words.Length > 0;

            var matched = searching
                ? SettingIndex.All.Where(s => Matches(s.SearchText, words)).ToList()
                : SettingIndex.All;

            var counts = new Dictionary<string, int>();
            foreach (var setting in matched)
            {
                counts.TryGetValue(setting.ModName, out var n);
                counts[setting.ModName] = n + 1;
            }

            var kept = Keeper.Pins.Select(RowOf).Where(r => Matches(r, words)).ToList();
            var profileChanged = Keeper.ProfileChanged().Select(id => RowOf(Keeper.Find(id))).Where(r => Matches(r, words)).ToList();
            var changed = Session.Changed().Select(RowOf).Where(r => Matches(r, words)).ToList();

            // A selected mod the search no longer matches would leave an empty middle column with
            // nothing on the left to explain it.
            if (_source == Source.Mod && !counts.ContainsKey(_mod ?? "")) _source = searching ? Source.All : Source.Kept;
            if (_source == Source.All && !searching) _source = Source.Kept;

            _sourceList.SetItems(SourceItems(counts, kept.Count, profileChanged.Count, changed.Count, searching), keepPosition: true);

            List<Row> rows;
            switch (_source)
            {
                case Source.Kept: rows = Sorted(kept); break;
                case Source.ProfileChanged: rows = Sorted(profileChanged); break;
                case Source.Changed: rows = Sorted(changed); break;
                case Source.Mod: rows = matched.Where(s => s.ModName == _mod).Select(RowOf).ToList(); break;
                default: rows = matched.Select(RowOf).ToList(); break;
            }

            _settingList.SetItems(SettingItems(rows), keepScroll);
            UpdateHeader(rows, searching);

            _emptyMessage.SetActive(rows.Count == 0);
            if (rows.Count == 0)
                _emptyMessage.GetComponent<Text>().text =
                    searching ? "Nothing here matches the search." :
                    _source == Source.Kept ? "Nothing is kept yet. Pick a mod on the left, or search, then select a setting and keep it." :
                    _source == Source.ProfileChanged ? "Every change the profile made has been looked at. Your values stay kept." :
                    _source == Source.Changed ? "No setting has changed since the game started. Settings you change in a config manager show up here." :
                    "This mod has no settings.";

            clock.Stop();
            _redraws++;
            if (clock.Elapsed.TotalMilliseconds > _slowestMs)
            {
                _slowestMs = clock.Elapsed.TotalMilliseconds;
                _slowestCount = matched.Count;
            }
        }

        private static List<SourceItem> SourceItems(Dictionary<string, int> counts, int kept, int profileChanged, int changed, bool searching)
        {
            var keptMods = new HashSet<string>(Keeper.Pins.Select(p => SettingIndex.Find(p.Id)?.ModName).Where(m => m != null));

            var items = new List<SourceItem> { new SourceItem { Text = "Kept", Source = Source.Kept, Count = kept, HasKept = true } };

            // Only there while the profile changed something, and while you are looking at it, so
            // answering the last one does not pull the list from under you.
            if (profileChanged > 0 || _source == Source.ProfileChanged)
                items.Add(new SourceItem { Text = "Profile changed", Source = Source.ProfileChanged, Count = profileChanged, HasKept = true });

            items.Add(new SourceItem { Text = "Changed this session", Source = Source.Changed, Count = changed });
            if (searching)
                items.Add(new SourceItem { Text = "All matches", Source = Source.All, Count = counts.Values.Sum() });

            items.Add(new SourceItem { Heading = searching ? "Mods with matches" : "Mods" });

            foreach (var mod in SettingIndex.Mods)
            {
                if (!counts.TryGetValue(mod, out var count)) continue;
                items.Add(new SourceItem { Text = mod, Source = Source.Mod, Mod = mod, Count = count, HasKept = keptMods.Contains(mod) });
            }

            return items;
        }

        /// <summary>One mod is headed by its sections; the lists that span mods by mod and section.</summary>
        private static List<SettingItem> SettingItems(List<Row> rows)
        {
            var items = new List<SettingItem>(rows.Count + 64);
            string group = null;

            foreach (var row in rows)
            {
                var heading = _source == Source.Mod ? row.Section : row.ModName + "  /  " + row.Section;
                if (heading != group)
                {
                    group = heading;
                    items.Add(new SettingItem { Heading = heading });
                }

                items.Add(new SettingItem { Row = row });
            }

            return items;
        }

        // ---------- rows ----------

        private static bool Matches(string text, string[] words)
        {
            for (var i = 0; i < words.Length; i++)
                if (text.IndexOf(words[i], StringComparison.Ordinal) < 0) return false;
            return true;
        }

        private static bool Matches(Row row, string[] words) =>
            words.Length == 0 || Matches(row.Setting != null
                ? row.Setting.SearchText
                : (row.Pin.File + " " + row.Pin.Section + " " + row.Pin.Key).ToLowerInvariant(), words);

        private static List<Row> Sorted(IEnumerable<Row> rows) => rows
            .OrderBy(r => r.ModName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Section, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        private static Row RowOf(Setting setting) => new Row
        {
            Id = setting.Id,
            Setting = setting,
            ModName = setting.ModName,
            Section = setting.Section,
            Key = setting.Key,
        };

        private static Row RowOf(Pin pin)
        {
            var setting = SettingIndex.Find(pin.Id);
            if (setting != null) return RowOf(setting);
            return new Row { Id = pin.Id, Pin = pin, ModName = pin.File, Section = pin.Section, Key = pin.Key };
        }

        // ---------- the views ----------

        private static SourceView CreateSourceView(RectTransform root)
        {
            var view = new SourceView
            {
                Background = root.gameObject.AddComponent<Image>(),
                Button = root.gameObject.AddComponent<Button>(),
            };
            view.Button.onClick.AddListener(() =>
            {
                var item = view.Item;
                if (item == null || item.Heading != null) return;

                // A setting from the place being left would stay open on the right beside a list
                // it is not in, so moving somewhere else starts with nothing selected.
                if (item.Source != _source || item.Mod != _mod)
                {
                    KeyCapture.Cancel();
                    _selectedId = null;
                    _showBindruneOffer = false;
                    _note = null;
                }

                _source = item.Source;
                _mod = item.Mod;
                Populate(keepScroll: false);
            });

            var name = Label("", root, 10f, SourceRowHeight, 15, Color.white);
            view.Name = OneLine(name);
            Stretch(name, 0f, 1f, 8f, -(CountColumn + 8f));

            var count = Label("", root, 10f, SourceRowHeight, 13, Dim);
            view.Count = OneLine(count);
            view.Count.alignment = TextAnchor.MiddleRight;
            Stretch(count, 1f, 1f, -(CountColumn + 4f), -6f);

            return view;
        }

        private static void BindSource(SourceView view, SourceItem item)
        {
            view.Item = item;

            if (item.Heading != null)
            {
                view.Background.color = Clearish;
                view.Button.interactable = false;
                view.Name.text = item.Heading;
                view.Name.font = GUIManager.Instance.AveriaSerifBold;
                view.Name.fontSize = 14;
                view.Name.color = GUIManager.Instance.ValheimOrange;
                view.Count.text = "";
                return;
            }

            var selected = _source == item.Source && (item.Source != Source.Mod || _mod == item.Mod);
            view.Background.color = selected ? Selected : Clearish;
            view.Button.interactable = true;
            view.Name.text = item.Text;
            view.Name.font = GUIManager.Instance.AveriaSerif;
            view.Name.fontSize = 15;
            view.Name.color = item.HasKept && item.Count > 0 ? Kept : Color.white;
            view.Count.text = item.Count.ToString();
        }

        private static SettingView CreateSettingView(RectTransform root)
        {
            var view = new SettingView
            {
                Background = root.gameObject.AddComponent<Image>(),
                Button = root.gameObject.AddComponent<Button>(),
            };
            view.Button.onClick.AddListener(() =>
            {
                var row = view.Item?.Row;
                if (row != null) Select(row.Id);
            });

            // The bar marks a value the profile or you changed from what the mod ships with.
            var bar = new GameObject("bar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(root, false);
            var barRect = (RectTransform)bar.transform;
            barRect.anchorMin = new Vector2(0f, 0.12f);
            barRect.anchorMax = new Vector2(0f, 0.88f);
            barRect.pivot = new Vector2(0f, 0.5f);
            barRect.sizeDelta = new Vector2(4f, 0f);
            barRect.anchoredPosition = new Vector2(2f, 0f);
            view.Bar = bar.GetComponent<Image>();
            view.Bar.raycastTarget = false;

            var key = Label("", root, 10f, RowHeight, 15, Color.white);
            view.Key = OneLine(key);
            view.KeyRect = Stretch(key, 0f, 0.5f, 12f, -6f);

            var value = Label("", root, 10f, RowHeight, 14, Color.white);
            view.Value = OneLine(value);
            Stretch(value, 0.5f, 1f, 2f, -(TagColumn + 8f));

            var tag = Label("", root, 10f, RowHeight, 12, Dim);
            view.Tag = OneLine(tag);
            view.Tag.alignment = TextAnchor.MiddleRight;
            Stretch(tag, 1f, 1f, -(TagColumn + 4f), -6f);

            return view;
        }

        private static void BindSetting(SettingView view, SettingItem item)
        {
            view.Item = item;

            if (item.Heading != null)
            {
                view.Background.color = Clearish;
                view.Button.interactable = false;
                view.Bar.enabled = false;
                view.KeyRect.anchorMax = new Vector2(1f, 1f);
                view.Key.text = item.Heading;
                view.Key.font = GUIManager.Instance.AveriaSerifBold;
                view.Key.color = GUIManager.Instance.ValheimOrange;
                view.Value.text = "";
                view.Tag.text = "";
                return;
            }

            var row = item.Row;
            var pin = row.Pin ?? Keeper.Find(row.Id);
            var kept = pin != null;
            var current = row.Setting != null ? row.Setting.Current : pin?.Value;
            var differs = row.Setting != null && current != null && row.Setting.Default != null && current != row.Setting.Default;

            view.Background.color = row.Id == _selectedId ? Selected : kept ? KeptRow : Clearish;
            view.Button.interactable = true;
            view.Bar.enabled = differs;
            view.Bar.color = DefaultBar;

            view.KeyRect.anchorMax = new Vector2(0.5f, 1f);
            view.Key.text = row.Key;
            view.Key.font = GUIManager.Instance.AveriaSerif;
            view.Key.color = kept ? Kept : Color.white;

            view.Value.text = KeyLabels.Shown(row.Setting, current) ?? "";
            view.Value.color = differs || kept ? Color.white : Dim;

            view.Tag.text = row.Setting == null ? "not loaded"
                : kept ? (row.Setting.LeftToBindrune ? "waiting" : Keeper.ProfileChangeOf(row.Id) != null ? "updated" : "kept")
                : Session.IsChanged(row.Setting) ? "changed" : "";
            view.Tag.color = kept ? Kept : Dim;
        }

        /// <summary>Moves the highlight and shows the setting on the right, without redrawing the lists.</summary>
        private static void Select(string id)
        {
            KeyCapture.Cancel();
            _selectedId = id;
            _showBindruneOffer = false;
            _settingList?.Rebind();

            _note = null;
            ShowDetail();
            ShowNote();
        }

        // ---------- placement ----------

        /// <summary>
        /// Text that stays on one line and is cut off at the end of its cell. Rich text is off
        /// unless asked for, since names and values are shown as they are, and a value such as
        /// &lt;Keyboard&gt;/f would otherwise be read as markup.
        /// </summary>
        private static Text OneLine(GameObject go, bool richText = false)
        {
            var text = go.GetComponent<Text>();
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = richText;
            return text;
        }

        /// <summary>
        /// Spans a widget across its row from one fraction of the width to another, inset by the
        /// given offsets, so the columns follow the panel as it is resized.
        /// </summary>
        private static RectTransform Stretch(GameObject go, float from, float to, float left, float right)
        {
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(from, 0f);
            rect.anchorMax = new Vector2(to, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(left, 0f);
            rect.offsetMax = new Vector2(right, 0f);
            return rect;
        }
    }
}
