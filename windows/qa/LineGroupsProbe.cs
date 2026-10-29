using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Snapline
{
    internal static class LineGroupsProbe
    {
        private static readonly List<string> results = new List<string>();
        private static void Check(bool ok, string text)
        { results.Add((ok ? "PASS " : "FAIL ") + text); if (!ok) throw new InvalidOperationException(text); }
        private static void Pump(int ms)
        {
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        private static BitmapSource Fixture(int number)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb((byte)(40 + number * 7 % 160), (byte)(70 + number * 11 % 140), (byte)(60 + number * 17 % 150))), null, new Rect(0, 0, 320, 200));
                dc.DrawText(new FormattedText("SNAPLINE\n" + number.ToString("00"), System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 42, Brushes.White, 1), new Point(28, 27));
            }
            var image = new RenderTargetBitmap(320, 200, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze(); return image;
        }
        private static ShotCard[] Cards(LineWindow line) { return ((Canvas)line.Content).Children.OfType<ShotCard>().ToArray(); }
        private static void Wheel(LineWindow line, int delta)
        { line.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = UIElement.PreviewMouseWheelEvent }); }
        private static void Capture(LineWindow line, string path)
        {
            line.UpdateLayout();
            var image = new RenderTargetBitmap((int)Math.Ceiling(line.ActualWidth), (int)LineWindow.PanelHeight, 96, 96, PixelFormats.Pbgra32);
            image.Render((Visual)line.Content); File.WriteAllBytes(path, ImageStore.Png(image));
        }
        private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        [STAThread] internal static int Main(string[] args)
        {
            bool physicsPreview = args.Length == 2 && args[0] == "--physics-preview";
            bool preview = args.Length == 0 || physicsPreview || (args.Length == 2 && args[0] == "--preview");
            string output = args.Length == 0 ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-data") : Path.GetFullPath(args[preview ? 1 : 0]);
            Directory.CreateDirectory(output);
            string root = Path.Combine(output, "run-" + Guid.NewGuid().ToString("N"));
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Controller controller = null;
            try {
                var seed = new ImageStore(root);
                seed.Settings.ListenClipboard = false; seed.Settings.CollectionPaused = true;
                seed.Settings.AutoCheckUpdates = false; seed.Settings.FileShelfEnabled = false;
                seed.Settings.HotkeyKey = 0x87; seed.Settings.HotkeyModifiers = 7;
                seed.Settings.CaptureHotkeyKey = 0x86; seed.Settings.CaptureHotkeyModifiers = 7;
                seed.Settings.ClearHotkeyKey = 0x85; seed.Settings.ClearHotkeyModifiers = 7;
                for (int i = 1; i <= 9; i++) seed.AddBitmap(Fixture(i));
                Check(seed.Items.Count == 9, "Nine shots fill the first group");
                seed.AddBitmap(Fixture(10));
                Check(seed.Items.Count == 10 && seed.Settings.Paths.Count == 10, "The tenth shot retains the first nine records");
                for (int i = 11; i <= 19; i++) seed.AddBitmap(Fixture(i));
                var reload = new ImageStore(root);
                Check(reload.Items.Count == 19 && reload.Items.Select(s => s.Path).SequenceEqual(seed.Items.Select(s => s.Path)), "Restart restores every group in order");
                Check(reload.Items.All(s => s.Thumbnail == null), "Restoring history does not eagerly decode thumbnail caches");
                controller = new Controller(root, false);
                ((DispatcherTimer)typeof(Controller).GetField("mouse", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller)).Stop();
                controller.Hover.Dispose();
                var line = controller.Line; line.Title = "Snapline · 截图分组独立预览";
                line.Reveal(System.Windows.Forms.Screen.PrimaryScreen);
                if (!preview) line.PreviewBackdrop(Fixture(0), 1);
                Pump(380);
                if (preview) {
                    if (physicsPreview) {
                        foreach (var item in controller.Store.Items.Take(3)) controller.Files.Store.Add(new[] { item.Path });
                        controller.Store.Settings.FileShelfEnabled = true; controller.Files.Apply();
                    }
                    var panel = new StackPanel { Margin = new Thickness(22) };
                    panel.Children.Add(new TextBlock { Text = physicsPreview ? "顶部晾衣绳 ＋ 右侧暂存架 · 物理反馈" : "截图分组预览 · 每组 9 张", FontSize = 20, FontWeight = FontWeights.SemiBold });
                    panel.Children.Add(new TextBlock { Text = physicsPreview
                        ? "左右轻扫图片，按下再松开，观察受力摆动和回弹；右侧悬停展开后也可试。\n顶部仍是 9 张一组，玻璃沿用桌面背景。这些都是生成图片，关闭即可退出。"
                        : "在顶部图片或分组条上滚动：向上看旧图，向下看新图。\n也可点击 ‹ / › / 最新。这里只使用生成图片，不更改已安装软件和原截图。", Margin = new Thickness(0, 12, 0, 16) });
                    var actions = new StackPanel { Orientation = Orientation.Horizontal };
                    actions.Children.Add(Ui.Button("增加 9 张测试图", delegate { for (int i = 0; i < 9; i++) controller.Store.AddBitmap(Fixture(controller.Store.Items.Count + 1)); }));
                    if (physicsPreview) {
                        actions.Children.Add(Ui.Button("右侧卡叠", delegate { controller.Files.Window.Restore(); }));
                        actions.Children.Add(Ui.Button("右侧面板", delegate { if (controller.Files.Window.State != Dropline.ShelfState.Open) controller.Files.Window.Toggle(); }));
                    }
                    actions.Children.Add(Ui.Button("关闭预览", delegate { app.Shutdown(); })); panel.Children.Add(actions);
                    var host = new Window { Title = physicsPreview ? "Snapline · 两处物理交互预览（独立数据）" : "Snapline · 截图分组预览（独立数据）", Width = 680, Height = 230,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = panel };
                    host.Closed += delegate { app.Shutdown(); }; host.Show(); app.Run(); return 0;
                }
                line.Width = 1280; line.Rebuild(); Pump(70);
                Check(line.GroupCount == 3 && line.GroupIndex == 2 && Cards(line).Length == 1, "Shot nineteen opens the latest third group");
                Check(!line.NewerButton.IsEnabled && line.OlderButton.IsEnabled, "Navigation states identify the newest group");
                line.UpdateLayout();
                Check(line.IsCardAt(line.GroupText.PointToScreen(new Point(line.GroupText.ActualWidth / 2, line.GroupText.ActualHeight / 2))), "The whole group indicator accepts input for wheel navigation");
                Check(controller.Store.Items.Count(s => s.Thumbnail != null) == 1, "Only the active group holds decoded thumbnail caches");
                Capture(line, Path.Combine(output, "latest-group.png"));
                Check(line.Browse(-1) && line.GroupIndex == 1 && Cards(line).Length == 9, "Browsing older reveals all nine shots in the second group");
                Pump(30);
                var moving = ((TransformGroup)Cards(line)[0].RenderTransform).Children.OfType<TranslateTransform>().Last();
                Check(!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast || (moving.X < 0 && moving.X > -24), "Group navigation has a real intermediate sliding frame");
                Capture(line, Path.Combine(output, "group-sliding.png")); Pump(220);
                Check(!moving.HasAnimatedProperties && moving.X == 0, "Group motion stops its clock after completion");
                Check(Cards(line).Select(c => c.Shot.Path).SequenceEqual(controller.Store.Items.Skip(9).Take(9).Select(s => s.Path)), "Second group contains exactly shots ten through eighteen");
                Check(Cards(line).All(c => Canvas.GetLeft(c) >= 0 && Canvas.GetLeft(c) + c.Width * c.LayoutTransform.Value.M11 <= line.Width), "All nine cards fit the normal-width row");
                Check(Cards(line).All(c => line.ShotAt(c.PointToScreen(new Point(c.ActualWidth / 2, c.ActualHeight / 2))) == c), "Scaled screenshot cards preserve native hit targets");
                Check(controller.Store.Items.Count(s => s.Thumbnail != null) == 9 && controller.Store.Items[18].Thumbnail == null, "Changing groups releases the previous group cache");
                Capture(line, Path.Combine(output, "nine-shot-group.png"));
                Click(line.OlderButton); Pump(220);
                Check(line.GroupIndex == 0 && !line.OlderButton.IsEnabled && Cards(line)[0].Shot == controller.Store.Items[0], "The oldest group remains reachable with a disabled older boundary");
                Check(!line.Browse(-1), "Scrolling beyond the oldest group does not wrap or lose records");
                Click(line.NewerButton); Pump(210);
                Check(line.GroupIndex == 1, "The newer button advances one group");
                Click(line.NewestButton);
                Check(line.GroupIndex == 2, "The latest button returns directly to the newest group");
                Wheel(line, 120); Pump(210);
                Check(line.GroupIndex == 1, "The real routed wheel handler navigates toward older screenshots");
                Wheel(line, -120); Pump(210);
                Check(line.GroupIndex == 2, "Reverse wheel input returns toward newer screenshots");
                Pump(270); Wheel(line, 60);
                Check(line.GroupIndex == 2, "High-resolution wheel input accumulates instead of skipping a group");
                Wheel(line, 60); Pump(210);
                Check(line.GroupIndex == 1, "Two half-notch inputs move exactly one group");
                controller.Busy = true;
                Check(!line.Browse(-1) && line.GroupIndex == 1, "Dragging or modal operations block group navigation"); controller.Busy = false;
                line.Width = 400; line.Rebuild(); Pump(80);
                Check(Cards(line).Length == 1 && line.GroupText.Text.Contains("/9"), "Narrow displays show a readable card and its range within the group");
                var reachable = new HashSet<string>();
                do { foreach (var card in Cards(line)) reachable.Add(card.Shot.Path); } while (line.Browse(-1));
                Click(line.NewestButton);
                do { foreach (var card in Cards(line)) reachable.Add(card.Shot.Path); } while (line.Browse(-1));
                Check(reachable.Count == 19, "Every screenshot remains reachable on a narrow display");
                Check(line.OlderButton.TranslatePoint(new Point(), line).X >= 0 && line.NewestButton.TranslatePoint(new Point(line.NewestButton.ActualWidth, 0), line).X <= line.Width, "Narrow group controls stay within the window");
                Capture(line, Path.Combine(output, "narrow-group.png"));
                line.Width = 1280; line.Rebuild(); Pump(60);
                controller.Store.AddBitmap(Fixture(20));
                Check(line.GroupIndex == 2 && Cards(line).Length == 2, "New capture returns from history to the latest group");
                controller.Store.Remove(controller.Store.Items.Last(), false);
                controller.Store.Remove(controller.Store.Items.Last(), false);
                Check(line.GroupCount == 2 && line.GroupIndex == 1 && Cards(line).Length == 9, "Removing the last shot of the newest group clamps to an existing group");
                line.Browse(-1); Pump(210);
                string original = controller.Store.Items[0].Path; controller.Store.Remove(controller.Store.Items[0], false);
                Check(line.GroupIndex == 0 && File.Exists(original), "Removing a history card preserves the source and current group");
                for (int i = 21; i <= 104; i++) controller.Store.AddBitmap(Fixture(i));
                Check(controller.Store.Items.Count == 101 && controller.Store.Items.Count(s => s.Thumbnail != null) <= 9, "A hundred-shot history retains all records with at most nine cached thumbnails");
                var finalReload = new ImageStore(root);
                Check(finalReload.Items.Count == 101 && finalReload.Items.All(s => s.Thumbnail == null), "Large grouped history survives restart without a full thumbnail cache");
                controller.Store.Clear();
                Check(controller.Store.Items.Count == 0 && line.GroupCount == 1 && Cards(line).Length == 0 && Directory.GetFiles(controller.Store.Inbox, "*.png").Length == 104, "Clear removes every group while preserving all generated originals");
                File.WriteAllLines(Path.Combine(output, "group-results.txt"), results); Console.WriteLine("PASS " + results.Count + " grouped screenshot checks; no system input or clipboard used."); return 0;
            }
            catch (Exception ex) { File.WriteAllLines(Path.Combine(output, "group-results.txt"), results); File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString()); Console.Error.WriteLine(ex); return 1; }
            finally { if (controller != null) controller.Dispose(); app.Shutdown(); }
        }
    }
}
