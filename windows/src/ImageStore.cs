using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapline
{
    [DataContract]
    internal sealed class Settings
    {
        [DataMember] public bool ListenClipboard = true;
        [DataMember] public string WatchFolder = Native.ScreenshotsFolder();
        [DataMember] public string InboxFolder;
        [DataMember] public List<string> Paths = new List<string>();
        [DataMember] public uint HotkeyKey = Hotkey.DefaultKey;
        [DataMember] public uint HotkeyModifiers = Hotkey.DefaultModifiers;
        [DataMember] public uint CaptureHotkeyKey = Hotkey.DefaultCaptureKey;
        [DataMember] public uint CaptureHotkeyModifiers = Hotkey.DefaultModifiers;
        [DataMember] public bool CollectionPaused;
        [DataMember] public bool ShowOnCapture = true;
        [DataMember] public string Appearance;
        [DataMember] public bool RemoveOnBlankDrop = true;
        [DataMember] public uint ClearHotkeyKey = Hotkey.DefaultClearKey;
        [DataMember] public uint ClearHotkeyModifiers = Hotkey.DefaultModifiers;
        [DataMember] public bool AutoCheckUpdates = true;
        [DataMember] public bool IncludeCursor;
        [DataMember] public long UpdateCheckedUtcTicks;
        [DataMember] public string UpdateNotifiedTag;
        [DataMember] public string AnnouncementSeenVersion;
        [DataMember] public bool FileShelfEnabled = true;
        [DataMember] public bool FileShelfAnyApp;
        [DataMember] public bool FileShelfKeepAfterDragOut;

        [OnDeserializing]
        private void HotkeyDefaults(StreamingContext context)
        {
            HotkeyKey = Hotkey.DefaultKey;
            HotkeyModifiers = Hotkey.DefaultModifiers;
            CaptureHotkeyKey = Hotkey.DefaultCaptureKey;
            CaptureHotkeyModifiers = Hotkey.DefaultModifiers;
            ClearHotkeyKey = Hotkey.DefaultClearKey;
            ClearHotkeyModifiers = Hotkey.DefaultModifiers;
            AutoCheckUpdates = true;
            ShowOnCapture = true;
            FileShelfEnabled = true;
            RemoveOnBlankDrop = true;
        }
    }

    internal sealed class Shot
    {
        internal string Path;
        internal string Hash;
        internal BitmapSource Thumbnail;
        internal int ThumbnailWidth, ThumbnailHeight;
        internal BitmapSource GetThumbnail()
        { return Thumbnail ?? (Thumbnail = ImageStore.Load(Path, ThumbnailWidth, ThumbnailHeight)); }
        internal double Tilt;
    }

    internal sealed class ImageStore
    {
        internal const int ItemsPerGroup = 9;
        internal readonly List<Shot> Items = new List<Shot>();
        internal readonly string Root;
        internal readonly string DefaultInbox;
        internal string Inbox { get; private set; }
        internal Settings Settings;
        internal event Action Changed;
        private readonly Random random = new Random();
        private readonly string stateFile;

        internal ImageStore(string root)
        {
            Root = System.IO.Path.GetFullPath(root);
            DefaultInbox = System.IO.Path.Combine(Root, "Inbox");
            Inbox = DefaultInbox;
            stateFile = System.IO.Path.Combine(Root, "settings.json");
            Directory.CreateDirectory(Inbox);
            Settings = new Settings();
            if (File.Exists(stateFile))
            {
                try
                {
                    using (var stream = File.OpenRead(stateFile))
                        Settings = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);
                }
                catch (IOException) { Settings = new Settings(); }
                catch (SerializationException) { Settings = new Settings(); }
            }
            string prepared, error;
            if (!string.IsNullOrEmpty(Settings.InboxFolder) && TryPrepareInbox(Settings.InboxFolder, out prepared, out error)) Inbox = prepared;
            var paths = Settings.Paths ?? new List<string>();
            StartupDiagnostics.RestoreTotal = paths.Count; StartupDiagnostics.RestoredCount = 0;
            StartupDiagnostics.RestoreErrors = 0; StartupDiagnostics.RestoreError = null;
            foreach (var path in paths)
            {
                try { AddFile(path, false); StartupDiagnostics.RestoredCount = Items.Count; }
                catch (Exception ex) {
                    if (!IsImageError(ex)) throw;
                    StartupDiagnostics.RestoreErrors++;
                    if (StartupDiagnostics.RestoreError == null) StartupDiagnostics.RestoreError = ex.GetType().FullName + " (0x" + ex.HResult.ToString("X8") + ")";
                }
            }
        }

        internal static bool IsImageError(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException ||
                ex is FileFormatException || ex is ArgumentException || ex is System.Runtime.InteropServices.COMException;
        }

        internal static bool TryPrepareInbox(string folder, out string fullPath, out string error)
        {
            fullPath = null; error = null;
            try {
                string path = System.IO.Path.GetFullPath(folder);
                if (path.Length > System.IO.Path.GetPathRoot(path).Length) path = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(path);
                string probe = System.IO.Path.Combine(path, ".snapline-write-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) stream.WriteByte(0);
                fullPath = path;
                return true;
            }
            catch (Exception ex) {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException) && !(ex is ArgumentException) &&
                    !(ex is NotSupportedException) && !(ex is System.Security.SecurityException)) throw;
                error = "无法使用截图收件夹，请选择可写的文件夹：" + ex.Message;
                return false;
            }
        }

        internal bool SetInbox(string folder, out string error)
        {
            string prepared;
            if (!TryPrepareInbox(string.IsNullOrEmpty(folder) ? DefaultInbox : folder, out prepared, out error)) return false;
            string setting = string.Equals(prepared, DefaultInbox, StringComparison.OrdinalIgnoreCase) ? null : prepared;
            if (string.Equals(Inbox, prepared, StringComparison.OrdinalIgnoreCase) && Settings.InboxFolder == setting) return true;
            string previous = Inbox, previousSetting = Settings.InboxFolder;
            Inbox = prepared; Settings.InboxFolder = setting;
            try { Save(); return true; }
            catch (Exception ex) {
                Inbox = previous; Settings.InboxFolder = previousSetting;
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                error = "无法保存收件夹位置，请检查数据文件夹是否可写后再试。";
                return false;
            }
        }

        internal static bool IsImage(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" ||
                ext == ".gif" || ext == ".tif" || ext == ".tiff";
        }

        internal static BitmapSource Load(string path, int thumbnailWidth, int thumbnailHeight = 0)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                if (thumbnailWidth > 0) image.DecodePixelWidth = thumbnailWidth;
                if (thumbnailHeight > 0) image.DecodePixelHeight = thumbnailHeight;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }

        internal static byte[] Png(BitmapSource image)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = new MemoryStream()) { encoder.Save(stream); return stream.ToArray(); }
        }

        private static string DigestFile(string path, out int width, out int height)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var image = new BitmapImage();
                image.BeginInit();
                // Hash through the open stream without retaining a full decoded bitmap.
                image.CacheOption = BitmapCacheOption.None;
                image.StreamSource = stream;
                image.EndInit();
                width = image.PixelWidth; height = image.PixelHeight;
                StartupDiagnostics.ImageWidth = width; StartupDiagnostics.ImageHeight = height;
                return Digest(image);
            }
        }

        internal static string Digest(BitmapSource image)
        {
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int stride = checked(converted.PixelWidth * 4);
            int rows = Math.Min(converted.PixelHeight, Math.Max(1, 262144 / stride));
            var bytes = new byte[checked(stride * rows)];
            using (var hash = SHA256.Create())
            {
                for (int y = 0; y < converted.PixelHeight; y += rows)
                {
                    int count = Math.Min(rows, converted.PixelHeight - y);
                    converted.CopyPixels(new System.Windows.Int32Rect(0, y, converted.PixelWidth, count), bytes, stride, 0);
                    hash.TransformBlock(bytes, 0, checked(stride * count), bytes, 0);
                }
                hash.TransformFinalBlock(bytes, 0, 0);
                return image.PixelWidth + "x" + image.PixelHeight + ":" + Convert.ToBase64String(hash.Hash);
            }
        }

        internal Shot AddBitmap(BitmapSource image)
        {
            string hash = Digest(image);
            if (Items.Any(item => item.Hash == hash)) return null;
            string name = "Capture-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".png";
            string path = System.IO.Path.Combine(Inbox, name);
            File.WriteAllBytes(path, Png(image));
            return AddFile(path, true);
        }

        internal Shot AddFile(string path, bool notify)
        {
            if (notify) Prune();
            path = System.IO.Path.GetFullPath(path);
            if (!File.Exists(path) || !IsImage(path)) return null;
            int width, height;
            string hash = DigestFile(path, out width, out height);
            int thumbnailWidth = width >= height ? Math.Min(480, width) : 0;
            int thumbnailHeight = height > width ? Math.Min(480, height) : 0;
            var existing = Items.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (existing.Hash == hash) return null;
                existing.Hash = hash;
                existing.Thumbnail = null;
                existing.ThumbnailWidth = thumbnailWidth; existing.ThumbnailHeight = thumbnailHeight;
                if (notify) Publish();
                return existing;
            }
            if (Items.Any(item => item.Hash == hash)) return null;
            var shot = new Shot { Path = path, Hash = hash, ThumbnailWidth = thumbnailWidth, ThumbnailHeight = thumbnailHeight, Tilt = random.NextDouble() * 5 - 2.5 };
            Items.Add(shot);
            if (notify) Publish();
            return shot;
        }

        internal bool Owns(string path)
        {
            path = System.IO.Path.GetFullPath(path);
            string directory = System.IO.Path.GetDirectoryName(path);
            return (string.Equals(directory, Inbox, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(directory, DefaultInbox, StringComparison.OrdinalIgnoreCase)) &&
                !new FileInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint) &&
                !new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }

        internal void Remove(Shot shot, bool recycle)
        {
            if (recycle && File.Exists(shot.Path) && Owns(shot.Path))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(shot.Path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                    Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
            int index = Items.IndexOf(shot);
            Items.Remove(shot);
            try { Publish(); }
            catch {
                if (!recycle && index >= 0) { Items.Insert(index, shot); Settings.Paths = Items.Select(item => item.Path).ToList(); }
                throw;
            }
        }

        internal void Clear()
        {
            var previous = Items.ToArray();
            Items.Clear();
            try { Publish(); }
            catch { Items.AddRange(previous); Settings.Paths = Items.Select(item => item.Path).ToList(); throw; }
        }

        internal void Prune()
        {
            if (Items.RemoveAll(item => !File.Exists(item.Path)) > 0) Publish();
        }

        internal void Save()
        {
            Settings.Paths = Items.Select(item => item.Path).ToList();
            string temporary = stateFile + ".tmp";
            using (var stream = File.Create(temporary))
                new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, Settings);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(stateFile)) File.Replace(temporary, stateFile, null);
                    else File.Move(temporary, stateFile);
                    break;
                }
                catch (IOException ex)
                {
                    int error = ex.HResult & 0xffff;
                    if (attempt == 2 || (error != 32 && error != 1175)) throw;
                    System.Threading.Thread.Sleep(80);
                }
            }
        }

        internal void RemapPaths(IDictionary<string, string> changes)
        {
            var previous = Items.Select(item => item.Path).ToArray();
            foreach (var item in Items) { string path; if (changes.TryGetValue(item.Path, out path)) item.Path = path; }
            try { Save(); }
            catch { for (int i = 0; i < Items.Count; i++) Items[i].Path = previous[i]; Settings.Paths = previous.ToList(); throw; }
            if (Changed != null) Changed();
        }

        private void Publish() { Save(); if (Changed != null) Changed(); }
    }
}
