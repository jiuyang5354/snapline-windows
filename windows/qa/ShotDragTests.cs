using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Snapline
{
    internal static class ShotDragTests
    {
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
        [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, int message, IntPtr wParam, IntPtr lParam);
        private static readonly List<string> checks = new List<string>();
        private static void Check(bool ok, string name)
        { if (!ok) throw new Exception("FAIL: " + name); checks.Add("PASS: " + name); Console.WriteLine("PASS: " + name); }
        private static void Pump(int ms)
        {
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        private static void Drag(Point start, Point destination, bool cancel)
        {
            var worker = new Thread(delegate() {
                SetCursorPos((int)start.X, (int)start.Y); Thread.Sleep(100);
                mouse_event(2, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(70);
                SetCursorPos((int)start.X + 24, (int)start.Y + 5); Thread.Sleep(140);
                for (int i = 1; i <= 12; i++) {
                    SetCursorPos((int)(start.X + (destination.X - start.X) * i / 12), (int)(start.Y + (destination.Y - start.Y) * i / 12)); Thread.Sleep(30);
                }
                Thread.Sleep(150);
                if (cancel) { keybd_event(27, 0, 0, UIntPtr.Zero); Thread.Sleep(100); keybd_event(27, 0, 2, UIntPtr.Zero); }
                mouse_event(4, 0, 0, 0, UIntPtr.Zero);
            }) { IsBackground = true };
            worker.Start(); var end = DateTime.UtcNow.AddSeconds(8);
            while (worker.IsAlive && DateTime.UtcNow < end) Pump(40);
            if (worker.IsAlive) throw new Exception("Mouse test timed out");
            Pump(300);
        }
        private static IEnumerable<DependencyObject> Children(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Children(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        private static Point Source(Controller controller)
        {
            controller.Busy = true;
            controller.Line.Reveal(System.Windows.Forms.Screen.PrimaryScreen); Pump(1350);
            var card = Children(controller.Line).OfType<ShotCard>().Single();
            return card.PointToScreen(new Point(card.ActualWidth / 2, 65));
        }
        internal static int Run(string output)
        {
            Directory.CreateDirectory(output);
            var root = Path.Combine(output, "run-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "settings.json"), "{\"ListenClipboard\":false,\"AutoCheckUpdates\":false,\"CollectionPaused\":true,\"FileShelfEnabled\":false,\"HotkeyKey\":133,\"HotkeyModifiers\":7,\"CaptureHotkeyKey\":132,\"CaptureHotkeyModifiers\":7,\"ClearHotkeyKey\":131,\"ClearHotkeyModifiers\":7,\"WatchFolder\":null,\"Paths\":[]}");
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Controller controller = null; Window window = null; string restart = null;
            EventHandler sample = null;
            var cursor = Native.Cursor();
            try {
                foreach (var process in Process.GetProcessesByName("Snapline")) {
                    using (process) {
                        string path = process.MainModule.FileName;
                        if (!string.Equals(path, @"D:\Snapline\Snapline.exe", StringComparison.OrdinalIgnoreCase)) continue;
                        IntPtr handle = IntPtr.Zero;
                        while ((handle = FindWindowEx(new IntPtr(-3), handle, null, "Snapline messages")) != IntPtr.Zero) {
                            uint owner; uint thread = GetWindowThreadProcessId(handle, out owner);
                            if (owner != process.Id) continue;
                            if (!PostThreadMessage(thread, 0x12, IntPtr.Zero, IntPtr.Zero) || !process.WaitForExit(5000)) throw new Exception("Cannot suspend installed Snapline for isolated test");
                            restart = path; break;
                        }
                    }
                }
                controller = new Controller(root, false); controller.Busy = true;
                var image = new RenderTargetBitmap(200, 120, 96, 96, PixelFormats.Pbgra32); var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen()) dc.DrawRectangle(Brushes.Coral, null, new Rect(0, 0, 200, 120));
                image.Render(visual); image.Freeze(); var shot = controller.Store.AddBitmap(image); var original = File.ReadAllBytes(shot.Path);
                string copied = Path.Combine(root, "received.png"); int drops = 0;
                var receiver = new Border { Background = Brushes.LightBlue, AllowDrop = true, Child = new TextBlock { Text = "生成的图片接收区", Padding = new Thickness(18) } };
                receiver.DragOver += delegate(object sender, DragEventArgs e) { e.Effects = e.AllowedEffects & DragDropEffects.Copy; e.Handled = true; };
                receiver.Drop += delegate(object sender, DragEventArgs e) {
                    var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                    if (paths != null && paths.Length == 1) { File.Copy(paths[0], copied, true); drops++; e.Effects = DragDropEffects.Copy; }
                    else e.Effects = DragDropEffects.None;
                    e.Handled = true;
                };
                var blank = new Border { Background = Brushes.Beige, Child = new TextBlock { Text = "生成的非接收区域", Padding = new Thickness(18) } };
                var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.Children.Add(receiver); Grid.SetColumn(blank, 1); grid.Children.Add(blank);
                var area = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea; double scale = Native.Scale(System.Windows.Forms.Screen.PrimaryScreen);
                window = new Window { Title = "Snapline 隔离拖拽测试", Width = 600, Height = 220, Topmost = true,
                    Left = (area.Left + 150) / scale, Top = (area.Top + 350) / scale, Content = grid };
                window.Show(); Pump(200);
                int tensionFrames = 0; double maximumPull = 0;
                sample = delegate {
                    if (!controller.Line.RopeAnimating || (Native.GetAsyncKeyState(1) & 0x8000) == 0) return;
                    var card = ((Canvas)controller.Line.Content).Children.OfType<ShotCard>().FirstOrDefault();
                    if (card == null) return;
                    var offset = ((TransformGroup)card.RenderTransform).Children.OfType<TranslateTransform>().Single();
                    maximumPull = Math.Max(maximumPull, Math.Abs(offset.Y));
                    if (Math.Abs(offset.Y) > .05) tensionFrames++;
                };
                CompositionTarget.Rendering += sample;
                controller.Store.Settings.RemoveOnBlankDrop = true;
                Drag(Source(controller), receiver.PointToScreen(new Point(100, 75)), false);
                Check(drops == 1 && File.Exists(copied) && File.ReadAllBytes(copied).SequenceEqual(original), "Real OLE receiver gets a byte-identical image copy");
                Check(controller.Store.Items.Contains(shot) && File.ReadAllBytes(shot.Path).SequenceEqual(original), "Successful real drag leaves the card and original in place");
                Check(!SystemParameters.ClientAreaAnimation || tensionFrames > 3, "Real OLE dragging updates attachment tension over multiple rendered frames before release");
                Check(maximumPull < 25, "Real drag across the desktop keeps rope displacement subtle and bounded");
                controller.Busy = true; Pump(2200);
                Check(!controller.Line.RopeAnimating, "Real accepted drag settles and stops the render subscription");
                Drag(Source(controller), blank.PointToScreen(new Point(100, 75)), true);
                Check(controller.Store.Items.Contains(shot) && File.Exists(shot.Path), "Real Escape cancel preserves the card even when blank-drop removal is enabled");
                controller.Store.Settings.RemoveOnBlankDrop = false;
                Drag(Source(controller), blank.PointToScreen(new Point(100, 75)), false);
                Check(controller.Store.Items.Contains(shot), "Real blank drop preserves the card when the preference is disabled");
                controller.Store.Settings.RemoveOnBlankDrop = true;
                Drag(Source(controller), blank.PointToScreen(new Point(100, 75)), false);
                Check(controller.Store.Items.Count == 0 && File.ReadAllBytes(shot.Path).SequenceEqual(original), "Enabled real blank drop removes only the card, retaining the file");
                Check(new ImageStore(root).Items.Count == 0, "Real drag removal survives a history reload");
                Check(!controller.Line.RopeAnimating, "Rebuilding after removal releases the active string simulation");
                Console.WriteLine("PASS " + checks.Count + " real screenshot drag checks"); return 0;
            }
            catch (Exception ex) { checks.Add(ex.ToString()); Console.Error.WriteLine(ex); return 1; }
            finally {
                if (sample != null) CompositionTarget.Rendering -= sample;
                mouse_event(4, 0, 0, 0, UIntPtr.Zero); keybd_event(27, 0, 2, UIntPtr.Zero); SetCursorPos(cursor.X, cursor.Y);
                if (window != null) window.Close(); if (controller != null) controller.Dispose(); app.Shutdown();
                if (restart != null) Process.Start(new ProcessStartInfo(restart) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
                File.WriteAllLines(Path.Combine(output, "real-shot-drag-results.txt"), checks);
            }
        }
    }
}
