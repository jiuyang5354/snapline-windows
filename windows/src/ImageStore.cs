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
        [DataMember] public List<string> Paths = new List<string>();
        [DataMember] public uint HotkeyKey = Hotkey.DefaultKey;
        [DataMember] public uint HotkeyModifiers = Hotkey.DefaultModifiers;
        [DataMember] public bool CollectionPaused;
        [DataMember] public bool AutoCheckUpdates = true;
        [DataMember] public long UpdateCheckedUtcTicks;
        [DataMember] public string UpdateNotifiedTag;

        [OnDeserializing]
        private void HotkeyDefaults(StreamingContext context)
        {
            HotkeyKey = Hotkey.DefaultKey;
            HotkeyModifiers = Hotkey.DefaultModifiers;
            AutoCheckUpdates = true;
        }
    }

    internal sealed class Shot
    {
        internal string Path;
        internal string Hash;
        internal BitmapSource Thumbnail;
        internal double Tilt;
    }

    internal sealed class ImageStore
    {
        internal const int MaxItems = 12;
        internal readonly List<Shot> Items = new List<Shot>();
        internal readonly string Root;
        internal readonly string Inbox;
        internal Settings Settings;
        internal event Action Changed;
        private readonly Random random = new Random();
        private readonly string stateFile;

        internal ImageStore(string root)
        {
            Root = System.IO.Path.GetFullPath(root);
            Inbox = System.IO.Path.Combine(Root, "Inbox");
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
            var paths = Settings.Paths ?? new List<string>();
            foreach (var path in paths)
            {
                try { AddFile(path, false); }
                catch (Exception ex) { if (!IsImageError(ex)) throw; }
            }
        }

        internal static bool IsImageError(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException ||
                ex is FileFormatException || ex is ArgumentException || ex is System.Runtime.InteropServices.COMException;
        }

        internal static bool IsImage(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" ||
                ext == ".gif" || ext == ".tif" || ext == ".tiff";
        }

        internal static BitmapSource Load(string path, int thumbnailWidth)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                if (thumbnailWidth > 0) image.DecodePixelWidth = thumbnailWidth;
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

        internal static string Digest(BitmapSource image)
        {
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int stride = checked(converted.PixelWidth * 4);
            var bytes = new byte[checked(stride * converted.PixelHeight)];
            converted.CopyPixels(bytes, stride, 0);
            using (var hash = SHA256.Create())
                return image.PixelWidth + "x" + image.PixelHeight + ":" + Convert.ToBase64String(hash.ComputeHash(bytes));
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
            var image = Load(path, 0);
            string hash = Digest(image);
            var existing = Items.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (existing.Hash == hash) return null;
                existing.Hash = hash;
                existing.Thumbnail = Load(path, Math.Min(480, image.PixelWidth));
                if (notify) Publish();
                return existing;
            }
            if (Items.Any(item => item.Hash == hash)) return null;
            var shot = new Shot { Path = path, Hash = hash, Thumbnail = Load(path, Math.Min(480, image.PixelWidth)), Tilt = random.NextDouble() * 5 - 2.5 };
            Items.Add(shot);
            while (Items.Count > MaxItems) Items.RemoveAt(0);
            if (notify) Publish();
            return shot;
        }

        internal bool Owns(string path)
        {
            path = System.IO.Path.GetFullPath(path);
            return string.Equals(System.IO.Path.GetDirectoryName(path), Inbox, StringComparison.OrdinalIgnoreCase) &&
                !new FileInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint) &&
                !new DirectoryInfo(Inbox).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }

        internal void Remove(Shot shot, bool recycle)
        {
            if (recycle && File.Exists(shot.Path) && Owns(shot.Path))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(shot.Path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                    Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
            Items.Remove(shot);
            Publish();
        }

        internal void Clear() { Items.Clear(); Publish(); }

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

        private void Publish() { Save(); if (Changed != null) Changed(); }
    }
}
