using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace Snapline
{
    internal static class ToolsOpeningProbe
    {
        [STAThread] private static int Main(string[] args)
        {
            string mode = args[0], output = Path.GetFullPath(args[1]); int count = int.Parse(args[2]);
            Directory.CreateDirectory(output);
            string root = Path.Combine(output, "generated-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string[] paths = Enumerable.Range(0, count).Select(i => Path.Combine(root, "样本-" + i + ".png")).ToArray();
            byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aXioAAAAASUVORK5CYII=");
            foreach (string path in paths) File.WriteAllBytes(path, png);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try {
                Ui.Initialize();
                if (args.Length > 3 && args[3] == "--check") {
                    var row = new FileToolRow { Selected = true, After = "另存 PNG" }; int changed = 0, selection = 0;
                    row.PropertyChanged += delegate { changed++; }; row.SelectionChanged = delegate { selection++; };
                    row.Selected = true; row.After = "另存 PNG";
                    if (changed != 0 || selection != 0) throw new InvalidOperationException("Unchanged file-row values must not enqueue another full-list refresh.");
                    row.Selected = false; row.After = "不处理";
                    if (changed != 2 || selection != 1) throw new InvalidOperationException("Actual file-row edits must still notify bindings and selection validation.");
                    Console.WriteLine("PASS File rows notify only real changes and preserve checkbox edits.");
                }
                for (int run = 0; run < 2; run++) {
                    var clock = Stopwatch.StartNew();
                    Window window = mode == "pdf" ? (Window)new PdfToolsWindow(paths, root, delegate { }, delegate { }) :
                        new FileToolsWindow(FileTool.Convert, paths, root, delegate { }, delegate { }, delegate { });
                    double constructed = clock.Elapsed.TotalMilliseconds, rendered = -1;
                    window.ShowActivated = false; window.IsHitTestVisible = false;
                    window.ContentRendered += delegate { rendered = clock.Elapsed.TotalMilliseconds; };
                    window.Show();
                    var frame = new DispatcherFrame();
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate { frame.Continue = false; }));
                    Dispatcher.PushFrame(frame);
                    double idle = clock.Elapsed.TotalMilliseconds;
                    if (rendered < 0) throw new InvalidOperationException("The tool did not render before idle.");
                    var convert = window as FileToolsWindow;
                    if (args.Length > 3 && args[3] == "--check" && convert != null) {
                        int updates = 0;
                        foreach (var row in convert.Rows) row.PropertyChanged += delegate { updates++; };
                        convert.RefreshPreview();
                        if (updates != 0) throw new InvalidOperationException("An unchanged conversion preview must not redraw every file row twice.");
                        Console.WriteLine("PASS An unchanged conversion preview preserves the displayed row values without notifications.");
                    }
                    Console.WriteLine(string.Join("\t", mode, count.ToString(), run.ToString(), constructed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                        rendered.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), idle.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
                    window.Close();
                    var drain = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate { drain.Continue = false; })); Dispatcher.PushFrame(drain);
                }
                return 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { app.Shutdown(); }
        }
    }
}
