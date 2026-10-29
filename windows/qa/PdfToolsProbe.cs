using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PdfSharp.Pdf.IO;

namespace Snapline
{
    internal static class PdfToolsPreview
    {
        [STAThread] private static int Main() { return Run(false); }
        internal static int Run(bool images)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-data", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string first = Path.Combine(root, "01 封面.png"), second = Path.Combine(root, "02 说明.png"), output = Path.Combine(root, "结果");
            File.WriteAllBytes(first, ImageStore.Png(Page(720, 480, "文件，顺手整理。", "图片转 PDF · WebP · 页面整理")));
            File.WriteAllBytes(second, ImageStore.Png(Page(480, 720, "每一页，都有顺序。", "添加图片 → 调整顺序 → 本机另存")));
            var pdf = PdfBatch.FromImages(new[] { first, second }, root, true, "03 示例图册.pdf", 0, CancellationToken.None, null);
            if (pdf.Outputs.Count != 1) { MessageBox.Show(string.Join("\n", pdf.Errors), "预览文件生成失败"); app.Shutdown(); return 1; }
            Action<string> open = delegate(string folder) { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true }); };
            Window window;
            if (images) {
                var convert = new FileToolsWindow(FileTool.Convert, new[] { first, second }, output, delegate { }, delegate { }, open);
                convert.FormatInput.SelectedItem = "PDF（合并多页）"; window = convert;
            } else window = new PdfToolsWindow(new[] { first, second, pdf.Outputs[0] }, output, delegate { }, open);
            window.Title += " · 独立预览"; app.Run(window); return 0;
        }
        private static BitmapSource Page(int width, int height, string title, string subtitle)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) {
                dc.DrawRoundedRectangle(Brushes.WhiteSmoke, null, new Rect(12, 12, width - 24, height - 24), 16, 16);
                dc.DrawRectangle(Brushes.OrangeRed, null, new Rect(42, 44, 48, 5));
                var font = new Typeface("Microsoft YaHei UI");
                dc.DrawText(new FormattedText("SNAPLINE", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 18, Brushes.DimGray, 1), new Point(42, 76));
                dc.DrawText(new FormattedText(title, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 32, Brushes.DarkSlateGray, 1), new Point(42, 146));
                dc.DrawText(new FormattedText(subtitle, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 15, Brushes.DimGray, 1), new Point(42, 210));
                dc.DrawText(new FormattedText("生成的预览样本 · 原文件保留", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 12, Brushes.Gray, 1), new Point(42, height - 66));
            }
            var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze(); return image;
        }
    }
    internal static class ImageConvertPreview
    {
        [STAThread] private static int Main() { return PdfToolsPreview.Run(true); }
    }

    internal static class PdfToolsProbe
    {
        private static readonly List<string> checks = new List<string>();
        private static Exception dispatcherFailure;
        private static void Check(bool ok, string message) { checks.Add((ok ? "PASS " : "FAIL ") + message); Console.WriteLine(checks.Last()); if (!ok) throw new InvalidOperationException(message); }
        private static void Pump(int ms) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); if (dispatcherFailure != null) throw new InvalidOperationException("Dispatcher: " + dispatcherFailure.GetType().FullName + ": " + dispatcherFailure.Message + "\n" + dispatcherFailure.StackTrace); }
        private static BitmapSource Fixture(int width, int height, bool alpha)
        {
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = (byte)(i / 4 % 230); pixels[i + 1] = 110; pixels[i + 2] = 225; pixels[i + 3] = alpha && i < pixels.Length / 2 ? (byte)0 : (byte)255; }
            var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); image.Freeze(); return image;
        }
        private static string Hash(string path) { using (var sha = SHA256.Create()) using (var input = File.OpenRead(path)) return Convert.ToBase64String(sha.ComputeHash(input)); }
        private static string One(BatchResult result) { Check(result.Outputs.Count == 1 && result.Errors.Count == 0, "One complete output is reported without errors: " + string.Join("; ", result.Errors)); return result.Outputs[0]; }
        private static int Count(string path, string password = "") { using (var doc = PdfReader.Open(path, password, PdfDocumentOpenMode.Import)) return doc.PageCount; }
        private static void Wait(Func<bool> running) { DateTime until = DateTime.UtcNow.AddSeconds(25); do { Pump(30); } while (running() && DateTime.UtcNow < until); Check(!running(), "Real async processing completes without blocking the UI dispatcher"); }
        private static void Click(Button button) { button.Dispatcher.BeginInvoke(new Action(delegate { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); })); Pump(1); }
        private static void Render(Window window, string path) { window.UpdateLayout(); var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32); image.Render(window); File.WriteAllBytes(path, ImageStore.Png(image)); }
        [STAThread] private static int Main(string[] args)
        {
            string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output); string root = Path.Combine(output, "run-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string results = Path.Combine(root, "处理结果"); Directory.CreateDirectory(results);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) { dispatcherFailure = e.Exception; e.Handled = true; };
            try {
                string first = Path.Combine(root, "横向 透明.png"), second = Path.Combine(root, "竖向.png"), corrupt = Path.Combine(root, "bad.png");
                File.WriteAllBytes(first, ImageStore.Png(Fixture(240, 160, true))); File.WriteAllBytes(second, ImageStore.Png(Fixture(160, 240, false))); File.WriteAllText(corrupt, "not an image");
                string firstHash = Hash(first), secondHash = Hash(second); var none = CancellationToken.None;
                var separate = FileBatch.Images(new[] { first, second }, results, "PDF（每张单独）", 82, 0, none, null);
                Check(separate.Outputs.Count == 2 && separate.Errors.Count == 0 && separate.Outputs.All(path => Count(path) == 1), "Each image becomes a real one-page PDF: " + string.Join("; ", separate.Errors));
                string combined = One(FileBatch.Images(new[] { first, second }, results, "PDF（合并多页）", 82, 0, none, null, "合并图片.pdf"));
                string pdfHash = Hash(combined);
                using (var doc = PdfReader.Open(combined, PdfDocumentOpenMode.Import)) Check(doc.PageCount == 2 && Math.Abs(doc.Pages[0].Width.Point - 180) < .01 && Math.Abs(doc.Pages[1].Height.Point - 180) < .01, "Merged image PDF preserves order, aspect ratio and separate page sizes");
                var failedMerge = FileBatch.Images(new[] { first, corrupt }, results, "PDF（合并多页）", 82, 0, none, null, "incomplete.pdf");
                Check(failedMerge.Outputs.Count == 0 && failedMerge.Errors.Count == 1 && !File.Exists(Path.Combine(results, "incomplete.pdf")), "An invalid image never leaves a partial merged document");
                using (var cancel = new CancellationTokenSource()) {
                    var stopped = FileBatch.Images(new[] { first, second }, results, "PDF（合并多页）", 82, 0, cancel.Token, delegate { cancel.Cancel(); }, "cancelled.pdf");
                    Check(stopped.Cancelled && stopped.Outputs.Count == 0 && !File.Exists(Path.Combine(results, "cancelled.pdf")), "Cancelling merged PDF generation removes its incomplete output");
                }
                Check(WebpCodec.Available, "The official local WebP tools are present on the test OS");
                string webp = One(FileBatch.Images(new[] { first }, results, "WebP（无损）", 82, 0, none, null));
                byte[] header = File.ReadAllBytes(webp);
                Check(System.Text.Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(header, 8, 4) == "WEBP", "WebP output contains actual WebP bytes");
                string png = One(FileBatch.Images(new[] { webp }, results, "PNG", 82, 0, none, null));
                Check(ImageStore.Digest(ImageStore.Load(png, 0)) == ImageStore.Digest(ImageStore.Load(first, 0)), "Lossless WebP round-trip preserves exact RGBA pixels including transparency");
                string lossy = One(FileBatch.Images(new[] { second }, results, "WebP", 60, 100, none, null));
                using (var decoded = new ImageInput(lossy, 0, none)) Check(Math.Abs(decoded.Image.PixelWidth - 160.0 / 240 * 100) < 1 && decoded.Image.PixelHeight == 100,
                    "Lossy WebP respects proportional size within one rounding pixel: " + decoded.Image.PixelWidth + "x" + decoded.Image.PixelHeight);
                Check(Count(One(FileBatch.Images(new[] { webp }, results, "PDF（每张单独）", 82, 0, none, null))) == 1, "WebP can also be used as PDF input");
                string badWebp = Path.Combine(root, "bad.webp"); File.WriteAllText(badWebp, "bad");
                var webpErrors = FileBatch.Images(new[] { badWebp, second }, results, "PNG", 82, 0, none, null);
                Check(webpErrors.Errors.Count == 1 && webpErrors.Outputs.Count == 1, "Invalid WebP is rejected without blocking the next valid image");
                string animated = Path.Combine(root, "animated.webp");
                using (var writer = new BinaryWriter(File.Create(animated))) {
                    writer.Write(0x46464952u); writer.Write(22u); writer.Write(0x50424557u); writer.Write(0x58385056u); writer.Write(10u);
                    writer.Write((byte)2); writer.Write(new byte[9]);
                }
                var animation = FileBatch.Images(new[] { animated }, results, "PNG", 82, 0, none, null);
                Check(animation.Outputs.Count == 0 && animation.Errors.Single().Contains("动画"), "An animated WebP header is rejected before a frame can be dropped");
                string same = One(FileBatch.Images(new[] { first }, root, "PNG", 82, 0, none, null));
                Check(same != first && Hash(first) == firstHash, "Same-folder exports never overwrite the source");
                string merged = One(PdfBatch.Process(new[] { combined, separate.Outputs[0] }, results, new PdfOptions { Action = PdfAction.Merge, Name = "合并 PDF.pdf" }, none, null));
                Check(Count(merged) == 3, "Existing PDF documents merge into a real three-page document");
                string extracted = One(PdfBatch.Process(new[] { combined }, results, new PdfOptions { Action = PdfAction.Extract, Pages = "2,1,2" }, none, null));
                using (var doc = PdfReader.Open(extracted, PdfDocumentOpenMode.Import)) Check(doc.PageCount == 3 && doc.Pages[0].Width.Point == 120 && doc.Pages[1].Width.Point == 180 && doc.Pages[2].Width.Point == 120, "Extraction preserves the requested order and allows intentional duplicate pages");
                string removed = One(PdfBatch.Process(new[] { combined }, results, new PdfOptions { Action = PdfAction.DeletePages, Pages = "1" }, none, null));
                Check(Count(removed) == 1, "Removing selected PDF pages retains the remaining page");
                var deleteAll = PdfBatch.Process(new[] { combined }, results, new PdfOptions { Action = PdfAction.DeletePages, Pages = "" }, none, null);
                Check(deleteAll.Outputs.Count == 0 && deleteAll.Errors.Count == 1, "Deleting all pages is rejected while preserving the original");
                var split = PdfBatch.Process(new[] { merged }, results, new PdfOptions { Action = PdfAction.Split }, none, null);
                Check(split.Outputs.Count == 3 && split.Outputs.All(path => Count(path) == 1), "Split exports one valid PDF for each page");
                string rotated = One(PdfBatch.Process(new[] { combined }, results, new PdfOptions { Action = PdfAction.Rotate, Pages = "1", Rotation = 90 }, none, null));
                using (var doc = PdfReader.Open(rotated, PdfDocumentOpenMode.Import)) Check(doc.Pages[0].Rotate == 90 && doc.Pages[1].Rotate == 0, "Rotation changes only the selected page");
                var rangeError = PdfBatch.Process(new[] { combined }, results, new PdfOptions { Action = PdfAction.Extract, Pages = "9" }, none, null);
                Check(rangeError.Outputs.Count == 0 && rangeError.Errors.Count == 1, "Out-of-range pages create no output");
                Check(PdfBatch.PageRange("1-2，1", 2).SequenceEqual(new[] { 0, 1, 0 }), "Page ranges accept Chinese commas without changing order");
                string numbered = One(PdfBatch.Process(new[] { combined }, results, new PdfOptions { Action = PdfAction.PageNumbers }, none, null));
                string watermarked = One(PdfBatch.Process(new[] { combined }, results, new PdfOptions { Action = PdfAction.Watermark, Text = "内部样稿" }, none, null));
                Check(Count(numbered) == 2 && Count(watermarked) == 2 && new FileInfo(watermarked).Length > new FileInfo(combined).Length, "Page numbers and Chinese vector watermarks retain all source pages");
                const string password = "Generated-测试-2026!";
                string protectedPdf = One(PdfBatch.Process(new[] { combined }, results, new PdfOptions { Action = PdfAction.Protect, Password = password }, none, null));
                bool denied = false; try { Count(protectedPdf); } catch (PdfReaderException) { denied = true; }
                Check(denied && Count(protectedPdf, password) == 2, "AES password protection requires the correct password and preserves pages");
                var badPassword = PdfBatch.Process(new[] { protectedPdf }, results, new PdfOptions { Action = PdfAction.Unprotect, InputPassword = "incorrect" }, none, null);
                Check(badPassword.Outputs.Count == 0 && badPassword.Errors.Count == 1, "An incorrect password cannot produce an unlocked PDF");
                string unlocked = One(PdfBatch.Process(new[] { protectedPdf }, results, new PdfOptions { Action = PdfAction.Unprotect, InputPassword = password }, none, null));
                Check(Count(unlocked) == 2, "Known-password decryption creates an independently readable PDF");
                string protectedCopy = One(PdfBatch.Process(new[] { protectedPdf }, results, new PdfOptions { Action = PdfAction.Extract, Pages = "1", InputPassword = password }, none, null));
                denied = false; try { Count(protectedCopy); } catch (PdfReaderException) { denied = true; }
                Check(denied && Count(protectedCopy, password) == 1, "Extracting from an encrypted PDF retains its password protection");
                string protectedMerge = One(PdfBatch.Process(new[] { protectedPdf, combined }, results, new PdfOptions { Action = PdfAction.Merge, Name = "protected-merge.pdf", InputPassword = password }, none, null));
                denied = false; try { Count(protectedMerge); } catch (PdfReaderException) { denied = true; }
                Check(denied && Count(protectedMerge, password) == 4, "A merge containing encrypted input retains the original open password");
                string ignorePages = One(PdfBatch.Process(new[] { combined }, results, new PdfOptions { Action = PdfAction.Protect, Pages = "999", Password = password }, none, null));
                Check(Count(ignorePages, password) == 2, "Whole-document password operations ignore a hidden stale page selection");
                using (var cancel = new CancellationTokenSource()) {
                    cancel.Cancel(); var cancelled = PdfBatch.Process(new[] { combined }, results, new PdfOptions { Action = PdfAction.Merge, Name = "not-created.pdf" }, cancel.Token, null);
                    Check(cancelled.Cancelled && cancelled.Outputs.Count == 0, "A cancelled PDF operation does not create files");
                }
                var collision = PdfBatch.Process(new[] { combined }, results, new PdfOptions { Action = PdfAction.Merge, Name = Path.GetFileName(combined) }, none, null);
                Check(collision.Outputs.Count == 1 && collision.Outputs[0] != combined && Count(combined) == 2, "A colliding PDF name is numbered instead of overwriting input");
                string[] captured = null; string opened = null;
                var convert = new FileToolsWindow(FileTool.Convert, new[] { first, second }, results, delegate { }, paths => captured = paths, path => opened = path) { ShowActivated = false, IsHitTestVisible = false };
                convert.Show(); Pump(60); convert.FormatInput.SelectedItem = "PDF（合并多页）"; convert.PdfNameInput.Text = "界面合并.pdf";
                convert.FilesGrid.SelectedItem = convert.Rows[1]; convert.MoveRow(-1);
                Check(convert.Rows[0].Path == second && convert.Rows[0].After.Contains("第 1 页"), "The actual image converter previews reordered PDF pages");
                convert.PdfNameInput.Text = "bad/name.pdf"; Check(!convert.ExecuteButton.IsEnabled, "The image converter disables execution for invalid PDF names");
                convert.PdfNameInput.Text = "界面合并.pdf"; convert.FormatInput.SelectedItem = "WebP"; Check(convert.QualityInput.IsEnabled, "Lossy WebP exposes its quality control");
                convert.FormatInput.SelectedItem = "WebP（无损）"; Check(!convert.QualityInput.IsEnabled, "Lossless WebP disables inapplicable quality control");
                convert.FormatInput.SelectedItem = "PDF（合并多页）"; Render(convert, Path.Combine(output, "image-to-pdf-window.png")); Click(convert.ExecuteButton); Wait(delegate { return convert.Running; });
                Check(captured != null && captured.Length == 1 && Count(captured[0]) == 2, "The real image-to-PDF dialog performs background export and reports its output"); convert.Close();
                captured = null;
                var window = new PdfToolsWindow(new[] { first, second, combined }, results, paths => captured = paths, path => opened = path) { ShowActivated = false, IsHitTestVisible = false };
                window.Show(); Pump(70);
                Check(window.Action == PdfAction.Merge && window.Rows.Count(row => row.Selected) == 1, "The PDF toolbox starts with only relevant PDF files selected");
                Check(window.ToolList.Items.Count == 10 && !window.FilesGrid.CanUserSortColumns, "The toolbox exposes ten real local tools with deliberate list order");
                window.Width = window.MinWidth; Pump(40);
                var toolbar = (WrapPanel)typeof(PdfToolsWindow).GetField("toolbar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(window);
                Check(toolbar.Children.OfType<Button>().All(button => button.TranslatePoint(new Point(button.ActualWidth, 0), window).X <= window.ActualWidth), "File-list actions remain inside the narrow toolbox window");
                window.Width = Math.Min(1040, SystemParameters.WorkArea.Width - 24); Pump(30);
                window.ToolList.SelectedIndex = 0; Check(window.Rows.Count(row => row.Selected) == 2 && window.Rows.Single(row => row.Path == combined).Selected == false, "Image mode selects only images in a mixed input list");
                window.ToolList.SelectedIndex = 8; window.NewPassword.Password = "test"; window.ConfirmPassword.Password = "other";
                Check(!window.ExecuteButton.IsEnabled, "Password confirmation prevents mismatched protection settings"); window.ConfirmPassword.Password = "test"; Check(window.ExecuteButton.IsEnabled, "Matching passwords enable protection");
                window.ToolList.SelectedIndex = 7; window.WatermarkInput.Text = "中文水印"; Render(window, Path.Combine(output, "pdf-toolbox-window.png"));
                window.ToolList.SelectedIndex = 5; window.PagesInput.Text = "1"; Click(window.ExecuteButton); Wait(delegate { return window.Running; });
                Check(captured != null && captured.Length == 1 && Count(captured[0]) == 2 && window.Status.Text.Contains("处理完成"), "The real PDF toolbox exports asynchronously and retains its completion report");
                int countBefore = Directory.GetFiles(results).Length; Click(window.ExecuteButton);
                Check(opened == results && Directory.GetFiles(results).Length == countBefore, "The completed action opens the output location without executing twice");
                window.ToolList.SelectedIndex = 7;
                Check(window.ToolList.IsEnabled && window.ExecuteButton.IsEnabled && (string)window.ExecuteButton.Content == "开始处理", "After completion the sidebar can start another deliberate operation");
                Click(window.ExecuteButton); Wait(delegate { return window.Running; });
                Check(captured.Length == 1 && Count(captured[0]) == 2, "A second tool executes successfully in the same window"); window.Close();
                Check(Hash(first) == firstHash && Hash(second) == secondHash && Hash(combined) == pdfHash, "All source images and PDF bytes remain unchanged");
                string artifacts = string.Join(Environment.NewLine, new[] { combined, numbered, watermarked, rotated, protectedPdf, unlocked });
                File.WriteAllText(Path.Combine(output, "artifacts.txt"), artifacts); File.WriteAllLines(Path.Combine(output, "pdf-tools-results.txt"), checks);
                Console.WriteLine("PASS " + checks.Count + " PDF/WebP checks; generated files only, no system input or clipboard."); return 0;
            } catch (Exception ex) { File.WriteAllLines(Path.Combine(output, "pdf-tools-results.txt"), checks); File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString()); Console.Error.WriteLine(ex); return 1; }
            finally { app.Shutdown(); }
        }
    }
}
