using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Snapline
{
    internal static class ToolIntegrationProbe
    {
        private static readonly List<string> checks = new List<string>();
        private static void Check(bool value, string message) { checks.Add((value ? "PASS " : "FAIL ") + message); if (!value) throw new InvalidOperationException(message); }
        private static void Pump(int ms) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
        private static IEnumerable<DependencyObject> Visual(DependencyObject root) { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var item in Visual(VisualTreeHelper.GetChild(root, i))) yield return item; }
        private static string Hash(string path) { using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path)) return Convert.ToBase64String(hash.ComputeHash(stream)); }
        [STAThread] private static int Main(string[] args)
        {
            string output = Path.GetFullPath(args[0]), root = Path.Combine(output, "generated-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Controller controller = null;
            try {
                var initial = new ImageStore(root); initial.Settings.ListenClipboard = false; initial.Settings.CollectionPaused = true; initial.Settings.AutoCheckUpdates = false; initial.Settings.Appearance = "Dark"; initial.Save();
                var image = new RenderTargetBitmap(120, 80, 96, 96, PixelFormats.Pbgra32); var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawRectangle(Brushes.OrangeRed, null, new Rect(0, 0, 120, 80)); image.Render(visual);
                string source = Path.Combine(root, "真实路径样本.png"); File.WriteAllBytes(source, ImageStore.Png(image)); string sourceHash = Hash(source);
                controller = new Controller(root, false); controller.Store.AddFile(source, true); controller.Files.Store.Add(new[] { source });
                var originalPaper = Ui.Brush("Paper"); string originalColor = originalPaper.ToString();
                int stage = 0; string renamed = null; Exception failure = null;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
                timer.Tick += delegate {
                    if (failure != null) return;
                    var window = app.Windows.OfType<Window>().FirstOrDefault(candidate => candidate.IsVisible && (candidate is FileToolsWindow || candidate is PdfToolsWindow)); if (window == null) return;
                    try {
                        if (stage == 0) {
                            var files = window as FileToolsWindow; if (files == null || files.Tool != FileTool.Convert) return;
                            Check(controller.Busy && !controller.Files.Window.IsVisible, "The production controller keeps the shelf hidden throughout the tool session");
                            Check(ToolDesign.Brush("Paper").ToString() == "#FF1B1E1B", "The tool session follows the saved application appearance");
                            var theme = Visual(files).OfType<Button>().Single(button => button.Tag as string == "ThemeToggle"); theme.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Check(new ImageStore(root).Settings.Appearance == "Light", "The tool appearance switch saves to the actual settings store");
                            Check(ReferenceEquals(Ui.Brush("Paper"), originalPaper) && originalPaper.ToString() == originalColor, "Tool theme resources do not change the existing screenshot-line palette");
                            stage = 1; ToolDesign.Navigate(window, "rename");
                        } else if (stage == 1) {
                            var files = window as FileToolsWindow; if (files == null || files.Tool != FileTool.Rename) return;
                            Check(files.Rows.Single().Path == source && controller.Busy, "Switching tools carries the real source paths and retains the busy guard");
                            files.TemplateInput.Text = "集成改名_{n}"; files.ExecuteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); renamed = controller.Store.Items.Single().Path;
                            Check(renamed != source && File.Exists(renamed) && !File.Exists(source), "The real rename dialog changes the generated file through the controller");
                            Check(files.Rows.Single().Path == renamed && controller.Files.Store.Items.Single().Paths.Single() == renamed, "Rename updates both dialog rows and the real shelf record");
                            stage = 2; ToolDesign.Navigate(window, "zip");
                        } else if (stage == 2) {
                            var files = window as FileToolsWindow; if (files == null || files.Tool != FileTool.Zip) return;
                            Check(files.Rows.Single().Path == renamed && Hash(renamed) == sourceHash, "The next tool receives the renamed path with original bytes preserved");
                            stage = 3; files.ExecuteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        } else if (stage == 3) {
                            var files = window as FileToolsWindow; if (files == null || files.Running || !files.Status.Text.Contains("处理完成")) return;
                            Check(controller.Files.Store.Items.SelectMany(item => item.Paths).Any(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(path)), "A completed real tool adds its output into the actual shelf store");
                            stage = 4; ToolDesign.Navigate(window, "pdf");
                        } else if (stage == 4) {
                            var pdf = window as PdfToolsWindow; if (pdf == null) return;
                            Check(pdf.Rows.Single().Path == renamed && pdf.FilesGrid.Items.Count == 1, "The PDF dialog receives the current image path after multiple tool switches");
                            stage = 5; pdf.ExecuteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        } else if (stage == 5) {
                            var pdf = window as PdfToolsWindow; if (pdf == null || pdf.Running || !pdf.Status.Text.Contains("处理完成")) return;
                            Check(controller.Files.Store.Items.SelectMany(item => item.Paths).Any(path => path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && File.Exists(path)), "PDF conversion writes a real document and adds it to the actual shelf");
                            timer.Stop(); stage = 6; window.Close();
                        }
                    } catch (Exception ex) { failure = ex; timer.Stop(); window.Close(); }
                };
                timer.Start(); try { controller.OpenFileTools(FileTool.Convert, new[] { source }); } finally { timer.Stop(); }
                if (failure != null) throw failure;
                Check(stage == 6 && !controller.Busy && ToolDesign.Navigate == null && ToolDesign.AppearanceChanged == null, "Closing the session restores controller state and removes its callbacks");
                Check(controller.Files.Window.IsVisible && controller.Store.Items.Single().Path == renamed, "The actual shelf is restored once after the final dialog closes");
                Check(new ImageStore(root).Items.Single().Path == renamed && Hash(renamed) == sourceHash, "Screenshot history survives a store reload after cross-tool rename and export");
                int emptyStage = 0; failure = null;
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
                timer.Tick += delegate {
                    var pdf = app.Windows.OfType<PdfToolsWindow>().FirstOrDefault(window => window.IsVisible);
                    var files = app.Windows.OfType<FileToolsWindow>().FirstOrDefault(window => window.IsVisible);
                    try {
                        if (emptyStage == 0 && pdf != null) { emptyStage = 1; ToolDesign.Navigate(pdf, "convert"); }
                        else if (emptyStage == 1 && files != null) {
                            Check(files.Rows.Count == 0 && !files.ExecuteButton.IsEnabled, "Switching an empty PDF session to conversion stays safe and does not inject history files");
                            files.AddPaths(new[] { renamed });
                            Check(files.Rows.Count == 1 && files.ExecuteButton.IsEnabled, "Files can be added to an empty conversion session without reopening the tool");
                            emptyStage = 2; timer.Stop(); files.Close();
                        }
                    } catch (Exception ex) { failure = ex; timer.Stop(); if (pdf != null) pdf.Close(); if (files != null) files.Close(); }
                };
                timer.Start(); try { controller.OpenFileTools(FileTool.Pdf, new string[0]); } finally { timer.Stop(); }
                if (failure != null) throw failure; Check(emptyStage == 2 && !controller.Busy, "The empty-session navigation returns control normally");
                File.WriteAllLines(Path.Combine(output, "integration-results.txt"), checks); Console.WriteLine("PASS " + checks.Count + " production tool integration checks"); return 0;
            } catch (Exception ex) { File.WriteAllLines(Path.Combine(output, "integration-results.txt"), checks); File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString()); Console.Error.WriteLine(ex); return 1; }
            finally { if (controller != null) controller.Dispose(); app.Shutdown(); }
        }
    }
}
