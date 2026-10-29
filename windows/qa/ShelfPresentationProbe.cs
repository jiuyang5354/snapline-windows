using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Snapline.Dropline;

namespace Snapline
{
    internal static class ShelfPresentationProbe
    {
        private static ShelfWindow window;
        private static readonly List<string> results = new List<string>();
        private static T Field<T>(string name) { return (T)typeof(ShelfWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window); }
        private static void Call(string name, params object[] args) { typeof(ShelfWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, args); }
        private static double Progress { get { return Field<ScaleTransform>("progress").ScaleX; } }
        private static void Check(bool valid, string message) { results.Add((valid ? "PASS " : "FAIL ") + message); if (!valid) throw new InvalidOperationException(message); }
        private static void Pump(int ms)
        {
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        private static void Capture(string path)
        {
            var view = (FrameworkElement)window.Content; view.UpdateLayout();
            var image = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32); image.Render(view);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var file = File.Create(path)) encoder.Save(file);
        }
        private static string Hash(string path)
        { using (var hash = System.Security.Cryptography.SHA256.Create()) using (var file = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(file)); }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { internal int X, Y; }
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
        [STAThread] private static int Main(string[] args)
        {
            string root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
            string data = Path.Combine(root, "run-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(data);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try {
                var store = new ShelfStore(Path.Combine(data, "FileShelf")); store.Load();
                string file = Path.Combine(data, "设计说明.txt"); File.WriteAllText(file, "Generated integration fixture"); string hash = Hash(file);
                window = new ShelfWindow(store); window.IsHitTestVisible = false;
                typeof(ShelfWindow).GetField("busy", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, true);
                Check(window.State == ShelfState.Hidden && store.Items.Count == 0, "Production shelf never seeds demo files into an empty store");
                store.Add(new[] { file }); window.Restore(); Pump(50); window.UpdateLayout();
                Check(window.State == ShelfState.Tab && Progress == 0, "Production records restore as the floating stack"); Capture(Path.Combine(root, "production-stack.png"));
                var first = Field<WrapPanel>("files").Children[0]; Call("SetState", ShelfState.Open, true); Pump(80); double middle = Progress; Pump(55);
                var outline = Field<RectangleGeometry>("outline");
                if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast) {
                    Check(middle > 0 && middle < .75, "Production reveal remains visible after 80ms");
                    Check(outline.Rect.Width > 156 && outline.Rect.Width < window.Width - 24, "Production reveal has a genuine intermediate boundary"); Capture(Path.Combine(root, "production-opening.png"));
                }
                Pump(350);
                Check(Progress == 1 && window.State == ShelfState.Open, "Production animation finishes open");
                Check(object.ReferenceEquals(first, Field<WrapPanel>("files").Children[0]), "Opening preserves file controls");
                Check(Field<ICollection>("clocks").Count == 0 && !Field<DispatcherTimer>("settle").IsEnabled, "Production animation stops its clocks while idle"); Capture(Path.Combine(root, "production-open.png"));
                Call("SetState", ShelfState.Tab, true); Pump(40); double before = Progress; var bounds = outline.Rect;
                typeof(ShelfWindow).GetField("busy", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, false); Call("HoverStack");
                typeof(ShelfWindow).GetField("busy", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, true);
                Check(Math.Abs(before - Progress) < .001 && Math.Abs(bounds.Width - outline.Rect.Width) < .001, "Production hover reverses from the current boundary"); Pump(350);
                Check(Progress == 1 && window.State == ShelfState.Open, "Re-entry settles open");
                window.ExternalDragStarted(); Check(Progress == 1, "External file drag opens the receiver immediately"); window.ExternalDragEnded(); Field<DispatcherTimer>("collapse").Stop();
                window.ApplyAppearance("Dark"); Capture(Path.Combine(root, "production-dark.png"));
                Check(((SolidColorBrush)Field<System.Windows.Shapes.Path>("backdrop").Fill).Color.R < 80, "Dark appearance reaches the actual shelf"); window.ApplyAppearance("Light");
                Check(((SolidColorBrush)Field<System.Windows.Shapes.Path>("backdrop").Fill).Color.R > 230, "Light appearance reaches the actual shelf");
                bool requested = false; window.ToolRequested += delegate(FileTool tool, string[] paths) { requested = tool == FileTool.Zip && paths.SequenceEqual(new[] { file }); }; Call("LaunchTool", FileTool.Zip, new[] { file });
                Check(requested, "New toolbar routes original file paths to existing tools");
                var own = new WindowInteropHelper(window).Handle; var solid = window.PointToScreen(new Point(30, 40)); var blank = window.PointToScreen(new Point(2, 2));
                Check(WindowFromPoint(new POINT { X = (int)solid.X, Y = (int)solid.Y }) == own, "Visible production panel accepts native hits");
                Check(WindowFromPoint(new POINT { X = (int)blank.X, Y = (int)blank.Y }) != own, "Transparent production margin passes native hits through");
                window.Enabled = false; window.Restore(); Check(!window.IsVisible && window.State == ShelfState.Hidden, "Disabling hides the integrated shelf"); window.Enabled = true; window.Restore();
                Check(window.State == ShelfState.Tab && store.Items.Count == 1, "Re-enabling keeps parked records");
                window.Toggle(); Pump(350);
                Check(window.State == ShelfState.Open && !Field<DispatcherTimer>("collapse").IsEnabled, "Manual opening stays available until the pointer visits and leaves");
                Check(File.Exists(file) && Hash(file) == hash, "Presentation and tool routing preserve original files");
                store.Clear(); window.Restore();
                typeof(ShelfWindow).GetField("busy", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, false);
                Check(window.State == ShelfState.Hidden && !window.IsVisible && Field<TranslateTransform>("edgeSlide").X >= window.Width,
                    "An empty shelf rests completely outside the visible screen");
                var display = System.Windows.Forms.Screen.PrimaryScreen;
                var edge = new System.Drawing.Point(display.Bounds.Right - 1, display.WorkingArea.Top + display.WorkingArea.Height / 2);
                var away = new System.Drawing.Point(display.WorkingArea.Left + 10, edge.Y);
                DateTime now = DateTime.UtcNow;
                Action<System.Drawing.Point, bool, bool, int> poll = delegate(System.Drawing.Point point, bool down, bool blocked, int count) {
                    for (int i = 0; i < count; i++) { now = now.AddMilliseconds(50); window.PollEmptyEdge(point, down, blocked, now); }
                };
                poll(away, false, false, 1); poll(edge, false, false, 4);
                Check(window.State == ShelfState.Hidden, "Passing the right edge briefly does not open an empty shelf");
                poll(edge, false, false, 3);
                Check(window.State == ShelfState.Open && store.Items.Count == 0 && window.IsVisible,
                    "A sustained right-edge hover opens the empty receiver without seeding files");
                Pump(70);
                if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast) {
                    Check(Field<TranslateTransform>("edgeSlide").X > 0 && Field<TranslateTransform>("edgeSlide").X < window.Width + 12,
                        "The actual empty receiver slides in from beyond the right edge");
                    Capture(Path.Combine(root, "production-empty-edge-opening.png"));
                }
                Pump(400);
                Check(Field<TranslateTransform>("edgeSlide").X == 0 && Field<ICollection>("clocks").Count == 0,
                    "Edge opening reaches the final position and stops animation clocks");
                poll(edge, false, false, 12);
                Check(window.State == ShelfState.Open, "Holding at the screen edge keeps the empty shelf open");
                var center = window.PointToScreen(new Point(window.Width / 2, window.Height / 2));
                poll(new System.Drawing.Point((int)center.X, (int)center.Y), false, false, 12);
                Check(window.State == ShelfState.Open, "Moving from the edge into the empty panel keeps it open");
                poll(away, false, false, 5);
                Check(window.State == ShelfState.Open, "Short pointer departures respect the clothing-line delay");
                poll(away, false, false, 6); Pump(70);
                if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
                    Check(window.State == ShelfState.Hidden && window.IsVisible && Field<TranslateTransform>("edgeSlide").X > 0,
                        "An empty shelf slides outward before its native window is hidden");
                Pump(400);
                Check(!window.IsVisible && store.Items.Count == 0, "Empty edge departure leaves no visible tab or placeholder");
                window.ResetEmptyEdge(); poll(edge, true, false, 8);
                Check(window.State == ShelfState.Hidden, "Holding a mouse button does not trigger empty-edge hover");
                window.ResetEmptyEdge(); poll(edge, false, true, 8);
                Check(window.State == ShelfState.Hidden, "A blocked fullscreen context does not trigger empty-edge hover");
                window.ResetEmptyEdge(); poll(new System.Drawing.Point(edge.X, display.WorkingArea.Top + 1), false, false, 8);
                Check(window.State == ShelfState.Hidden, "The top corner remains reserved for the screenshot clothing line");
                window.ResetEmptyEdge(); poll(edge, false, false, 3); now = now.AddSeconds(1); poll(edge, false, false, 1);
                Check(window.State == ShelfState.Hidden, "A pause in polling does not count as edge dwell time");
                poll(edge, false, false, 6); Pump(400); store.Add(new[] { file });
                Check(window.State == ShelfState.Open && !Field<bool>("emptyReveal"), "The first parked file switches to the existing occupied presentation");
                window.Restore(); poll(edge, false, false, 8);
                Check(window.State == ShelfState.Tab && store.Items.Count == 1, "With files present the existing floating stack behavior is unchanged");
                store.Clear(); Check(window.State == ShelfState.Hidden && !window.IsVisible && File.Exists(file),
                    "Removing the last collapsed record hides the shelf and preserves the original");
                File.WriteAllLines(Path.Combine(root, "presentation-results.txt"), results); Console.WriteLine("PASS " + results.Count + " production shelf presentation checks"); return 0;
            }
            catch (Exception ex) { File.WriteAllLines(Path.Combine(root, "presentation-results.txt"), results.Concat(new[] { ex.ToString() })); Console.Error.WriteLine(ex); return 1; }
            finally { if (window != null) window.Close(); app.Shutdown(); }
        }
    }
}
