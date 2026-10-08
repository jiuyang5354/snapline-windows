using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Snapline
{
    internal static class Tests
    {
        private static readonly List<string> checks = new List<string>();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint thread, int message, IntPtr wParam, IntPtr lParam);

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--network") return CheckNetwork(Path.GetFullPath(args[1]));
            string output = Path.GetFullPath(args[0]);
            string root = Path.Combine(output, "run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(root);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            DataObject clipboardBackup = BackupClipboard();
            bool clipboardUsed = false;
            Controller controller = null;
            try
            {
                // Storage behavior: use only generated fixtures in an isolated directory.
                var store = new ImageStore(Path.Combine(root, "store"));
                var image = Fixture(700, 420, 0);
                var first = store.AddBitmap(image);
                Check(first != null && File.Exists(first.Path), "Clipboard images are saved as real PNG files");
                Check(ImageStore.Digest(ImageStore.Load(first.Path, 0)) == ImageStore.Digest(image), "PNG preserves source pixels");
                Check(store.AddBitmap(image) == null && store.Items.Count == 1, "Repeated images do not duplicate");
                string external = Path.Combine(root, "中文 图片.png");
                File.WriteAllBytes(external, ImageStore.Png(Fixture(500, 310, 1)));
                var externalShot = store.AddFile(external, true);
                Check(externalShot != null && !store.Owns(external), "Unicode external paths stay external");
                Check(store.Owns(first.Path), "Own screenshots are distinguished from originals");
                string sibling = Path.Combine(store.Root, "Inbox-other");
                Directory.CreateDirectory(sibling);
                string siblingFile = Path.Combine(sibling, "image.png");
                File.Copy(external, siblingFile);
                Check(!store.Owns(siblingFile), "Ownership check rejects similarly named sibling folders");
                store.Remove(externalShot, true);
                Check(File.Exists(external) && store.Items.Count == 1, "Taking down external images preserves original files");
                var recyclable = store.AddBitmap(Fixture(420, 300, 2));
                store.Remove(recyclable, true);
                Check(!File.Exists(recyclable.Path) && store.Items.Count == 1, "Own captures can be taken down using the Windows recycle-bin API");
                store.Settings.ListenClipboard = false;
                store.Save();
                var restored = new ImageStore(store.Root);
                Check(restored.Items.Count == 1 && !restored.Settings.ListenClipboard, "Images and preferences survive restart");
                File.Delete(first.Path);
                restored.Prune();
                Check(restored.Items.Count == 0, "Files moved away disappear from the line");
                for (int i = 0; i < 14; i++) restored.AddBitmap(Fixture(300 + i, 220, i));
                Check(restored.Items.Count == 12 && Directory.GetFiles(restored.Inbox, "*.png").Length == 14, "Capacity limit retains older PNG files");
                restored.Clear();
                Check(restored.Items.Count == 0 && Directory.GetFiles(restored.Inbox, "*.png").Length == 14, "Clear takes down cards without deleting captures");
                var transparent = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 10, 20, 30, 0, 40, 50, 60, 0 }, 8);
                var repaired = Controller.RepairBitmapAlpha(transparent);
                var pixels = new byte[8]; repaired.CopyPixels(pixels, 8, 0);
                Check(pixels[3] == 255 && pixels[7] == 255 && pixels[0] == 10, "Legacy zero-alpha clipboard bitmaps stay visible");

                // Live WPF/Win32 checks: hidden message window, real clipboard, real overlay.
                controller = new Controller(Path.Combine(root, "live"), false);
                controller.Busy = true;
                Check(controller.Sink.ClipboardRegistered, "Windows clipboard listener registers");
                Console.WriteLine("Global shortcut registration: " + controller.Sink.HotkeyRegistered + ", Windows error: " + controller.Sink.HotkeyError);
                Check(controller.Sink.HotkeyRegistered, controller.Sink.HotkeyText + " registers as a global hotkey");
                clipboardUsed = true;
                Clipboard.SetImage(Fixture(600, 400, 2));
                Pump(1100);
                Check(controller.Store.Items.Count == 1, "WM_CLIPBOARDUPDATE captures a new system clipboard image");
                var live = controller.Store.Items[0];
                Check(controller.Copy(live), "Copy action writes clipboard formats");
                Pump(600);
                var copiedPng = Clipboard.GetData("PNG") as MemoryStream;
                Check(Clipboard.ContainsImage() && copiedPng != null && copiedPng.Length > 8 && Clipboard.ContainsFileDropList(), "Copy offers readable bitmap, PNG and file-drop formats");
                Check(controller.Store.Items.Count == 1, "Copying a hung screenshot does not feed back into capture");
                Check(ImageStore.Digest(Clipboard.GetImage()) == ImageStore.Digest(ImageStore.Load(live.Path, 0)), "Copied image matches saved source pixels");
                controller.Store.Settings.ListenClipboard = false;
                Clipboard.SetImage(Fixture(320, 200, 3));
                Pump(500);
                Check(controller.Store.Items.Count == 1, "Clipboard capture can be disabled");
                controller.Store.Settings.ListenClipboard = true;
                var pngOnly = new DataObject();
                pngOnly.SetData("PNG", new MemoryStream(ImageStore.Png(Fixture(310, 220, 4))), false);
                Clipboard.SetDataObject(pngOnly, true);
                Pump(600);
                Check(controller.Store.Items.Count == 2, "PNG-only clipboard images are captured without relying on ContainsData");

                // Watch files written in stages, editor saves, and folder changes.
                string watchFolder = Path.Combine(root, "watched");
                Directory.CreateDirectory(watchFolder);
                controller.Store.Settings.WatchFolder = watchFolder;
                using (var watcher = new CaptureWatch(controller.Store, Dispatcher.CurrentDispatcher))
                {
                    string partial = Path.Combine(watchFolder, "partial.png");
                    File.WriteAllBytes(partial, new byte[0]);
                    Pump(350);
                    File.WriteAllBytes(partial, ImageStore.Png(Fixture(640, 480, 4)));
                    Pump(850);
                    Check(controller.Store.Items.Any(shot => shot.Path == partial), "Folder watcher retries partially written screenshots");
                    string oldHash = controller.Store.Items.First(shot => shot.Path == partial).Hash;
                    File.WriteAllBytes(partial, ImageStore.Png(Fixture(640, 480, 5)));
                    Pump(700);
                    Check(controller.Store.Items.First(shot => shot.Path == partial).Hash != oldHash, "Saved editor changes refresh the existing screenshot");
                    string renamed = Path.Combine(watchFolder, "renamed.png");
                    File.Move(partial, renamed);
                    Pump(700);
                    Check(controller.Store.Items.Any(shot => shot.Path == renamed) && !controller.Store.Items.Any(shot => shot.Path == partial), "Renamed screenshots update without dangling cards");
                }

                // Render actual native window content using only fixtures.
                controller.Store.Clear();
                controller.Store.AddBitmap(Fixture(700, 430, 0));
                controller.Store.AddBitmap(Fixture(450, 600, 1));
                controller.Store.AddBitmap(Fixture(650, 410, 2));
                var display = Forms.Screen.FromPoint(Native.Cursor());
                IntPtr foreground = Native.GetForegroundWindow();
                controller.Line.Reveal(display);
                Pump(1500);
                Check(Native.GetForegroundWindow() == foreground, "Overlay reveal does not steal foreground focus");
                var line = controller.Line;
                var canvas = (Canvas)line.Content;
                var cards = canvas.Children.OfType<ShotCard>().ToArray();
                Check(cards.Length == 3, "Live WPF overlay lays out three hung screenshots");
                Check(cards.All(card => Canvas.GetLeft(card) >= 0 && Canvas.GetTop(card) + card.Height < LineWindow.PanelHeight), "Portrait and landscape cards fit the panel");
                var center = cards[1].PointToScreen(new Point(cards[1].Width / 2, 55));
                var blank = line.PointToScreen(new Point(20, 170));
                IntPtr handle = new WindowInteropHelper(line).Handle;
                Check(Hit(handle, center) == 1 && Hit(handle, blank) == -1, "Native hit testing accepts cards and passes through empty areas");
                controller.Busy = false;
                line.UpdateInteraction(new System.Drawing.Point((int)blank.X, (int)blank.Y));
                Check((Native.GetWindowLong(handle, Native.GWL_EXSTYLE) & Native.WS_EX_TRANSPARENT) != 0, "Empty areas enable Windows click-through style");
                line.UpdateInteraction(new System.Drawing.Point((int)center.X, (int)center.Y));
                Check((Native.GetWindowLong(handle, Native.GWL_EXSTYLE) & Native.WS_EX_TRANSPARENT) == 0, "Photo areas restore mouse input");
                controller.Busy = true;
                SaveRender(line, Path.Combine(output, "line-render.png"));
                SavePreview(line, Path.Combine(output, "preview.png"), controller.Sink.HotkeyText);

                // Button event is the same accessible Click event used by WPF UI Automation.
                string original = controller.Store.Items.Last().Path;
                string originalExternal = Path.Combine(root, "original-kept.png");
                File.Copy(original, originalExternal);
                controller.Store.Remove(controller.Store.Items.Last(), false);
                controller.Store.AddFile(originalExternal, true);
                Pump(150);
                var lastCard = ((Canvas)line.Content).Children.OfType<ShotCard>().Last();
                lastCard.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(File.Exists(originalExternal) && controller.Store.Items.Count == 2, "Photo close button takes down external image without deletion");

                line.Width = 400;
                Pump(200);
                var narrowCards = ((Canvas)line.Content).Children.OfType<ShotCard>().ToArray();
                Check(narrowCards.Length == 1 && Canvas.GetLeft(narrowCards[0]) >= 0, "Narrow displays show recent cards without overflow");
                line.Reveal(display);
                Pump(450);
                SendMessage(controller.Sink.Handle, Native.WM_HOTKEY, new IntPtr(controller.Sink.HotkeyId), IntPtr.Zero);
                Pump(300);
                Check(!line.Revealed && !line.IsVisible, "Global hotkey message tucks away the line");
                SendMessage(controller.Sink.Handle, Native.WM_HOTKEY, new IntPtr(controller.Sink.HotkeyId), IntPtr.Zero);
                Pump(450);
                Check(line.Revealed, "Global hotkey message reveals the line again");

                var full = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                    ShowActivated = false, ShowInTaskbar = false, Width = 400, Height = 200,
                    Background = Brushes.Black, Title = "Snapline QA fullscreen fixture" };
                full.Show();
                var fullHandle = new WindowInteropHelper(full).Handle;
                var bounds = display.Bounds;
                Native.SetWindowPos(fullHandle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0014);
                Pump(200);
                Check(Native.IsFullScreen(fullHandle, bounds), "Fullscreen client geometry is recognized");
                Native.SetWindowPos(fullHandle, IntPtr.Zero, bounds.Left + 100, bounds.Top + 100, 400, 200, 0x0014);
                Pump(100);
                Check(!Native.IsFullScreen(fullHandle, bounds), "Ordinary window geometry is not fullscreen");
                full.Close();

                CheckHotkeys(controller, root, output);
                CheckConvenience(controller, root);
                CheckUpdates(root, output);
                controller.Dispose(); controller = null;
                using (var restarted = new Controller(Path.Combine(root, "live"), false))
                {
                    var saved = new ImageStore(Path.Combine(root, "live")).Settings;
                    Check(restarted.Sink.HotkeyRegistered && restarted.Sink.HotkeyKey == saved.HotkeyKey &&
                        restarted.Sink.HotkeyModifiers == saved.HotkeyModifiers, "Custom shortcut is registered again after restart");
                }
                SmokeExecutable(root);
                File.WriteAllLines(Path.Combine(output, "test-results.txt"), checks);
                Console.WriteLine("PASS " + checks.Count + " checks. Output: " + output);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("HRESULT: 0x" + ex.HResult.ToString("X8"));
                Console.Error.WriteLine(ex);
                File.WriteAllLines(Path.Combine(output, "test-results.txt"), checks.Concat(new[] { "FAIL " + ex.ToString() }));
                return 1;
            }
            finally
            {
                if (controller != null) controller.Dispose();
                if (clipboardUsed)
                {
                    if (clipboardBackup == null) Clipboard.Clear();
                    else Clipboard.SetDataObject(clipboardBackup, true);
                }
                app.Shutdown();
            }
        }

        private static void CheckConvenience(Controller controller, string root)
        {
            int count = controller.Store.Items.Count;
            var latest = controller.Store.Items[count - 1];
            bool copied = controller.CopyLatest();
            var copiedImage = Controller.RepairBitmapAlpha(Clipboard.GetImage());
            var copiedPng = Clipboard.GetData("PNG") as Stream;
            var lossless = BitmapFrame.Create(copiedPng, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Check(copied && ImageStore.Digest(lossless) == latest.Hash && copiedImage.PixelWidth == lossless.PixelWidth &&
                copiedImage.PixelHeight == lossless.PixelHeight && Clipboard.ContainsFileDropList(),
                "Quick Copy writes the latest screenshot to the real system clipboard");
            Check(controller.SetCollectionPaused(true) && new ImageStore(controller.Store.Root).Settings.CollectionPaused,
                "Pausing collection is saved and survives restart");
            Clipboard.SetImage(Fixture(390, 230, 8));
            Pump(450);
            Check(controller.Store.Items.Count == count, "Pause blocks new clipboard captures while preserving existing cards");
            Check(controller.CopyLatest(), "Existing screenshots remain copyable while collection is paused");
            Check(controller.SetCollectionPaused(false) && !new ImageStore(controller.Store.Root).Settings.CollectionPaused,
                "Resuming collection persists without changing the clipboard preference");
            Pump(300);
            Check(controller.Store.Items.Count == count, "Resuming does not collect clipboard images from the paused period");
            Clipboard.SetImage(Fixture(395, 235, 9));
            Pump(500);
            Check(controller.Store.Items.Count == count + 1, "New clipboard images are collected after resuming");

            var folderStore = new ImageStore(Path.Combine(root, "paused-watch"));
            folderStore.Settings.CollectionPaused = true;
            folderStore.Settings.WatchFolder = folderStore.Inbox;
            using (var watcher = new CaptureWatch(folderStore, Dispatcher.CurrentDispatcher))
            {
                string ignored = Path.Combine(folderStore.Inbox, "paused.png");
                File.WriteAllBytes(ignored, ImageStore.Png(Fixture(201, 151, 1)));
                Pump(500);
                Check(folderStore.Items.Count == 0, "Pause also blocks automatic screenshot-folder collection");
                folderStore.Settings.CollectionPaused = false;
                Pump(300);
                Check(folderStore.Items.Count == 0 && File.Exists(ignored), "Resuming does not import or delete folder images created while paused");
                File.WriteAllBytes(Path.Combine(folderStore.Inbox, "resumed.png"), ImageStore.Png(Fixture(202, 152, 2)));
                Pump(650);
                Check(folderStore.Items.Count == 1, "Screenshot-folder collection resumes for new files");
            }
            controller.SetAutoUpdates(false);
            Check(!new ImageStore(controller.Store.Root).Settings.AutoCheckUpdates, "Disabling automatic update checks survives restart");
            string command = Startup.Command(@"C:\含 空格\Snapline.exe", Path.Combine(root, "含 空格"));
            Check(command.StartsWith("\"C:\\含 空格\\Snapline.exe\" --data-dir \"") && command.EndsWith("含 空格\""),
                "Optional startup command quotes executable and data paths containing spaces and Unicode");
            Check(Startup.Command(@"C:\Snapline.exe", @"D:\").EndsWith("D:\\\\\""),
                "Startup command preserves a drive-root data directory through Windows argument escaping");
        }

        private sealed class UpdateHandler : HttpMessageHandler
        {
            internal byte[] Json;
            internal byte[] Package;
            internal HttpStatusCode Status = HttpStatusCode.OK;
            internal int Calls;
            internal bool Headers;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
            {
                cancel.ThrowIfCancellationRequested();
                Calls++;
                Headers = request.Headers.UserAgent.ToString().StartsWith("Snapline/") && !request.Headers.Contains("Authorization");
                return Task.FromResult(new HttpResponseMessage(Status) {
                    RequestMessage = request,
                    Content = new ByteArrayContent(request.RequestUri.Host == "raw.githubusercontent.com" ? Json : Package)
                });
            }
        }

        private static string ReleaseJson(string tag, byte[] package, bool draft, bool prerelease)
        {
            string digest;
            using (var hash = SHA256.Create()) digest = BitConverter.ToString(hash.ComputeHash(package)).Replace("-", "").ToLowerInvariant();
            return "{\"tag_name\":\"" + tag + "\",\"draft\":" + draft.ToString().ToLowerInvariant() +
                ",\"prerelease\":" + prerelease.ToString().ToLowerInvariant() + ",\"body\":\"Update fixture\",\"assets\":[{" +
                "\"name\":\"Snapline-Windows-" + tag + ".zip\",\"state\":\"uploaded\",\"size\":" + package.Length +
                ",\"digest\":\"sha256:" + digest + "\",\"browser_download_url\":\"" + Updates.Repository +
                "/releases/download/" + tag + "/Snapline-Windows-" + tag + ".zip\"}]}";
        }

        private static void CheckUpdates(string root, string output)
        {
            var package = Encoding.UTF8.GetBytes("PK generated update package fixture");
            string stable = ReleaseJson("v1.3.0", package, false, false);
            string preview = ReleaseJson("v1.4.0", package, false, true);
            string draft = ReleaseJson("v8.0.0", package, true, false);
            byte[] json = Encoding.UTF8.GetBytes("[" + stable + "," + draft + "," + preview + "]");
            var release = Updates.Parse(json, Updates.CurrentVersion);
            Check(release.Tag == "v1.4.0" && release.Prerelease, "Update checks select the highest published version including prereleases, skipping drafts");
            Check(Updates.Parse(json, new Version(1, 4, 0)) == null, "Equal and older release versions do not prompt for updates");
            Check(Updates.Parse(Encoding.UTF8.GetBytes("[" + stable.Replace("github.com/jiuyang5354", "github.com/other-owner") + "]"), Updates.CurrentVersion) == null,
                "A package URL outside the publishing repository is rejected");
            Check(Updates.Parse(Encoding.UTF8.GetBytes("[" + stable.Replace("sha256:", "sha1:") + "]"), Updates.CurrentVersion) == null,
                "Packages without a complete SHA-256 digest are not offered for download");
            var settings = new Settings();
            var now = DateTime.UtcNow;
            Check(Updates.Due(settings, now), "New installations enable an initial background update check");
            settings.UpdateCheckedUtcTicks = now.Ticks;
            Check(!Updates.Due(settings, now.AddHours(23)) && Updates.Due(settings, now.AddHours(24)),
                "Automatic update checks are limited to once per 24 hours across restarts");
            settings.AutoCheckUpdates = false;
            Check(!Updates.Due(settings, now.AddDays(3)), "Disabling automatic checks prevents scheduled network requests");
            var legacy = new ImageStore(Path.Combine(root, "legacy-settings")).Settings;
            Check(legacy.AutoCheckUpdates && !legacy.CollectionPaused, "Older settings gain update checks while keeping collection active");

            var handler = new UpdateHandler { Json = json, Package = package };
            using (var updater = new Updates(handler))
            {
                var found = updater.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();
                Check(found.Tag == "v1.4.0" && handler.Headers, "HTTP update pipeline sends an app user-agent without an account token");
                string destination = Path.Combine(root, "更新 包.zip");
                updater.DownloadAsync(found, destination, null, CancellationToken.None).GetAwaiter().GetResult();
                Check(File.ReadAllBytes(destination).SequenceEqual(package), "Verified downloads arrive intact at a Unicode destination");
                handler.Package = Enumerable.Repeat((byte)42, package.Length).ToArray();
                bool rejected = false;
                try { updater.DownloadAsync(found, destination, null, CancellationToken.None).GetAwaiter().GetResult(); }
                catch (IOException) { rejected = true; }
                Check(rejected && File.ReadAllBytes(destination).SequenceEqual(package) && Directory.GetFiles(root, "*.download").Length == 0,
                    "Checksum failures preserve existing files and remove incomplete download files");
                handler.Package = new byte[package.Length - 1];
                rejected = false;
                try { updater.DownloadAsync(found, destination, null, CancellationToken.None).GetAwaiter().GetResult(); }
                catch (IOException) { rejected = true; }
                Check(rejected && File.ReadAllBytes(destination).SequenceEqual(package), "Truncated downloads are rejected without replacing a saved package");
                using (var cancel = new CancellationTokenSource())
                {
                    cancel.Cancel();
                    rejected = false;
                    try { updater.DownloadAsync(found, destination, null, cancel.Token).GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) { rejected = true; }
                    Check(rejected && Directory.GetFiles(root, "*.download").Length == 0, "Cancelled downloads leave no temporary update files");
                }
                handler.Status = HttpStatusCode.Forbidden;
                rejected = false;
                try { updater.CheckAsync(CancellationToken.None).GetAwaiter().GetResult(); }
                catch (HttpRequestException) { rejected = true; }
                Check(rejected, "HTTP access errors are returned without a false update result");
            }

            var automatic = new UpdateHandler { Json = json, Package = package };
            string autoRoot = Path.Combine(root, "automatic-updates");
            using (var controller = new Controller(autoRoot, false, new Updates(automatic)))
            {
                var timer = (DispatcherTimer)typeof(Controller).GetField("updateTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller);
                var tray = (Forms.NotifyIcon)typeof(Controller).GetField("tray", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller);
                tray.ContextMenuStrip.Show(new System.Drawing.Point(30, 30));
                Pump(100);
                var menuItems = tray.ContextMenuStrip.Items.OfType<Forms.ToolStripMenuItem>().ToArray();
                Check(menuItems.Any(item => item.Text == "复制最近一张" && !item.Enabled) &&
                    menuItems.Any(item => item.Text == "暂停所有自动收集" && !item.Checked) &&
                    menuItems.Any(item => item.Text == "自动检查更新（含预发布）" && item.Checked) &&
                    menuItems.Any(item => item.Text == "开机启动" && !item.Checked),
                    "The actual tray menu exposes convenience controls with auto-check enabled and optional startup disabled");
                using (var image = new System.Drawing.Bitmap(tray.ContextMenuStrip.Width, tray.ContextMenuStrip.Height))
                {
                    tray.ContextMenuStrip.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, image.Width, image.Height));
                    image.Save(Path.Combine(output, "tray-menu.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                tray.ContextMenuStrip.Close();
                controller.Welcome();
                timer.Interval = TimeSpan.FromMilliseconds(100);
                Pump(400);
                var saved = new ImageStore(autoRoot).Settings;
                Check(automatic.Calls == 1 && saved.UpdateNotifiedTag == release.Tag && tray.ContextMenuStrip.Items.OfType<Forms.ToolStripMenuItem>().Any(item => item.Text == "更新到 " + release.Tag + "…"),
                    "The real background timer persists a version reminder and exposes the download entry in the tray");
                timer.Interval = TimeSpan.FromMilliseconds(100);
                Pump(350);
                Check(automatic.Calls == 1, "The scheduler does not repeat a recent version check or reminder");
                controller.SetAutoUpdates(false);
                controller.Store.Settings.UpdateCheckedUtcTicks = 0;
                timer.Interval = TimeSpan.FromMilliseconds(100);
                Pump(350);
                Check(automatic.Calls == 1, "The live scheduler stays offline after automatic checks are disabled");
            }
            using (var dialog = new UpdateDialog(release, delegate { return Task.FromResult(0); }, delegate { }))
            using (var timer = new Forms.Timer { Interval = 150 })
            {
                timer.Tick += delegate {
                    timer.Stop();
                    using (var image = new System.Drawing.Bitmap(dialog.Width, dialog.Height))
                    {
                        dialog.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, dialog.Width, dialog.Height));
                        image.Save(Path.Combine(output, "update-dialog.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    dialog.Controls.OfType<Forms.Button>().Single(button => button.Text == "稍后").PerformClick();
                };
                timer.Start();
                Check(dialog.ShowDialog() == Forms.DialogResult.Cancel, "The actual update dialog shows release notes and can be dismissed without downloading");
            }
        }

        private static int CheckNetwork(string output)
        {
            Directory.CreateDirectory(output);
            try
            {
                Release release;
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 1024 * 1024 })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("Snapline-QA/" + Updates.CurrentVersion);
                    release = Updates.Parse(http.GetByteArrayAsync(Updates.Feed).GetAwaiter().GetResult(), new Version(0, 0, 0));
                }
                Check(release != null, "Anonymous HTTPS retrieves a valid published update feed");
                using (var updater = new Updates())
                {
                    var available = updater.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();
                    Check(release.Version > Updates.CurrentVersion ? available != null && available.Tag == release.Tag : available == null,
                        "The actual app HTTP client correctly compares the public release with its own version");
                    string path = Path.Combine(output, release.Package.Name);
                    updater.DownloadAsync(release, path, null, CancellationToken.None).GetAwaiter().GetResult();
                    Check(File.Exists(path) && new FileInfo(path).Length == release.Package.Size,
                        "The actual app downloads and SHA-256-verifies the public GitHub release package without an account");
                    Check(Directory.GetFiles(output, "*.download").Length == 0, "A real completed download leaves no incomplete files");
                }
                File.WriteAllLines(Path.Combine(output, "network-results.txt"), checks);
                Console.WriteLine("PASS " + checks.Count + " real network checks.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        private static uint FreeHotkey(uint modifiers)
        {
            for (uint key = 0x75; key <= 0x7a; key++)
                if (Native.RegisterHotKey(IntPtr.Zero, 90, modifiers | 0x4000, key))
                {
                    Native.UnregisterHotKey(IntPtr.Zero, 90);
                    return key;
                }
            throw new Exception("No test shortcut is available.");
        }

        private static void CheckHotkeys(Controller controller, string root, string output)
        {
            controller.Busy = true;
            string legacyRoot = Path.Combine(root, "legacy-settings");
            Directory.CreateDirectory(legacyRoot);
            File.WriteAllText(Path.Combine(legacyRoot, "settings.json"), "{\"ListenClipboard\":false,\"Paths\":[]}");
            var legacy = new ImageStore(legacyRoot).Settings;
            Check(legacy.HotkeyKey == Hotkey.DefaultKey && legacy.HotkeyModifiers == Hotkey.DefaultModifiers && !legacy.ListenClipboard,
                "Older settings gain the default shortcut without changing clipboard preferences");
            Check(Hotkey.Modifiers(Forms.Keys.Control | Forms.Keys.Alt | Forms.Keys.Shift | Forms.Keys.H, true) == 15,
                "Recording includes Ctrl, Alt, Shift and Win modifiers");

            uint oldKey = controller.Sink.HotkeyKey, oldModifiers = controller.Sink.HotkeyModifiers;
            int oldId = controller.Sink.HotkeyId;
            uint custom = FreeHotkey(7);
            string error;
            Check(controller.ChangeHotkey(custom, 7, out error) && controller.Sink.HotkeyRegistered,
                "A custom combination applies without restarting the app");
            bool released = Native.RegisterHotKey(IntPtr.Zero, 90, oldModifiers | 0x4000, oldKey);
            if (released) Native.UnregisterHotKey(IntPtr.Zero, 90);
            Check(released, "Changing the shortcut releases the previous native registration");
            controller.Line.Tuck();
            SendMessage(controller.Sink.Handle, Native.WM_HOTKEY, new IntPtr(oldId), IntPtr.Zero);
            Check(!controller.Line.Revealed, "Queued messages for the old shortcut no longer toggle the line");
            SendMessage(controller.Sink.Handle, Native.WM_HOTKEY, new IntPtr(controller.Sink.HotkeyId), IntPtr.Zero);
            Check(controller.Line.Revealed, "The replacement native hotkey message still toggles the line");
            var saved = new ImageStore(controller.Store.Root).Settings;
            Check(saved.HotkeyKey == custom && saved.HotkeyModifiers == 7, "Custom shortcut is persisted to settings.json");

            uint occupied = FreeHotkey(7);
            Check(Native.RegisterHotKey(IntPtr.Zero, 90, 0x4007, occupied), "A separate registration creates a real shortcut conflict");
            try
            {
                Check(!controller.ChangeHotkey(occupied, 7, out error) && error != null && controller.Sink.HotkeyRegistered &&
                    controller.Sink.HotkeyKey == custom && controller.Store.Settings.HotkeyKey == custom,
                    "Conflicting bindings preserve the active shortcut and saved settings");
            }
            finally { Native.UnregisterHotKey(IntPtr.Zero, 90); }
            Check(!controller.ChangeHotkey(0x7b, 0, out error) && error.Contains("F12") &&
                !controller.ChangeHotkey(0x10, 0, out error) && controller.Sink.HotkeyKey == custom,
                "Reserved F12 and modifier-only bindings are rejected without changing the shortcut");
            using (var locked = new FileStream(Path.Combine(controller.Store.Root, "settings.json.tmp"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                Check(!controller.ChangeHotkey(FreeHotkey(7), 7, out error) && controller.Sink.HotkeyRegistered &&
                    controller.Sink.HotkeyKey == custom && controller.Sink.HotkeyModifiers == 7 && controller.Store.Settings.HotkeyKey == custom,
                    "A settings write failure restores the prior native binding and preferences");

            uint single = FreeHotkey(0);
            DialogAction(controller, delegate(HotkeyDialog dialog) {
                var input = dialog.Controls.OfType<HotkeyInput>().Single();
                bool revealed = controller.Line.Revealed;
                SendMessage(controller.Sink.Handle, Native.WM_HOTKEY, new IntPtr(controller.Sink.HotkeyId), IntPtr.Zero);
                Check(input.Text == controller.Sink.HotkeyText && controller.Line.Revealed == revealed,
                    "The existing global shortcut can be recorded without toggling the overlay");
                input.Focus();
                SendMessage(input.Handle, 0x0100, new IntPtr(single), IntPtr.Zero);
                Check(input.Text == Hotkey.Text(single, 0) && dialog.Controls.OfType<Forms.Button>().Single(button => button.Text == "保存").Enabled,
                    "The native shortcut input records a single key and enables Save");
                using (var image = new System.Drawing.Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, dialog.Width, dialog.Height));
                    image.Save(Path.Combine(output, "hotkey-dialog.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                dialog.Controls.OfType<Forms.Button>().Single(button => button.Text == "保存").PerformClick();
            });
            Check(controller.Sink.HotkeyKey == single && controller.Sink.HotkeyModifiers == 0 &&
                new ImageStore(controller.Store.Root).Settings.HotkeyKey == single,
                "Saving from the actual dialog applies and persists a single-key binding");
            var tray = (Forms.NotifyIcon)typeof(Controller).GetField("tray", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller);
            Check(tray.ContextMenuStrip.Items.OfType<Forms.ToolStripMenuItem>().First().Text.Contains(Hotkey.Text(single, 0)),
                "The tray menu updates to display the custom shortcut");
            DialogAction(controller, delegate(HotkeyDialog dialog) {
                SendMessage(dialog.Controls.OfType<HotkeyInput>().Single().Handle, 0x0100, new IntPtr(FreeHotkey(0)), IntPtr.Zero);
                dialog.Controls.OfType<Forms.Button>().Single(button => button.Text == "取消").PerformClick();
            });
            Check(controller.Sink.HotkeyKey == single && new ImageStore(controller.Store.Root).Settings.HotkeyKey == single,
                "Canceling a recorded shortcut preserves the active and saved binding");
            DialogAction(controller, delegate(HotkeyDialog dialog) {
                dialog.Controls.OfType<Forms.Button>().Single(button => button.Text == "恢复默认").PerformClick();
                Check(dialog.Controls.OfType<HotkeyInput>().Single().Text == Hotkey.Text(Hotkey.DefaultKey, Hotkey.DefaultModifiers),
                    "Restore Default selects Ctrl + Alt + T in the dialog");
                dialog.Controls.OfType<Forms.Button>().Single(button => button.Text == "取消").PerformClick();
            });
        }

        private static void DialogAction(Controller controller, Action<HotkeyDialog> action)
        {
            Exception failure = null;
            using (var timer = new Forms.Timer { Interval = 150 })
            {
                timer.Tick += delegate {
                    var dialog = Forms.Application.OpenForms.OfType<HotkeyDialog>().FirstOrDefault();
                    if (dialog == null) return;
                    timer.Stop();
                    try { action(dialog); }
                    catch (Exception ex) { failure = ex; }
                    finally { dialog.Close(); }
                };
                timer.Start();
                controller.ShowHotkeySettings();
            }
            controller.Busy = true;
            if (failure != null) throw failure;
        }

        private static DataObject BackupClipboard()
        {
            var original = Clipboard.GetDataObject();
            if (original == null) return null;
            var backup = new DataObject();
            foreach (var format in original.GetFormats(false))
            {
                try
                {
                    object value = original.GetData(format, false);
                    var stream = value as MemoryStream;
                    if (stream != null) value = new MemoryStream(stream.ToArray());
                    if (value != null) backup.SetData(format, value, false);
                }
                catch (ExternalException) { }
            }
            return backup;
        }

        private static void Check(bool success, string message)
        {
            if (!success) throw new Exception("FAIL: " + message);
            checks.Add("PASS: " + message);
            Console.WriteLine("PASS: " + message);
        }

        private static void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private static int Hit(IntPtr handle, Point point)
        {
            int packed = ((int)point.Y << 16) | ((int)point.X & 0xffff);
            return SendMessage(handle, 0x0084, IntPtr.Zero, new IntPtr(packed)).ToInt32();
        }

        private static BitmapSource Fixture(int width, int height, int style)
        {
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
            {
                bool dark = style % 3 == 2;
                dc.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(26, 33, 42) : Color.FromRgb(241, 239, 233)), null, new Rect(0, 0, width, height));
                DrawText(dc, style % 3 == 0 ? "A quiet place" : style % 3 == 1 ? "FIELD NOTES" : "WORK / 026", 28, dark ? Brushes.White : Brushes.Black, 30, 35);
                if (style % 3 == 1)
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(85, 139, 120)), null, new Rect(30, 95, width - 60, height - 125));
                    dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(228, 207, 142)), null, new Point(width * 0.55, height * 0.4), width * 0.16, width * 0.16);
                    var mountain = new StreamGeometry();
                    using (var g = mountain.Open()) { g.BeginFigure(new Point(30, height - 30), true, true); g.LineTo(new Point(width * 0.45, height * 0.48), true, false); g.LineTo(new Point(width - 30, height - 30), true, false); }
                    dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(36, 73, 67)), null, mountain);
                }
                else if (dark)
                {
                    for (int i = 0; i < 6; i++) dc.DrawRectangle(new SolidColorBrush(i == 4 ? Color.FromRgb(121, 224, 192) : Color.FromRgb(65, 96, 90)), null,
                        new Rect(35 + i * (width - 70) / 6.0, height * 0.8 - (i * 17 + 45), (width - 100) / 7.0, i * 17 + 45));
                }
                else
                {
                    DrawText(dc, "A small collection of things worth keeping.", 14, new SolidColorBrush(Color.FromRgb(110, 116, 115)), 30, 85);
                    for (int i = 0; i < 6; i++) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(196, 200, 196)), null, new Rect(30, 130 + i * 25, width - 80 - (i % 3) * 40, 7));
                }
            }
            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            target.Render(drawing); target.Freeze(); return target;
        }

        private static void DrawText(DrawingContext dc, string text, double size, Brush brush, double x, double y)
        {
            dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, 1.0), new Point(x, y));
        }

        private static void SaveRender(LineWindow line, string path)
        {
            var image = new RenderTargetBitmap((int)Math.Ceiling(line.ActualWidth), (int)Math.Ceiling(line.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            image.Render(line);
            File.WriteAllBytes(path, ImageStore.Png(image));
        }

        private static void SmokeExecutable(string root)
        {
            string executable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Snapline.exe");
            string dataRoot = Path.Combine(root, "executable-smoke");
            var launch = new ProcessStartInfo(executable, "--data-dir \"" + dataRoot + "\"") {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var process = Process.Start(launch))
            {
                try
                {
                    process.WaitForInputIdle(4000);
                    Pump(700);
                    Check(!process.HasExited && Directory.Exists(Path.Combine(dataRoot, "Inbox")), "Released EXE starts with an isolated data directory");
                    using (var second = Process.Start(launch))
                    {
                        Check(second.WaitForExit(4000) && second.ExitCode == 0 && !process.HasExited, "Launching EXE twice reuses the running instance");
                    }
                    uint thread = 0;
                    IntPtr messageWindow = IntPtr.Zero;
                    while ((messageWindow = FindWindowEx(new IntPtr(-3), messageWindow, null, "Snapline messages")) != IntPtr.Zero)
                    {
                        uint owner;
                        uint candidate = GetWindowThreadProcessId(messageWindow, out owner);
                        if (owner == process.Id) { thread = candidate; break; }
                    }
                    Check(thread != 0 && PostThreadMessage(thread, 0x0012, IntPtr.Zero, IntPtr.Zero) && process.WaitForExit(5000) && process.ExitCode == 0,
                        "Released EXE shuts down its GUI message loop cleanly");
                }
                finally { if (!process.HasExited) process.Kill(); }
            }
        }

        private static void SavePreview(LineWindow line, string path, string hotkey)
        {
            var visual = new DrawingVisual();
            const int width = 1200, height = 500;
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(22, 28, 35)), null, new Rect(0, 0, width, height));
                DrawText(dc, "Snapline", 40, Brushes.White, 64, 42);
                DrawText(dc, "Screenshots, within reach.  /  Windows", 15, new SolidColorBrush(Color.FromRgb(159, 177, 184)), 66, 101);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(35, 49, 52)), null, new Rect(48, 155, 1104, 280));
                // Crop the native overlay around its center instead of scaling down photos on wide monitors.
                dc.PushClip(new RectangleGeometry(new Rect(48, 155, 1104, 280)));
                dc.DrawRectangle(new VisualBrush(line) { Stretch = Stretch.None, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Top }, null,
                    new Rect(48 + (1104 - line.ActualWidth) / 2, 169, line.ActualWidth, line.ActualHeight));
                dc.Pop();
                DrawText(dc, hotkey + "   /   click to copy · hold to edit · drag to use", 13,
                    new SolidColorBrush(Color.FromRgb(159, 177, 184)), 66, 461);
            }
            var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            image.Render(visual);
            File.WriteAllBytes(path, ImageStore.Png(image));
        }
    }
}
