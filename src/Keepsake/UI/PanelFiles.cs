using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace Keepsake.UI
{
    /// <summary>
    /// The Files place: the files and folders mods keep in BepInEx/config besides their settings,
    /// such as a timer per world, and the right column for keeping one whole. See FileKeeper.
    /// </summary>
    public static partial class KeepsakePanel
    {
        /// <summary>Which files the list shows. Mods ship textures by the hundred, which are rarely anything to keep.</summary>
        private enum FileFilter
        {
            NoImages,
            ImagesOnly,
            All,
        }

        private static readonly string[] FileFilterNames = { "No images", "Images only", "All files" };

        private static FileFilter _fileFilter = FileFilter.NoImages;
        private static GameObject _fileFilterObject;

        private const float FileFilterWidth = 150f;

        /// <summary>What BepInEx/config holds, read in the background as the panel opens.</summary>
        private static List<ConfigItem> _configItems;

        private static Task<List<ConfigItem>> _configItemsReading;

        /// <summary>
        /// Starts reading BepInEx/config on another thread, so opening the panel does not wait for
        /// it. The Files count on the left fills in once it is done.
        /// </summary>
        private static void StartReadingFiles()
        {
            var settingsFiles = new HashSet<string>(SettingIndex.All.Select(s => s.File), StringComparer.OrdinalIgnoreCase);
            _configItems = null;
            _configItemsReading = Task.Run(() => FileKeeper.Browse(settingsFiles));
        }

        /// <summary>What BepInEx/config holds; waits for the reading when the Files list is wanted before it is done.</summary>
        private static List<ConfigItem> ConfigItems
        {
            get
            {
                if (_configItems != null) return _configItems;
                if (_configItemsReading == null) StartReadingFiles();

                try
                {
                    _configItems = _configItemsReading.Result;
                }
                catch (Exception ex)
                {
                    Plugin.WarnOnce($"Keepsake: could not look through BepInEx/config: {ex.GetBaseException().Message}", ex);
                    _configItems = new List<ConfigItem>();
                }

                _configItemsReading = null;
                return _configItems;
            }
        }

        /// <summary>Called every frame while the panel is open: shows the Files count once the reading is done.</summary>
        private static void TickFiles()
        {
            if (_configItems != null || _configItemsReading == null || !_configItemsReading.IsCompleted) return;
            if (ConfigItems != null) RedrawLists();
        }

        private static SourceItem FilesSourceItem()
        {
            var item = new SourceItem { Text = "Files", Source = Source.Files, Count = FileKeeper.Kept.Count, HasKept = true };

            // Kept files out of all of them, once they are counted.
            if (_configItems != null)
                item.CountText = $"{_configItems.Count(i => !i.IsFolder && FileKeeper.KeptBy(i.Path) != null)}/{_configItems.Count(i => !i.IsFolder)}";
            return item;
        }

        private static bool IsFileId(string id) => id != null && id.StartsWith("file:", StringComparison.Ordinal);

        private static bool FileShows(ConfigItem item) =>
            _fileFilter == FileFilter.All || (_fileFilter == FileFilter.ImagesOnly) == FileKeeper.IsImage(item.Path);

        private static bool FolderShows(ConfigItem folder) =>
            _fileFilter == FileFilter.All ||
            (_fileFilter == FileFilter.ImagesOnly ? folder.Images > 0 : folder.Files > folder.Images);

        /// <summary>
        /// The files the filter and the search let through, each under its folder's row, and the
        /// folders whose own path matches. A folder's row stays over any file of it that is listed.
        /// </summary>
        private static List<Row> FileRows(string[] words)
        {
            var items = ConfigItems;
            var files = new HashSet<ConfigItem>(items.Where(i => !i.IsFolder && FileShows(i) &&
                                                                 (words.Length == 0 || Matches(i.Path.ToLowerInvariant(), words))));
            var parents = new HashSet<string>(files.Select(f => f.Parent), StringComparer.OrdinalIgnoreCase);

            return items
                .Where(i => i.IsFolder
                    ? parents.Contains(i.Path) || FolderShows(i) && (words.Length == 0 || Matches(i.Path.ToLowerInvariant(), words))
                    : files.Contains(i))
                .Select(i => new Row { Id = i.Id, Item = i })
                .ToList();
        }

        private static void UpdateFilesHeader(List<Row> rows, bool searching)
        {
            var files = ConfigItems.Count(i => !i.IsFolder);
            var listed = rows.Count(r => !r.Item.IsFolder);
            var kept = FileKeeper.Kept.Count;
            _headerTitle.text = "Files";
            _headerInfo.text = (searching || _fileFilter != FileFilter.All
                                   ? $"{listed} of {Plural(files, "file")} in BepInEx/config listed"
                                   : Plural(files, "file") + " in BepInEx/config besides the mods' settings") +
                               (kept > 0 ? $", {kept} kept" : "");
        }

        /// <summary>The filter over the Files list, in the header beside where the buttons go.</summary>
        private static void BuildFileFilter(Transform strip)
        {
            _fileFilterObject = GUIManager.Instance.CreateDropDown(strip, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, 14, FileFilterWidth, 30f);
            var dropdown = _fileFilterObject.GetComponent<Dropdown>();
            dropdown.ClearOptions();
            dropdown.AddOptions(FileFilterNames.ToList());
            dropdown.SetValueWithoutNotify((int)_fileFilter);
            dropdown.onValueChanged.AddListener(i =>
            {
                _fileFilter = (FileFilter)Mathf.Clamp(i, 0, FileFilterNames.Length - 1);
                RequestPopulate(reset: true);
            });
            _fileFilterObject.SetActive(false);
        }

        /// <summary>Shows the filter over the Files list only, at the right edge. Returns the room it takes.</summary>
        private static float PlaceFileFilter(float right)
        {
            if (_fileFilterObject == null) return 0f;

            var shown = _source == Source.Files;
            _fileFilterObject.SetActive(shown);
            if (!shown) return 0f;

            AnchorRight(_fileFilterObject, -right, -HeaderHeight / 2f);
            return FileFilterWidth + 8f;
        }

        private static void BindFile(SettingView view, Row row)
        {
            var item = row.Item;
            var by = FileKeeper.KeptBy(item.Path);
            var kept = by != null;

            view.Background.color = row.Id == _selectedId ? Selected : kept ? KeptRow : Clearish;
            view.Button.interactable = true;
            view.Bar.enabled = false;

            if (item.IsFolder)
            {
                // The folder's row heads its files: its whole path, and what is in it on the right.
                view.KeyRect.anchorMax = new Vector2(0.7f, 1f);
                view.ValueRect.anchorMin = new Vector2(0.7f, view.ValueRect.anchorMin.y);
                view.Key.text = item.Path + "/";
                view.Key.font = GUIManager.Instance.AveriaSerifBold;
                view.Key.color = kept ? Kept : GUIManager.Instance.ValheimOrange;
                view.Value.text = $"{Plural(item.Files, "file")}, {SizeOf(item.Size)}";
            }
            else
            {
                view.KeyRect.anchorMax = new Vector2(0.7f, 1f);
                view.KeyRect.offsetMin = new Vector2(item.Parent.Length > 0 ? 32f : 12f, view.KeyRect.offsetMin.y);
                view.ValueRect.anchorMin = new Vector2(0.7f, view.ValueRect.anchorMin.y);
                view.Key.text = item.Name;
                view.Key.font = GUIManager.Instance.AveriaSerif;
                view.Key.color = kept ? Kept : Color.white;
                view.Value.text = SizeOf(item.Size);
            }
            view.Value.color = Dim;

            // Kept with a folder around it reads differently from kept on its own.
            view.Tag.text = !kept ? "" : string.Equals(by.Path, item.Path, StringComparison.OrdinalIgnoreCase) ? "kept" : "in folder";
            view.Tag.color = Kept;
        }

        // ---------- the right column ----------

        /// <summary>An image shown in the right column, freed when the column is redrawn or the panel closes.</summary>
        private static Texture2D _preview;

        private static void ClearPreview()
        {
            if (_preview == null) return;
            UnityEngine.Object.Destroy(_preview);
            _preview = null;
        }

        /// <summary>The right column for a file or folder: what it is, keeping or releasing it, and a look inside.</summary>
        private static void FileDetail(string id)
        {
            var item = ConfigItems.FirstOrDefault(i => i.Id == id);
            var path = item?.Path ?? id.Substring("file:".Length).TrimEnd('/');
            var isFolder = item?.IsFolder ?? id.EndsWith("/");
            var by = FileKeeper.KeptBy(path);
            var keptItself = by != null && string.Equals(by.Path, path, StringComparison.OrdinalIgnoreCase);
            var name = path.Substring(path.LastIndexOf('/') + 1) + (isFolder ? "/" : "");

            var parent = path.Contains("/") ? path.Substring(0, path.LastIndexOf('/')) + "/" : "BepInEx/config";
            Wrapped("in " + parent, _detail, DetailInner, 14, Dim);
            Wrapped(name, _detail, DetailInner, 21, by != null ? Kept : GUIManager.Instance.ValheimOrange, true);

            if (item != null)
            {
                Fact(isFolder ? "Folder" : "File", isFolder ? $"{Plural(item.Files, "file")}, {SizeOf(item.Size)}" : SizeOf(item.Size));
                Fact("Changed", item.Changed.ToString("yyyy-MM-dd HH:mm"));
            }
            else
            {
                Wrapped("Not in BepInEx/config right now. Its copy goes back in at the next launch.", _detail, DetailInner, 15, Color.white);
            }

            Spacer(10f);
            FileControls(path, isFolder, name, by, keptItself);

            if (item != null && !isFolder) Preview(path);
        }

        private static void FileControls(string path, bool isFolder, string name, KeptPath by, bool keptItself)
        {
            if (by != null && !keptItself)
            {
                Wrapped($"Kept with the folder {by.Path}/, which keeps everything in it.", _detail, DetailInner, 15, Color.white);
                return;
            }

            var row = ButtonRow();
            if (keptItself)
            {
                FixedButton("Release", row, 150f, 34f, () => Act(() =>
                {
                    FileKeeper.Release(path);
                    return null;
                }, Sfx.Released, () => $"{name} is no longer kept. It stays as it is until the next profile sync."));

                Wrapped("Kept. Keepsake saves a copy of " + (isFolder ? "every file in it" : "it") + " while the game runs, and puts the " +
                        "copy back at launch if a profile sync replaced or removed it. Release stops that and removes the copies.",
                    _detail, DetailInner, 13, Dim);
                return;
            }

            FixedButton("Keep", row, 150f, 34f, () => Act(() => FileKeeper.Keep(path, isFolder), Sfx.Kept,
                () => $"{name} is kept. A profile sync can no longer replace or remove it."));

            Wrapped((isFolder
                        ? "Keeping a folder holds every file in it through profile syncs, and every file a mod adds to it later: "
                        : "Keeping a file holds it through profile syncs: ") +
                    "Keepsake saves a copy while the game runs and puts it back at launch if a sync replaced or removed it. " +
                    "Mods that keep their state in files, such as a timer per world, carry on where you left them. " +
                    "At launch your copy wins, so change a kept file while the game runs.",
                _detail, DetailInner, 13, Dim);
        }

        // ---------- preview ----------

        private const long PreviewImageBytes = 8 * 1024 * 1024;

        /// <summary>How much of a text file the viewer shows, and how tall it is.</summary>
        private const int ViewerBytes = 64 * 1024;
        private const float ViewerHeight = 320f;

        /// <summary>Characters per text block in the viewer: Unity's text mesh holds about 16,000 at most.</summary>
        private const int ViewerBlock = 4000;

        private static readonly Color ViewerBackground = new Color(0.07f, 0.06f, 0.05f, 0.94f);
        private static readonly Color ViewerText = new Color(0.87f, 0.85f, 0.79f);

        private static Font _viewerFont;

        /// <summary>A fixed width font from the system, so a file reads as a file; the panel's own font where there is none.</summary>
        private static Font ViewerFont
        {
            get
            {
                if (_viewerFont != null) return _viewerFont;
                try
                {
                    _viewerFont = Font.CreateDynamicFontFromOSFont(
                        new[] { "Consolas", "Cascadia Mono", "Lucida Console", "Courier New", "DejaVu Sans Mono", "Liberation Mono" }, 13);
                }
                catch (Exception ex)
                {
                    Plugin.WarnOnce($"Keepsake: no fixed width font was found for showing files: {ex.Message}", ex);
                }
                return _viewerFont != null ? _viewerFont : GUIManager.Instance.AveriaSerif;
            }
        }

        /// <summary>A look inside a file: a text file whole, in a viewer of its own, or an image.</summary>
        private static void Preview(string path)
        {
            Spacer(10f);

            try
            {
                var full = KeptFiles.Live(path);
                var length = new FileInfo(full).Length;

                if (FileKeeper.IsImage(path))
                {
                    Wrapped("Preview", _detail, DetailInner, 13, Dim);
                    ImagePreview(path, full, length);
                    return;
                }

                var head = ReadHead(full, ViewerBytes);
                if (Array.IndexOf(head, (byte)0, 0, Math.Min(head.Length, 4096)) < 0)
                {
                    TextViewer(head, length);
                    return;
                }

                Wrapped("A binary file of " + SizeOf(length) + ", with nothing to show as text.", _detail, DetailInner, 13, Dim);
            }
            catch (Exception ex)
            {
                Wrapped("Could not be read: " + ex.Message, _detail, DetailInner, 13, Dim);
            }
        }

        private static byte[] ReadHead(string full, int max)
        {
            using (var stream = File.OpenRead(full))
            {
                var buffer = new byte[(int)Math.Min(max, stream.Length)];
                var read = 0;
                while (read < buffer.Length)
                {
                    var n = stream.Read(buffer, read, buffer.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                return read == buffer.Length ? buffer : buffer.Take(read).ToArray();
            }
        }

        /// <summary>
        /// A text file in a scroll box of its own, set apart from the panel by its background and a
        /// fixed width font. Read only; shown as it is, markup and all.
        /// </summary>
        private static void TextViewer(byte[] bytes, long length)
        {
            var cut = length > bytes.Length;
            var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ");

            // A cut file ends on its last whole line, and no line is left half a character.
            if (cut && text.LastIndexOf('\n') > 0) text = text.Substring(0, text.LastIndexOf('\n'));
            var lines = text.TrimEnd('\n').Split('\n');

            Wrapped($"{Plural(lines.Length, "line")}, {SizeOf(length)}" + (cut ? $", the first {SizeOf(bytes.Length)} shown" : ""),
                _detail, DetailInner, 12, Dim);

            var box = GUIManager.Instance.CreateScrollView(_detail, false, true, 6f, 4f,
                GUIManager.Instance.ValheimScrollbarHandleColorBlock, new Color(0f, 0f, 0f, 0.3f), DetailInner, ViewerHeight);
            Fix(box, DetailInner, ViewerHeight);
            var background = box.GetComponent<Image>() ?? box.AddComponent<Image>();
            background.color = ViewerBackground;

            var scroll = box.GetComponent<ScrollRect>() ?? box.GetComponentInChildren<ScrollRect>(true);
            if (scroll == null) return;
            scroll.scrollSensitivity = 60f;

            var content = scroll.content;
            var layout = content.GetComponent<VerticalLayoutGroup>() ?? content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(10, 14, 8, 8);
            layout.spacing = 0f;
            var fitter = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (var block in Blocks(lines))
            {
                var go = new GameObject("text", typeof(RectTransform), typeof(Text));
                go.transform.SetParent(content, false);
                var label = go.GetComponent<Text>();
                label.font = ViewerFont;
                label.fontSize = 13;
                label.color = ViewerText;
                label.supportRichText = false;
                label.alignment = TextAnchor.UpperLeft;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                label.raycastTarget = false;
                label.text = block;
            }
        }

        /// <summary>The lines in blocks of at most ViewerBlock characters, a longer line split across blocks.</summary>
        private static IEnumerable<string> Blocks(string[] lines)
        {
            var block = new StringBuilder();
            foreach (var line in lines)
            {
                for (var start = 0; start == 0 || start < line.Length; start += ViewerBlock)
                {
                    var piece = line.Length <= ViewerBlock ? line : line.Substring(start, Math.Min(ViewerBlock, line.Length - start));
                    if (block.Length > 0 && block.Length + piece.Length + 1 > ViewerBlock)
                    {
                        yield return block.ToString();
                        block.Length = 0;
                    }
                    if (block.Length > 0) block.Append('\n');
                    block.Append(piece);
                    if (line.Length <= ViewerBlock) break;
                }
            }
            if (block.Length > 0) yield return block.ToString();
        }
        private static void ImagePreview(string path, string full, long length)
        {
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg")
            {
                Wrapped("No preview for this kind of image.", _detail, DetailInner, 13, Dim);
                return;
            }
            if (length > PreviewImageBytes)
            {
                Wrapped("Too large to show here.", _detail, DetailInner, 13, Dim);
                return;
            }

            ClearPreview();
            var texture = new Texture2D(2, 2);
            if (!LoadImage(texture, File.ReadAllBytes(full)))
            {
                UnityEngine.Object.Destroy(texture);
                Wrapped("The game cannot read this image.", _detail, DetailInner, 13, Dim);
                return;
            }
            _preview = texture;

            // Fit into the column, and let small textures grow a little so they can be made out.
            var scale = Mathf.Min(DetailInner / texture.width, 240f / texture.height, 4f);
            var go = new GameObject("preview", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(_detail, false);
            var image = go.GetComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            Fix(go, texture.width * scale, texture.height * scale);

            Wrapped($"{texture.width} x {texture.height}", _detail, DetailInner, 12, Dim);
        }

        private static System.Reflection.MethodInfo _loadImage;
        private static bool _loadImageLooked;

        /// <summary>
        /// Unity's ImageConversion.LoadImage, looked up once by name. Its assembly is built against
        /// a newer netstandard than this plugin can reference, so it is called without one.
        /// </summary>
        private static bool LoadImage(Texture2D texture, byte[] bytes)
        {
            if (!_loadImageLooked)
            {
                _loadImageLooked = true;
                var type = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                _loadImage = type?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                if (_loadImage == null) Plugin.WarnOnce("Keepsake: the game's image loading was not found, so images are not previewed.");
            }

            return _loadImage != null && (bool)_loadImage.Invoke(null, new object[] { texture, bytes });
        }

        private static string SizeOf(long bytes) =>
            bytes < 1024 ? $"{bytes} B" :
            bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" :
            $"{bytes / (1024.0 * 1024.0):0.#} MB";
    }
}
