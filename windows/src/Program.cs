using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;

[assembly: AssemblyTitle("Snapline")]
[assembly: AssemblyDescription("Screenshots on a line · Windows")]
[assembly: AssemblyVersion("1.1.0.0")]

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
                        try { app.Run(); }
                        finally { registration.Unregister(null); }
                    }
                }
                catch (Exception ex) { MessageBox.Show("无法启动 Snapline：\n" + ex.Message, "Snapline", MessageBoxButton.OK, MessageBoxImage.Error); }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
