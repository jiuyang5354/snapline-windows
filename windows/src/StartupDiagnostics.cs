using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Snapline
{
    internal static class StartupDiagnostics
    {
        internal static string Phase = "Starting";
        internal static int ImageWidth, ImageHeight;
        internal static int RestoreTotal, RestoredCount, RestoreErrors;
        internal static string RestoreError;

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            internal uint Length, Load;
            internal ulong TotalPhysical, AvailablePhysical, TotalCommit, AvailableCommit;
            internal ulong TotalVirtual, AvailableVirtual, AvailableExtended;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

        internal static string Write(string root, Exception error)
        { return WriteReport(root, error, Phase, error == null ? "started" : "failed", "diagnostics.log"); }

        internal static string WriteClipboardFailure(string root, Exception error)
        { return WriteReport(root, error, "Collecting clipboard image", "capture-skipped", "clipboard-error.log"); }

        private static string WriteReport(string root, Exception error, string phase, string state, string filename)
        {
            try
            {
                var report = new StringBuilder();
                report.AppendLine("Snapline diagnostics " + DateTimeOffset.Now.ToString("O"));
                var assembly = Assembly.GetExecutingAssembly();
                var version = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(assembly, typeof(AssemblyInformationalVersionAttribute));
                report.AppendLine("Version: " + (version == null ? assembly.GetName().Version.ToString() : version.InformationalVersion));
                report.AppendLine("Phase: " + phase);
                report.AppendLine("OS: " + Environment.OSVersion + "; OS64=" + Environment.Is64BitOperatingSystem + "; Process64=" + Environment.Is64BitProcess);
                using (var process = Process.GetCurrentProcess()) {
                    report.AppendLine("Process private bytes: " + process.PrivateMemorySize64);
                    report.AppendLine("Process peak working set bytes: " + process.PeakWorkingSet64);
                }
                var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
                if (GlobalMemoryStatusEx(ref memory)) {
                    report.AppendLine("Physical total/available bytes: " + memory.TotalPhysical + "/" + memory.AvailablePhysical);
                    report.AppendLine("Commit total/available bytes: " + memory.TotalCommit + "/" + memory.AvailableCommit);
                    report.AppendLine("Virtual total/available bytes: " + memory.TotalVirtual + "/" + memory.AvailableVirtual);
                }
                report.AppendLine("Last image dimensions: " + ImageWidth + "x" + ImageHeight);
                report.AppendLine("History entries/restored/read errors: " + RestoreTotal + "/" + RestoredCount + "/" + RestoreErrors);
                if (RestoreError != null) report.AppendLine("First history read error: " + RestoreError);
                report.AppendLine("State: " + state);
                if (error != null) report.AppendLine(error.ToString());
                Directory.CreateDirectory(root);
                string path = Path.Combine(root, filename);
                File.WriteAllText(path, report.ToString(), new UTF8Encoding(true));
                return path;
            }
            catch (Exception) { return null; } // Reporting must not replace the original failure.
        }
    }
}
