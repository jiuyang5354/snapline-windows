using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;

namespace Snapline
{
    internal static class WebpCodec
    {
        internal static bool Available { get { return Environment.Is64BitOperatingSystem && File.Exists(Tool("cwebp")) && File.Exists(Tool("dwebp")); } }
        private static string Tool(string name) { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Converters", "webp", name + ".exe"); }
        private static string Quote(string path) { return "\"" + path + "\""; }
        private static string Temporary()
        {
            string path = Path.Combine(Path.GetTempPath(), "Snapline-image-" + Guid.NewGuid().ToString("N") + ".png");
            using (new FileStream(path, FileMode.CreateNew, FileAccess.Write)) { }
            return path;
        }
        private static void Run(string tool, string arguments, CancellationToken cancel)
        {
            if (!Available) throw new IOException("WebP 工具需要 64 位 Windows；请确认程序的 Converters 文件夹完整。");
            cancel.ThrowIfCancellationRequested();
            using (var process = new Process { StartInfo = new ProcessStartInfo(Tool(tool), arguments) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true
            } }) {
                try { process.Start(); }
                catch (System.ComponentModel.Win32Exception ex) { throw new IOException("无法启动本地 WebP 工具，请检查程序文件是否完整。", ex); }
                var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
                while (!process.WaitForExit(50)) if (cancel.IsCancellationRequested) {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    process.WaitForExit(); cancel.ThrowIfCancellationRequested();
                }
                string detail = error.GetAwaiter().GetResult(); output.GetAwaiter().GetResult();
                cancel.ThrowIfCancellationRequested();
                if (process.ExitCode != 0) throw new IOException("WebP 转换失败：" + detail.Trim());
            }
        }
        internal static void Encode(BitmapSource image, string destination, int quality, bool lossless, CancellationToken cancel)
        {
            string temporary = Temporary();
            try {
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using (var stream = File.Create(temporary)) encoder.Save(stream);
                Run("cwebp", "-quiet " + (lossless ? "-lossless -exact" : "-q " + quality + " -alpha_q 100") + " " + Quote(temporary) + " -o " + Quote(destination), cancel);
            } finally { File.Delete(temporary); }
        }
        internal static string Decode(string source, CancellationToken cancel)
        {
            ValidateHeader(source);
            string temporary = Temporary();
            try { Run("dwebp", "-quiet " + Quote(source) + " -o " + Quote(temporary), cancel); return temporary; }
            catch { File.Delete(temporary); throw; }
        }
        private static int U24(BinaryReader reader) { return reader.ReadByte() | reader.ReadByte() << 8 | reader.ReadByte() << 16; }
        private static void ValidateHeader(string path)
        {
            using (var stream = File.OpenRead(path)) using (var reader = new BinaryReader(stream)) {
                if (stream.Length < 20 || reader.ReadUInt32() != 0x46464952) throw new IOException("WebP 文件头无效。");
                long end = 8L + reader.ReadUInt32();
                if (end > stream.Length || reader.ReadUInt32() != 0x50424557) throw new IOException("WebP 文件不完整。");
                int canvasWidth = 0, canvasHeight = 0;
                while (stream.Position + 8 <= end) {
                    uint kind = reader.ReadUInt32(), length = reader.ReadUInt32(); long next = stream.Position + length + (length & 1);
                    if (next > end) throw new IOException("WebP 数据块不完整。");
                    if (kind == 0x4d494e41 || kind == 0x464d4e41) throw new IOException("动画 WebP 暂不转换，避免丢失动画帧。");
                    int width = 0, height = 0;
                    if (kind == 0x58385056 && length >= 10) {
                        byte flags = reader.ReadByte();
                        if ((flags & 2) != 0) throw new IOException("动画 WebP 暂不转换，避免丢失动画帧。");
                        reader.ReadBytes(3); width = U24(reader) + 1; height = U24(reader) + 1;
                    } else if (kind == 0x4c385056 && length >= 5) {
                        if (reader.ReadByte() != 0x2f) throw new IOException("WebP 无损数据头无效。");
                        uint bits = reader.ReadUInt32(); width = (int)(bits & 0x3fff) + 1; height = (int)((bits >> 14) & 0x3fff) + 1;
                    } else if (kind == 0x20385056 && length >= 10) {
                        reader.ReadBytes(3);
                        if (U24(reader) != 0x2a019d) throw new IOException("WebP 图片数据头无效。");
                        width = reader.ReadUInt16() & 0x3fff; height = reader.ReadUInt16() & 0x3fff;
                    }
                    if (width > 0 && height > 0) {
                        if ((long)width * height > 32000000 || Math.Max(width, height) > 16384) throw new IOException("WebP 超过单张图片的处理上限。");
                        if (kind == 0x58385056) { canvasWidth = width; canvasHeight = height; }
                        else {
                            if (canvasWidth != 0 && (canvasWidth != width || canvasHeight != height)) throw new IOException("WebP 画布与图片尺寸不一致。");
                            return;
                        }
                    }
                    stream.Position = next;
                }
                throw new IOException("未找到 WebP 图片尺寸。");
            }
        }
    }
}
