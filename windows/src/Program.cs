using System;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;

[assembly: AssemblyTitle("Snapline")]
[assembly: AssemblyDescription("Screenshots on a line · Windows")]
[assembly: AssemblyVersion("1.4.8.0")]
[assembly: AssemblyInformationalVersion("1.4.8-preview")]
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace Snapline
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snapline");
            if (args.Length == 2 && args[0] == "--data-dir") root = Path.GetFullPath(args[1]);
            else if (args.Length != 0) { MessageBox.Show("用法：Snapline.exe [--data-dir 文件夹]", "Snapline"); return; }
            string key;
            using (var hash = SHA256.Create()) key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(root.ToUpperInvariant()))).Replace("-", "").Substring(0, 20);
            bool first;
            using (var mutex = new Mutex(true, "Local\\Snapline-" + key, out first))
            using (var request = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\Snapline-Show-" + key))
            {
                if (!first) { request.Set(); return; }
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                bool running = false;
                try
                {
                    using (var controller = new Controller(root, true))
                    {
                        app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
                            if (ImageStore.IsImageError(e.Exception)) { controller.Notify(e.Exception.Message); e.Handled = true; }
                        };
                        var registration = ThreadPool.RegisterWaitForSingleObject(request, delegate {
                            app.Dispatcher.BeginInvoke(new Action(controller.Toggle));
                        }, null, -1, false);
                        controller.Welcome();
                        app.Dispatcher.BeginInvoke(new Action(delegate { controller.ShowAnnouncement(true); }));
                        running = true;
                        StartupDiagnostics.Phase = "Running";
                        StartupDiagnostics.Write(root, null);
                        try { app.Run(); }
                        finally { registration.Unregister(null); }
                    }
                }
                catch (Exception ex) {
                    string log = StartupDiagnostics.Write(root, ex);
                    MessageBox.Show((running ? "Snapline 运行出错：\n" : "无法启动 Snapline：\n") + ex.Message +
                        (log == null ? "" : "\n\n错误记录已保存，请反馈此文件：\n" + log), "Snapline", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
