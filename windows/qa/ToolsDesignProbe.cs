using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Snapline
{
    internal static class ToolsDesignProbe
    {
        private static readonly List<string> checks = new List<string>();
        private static void Check(bool value, string message) { checks.Add((value ? "PASS " : "FAIL ") + message); if (!value) throw new InvalidOperationException(message); }
        private static void Pump(int ms) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
        private static IEnumerable<DependencyObject> Visual(DependencyObject root) { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Visual(VisualTreeHelper.GetChild(root, i))) yield return child; }
        private static string Pixels(Window window) { var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window); using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(ImageStore.Png(image))); }
        [STAThread] private static int Main(string[] args)
        {
            string root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root); var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try {
                var image = new RenderTargetBitmap(80, 60, 96, 96, PixelFormats.Pbgra32); var drawing = new DrawingVisual(); using (var dc = drawing.RenderOpen()) dc.DrawRectangle(Brushes.OrangeRed, null, new Rect(0, 0, 80, 60)); image.Render(drawing);
                string png = Path.Combine(root, "生成样本.png"); File.WriteAllBytes(png, ImageStore.Png(image));
                string pdf = PdfBatch.FromImages(new[] { png }, root, true, "生成文档.pdf", 0, CancellationToken.None, null).Outputs.Single();
                var window = new PdfToolsWindow(new[] { png, pdf }, root, delegate { }, delegate { }) { ShowActivated = false };
                window.Show(); Pump(160);
                Check(window.FilesGrid.Items.Count == 1 && window.FilesGrid.Items.Cast<FileToolRow>().All(row => PdfBatch.IsPdf(row.Path)), "PDF mode shows only supported PDF rows without discarding images");
                Check(window.Rows.Count == 2, "Switching file types preserves the complete source list");
                var indicator = Visual(window).OfType<Border>().Single(border => border.Height == 37 && ReferenceEquals(border.Background, ToolDesign.Brush("Active")));
                double before = ((TranslateTransform)indicator.RenderTransform).Y;
                window.ToolList.SelectedIndex = 8; Pump(45); double middle = ((TranslateTransform)indicator.RenderTransform).Y;
                if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast) Check(middle > before && indicator.RenderTransform.HasAnimatedProperties, "Sidebar selection has a visible intermediate translated state");
                Pump(220); Check(!indicator.RenderTransform.HasAnimatedProperties, "Sidebar animation releases its clock after settling");
                window.ToolList.SelectedIndex = 0; Pump(35); window.ToolList.SelectedIndex = 7; Pump(240);
                double target = ((ListBoxItem)window.ToolList.SelectedItem).TranslatePoint(new Point(), (UIElement)VisualTreeHelper.GetParent(window.ToolList)).Y;
                Check(Math.Abs(((TranslateTransform)indicator.RenderTransform).Y - target) < .1 && !indicator.RenderTransform.HasAnimatedProperties, "An interrupted selection settles at the latest tool without an old clock overriding it");
                window.ToolList.SelectedIndex = 8; Pump(200);
                Check(!window.ExecuteButton.IsEnabled && !ReferenceEquals(window.Status.Foreground, Brushes.Firebrick), "An untouched password form gives neutral guidance with execution disabled");
                window.NewPassword.Password = "one"; window.ConfirmPassword.Password = "two"; Check(!window.ExecuteButton.IsEnabled, "Mismatched passwords remain blocked after restyling");
                window.ConfirmPassword.Password = "one"; Check(window.ExecuteButton.IsEnabled, "Matching passwords enable the real action");
                window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Down) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                window.ToolList.SelectedIndex = 0; Pump(35); Check(!indicator.RenderTransform.HasAnimatedProperties, "Keyboard navigation applies its selected state without waiting for motion");
                Check(window.FilesGrid.Items.Count == 1 && window.FilesGrid.Items.Cast<FileToolRow>().All(row => FileBatch.IsInputImage(row.Path)), "Image mode restores only image rows from the preserved source list");
                ToolDesign.Theme(true); Pump(30); Check(ToolDesign.Brush("Paper").ToString() == "#FF1B1E1B", "Dark mode updates existing native surfaces without recreating the window");
                ToolDesign.Theme(false); Pump(30); Check(ToolDesign.Brush("Paper").ToString() == "#FFF6F6F3", "Light mode restores the original surface colors");
                window.Width = window.MinWidth; window.Height = window.MinHeight; Pump(70);
                Check(window.ExecuteButton.TranslatePoint(new Point(window.ExecuteButton.ActualWidth, window.ExecuteButton.ActualHeight), window).X <= window.ActualWidth, "The main action stays inside the smallest supported window");
                Check(Visual(window).OfType<TextBox>().Where(input => input.IsVisible).All(input => input.TranslatePoint(new Point(input.ActualWidth, 0), window).X <= window.ActualWidth + 1), "Visible PDF fields do not overflow the narrow window horizontally");
                window.ToolList.SelectedIndex = 1; Pump(80);
                string baseline = Pixels(window);
                window.ExecuteButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent }); Pump(70);
                if ((RenderCapability.Tier >> 16) >= 1 && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast) {
                    Check(window.ExecuteButton.Effect is ToolSheen, "Pointer entry activates the small-area shader on supported hardware");
                    Check(Pixels(window) != baseline, "The shader changes rendered pixels rather than only storing an effect property");
                } else Check(window.ExecuteButton.Effect == null, "Unsupported or reduced-motion rendering keeps the shader disabled");
                window.ExecuteButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent }); Pump(30);
                Check(window.ExecuteButton.Effect == null, "Leaving the action button removes its shader workload");
                window.Close();
                var empty = new PdfToolsWindow(new string[0], root, delegate { }, delegate { }) { ShowActivated = false }; empty.Show(); Pump(90);
                Check(Visual(empty).OfType<TextBlock>().Any(text => text.IsVisible && text.Text == "添加文件，开始整理"), "An empty tool shows an actionable empty state instead of a blank table");
                Check(!empty.ExecuteButton.IsEnabled && !ReferenceEquals(empty.Status.Foreground, Brushes.Firebrick), "The empty state is guidance rather than a premature validation error"); empty.Close();
                foreach (FileTool tool in new[] { FileTool.Convert, FileTool.Compress, FileTool.Rename, FileTool.Zip }) {
                    var files = new FileToolsWindow(tool, new[] { png, pdf }, root, delegate { }, delegate { }, delegate { }) { ShowActivated = false }; files.Show(); Pump(80);
                    Check(files.FilesGrid.Items.Count == 2 && files.ExecuteButton.IsVisible, tool + " keeps its real input list and action in the redesigned window");
                    files.Width = files.MinWidth; files.Height = files.MinHeight; Pump(50);
                    Check(Visual(files).OfType<TextBox>().Where(input => input.IsVisible).All(input => input.TranslatePoint(new Point(input.ActualWidth, 0), files).X <= files.ActualWidth + 1), tool + " fields stay within the narrow inspector");
                    if (tool == FileTool.Convert) {
                        string routed = null; ToolDesign.Navigate = delegate(Window source, string mode) { if (source == files) routed = mode; };
                        var zip = Visual(files).OfType<Button>().First(button => Visual(button).OfType<TextBlock>().Any(text => text.Text == "ZIP 打包"));
                        zip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(routed == "zip", "The real cross-tool navigation emits the selected destination"); ToolDesign.Navigate = null;
                    }
                    files.Close();
                }
                File.WriteAllLines(Path.Combine(root, "design-results.txt"), checks); Console.WriteLine("PASS " + checks.Count + " native design checks"); return 0;
            } catch (Exception ex) { File.WriteAllLines(Path.Combine(root, "design-results.txt"), checks); File.WriteAllText(Path.Combine(root, "failure.txt"), ex.ToString()); Console.Error.WriteLine(ex); return 1; }
            finally { app.Shutdown(); }
        }
    }
}
