using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ShelfItem = Snapline.Dropline.ShelfItem;
using ShelfStore = Snapline.Dropline.ShelfStore;
using ShelfWindow = Snapline.Dropline.ShelfWindow;
using ShelfState = Snapline.Dropline.ShelfState;

namespace Snapline
{
    internal static class FileShelfTests
    {
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Native.POINT point);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        private static readonly List<string> results = new List<string>();
        private static void Check(bool ok, string name)
        { if (!ok) throw new Exception("FAIL: " + name); results.Add("PASS: " + name); Console.WriteLine("PASS: " + name); }
        private static void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        private static BitmapSource Image(int width, int height)
        {
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 25; pixels[i + 1] = 110; pixels[i + 2] = 230; pixels[i + 3] = (byte)(i % 255); }
            var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            image.Freeze(); return image;
        }
        private static void Render(FrameworkElement view, string path)
        {
            view.UpdateLayout();
            var image = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            image.Render(view); File.WriteAllBytes(path, ImageStore.Png(image));
        }
        private static void Settings(Controller controller, Action<SettingsWindow> action)
        {
            Exception failure = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += delegate {
                var window = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault(w => w.IsVisible);
                if (window == null) return;
                timer.Stop();
                try { action(window); }
                catch (Exception ex) { failure = ex; }
                finally { if (window.IsVisible) window.Close(); }
            };
            try { timer.Start(); controller.ShowSettings("Shelf"); }
            finally { timer.Stop(); }
            if (failure != null) throw failure;
        }
        internal static int Run(string output)
        {
            Directory.CreateDirectory(output);
            string root = Path.Combine(output, "run-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(root);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Controller controller = null;
            try {
                File.WriteAllText(Path.Combine(root, "settings.json"), "{\"ListenClipboard\":false,\"AutoCheckUpdates\":false,\"CollectionPaused\":true,\"HotkeyKey\":135,\"HotkeyModifiers\":7,\"CaptureHotkeyKey\":134,\"CaptureHotkeyModifiers\":7,\"ClearHotkeyKey\":133,\"ClearHotkeyModifiers\":7,\"Paths\":[]}");
                controller = new Controller(root, false);
                var store = controller.Files.Store;
                var shelf = controller.Files.Window;
                Check(controller.Store.Settings.FileShelfEnabled && !controller.Store.Settings.FileShelfAnyApp && !controller.Store.Settings.FileShelfKeepAfterDragOut,
                    "Legacy settings enable the shelf with Explorer-only expansion and remove-after-success defaults");
                Check(store.Root == Path.Combine(root, "FileShelf") && shelf.State == ShelfState.Hidden,
                    "The empty shelf starts hidden and uses the isolated Snapline data root");
                string source = Path.Combine(root, "原始文件"); Directory.CreateDirectory(source);
                string first = Path.Combine(source, "设计稿.txt"), second = Path.Combine(source, "中文 名称.pdf"), folder = Path.Combine(source, "文件夹");
                File.WriteAllText(first, "Original text"); File.WriteAllText(second, "Synthetic PDF fixture"); Directory.CreateDirectory(folder);
                var files = new DataObject(DataFormats.FileDrop, new[] { first, second, folder });
                Check(ShelfWindow.DropEffect(files, DragDropEffects.Move) == DragDropEffects.None,
                    "Move-only incoming drops are refused so the source cannot delete originals for metadata-only storage");
                Check(ShelfWindow.DropEffect(files, DragDropEffects.Copy | DragDropEffects.Move) == DragDropEffects.Copy &&
                    ShelfWindow.DropEffect(files, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link) == DragDropEffects.Link,
                    "Incoming file drops report only an allowed Copy or Link effect");
                Check(shelf.AcceptDrop(files) && store.Items.Count == 1 && store.Items[0].Paths.Count == 3 && store.Items[0].IsStack,
                    "Dropping files and a folder together produces one stack");
                var dragData = ShelfWindow.DragData(store.Items[0]);
                Check(((string[])dragData.GetData(DataFormats.FileDrop, false)).SequenceEqual(new[] { first, second, folder }),
                    "A stack's actual drag payload contains every file and folder in order");
                Check(!dragData.GetDataPresent(DataFormats.Bitmap, false) && !dragData.GetDataPresent("PNG", false),
                    "Drag-out carries file paths without loading a second full-resolution bitmap payload");
                Check(File.ReadAllText(first) == "Original text" && File.ReadAllText(second) == "Synthetic PDF fixture" &&
                    Directory.Exists(folder) && Directory.GetFiles(store.Clippings).Length == 0,
                    "Parking existing files only records paths; no originals are moved, copied or modified");
                var reloaded = new ShelfStore(store.Root); reloaded.Load();
                Check(reloaded.Items.Count == 1 && reloaded.Items[0].Paths.SequenceEqual(new[] { first, second, folder }),
                    "Unicode paths, folders, stack membership and order survive store reload");
                store.Split(store.Items[0]);
                Check(store.Items.Count == 3 && store.Items.All(i => !i.IsStack) && store.Items.Select(i => i.Paths[0]).SequenceEqual(new[] { first, second, folder }),
                    "Splitting a stack keeps its original path order and saves individual records");
                var parked = store.Items[0];
                shelf.CompleteDragOut(parked, DragDropEffects.None);
                Check(store.Items.Contains(parked), "Cancelling drag-out keeps the parked record");
                shelf.CompleteDragOut(parked, DragDropEffects.Copy);
                Check(!store.Items.Contains(parked) && File.ReadAllText(first) == "Original text", "Successful drag-out removes only the record by default");
                shelf.KeepAfterDragOut = true; parked = store.Items[0];
                shelf.CompleteDragOut(parked, DragDropEffects.Copy);
                Check(store.Items.Contains(parked) && File.Exists(second), "Keep-after-drag preserves the record and original file");
                store.Remove(parked);
                Check(File.Exists(second), "Removing a tile never deletes its external file");
                store.Clear();
                Check(File.Exists(first) && File.Exists(second) && Directory.Exists(folder), "Clearing the shelf keeps all original files and folders");
                Check(shelf.AcceptDrop(new DataObject(DataFormats.UnicodeText, "中文剪藏\n第二行")), "Dropped Unicode text is accepted");
                string textPath = store.Items[0].Paths[0];
                Check(textPath.StartsWith(store.Clippings + Path.DirectorySeparatorChar) && File.ReadAllText(textPath) == "中文剪藏\n第二行" && Path.GetExtension(textPath) == ".txt",
                    "Text is saved as an exact UTF-8 clipping inside the shelf's own folder");
                shelf.AcceptDrop(new DataObject(DataFormats.UnicodeText, "https://example.com/中文?q=设计"));
                string urlPath = store.Items[0].Paths[0];
                Check(Path.GetExtension(urlPath) == ".url" && File.ReadAllText(urlPath).Contains(new Uri("https://example.com/中文?q=设计").AbsoluteUri),
                    "HTTP links become InternetShortcut files without losing Unicode URL content");
                shelf.AcceptDrop(new DataObject(DataFormats.Text, "javascript:alert(1)"));
                Check(Path.GetExtension(store.Items[0].Paths[0]) == ".txt", "Non-HTTP schemes stay plain text and are never opened on drop");
                shelf.AcceptDrop(new DataObject(DataFormats.Text, "CON"));
                Check(Path.GetFileName(store.Items[0].Paths[0]).StartsWith("_CON"), "Reserved Windows names are made writable for text clippings");
                string duplicate = store.SaveText("CON");
                Check(duplicate != store.Items[0].Paths[0] && File.ReadAllText(duplicate) == "CON", "Duplicate clipping names do not overwrite earlier content");
                var image = Image(80, 50); var png = new DataObject(); png.SetData("PNG", new MemoryStream(ImageStore.Png(image)), false);
                Check(shelf.AcceptDrop(png) && ImageStore.Digest(ImageStore.Load(store.Items[0].Paths[0], 0)) == ImageStore.Digest(image),
                    "Direct PNG drops save the full image and preserve transparent pixels");
                var bitmap = new DataObject(); bitmap.SetImage(Image(30, 20));
                Check(shelf.AcceptDrop(bitmap) && ImageStore.Load(store.Items[0].Paths[0], 0).PixelWidth == 30, "Native bitmap drops are saved as PNG clippings");
                var unsupported = new DataObject(); unsupported.SetData("Synthetic Unsupported", new byte[] { 1 });
                Check(ShelfWindow.DropEffect(unsupported, DragDropEffects.All) == DragDropEffects.None && !shelf.AcceptDrop(unsupported),
                    "Unsupported drag data adds no placeholder tile or file");
                string tall = Path.Combine(source, "长图.png"); File.WriteAllBytes(tall, ImageStore.Png(Image(2, 30000)));
                var thumbnail = (BitmapSource)ShelfStore.Thumbnail(tall, 48);
                Check(thumbnail.PixelWidth <= 96 && thumbnail.PixelHeight <= 96, "Tall screenshot thumbnails bound both dimensions instead of allocating full-height images");
                using (File.Open(tall, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { Check(true, "Thumbnail decoding releases its file handle"); }
                var before = store.Items.ToArray();
                string blocker = Path.Combine(store.Root, "shelf.txt.tmp"); Directory.CreateDirectory(blocker);
                bool saveFailed = false;
                try { store.Add(new[] { first }); } catch (IOException) { saveFailed = true; } catch (UnauthorizedAccessException) { saveFailed = true; }
                Directory.Delete(blocker);
                Check(saveFailed && store.Items.SequenceEqual(before), "A failed metadata save rolls back a newly parked tile");
                reloaded.Load(); Check(reloaded.Items.Count == before.Length, "A failed save leaves the previous on-disk shelf intact");
                string error;
                Check(controller.ChangeFileShelf(false, true, true, out error) && shelf.State == ShelfState.Hidden,
                    "Disabling the shelf immediately hides it while retaining all parked entries");
                Check(!shelf.AcceptDrop(files) && store.Items.Count == before.Length, "A disabled shelf does not accept generated drops");
                Check(controller.ChangeFileShelf(true, false, false, out error) && shelf.State == ShelfState.Tab, "Re-enabling restores the collapsed tab and its records");
                shelf.ExternalDragStarted(); Check(shelf.State == ShelfState.Open, "A qualified external drag opens the shelf");
                shelf.ExternalDragEnded(); shelf.Restore(); Check(shelf.State == ShelfState.Tab, "Restoring an occupied shelf displays its edge tab");
                int starts = 0, ends = 0, sourceReads = 0;
                using (var watcher = new Dropline.DragWatcher()) {
                    watcher.SourceFilter = delegate { sourceReads++; return true; };
                    watcher.DragStarted += delegate { starts++; }; watcher.DragEnded += delegate { ends++; };
                    watcher.ProcessMouse(0x201, new Dropline.Native.POINT { X = 50, Y = 50 });
                    watcher.ProcessMouse(0x200, new Dropline.Native.POINT { X = 55, Y = 55 }); Pump(30);
                    Check(starts == 0 && sourceReads == 0, "Small pointer movement does not trigger the shelf or inspect a source window");
                    watcher.ProcessMouse(0x200, new Dropline.Native.POINT { X = 70, Y = 50 });
                    Check(sourceReads == 0, "Source inspection is deferred outside the input sampling callback"); Pump(30);
                    watcher.ProcessMouse(0x200, new Dropline.Native.POINT { X = 90, Y = 50 });
                    watcher.ProcessMouse(0x202, new Dropline.Native.POINT { X = 90, Y = 50 }); Pump(30);
                    Check(starts == 1 && ends == 1 && sourceReads == 1, "A threshold-crossing drag emits one start/end pair without per-move source inspection");
                    watcher.SourceFilter = delegate { return false; };
                    watcher.ProcessMouse(0x201, new Dropline.Native.POINT()); watcher.ProcessMouse(0x200, new Dropline.Native.POINT { X = 50 });
                    watcher.ProcessMouse(0x202, new Dropline.Native.POINT { X = 50 }); Pump(30);
                    Check(starts == 1 && ends == 1, "Ineligible sources do not open or close the shelf");
                    watcher.SourceFilter = delegate { return true; };
                    watcher.ProcessMouse(0x201, new Dropline.Native.POINT()); watcher.ProcessMouse(0x200, new Dropline.Native.POINT { X = 50 });
                    watcher.Dispose(); Pump(30);
                    Check(starts == 1, "Disposal cancels queued drag notifications");
                }
                controller.Store.Save(); byte[] preferences = File.ReadAllBytes(Path.Combine(root, "settings.json"));
                Settings(controller, delegate(SettingsWindow settings) {
                    Check(settings.Part<System.Windows.Controls.Primitives.ToggleButton>("NavShelf").IsChecked == true && settings.Part<StackPanel>("ShelfPage").Visibility == Visibility.Visible,
                        "The native settings navigation opens the new shelf page");
                    Pump(1400); Render(settings.View, Path.Combine(output, "settings-shelf.png"));
                    settings.Part<CheckBox>("ShelfToggle").IsChecked = false;
                    settings.ShowPage("Keys"); settings.Part<TextBox>("HotkeyInput").Focus(); Pump(60);
                    var state = shelf.State; controller.FileShelfHotkey();
                    Check(settings.Part<TextBox>("HotkeyInput").Text == "Ctrl + Alt + D" && shelf.State == state,
                        "The shelf hotkey records a candidate instead of opening the shelf during shortcut recording");
                });
                Check(File.ReadAllBytes(Path.Combine(root, "settings.json")).SequenceEqual(preferences) && shelf.Enabled, "Cancelling shelf settings preserves persisted preferences and runtime behavior");
                shelf.Restore();
                Settings(controller, delegate(SettingsWindow settings) { settings.Part<Button>("OpenShelf").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); });
                Pump(160);
                Check(shelf.State == ShelfState.Open && File.ReadAllBytes(Path.Combine(root, "settings.json")).SequenceEqual(preferences),
                    "Opening the shelf from settings exits the modal dialog and keeps unsaved preferences unchanged");
                Settings(controller, delegate(SettingsWindow settings) {
                    settings.Part<CheckBox>("ShelfToggle").IsChecked = false; settings.Part<CheckBox>("ShelfAnyAppToggle").IsChecked = true;
                    settings.Part<CheckBox>("ShelfKeepToggle").IsChecked = true; settings.Part<Button>("SaveSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                var saved = new ImageStore(root).Settings;
                Check(!saved.FileShelfEnabled && saved.FileShelfAnyApp && saved.FileShelfKeepAfterDragOut && shelf.State == ShelfState.Hidden,
                    "Saving applies all three shelf preferences and persists them for the next launch");
                controller.ChangeFileShelf(true, false, false, out error);
                shelf.ExternalDragStarted(); Pump(180); Render((FrameworkElement)shelf.Content, Path.Combine(output, "file-shelf.png"));
                shelf.ExternalDragEnded(); shelf.Restore();
                var clippings = Directory.GetFiles(store.Clippings);
                store.Clear();
                Check(clippings.All(File.Exists) && File.Exists(first) && File.Exists(second), "Clearing after clipping collection preserves both generated clippings and source files");
                Check(controller.Store.Items.Count == 0, "File shelf operations do not create screenshot-history entries");
                string blockedRoot = Path.Combine(root, "blocked-store"); Directory.CreateDirectory(blockedRoot);
                File.WriteAllText(Path.Combine(blockedRoot, "FileShelf"), "An existing file blocks the optional shelf directory");
                File.WriteAllText(Path.Combine(blockedRoot, "settings.json"), "{\"ListenClipboard\":false,\"AutoCheckUpdates\":false,\"CollectionPaused\":true,\"Paths\":[]}");
                using (var blocked = new Controller(blockedRoot, false)) {
                    Check(!blocked.Files.Window.Enabled && blocked.Store.AddBitmap(Image(12, 8)) != null,
                        "An unavailable optional shelf directory does not prevent screenshot storage or controller startup");
                }
                controller.Files.Window.Close(); controller.Files.Dispose();
                Check(true, "Shelf cleanup remains safe after its native window has already closed");
                string hotkeyRoot = Path.Combine(root, "live-hotkey"); Directory.CreateDirectory(hotkeyRoot);
                File.WriteAllText(Path.Combine(hotkeyRoot, "settings.json"), "{\"ListenClipboard\":false,\"AutoCheckUpdates\":false,\"CollectionPaused\":true,\"WatchFolder\":null,\"Paths\":[]}");
                using (var handle = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Shelf hotkey QA") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 })) {
                    bool reserved = Native.RegisterHotKey(handle.Handle, 80, 0x4003, 0x44);
                    try {
                        using (var live = new Controller(hotkeyRoot, true)) {
                            Check(!live.Files.HotkeyRegistered, "An occupied Ctrl+Alt+D leaves the application running with a manual shelf entry");
                            live.Files.Toggle(); Check(live.Files.Window.State == ShelfState.Open, "The manual shelf entry works when the native shortcut is occupied");
                            if (reserved) {
                                Native.UnregisterHotKey(handle.Handle, 80); reserved = false; live.Files.Apply();
                                Check(live.Files.HotkeyRegistered, "The shelf registers its native shortcut when the conflict is removed");
                                SendMessage(live.Files.Window.Handle, Native.WM_HOTKEY, new IntPtr(FileShelf.HotkeyId), IntPtr.Zero);
                                Check(live.Files.Window.State == ShelfState.Open, "The registered native shelf hotkey dispatches to the actual window");
                            }
                        }
                    }
                    finally { if (reserved) Native.UnregisterHotKey(handle.Handle, 80); }
                }
                Console.WriteLine("PASS " + results.Count + " file-shelf checks with synthetic inputs; no system clipboard or keyboard/mouse input.");
                File.WriteAllLines(Path.Combine(output, "file-shelf-results.txt"), results); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); File.WriteAllLines(Path.Combine(output, "file-shelf-results.txt"), results.Concat(new[] { ex.ToString() })); return 1; }
            finally { if (controller != null) controller.Dispose(); app.Shutdown(); }
        }

        private static void Drag(Point start, Point destination)
        {
            var worker = new Thread(delegate() {
                SetCursorPos((int)start.X, (int)start.Y); Thread.Sleep(100);
                mouse_event(2, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(100);
                SetCursorPos((int)start.X + 24, (int)start.Y + 5); Thread.Sleep(450);
                for (int i = 1; i <= 12; i++) {
                    SetCursorPos((int)(start.X + (destination.X - start.X) * i / 12), (int)(start.Y + (destination.Y - start.Y) * i / 12)); Thread.Sleep(35);
                }
                Thread.Sleep(250); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
            });
            worker.IsBackground = true; worker.Start();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (worker.IsAlive && DateTime.UtcNow < deadline) Pump(50);
            if (worker.IsAlive) throw new Exception("Drag input worker did not finish");
            Pump(180);
        }

        internal static int RunDrag(string output)
        {
            Directory.CreateDirectory(output);
            string root = Path.Combine(output, "real-drag-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(root);
            string original = Path.Combine(root, "拖放测试.txt"); File.WriteAllText(original, "Synthetic drag source");
            File.WriteAllText(Path.Combine(root, "settings.json"), "{\"ListenClipboard\":false,\"AutoCheckUpdates\":false,\"CollectionPaused\":true,\"WatchFolder\":null,\"FileShelfEnabled\":true,\"FileShelfAnyApp\":false,\"Paths\":[]}");
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Controller controller = null; Window target = null; System.Diagnostics.Process sourceProcess = null;
            var cursor = Native.Cursor();
            try {
                controller = new Controller(root, true);
                var shelf = controller.Files.Window;
                var launch = new System.Diagnostics.ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,
                    "--shelf-source \"" + original + "\"") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden };
                sourceProcess = System.Diagnostics.Process.Start(launch);
                var sourceDeadline = DateTime.UtcNow.AddSeconds(4);
                while (DateTime.UtcNow < sourceDeadline && !sourceProcess.HasExited) { sourceProcess.Refresh(); if (sourceProcess.MainWindowHandle != IntPtr.Zero) break; Pump(80); }
                var targetText = new TextBlock { Text = "生成的接收窗口\n只复制测试文件", FontSize = 20, Padding = new Thickness(24), Background = Brushes.LightBlue, AllowDrop = true };
                string copied = Path.Combine(root, "收到的测试文件.txt");
                targetText.DragOver += delegate(object sender, DragEventArgs e) { e.Effects = DragDropEffects.Copy; e.Handled = true; };
                targetText.Drop += delegate(object sender, DragEventArgs e) {
                    var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                    if (paths != null && paths.Length == 1 && paths[0] == original) { File.Copy(original, copied, true); e.Effects = DragDropEffects.Copy; }
                    else e.Effects = DragDropEffects.None;
                    e.Handled = true;
                };
                target = new Window { Title = "Snapline 测试接收窗口", Width = 300, Height = 200, Left = 480, Top = 180, Topmost = true, Content = targetText };
                target.Show(); Pump(300);
                Point from = SourcePoint(sourceProcess, 150, 100);
                Drag(from, new Point(from.X + 70, from.Y + 30));
                Check(shelf.State == ShelfState.Hidden && controller.Files.Store.Items.Count == 0,
                    "A real non-Explorer OLE drag does not auto-expand the shelf in its default source mode");
                string error; controller.ChangeFileShelf(true, true, false, out error);
                var area = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                Drag(from, new Point(area.Right - 65, area.Top + area.Height / 2));
                Check(controller.Files.Store.Items.Count == 1 && controller.Files.Store.Items[0].Paths.Single() == original,
                    "The live input timer opens the shelf and a real OLE file drop parks the generated source");
                Check(File.ReadAllText(original) == "Synthetic drag source", "A real incoming drop leaves its source file unchanged");
                shelf.ExternalDragStarted(); Pump(200);
                FrameworkElement tile = FindTile((DependencyObject)shelf.Content, controller.Files.Store.Items[0]);
                Check(tile != null, "The parked file has a real draggable tile");
                from = tile.PointToScreen(new Point(tile.ActualWidth / 2, 45));
                Drag(from, targetText.PointToScreen(new Point(130, 70)));
                Check(File.Exists(copied) && File.ReadAllText(copied) == File.ReadAllText(original) && File.Exists(original),
                    "Dragging out through real OLE copies the generated file into the receiving window");
                Check(controller.Files.Store.Items.Count == 0, "Successful real drag-out removes the tile under the default policy");
                controller.Files.Store.Add(new[] { original }); shelf.ExternalDragStarted(); Pump(160);
                tile = FindTile((DependencyObject)shelf.Content, controller.Files.Store.Items[0]);
                from = tile.PointToScreen(new Point(tile.ActualWidth / 2, 45));
                Drag(from, SourcePoint(sourceProcess, 35, 160));
                Check(controller.Files.Store.Items.Count == 1 && File.Exists(original), "A cancelled real drag-out preserves the parked tile and source");
                controller.ChangeFileShelf(true, true, true, out error); shelf.ExternalDragStarted(); Pump(160);
                tile = FindTile((DependencyObject)shelf.Content, controller.Files.Store.Items[0]);
                Drag(tile.PointToScreen(new Point(tile.ActualWidth / 2, 45)), targetText.PointToScreen(new Point(130, 70)));
                Check(controller.Files.Store.Items.Count == 1 && File.Exists(copied), "The keep-after-drag preference also works through real OLE");
                var reload = new ShelfStore(controller.Files.Store.Root); reload.Load();
                Check(reload.Items.Count == 1 && reload.Items[0].Paths.Single() == original, "The record retained after real drag-out survives a store reload");
                File.WriteAllLines(Path.Combine(output, "real-drag-results.txt"), results);
                Console.WriteLine("PASS " + results.Count + " real mouse/OLE drag checks; only generated test files were used."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); File.WriteAllLines(Path.Combine(output, "real-drag-results.txt"), results.Concat(new[] { ex.ToString() })); return 1; }
            finally {
                mouse_event(4, 0, 0, 0, UIntPtr.Zero); SetCursorPos(cursor.X, cursor.Y);
                if (sourceProcess != null) { if (!sourceProcess.HasExited) { sourceProcess.CloseMainWindow(); if (!sourceProcess.WaitForExit(3000)) sourceProcess.Kill(); } sourceProcess.Dispose(); }
                if (target != null) target.Close(); if (controller != null) controller.Dispose(); app.Shutdown();
            }
        }

        private static FrameworkElement FindTile(DependencyObject root, ShelfItem item)
        {
            var element = root as FrameworkElement;
            if (element != null && Object.ReferenceEquals(element.Tag, item)) return element;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var found = FindTile(VisualTreeHelper.GetChild(root, i), item); if (found != null) return found; }
            return null;
        }

        private static Point SourcePoint(System.Diagnostics.Process process, int x, int y)
        {
            process.Refresh(); var point = new Native.POINT { X = x, Y = y };
            if (process.MainWindowHandle == IntPtr.Zero || !ClientToScreen(process.MainWindowHandle, ref point)) throw new Exception("Test source window is unavailable");
            return new Point(point.X, point.Y);
        }

        internal static int RunSource(string path)
        {
            var app = new Application();
            var text = new TextBlock { Text = "生成的测试文件\n拖放测试.txt", FontSize = 20, Padding = new Thickness(24), Background = Brushes.Beige };
            var window = new Window { Title = "Snapline 独立测试源文件", Width = 280, Height = 190, Left = 120, Top = 180, Topmost = true, Content = text };
            bool pressed = false; Point origin = new Point();
            text.MouseLeftButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e) { pressed = true; origin = e.GetPosition(text); };
            text.MouseMove += delegate(object sender, System.Windows.Input.MouseEventArgs e) {
                if (!pressed || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
                Point position = e.GetPosition(text);
                if (Math.Abs(position.X - origin.X) + Math.Abs(position.Y - origin.Y) < 10) return;
                pressed = false;
                DragDrop.DoDragDrop(text, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            };
            text.MouseLeftButtonUp += delegate { pressed = false; };
            return app.Run(window);
        }
    }
}
