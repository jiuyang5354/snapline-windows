using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapline
{
    internal enum FileTool { Rename, Convert, Compress, Zip, Pdf }

    internal sealed class RenameEntry
    {
        public string Source { get; internal set; }
        public string Target { get; internal set; }
        public string Before { get { return Path.GetFileName(Source); } }
        public string After { get { return Path.GetFileName(Target); } }
    }

    internal sealed class BatchResult
    {
        internal readonly List<string> Outputs = new List<string>();
        internal readonly List<string> Errors = new List<string>();
        internal long InputBytes, OutputBytes;
        internal bool Cancelled;
    }

    internal static class FileBatch
    {
        internal static string[] Paths(IEnumerable<string> paths)
        {
            return paths.Select(path => { string full = Path.GetFullPath(path); return full.Length > Path.GetPathRoot(full).Length ? full.TrimEnd('\\', '/') : full; })
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        internal static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(".") || name.EndsWith(" "))
                throw new IOException("文件名为空或含有 Windows 不允许的字符、结尾空格或句点。");
            string stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789¹²³".IndexOf(stem[3]) >= 0))
                throw new IOException("不能使用 Windows 保留文件名：" + name);
        }

        internal static List<RenameEntry> PlanRename(IEnumerable<string> paths, string template, int start, int digits)
        {
            if (start < 0 || digits < 1 || digits > 9) throw new ArgumentException("起始编号须不小于 0，位数须为 1–9。");
            var entries = new List<RenameEntry>();
            foreach (string path in Paths(paths)) {
                if (!File.Exists(path)) throw new IOException("仅支持重命名现有文件，不支持文件夹：" + path);
                string name = template.Replace("{name}", Path.GetFileNameWithoutExtension(path))
                    .Replace("{n}", checked(start + entries.Count).ToString("D" + digits, CultureInfo.InvariantCulture)) + Path.GetExtension(path);
                ValidateName(name);
                entries.Add(new RenameEntry { Source = path, Target = Path.Combine(Path.GetDirectoryName(path), name) });
            }
            ValidateRename(entries);
            return entries;
        }

        private static void ValidateRename(IList<RenameEntry> entries)
        {
            var sources = new HashSet<string>(entries.Select(e => e.Source), StringComparer.OrdinalIgnoreCase);
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries) {
                if (!File.Exists(entry.Source)) throw new IOException("原文件已不存在：" + entry.Source);
                if (!targets.Add(entry.Target)) throw new IOException("新名称重复，请在模板中加入 {n}：" + entry.After);
                if (Directory.Exists(entry.Target) || (File.Exists(entry.Target) && !sources.Contains(entry.Target)))
                    throw new IOException("目标名称已存在，不会覆盖：" + entry.Target);
            }
        }

        // Stage all names first so swaps and case-only changes work without overwriting files.
        internal static void Rename(IList<RenameEntry> entries, Action<IDictionary<string, string>> commit)
        {
            ValidateRename(entries);
            var moving = entries.Where(e => !string.Equals(e.Source, e.Target, StringComparison.Ordinal)).ToArray();
            var staged = new Dictionary<RenameEntry, string>();
            var completed = new HashSet<RenameEntry>();
            try {
                foreach (var entry in moving) {
                    string temporary = Path.Combine(Path.GetDirectoryName(entry.Source), ".snapline-rename-" + Guid.NewGuid().ToString("N"));
                    File.Move(entry.Source, temporary); staged.Add(entry, temporary);
                }
                foreach (var entry in moving) { File.Move(staged[entry], entry.Target); completed.Add(entry); }
                commit(moving.ToDictionary(e => e.Source, e => e.Target, StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception failure) {
                var errors = new List<string>();
                foreach (var entry in moving.Where(completed.Contains)) {
                    try { File.Move(entry.Target, staged[entry]); }
                    catch (IOException ex) { errors.Add(entry.Target + ": " + ex.Message); }
                    catch (UnauthorizedAccessException ex) { errors.Add(entry.Target + ": " + ex.Message); }
                }
                foreach (var pair in staged) {
                    try { if (File.Exists(pair.Value)) File.Move(pair.Value, pair.Key.Source); }
                    catch (IOException ex) { errors.Add(pair.Value + ": " + ex.Message); }
                    catch (UnauthorizedAccessException ex) { errors.Add(pair.Value + ": " + ex.Message); }
                }
                if (errors.Count > 0) throw new IOException("重命名失败，部分文件未能恢复。请保留以下路径中的文件：\n" + string.Join("\n", errors), failure);
                throw;
            }
        }

        internal static FileStream CreateUnique(string directory, string name, out string path)
        {
            path = null;
            ValidateName(name);
            string stem = Path.GetFileNameWithoutExtension(name), extension = Path.GetExtension(name);
            for (int n = 1; ; n++) {
                string candidate = Path.Combine(directory, stem + (n == 1 ? "" : " (" + n + ")") + extension);
                try { var stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None); path = candidate; return stream; }
                catch (IOException) { if (!File.Exists(candidate) && !Directory.Exists(candidate)) throw; }
            }
        }

        internal static string Extension(string format)
        {
            switch (format) {
                case "PNG": return ".png";
                case "JPEG": return ".jpg";
                case "BMP": return ".bmp";
                case "TIFF": return ".tiff";
                case "WebP": case "WebP（无损）": return ".webp";
                case "PDF（每张单独）": case "PDF（合并多页）": return ".pdf";
                default: throw new ArgumentException("不支持的输出格式。");
            }
        }

        internal static bool IsInputImage(string path)
        { return new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".gif", ".webp" }.Contains(Path.GetExtension(path).ToLowerInvariant()); }

        internal static BatchResult Images(IEnumerable<string> paths, string directory, string format, int quality, int maxEdge,
            CancellationToken cancel, Action<int, int> progress, string pdfName = null)
        {
            string extension = Extension(format);
            if (quality < 1 || quality > 100 || maxEdge < 0 || maxEdge > 16384) throw new ArgumentException("质量范围为 1–100，长边范围为 0–16384。");
            if (extension == ".pdf") return PdfBatch.FromImages(paths, directory, format == "PDF（合并多页）", pdfName, maxEdge, cancel, progress);
            Directory.CreateDirectory(directory);
            var result = new BatchResult(); var sources = Paths(paths);
            for (int i = 0; i < sources.Length; i++) {
                if (cancel.IsCancellationRequested) { result.Cancelled = true; break; }
                string source = sources[i], output = null;
                try {
                    using (var input = new ImageInput(source, maxEdge, cancel)) {
                        BitmapSource image = input.Image;
                        if (extension == ".webp") {
                            using (CreateUnique(directory, Path.GetFileNameWithoutExtension(source) + extension, out output)) { }
                            WebpCodec.Encode(image, output, quality, format == "WebP（无损）", cancel);
                        } else {
                        BitmapEncoder encoder;
                        switch (format) {
                            case "JPEG":
                                var drawing = new DrawingVisual();
                                using (var context = drawing.RenderOpen()) {
                                    var bounds = new Rect(0, 0, image.PixelWidth, image.PixelHeight);
                                    context.DrawRectangle(Brushes.White, null, bounds); context.DrawImage(image, bounds);
                                }
                                var flattened = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
                                flattened.Render(drawing); flattened.Freeze();
                                image = new FormatConvertedBitmap(flattened, PixelFormats.Bgr24, null, 0);
                                encoder = new JpegBitmapEncoder { QualityLevel = quality }; break;
                            case "BMP": encoder = new BmpBitmapEncoder(); break;
                            case "TIFF": encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.Zip }; break;
                            default: encoder = new PngBitmapEncoder(); break;
                        }
                        encoder.Frames.Add(BitmapFrame.Create(image));
                        using (var destination = CreateUnique(directory, Path.GetFileNameWithoutExtension(source) + extension, out output)) encoder.Save(destination);
                        }
                    }
                    result.InputBytes += new FileInfo(source).Length; result.OutputBytes += new FileInfo(output).Length;
                    result.Outputs.Add(output);
                }
                catch (OperationCanceledException) {
                    if (output != null && File.Exists(output)) File.Delete(output);
                    result.Cancelled = true; break;
                }
                catch (Exception ex) {
                    if (output != null && File.Exists(output)) File.Delete(output);
                    if (!ImageStore.IsImageError(ex) && !(ex is OutOfMemoryException) && !(ex is OverflowException)) throw;
                    result.Errors.Add(Path.GetFileName(source) + "：" + ex.Message);
                }
                if (progress != null) progress(i + 1, sources.Length);
            }
            return result;
        }

        internal static string Zip(IEnumerable<string> paths, string directory, string name, CancellationToken cancel, Action<string> progress)
        {
            var sources = Paths(paths);
            if (sources.Length == 0) throw new IOException("没有可打包的项目。");
            directory = Paths(new[] { directory })[0];
            foreach (var source in sources) {
                if (!File.Exists(source) && !Directory.Exists(source)) throw new IOException("项目已不存在：" + source);
                if (Directory.Exists(source) && (directory.Equals(source, StringComparison.OrdinalIgnoreCase) ||
                    directory.StartsWith(source.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("ZIP 输出位置不能放在正在打包的文件夹内。");
            }
            ValidateName(name);
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) name += ".zip";
            Directory.CreateDirectory(directory);
            string output = null;
            try {
                using (var stream = CreateUnique(directory, name, out output))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, false, Encoding.UTF8)) {
                    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var source in sources) {
                        string baseName = Path.GetFileName(source.TrimEnd('\\'));
                        if (string.IsNullOrEmpty(baseName)) throw new IOException("请打包具体文件夹，不要直接打包盘符根目录。");
                        string entry = baseName;
                        for (int n = 2; !used.Add(entry); n++) entry = Path.GetFileNameWithoutExtension(baseName) + " (" + n + ")" + Path.GetExtension(baseName);
                        ZipItem(archive, source, entry, cancel, progress);
                    }
                }
                return output;
            }
            catch { if (output != null && File.Exists(output)) File.Delete(output); throw; }
        }

        private static void ZipItem(ZipArchive archive, string source, string entry, CancellationToken cancel, Action<string> progress)
        {
            cancel.ThrowIfCancellationRequested();
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("暂不打包链接或联接目录，请选择实际文件：" + source);
            if (progress != null) progress(entry);
            if (Directory.Exists(source)) {
                archive.CreateEntry(entry + "/");
                foreach (var child in Directory.EnumerateFileSystemEntries(source)) ZipItem(archive, child, entry + "/" + Path.GetFileName(child), cancel, progress);
            }
            else {
                var zipEntry = archive.CreateEntry(entry, CompressionLevel.Optimal);
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = zipEntry.Open()) {
                    var buffer = new byte[81920]; int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0) { cancel.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); }
                }
            }
        }
    }
}
