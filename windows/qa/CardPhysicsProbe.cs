using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Snapline
{
    internal static class CardPhysicsPreview
    {
        [STAThread] private static int Main()
        { return LineGroupsProbe.Main(new[] { "--physics-preview", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-data") }); }
    }

    internal static class CardPhysicsProbe
    {
        private static readonly List<string> checks = new List<string>();
        private static void Check(bool ok, string message)
        { checks.Add((ok ? "PASS " : "FAIL ") + message); if (!ok) throw new InvalidOperationException(message); }
        private static T Field<T>(object obj, string name)
        { return (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj); }
        private static void Pump(int ms)
        {
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
                var child = VisualTreeHelper.GetChild(parent, i); yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
        private static BitmapSource Fixture()
        {
            var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) {
                dc.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, 320, 200));
                dc.DrawRectangle(Brushes.DarkOrange, null, new Rect(20, 20, 90, 160));
                dc.DrawRectangle(Brushes.DarkSlateGray, null, new Rect(130, 50, 170, 24));
            }
            var image = new RenderTargetBitmap(320, 200, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze(); return image;
        }
        private static void Capture(FrameworkElement element, string path)
        {
            element.UpdateLayout();
            var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            image.Render(element); File.WriteAllBytes(path, ImageStore.Png(image));
        }
        private static double Angle(PhysicalCard motion) { return Field<RotateTransform>(motion, "rotation").Angle; }
        private static double Scale(PhysicalCard motion) { return Field<ScaleTransform>(motion, "scale").ScaleX; }
        private static void Settle(CardSpring spring) { for (int i = 0; i < 600; i++) spring.Step(1.0 / 120); }
        [STAThread] private static int Main(string[] args)
        {
            string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
            string root = Path.Combine(output, "run-" + Guid.NewGuid().ToString("N"));
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Controller controller = null;
            try {
                var spring = new CardSpring();
                Check(!spring.Step(1.0 / 60) && spring.Position.Length == 0, "A resting spring performs no ongoing motion");
                spring.Aim(new Vector(4, -3.5)); for (int i = 0; i < 8; i++) spring.Step(1.0 / 120);
                Check(spring.Position.X > 0 && spring.Position.X < 4 && spring.Position.Y < 0, "Force produces progressive rotation and press depth");
                Vector position = spring.Position, velocity = spring.Velocity; spring.Aim(new Vector(-4, 1));
                Check(spring.Position == position && spring.Velocity == velocity, "Reversing a gesture preserves both position and momentum");
                for (int i = 0; i < 40; i++) spring.Step(1.0 / 120);
                Check(spring.Position.X < 0 && spring.Position.Y > 0, "A reversed force changes direction without a discontinuity");
                spring.Aim(new Vector()); Settle(spring);
                Check(spring.Position.Length == 0 && spring.Velocity.Length == 0, "Release reaches exact rest rather than an endless approximation");
                spring.Aim(new Vector(10000, -10000)); Settle(spring);
                Check(spring.Position == new Vector(6, -3.5), "Extreme pointer forces are clamped to safe rotation and scale targets");
                spring.Reset(); spring.Kick(10); spring.Step(1000);
                Check(Math.Abs(spring.Position.X) < 4 && !double.IsNaN(spring.Position.X), "A delayed frame is clamped instead of exploding the simulation");
                for (int i = 0; i < 100; i++) spring.Kick(10000);
                Check(Math.Abs(spring.Velocity.X) <= 90, "Repeated impulses have a bounded velocity");
                spring.Aim(new Vector()); Settle(spring);
                Check(!spring.Step(1.0 / 60) && spring.Position.Length == 0, "Repeated impulses still settle and terminate");
                spring.Aim(new Vector(6, 1)); spring.Step(.05); spring.Reset();
                Check(spring.Position.Length == 0 && spring.Velocity.Length == 0 && !spring.Step(.05), "Reset clears velocity, displacement and targets");

                var seed = new ImageStore(root); seed.Settings.ListenClipboard = false; seed.Settings.CollectionPaused = true;
                seed.Settings.AutoCheckUpdates = false; seed.Settings.FileShelfEnabled = true;
                seed.Settings.HotkeyKey = 0x87; seed.Settings.HotkeyModifiers = 7;
                seed.Settings.CaptureHotkeyKey = 0x86; seed.Settings.CaptureHotkeyModifiers = 7;
                seed.Settings.ClearHotkeyKey = 0x85; seed.Settings.ClearHotkeyModifiers = 7;
                var shot = seed.AddBitmap(Fixture()); string original = shot.Path, digest = shot.Hash;
                controller = new Controller(root, false); controller.Busy = true;
                Field<DispatcherTimer>(controller, "mouse").Stop(); controller.Hover.Dispose();
                var line = controller.Line; line.IsHitTestVisible = false;
                line.Reveal(System.Windows.Forms.Screen.PrimaryScreen); line.PreviewBackdrop(Fixture(), 1); Pump(400);
                var card = ((Canvas)line.Content).Children.OfType<ShotCard>().Single(); var motion = card.Physics;
                Check(!motion.Running && Angle(motion) == 0, "The actual screenshot card starts with no idle physical animation");
                if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast) {
                    motion.PointAt(new Point(card.ActualWidth, 50), false); Pump(90);
                    Check(motion.Running && Angle(motion) > 0 && Angle(motion) < 2.2, "The production screenshot responds gradually to pointer position");
                    double angle = Angle(motion); motion.Pull(-4);
                    Check(Angle(motion) == angle, "Rope tension retargets the card without resetting its angle");
                    Pump(240); Check(Angle(motion) < 0, "The screenshot follows the reversed clothing-line force");
                    line.IsHitTestVisible = true;
                    Check(line.ShotAt(card.PointToScreen(new Point(card.ActualWidth / 2, card.ActualHeight / 2))) == card,
                        "A physically tilted screenshot keeps its hit target attached");
                    line.IsHitTestVisible = false;
                    Capture((FrameworkElement)line.Content, Path.Combine(output, "screenshot-force.png"));
                    motion.Release(); motion.Pull(0); Pump(1800);
                    Check(!motion.Running && Angle(motion) == 0, "The released screenshot settles and unsubscribes its frame loop");
                    card.Sway(5); Pump(70);
                    Check(Angle(motion) > 0, "Copy feedback uses the real impulse spring rather than a replayed keyframe sequence");
                    Pump(1800); Check(!motion.Running && Angle(motion) == 0, "Copy impulse also ends at exact rest");
                    var remove = card.Children.OfType<Button>().Single();
                    remove.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
                    Check(!motion.Running && controller.Store.Items.Count == 1, "The remove control does not press or sway its parent card");
                    motion.PointAt(new Point(card.ActualWidth, 50), false); Pump(30); line.Rebuild(); Pump(30);
                    Check(!motion.Running && !card.IsLoaded, "Changing groups or rebuilding releases the removed screenshot motion");
                }
                var shelf = controller.Files.Window; shelf.IsHitTestVisible = false;
                typeof(Dropline.ShelfWindow).GetField("busy", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(shelf, true);
                controller.Files.Store.Add(new[] { original }); shelf.Restore(); Pump(80);
                var stack = Field<FrameworkElement>(shelf, "anchor");
                var stackedCard = Descendants(stack).OfType<Border>().First(b => PhysicalCard.Get(b) != null);
                var stackMotion = PhysicalCard.Get(stackedCard);
                Check(stackMotion != null && !stackMotion.Running, "The real collapsed file stack has an idle physical attachment");
                if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast) {
                    stackMotion.PointAt(new Point(stackedCard.ActualWidth, 30), false); Pump(90);
                    Check(Angle(stackMotion) > 0 && Scale(stackMotion) > 1, "The collapsed file card leans and lifts under the pointer");
                    stackMotion.Release(); Pump(1800);
                    Check(!stackMotion.Running && Angle(stackMotion) == 0 && Scale(stackMotion) == 1, "The collapsed stack returns to its original resting fan");
                }
                shelf.Toggle(); Pump(90);
                var tile = Field<WrapPanel>(shelf, "files").Children.OfType<Border>().Single(); var tileMotion = PhysicalCard.Get(tile);
                Check(tileMotion != null && !tileMotion.Running, "The actual expanded file tile also starts without an idle loop");
                if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast) {
                    Size size = tile.RenderSize; tileMotion.PointAt(new Point(tile.ActualWidth, 20), true); Pump(120);
                    Check(Angle(tileMotion) > 0 && Scale(tileMotion) < 1 && Scale(tileMotion) > .94, "Pressing the file tile produces bounded tilt and spring compression");
                    double angle = Angle(tileMotion), scale = Scale(tileMotion); tileMotion.PointAt(new Point(0, 20), true);
                    Check(Angle(tileMotion) == angle && Scale(tileMotion) == scale, "Rapid file-tile reversal preserves the current visual state");
                    Pump(240); Check(Angle(tileMotion) < 0, "The expanded tile follows a reversed gesture");
                    Check(tile.RenderSize == size && controller.Files.Store.Items.Count == 1, "Physics changes render transforms without relaying out or reordering files");
                    Capture((FrameworkElement)shelf.Content, Path.Combine(output, "file-tile-press.png"));
                    tileMotion.Release(); Pump(1800);
                    Check(!tileMotion.Running && Angle(tileMotion) == 0 && Scale(tileMotion) == 1, "Release restores the exact file tile transform and stops refreshing");
                    tileMotion.PointAt(new Point(tile.ActualWidth, 20), true); Pump(30); tile.IsEnabled = false;
                    Check(!tileMotion.Running && Angle(tileMotion) == 0 && Scale(tileMotion) == 1, "Disabling a card immediately cancels physical feedback"); tile.IsEnabled = true;
                    tileMotion.PointAt(new Point(tile.ActualWidth, 20), false); Pump(30);
                    shelf.RefreshItems(); Pump(30);
                    Check(!tileMotion.Running && !tile.IsLoaded, "Rebuilding file tiles removes their global rendering subscriptions");
                    tile = Field<WrapPanel>(shelf, "files").Children.OfType<Border>().Single(); tileMotion = PhysicalCard.Get(tile);
                    tileMotion.PointAt(new Point(tile.ActualWidth, 20), false); Pump(30); shelf.Enabled = false; shelf.Restore();
                    Check(!tileMotion.Running && Angle(tileMotion) == 0 && !shelf.IsVisible, "Hiding the shelf resets all active card feedback");
                    shelf.Enabled = true; shelf.Toggle(); Pump(40);
                    tileMotion.PointAt(new Point(tile.ActualWidth, 20), false); Pump(30); shelf.Close(); Pump(20);
                    Check(!tileMotion.Running, "Closing the real shelf during motion releases its frame subscription");
                }
                Check(controller.Store.Items.Count == 1 && File.Exists(original) && ImageStore.Digest(ImageStore.Load(original, 0)) == digest,
                    "Physical gestures preserve screenshot pixels and original files");
                Check(Dropline.ShelfWindow.DragData(controller.Files.Store.Items[0]).GetFileDropList()[0] == original,
                    "The real drag payload still contains the original file path");
                File.WriteAllLines(Path.Combine(output, "physics-results.txt"), checks); Console.WriteLine("PASS " + checks.Count + " card-physics checks; generated data only, no system input."); return 0;
            }
            catch (Exception ex) { File.WriteAllLines(Path.Combine(output, "physics-results.txt"), checks); File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString()); Console.Error.WriteLine(ex); return 1; }
            finally { if (controller != null) controller.Dispose(); app.Shutdown(); }
        }
    }
}
