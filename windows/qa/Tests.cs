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
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out Native.RECT rectangle);
        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--inbox") return CheckInboxLocation(Path.GetFullPath(args[1]));
            if (args.Length == 2 && args[0] == "--settings") return CheckSettingsOnly(Path.GetFullPath(args[1]));
            if (args.Length == 2 && args[0] == "--announcement-content") {
                string announcementOutput = Path.GetFullPath(args[1]); Directory.CreateDirectory(announcementOutput);
                var announcementApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                try {
                    CheckAnnouncements(announcementOutput, announcementOutput, false);
                    File.WriteAllLines(Path.Combine(announcementOutput, "announcement-content-results.txt"), checks);
                    Console.WriteLine("PASS " + checks.Count + " announcement checks without keyboard input."); return 0;
                } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
                finally { announcementApp.Shutdown(); }
            }
            if (args.Length == 2 && args[0] == "--materials") return CheckSettingsMaterials(Path.GetFullPath(args[1]));
            if (args.Length == 2 && args[0] == "--network") return CheckNetwork(Path.GetFullPath(args[1]));
            if (args.Length == 2 && args[0] == "--editor") return CheckPaint(Path.GetFullPath(args[1]));
            if (args.Length == 2 && args[0] == "--bindings") {
                string bindingOutput = Path.GetFullPath(args[1]); Directory.CreateDirectory(bindingOutput);
                var bindingApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                try {
                    CheckBindingRegistrations();
                    File.WriteAllLines(Path.Combine(bindingOutput, "binding-results.txt"), checks);
                    Console.WriteLine("PASS " + checks.Count + " binding checks without keyboard, mouse or clipboard input."); return 0;
                }
                catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
                finally { bindingApp.Shutdown(); }
            }
            bool featuresOnly = args.Length == 2 && args[0] == "--features";
            bool announcementsOnly = args.Length == 2 && args[0] == "--announcements";
            string output = Path.GetFullPath(args[featuresOnly || announcementsOnly ? 1 : 0]);
            string root = Path.Combine(output, "run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(root);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            DataObject clipboardBackup = BackupClipboard();
            bool clipboardUsed = false;
            Controller controller = null;
            try
            {
                if (featuresOnly) {
                    clipboardUsed = true; CheckHoverAndCapture(root, output);
                    File.WriteAllLines(Path.Combine(output, "feature-results.txt"), checks);
                    Console.WriteLine("PASS " + checks.Count + " feature checks. Output: " + output); return 0;
                }
                if (announcementsOnly) {
                    CheckAnnouncements(root, output); SmokeExecutable(root);
                    File.WriteAllLines(Path.Combine(output, "announcement-results.txt"), checks);
                    Console.WriteLine("PASS " + checks.Count + " announcement checks. Output: " + output); return 0;
                }
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
                string liveRoot = Path.Combine(root, "live");
                var liveSettings = new ImageStore(liveRoot);
                liveSettings.Settings.HotkeyKey = FreeHotkey(7);
                liveSettings.Settings.CaptureHotkeyKey = FreeHotkey(6);
                liveSettings.Settings.CaptureHotkeyModifiers = 6;
                liveSettings.Settings.HotkeyModifiers = 7;
                liveSettings.Save();
                controller = new Controller(liveRoot, false, new Updates(new UpdateHandler { Json = Encoding.UTF8.GetBytes("[]") }));
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
                line.PreviewBackdrop(Fixture((int)Math.Ceiling(line.ActualWidth), (int)LineWindow.PanelHeight, 4), 1);
                Pump(200);
                var clearPixel = new RenderTargetBitmap((int)Math.Ceiling(line.ActualWidth), (int)LineWindow.PanelHeight, 96, 96, PixelFormats.Pbgra32);
                clearPixel.Render(line);
                var blankPixel = new byte[4]; clearPixel.CopyPixels(new Int32Rect(20, 80, 1, 1), blankPixel, 4, 0);
                Check(blankPixel[3] == 0 && Hit(handle, line.PointToScreen(new Point(20, 80))) == -1,
                    "Space outside the individual photo frames stays fully transparent and passes clicks to the desktop");
                cards = canvas.Children.OfType<ShotCard>().ToArray();
                var frames = Descendants(line).OfType<GlassFrame>().ToArray();
                Check(frames.Length == cards.Length && frames.All(frame => frame.Children.OfType<Image>().Any(materialImage => !materialImage.IsHitTestVisible && materialImage.Source != null)),
                    "Every live photo container has its own rendered optical material beneath the screenshot");
                foreach (var card in cards)
                {
                    var photo = Descendants(card).OfType<Image>().Single(photoImage => photoImage.Source == card.Shot.Thumbnail);
                    Check(Math.Abs(photo.ActualWidth / photo.ActualHeight - card.Shot.Thumbnail.Width / card.Shot.Thumbnail.Height) < 0.001,
                        "Original aspect ratio is preserved without cropping for " + card.Shot.Thumbnail.PixelWidth + "x" + card.Shot.Thumbnail.PixelHeight);
                }
                CheckGlassMaterial();
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
                Check(new[] { line.CaptureButton, line.LatestButton, line.PauseButton, line.SettingsButton }.All(button => {
                    var point = button.TranslatePoint(new Point(), line);
                    return button.ActualWidth >= 28 && point.X >= 0 && point.X + button.ActualWidth <= line.ActualWidth && point.Y + button.ActualHeight <= line.ActualHeight;
                }), "All toolbar actions remain visible and usable on a 400 DIP display");
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
                CheckCaptureHotkeys(controller, root, output);
                CheckSettings(controller, output);
                CheckConvenience(controller, root);
                CheckUpdates(root, output);
                CheckAnnouncements(root, output);
                CheckInstallerUpdates(root, output);
                controller.Busy = true; controller.Line.Tuck(); Pump(300);
                CheckHoverAndCapture(root, output);
                controller.Dispose(); controller = null;
                using (var restarted = new Controller(Path.Combine(root, "live"), false))
                {
                    var saved = new ImageStore(Path.Combine(root, "live")).Settings;
                    Check(restarted.Sink.HotkeyRegistered && restarted.Sink.HotkeyKey == saved.HotkeyKey &&
                        restarted.Sink.HotkeyModifiers == saved.HotkeyModifiers, "Custom shortcut is registered again after restart");
                    Check(restarted.Sink.CaptureHotkeyRegistered && restarted.Sink.CaptureHotkeyKey == saved.CaptureHotkeyKey &&
                        restarted.Sink.CaptureHotkeyModifiers == saved.CaptureHotkeyModifiers, "Custom region capture shortcut is registered again after restart");
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
                File.WriteAllLines(Path.Combine(output, featuresOnly ? "feature-results.txt" : announcementsOnly ? "announcement-results.txt" : "test-results.txt"), checks.Concat(new[] { "FAIL " + ex.ToString() }));
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

        private static string ReleaseJson(string tag, byte[] package, bool draft, bool prerelease, bool includeInstaller = false)
        {
            string digest;
            using (var hash = SHA256.Create()) digest = BitConverter.ToString(hash.ComputeHash(package)).Replace("-", "").ToLowerInvariant();
            string asset = "{" +
                "\"name\":\"Snapline-Windows-" + tag + ".zip\",\"state\":\"uploaded\",\"size\":" + package.Length +
                ",\"digest\":\"sha256:" + digest + "\",\"browser_download_url\":\"" + Updates.Repository +
                "/releases/download/" + tag + "/Snapline-Windows-" + tag + ".zip\"}";
            return "{\"tag_name\":\"" + tag + "\",\"draft\":" + draft.ToString().ToLowerInvariant() +
                ",\"prerelease\":" + prerelease.ToString().ToLowerInvariant() + ",\"body\":\"Update fixture\",\"assets\":[" + asset +
                (includeInstaller ? "," + asset.Replace("Snapline-Windows-", "Snapline-Windows-Setup-").Replace(".zip", ".exe") : "") + "]}";
        }

        private static void CheckUpdates(string root, string output)
        {
            Check(System.Net.ServicePointManager.SecurityProtocol == System.Net.SecurityProtocolType.SystemDefault &&
                AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName == ".NETFramework,Version=v4.8",
                "The executable targets .NET Framework 4.8 and negotiates HTTPS using operating-system TLS defaults");
            var package = Encoding.UTF8.GetBytes("PK generated update package fixture");
            string stable = ReleaseJson("v1.3.0", package, false, false);
            string preview = ReleaseJson("v1.5.0", package, false, true);
            string draft = ReleaseJson("v8.0.0", package, true, false);
            byte[] json = Encoding.UTF8.GetBytes("[" + stable + "," + draft + "," + preview + "]");
            var release = Updates.Parse(json, Updates.CurrentVersion);
            Check(release.Tag == "v1.5.0" && release.Prerelease, "Update checks select the highest published version including prereleases, skipping drafts");
            Check(Updates.Parse(json, new Version(1, 5, 0)) == null, "Equal and older release versions do not prompt for updates");
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
                Check(found.Tag == "v1.5.0" && handler.Headers, "HTTP update pipeline sends an app user-agent without an account token");
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
            using (var dialog = new UpdateDialog(release, delegate { return Task.FromResult(0); }, delegate { }, delegate { }))
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

        private static void AnnouncementAction(Action open, Action<AnnouncementWindow> inspect)
        {
            Exception failure = null; bool shown = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            try {
                timer.Tick += delegate {
                    var dialog = Application.Current.Windows.OfType<AnnouncementWindow>().FirstOrDefault(window => window.IsVisible);
                    if (dialog == null) return;
                    timer.Stop(); shown = true;
                    try { inspect(dialog); }
                    catch (Exception ex) { failure = ex; }
                    finally { if (dialog.IsVisible) dialog.Close(); }
                };
                timer.Start(); open();
            }
            finally { timer.Stop(); }
            if (failure != null) throw failure;
            if (!shown) throw new Exception("The announcement action did not show its real window.");
        }

        private static void CheckAnnouncements(string root, string output, bool keyboard = true)
        {
            string data = Path.Combine(root, "announcements");
            var original = new ImageStore(data);
            original.Settings.ListenClipboard = false; original.Settings.CollectionPaused = true;
            original.Settings.AutoCheckUpdates = false; original.Settings.IncludeCursor = true;
            original.Settings.HotkeyKey = FreeHotkey(7); original.Settings.HotkeyModifiers = 7;
            var shot = original.AddBitmap(Fixture(420, 280, 4));
            string settingsPath = Path.Combine(data, "settings.json");
            string oldSettings = File.ReadAllText(settingsPath);
            oldSettings = System.Text.RegularExpressions.Regex.Replace(oldSettings, "\\\"AnnouncementSeenVersion\\\":null,?", "");
            File.WriteAllText(settingsPath, oldSettings);
            var handler = new UpdateHandler { Json = Encoding.UTF8.GetBytes("[]") };
            using (var controller = new Controller(data, false, new Updates(handler)))
            {
                Check(AnnouncementWindow.Due(controller.Store.Settings) && controller.Store.Settings.AnnouncementSeenVersion == null,
                    "Legacy settings without announcement history show the current version once");
                AnnouncementAction(delegate { controller.ShowAnnouncement(true); }, delegate(AnnouncementWindow dialog) {
                    Check(dialog.Notes.IsReadOnly && dialog.Notes.Text.Contains("更新公告") && dialog.Notes.Text.Contains("Alejandro Buján"),
                        "The real announcement displays bundled readable release notes and upstream attribution offline");
                    Check(dialog.Notes.Text.Contains("显卡采样坐标") && dialog.Notes.Text.Contains("后台线程") && dialog.Notes.Text.Contains("整个按钮区域"),
                        "Bundled notes retain the recent drag, asynchronous metal and full-button input changes");
                    Check(dialog.Notes.Text.Contains("分块计算校验") && dialog.Notes.Text.Contains("原去重结果") && dialog.Notes.Text.Contains("仍需实际确认"),
                        "Bundled notes describe the memory fix, digest compatibility and unconfirmed customer environment");
                    Check(controller.Busy && !controller.Line.Revealed, "An open announcement suspends shelf input and keeps the shelf hidden");
                    controller.ShowAnnouncement();
                    Check(Application.Current.Windows.OfType<AnnouncementWindow>().Count(window => window.IsVisible) == 1,
                        "Reopening while the announcement is visible reuses the existing dialog without duplicates");
                    dialog.UpdateLayout();
                    var view = (FrameworkElement)dialog.Content;
                    var image = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth * 1.5), (int)Math.Ceiling(view.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
                    var drawing = new DrawingVisual();
                    using (var dc = drawing.RenderOpen()) {
                        dc.DrawRectangle(dialog.Background, null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
                        dc.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
                    }
                    image.Render(drawing); File.WriteAllBytes(Path.Combine(output, "announcement.png"), ImageStore.Png(image));
                    Check(dialog.Dismiss.IsDefault && dialog.Dismiss.IsCancel && dialog.Dismiss.IsVisible &&
                        dialog.Notes.ActualHeight > 150 && dialog.Notes.VerticalScrollBarVisibility == ScrollBarVisibility.Auto,
                        "Announcement content scrolls while its Enter and Escape dismissal button remains accessible");
                    dialog.Dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                var saved = new ImageStore(data);
                Check(saved.Settings.AnnouncementSeenVersion == AnnouncementWindow.Version && !AnnouncementWindow.Due(saved.Settings),
                    "Closing the real announcement persists its version and prevents repeated automatic display after reload");
                Check(!saved.Settings.ListenClipboard && saved.Settings.CollectionPaused && !saved.Settings.AutoCheckUpdates && saved.Settings.IncludeCursor &&
                    saved.Settings.HotkeyKey == original.Settings.HotkeyKey && saved.Items.Count == 1 && File.Exists(shot.Path),
                    "Reading an announcement preserves existing collection, cursor, hotkey and screenshot settings");
                string unchanged = File.ReadAllText(settingsPath);
                controller.ShowAnnouncement(true);
                Check(!controller.Busy && !Application.Current.Windows.OfType<AnnouncementWindow>().Any(window => window.IsVisible) && File.ReadAllText(settingsPath) == unchanged,
                    "A repeated automatic request returns without a dialog or another settings write");
                var tray = (Forms.NotifyIcon)typeof(Controller).GetField("tray", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller);
                AnnouncementAction(delegate { tray.ContextMenuStrip.Items.OfType<Forms.ToolStripMenuItem>().Single(item => item.Text == "更新公告…").PerformClick(); },
                    delegate(AnnouncementWindow dialog) {
                        Check(!AnnouncementWindow.Due(controller.Store.Settings), "The real tray command reopens an already-read announcement");
                        if (!keyboard) { dialog.Dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return; }
                        IntPtr handle = new WindowInteropHelper(dialog).Handle; SetForegroundWindow(handle); dialog.Notes.Focus(); Pump(80);
                        if (Native.GetForegroundWindow() != handle) throw new Exception("Escape probe did not target its own announcement window.");
                        TypeKey(0x1b);
                        Check(!dialog.IsVisible, "The real Escape key closes the announcement without exiting Snapline");
                    });
                DialogAction(controller, delegate(SettingsWindow settings) {
                    settings.ShowPage("About");
                    AnnouncementAction(delegate { settings.Part<Button>("ViewAnnouncement").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); },
                        delegate(AnnouncementWindow dialog) {
                            Check(dialog.Owner == settings, "The real About button opens the announcement over its settings owner");
                            if (!keyboard) { dialog.Dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return; }
                            IntPtr handle = new WindowInteropHelper(dialog).Handle; SetForegroundWindow(handle); dialog.Notes.Focus(); Pump(80);
                            if (Native.GetForegroundWindow() != handle) throw new Exception("Enter probe did not target its own announcement window.");
                            TypeKey(0x0d);
                            Check(!dialog.IsVisible, "The real Enter key dismisses the announcement while its settings window remains open");
                        });
                    Check(controller.Busy && settings.IsVisible, "Closing the announcement keeps its parent settings dialog active and shelf input suspended");
                });
                controller.Busy = false;
                controller.Store.Settings.AnnouncementSeenVersion = "1.4.1"; controller.Store.Save();
                Check(AnnouncementWindow.Due(new ImageStore(data).Settings), "A previously read older version still requires the new version announcement");
                using (var locked = new FileStream(settingsPath + ".tmp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    AnnouncementAction(delegate { controller.ShowAnnouncement(true); }, delegate(AnnouncementWindow dialog) { });
                Check(controller.Store.Settings.AnnouncementSeenVersion == "1.4.1" && new ImageStore(data).Settings.AnnouncementSeenVersion == "1.4.1",
                    "A failed history write restores the old record so the announcement can appear on the next launch");
                Check(handler.Calls == 0, "Automatic and manual local announcements require no update network requests");
            }
        }

        private static void PressKey(int key, bool up = false)
        { keybd_event((byte)key, 0, up ? 2u : 0u, UIntPtr.Zero); Pump(35); }

        private static void TypeKey(int key) { PressKey(key); PressKey(key, true); }

        private static void TypeShortcut(uint key, uint modifiers)
        {
            int[] keys = new[] { 0x11, 0x12, 0x10, 0x5b }; uint[] flags = new[] { 2u, 1u, 4u, 8u };
            for (int i = 0; i < keys.Length; i++) if ((modifiers & flags[i]) != 0) PressKey(keys[i]);
            try { TypeKey((int)key); }
            finally { for (int i = keys.Length - 1; i >= 0; i--) if ((modifiers & flags[i]) != 0) PressKey(keys[i], true); }
        }

        private static void PointAt(Point point)
        { SetCursorPos((int)Math.Round(point.X), (int)Math.Round(point.Y)); Pump(80); }

        private static ShotCard HoverCard(Controller controller)
        {
            bool busy = controller.Busy; controller.Busy = true;
            controller.Line.Reveal(Forms.Screen.FromPoint(Native.Cursor())); Pump(1400);
            var card = ((Canvas)controller.Line.Content).Children.OfType<ShotCard>().First();
            var photo = Descendants(card).OfType<Image>().Single(image => image.Source == card.Shot.Thumbnail);
            PointAt(photo.PointToScreen(new Point(photo.ActualWidth / 2, photo.ActualHeight / 2))); Pump(200);
            controller.Busy = busy;
            return card;
        }

        private static void CheckHoverAndCapture(string root, string output)
        {
            var cursorBefore = Native.Cursor();
            int edits = 0; string editor = null, editedFile = null;
            string featureRoot = Path.Combine(root, "hover-and-capture");
            var preferences = new ImageStore(featureRoot);
            preferences.Settings.ListenClipboard = false; preferences.Settings.CollectionPaused = true;
            preferences.Settings.AutoCheckUpdates = false; preferences.Settings.HotkeyKey = FreeHotkey(7); preferences.Settings.HotkeyModifiers = 7;
            preferences.Settings.CaptureHotkeyKey = FreeHotkey(6); preferences.Settings.CaptureHotkeyModifiers = 6;
            preferences.Save();
            try
            {
                using (var controller = new Controller(featureRoot, false, new Updates(new UpdateHandler { Json = Encoding.UTF8.GetBytes("[]") }),
                    delegate(string path, string arguments) { edits++; editor = path; editedFile = arguments; }))
                using (var host = new Forms.Form { Text = "Snapline QA keyboard target", Bounds = new System.Drawing.Rectangle(100, 280, 500, 250), StartPosition = Forms.FormStartPosition.Manual, AutoScaleMode = Forms.AutoScaleMode.None })
                using (var text = new Forms.TextBox { Dock = Forms.DockStyle.Fill, ImeMode = Forms.ImeMode.Disable })
                {
                    host.Controls.Add(text); host.Show(); host.Activate(); text.Focus(); Pump(100);
                    var textPoint = text.PointToScreen(new System.Drawing.Point(30, 30));
                    var keyboardPoint = new Point(textPoint.X, textPoint.Y);
                    PointAt(keyboardPoint); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); Pump(80);
                    SetForegroundWindow(host.Handle); text.Focus(); Pump(80);
                    if (Native.GetForegroundWindow() != host.Handle) Console.WriteLine("Keyboard probe host=" + host.Handle + " foreground=" + Native.GetForegroundWindow() + " bounds=" + host.Bounds + " cursor=" + Native.Cursor());
                    Check(Native.GetForegroundWindow() == host.Handle, "Keyboard passthrough probes target only their own generated test window");
                    Check(controller.Hover.Registered, "A real native keyboard hook supports hover actions without activating the screenshot shelf");
                    string firstPath = Path.Combine(root, "悬停 中文 图片.png"), secondPath = Path.Combine(root, "hover-second.png");
                    File.WriteAllBytes(firstPath, ImageStore.Png(Fixture(420, 280, 8)));
                    File.WriteAllBytes(secondPath, ImageStore.Png(Fixture(500, 310, 9)));
                    var first = controller.Store.AddFile(firstPath, true); controller.Store.AddFile(secondPath, true);
                    controller.Busy = false;
                    HoverCard(controller);
                    IntPtr foreground = Native.GetForegroundWindow();
                    TypeKey(0x43);
                    var copied = BitmapFrame.Create((Stream)Clipboard.GetData("PNG"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    Check(ImageStore.Digest(copied) == first.Hash && text.Text == "" && Native.GetForegroundWindow() == foreground,
                        "Native C copies the hovered image rather than the latest image, consumes its key and preserves foreground focus");
                    PressKey(0x11); TypeKey(0x41); PressKey(0x11, true);
                    Check(edits == 0, "Ctrl combinations over a screenshot remain available to the foreground application");
                    controller.Busy = true; TypeKey(0x41);
                    Check(edits == 0 && text.Text.ToLowerInvariant().Contains("a"), "Hover edit stays disabled during another operation instead of taking keyboard input");
                    controller.Busy = false; text.Clear();
                    PointAt(keyboardPoint); PressKey(0x43);
                    uint unchanged = Native.GetClipboardSequenceNumber();
                    HoverCard(controller); PressKey(0x43); PressKey(0x43, true);
                    Check(Native.GetClipboardSequenceNumber() == unchanged && text.Text.Length >= 2,
                        "Holding a key outside the shelf then moving onto a photo never starts an action from key repeat");
                    HoverCard(controller); PressKey(0x44); PressKey(0x44); PressKey(0x44, true);
                    Check(controller.Store.Items.Count == 1 && File.Exists(firstPath) && File.Exists(secondPath),
                        "D takes down only the hovered external photo and key repeat cannot remove its neighbor");
                    HoverCard(controller); TypeKey(0x2e);
                    Check(controller.Store.Items.Count == 0 && File.Exists(secondPath), "Delete takes down an external screenshot while retaining its original file");
                    first = controller.Store.AddFile(firstPath, true); HoverCard(controller); TypeKey(0x41);
                    Check(edits == 1 && Path.IsPathRooted(editor) && Path.GetFileName(editor).Equals("mspaint.exe", StringComparison.OrdinalIgnoreCase) &&
                        editedFile == "\"" + firstPath + "\"" && !controller.Line.Revealed,
                        "Native A tucks the shelf and selects Paint with the exact quoted Unicode image path");
                    Check(File.Exists(editor), "Editing resolves an existing Paint entry point, including WindowsApps when System32 Paint is absent");
                    TypeKey(0x41);
                    Check(edits == 1, "A is ordinary keyboard input again after the shelf is hidden");
                    var card = HoverCard(controller);
                    var photo = Descendants(card).OfType<Image>().Single(image => image.Source == card.Shot.Thumbnail);
                    PointAt(photo.PointToScreen(new Point(photo.ActualWidth - 2, photo.ActualHeight - 2))); Pump(200);
                    var pointer = Native.Cursor();
                    Check(controller.Line.ShotAt(new Point(pointer.X, pointer.Y)) == card,
                        "Long-press regression targets the actual photo rather than a transparent glass corner");
                    int before = edits;
                    mouse_event(2, 0, 0, 0, UIntPtr.Zero);
                    var releaseMouse = Task.Run(delegate { Thread.Sleep(700); mouse_event(4, 0, 0, 0, UIntPtr.Zero); });
                    Pump(1000); releaseMouse.GetAwaiter().GetResult();
                    Check(edits == before + 1, "A stationary long press near the photo edge opens the editor despite the shrinking animation");

                    controller.Busy = true;
                    Check(!controller.Store.Settings.IncludeCursor, "Existing settings retain a default-off cursor option and the original collection preferences");
                    DialogAction(controller, delegate(SettingsWindow dialog) {
                        dialog.ShowPage("Capture"); dialog.Part<CheckBox>("CursorToggle").IsChecked = true;
                        dialog.Part<Button>("CancelSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    });
                    Check(!controller.Store.Settings.IncludeCursor && !new ImageStore(featureRoot).Settings.IncludeCursor,
                        "Canceling the real cursor settings page preserves its saved value");
                    DialogAction(controller, delegate(SettingsWindow dialog) {
                        dialog.ShowPage("Capture"); dialog.Part<CheckBox>("CursorToggle").IsChecked = true;
                        dialog.Part<Button>("SaveSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    });
                    Check(controller.Store.Settings.IncludeCursor && new ImageStore(featureRoot).Settings.IncludeCursor,
                        "The real settings Save persists cursor inclusion across a reload");
                    string error;
                    using (var locked = new FileStream(Path.Combine(featureRoot, "settings.json.tmp"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                        Check(!controller.ApplyPreferences(false, true, false, false, controller.Store.Settings.WatchFolder,
                            controller.Sink.HotkeyKey, controller.Sink.HotkeyModifiers, out error) && controller.Store.Settings.IncludeCursor,
                            "Settings write failure restores the previously saved cursor preference");
                    controller.Line.Reveal(Forms.Screen.FromPoint(Native.Cursor())); Pump(400);
                    Check(controller.Line.CaptureButton.IsEnabled && Hit(new WindowInteropHelper(controller.Line).Handle,
                        controller.Line.CaptureButton.PointToScreen(new Point(10, 10))) == 1,
                        "The new screenshot toolbar command is visible and accepts native mouse input");
                    controller.Line.Tuck(); Pump(300);
                    CheckRegionPicker(controller, output);
                }
            }
            finally {
                foreach (int key in new[] { 0x41, 0x43, 0x44, 0x2e, 0x11 }) keybd_event((byte)key, 0, 2, UIntPtr.Zero);
                mouse_event(4, 0, 0, 0, UIntPtr.Zero); SetCursorPos(cursorBefore.X, cursorBefore.Y);
            }
        }

        private static int CheckPaint(string output)
        {
            Directory.CreateDirectory(output);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            string root = Path.Combine(output, "data");
            string stem = "Snapline 编辑验证 " + Guid.NewGuid().ToString("N").Substring(0, 8);
            string path = Path.Combine(output, stem + ".png");
            var before = Process.GetProcessesByName("mspaint").Select(process => process.Id).ToArray();
            Process paint = null;
            try
            {
                var settings = new ImageStore(root);
                settings.Settings.ListenClipboard = false; settings.Settings.CollectionPaused = true;
                settings.Settings.AutoCheckUpdates = false; settings.Settings.HotkeyKey = FreeHotkey(7); settings.Settings.HotkeyModifiers = 7; settings.Save();
                byte[] png = ImageStore.Png(Fixture(640, 360, 8)); File.WriteAllBytes(path, png);
                using (var controller = new Controller(root, false, new Updates(new UpdateHandler { Json = Encoding.UTF8.GetBytes("[]") })))
                {
                    controller.Busy = true;
                    var shot = controller.Store.AddFile(path, true);
                    Check(File.Exists(Controller.PaintPath()), "Real editing uses an existing installed Paint entry point");
                    var card = new ShotCard(shot, controller, null);
                    card.ContextMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "用画图编辑").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    var deadline = DateTime.UtcNow.AddSeconds(15);
                    while (paint == null && DateTime.UtcNow < deadline)
                    {
                        Pump(200);
                        paint = Process.GetProcessesByName("mspaint").FirstOrDefault(process => !before.Contains(process.Id) &&
                            process.MainWindowHandle != IntPtr.Zero && process.MainWindowTitle.StartsWith(stem, StringComparison.OrdinalIgnoreCase));
                    }
                    Check(paint != null, "The actual Edit menu event opens a new Paint window with the generated Unicode image filename");
                    Pump(900); paint.Refresh();
                    Native.RECT rectangle;
                    if (GetWindowRect(paint.MainWindowHandle, out rectangle))
                    {
                        using (var image = new System.Drawing.Bitmap(rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                        using (var graphics = System.Drawing.Graphics.FromImage(image))
                        {
                            IntPtr dc = graphics.GetHdc(); bool rendered;
                            try { rendered = PrintWindow(paint.MainWindowHandle, dc, 2); }
                            finally { graphics.ReleaseHdc(dc); }
                            if (rendered) image.Save(Path.Combine(output, "paint-window.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    Check(File.ReadAllBytes(path).SequenceEqual(png), "Opening the real Paint editor preserves the source image bytes");
                }
                paint.Refresh();
                Check(paint.MainWindowTitle.StartsWith(stem, StringComparison.OrdinalIgnoreCase) && paint.CloseMainWindow() && paint.WaitForExit(4000),
                    "Only the new Paint process showing the generated test image is closed cleanly");
                File.WriteAllLines(Path.Combine(output, "editor-results.txt"), checks);
                Console.WriteLine("PASS " + checks.Count + " real editor checks. Output: " + output); return 0;
            }
            catch (Exception ex) {
                Console.Error.WriteLine(ex); File.WriteAllLines(Path.Combine(output, "editor-results.txt"), checks.Concat(new[] { "FAIL " + ex.ToString() })); return 1;
            }
            finally {
                if (paint != null) {
                    paint.Refresh();
                    if (!paint.HasExited && !before.Contains(paint.Id) && paint.MainWindowTitle.StartsWith(stem, StringComparison.OrdinalIgnoreCase)) paint.CloseMainWindow();
                    paint.Dispose();
                }
                app.Shutdown();
            }
        }

        private static Forms.DialogResult PickerAction(RegionCapture picker, Action action)
        {
            Exception failure = null;
            using (var timer = new Forms.Timer { Interval = 100 })
            {
                timer.Tick += delegate {
                    timer.Stop();
                    try { action(); }
                    catch (Exception ex) { failure = ex; }
                    finally { if (!picker.IsDisposed && picker.Visible) picker.Close(); }
                };
                timer.Start();
                var result = picker.ShowDialog();
                if (failure != null) throw failure;
                return result;
            }
        }

        private static void SelectRegion(RegionCapture picker, int x1, int y1, int x2, int y2)
        {
            var start = picker.PointToScreen(new System.Drawing.Point(x1, y1));
            var end = picker.PointToScreen(new System.Drawing.Point(x2, y2));
            SetCursorPos(start.X, start.Y); Pump(50); mouse_event(2, 0, 0, 0, UIntPtr.Zero); Pump(50);
            SetCursorPos(end.X, end.Y); Pump(50); mouse_event(4, 0, 0, 0, UIntPtr.Zero); Pump(50);
        }

        private static void CheckRegionPicker(Controller controller, string output)
        {
            var bounds = new System.Drawing.Rectangle(120, 280, 480, 280);
            using (var desktop = new System.Drawing.Bitmap(bounds.Width, bounds.Height))
            {
                using (var graphics = System.Drawing.Graphics.FromImage(desktop)) {
                    graphics.Clear(System.Drawing.Color.FromArgb(36, 112, 198));
                    graphics.FillRectangle(System.Drawing.Brushes.Orange, 20, 80, 70, 100);
                }
                BitmapSource plain;
                using (var picker = new RegionCapture(desktop, bounds, false))
                {
                    var result = PickerAction(picker, delegate { SelectRegion(picker, 40, 70, 260, 230); });
                    plain = picker.Result;
                    Check(result == Forms.DialogResult.OK && plain != null && plain.PixelWidth == 220 && plain.PixelHeight == 160,
                        "Actual mouse drag with cursor inclusion off immediately captures the selected pixel dimensions");
                    var expected = ScreenCapture.Crop(desktop, bounds, picker.Selection, false);
                    Check(ImageStore.Digest(plain) == ImageStore.Digest(expected), "Region output retains original pixels without the picker shade or selection border");
                    File.WriteAllBytes(Path.Combine(output, "capture-without-cursor.png"), ImageStore.Png(plain));
                }
                using (var picker = new RegionCapture(desktop, bounds, true))
                {
                    var result = PickerAction(picker, delegate {
                        SelectRegion(picker, 260, 230, 40, 70);
                        Check(picker.Visible && picker.Result == null && picker.Selection == new System.Drawing.Rectangle(40, 70, 220, 160),
                            "Reverse-direction selection normalizes bounds and waits for cursor placement when enabled");
                        SetCursorPos(bounds.Left + 120, bounds.Top + 135); Pump(50);
                        using (var image = new System.Drawing.Bitmap(picker.Width, picker.Height)) {
                            picker.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, image.Width, image.Height));
                            image.Save(Path.Combine(output, "region-picker.png"));
                        }
                        TypeKey(0x0d);
                    });
                    Check(result == Forms.DialogResult.OK && picker.Result != null && ImageStore.Digest(picker.Result) != ImageStore.Digest(plain),
                        "Real Enter confirmation overlays the actual Windows mouse cursor onto the frozen selected image");
                    File.WriteAllBytes(Path.Combine(output, "capture-with-cursor.png"), ImageStore.Png(picker.Result));
                    int count = controller.Store.Items.Count;
                    controller.AcceptCapture(picker.Result);
                    Check(controller.Store.Items.Count == count + 1 && controller.Store.Settings.CollectionPaused &&
                        !controller.Store.Settings.ListenClipboard && Clipboard.ContainsImage(),
                        "Explicit region capture saves and copies an image even when automatic collection is paused and clipboard capture is off");
                    Check(ImageStore.Digest(ImageStore.Load(controller.Store.Items.Last().Path, 0)) == ImageStore.Digest(picker.Result) &&
                        ImageStore.Digest(BitmapFrame.Create((Stream)Clipboard.GetData("PNG"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad)) == ImageStore.Digest(picker.Result),
                        "Stored PNG and copied PNG both preserve the captured cursor and exact screenshot pixels");
                    controller.AcceptCapture(picker.Result);
                    Check(controller.Store.Items.Count == count + 1, "Repeating an identical manual capture copies it without adding a duplicate card");
                }
                controller.Line.Tuck(); Pump(300);
                using (var picker = new RegionCapture(desktop, bounds, true))
                {
                    var result = PickerAction(picker, delegate { SelectRegion(picker, 40, 70, 40, 70); Check(picker.Result == null && picker.Selection.IsEmpty, "A click without a valid selection does not create a screenshot"); TypeKey(0x1b); });
                    Check(result == Forms.DialogResult.Cancel && picker.Result == null, "Real Escape cancels the region picker without returning or saving an image");
                }
                var selected = new System.Drawing.Rectangle(40, 70, 220, 160);
                SetCursorPos(bounds.Right + 100, bounds.Bottom + 100); Pump(40);
                Check(ImageStore.Digest(ScreenCapture.Crop(desktop, bounds, selected, true)) == ImageStore.Digest(plain),
                    "A cursor outside the selected area leaves screenshot pixels unchanged");
                var translated = new System.Drawing.Rectangle(bounds.Left - 800, bounds.Top - 600, bounds.Width, bounds.Height);
                Check(ImageStore.Digest(ScreenCapture.Crop(desktop, translated, selected, false)) == ImageStore.Digest(plain),
                    "Region cropping preserves pixel dimensions for virtual desktops with negative coordinates");
            }
            using (var host = new Forms.Form { Bounds = bounds, StartPosition = Forms.FormStartPosition.Manual, AutoScaleMode = Forms.AutoScaleMode.None, FormBorderStyle = Forms.FormBorderStyle.None, BackColor = System.Drawing.Color.FromArgb(35, 100, 170), TopMost = true })
            {
                host.Show(); Pump(150);
                using (var live = ScreenCapture.Read(new System.Drawing.Rectangle(bounds.Left + 30, bounds.Top + 90, 40, 30)))
                    Check(live.GetPixel(20, 15).ToArgb() == host.BackColor.ToArgb(), "Live GDI capture reads actual generated on-screen content at its physical location");
                controller.Busy = true; controller.Line.Reveal(Forms.Screen.FromPoint(Native.Cursor())); Pump(350);
                controller.Store.Settings.IncludeCursor = false; controller.Busy = false;
                int count = controller.Store.Items.Count;
                bool selected = false; Exception failure = null;
                using (var timer = new Forms.Timer { Interval = 75 })
                {
                    timer.Tick += delegate {
                        var picker = Forms.Application.OpenForms.OfType<RegionCapture>().FirstOrDefault();
                        if (picker == null) return;
                        timer.Stop();
                        try {
                            Check(!controller.Line.IsVisible, "The actual screenshot command waits for the shelf to disappear before sampling the desktop");
                            var start = picker.PointToClient(new System.Drawing.Point(bounds.Left + 100, bounds.Top + 100));
                            var end = picker.PointToClient(new System.Drawing.Point(bounds.Left + 300, bounds.Top + 220));
                            SelectRegion(picker, start.X, start.Y, end.X, end.Y); selected = true;
                        }
                        catch (Exception ex) { failure = ex; if (!picker.IsDisposed) picker.Close(); }
                    };
                    timer.Start();
                    controller.Line.CaptureButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(1800);
                    if (failure != null) throw failure;
                }
                var captured = ImageStore.Load(controller.Store.Items.Last().Path, 0);
                Check(selected && controller.Store.Items.Count == count + 1 && captured.PixelWidth == 200 && captured.PixelHeight == 120 && !controller.Busy,
                    "The real toolbar event completes the asynchronous capture, save and copy pipeline while automatic collection stays paused");
                var pixel = new byte[4];
                new FormatConvertedBitmap(captured, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(100, 60, 1, 1), pixel, 4, 0);
                Check(pixel.SequenceEqual(new byte[] { 170, 100, 35, 255 }) &&
                    ImageStore.Digest(BitmapFrame.Create((Stream)Clipboard.GetData("PNG"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad)) == ImageStore.Digest(captured),
                    "The complete screenshot command retains the generated window pixels and the same lossless PNG on the clipboard");
                string error;
                uint customCapture = FreeHotkey(6);
                Check(controller.ChangeHotkeys(controller.Sink.HotkeyKey, controller.Sink.HotkeyModifiers, customCapture, 6, out error),
                    "The live capture command accepts a separate customized global shortcut");
                controller.Busy = true;
                TypeShortcut(customCapture, 6); Pump(400);
                Check(!Forms.Application.OpenForms.OfType<RegionCapture>().Any(), "The capture shortcut respects the existing busy-dialog guard");
                controller.Busy = false; count = controller.Store.Items.Count;
                CaptureShortcutAction(controller, delegate(RegionCapture picker) {
                    Check(!controller.Line.IsVisible, "Global capture hides the shelf before sampling the desktop");
                    TypeShortcut(customCapture, 6);
                    Check(Forms.Application.OpenForms.OfType<RegionCapture>().Count() == 1, "Pressing the capture shortcut during selection does not open another picker");
                    var start = picker.PointToClient(new System.Drawing.Point(bounds.Left + 100, bounds.Top + 100));
                    var end = picker.PointToClient(new System.Drawing.Point(bounds.Left + 320, bounds.Top + 230));
                    SelectRegion(picker, start.X, start.Y, end.X, end.Y);
                });
                captured = ImageStore.Load(controller.Store.Items.Last().Path, 0);
                Check(controller.Store.Items.Count == count + 1 && captured.PixelWidth == 220 && captured.PixelHeight == 130 && !controller.Busy &&
                    controller.Store.Settings.CollectionPaused && !controller.Store.Settings.ListenClipboard,
                    "A real custom global shortcut completes region capture while automatic collection remains paused");
                Check(ImageStore.Digest(BitmapFrame.Create((Stream)Clipboard.GetData("PNG"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad)) == ImageStore.Digest(captured),
                    "The global capture shortcut saves and copies the exact same lossless PNG pixels");
                count = controller.Store.Items.Count;
                CaptureShortcutAction(controller, delegate(RegionCapture picker) { TypeKey(0x1b); });
                Check(controller.Store.Items.Count == count && !controller.Busy && !Forms.Application.OpenForms.OfType<RegionCapture>().Any(),
                    "Escape cancels a globally triggered screenshot without saving an image or leaving the app busy");
                host.Close();
            }
        }

        private static void CaptureShortcutAction(Controller controller, Action<RegionCapture> action)
        {
            bool opened = false; Exception failure = null;
            using (var timer = new Forms.Timer { Interval = 75 }) {
                timer.Tick += delegate {
                    var picker = Forms.Application.OpenForms.OfType<RegionCapture>().FirstOrDefault();
                    if (picker == null) return;
                    timer.Stop(); opened = true;
                    try { action(picker); }
                    catch (Exception ex) { failure = ex; }
                    finally { if (!picker.IsDisposed && picker.Visible) picker.Close(); }
                };
                timer.Start(); TypeShortcut(controller.Sink.CaptureHotkeyKey, controller.Sink.CaptureHotkeyModifiers); Pump(1800);
                if (failure != null) throw failure;
                Check(opened, "A physical global shortcut reaches the region picker through RegisterHotKey");
            }
        }

        private static void CheckInstallerUpdates(string root, string output)
        {
            string registered = Path.Combine(root, "安装 目录");
            Check(Updates.InstalledAt(Path.Combine(registered, "Snapline.exe"), registered.ToUpperInvariant()),
                "Installed update mode matches the registered executable path including Unicode and case differences");
            Check(!Updates.InstalledAt(Path.Combine(registered + "-portable", "Snapline.exe"), registered),
                "A separate portable copy stays in ZIP update mode even when another copy is installed");
            Check(!Updates.InstalledAt(Path.Combine(registered, "Snapline.exe"), null) &&
                !Updates.InstalledAt(Path.Combine(registered, "Snapline.exe"), "relative-directory"), "Missing or relative installation registration does not enable EXE update mode");
            var package = Encoding.UTF8.GetBytes("MZ generated installer fixture; never executed");
            string both = "[" + ReleaseJson("v1.5.0", package, false, true, true) + "]";
            var json = Encoding.UTF8.GetBytes(both);
            var release = Updates.Parse(json, Updates.CurrentVersion, true);
            Check(release != null && release.IsInstaller && release.Package.Name == "Snapline-Windows-Setup-v1.5.0.exe",
                "Installed clients select the EXE installer from a feed containing both update formats");
            Check(Updates.Parse(json, new Version(1, 3, 2)).Package.Name == "Snapline-Windows-v1.5.0.zip",
                "Portable and legacy ZIP clients retain a compatible package in the same update feed");
            Check(Updates.Parse(Encoding.UTF8.GetBytes("[" + ReleaseJson("v1.5.0", package, false, true) + "]"), Updates.CurrentVersion, true) == null,
                "Installed clients never silently substitute a ZIP when the verified installer is missing");
            Check(Updates.Parse(Encoding.UTF8.GetBytes(both.Replace("/Snapline-Windows-Setup-v1.5.0.exe", "/other.exe")), Updates.CurrentVersion, true) == null,
                "Installer download URLs must match the exact release asset in the publishing repository");
            Check(Updates.Parse(Encoding.UTF8.GetBytes(both.Replace("sha256:", "sha1:")), Updates.CurrentVersion, true) == null,
                "Installer assets without a complete SHA-256 digest are rejected");
            var handler = new UpdateHandler { Json = json, Package = package };
            using (var updater = new Updates(handler, true))
            {
                var found = updater.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();
                Check(found.IsInstaller && handler.Headers, "Actual installed-mode HTTP checks select an EXE without sending an account token");
                string destination = Path.Combine(root, "新版 安装包.exe");
                int started = 0; string opened = null;
                using (var dialog = new UpdateDialog(found,
                    delegate(string path, IProgress<int> progress, CancellationToken cancel) { return updater.DownloadAsync(found, path, progress, cancel); },
                    delegate(string path) { opened = path; }, delegate(string path) { if (path == destination) started++; }))
                {
                    using (var picker = dialog.SaveDialog())
                        Check(picker.DefaultExt == "exe" && picker.Filter == "EXE 安装包|*.exe" && picker.FileName == found.Package.Name,
                            "Installed update Save As uses the EXE filename and extension instead of ZIP");
                    dialog.Show(); Pump(50);
                    var pending = dialog.DownloadTo(destination); WaitUI(pending);
                    var button = dialog.Controls.OfType<Forms.Button>().Single(item => item.Text == "安装更新");
                    Check(File.ReadAllBytes(destination).SequenceEqual(package) && button.Enabled && opened == destination && started == 0,
                        "Verified installer download enables Install Update and waits for an explicit click");
                    using (var image = new System.Drawing.Bitmap(dialog.Width, dialog.Height))
                    {
                        dialog.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, image.Width, image.Height));
                        image.Save(Path.Combine(output, "installer-update-dialog.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    button.PerformClick();
                    Check(started == 1 && !button.Enabled, "Install Update launches the verified file exactly once through the real button event");
                    dialog.Close();
                }
                started = 0;
                using (var dialog = new UpdateDialog(found,
                    delegate(string path, IProgress<int> progress, CancellationToken cancel) { return updater.DownloadAsync(found, path, progress, cancel); },
                    delegate { }, delegate { started++; }))
                {
                    dialog.Show(); Pump(50); WaitUI(dialog.DownloadTo(destination));
                    File.WriteAllBytes(destination, Enumerable.Repeat((byte)42, package.Length).ToArray());
                    dialog.Controls.OfType<Forms.Button>().Single(item => item.Text == "安装更新").PerformClick();
                    Check(started == 0 && dialog.Controls.OfType<Forms.Button>().Any(item => item.Text == "重新下载…" && item.Enabled),
                        "A changed installer file is rechecked and cannot run after download verification");
                    WaitUI(dialog.DownloadTo(destination));
                    Check(File.ReadAllBytes(destination).SequenceEqual(package) && started == 0,
                        "A rejected installer can be downloaded and verified again without launching automatically");
                    dialog.Close();
                }
                handler.Package = Enumerable.Repeat((byte)42, package.Length).ToArray(); started = 0;
                using (var dialog = new UpdateDialog(found,
                    delegate(string path, IProgress<int> progress, CancellationToken cancel) { return updater.DownloadAsync(found, path, progress, cancel); },
                    delegate { }, delegate { started++; }))
                {
                    dialog.Show(); Pump(50); WaitUI(dialog.DownloadTo(destination));
                    Check(started == 0 && !dialog.Controls.OfType<Forms.Button>().Any(item => item.Text == "安装更新") && File.ReadAllBytes(destination).SequenceEqual(package),
                        "A failed EXE checksum never enables installation or replaces a previous verified download");
                    dialog.Close();
                }
                started = 0;
                using (var dialog = new UpdateDialog(found,
                    delegate(string path, IProgress<int> progress, CancellationToken cancel) { return Task.Delay(3000, cancel); },
                    delegate { }, delegate { started++; }))
                {
                    dialog.Show(); Pump(50); var pending = dialog.DownloadTo(destination); dialog.Close(); WaitUI(pending);
                    Check(started == 0 && !dialog.IsDisposed && dialog.Controls.OfType<Forms.Label>().Any(item => item.Text.Contains("已取消")),
                        "Closing during an installer download cancels it and never launches the installer");
                    dialog.Close();
                }
            }
            using (var portable = new UpdateDialog(Updates.Parse(json, Updates.CurrentVersion), delegate { return Task.FromResult(0); }, delegate { }, delegate { }))
            using (var picker = portable.SaveDialog())
                Check(picker.DefaultExt == "zip" && picker.Filter == "ZIP 更新包|*.zip", "Portable updates retain their original ZIP Save As format");
        }

        private static void WaitUI(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) Pump(20);
            if (!task.IsCompleted) throw new TimeoutException("Update dialog did not complete the test transfer.");
            task.GetAwaiter().GetResult();
        }

        private static int CheckNetwork(string output)
        {
            Directory.CreateDirectory(output);
            try
            {
                Release release; byte[] feed;
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 1024 * 1024 })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("Snapline-QA/" + Updates.CurrentVersion);
                    feed = http.GetByteArrayAsync(Updates.Feed).GetAwaiter().GetResult();
                    release = Updates.Parse(feed, new Version(0, 0, 0));
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
                var installerRelease = Updates.Parse(feed, new Version(0, 0, 0), true);
                Check(installerRelease != null && installerRelease.IsInstaller, "Anonymous public update feed also contains a verified EXE installer");
                using (var updater = new Updates(new HttpClientHandler(), true))
                {
                    var available = updater.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();
                    Check(installerRelease.Version > Updates.CurrentVersion ? available != null && available.IsInstaller && available.Tag == installerRelease.Tag : available == null,
                        "Installed-mode native HTTP client compares the release version and selects EXE updates");
                    string path = Path.Combine(output, installerRelease.Package.Name);
                    updater.DownloadAsync(installerRelease, path, null, CancellationToken.None).GetAwaiter().GetResult();
                    Check(File.Exists(path) && new FileInfo(path).Length == installerRelease.Package.Size && Directory.GetFiles(output, "*.download").Length == 0,
                        "Installed-mode native updater downloads and SHA-256-verifies the public EXE without running it");
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
            Check(legacy.HotkeyKey == Hotkey.DefaultKey && legacy.HotkeyModifiers == Hotkey.DefaultModifiers && !legacy.ListenClipboard &&
                legacy.CaptureHotkeyKey == Hotkey.DefaultCaptureKey && legacy.CaptureHotkeyModifiers == Hotkey.DefaultModifiers,
                "Older settings gain both default shortcuts without changing clipboard preferences");
            string legacyCustomRoot = Path.Combine(root, "legacy-custom-settings");
            Directory.CreateDirectory(legacyCustomRoot);
            File.WriteAllText(Path.Combine(legacyCustomRoot, "settings.json"), "{\"HotkeyKey\":83,\"HotkeyModifiers\":3,\"CollectionPaused\":true,\"IncludeCursor\":true,\"Paths\":[]}");
            legacy = new ImageStore(legacyCustomRoot).Settings;
            Check(legacy.HotkeyKey == 83 && legacy.HotkeyModifiers == 3 && legacy.CaptureHotkeyKey == Hotkey.DefaultCaptureKey &&
                legacy.CollectionPaused && legacy.IncludeCursor, "Migrating a legacy custom display shortcut retains it and existing capture preferences");
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
            DialogAction(controller, delegate(SettingsWindow dialog) {
                var input = dialog.Part<TextBox>("HotkeyInput");
                input.Focus();
                bool revealed = controller.Line.Revealed;
                SendMessage(controller.Sink.Handle, Native.WM_HOTKEY, new IntPtr(controller.Sink.HotkeyId), IntPtr.Zero);
                Check(input.Text == controller.Sink.HotkeyText && controller.Line.Revealed == revealed,
                    "The existing global shortcut can be recorded without toggling the overlay");
                input.Focus();
                SendMessage(new WindowInteropHelper(dialog).Handle, 0x0100, new IntPtr(single), IntPtr.Zero);
                Check(input.Text == Hotkey.Text(single, 0) && dialog.Part<Button>("SaveSettings").IsEnabled,
                    "The native shortcut input records a single key and enables Save");
                SaveSettingsRender(dialog, Path.Combine(output, "hotkey-dialog.png"));
                dialog.Part<Button>("SaveSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            Check(controller.Sink.HotkeyKey == single && controller.Sink.HotkeyModifiers == 0 &&
                new ImageStore(controller.Store.Root).Settings.HotkeyKey == single,
                "Saving from the actual dialog applies and persists a single-key binding");
            var tray = (Forms.NotifyIcon)typeof(Controller).GetField("tray", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller);
            Check(tray.ContextMenuStrip.Items.OfType<Forms.ToolStripMenuItem>().First().Text.Contains(Hotkey.Text(single, 0)),
                "The tray menu updates to display the custom shortcut");
            DialogAction(controller, delegate(SettingsWindow dialog) {
                dialog.Part<TextBox>("HotkeyInput").Focus();
                SendMessage(new WindowInteropHelper(dialog).Handle, 0x0100, new IntPtr(FreeHotkey(0)), IntPtr.Zero);
                dialog.Part<Button>("CancelSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            Check(controller.Sink.HotkeyKey == single && new ImageStore(controller.Store.Root).Settings.HotkeyKey == single,
                "Canceling a recorded shortcut preserves the active and saved binding");
            DialogAction(controller, delegate(SettingsWindow dialog) {
                dialog.Part<Button>("RestoreKeys").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(dialog.Part<TextBox>("HotkeyInput").Text == Hotkey.Text(Hotkey.DefaultKey, Hotkey.DefaultModifiers),
                    "Restore Default selects Ctrl + Alt + T in the dialog");
                dialog.Part<Button>("CancelSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
        }

        private static void CheckBindingRegistrations()
        {
            uint display = FreeHotkey(7), capture = FreeHotkey(6);
            string error;
            using (var sink = new MessageSink(display, 7, capture, 6)) {
                Check(sink.HotkeyRegistered && sink.CaptureHotkeyRegistered && sink.HotkeyId != sink.CaptureHotkeyId,
                    "Display and capture commands have separate active native registrations");
                int toggles = 0, captures = 0;
                sink.ToggleRequested += delegate { toggles++; }; sink.CaptureRequested += delegate { captures++; };
                SendMessage(sink.Handle, Native.WM_HOTKEY, new IntPtr(sink.HotkeyId), IntPtr.Zero);
                SendMessage(sink.Handle, Native.WM_HOTKEY, new IntPtr(sink.CaptureHotkeyId), IntPtr.Zero);
                Check(toggles == 1 && captures == 1, "Native hotkey messages dispatch to their corresponding commands");
                int displayId = sink.HotkeyId, captureId = sink.CaptureHotkeyId;
                Check(!sink.TrySetHotkeys(display, 7, display, 7, null, out error) && error.Contains("同一") &&
                    sink.HotkeyId == displayId && sink.CaptureHotkeyId == captureId, "Duplicate shortcuts preserve both active native registrations");
                Check(!sink.TrySetHotkeys(display, 7, 0x7b, 0, null, out error) && error.Contains("F12"),
                    "Reserved F12 is rejected for the capture shortcut");
                uint occupied = FreeHotkey(6), replacement = FreeHotkey(7);
                Check(Native.RegisterHotKey(IntPtr.Zero, 90, 0x4006, occupied), "Capture conflict fixture owns a real native hotkey");
                try {
                    Check(!sink.TrySetHotkeys(replacement, 7, occupied, 6, null, out error) && error.StartsWith("区域截图") &&
                        sink.HotkeyId == displayId && sink.CaptureHotkeyId == captureId,
                        "A capture conflict preserves both commands even when display also changes");
                    bool released = Native.RegisterHotKey(IntPtr.Zero, 91, 0x4007, replacement);
                    if (released) Native.UnregisterHotKey(IntPtr.Zero, 91);
                    Check(released, "A failed pair change releases its provisional display registration");
                }
                finally { Native.UnregisterHotKey(IntPtr.Zero, 90); }
                uint nextCapture = FreeHotkey(6);
                bool failed = false;
                try { sink.TrySetHotkeys(replacement, 7, nextCapture, 6, delegate { throw new IOException("Generated save failure"); }, out error); }
                catch (IOException) { failed = true; }
                Check(failed && sink.HotkeyId == displayId && sink.CaptureHotkeyId == captureId && sink.HotkeyRegistered && sink.CaptureHotkeyRegistered,
                    "A save exception keeps both original registrations without releasing and reacquiring them");
                bool displayReleased = Native.RegisterHotKey(IntPtr.Zero, 90, 0x4007, replacement);
                bool captureReleased = Native.RegisterHotKey(IntPtr.Zero, 91, 0x4006, nextCapture);
                if (displayReleased) Native.UnregisterHotKey(IntPtr.Zero, 90);
                if (captureReleased) Native.UnregisterHotKey(IntPtr.Zero, 91);
                Check(displayReleased && captureReleased, "Save rollback releases both provisional registrations");
                Check(sink.TrySetHotkeys(capture, 6, display, 7, null, out error) && sink.HotkeyId == captureId && sink.CaptureHotkeyId == displayId,
                    "Swapping the two shortcuts reuses their existing native registrations");
                SendMessage(sink.Handle, Native.WM_HOTKEY, new IntPtr(sink.HotkeyId), IntPtr.Zero);
                Check(toggles == 2 && captures == 1, "Swapped registrations dispatch to the new logical command");
            }
            bool displayFree = Native.RegisterHotKey(IntPtr.Zero, 90, 0x4007, display);
            bool captureFree = Native.RegisterHotKey(IntPtr.Zero, 91, 0x4006, capture);
            if (displayFree) Native.UnregisterHotKey(IntPtr.Zero, 90);
            if (captureFree) Native.UnregisterHotKey(IntPtr.Zero, 91);
            Check(displayFree && captureFree, "Disposing a swapped pair releases both native registrations");
            using (var legacy = new MessageSink(display, 7, display, 7)) {
                Check(legacy.HotkeyRegistered && !legacy.CaptureHotkeyRegistered && legacy.HotkeyKey == display && legacy.HotkeyModifiers == 7,
                    "A duplicate legacy capture default leaves the existing display shortcut intact");
                Check(legacy.TrySetHotkeys(display, 7, capture, 6, null, out error) && legacy.CaptureHotkeyRegistered,
                    "Choosing a different capture shortcut enables it after a startup conflict");
            }
        }

        private static void CheckCaptureHotkeys(Controller controller, string root, string output)
        {
            CheckBindingRegistrations();
            controller.Busy = true;
            uint display = controller.Sink.HotkeyKey, displayFlags = controller.Sink.HotkeyModifiers;
            uint capture = FreeHotkey(6);
            string error;
            Check(controller.ChangeHotkeys(display, displayFlags, capture, 6, out error) && controller.Sink.CaptureHotkeyRegistered,
                "A custom region capture shortcut applies while keeping the display shortcut");
            var saved = new ImageStore(controller.Store.Root).Settings;
            Check(saved.CaptureHotkeyKey == capture && saved.CaptureHotkeyModifiers == 6 && saved.HotkeyKey == display,
                "Both shortcuts are persisted independently to settings.json");
            int displayId = controller.Sink.HotkeyId, captureId = controller.Sink.CaptureHotkeyId;
            using (var locked = new FileStream(Path.Combine(controller.Store.Root, "settings.json.tmp"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                Check(!controller.ApplyPreferences(false, true, false, true, controller.Store.Settings.WatchFolder, FreeHotkey(7), 7, FreeHotkey(6), 6, out error) &&
                    controller.Sink.HotkeyId == displayId && controller.Sink.CaptureHotkeyId == captureId &&
                    controller.Store.Settings.CaptureHotkeyKey == capture && new ImageStore(controller.Store.Root).Settings.CaptureHotkeyKey == capture,
                    "An actual settings write failure leaves both native registrations and saved capture preference unchanged");
            DialogAction(controller, delegate(SettingsWindow dialog) {
                var input = dialog.Part<TextBox>("CaptureHotkeyInput"); input.Focus();
                SendMessage(controller.Sink.Handle, Native.WM_HOTKEY, new IntPtr(captureId), IntPtr.Zero);
                Check(input.Text == controller.Sink.CaptureHotkeyText && !Forms.Application.OpenForms.OfType<RegionCapture>().Any(),
                    "Recording the active capture shortcut fills its input without opening a picker");
                dialog.SetCaptureCandidate(display, displayFlags);
                Check(!dialog.Part<Button>("SaveSettings").IsEnabled && dialog.Part<TextBlock>("KeyHint").Text.Contains("同一"),
                    "Settings shows an inline duplicate error and disables Save");
                dialog.SetCaptureCandidate(capture, 6);
                dialog.SetCandidate(0x7b, 0); dialog.SetCaptureCandidate(FreeHotkey(6), 6);
                Check(!dialog.Part<Button>("SaveSettings").IsEnabled && dialog.Part<TextBlock>("KeyHint").Text.Contains("F12"),
                    "Editing capture does not clear an unresolved display validation error");
                dialog.SetCandidate(display, displayFlags); dialog.SetCaptureCandidate(0x7b, 0); dialog.SetCandidate(display, displayFlags);
                Check(!dialog.Part<Button>("SaveSettings").IsEnabled, "Editing display does not clear an unresolved capture validation error");
                dialog.Part<Button>("RestoreCaptureKeys").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(input.Text == Hotkey.Text(Hotkey.DefaultCaptureKey, Hotkey.DefaultModifiers) && dialog.Part<Button>("SaveSettings").IsEnabled,
                    "Restore Capture Default selects Ctrl + Alt + S and clears its validation error");
                input.Focus(); SendMessage(new WindowInteropHelper(dialog).Handle, 0x0100, new IntPtr(0x1b), IntPtr.Zero);
                Check(input.Text == controller.Sink.CaptureHotkeyText, "Escape restores the active capture shortcut in its own input");
                uint single = FreeHotkey(0);
                input.Focus(); SendMessage(new WindowInteropHelper(dialog).Handle, 0x0100, new IntPtr(single), IntPtr.Zero);
                Check(input.Text == Hotkey.Text(single, 0), "The capture input records a real native single-key message");
                dialog.Part<Button>("SaveSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            Check(controller.Sink.CaptureHotkeyModifiers == 0 && new ImageStore(controller.Store.Root).Settings.CaptureHotkeyKey == controller.Sink.CaptureHotkeyKey,
                "Saving the capture input applies and persists a single-key shortcut");
            uint singleCapture = controller.Sink.CaptureHotkeyKey;
            DialogAction(controller, delegate(SettingsWindow dialog) {
                dialog.SetCaptureCandidate(FreeHotkey(6), 6);
                dialog.Part<Button>("CancelSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            Check(controller.Sink.CaptureHotkeyKey == singleCapture && new ImageStore(controller.Store.Root).Settings.CaptureHotkeyKey == singleCapture,
                "Canceling a capture shortcut edit preserves active and saved values");
            Check(controller.ChangeHotkeys(display, displayFlags, capture, 6, out error), "A capture combination can replace a single key");
            var tray = (Forms.NotifyIcon)typeof(Controller).GetField("tray", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller);
            Check(tray.ContextMenuStrip.Items.OfType<Forms.ToolStripMenuItem>().Any(item => item.Text.StartsWith("区域截图") && item.Text.Contains(controller.Sink.CaptureHotkeyText)),
                "The tray capture command displays the custom capture shortcut");
            DialogAction(controller, delegate(SettingsWindow dialog) { SaveSettingsRender(dialog, Path.Combine(output, "capture-hotkey-dialog.png")); });
        }

        private static int CheckSettingsMaterials(string output)
        {
            Directory.CreateDirectory(output);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Controller controller = null; SettingsWindow dialog = null;
            try {
                string root = Path.Combine(output, "material-run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                var store = new ImageStore(root);
                store.Settings.ListenClipboard = false; store.Settings.CollectionPaused = true; store.Settings.AutoCheckUpdates = false;
                store.Settings.HotkeyKey = FreeHotkey(7); store.Settings.HotkeyModifiers = 7;
                store.Settings.CaptureHotkeyKey = FreeHotkey(6); store.Settings.CaptureHotkeyModifiers = 6; store.Save();
                controller = new Controller(root, false, new Updates(new UpdateHandler { Json = Encoding.UTF8.GetBytes("[]") }));
                controller.Busy = true;
                dialog = new SettingsWindow(controller) { ShowActivated = false };
                dialog.Show(); Pump(500);
                var screen = Forms.Screen.FromHandle(new WindowInteropHelper(dialog).Handle);
                double scale = Native.Scale(screen);
                var backdrop = new RenderTargetBitmap(screen.Bounds.Width, screen.Bounds.Height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                var landscape = new DrawingVisual();
                using (var dc = landscape.RenderOpen()) {
                    double w = screen.Bounds.Width / scale, h = screen.Bounds.Height / scale;
                    dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(202, 215, 217), Color.FromRgb(141, 166, 154), 90), null, new Rect(0, 0, w, h));
                    dc.PushTransform(new ScaleTransform(w / 1600, h / 1000));
                    foreach (var hill in new[] {
                        new[] { "#B4C5C1", "M0,480 L200,365 L330,180 L520,370 L750,135 L1000,405 L1180,235 L1380,460 L1600,400 L1600,720 L0,720 Z" },
                        new[] { "#8FA9A0", "M0,600 L160,470 L320,540 L490,360 L730,570 L1000,350 L1300,520 L1480,440 L1600,570 L1600,800 L0,800 Z" },
                        new[] { "#708F83", "M0,780 L190,655 L420,705 L690,585 L930,720 L1140,640 L1450,560 L1600,700 L1600,1000 L0,1000 Z" } })
                        dc.DrawGeometry(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hill[0])), null, Geometry.Parse(hill[1]));
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(154, 177, 169)), null, new Rect(0, 775, 1600, 225));
                    dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(65, 90, 79)), null, Geometry.Parse("M0,715 L170,720 L305,1000 L0,1000 Z"));
                    dc.Pop();
                }
                backdrop.Render(landscape); backdrop.Freeze();
                dialog.Glass.SetBackdrop(new GlassBackdrop(backdrop, scale), new Rect(screen.Bounds.Left / scale, screen.Bounds.Top / scale, screen.Bounds.Width / scale, screen.Bounds.Height / scale));
                dialog.UpdateLayout(); Pump(100);
                var glassImage = new RenderTargetBitmap((int)Math.Ceiling(dialog.Glass.ActualWidth * 1.5), (int)Math.Ceiling(dialog.Glass.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
                glassImage.Render(dialog.Glass);
                var glassPixels = new byte[glassImage.PixelWidth * glassImage.PixelHeight * 4];
                glassImage.CopyPixels(glassPixels, glassImage.PixelWidth * 4, 0);
                int center = ((glassImage.PixelHeight / 2) * glassImage.PixelWidth + glassImage.PixelWidth / 2) * 4;
                Check(glassPixels[center + 3] == 255 && glassPixels[3] == 0, "Glass paints the full backdrop center and preserves transparent rounded corners");
                Check(glassPixels[center] < 240 && glassPixels[center + 2] < 240, "The glass center retains landscape color instead of turning into a white rectangle");
                Point beforeMove = dialog.Glass.SampleOrigin;
                int mapBuilds = dialog.Glass.MapBuilds;
                var zeroAlpha = new GlassBackdrop(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 44, 55, 66, 0 }, 4), 1).Sample(new Rect(0, 0, 1, 1));
                var zeroPixels = new byte[4]; zeroAlpha.CopyPixels(zeroPixels, 4, 0);
                Check(zeroPixels.SequenceEqual(new byte[] { 44, 55, 66, 255 }), "Desktop sampling repairs zero-alpha screen pixels without changing their color");
                Check(dialog.Materials.Count == 22 && dialog.Materials.Where(m => m.IsVisible).All(m => m.Frames > 0), "Native metal textures render on all visible controls");
                foreach (string name in new[] { "NavCapture", "CollectToggle", "CancelSettings", "SaveSettings" }) {
                    var control = dialog.Part<Control>(name);
                    Check(control.InputHitTest(new Point(4, control.ActualHeight / 2)) != null &&
                        control.InputHitTest(new Point(control.ActualWidth - 4, control.ActualHeight / 2)) != null,
                        name + " accepts pointer input at both capsule ends, outside its text or thumb");
                }
                foreach (string page in new[] { "General", "Capture", "Keys", "Storage", "About" }) {
                    dialog.ShowPage(page); dialog.UpdateLayout(); Pump(100);
                    var bounds = dialog.Part<Grid>("Pages");
                    Check(Descendants(dialog.Part<StackPanel>(page + "Page")).OfType<FrameworkElement>().Where(el => el is TextBlock || el is Button || el is CheckBox || el is TextBox).All(el => {
                        var box = el.TransformToAncestor(bounds).TransformBounds(new Rect(el.RenderSize));
                        return box.Left >= -1 && box.Top >= -1 && box.Right <= bounds.ActualWidth + 1 && box.Bottom <= bounds.ActualHeight + 1;
                    }), "Native glass " + page + " page keeps labels and controls inside the content area");
                    SaveSettingsRender(dialog, Path.Combine(output, "native-settings-" + page.ToLowerInvariant() + ".png"));
                }
                dialog.ShowPage("General"); dialog.UpdateLayout();
                var surface = dialog.Materials.First(m => m.ActualWidth > 100 && m.ActualHeight > 35);
                var textures = LiquidMetal.Render(140, 41, 3.5, new Point(70, 20.5), false, false, -1);
                var pixels = new byte[textures[0].PixelWidth * textures[0].PixelHeight * 4]; textures[0].CopyPixels(pixels, textures[0].PixelWidth * 4, 0);
                Check(pixels[3] < 24 && pixels[((textures[0].PixelHeight / 2) * textures[0].PixelWidth + textures[0].PixelWidth / 2) * 4 + 3] == 255,
                    "Metal keeps exterior padding translucent and its readable center opaque");
                Check(Enumerable.Range(0, pixels.Length / 4).All(i => pixels[i * 4] <= pixels[i * 4 + 3] && pixels[i * 4 + 1] <= pixels[i * 4 + 3] && pixels[i * 4 + 2] <= pixels[i * 4 + 3]),
                    "Native metal pixels use valid premultiplied alpha");
                var hovered = LiquidMetal.Render(140, 41, 4.1, new Point(100, 16), true, false, .2);
                Check(ImageStore.Digest(textures[0]) != ImageStore.Digest(hovered[0]) && ImageStore.Digest(textures[1]) != ImageStore.Digest(hovered[1]), "Hover and ripple change the native metal body and crisp rim pixels");
                var glowPixels = new byte[textures[2].PixelWidth * textures[2].PixelHeight * 4]; textures[2].CopyPixels(glowPixels, textures[2].PixelWidth * 4, 0);
                int haloPixel = (8 * textures[0].PixelWidth + textures[0].PixelWidth / 2) * 4;
                Check(glowPixels[haloPixel + 3] > 0 && pixels[haloPixel + 3] > 0 && pixels[haloPixel + 3] < 255,
                    "The composited metal halo stays visible four pixels outside the capsule without becoming an opaque border");
                foreach (string background in new[] { "light", "dark", "text" }) {
                    var fixture = new DrawingVisual();
                    using (var dc = fixture.RenderOpen()) {
                        double w = screen.Bounds.Width / scale, h = screen.Bounds.Height / scale;
                        bool light = background == "light";
                        dc.DrawRectangle(new SolidColorBrush(light ? Color.FromRgb(232, 237, 240) : Color.FromRgb(30, 34, 38)), null, new Rect(0, 0, w, h));
                        if (background == "text") {
                            for (int y = 0; y < h; y += 38) dc.DrawText(new FormattedText("BACKGROUND TEXT · 0123456789 · SNAPLINE SETTINGS", System.Globalization.CultureInfo.InvariantCulture,
                                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 18, Brushes.White, scale), new Point(60, y));
                            // Eight-pixel stems reproduce the fine high-contrast
                            // details behind the settings window in the report.
                            for (int x = 0; x < w; x += 16) dc.DrawRectangle(Brushes.White, null, new Rect(x, 0, 8, h));
                        } else {
                            dc.DrawGeometry(new SolidColorBrush(light ? Color.FromRgb(148, 182, 186) : Color.FromRgb(41, 91, 94)), null,
                                Geometry.Parse("M0,450 L420,160 L900,480 L1600,180 L1600,1000 L0,1000 Z"));
                        }
                    }
                    var image = new RenderTargetBitmap(screen.Bounds.Width, screen.Bounds.Height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    image.Render(fixture); image.Freeze();
                    dialog.Glass.SetBackdrop(new GlassBackdrop(image, scale), new Rect(screen.Bounds.Left / scale, screen.Bounds.Top / scale, screen.Bounds.Width / scale, screen.Bounds.Height / scale));
                    Pump(100);
                    SaveSettingsRender(dialog, Path.Combine(output, "native-settings-" + background + "-background.png"));
                    if (background == "text") {
                        var rendered = new RenderTargetBitmap((int)Math.Ceiling(dialog.Glass.ActualWidth), (int)Math.Ceiling(dialog.Glass.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                        rendered.Render(dialog.Glass);
                        var detail = new byte[rendered.PixelWidth * rendered.PixelHeight * 4]; rendered.CopyPixels(detail, rendered.PixelWidth * 4, 0);
                        int row = rendered.PixelHeight / 2, min = 255, max = 0;
                        for (int x = rendered.PixelWidth / 2 - 32; x < rendered.PixelWidth / 2 + 32; x++) { int value = detail[(row * rendered.PixelWidth + x) * 4 + 1]; min = Math.Min(min, value); max = Math.Max(max, value); }
                        Check(max - min < 12, "Frosted glass suppresses fine high-contrast background detail below twelve color levels");
                        Check(detail[(row * rendered.PixelWidth + 28) * 4 + 3] == 255,
                            "Clipping after blur preserves opaque glass beside the bevel instead of a translucent haze");
                    }
                }
                dialog.Glass.SetBackdrop(new GlassBackdrop(backdrop, scale), new Rect(screen.Bounds.Left / scale, screen.Bounds.Top / scale, screen.Bounds.Width / scale, screen.Bounds.Height / scale));
                beforeMove = dialog.Glass.SampleOrigin; mapBuilds = dialog.Glass.MapBuilds;
                var performance = Stopwatch.StartNew();
                for (int n = 0; n < 10; n++) LiquidMetal.Render(140, 41, 4 + n / 30.0, new Point(100, 16), true, false, .2 + n / 30.0);
                performance.Stop();
                Console.WriteLine("Metal 140x41 render: " + (performance.Elapsed.TotalMilliseconds / 10).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " ms/frame");
                File.WriteAllBytes(Path.Combine(output, "metal-idle.png"), ImageStore.Png(textures[0]));
                File.WriteAllBytes(Path.Combine(output, "metal-hover.png"), ImageStore.Png(hovered[0]));
                var nav = dialog.Part<RadioButton>("NavGeneral");
                dialog.Activate(); nav.Focus(); dialog.SetInputMode(true); Pump(100);
                Check(dialog.Materials.Any(m => m.FocusVisible), "Keyboard interaction shows a native focus outline");
                SaveSettingsRender(dialog, Path.Combine(output, "native-keyboard-focus.png"));
                dialog.SetInputMode(false); Pump(100);
                Check(!dialog.Materials.Any(m => m.FocusVisible) && nav.IsKeyboardFocused, "Mouse input removes the outline while preserving the same focused native control");
                Pump(2700);
                Check(!dialog.Materials.Any(m => m.Animating), "Native material animation stops after keyboard focus returns to pointer input");
                int frames = dialog.Materials.Sum(m => m.Frames); Pump(350);
                Check(frames == dialog.Materials.Sum(m => m.Frames), "Idle settings do not continuously regenerate metal textures");
                var dragClock = Stopwatch.StartNew();
                double startLeft = dialog.Left;
                for (int n = 0; n < 80; n++) { dialog.Left = startLeft + n % 20; dialog.Glass.Update(); }
                dragClock.Stop();
                Console.WriteLine("Glass move update: " + (dragClock.Elapsed.TotalMilliseconds / 80).ToString("F3", CultureInfo.InvariantCulture) + " ms/update");
                Check(dragClock.Elapsed.TotalMilliseconds / 80 < 4 && dialog.Glass.MapBuilds == mapBuilds,
                    "Repeated moves stay below four milliseconds per CPU update and reuse the same optical map");
                dialog.Left += 24; dialog.Glass.Update(); Pump(100);
                SaveSettingsRender(dialog, Path.Combine(output, "native-settings-moved.png"));
                Check(beforeMove != dialog.Glass.SampleOrigin && dialog.Glass.MapBuilds == mapBuilds, "Moving settings changes background coordinates without rebuilding optical pixels");
                Console.WriteLine("PASS " + checks.Count + " native material checks.");
                File.WriteAllLines(Path.Combine(output, "material-results.txt"), checks);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace); return 1; }
            finally { if (dialog != null) dialog.Close(); if (controller != null) controller.Dispose(); app.Shutdown(); }
        }

        private static void DialogAction(Controller controller, Action<SettingsWindow> action)
        {
            Exception failure = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            try {
                timer.Tick += delegate {
                    var dialog = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault(window => window.IsVisible);
                    if (dialog == null) return;
                    timer.Stop();
                    try { action(dialog); }
                    catch (Exception ex) { failure = ex; }
                    finally { if (dialog.IsVisible) dialog.Close(); }
                };
                timer.Start();
                controller.ShowHotkeySettings();
            }
            finally { timer.Stop(); }
            controller.Busy = true;
            if (failure != null) throw failure;
        }

        private static void SaveSettingsRender(SettingsWindow dialog, string path)
        {
            dialog.UpdateLayout();
            var image = new RenderTargetBitmap((int)Math.Ceiling(dialog.View.ActualWidth * 1.5), (int)Math.Ceiling(dialog.View.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen()) dc.DrawRectangle(new VisualBrush(dialog.View), null, new Rect(0, 0, dialog.View.ActualWidth, dialog.View.ActualHeight));
            image.Render(drawing);
            File.WriteAllBytes(path, ImageStore.Png(image));
        }

        private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) {
                var child = VisualTreeHelper.GetChild(root, i); yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }

        private static int CheckInboxLocation(string output)
        {
            Directory.CreateDirectory(output);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Controller controller = null;
            try {
                string root = Path.Combine(output, "run-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                var store = new ImageStore(Path.Combine(root, "store"));
                string custom = Path.Combine(root, "中文截图 收件夹"), error;
                var original = store.AddBitmap(Fixture(300, 220, 0));
                Check(store.SetInbox(custom + Path.DirectorySeparatorChar, out error) && store.Inbox == custom,
                    "Inbox can move to a newly created Unicode folder with a normalized path");
                var shot = store.AddBitmap(Fixture(310, 220, 1));
                Check(shot != null && File.Exists(shot.Path) && Path.GetDirectoryName(shot.Path) == custom && store.Owns(shot.Path) && store.Owns(original.Path),
                    "New screenshots are written to and owned by the selected inbox; default inbox captures remain owned");
                var restored = new ImageStore(store.Root);
                Check(restored.Inbox == custom && restored.Settings.InboxFolder == custom && restored.Items.Count == 2,
                    "Inbox location and screenshots survive restart");
                Check(restored.SetInbox(null, out error) && restored.Inbox == restored.DefaultInbox && restored.Settings.InboxFolder == null &&
                    new ImageStore(store.Root).Inbox == restored.DefaultInbox,
                    "Null restores the default inbox and persists the default choice");
                restored.Remove(restored.Items.Single(item => item.Path == shot.Path), true);
                Check(File.Exists(shot.Path) && !restored.Owns(shot.Path), "Taking down a previous custom inbox capture preserves its original file");
                string sibling = custom + "-other";
                Directory.CreateDirectory(sibling);
                string external = Path.Combine(sibling, "external.png"); File.Copy(original.Path, external);
                Check(!restored.Owns(external), "Similarly named folders never become owned inboxes");
                string state = Path.Combine(store.Root, "settings.json");
                byte[] before = File.ReadAllBytes(state);
                Check(!restored.SetInbox(original.Path, out error) && !string.IsNullOrEmpty(error) && restored.Inbox == restored.DefaultInbox &&
                    File.ReadAllBytes(state).SequenceEqual(before), "A file used as the target is rejected without changing saved preferences");
                using (var locked = new FileStream(state + ".tmp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    Check(!restored.SetInbox(custom, out error) && restored.Inbox == restored.DefaultInbox && restored.Settings.InboxFolder == null &&
                        File.ReadAllBytes(state).SequenceEqual(before), "A settings write failure rolls back both inbox fields and preserves saved preferences");
                restored.Settings.InboxFolder = original.Path; restored.Save();
                Check(new ImageStore(store.Root).Inbox == restored.DefaultInbox, "An unavailable custom inbox falls back to the default during startup");
                Check(Directory.GetFiles(custom, ".snapline-write-*").Length == 0, "Writable-folder probes leave no temporary files behind");

                string liveRoot = Path.Combine(root, "live");
                var live = new ImageStore(liveRoot);
                live.Settings.ListenClipboard = false; live.Settings.AutoCheckUpdates = false; live.Settings.WatchFolder = null;
                live.Settings.HotkeyKey = FreeHotkey(7); live.Settings.HotkeyModifiers = 7;
                live.Settings.CaptureHotkeyKey = FreeHotkey(6); live.Settings.CaptureHotkeyModifiers = 6; live.Save();
                controller = new Controller(liveRoot, true, new Updates(new UpdateHandler { Json = Encoding.UTF8.GetBytes("[]") }));
                controller.Busy = true;
                string liveInbox = Path.Combine(root, "监听 收件夹");
                Check(controller.ChangeInbox(liveInbox, out error), "Controller accepts an inbox change with file watching enabled");
                string watched = Path.Combine(liveInbox, "watched.png");
                File.WriteAllBytes(watched, ImageStore.Png(Fixture(320, 220, 2))); Pump(800);
                Check(controller.Store.Items.Any(item => item.Path == watched), "Inbox change restarts the watcher and collects files from the new folder");
                string oldFile = Path.Combine(controller.Store.DefaultInbox, "unwatched.png");
                File.WriteAllBytes(oldFile, ImageStore.Png(Fixture(330, 220, 3))); Pump(600);
                Check(!controller.Store.Items.Any(item => item.Path == oldFile), "The previous inbox watcher is released after the location changes");
                controller.Store.Settings.CollectionPaused = true; controller.Store.Save();
                string candidate = Path.Combine(root, "待保存位置");
                DialogAction(controller, delegate(SettingsWindow dialog) {
                    dialog.ShowPage("Storage");
                    Check(dialog.Part<Button>("ChooseInbox").IsVisible && dialog.SetInboxCandidate(candidate) &&
                        dialog.Part<TextBlock>("SettingsFeedback").Text == "有未保存的修改" && controller.Store.Inbox == liveInbox,
                        "Choosing an inbox displays unsaved feedback without changing the active location");
                    Pump(250);
                    dialog.UpdateLayout();
                    var label = dialog.Part<TextBlock>("InboxPath");
                    var buttons = (StackPanel)dialog.Part<Button>("ChooseInbox").Parent;
                    var row = (Grid)buttons.Parent;
                    var labelBox = label.TransformToAncestor(row).TransformBounds(new Rect(label.RenderSize));
                    var buttonBox = buttons.TransformToAncestor(row).TransformBounds(new Rect(buttons.RenderSize));
                    Check(label.MaxWidth == 300 && label.TextTrimming == TextTrimming.CharacterEllipsis && labelBox.Right <= buttonBox.Left &&
                        buttonBox.Right <= row.ActualWidth + 1 && Descendants(dialog.Part<Button>("ChooseInbox")).OfType<LiquidMetal>().Any(material => material.Frames > 0),
                        "Inbox path and both buttons fit the row; the new button has liquid metal material");
                    SaveSettingsRender(dialog, Path.Combine(output, "settings-storage.png"));
                    dialog.Part<Button>("CancelSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                Check(controller.Store.Inbox == liveInbox && new ImageStore(liveRoot).Inbox == liveInbox, "Cancel preserves the current and saved inbox locations");
                DialogAction(controller, delegate(SettingsWindow dialog) {
                    dialog.ShowPage("Storage"); dialog.SetInboxCandidate(candidate);
                    using (var locked = new FileStream(Path.Combine(liveRoot, "settings.json.tmp"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
                        dialog.Part<Button>("SaveSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Check(dialog.IsVisible && controller.Store.Inbox == liveInbox &&
                            ((SolidColorBrush)dialog.Part<TextBlock>("SettingsFeedback").Foreground).Color == Colors.Firebrick,
                            "A failed save keeps settings open with a red error and the previous inbox active");
                    }
                    dialog.Part<Button>("SaveSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                Check(controller.Store.Inbox == candidate && new ImageStore(liveRoot).Inbox == candidate, "Saving the settings window applies and persists the chosen inbox");
                DialogAction(controller, delegate(SettingsWindow dialog) {
                    dialog.SetInboxCandidate(controller.Store.DefaultInbox);
                    dialog.Part<Button>("SaveSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                Check(controller.Store.Inbox == controller.Store.DefaultInbox && controller.Store.Settings.InboxFolder == null,
                    "Choosing the default folder in settings restores the default inbox");
                File.WriteAllLines(Path.Combine(output, "inbox-results.txt"), checks);
                Console.WriteLine("PASS " + checks.Count + " inbox checks using isolated fixtures; no keyboard, mouse or clipboard input."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { if (controller != null) controller.Dispose(); app.Shutdown(); }
        }

        private static int CheckSettingsOnly(string output)
        {
            Directory.CreateDirectory(output);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Controller controller = null;
            try {
                string root = Path.Combine(output, "settings-run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                var store = new ImageStore(root);
                store.Settings.ListenClipboard = false; store.Settings.CollectionPaused = true; store.Settings.AutoCheckUpdates = false;
                store.Settings.HotkeyKey = FreeHotkey(7); store.Settings.HotkeyModifiers = 7;
                store.Settings.CaptureHotkeyKey = FreeHotkey(6); store.Settings.CaptureHotkeyModifiers = 6; store.Save();
                controller = new Controller(root, false, new Updates(new UpdateHandler { Json = Encoding.UTF8.GetBytes("[]") }));
                controller.Busy = true;
                controller.Line.Reveal(Forms.Screen.FromPoint(Native.Cursor())); Pump(80); controller.Line.Hide();
                CheckSettings(controller, output, false);
                File.WriteAllLines(Path.Combine(output, "settings-results.txt"), checks);
                Console.WriteLine("PASS " + checks.Count + " settings checks without keyboard, mouse or clipboard input."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { if (controller != null) controller.Dispose(); app.Shutdown(); }
        }

        private static void CheckSettings(Controller controller, string output, bool checkToolbar = true)
        {
            bool originalClipboard = controller.Store.Settings.ListenClipboard, originalPaused = controller.Store.Settings.CollectionPaused;
            bool originalAutomatic = controller.Store.Settings.AutoCheckUpdates;
            controller.Store.Settings.UpdateCheckedUtcTicks = DateTime.UtcNow.Ticks;
            string runValue;
            using (var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                runValue = run == null ? null : run.GetValue("Snapline") as string;
            DialogAction(controller, delegate(SettingsWindow dialog) {
                Check(dialog.Part<CheckBox>("StartupToggle").IsChecked == controller.StartupEnabled, "Settings reads actual startup state without enabling it");
                var saveText = Descendants(dialog.Part<Button>("SaveSettings")).OfType<TextBlock>().First(block => block.Text == "保存");
                Check(((SolidColorBrush)saveText.Foreground).Color == ((SolidColorBrush)Ui.Brush("Surface")).Color,
                    "Save button renders light text on its dark surface");
                var closeText = Descendants(dialog.Part<Button>("CloseSettings")).OfType<TextBlock>().First();
                Check(closeText.FontFamily.Source == "Segoe MDL2 Assets", "Close button renders using the real Windows icon font");
                foreach (string page in new[] { "General", "Capture", "Keys", "Storage", "About" }) {
                    dialog.ShowPage(page); dialog.UpdateLayout();
                    var bounds = (Grid)dialog.View.FindName("Pages");
                    var content = dialog.Part<StackPanel>(page + "Page");
                    bool fits = Descendants(content).OfType<FrameworkElement>().Where(el => el is TextBlock || el is Button || el is CheckBox || el is TextBox).All(el => {
                        var box = el.TransformToAncestor(bounds).TransformBounds(new Rect(el.RenderSize));
                        bool inside = box.Top >= -1 && box.Bottom <= bounds.ActualHeight + 1 && box.Right <= bounds.ActualWidth + 1;
                        if (!inside) Console.WriteLine("Clipped " + page + " / " + el.Name + " / " + box + " / available " + bounds.RenderSize);
                        return inside;
                    });
                    Check(fits, page + " settings page has no clipped text or controls");
                    SaveSettingsRender(dialog, Path.Combine(output, "settings-" + page.ToLowerInvariant() + ".png"));
                }
                dialog.ShowPage("Keys");
                Check(!dialog.SetCandidate(0x7b, 0) && !dialog.Part<Button>("SaveSettings").IsEnabled && dialog.Part<TextBlock>("KeyHint").Text.Contains("F12"), "New settings rejects reserved F12 with inline error");
                dialog.Part<Button>("RestoreKeys").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(dialog.Part<Button>("SaveSettings").IsEnabled, "Restore default recovers settings Save after invalid input");
                dialog.Part<CheckBox>("CollectToggle").IsChecked = !originalClipboard;
                dialog.Part<CheckBox>("StartupToggle").IsChecked = !controller.StartupEnabled;
                dialog.Part<Button>("CancelSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            Check(controller.Store.Settings.ListenClipboard == originalClipboard && new ImageStore(controller.Store.Root).Settings.ListenClipboard == originalClipboard,
                "Canceling general preferences preserves active and stored settings");
            DialogAction(controller, delegate(SettingsWindow dialog) {
                dialog.ShowPage("General");
                dialog.Part<CheckBox>("CollectToggle").IsChecked = false;
                dialog.Part<CheckBox>("PauseToggle").IsChecked = true;
                dialog.Part<CheckBox>("UpdateToggle").IsChecked = false;
                dialog.Part<Button>("SaveSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            var saved = new ImageStore(controller.Store.Root).Settings;
            Check(!saved.ListenClipboard && saved.CollectionPaused && !saved.AutoCheckUpdates, "New settings saves collection, pause and update preferences together");
            Check(controller.Line.StatusText.Text == "收集已暂停", "Saved settings update the real shelf status");
            string error;
            using (var locked = new FileStream(Path.Combine(controller.Store.Root, "settings.json.tmp"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                Check(!controller.ApplyPreferences(true, false, true, false, controller.Store.Settings.WatchFolder, FreeHotkey(7), 7, out error) &&
                    !controller.Store.Settings.ListenClipboard && controller.Store.Settings.CollectionPaused && !controller.Store.Settings.AutoCheckUpdates,
                    "Settings write failure rolls back all collection preferences as well as the shortcut");
            Check(controller.ApplyPreferences(originalClipboard, originalPaused, originalAutomatic, controller.Store.Settings.IncludeCursor, controller.Store.Settings.WatchFolder, controller.Sink.HotkeyKey, controller.Sink.HotkeyModifiers, out error),
                "Settings can save normally after a write failure");
            using (var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                Check((run == null ? null : run.GetValue("Snapline") as string) == runValue, "Opening, canceling and saving preferences leaves real startup entry untouched");
            if (!checkToolbar) return;
            controller.Line.Reveal(Forms.Screen.FromPoint(Native.Cursor())); Pump(400);
            var latest = controller.Store.Items.Last();
            controller.Line.LatestButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(ImageStore.Digest(BitmapFrame.Create((Stream)Clipboard.GetData("PNG"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad)) == latest.Hash,
                "New shelf Copy Latest button writes actual latest image pixels");
            controller.Line.PauseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(controller.Store.Settings.CollectionPaused != originalPaused && controller.Line.StatusText.Text == "收集已暂停", "New shelf pause button updates saved preference and visible status");
            controller.Line.PauseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(controller.Store.Settings.CollectionPaused == originalPaused, "New shelf resume button restores collection");
            var buttonPoint = controller.Line.SettingsButton.PointToScreen(new Point(10, 10));
            Check(Hit(new WindowInteropHelper(controller.Line).Handle, buttonPoint) == 1, "Native hit testing accepts the new toolbar buttons");
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

        private static void CheckGlassMaterial()
        {
            var area = new Rect(17, 18, 240, 76);
            var first = new GlassBackdrop(Fixture(400, 200, 1), 1).Bevel(area, 22);
            var second = new GlassBackdrop(Fixture(400, 200, 2), 1).Bevel(area, 22);
            var pixels = new byte[first.PixelWidth * first.PixelHeight * 4];
            first.CopyPixels(pixels, first.PixelWidth * 4, 0);
            Check(pixels[(38 * first.PixelWidth + 120) * 4 + 3] == 0, "Optical material leaves the center clear so screenshot pixels remain in their own layer");
            Check(pixels[3] == 0 && pixels[(2 * first.PixelWidth + 120) * 4 + 3] > 0, "Rounded corners are transparent while the bevel contains refracted backdrop pixels");
            Check(ImageStore.Digest(first) != ImageStore.Digest(second), "Optical rim responds to the actual backdrop instead of a fixed painted gradient");
            Check(Enumerable.Range(0, pixels.Length / 4).All(index => pixels[index * 4] <= pixels[index * 4 + 3] &&
                pixels[index * 4 + 1] <= pixels[index * 4 + 3] && pixels[index * 4 + 2] <= pixels[index * 4 + 3]), "Glass bitmap has valid premultiplied alpha without bright halos");
            var scaled = new GlassBackdrop(Fixture(600, 300, 1), 1.5).Bevel(area, 22);
            Check(scaled.PixelWidth == 360 && scaled.PixelHeight == 114 && Math.Abs(scaled.Width - area.Width) < 0.001,
                "150 percent DPI material retains its logical size and pixel alignment");
            var split = new byte[400 * 240 * 4];
            for (int y = 0; y < 240; y++)
            for (int x = 0; x < 400; x++)
            {
                int offset = (y * 400 + x) * 4;
                split[offset] = (byte)(x < 200 ? 220 : 20); split[offset + 1] = 30;
                split[offset + 2] = (byte)(x < 200 ? 20 : 220); split[offset + 3] = 255;
            }
            var backdrop = new GlassBackdrop(BitmapSource.Create(400, 240, 96, 96, PixelFormats.Bgra32, null, split, 400 * 4), 1);
            var map = new GlassBackdrop.BevelMap(new Size(160, 120), 14, 1);
            var left = new byte[160 * 120 * 4]; var right = new byte[left.Length];
            map.Render(backdrop, Matrix.Identity).CopyPixels(left, 160 * 4, 0);
            map.Render(backdrop, new Matrix(1, 0, 0, 1, 220, 0)).CopyPixels(right, 160 * 4, 0);
            int rimPixel = (2 * 160 + 80) * 4;
            Check(left[rimPixel] > left[rimPixel + 2] && right[rimPixel + 2] > right[rimPixel],
                "Moving a photo container samples the blue or red backdrop region at its actual position");
            map.Render(backdrop, new Matrix(0, 1, -1, 0, 220, 10)).CopyPixels(right, 160 * 4, 0);
            Check(right[rimPixel + 2] > right[rimPixel], "Rotated containers counter-align the backdrop instead of rotating a stale texture");
        }

        private static void SaveRender(LineWindow line, string path)
        {
            var image = new RenderTargetBitmap((int)Math.Ceiling(line.ActualWidth), (int)Math.Ceiling(line.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            image.Render(line);
            File.WriteAllBytes(path, ImageStore.Png(image));
        }

        private static IntPtr AnnouncementHandle(Process process)
        {
            IntPtr window = IntPtr.Zero;
            while ((window = FindWindowEx(IntPtr.Zero, window, null, "Snapline · 更新公告")) != IntPtr.Zero) {
                uint owner; GetWindowThreadProcessId(window, out owner);
                if (owner == process.Id) return window;
            }
            return IntPtr.Zero;
        }

        private static void SmokeExecutable(string root)
        {
            string executable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Snapline.exe");
            string dataRoot = Path.Combine(root, "executable-smoke");
            var settings = new ImageStore(dataRoot);
            settings.Settings.ListenClipboard = false; settings.Settings.AutoCheckUpdates = false;
            settings.Settings.CollectionPaused = true; settings.Settings.HotkeyKey = FreeHotkey(7); settings.Settings.HotkeyModifiers = 7; settings.Save();
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
                    IntPtr announcement = AnnouncementHandle(process);
                    var deadline = DateTime.UtcNow.AddSeconds(4);
                    while (announcement == IntPtr.Zero && !process.HasExited && DateTime.UtcNow < deadline) { Pump(100); announcement = AnnouncementHandle(process); }
                    Check(announcement != IntPtr.Zero, "The actual released EXE automatically displays the current announcement on its first launch");
                    SendMessage(announcement, 0x10, IntPtr.Zero, IntPtr.Zero); Pump(200);
                    Check(!AnnouncementWindow.Due(new ImageStore(dataRoot).Settings), "Closing the actual EXE announcement saves its version in the isolated settings");
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
            using (var restarted = Process.Start(launch))
            {
                try {
                    restarted.WaitForInputIdle(4000); Pump(700);
                    Check(!restarted.HasExited && AnnouncementHandle(restarted) == IntPtr.Zero && !AnnouncementWindow.Due(new ImageStore(dataRoot).Settings),
                        "The released EXE restarts without repeating an already-read version announcement");
                    uint thread = 0; IntPtr messageWindow = IntPtr.Zero;
                    while ((messageWindow = FindWindowEx(new IntPtr(-3), messageWindow, null, "Snapline messages")) != IntPtr.Zero) {
                        uint owner; uint candidate = GetWindowThreadProcessId(messageWindow, out owner);
                        if (owner == restarted.Id) { thread = candidate; break; }
                    }
                    Check(thread != 0 && PostThreadMessage(thread, 0x0012, IntPtr.Zero, IntPtr.Zero) && restarted.WaitForExit(5000) && restarted.ExitCode == 0,
                        "The announcement-enabled EXE exits cleanly after its subsequent launch");
                }
                finally { if (!restarted.HasExited) restarted.Kill(); }
            }
        }

        private static void SavePreview(LineWindow line, string path, string hotkey)
        {
            var visual = new DrawingVisual();
            const int width = 1200, height = 500;
            double originalWidth = line.Width;
            line.Width = 1132;
            Pump(150);
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Ui.Brush("Paper"), null, new Rect(0, 0, width, height));
                DrawText(dc, "Snapline", 32, Ui.Brush("Ink"), 60, 30);
                DrawText(dc, "Screenshots, within reach.  /  Windows v" + Updates.CurrentVersion, 14, Ui.Brush("Muted"), 62, 81);
                dc.DrawRectangle(new VisualBrush(line) { Stretch = Stretch.Fill }, null, new Rect(34, 117, 1132, 320));
                DrawText(dc, hotkey + "  /  Tendedero by Alejandro Buján · Unofficial Windows port", 12, Ui.Brush("Muted"), 62, 461);
            }
            var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            image.Render(visual);
            File.WriteAllBytes(path, ImageStore.Png(image));
            line.Width = originalWidth;
            Pump(100);
        }
    }
}
