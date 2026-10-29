using System;
using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;

namespace Snapline
{
    internal sealed class ImageInput : IDisposable
    {
        private FileStream stream;
        private string temporary;
        internal BitmapSource Image { get; private set; }
        internal ImageInput(string source, int maxEdge, CancellationToken cancel)
        {
            try {
                cancel.ThrowIfCancellationRequested();
                if (!FileBatch.IsInputImage(source)) throw new IOException("此项目不是支持的图片格式。");
                if (new FileInfo(source).Length > 128L * 1024 * 1024) throw new IOException("单张图片超过 128 MB，请先缩小或分批处理。");
                string decoded = source;
                if (Path.GetExtension(source).Equals(".webp", StringComparison.OrdinalIgnoreCase)) decoded = temporary = WebpCodec.Decode(source, cancel);
                stream = new FileStream(decoded, FileMode.Open, FileAccess.Read, FileShare.Read);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (decoder.Frames.Count != 1) throw new IOException("多帧或动画图片暂不转换，避免丢失页面或动画。");
                var frame = decoder.Frames[0]; int width = frame.PixelWidth, height = frame.PixelHeight, edge = Math.Max(width, height);
                int outputEdge = maxEdge > 0 ? Math.Min(edge, maxEdge) : edge;
                if ((long)width * height > 32000000 || outputEdge > 16384) throw new IOException("图片超过单张 3200 万像素或长边 16384 的处理上限，请先缩小。");
                Image = frame;
                if (outputEdge < edge) {
                    stream.Position = 0;
                    var resized = new BitmapImage(); resized.BeginInit(); resized.StreamSource = stream; resized.CacheOption = BitmapCacheOption.OnLoad;
                    if (width >= height) resized.DecodePixelWidth = outputEdge; else resized.DecodePixelHeight = outputEdge;
                    resized.EndInit(); resized.Freeze(); Image = resized;
                }
                cancel.ThrowIfCancellationRequested();
            } catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (stream != null) { stream.Dispose(); stream = null; }
            if (temporary != null) { File.Delete(temporary); temporary = null; }
        }
    }
}
