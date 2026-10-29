using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Snapline
{
    internal enum PdfAction { Merge, Split, Extract, DeletePages, Rotate, PageNumbers, Watermark, Protect, Unprotect, Images }
    internal sealed class PdfOptions
    {
        internal PdfAction Action;
        internal string Pages = "", Name = "合并文档.pdf", Text = "样稿", InputPassword = "", Password = "";
        internal int Rotation = 90;
    }
    internal static class PdfBatch
    {
        internal static bool IsPdf(string path) { return Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase); }
        internal static int[] PageRange(string text, int count)
        {
            if (count < 1 || count > 512) throw new IOException("每次最多处理 512 页 PDF；空文档不能处理。");
            if (string.IsNullOrWhiteSpace(text)) return Enumerable.Range(0, count).ToArray();
            var result = new List<int>();
            foreach (string token in text.Replace('，', ',').Split(',')) {
                string[] pair = token.Trim().Split('-'); int start, end;
                if (pair.Length < 1 || pair.Length > 2 || !int.TryParse(pair[0], out start) || start < 1 || start > count)
                    throw new IOException("页码超出范围或格式不正确；例如 1-3,5，页码从 1 开始。");
                end = start;
                if (pair.Length == 2 && (!int.TryParse(pair[1], out end) || end < start || end > count)) throw new IOException("页码范围无效，请使用较小页码-较大页码。");
                if (result.Count + end - start + 1 > 512) throw new IOException("选择的页面超过 512 页。");
                result.AddRange(Enumerable.Range(start - 1, end - start + 1));
            }
            return result.ToArray();
        }
        internal static string PdfName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "合并文档.pdf";
            FileBatch.ValidateName(name);
            return name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? name : name + ".pdf";
        }
        private static void Budget(string[] paths, int maximum)
        {
            if (paths.Length == 0 || paths.Length > maximum) throw new IOException("请选择 1–" + maximum + " 个文件。");
            long total = 0;
            foreach (string path in paths) {
                if (!File.Exists(path)) throw new IOException("原文件不存在：" + Path.GetFileName(path));
                total = checked(total + new FileInfo(path).Length);
                if (total > 128L * 1024 * 1024) throw new IOException("同一合并任务的输入总量超过 128 MB，请分批处理。");
            }
        }
        private static string Save(PdfDocument doc, string directory, string name, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested(); string path = null;
            try {
                using (var stream = FileBatch.CreateUnique(directory, PdfName(name), out path)) doc.Save(stream, false);
                cancel.ThrowIfCancellationRequested(); return path;
            } catch { if (path != null && File.Exists(path)) File.Delete(path); throw; }
        }
        private static void Record(BatchResult result, string output)
        { result.Outputs.Add(output); result.OutputBytes += new FileInfo(output).Length; }
        internal static bool IsExpected(Exception ex)
        { return ImageStore.IsImageError(ex) || ex is PdfReaderException || ex is InvalidOperationException || ex is OutOfMemoryException || ex is OverflowException; }
        private static string Message(Exception ex)
        {
            if (ex is OutOfMemoryException) return "本次任务内存不足，请减少图片、页数或长边后重试。";
            if (ex is PdfReaderException) return "PDF 无法读取；请检查文件是否损坏、是否填写了正确密码。";
            return ex.Message;
        }
        private static void AddImage(PdfDocument doc, string path, int maxEdge, CancellationToken cancel)
        {
            using (var input = new ImageInput(path, maxEdge, cancel))
            using (var bytes = new MemoryStream(ImageStore.Png(input.Image)))
            using (var image = XImage.FromStream(bytes)) {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(input.Image.PixelWidth * 72.0 / 96);
                page.Height = XUnit.FromPoint(input.Image.PixelHeight * 72.0 / 96);
                using (var gfx = XGraphics.FromPdfPage(page)) gfx.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
            }
        }
        internal static BatchResult FromImages(IEnumerable<string> paths, string directory, bool merge, string name, int maxEdge,
            CancellationToken cancel, Action<int, int> progress)
        {
            if (maxEdge < 0 || maxEdge > 16384) throw new ArgumentException("长边须为 0–16384。");
            var result = new BatchResult(); string[] sources = FileBatch.Paths(paths);
            Directory.CreateDirectory(directory);
            if (merge) {
                try {
                    Budget(sources, 200); name = PdfName(name ?? "合并图片.pdf");
                    using (var doc = new PdfDocument()) {
                        doc.Info.Title = Path.GetFileNameWithoutExtension(name);
                        for (int i = 0; i < sources.Length; i++) {
                            cancel.ThrowIfCancellationRequested(); AddImage(doc, sources[i], maxEdge, cancel);
                            if (progress != null) progress(i + 1, sources.Length);
                        }
                        Record(result, Save(doc, directory, name, cancel));
                        result.InputBytes = sources.Sum(path => new FileInfo(path).Length);
                    }
                } catch (OperationCanceledException) { result.Cancelled = true; }
                catch (Exception ex) { if (!IsExpected(ex)) throw; result.Errors.Add("合并未完成：" + Message(ex)); }
                return result;
            }
            for (int i = 0; i < sources.Length; i++) {
                try {
                    cancel.ThrowIfCancellationRequested();
                    using (var doc = new PdfDocument()) {
                        AddImage(doc, sources[i], maxEdge, cancel);
                        Record(result, Save(doc, directory, Path.GetFileNameWithoutExtension(sources[i]) + ".pdf", cancel));
                        result.InputBytes += new FileInfo(sources[i]).Length;
                    }
                } catch (OperationCanceledException) { result.Cancelled = true; break; }
                catch (Exception ex) { if (!IsExpected(ex)) throw; result.Errors.Add(Path.GetFileName(sources[i]) + "：" + Message(ex)); }
                if (progress != null) progress(i + 1, sources.Length);
            }
            return result;
        }
        private static void Password(PdfDocument doc, string password)
        {
            doc.SecuritySettings.UserPassword = password; doc.SecuritySettings.OwnerPassword = password;
            doc.SecurityHandler.SetEncryptionToV5(true);
        }
        private static void DrawLabel(XGraphics gfx, string text, double size, XBrush brush, XRect box)
        {
            // WPF resolves installed CJK font collections; vector outlines avoid rasterized or missing glyphs.
            var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"), size, Brushes.Black, 1);
            var geometry = formatted.BuildGeometry(new Point()).GetFlattenedPathGeometry(.01, ToleranceType.Absolute);
            Rect bounds = geometry.Bounds;
            if (bounds.IsEmpty) throw new IOException("文字没有可显示的字形。");
            double scale = Math.Min(1, Math.Min(box.Width * .85 / Math.Max(1, bounds.Width), box.Height * .85 / Math.Max(1, bounds.Height)));
            var state = gfx.Save();
            try {
                gfx.TranslateTransform(box.X + (box.Width - bounds.Width * scale) / 2 - bounds.X * scale,
                    box.Y + (box.Height - bounds.Height * scale) / 2 - bounds.Y * scale);
                gfx.ScaleTransform(scale);
                var path = new XGraphicsPath { FillMode = geometry.FillRule == FillRule.Nonzero ? XFillMode.Winding : XFillMode.Alternate };
                foreach (var figure in geometry.Figures) {
                    var points = new List<XPoint> { new XPoint(figure.StartPoint.X, figure.StartPoint.Y) };
                    foreach (var segment in figure.Segments) {
                        var line = segment as LineSegment; var lines = segment as PolyLineSegment;
                        if (line != null) points.Add(new XPoint(line.Point.X, line.Point.Y));
                        if (lines != null) foreach (Point point in lines.Points) points.Add(new XPoint(point.X, point.Y));
                    }
                    if (points.Count >= 3) path.AddPolygon(points.ToArray());
                }
                gfx.DrawPath(brush, path);
            } finally { gfx.Restore(state); }
        }
        private static PdfDocument Open(string path, string password, PdfDocumentOpenMode mode, out bool encrypted)
        {
            if (!IsPdf(path)) throw new IOException("此项目不是 PDF 文件。");
            if (new FileInfo(path).Length > 128L * 1024 * 1024) throw new IOException("单个 PDF 超过 128 MB，请分批或缩小文件。");
            bool requested = false;
            var doc = PdfReader.Open(path, mode, delegate(PdfPasswordProviderArgs args) {
                if (requested || string.IsNullOrEmpty(password)) { args.Abort = true; return; }
                requested = true; args.Password = password;
            });
            if (doc == null) throw new IOException("无法打开加密 PDF，请填写正确的原密码。");
            encrypted = requested || doc.SecuritySettings.IsEncrypted;
            if (encrypted && string.IsNullOrEmpty(password)) { doc.Dispose(); throw new IOException("此 PDF 有权限保护，请提供原密码，避免生成不受保护的副本。"); }
            if (doc.PageCount < 1 || doc.PageCount > 512) { doc.Dispose(); throw new IOException("每次最多处理 512 页，空 PDF 不能处理。"); }
            if (doc.Internals.Catalog.Elements.ContainsKey("/AcroForm") && doc.AcroForm.Fields.Count > 0) { doc.Dispose(); throw new IOException("包含交互表单或数字签名的 PDF 暂不处理，请先另存为普通 PDF；原文件保留。"); }
            return doc;
        }
        internal static BatchResult Process(IEnumerable<string> paths, string directory, PdfOptions options, CancellationToken cancel, Action<int, int> progress)
        {
            Directory.CreateDirectory(directory); string[] sources = FileBatch.Paths(paths); var result = new BatchResult();
            if (options.Action == PdfAction.Images) return FromImages(sources, directory, true, options.Name, 0, cancel, progress);
            if (sources.Length == 0) throw new IOException("请至少选择一个 PDF。");
            if (options.Action == PdfAction.Protect && string.IsNullOrEmpty(options.Password)) throw new IOException("请设置非空的打开密码。");
            if (options.Action == PdfAction.Rotate && !new[] { 90, 180, 270 }.Contains(options.Rotation)) throw new IOException("旋转角度须为 90、180 或 270 度。");
            if (options.Action == PdfAction.Watermark && (string.IsNullOrWhiteSpace(options.Text) || options.Text.Length > 64)) throw new IOException("水印须为 1–64 个字符。");
            if (options.Action == PdfAction.Merge) {
                try {
                    Budget(sources, 200); bool encrypted = false;
                    using (var output = new PdfDocument()) {
                        for (int i = 0; i < sources.Length; i++) {
                            cancel.ThrowIfCancellationRequested();
                            bool protectedInput;
                            using (var input = Open(sources[i], options.InputPassword, PdfDocumentOpenMode.Import, out protectedInput)) {
                                encrypted |= protectedInput;
                                foreach (int page in PageRange(options.Pages, input.PageCount)) {
                                    cancel.ThrowIfCancellationRequested();
                                    if (output.PageCount >= 512) throw new IOException("合并结果超过 512 页。");
                                    output.AddPage(input.Pages[page]);
                                }
                            }
                            if (progress != null) progress(i + 1, sources.Length);
                        }
                        if (encrypted) Password(output, options.InputPassword);
                        Record(result, Save(output, directory, options.Name, cancel));
                        result.InputBytes = sources.Sum(path => new FileInfo(path).Length);
                    }
                } catch (OperationCanceledException) { result.Cancelled = true; }
                catch (Exception ex) { if (!IsExpected(ex)) throw; result.Errors.Add("合并未完成：" + Message(ex)); }
                return result;
            }
            for (int i = 0; i < sources.Length; i++) {
                try {
                    cancel.ThrowIfCancellationRequested();
                    bool copy = options.Action == PdfAction.Split || options.Action == PdfAction.Extract || options.Action == PdfAction.DeletePages;
                    bool encrypted;
                    using (var doc = Open(sources[i], options.InputPassword, copy ? PdfDocumentOpenMode.Import : PdfDocumentOpenMode.Modify, out encrypted)) {
                        int[] pages = options.Action == PdfAction.Protect || options.Action == PdfAction.Unprotect ?
                            Enumerable.Range(0, doc.PageCount).ToArray() : PageRange(options.Pages, doc.PageCount);
                        string stem = Path.GetFileNameWithoutExtension(sources[i]);
                        if (options.Action == PdfAction.DeletePages) pages = Enumerable.Range(0, doc.PageCount).Except(pages).ToArray();
                        if (pages.Length == 0) throw new IOException("删除后没有剩余页面，已停止并保留原文件。");
                        if (copy) {
                            if (options.Action == PdfAction.Split) foreach (int page in pages) {
                                cancel.ThrowIfCancellationRequested();
                                using (var output = new PdfDocument()) {
                                    output.AddPage(doc.Pages[page]);
                                    if (encrypted) Password(output, options.InputPassword);
                                    Record(result, Save(output, directory, stem + "-p" + (page + 1).ToString("D3") + ".pdf", cancel));
                                }
                            } else using (var output = new PdfDocument()) {
                                foreach (int page in pages) { cancel.ThrowIfCancellationRequested(); output.AddPage(doc.Pages[page]); }
                                if (encrypted) Password(output, options.InputPassword);
                                Record(result, Save(output, directory, stem + (options.Action == PdfAction.Extract ? "-pages.pdf" : "-removed.pdf"), cancel));
                            }
                        } else {
                            foreach (int index in pages.Distinct()) {
                                cancel.ThrowIfCancellationRequested(); var page = doc.Pages[index];
                                if (options.Action == PdfAction.Rotate) page.Rotate = (page.Rotate + options.Rotation) % 360;
                                else if (options.Action == PdfAction.PageNumbers || options.Action == PdfAction.Watermark) {
                                    using (var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append)) {
                                        double w = page.Width.Point, h = page.Height.Point;
                                        if (options.Action == PdfAction.PageNumbers) DrawLabel(gfx, (index + 1) + " / " + doc.PageCount, 10, XBrushes.Gray, new XRect(0, Math.Max(0, h - 26), w, 20));
                                        else {
                                            gfx.TranslateTransform(w / 2, h / 2); gfx.RotateTransform(-35);
                                            DrawLabel(gfx, options.Text, 28, new XSolidBrush(XColor.FromArgb(65, 100, 100, 100)), new XRect(-w / 2, -40, w, 80));
                                        }
                                    }
                                }
                            }
                            if (encrypted && options.Action != PdfAction.Unprotect) Password(doc, options.InputPassword);
                            if (options.Action == PdfAction.Protect) Password(doc, options.Password);
                            if (options.Action == PdfAction.Unprotect) doc.SecurityHandler.SetEncryptionToNoneAndResetPasswords();
                            Record(result, Save(doc, directory, stem + "-" + options.Action.ToString().ToLowerInvariant() + ".pdf", cancel));
                        }
                        result.InputBytes += new FileInfo(sources[i]).Length;
                    }
                } catch (OperationCanceledException) { result.Cancelled = true; break; }
                catch (Exception ex) { if (!IsExpected(ex)) throw; result.Errors.Add(Path.GetFileName(sources[i]) + "：" + Message(ex)); }
                if (progress != null) progress(i + 1, sources.Length);
            }
            return result;
        }
    }
}
