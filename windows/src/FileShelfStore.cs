// Adapted from jiuyang5354/Dropline- v0.1.0 (MIT), commit 51f2d7c. See NOTICE.md.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapline.Dropline
{
    // One tile on the shelf. A tile can hold one file or a stack of several dropped together.
    internal sealed class ShelfItem
    {
        public ShelfItem(IEnumerable<string> paths)
        {
            Paths = paths.Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public List<string> Paths { get; private set; }
        public bool IsStack { get { return Paths.Count > 1; } }

        public string Title
        {
            get
            {
                if (Paths.Count == 0) return "";
                if (IsStack) return Paths.Count + " 个项目";
                string name = Path.GetFileName(Paths[0].TrimEnd('\\'));
                return string.IsNullOrEmpty(name) ? Paths[0] : name;
            }
        }

        public bool AllExist
        {
            get { return Paths.Count > 0 && Paths.All(p => File.Exists(p) || Directory.Exists(p)); }
        }

        public void PruneMissing()
        {
            Paths.RemoveAll(p => !File.Exists(p) && !Directory.Exists(p));
        }
    }

    internal sealed class ShelfStore
    {
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".ico" };
        private readonly string dataFile;

        public ShelfStore(string root)
        {
            Root = Path.GetFullPath(root);
            Clippings = Path.Combine(Root, "clippings");
            dataFile = Path.Combine(Root, "shelf.txt");
            Items = new ObservableCollection<ShelfItem>();
        }

        public string Root { get; private set; }
        public string Clippings { get; private set; }
        public ObservableCollection<ShelfItem> Items { get; private set; }

        public void Load()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Clippings);
            Items.Clear();
            if (!File.Exists(dataFile)) return;
            foreach (string line in File.ReadLines(dataFile, Encoding.UTF8))
            {
                if (line.Trim().Length == 0) continue;
                var item = new ShelfItem(line.Split('|'));
                item.PruneMissing();
                if (item.Paths.Count > 0) Items.Add(item);
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(Root);
            string temporary = dataFile + ".tmp";
            File.WriteAllLines(temporary, Items.Select(i => string.Join("|", i.Paths)).ToArray(), Encoding.UTF8);
            if (File.Exists(dataFile)) File.Replace(temporary, dataFile, null);
            else File.Move(temporary, dataFile);
        }

        public void Add(IEnumerable<string> paths)
        {
            var item = new ShelfItem(paths.Where(p => File.Exists(p) || Directory.Exists(p)).Select(Path.GetFullPath));
            if (item.Paths.Count == 0) return;
            Items.Insert(0, item);
            try { Save(); }
            catch { Items.Remove(item); throw; }
        }

        public void Remove(ShelfItem item)
        {
            int index = Items.IndexOf(item);
            if (index < 0) return;
            Items.Remove(item);
            try { Save(); }
            catch { Items.Insert(index, item); throw; }
        }

        public void Clear()
        {
            var previous = Items.ToArray();
            Items.Clear();
            try { Save(); }
            catch { foreach (var item in previous) Items.Add(item); throw; }
        }

        public void Split(ShelfItem item)
        {
            int index = Items.IndexOf(item);
            if (index < 0 || !item.IsStack) return;
            var parts = item.Paths.Select(p => new ShelfItem(new[] { p })).ToArray();
            Items.RemoveAt(index);
            for (int i = 0; i < parts.Length; i++) Items.Insert(index + i, parts[i]);
            try { Save(); }
            catch { foreach (var part in parts) Items.Remove(part); Items.Insert(index, item); throw; }
        }

        internal void RemapPaths(IDictionary<string, string> changes)
        {
            var previous = Items.Select(item => item.Paths.ToArray()).ToArray();
            for (int i = 0; i < Items.Count; i++) {
                for (int j = 0; j < Items[i].Paths.Count; j++) {
                    string replacement;
                    if (changes.TryGetValue(Items[i].Paths[j], out replacement)) Items[i].Paths[j] = replacement;
                }
            }
            try { Save(); }
            catch {
                for (int i = 0; i < Items.Count; i++) { Items[i].Paths.Clear(); Items[i].Paths.AddRange(previous[i]); }
                throw;
            }
        }

        // Text or an image dropped from a browser or an editor has no file behind it,
        // so it is written to a clippings folder and the tile points at that file.
        public string SaveText(string text)
        {
            Directory.CreateDirectory(Clippings);
            string firstLine = text.Split('\n')[0].Trim();
            string name = MakeSafeName(firstLine.Length > 24 ? firstLine.Substring(0, 24) : firstLine);
            if (name.Length == 0) name = "文本";
            string path = Unique(Path.Combine(Clippings, name + ".txt"));
            File.WriteAllText(path, text, Encoding.UTF8);
            return path;
        }

        public string SaveLink(Uri uri)
        {
            Directory.CreateDirectory(Clippings);
            string name = MakeSafeName(uri.Host + uri.AbsolutePath.TrimEnd('/').Replace('/', ' '));
            if (name.Length > 40) name = name.Substring(0, 40).Trim();
            string path = Unique(Path.Combine(Clippings, name + ".url"));
            File.WriteAllText(path, "[InternetShortcut]\r\nURL=" + uri.AbsoluteUri + "\r\n", Encoding.UTF8);
            return path;
        }

        public string SaveImage(BitmapSource image)
        {
            Directory.CreateDirectory(Clippings);
            string path = Unique(Path.Combine(Clippings, "图片 " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ".png"));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(path)) encoder.Save(stream);
            return path;
        }

        public static bool IsImage(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ImageExtensions.Contains(ext);
        }

        public static ImageSource Thumbnail(string path, int size)
        {
            try
            {
                if (File.Exists(path) && IsImage(path))
                {
                    int width, height;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                        var header = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                        width = header.PixelWidth; height = header.PixelHeight;
                    }
                    return width >= height ? Snapline.ImageStore.Load(path, Math.Min(width, size * 2)) :
                        Snapline.ImageStore.Load(path, 0, Math.Min(height, size * 2));
                }
            }
            catch (Exception ex) { if (!Snapline.ImageStore.IsImageError(ex) && !(ex is OutOfMemoryException) && !(ex is OverflowException)) throw; }
            return ShellIcon(path);
        }

        public static ImageSource ShellIcon(string path)
        {
            var info = new Native.SHFILEINFO();
            IntPtr result = Native.SHGetFileInfo(path, 0, ref info, (uint)System.Runtime.InteropServices.Marshal.SizeOf(info), Native.SHGFI_ICON | Native.SHGFI_LARGEICON);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                Native.DestroyIcon(info.hIcon);
            }
        }

        private static string MakeSafeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, ' ');
            name = name.Trim().TrimEnd('.');
            string stem = name.Split('.')[0].ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] >= '1' && stem[3] <= '9')) name = "_" + name;
            return name;
        }

        private static string Unique(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string stem = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 2; ; i++)
            {
                string candidate = Path.Combine(dir, stem + " (" + i + ")" + ext);
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            }
        }
    }
}
