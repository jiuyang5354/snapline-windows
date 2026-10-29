using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Snapline
{
    internal static class FileToolsTests
    {
        private static readonly List<string> checks = new List<string>();
        private static void Check(bool ok, string name)
        { if (!ok) throw new Exception("FAIL: " + name); checks.Add("PASS: " + name); Console.WriteLine("PASS: " + name); }
        private static void Reject(Action action, string name)
        { bool rejected = false; try { action(); } catch (IOException) { rejected = true; } Check(rejected, name); }
        private static string Hash(string path)
        { using (var sha = SHA256.Create()) using (var file = File.OpenRead(path)) return Convert.ToBase64String(sha.ComputeHash(file)); }
        private static void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        private static BitmapSource Image(int width, int height)
        {
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 12; pixels[i + 1] = 110; pixels[i + 2] = 220; pixels[i + 3] = i < pixels.Length / 2 ? (byte)0 : (byte)255; }
            var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); image.Freeze(); return image;
        }
        private static void Render(Window window, string path)
        {
            window.UpdateLayout(); var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            image.Render(window); File.WriteAllBytes(path, ImageStore.Png(image));
        }
        private static void Wait(FileToolsWindow window)
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            do { Pump(50); } while (window.Running && DateTime.UtcNow < deadline);
            Check(!window.Running, "Asynchronous operation completes without blocking the UI dispatcher");
        }
        private static Border Tile(DependencyObject view)
        {
            var border = view as Border;
            if (border != null && border.Tag is Dropline.ShelfItem) return border;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(view); i++) { var found = Tile(VisualTreeHelper.GetChild(view, i)); if (found != null) return found; }
            return null;
        }
        private static CheckBox FirstCheckbox(DependencyObject view)
        {
            var checkbox = view as CheckBox; if (checkbox != null) return checkbox;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(view); i++) { var found = FirstCheckbox(VisualTreeHelper.GetChild(view, i)); if (found != null) return found; }
            return null;
        }
        internal static int Run(string output)
        {
            Directory.CreateDirectory(output);
            string root = Path.Combine(output, "run-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(root);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Controller controller = null;
            try {
                string source = Path.Combine(root, "中文 原始文件"), results = Path.Combine(root, "处理结果"); Directory.CreateDirectory(source); Directory.CreateDirectory(results);
                string a = Path.Combine(source, "原稿.txt"), b = Path.Combine(source, "另一个.TXT"); File.WriteAllText(a, "alpha"); File.WriteAllText(b, "beta");
                var plan = FileBatch.PlanRename(new[] { a, b, a }, "项目_{n}_{name}", 7, 3);
                Check(plan.Count == 2 && plan[0].After == "项目_007_原稿.txt" && plan[1].After == "项目_008_另一个.TXT", "Rename preview deduplicates paths and preserves Unicode, order and original extension case");
                Reject(delegate { FileBatch.PlanRename(new[] { a, b }, "相同名称", 1, 3); }, "Duplicate preview targets are rejected before changing files");
                foreach (var invalid in new[] { "CON", "PRN", "AUX", "NUL", "COM1", "LPT9", "COM¹", "../escape", "bad:name" }) {
                    string value = invalid; Reject(delegate { FileBatch.PlanRename(new[] { a }, value, 1, 3); }, "Reserved or invalid rename target rejected: " + value);
                }
                string collision = Path.Combine(source, "项目_001.txt"); File.WriteAllText(collision, "keep");
                Reject(delegate { FileBatch.PlanRename(new[] { a }, "项目_{n}", 1, 3); }, "Existing unrelated destination is never overwritten");
                File.Delete(collision);
                string nested = Path.Combine(source, "子文件夹"); Directory.CreateDirectory(nested);
                Reject(delegate { FileBatch.PlanRename(new[] { nested }, "目录_{n}", 1, 3); }, "Folder renaming is explicitly refused");
                FileBatch.Rename(plan, map => { Check(map.Count == 2, "Rename provides one complete path map to persistence"); });
                Check(File.ReadAllText(plan[0].Target) == "alpha" && File.ReadAllText(plan[1].Target) == "beta" && !File.Exists(a), "Batch rename changes names and preserves exact contents");
                var swap = new[] { new RenameEntry { Source = plan[0].Target, Target = plan[1].Target }, new RenameEntry { Source = plan[1].Target, Target = plan[0].Target } };
                FileBatch.Rename(swap, map => { });
                Check(File.ReadAllText(plan[0].Target) == "beta" && File.ReadAllText(plan[1].Target) == "alpha", "Two-stage rename supports name swaps without overwriting either source");
                var rollback = FileBatch.PlanRename(swap.Select(e => e.Target), "恢复_{n}", 1, 2);
                Reject(delegate { FileBatch.Rename(rollback, map => { throw new IOException("Synthetic metadata save failure"); }); }, "Persistence failure propagates after file-name rollback");
                Check(rollback.All(e => File.Exists(e.Source) && !File.Exists(e.Target)) && !Directory.GetFiles(source, ".snapline-rename-*").Any(), "Failed rename restores every original path and leaves no staging files");
                var caseOnly = new[] { new RenameEntry { Source = swap[0].Target, Target = swap[0].Target.ToUpperInvariant() } };
                FileBatch.Rename(caseOnly, map => { }); Check(File.Exists(caseOnly[0].Target), "Case-only rename succeeds on Windows");

                string png = Path.Combine(source, "透明 原图.png"); File.WriteAllBytes(png, ImageStore.Png(Image(640, 360))); string originalHash = Hash(png);
                foreach (string format in new[] { "PNG", "JPEG", "BMP", "TIFF" }) {
                    var converted = FileBatch.Images(new[] { png }, results, format, 82, 0, CancellationToken.None, null);
                    Check(converted.Outputs.Count == 1 && converted.Errors.Count == 0 && Path.GetExtension(converted.Outputs[0]) == FileBatch.Extension(format), "Image conversion writes actual " + format + " bytes to a new file");
                    var decoded = ImageStore.Load(converted.Outputs[0], 0); Check(decoded.PixelWidth == 640 && decoded.PixelHeight == 360, format + " output reopens with original dimensions");
                    using (var encoded = File.OpenRead(converted.Outputs[0])) {
                        var codec = BitmapDecoder.Create(encoded, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                        Check((format == "PNG" && codec is PngBitmapDecoder) || (format == "JPEG" && codec is JpegBitmapDecoder) ||
                            (format == "BMP" && codec is BmpBitmapDecoder) || (format == "TIFF" && codec is TiffBitmapDecoder), format + " output is encoded in the selected codec, not just renamed");
                    }
                    if (format == "PNG") Check(ImageStore.Digest(decoded) == ImageStore.Digest(ImageStore.Load(png, 0)), "PNG conversion preserves transparent pixel content");
                    if (format == "JPEG") {
                        var rgb = new FormatConvertedBitmap(decoded, PixelFormats.Bgr24, null, 0); var pixel = new byte[3]; rgb.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 3, 0);
                        Check(pixel.All(c => c >= 250), "JPEG explicitly fills fully transparent regions with white");
                    }
                }
                var compressed = FileBatch.Images(new[] { png }, results, "JPEG", 65, 160, CancellationToken.None, null);
                var small = ImageStore.Load(compressed.Outputs.Single(), 0);
                Check(small.PixelWidth == 160 && small.PixelHeight == 90 && compressed.InputBytes > 0 && compressed.OutputBytes > 0, "Image compression resizes proportionally and reports actual input/output bytes");
                var noUpscale = FileBatch.Images(new[] { png }, results, "PNG", 82, 1920, CancellationToken.None, null);
                Check(ImageStore.Load(noUpscale.Outputs.Single(), 0).PixelWidth == 640, "Small images are never enlarged by a long-edge limit");
                var sameFolder = FileBatch.Images(new[] { png }, source, "PNG", 82, 0, CancellationToken.None, null);
                Check(sameFolder.Outputs.Single() != png && Hash(png) == originalHash, "Same-directory conversion creates a unique result and retains original bytes");
                string corrupt = Path.Combine(source, "损坏.png"); File.WriteAllText(corrupt, "not an image");
                var mixed = FileBatch.Images(new[] { corrupt, png }, results, "PNG", 82, 0, CancellationToken.None, null);
                Check(mixed.Errors.Count == 1 && mixed.Outputs.Count == 1, "A corrupt image reports its error while the next image still completes");
                string animated = Path.Combine(source, "动画.gif");
                var gif = new GifBitmapEncoder(); gif.Frames.Add(BitmapFrame.Create(Image(10, 10))); gif.Frames.Add(BitmapFrame.Create(Image(12, 10)));
                using (var stream = File.Create(animated)) gif.Save(stream);
                var animationResult = FileBatch.Images(new[] { animated }, results, "PNG", 82, 0, CancellationToken.None, null);
                Check(animationResult.Outputs.Count == 0 && animationResult.Errors.Count == 1, "Multi-frame GIF is skipped rather than silently losing frames");
                string huge = Path.Combine(source, "超大长图.png"); File.WriteAllBytes(huge, ImageStore.Png(Image(2, 30000)));
                var hugeResult = FileBatch.Images(new[] { huge }, results, "PNG", 82, 0, CancellationToken.None, null);
                Check(hugeResult.Outputs.Count == 0 && hugeResult.Errors.Count == 1, "Oversize output is rejected before allocating a full export bitmap");
                using (var cancellation = new CancellationTokenSource()) {
                    cancellation.Cancel(); var cancelled = FileBatch.Images(new[] { png }, results, "PNG", 82, 0, cancellation.Token, null);
                    Check(cancelled.Cancelled && cancelled.Outputs.Count == 0, "Cancelled image batch starts no extra exports");
                }
                Check(Hash(png) == originalHash, "All conversion and compression operations leave the original image unchanged");

                string pack = Path.Combine(source, "打包文件夹"); Directory.CreateDirectory(Path.Combine(pack, "空 文件夹"));
                File.WriteAllText(Path.Combine(pack, "中文.txt"), "中文 ZIP 内容");
                string other = Path.Combine(root, "同名目录"); Directory.CreateDirectory(other); string duplicate = Path.Combine(other, Path.GetFileName(png)); File.Copy(png, duplicate);
                string zip = FileBatch.Zip(new[] { pack, png, duplicate }, results, "中文 打包.zip", CancellationToken.None, null);
                using (var archive = new ZipArchive(File.OpenRead(zip), ZipArchiveMode.Read)) {
                    Check(archive.GetEntry("打包文件夹/空 文件夹/") != null && archive.GetEntry("打包文件夹/中文.txt") != null, "ZIP retains Unicode names, hierarchy and empty directories");
                    using (var reader = new StreamReader(archive.GetEntry("打包文件夹/中文.txt").Open())) Check(reader.ReadToEnd() == "中文 ZIP 内容", "ZIP file content round-trips exactly");
                    Check(archive.Entries.Count(e => e.Name.EndsWith(".png")) == 2 && archive.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == archive.Entries.Count,
                        "Same-named top-level inputs receive distinct archive names");
                }
                string zipAgain = FileBatch.Zip(new[] { pack }, results, "中文 打包.zip", CancellationToken.None, null);
                Check(zipAgain != zip && File.Exists(zip), "ZIP name collision retains the earlier archive");
                Reject(delegate { FileBatch.Zip(new[] { pack + "\\" }, pack, "循环.zip", CancellationToken.None, null); }, "ZIP output inside a selected directory is rejected even with trailing separators");
                using (var cancellation = new CancellationTokenSource()) {
                    bool cancelled = false; try { FileBatch.Zip(new[] { pack }, results, "取消.zip", cancellation.Token, name => cancellation.Cancel()); } catch (OperationCanceledException) { cancelled = true; }
                    Check(cancelled && !File.Exists(Path.Combine(results, "取消.zip")), "Cancelling ZIP removes only its own incomplete output");
                }

                File.WriteAllText(Path.Combine(root, "settings.json"), "{\"ListenClipboard\":false,\"AutoCheckUpdates\":false,\"CollectionPaused\":true,\"Paths\":[]}");
                controller = new Controller(root, false); controller.Store.AddFile(png, true); controller.Files.Store.Add(new[] { png }); controller.Files.Store.Add(new[] { png });
                var trackedPlan = FileBatch.PlanRename(new[] { png }, "已同步_{n}", 1, 2); FileBatch.Rename(trackedPlan, controller.CommitFileRename);
                string renamed = trackedPlan.Single().Target;
                Check(controller.Store.Items.Single().Path == renamed && controller.Files.Store.Items.All(item => item.Paths.Single() == renamed), "Rename updates screenshot history and every shelf reference including duplicate parked tiles");
                var restoredImages = new ImageStore(root); var restoredShelf = new Dropline.ShelfStore(controller.Files.Store.Root); restoredShelf.Load();
                Check(restoredImages.Items.Single().Path == renamed && restoredShelf.Items.All(item => item.Paths.Single() == renamed), "Renamed screenshot and shelf paths persist across store reload");
                string dataFile = Path.Combine(controller.Files.Store.Root, "shelf.txt"); File.Delete(dataFile); Directory.CreateDirectory(dataFile);
                var failedStorePlan = FileBatch.PlanRename(new[] { renamed }, "保存失败_{n}", 1, 2);
                Reject(delegate { FileBatch.Rename(failedStorePlan, controller.CommitFileRename); }, "Shelf persistence failure rejects the complete rename operation");
                Check(File.Exists(renamed) && controller.Store.Items.Single().Path == renamed && controller.Files.Store.Items.All(item => item.Paths.Single() == renamed), "Failed metadata save restores file names, screenshot history and shelf references together");
                Directory.Delete(dataFile); controller.Files.Store.Save();

                Exception menuFailure = null; bool menuOpened = false;
                var menuTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                menuTimer.Tick += delegate {
                    var dialog = app.Windows.OfType<FileToolsWindow>().FirstOrDefault(w => w.IsVisible);
                    if (dialog == null) return; menuTimer.Stop();
                    try {
                        menuOpened = true; dialog.TemplateInput.Text = "菜单改名_{n}"; dialog.Execute();
                        Check(dialog.Status.Text.Contains("已重命名"), "Shelf context menu opens the real rename dialog and executes its commit callback");
                    }
                    catch (Exception ex) { menuFailure = ex; }
                    finally { dialog.Close(); }
                };
                menuTimer.Start();
                try {
                    controller.Files.Window.Toggle(); Pump(100);
                    var tile = Tile(controller.Files.Window);
                    Check(tile != null && tile.ContextMenu.Items.OfType<MenuItem>().Count(item => ((string)item.Header).EndsWith("…")) == 5,
                        "A parked tile exposes the original batch tools and PDF toolbox through its actual context menu");
                    tile.ContextMenu.Items.OfType<MenuItem>().First(item => ((string)item.Header).StartsWith("批量重命名")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                }
                finally { menuTimer.Stop(); }
                if (menuFailure != null) throw menuFailure;
                Check(menuOpened && !controller.Busy && controller.Store.Items.Count == 1 && controller.Files.Store.Items.Count == 2,
                    "Closing a batch dialog restores controller interaction and both tracked path stores");
                renamed = controller.Store.Items.Single().Path;

                int opened = 0; var added = new List<string>();
                foreach (var tool in new[] { FileTool.Rename, FileTool.Convert, FileTool.Compress, FileTool.Zip }) {
                    var window = new FileToolsWindow(tool, new[] { renamed, pack }, results, controller.CommitFileRename, paths => added.AddRange(paths), path => opened++);
                    window.ShowActivated = false; window.Show(); Pump(250);
                    Check(window.Rows.Count == 2 && window.Rows[0].Selected && (window.Rows[1].Selected == (tool == FileTool.Zip)), tool + " UI defaults include files/images and select folders only for ZIP");
                    if (tool == FileTool.Convert) {
                        var checkbox = FirstCheckbox(window.FilesGrid); Check(checkbox != null, "Selection checkbox is rendered directly in the grid without entering cell edit mode");
                        checkbox.SetCurrentValue(CheckBox.IsCheckedProperty, false); Pump(30);
                        Check(!window.Rows[0].Selected && !window.ExecuteButton.IsEnabled, "Unchecking the rendered image updates selection and disables empty batch execution");
                        checkbox.SetCurrentValue(CheckBox.IsCheckedProperty, true); Pump(30);
                        Check(window.Rows[0].Selected && window.ExecuteButton.IsEnabled, "Checking the rendered image restores the preview and executable batch");
                        window.FormatInput.SelectedItem = "JPEG"; Check(window.QualityInput.IsEnabled, "JPEG selection enables its quality control");
                        window.FormatInput.SelectedItem = "PNG"; Check(!window.QualityInput.IsEnabled, "PNG selection disables inapplicable JPEG quality control");
                        window.EdgeInput.Text = "-1"; Check(!window.ExecuteButton.IsEnabled, "Invalid image size disables execution before export"); window.EdgeInput.Text = "0";
                    }
                    if (tool == FileTool.Rename) {
                        window.TemplateInput.Text = "../bad"; Check(!window.ExecuteButton.IsEnabled, "Rename UI blocks invalid preview before execution");
                        window.TemplateInput.Text = "预览_{n}";
                    }
                    Render(window, Path.Combine(output, tool.ToString().ToLowerInvariant() + ".png"));
                    if (tool != FileTool.Rename) {
                        window.Execute(); Wait(window);
                        Check(window.Status.Text.Contains("处理完成") && added.Count > 0, tool + " dialog executes real background export and adds results to shelf callback");
                        window.ExecuteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Check(opened > 0 && !window.Running, tool + " completed action opens the output directory without processing twice");
                    }
                    window.Close();
                }
                bool closingDialogOpened = false;
                var closingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
                closingTimer.Tick += delegate {
                    var dialog = app.Windows.OfType<FileToolsWindow>().FirstOrDefault(w => w.IsVisible);
                    if (dialog == null) return;
                    closingTimer.Stop(); closingDialogOpened = true;
                    controller.Files.Window.Close(); dialog.Close();
                };
                closingTimer.Start();
                try { controller.OpenFileTools(FileTool.Zip, new[] { controller.Store.Items.Single().Path }); }
                finally { closingTimer.Stop(); }
                Check(closingDialogOpened && !controller.Busy && !controller.Files.Window.IsVisible,
                    "Closing the shelf while a batch dialog is open does not try to show a closed window");
                Check(!Directory.GetFiles(source, ".snapline-rename-*").Any(), "No temporary renamed originals remain after all checks");
                File.WriteAllLines(Path.Combine(output, "file-tools-results.txt"), checks); Console.WriteLine("PASS " + checks.Count + " file-tool checks"); return 0;
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString()); Console.Error.WriteLine(ex); return 1; }
            finally { if (controller != null) controller.Dispose(); foreach (Window window in app.Windows.Cast<Window>().ToArray()) window.Close(); app.Shutdown(); }
        }

        internal static void Preview(string root)
        {
            Directory.CreateDirectory(root);
            string demo = Path.Combine(root, "演示文件"), output = Path.Combine(root, "处理结果"); Directory.CreateDirectory(demo);
            for (int i = 1; i <= 3; i++) {
                string path = Path.Combine(demo, "设计稿 " + i + ".png");
                if (!File.Exists(path)) File.WriteAllBytes(path, ImageStore.Png(Image(640 + i * 60, 420)));
            }
            string folder = Path.Combine(demo, "素材文件夹"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "说明.txt"), "这是独立预览生成的演示文件，可自由测试。", System.Text.Encoding.UTF8);
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose }; Ui.Initialize();
            var host = new Window { Title = "Snapline · 批量工具独立预览", Width = 620, Height = 420,
                WindowStartupLocation = WindowStartupLocation.CenterScreen, ResizeMode = ResizeMode.NoResize, Background = Ui.Brush("Paper"), FontFamily = Ui.Font };
            var panel = new StackPanel { Margin = new Thickness(28) };
            panel.Children.Add(Ui.Text("文件批量工具", 26, false));
            panel.Children.Add(new TextBlock { Text = "使用自动生成的演示文件测试；不读取安装版设置和截图。\n关闭此窗口退出预览，处理结果保存在预览文件夹。",
                Margin = new Thickness(0, 12, 0, 20), TextWrapping = TextWrapping.Wrap, Foreground = Ui.Brush("Muted"), FontSize = 13 });
            foreach (var kind in new[] { FileTool.Rename, FileTool.Convert, FileTool.Compress, FileTool.Zip }) {
                var tool = kind;
                var button = Ui.Button(FileToolsWindow.Label(tool), delegate {
                    var dialog = new FileToolsWindow(tool, Directory.GetFiles(demo).Concat(new[] { folder }).ToArray(), output,
                        map => { }, paths => { }, path => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }));
                    dialog.Owner = host; dialog.ShowDialog();
                });
                button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Margin = new Thickness(0, 0, 0, 8); panel.Children.Add(button);
            }
            host.Content = panel; app.Run(host);
        }
    }

    internal static class FileToolsPreview
    {
        [STAThread] private static void Main(string[] args)
        { FileToolsTests.Preview(args.Length == 0 ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PreviewData") : Path.GetFullPath(args[0])); }
    }
}
