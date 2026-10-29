using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Snapline
{
    internal static class SettingsFeedbackTests
    {
        private static readonly List<string> checks = new List<string>();
        private static void Check(bool ok, string name)
        { if (!ok) throw new Exception("FAIL: " + name); checks.Add("PASS: " + name); Console.WriteLine("PASS: " + name); }
        private static void Pump(int ms)
        {
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        private static IEnumerable<DependencyObject> Children(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Children(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        private static void Render(SettingsWindow window, string path)
        {
            window.UpdateLayout();
            var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * 1.5), (int)Math.Ceiling(window.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
            image.Render(window); File.WriteAllBytes(path, ImageStore.Png(image));
        }
        private static void Prepare(string root)
        {
            Directory.CreateDirectory(root);
            string settings = Path.Combine(root, "settings.json");
            if (!File.Exists(settings)) File.WriteAllText(settings, "{\"ListenClipboard\":true,\"AutoCheckUpdates\":false,\"CollectionPaused\":true,\"WatchFolder\":null,\"FileShelfEnabled\":true,\"HotkeyKey\":133,\"HotkeyModifiers\":7,\"CaptureHotkeyKey\":132,\"CaptureHotkeyModifiers\":7,\"ClearHotkeyKey\":131,\"ClearHotkeyModifiers\":7,\"Paths\":[]}");
        }
        private static void ProtectPreview(SettingsWindow window)
        {
            window.Part<CheckBox>("StartupToggle").IsEnabled = false;
            window.Part<CheckBox>("StartupToggle").ToolTip = "预览不修改电脑的开机启动。";
            window.Part<Button>("OpenShelf").IsEnabled = false;
            window.Part<Button>("OpenShelf").ToolTip = "请使用已安装的 Snapline 暂存架；此窗口用于查看设置设计。";
        }
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        private static uint FreeKey(uint modifiers)
        {
            for (uint key = 0x87; key >= 0x7c; key--) {
                if (!Native.RegisterHotKey(IntPtr.Zero, 190, modifiers | 0x4000, key)) continue;
                Native.UnregisterHotKey(IntPtr.Zero, 190); return key;
            }
            throw new Exception("No free test shortcut");
        }
        private static void CheckFeedback(Controller controller, string root)
        {
            var legacyRoot = Path.Combine(root, "legacy"); Directory.CreateDirectory(legacyRoot);
            File.WriteAllText(Path.Combine(legacyRoot, "settings.json"), "{\"Paths\":[]}");
            var legacy = new ImageStore(legacyRoot).Settings;
            Check(legacy.ClearHotkeyKey == 8 && legacy.ClearHotkeyModifiers == 3 && legacy.RemoveOnBlankDrop,
                "Legacy settings without the preference get Ctrl+Alt+Backspace and default-on blank-drop removal");
            Check(new Settings().RemoveOnBlankDrop, "Fresh installations enable blank-drop removal");
            File.WriteAllText(Path.Combine(legacyRoot, "settings.json"), "{\"Paths\":[],\"RemoveOnBlankDrop\":false}");
            Check(!new ImageStore(legacyRoot).Settings.RemoveOnBlankDrop, "An explicitly disabled blank-drop preference survives an upgrade");
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen()) dc.DrawRectangle(Brushes.SteelBlue, null, new Rect(0, 0, 140, 90));
            var image = new RenderTargetBitmap(140, 90, 96, 96, PixelFormats.Pbgra32); image.Render(drawing); image.Freeze();
            var shot = controller.Store.AddBitmap(image); string original = shot.Path;
            var digest = File.ReadAllBytes(original);
            var data = controller.Data(shot);
            var preferred = (MemoryStream)data.GetData("Preferred DropEffect", false);
            Check(BitConverter.ToInt32(preferred.ToArray(), 0) == 1 && data.GetDataPresent(DataFormats.FileDrop) && data.GetDataPresent("PNG"),
                "Drag payload prefers Copy and offers both image and file formats");
            var card = new ShotCard(shot, controller, null);
            controller.Store.Settings.RemoveOnBlankDrop = false;
            card.CompleteDrag(DragDropEffects.None, true, false, false);
            Check(controller.Store.Items.Contains(shot), "Disabling blank-drop removal preserves the card");
            controller.Store.Settings.RemoveOnBlankDrop = true;
            card.CompleteDrag(DragDropEffects.Copy, true, false, false);
            Check(controller.Store.Items.Contains(shot), "Successful receiver keeps the card on the line");
            card.CompleteDrag(DragDropEffects.None, true, true, false);
            Check(controller.Store.Items.Contains(shot), "Escape cancellation never removes the card");
            card.CompleteDrag(DragDropEffects.None, false, false, false);
            Check(controller.Store.Items.Contains(shot), "An aborted drag without mouse release preserves the card");
            card.CompleteDrag(DragDropEffects.None, true, false, true);
            Check(controller.Store.Items.Contains(shot), "Releasing over the line preserves the card");
            card.CompleteDrag(DragDropEffects.None, true, false, false);
            Check(!controller.Store.Items.Contains(shot) && File.ReadAllBytes(original).SequenceEqual(digest),
                "Enabled blank drop detaches the record and leaves the original unchanged");
            shot = controller.Store.AddFile(original, true);
            controller.Store.Settings.RemoveOnBlankDrop = false;
            uint display = controller.Sink.HotkeyKey, displayFlags = controller.Sink.HotkeyModifiers;
            uint capture = controller.Sink.CaptureHotkeyKey, captureFlags = controller.Sink.CaptureHotkeyModifiers;
            uint clear = FreeKey(6); string error;
            Check(controller.ChangeHotkeys(display, displayFlags, capture, captureFlags, clear, 6, out error) && controller.Sink.ClearHotkeyRegistered,
                "A custom clear shortcut registers and persists alongside the two existing commands");
            Check(new ImageStore(root).Settings.ClearHotkeyKey == clear, "Reload restores the clear shortcut");
            int displayId = controller.Sink.HotkeyId, captureId = controller.Sink.CaptureHotkeyId, clearId = controller.Sink.ClearHotkeyId;
            uint occupied = FreeKey(6);
            Check(Native.RegisterHotKey(IntPtr.Zero, 191, 0x4006, occupied), "Conflict fixture registers an occupied key");
            try {
                Check(!controller.ChangeHotkeys(FreeKey(7), 7, capture, captureFlags, occupied, 6, out error) && error.StartsWith("清空截图栏") &&
                    controller.Sink.HotkeyId == displayId && controller.Sink.CaptureHotkeyId == captureId && controller.Sink.ClearHotkeyId == clearId,
                    "A clear-key conflict rolls back all three native bindings");
            } finally { Native.UnregisterHotKey(IntPtr.Zero, 191); }
            var stateFile = Path.Combine(root, "settings.json");
            using (var locked = new FileStream(stateFile, FileMode.Open, FileAccess.Read, FileShare.None)) {
                Check(!controller.ChangeHotkeys(FreeKey(7), 7, capture, captureFlags, FreeKey(6), 6, out error) &&
                    controller.Sink.HotkeyId == displayId && controller.Sink.ClearHotkeyId == clearId && controller.Store.Settings.ClearHotkeyKey == clear,
                    "A settings write failure preserves both native bindings and saved candidates");
                bool failed = false; try { controller.Store.Clear(); } catch (IOException) { failed = true; }
                Check(failed && controller.Store.Items.Contains(shot), "Failed clear preserves screenshot history in memory");
            }
            controller.Busy = true;
            SendMessage(controller.Sink.Handle, Native.WM_HOTKEY, new IntPtr(clearId), IntPtr.Zero);
            Check(controller.Store.Items.Contains(shot), "Clear is suppressed during a drag or modal operation");
            controller.Busy = false;
            SendMessage(controller.Sink.Handle, Native.WM_HOTKEY, new IntPtr(clearId), IntPtr.Zero);
            Check(controller.Store.Items.Count == 0 && File.ReadAllBytes(original).SequenceEqual(digest) && new ImageStore(root).Items.Count == 0,
                "A native clear message removes every card, persists empty history and retains the original");
            Check(controller.ChangeHotkeys(clear, 6, display, displayFlags, capture, captureFlags, out error) &&
                controller.Sink.HotkeyId == clearId && controller.Sink.CaptureHotkeyId == displayId && controller.Sink.ClearHotkeyId == captureId,
                "Three-way shortcut rotation reuses the existing registrations atomically");
            Check(controller.ChangeHotkeys(display, displayFlags, capture, captureFlags, clear, 6, out error), "Original test bindings can be restored");
            controller.Line.Width = 1000; controller.Line.Rebuild();
            var rope = (PathGeometry)((Canvas)controller.Line.Content).Children.OfType<System.Windows.Shapes.Path>().First().Data;
            var points = ((PolyLineSegment)rope.Figures[0].Segments[0]).Points;
            double rest = points[15].Y;
            controller.Line.NudgeRope(); Pump(210);
            Check(!SystemParameters.ClientAreaAnimation || Math.Abs(points[15].Y - rest) > .001, "Rope feedback changes the actual native line geometry");
            Pump(2000);
            controller.Line.Tuck();
            Check(points[15].Y == rest && !controller.Line.RopeAnimating, "Spring feedback settles exactly and unsubscribes the render loop");
            CheckRopePhysics();
        }

        private static void CheckRopePhysics()
        {
            var rope = new RopePhysics();
            Check(!rope.Step(1.0 / 60), "An idle string needs no frame loop");
            rope.Pull(.5, new Vector(160, 130));
            for (int i = 0; i < 90; i++) rope.Step(1.0 / 120);
            var right = rope.Sample(.5);
            Check(right.X > 0 && right.Y > 0 && right.X < 12 && right.Y < 18, "Drag tension follows both axes with bounded displacement");
            Check(rope.Sample(0).Length == 0 && rope.Sample(1).Length == 0, "Both rope endpoints remain pinned during a drag");
            Check(rope.Sample(.4).Y > 0 && rope.Sample(.4).Y < right.Y && rope.Sample(.1).Y < rope.Sample(.4).Y,
                "Neighbouring attachments respond while distant sections move less");
            rope.Pull(.5, new Vector(-160, -130));
            Check(rope.Sample(.5) == right, "Reversing a drag preserves the current position without snapping");
            for (int i = 0; i < 90; i++) rope.Step(1.0 / 120);
            Check(rope.Sample(.5).X < 0 && rope.Sample(.5).Y < 0, "Reversing direction changes the live pull direction");
            var before = rope.Sample(.5); rope.Release();
            Check(rope.Sample(.5) == before, "Release preserves position and velocity for a continuous spring return");
            bool active = true; int frames = 0;
            while (active && frames++ < 600) active = rope.Step(1.0 / 120);
            Check(!active && rope.Sample(.5).Length == 0, "Released string reaches exact rest within five seconds");
            rope.Pull(.02, new Vector(double.MaxValue, double.MaxValue));
            for (int i = 0; i < 120; i++) rope.Step(1.0 / 120);
            Check(!double.IsNaN(rope.Sample(.02).X) && rope.Sample(.02).Length < 25, "Extreme pointer movement stays finite and bounded near an endpoint");
            rope.Reset(); rope.Kick(); rope.Step(100);
            Check(rope.Sample(.5).Length < 2, "A delayed frame is clamped to avoid a simulation explosion");
            rope.Reset(); Check(!rope.Step(.016) && rope.Sample(.5).Length == 0, "Reset cancels pending drag force and motion immediately");
        }

        private static TranslateTransform Selection(SettingsWindow window, string name)
        { return (TranslateTransform)window.Part<Border>(name).RenderTransform; }

        private static void CheckMotion(SettingsWindow window, Controller controller, string output)
        {
            window.ShowPage("General"); Pump(220);
            Check(window.Motion.ActiveCount == 0, "Initial and programmatic settings display does not start decorative animation clocks");
            var toggle = window.Part<CheckBox>("CollectToggle");
            var track = (Border)toggle.Template.FindName("Track", toggle);
            var thumb = (System.Windows.Shapes.Ellipse)toggle.Template.FindName("Thumb", toggle);
            var move = (TranslateTransform)thumb.RenderTransform;
            double travel = track.ActualWidth - thumb.ActualWidth - 6;
            Check(toggle.IsChecked == true && Math.Abs(move.X - travel) < .1, "An initially checked toggle paints its thumb at the checked endpoint");
            var before = File.ReadAllBytes(Path.Combine(controller.Store.Root, "settings.json"));
            window.Motion.SetPointerMode(true); toggle.IsChecked = false; Pump(40);
            Check(move.X > .1 && move.X < travel - .1 && window.Motion.ActiveCount > 0, "Pointer toggle produces a real intermediate thumb position");
            Render(window, Path.Combine(output, "motion-switch-mid.png"));
            for (int i = 0; i < 12; i++) { toggle.IsChecked = i % 2 == 0; Pump(7); }
            Check(window.Motion.ActiveCount <= 2, "Repeated toggle input replaces its clocks instead of queuing old clicks");
            Pump(230);
            Check(toggle.IsChecked == false && move.X == 0 && window.Motion.ActiveCount == 0, "Rapid toggle reversal settles at the latest state and releases every clock");
            Check(File.ReadAllBytes(Path.Combine(controller.Store.Root, "settings.json")).SequenceEqual(before), "Animated preference changes remain unsaved until Save");
            window.Motion.SetPointerMode(false); toggle.IsChecked = true;
            Check(move.X == travel && window.Motion.ActiveCount == 0, "Keyboard-mode toggle updates its visual state without movement or delay");

            var button = window.Part<Button>("SaveSettings"); button.ApplyTemplate();
            var chrome = (Border)button.Template.FindName("Chrome", button);
            var scale = (ScaleTransform)chrome.RenderTransform;
            var wash = (Border)button.Template.FindName("HoverWash", button);
            window.Motion.SetPointerMode(true);
            button.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) {
                RoutedEvent = UIElement.MouseEnterEvent });
            Pump(35);
            Check(wash.Opacity > 0 && wash.Opacity < .06, "The actual button hover event starts a gradual native overlay transition");
            scale = (ScaleTransform)chrome.RenderTransform;
            window.Motion.UpdateButton(button, true, true); Pump(35);
            Check(scale.ScaleX > .98 && scale.ScaleX < 1 && scale.ScaleY == scale.ScaleX, "Button press feedback has a real intermediate scale on both axes");
            window.Motion.UpdateButton(button, true, false); Pump(180);
            Check(scale.ScaleX == 1 && scale.ScaleY == 1 && window.Motion.ActiveCount == 0, "Releasing a pressed button restores its size and removes animation clocks");
            window.Motion.UpdateButton(button, true, true); Pump(20); button.IsEnabled = false;
            Check(scale.ScaleX == 1 && scale.ScaleY == 1 && wash.Opacity == 0 && window.Motion.ActiveCount == 0, "Disabling a button cancels press and hover visuals immediately");
            button.IsEnabled = true;
            window.Motion.SetPointerMode(true); window.Motion.UpdateButton(button, true, true); Pump(20);
            window.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(window), Environment.TickCount, System.Windows.Input.Key.Tab) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
            Check(scale.ScaleX == .98 && window.Motion.ActiveCount == 0, "An actual preview-key event stops pending pointer clocks immediately");
            window.Motion.UpdateButton(button, false, false);

            window.Part<RadioButton>("ThemeLight").IsChecked = true; window.Motion.UpdateSelections();
            window.Motion.SetPointerMode(true); window.Part<RadioButton>("ThemeSystem").IsChecked = true; Pump(45);
            var theme = Selection(window, "ThemeSelection");
            var themeHost = window.Part<Grid>("ThemeTabs");
            double target = window.Part<RadioButton>("ThemeSystem").TranslatePoint(new Point(), themeHost).X;
            Check(theme.X > 0 && theme.X < target, "Theme selection uses one moving native selection plate");
            foreach (string mode in new[] { "Dark", "Light", "System", "Dark", "System" }) { window.Part<RadioButton>("Theme" + mode).IsChecked = true; Pump(9); }
            Pump(230);
            Check(Math.Abs(theme.X - target) < .1 && window.Motion.ActiveCount == 0, "Rapid theme changes finish under the latest selected segment with no queued motion");
            var nav = Selection(window, "NavSelection"); window.ShowPage("Storage"); Pump(35);
            Check(window.Part<StackPanel>("StoragePage").Visibility == Visibility.Visible && nav.Y > 0 && window.Motion.ActiveCount > 0,
                "Pointer category selection makes the destination interactive while its highlight moves");
            Render(window, Path.Combine(output, "motion-page-mid.png"));
            foreach (string page in new[] { "Capture", "About", "Keys", "Shelf", "General" }) { window.ShowPage(page); Pump(8); }
            Pump(240);
            var general = window.Part<StackPanel>("GeneralPage");
            Check(Children(window.Part<Grid>("Pages")).OfType<StackPanel>().Count(page => page.Name.EndsWith("Page") && page.Visibility == Visibility.Visible) == 1 &&
                general.Opacity == 1 && ((TranslateTransform)general.RenderTransform).Y == 0 && window.Motion.ActiveCount == 0,
                "Rapid page changes leave exactly one fully visible page and no active clocks");
            window.Motion.SetPointerMode(false); window.ShowPage("Keys");
            Check(window.Part<StackPanel>("KeysPage").Opacity == 1 && window.Motion.ActiveCount == 0,
                "Keyboard page selection remains immediate and does not start an entrance animation");
            Check(window.Part<Border>("ThemeSelection").Width == window.Part<RadioButton>("ThemeSystem").ActualWidth &&
                Math.Abs(window.Part<RadioButton>("NavKeys").TranslatePoint(new Point(), window.Part<Grid>("NavigationTabs")).Y - nav.Y) < .1,
                "Shared selection plates align with the real native hit targets");
            window.ShowPage("General");
            var reduced = new SettingsWindow(controller, delegate { return false; });
            try {
                reduced.ShowActivated = false; reduced.Show(); Pump(70);
                reduced.Motion.SetPointerMode(true);
                reduced.Part<CheckBox>("CollectToggle").IsChecked = false;
                reduced.Part<RadioButton>("ThemeDark").IsChecked = true;
                reduced.ShowPage("Capture");
                Check(reduced.Motion.ActiveCount == 0 && reduced.Part<StackPanel>("CapturePage").Opacity == 1,
                    "Reduced-motion policy keeps pointer changes immediate without altering Windows settings");
                var reducedThumb = (System.Windows.Shapes.Ellipse)reduced.Part<CheckBox>("CollectToggle").Template.FindName("Thumb", reduced.Part<CheckBox>("CollectToggle"));
                Check(((TranslateTransform)reducedThumb.RenderTransform).X == 0, "Reduced-motion toggle paints the correct endpoint immediately");
                reduced.ShowPage("Shelf"); reduced.UpdateLayout(); Pump(40);
                var shelfToggle = reduced.Part<CheckBox>("ShelfToggle");
                var shelfTrack = (Border)shelfToggle.Template.FindName("Track", shelfToggle);
                var shelfThumb = (System.Windows.Shapes.Ellipse)shelfToggle.Template.FindName("Thumb", shelfToggle);
                Check(shelfToggle.IsChecked == true && Math.Abs(((TranslateTransform)shelfThumb.RenderTransform).X -
                    (shelfTrack.ActualWidth - shelfThumb.ActualWidth - 6)) < .1,
                    "A previously collapsed page displays its checked toggle at the correct measured endpoint");

            }
            finally { reduced.Close(); }
            var closing = new SettingsWindow(controller, delegate { return true; }) { ShowActivated = false };
            closing.Show(); Pump(60); closing.Motion.SetPointerMode(true); closing.Part<RadioButton>("ThemeDark").IsChecked = true;
            closing.Close();
            Check(closing.Motion.ActiveCount == 0, "Closing during motion releases every active animation clock");
            window.Motion.SetPointerMode(false); window.Part<RadioButton>("ThemeLight").IsChecked = true;
            Render(window, Path.Combine(output, "motion-light-final.png"));
            window.Part<RadioButton>("ThemeDark").IsChecked = true;
            Render(window, Path.Combine(output, "motion-dark-final.png"));
        }

        internal static int Run(string output)
        {
            bool qa = true; string[] args = { "--qa", output };
            string root = qa ? Path.Combine(args[1], "run-" + Guid.NewGuid().ToString("N")) : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PreviewData");
            Prepare(root); var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Controller controller = null; SettingsWindow window = null;
            try {
                controller = new Controller(root, false);
                CheckFeedback(controller, root);
                controller.Busy = true;
                var constructClock = Stopwatch.StartNew();
                window = new SettingsWindow(controller, delegate { return true; }); ProtectPreview(window);
                double constructed = constructClock.Elapsed.TotalMilliseconds;
                window.Title = "Snapline · 设置设计预览";
                if (!qa) { window.ShowDialog(); return 0; }
                output = Path.GetFullPath(output); Directory.CreateDirectory(output);
                var clock = Stopwatch.StartNew(); double painted = -1;
                window.ContentRendered += delegate { if (painted < 0) painted = clock.Elapsed.TotalMilliseconds; };
                window.ShowActivated = false; window.Show(); Pump(100);
                Check(painted >= 0 && painted < 500, "Solid settings paint within 500 ms without desktop capture or diffusion");
                Console.WriteLine("First paint: " + painted.ToString("F1") + " ms");
                Check(constructed + painted < 800, "Settings construction and first paint together stay below the original 800 ms local budget");
                File.WriteAllText(Path.Combine(output, "settings-timing.txt"), "Construct: " + constructed.ToString("F1") + " ms\nFirst paint after Show: " + painted.ToString("F1") + " ms\nTotal: " + (constructed + painted).ToString("F1") + " ms\nProcess startup excluded; local isolated-data measurement.");
                Check(!Children(window.View).OfType<System.Windows.Shapes.Rectangle>().Any(element => element.Effect != null), "The settings content contains no backdrop shader or blur effect");
                foreach (string theme in new[] { "Light", "Dark" }) {
                    window.Part<RadioButton>("Theme" + theme).IsChecked = true; Pump(35);
                    foreach (string page in new[] { "General", "Capture", "Keys", "Storage", "Shelf", "About" }) {
                        window.ShowPage(page); window.UpdateLayout(); Pump(30);
                        Check(window.Part<StackPanel>(page + "Page").Visibility == Visibility.Visible && window.Part<RadioButton>("Nav" + page).IsChecked == true, theme + " / " + page + " navigation updates selected state and page");
                        var pages = window.Part<Grid>("Pages");
                        Check(Children(window.Part<StackPanel>(page + "Page")).OfType<FrameworkElement>().Where(element => element is TextBlock || element is Button || element is CheckBox || element is TextBox).All(element => {
                            var bounds = element.TransformToAncestor(pages).TransformBounds(new Rect(element.RenderSize));
                            return bounds.Left >= -1 && bounds.Right <= pages.ActualWidth + 1;
                        }), theme + " / " + page + " labels and controls stay within the content column");
                        Render(window, Path.Combine(output, theme.ToLowerInvariant() + "-" + page.ToLowerInvariant() + ".png"));
                    }
                    window.ShowPage("General"); window.UpdateLayout();
                    var toggle = window.Part<CheckBox>("CollectToggle"); toggle.ApplyTemplate();
                    var ring = (Border)toggle.Template.FindName("Ring", toggle);
                    Check(ring.InputHitTest(new Point(4, ring.ActualHeight / 2)) != null && ring.InputHitTest(new Point(ring.ActualWidth - 4, ring.ActualHeight / 2)) != null, theme + " toggle accepts clicks at both ends");
                    var ink = (SolidColorBrush)window.View.FindResource("Ink"); var paper = (SolidColorBrush)window.View.FindResource("Paper");
                    Check(Math.Abs(ink.Color.R - paper.Color.R) > 170, theme + " text and background keep strong contrast");
                }
                CheckMotion(window, controller, output);
                window.Part<RadioButton>("ThemeSystem").IsChecked = true;
                Check(window.Part<RadioButton>("ThemeSystem").IsChecked == true, "System appearance is selectable alongside light and dark");
                var settingsPath = Path.Combine(root, "settings.json"); var before = File.ReadAllBytes(settingsPath);
                window.Part<CheckBox>("CollectToggle").IsChecked = false;
                window.Part<RadioButton>("ThemeDark").IsChecked = true; window.Close();
                Check(File.ReadAllBytes(settingsPath).SequenceEqual(before), "Closing without saving preserves settings and appearance");
                window = new SettingsWindow(controller); ProtectPreview(window);
                var saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                saveTimer.Tick += delegate {
                    saveTimer.Stop(); window.Part<RadioButton>("ThemeDark").IsChecked = true;
                    window.Part<CheckBox>("CollectToggle").IsChecked = false;
                    window.Part<CheckBox>("BlankDropToggle").IsChecked = true;
                    window.SetClearCandidate(0x81, 7);
                    window.Part<Button>("SaveSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                saveTimer.Start(); window.ShowDialog(); saveTimer.Stop();
                var restored = new ImageStore(root).Settings;
                Check(restored.Appearance == "Dark" && !restored.ListenClipboard && restored.RemoveOnBlankDrop && restored.ClearHotkeyKey == 0x81, "Saving persists appearance, collection, blank-drop option and clear shortcut");
                window = new SettingsWindow(controller); ProtectPreview(window); window.ShowActivated = false; window.Show(); Pump(60);
                Check(window.Part<RadioButton>("ThemeDark").IsChecked == true, "Reopening restores the saved appearance");
                window.ShowPage("Keys"); window.Part<TextBox>("HotkeyInput").Focus();
                Check(window.SetCandidate(0x78, 6) && window.Part<TextBox>("HotkeyInput").Text.Contains("F9"), "Shortcut recording keeps the existing native key-binding logic");
                Check(!window.SetClearCandidate(0x7b, 0) && !window.Part<Button>("SaveSettings").IsEnabled, "Invalid clear shortcut disables Save with inline feedback");
                window.SetClearCandidate(0x78, 6);
                Check(!window.Part<Button>("SaveSettings").IsEnabled, "Clear shortcut cannot duplicate the show shortcut");
                window.SetClearCandidate(controller.Sink.ClearHotkeyKey, controller.Sink.ClearHotkeyModifiers);
                Check(window.Part<Button>("SaveSettings").IsEnabled, "Restoring an independent clear shortcut enables Save");
                Render(window, Path.Combine(output, "light-keys-recording.png"));
                window.Close();
                File.WriteAllLines(Path.Combine(output, "settings-design-results.txt"), checks);
                Console.WriteLine("PASS " + checks.Count + " settings design checks"); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); if (!qa) MessageBox.Show(ex.Message, "设置预览"); return 1; }
            finally { if (window != null) window.Close(); if (controller != null) controller.Dispose(); app.Shutdown(); }
        }
    }
}
