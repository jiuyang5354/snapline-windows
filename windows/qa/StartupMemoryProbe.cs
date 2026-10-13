using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;

[assembly: TargetFramework(".NETFramework,Version=v4.8")]

internal static class StartupMemoryProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        internal long ProcessTime, JobTime;
        internal uint Flags;
        internal UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        internal uint ActiveProcesses;
        internal UIntPtr Affinity;
        internal uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        internal ulong ReadOperations, WriteOperations, OtherOperations;
        internal ulong ReadBytes, WriteBytes, OtherBytes;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        internal BasicLimits Basic;
        internal IoCounters Io;
        internal UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr security, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int kind, ref ExtendedLimits limits, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(IntPtr job, int kind, out ExtendedLimits limits, uint length, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
        if (args.Length == 3 && args[0] == "--child") return Probe(args[1], args[2]);
        if (args.Length != 3) return 2;
        string assembly = Path.GetFullPath(args[0]);
        string fixtures = Path.GetFullPath(args[1]);
        string root = Path.GetFullPath(args[2]);
        foreach (string name in new[] { "empty", "small", "alpha", "large" })
        {
            string data = Path.Combine(root, name);
            Directory.CreateDirectory(data);
            string image = Path.Combine(data, "generated-fixture.png");
            if (name != "empty") File.Copy(Path.Combine(fixtures, name + ".png"), image, true);
            File.WriteAllText(Path.Combine(data, "settings.json"), "{\"Paths\":[" +
                (name == "empty" ? "" : "\"" + image.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"") +
                "],\"ListenClipboard\":false,\"CollectionPaused\":true,\"AutoCheckUpdates\":false,\"WatchFolder\":\"\"}");
        }
        int failed = 0;
        foreach (string name in new[] { "empty", "small", "alpha", "large", "large", "small-stage", "alpha-stage", "large-stage" })
            if (!Run(assembly, Path.Combine(root, name))) failed++;
        Console.WriteLine((failed == 0 ? "PASS " : "FAIL ") + (8 - failed) + "/8 startup memory cases at 192 MiB.");
        return failed == 0 ? 0 : 1;
    }

    private static string Expected(string name)
    {
        if (name.StartsWith("small", StringComparison.Ordinal)) return "1920x1080:PglZ0FIFZBF4aBor+Zwj+pCnd2QWc53iw98RiApv6Kk=";
        if (name.StartsWith("alpha", StringComparison.Ordinal)) return "1920x35:6R5zzbs+nSa4A/gdg15BwWV2nSOIbZTR8bCAwrZMIR8=";
        return "7680x4320:HsktoF3b5ssyfPMchW6hFRhOhmLmamqcUx1IAXHA2Yw=";
    }

    private static int Probe(string assembly, string root)
    {
        Console.WriteLine("READY");
        if (Console.ReadLine() != "GO") return 3;
        try
        {
            var app = Assembly.LoadFrom(assembly);
            var storeType = app.GetType("Snapline.ImageStore", true);
            if (Path.GetFileName(root).Contains("-stage"))
            {
                var load = storeType.GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic);
                var loadCall = new DynamicMethod("ProbeLoad", typeof(object), new[] { typeof(string), typeof(int) }, storeType.Module, true);
                var loadIl = loadCall.GetILGenerator();
                loadIl.Emit(OpCodes.Ldarg_0); loadIl.Emit(OpCodes.Ldarg_1); loadIl.Emit(OpCodes.Call, load); loadIl.Emit(OpCodes.Ret);
                var loadImage = (Func<string, int, object>)loadCall.CreateDelegate(typeof(Func<string, int, object>));
                string fixture = Path.GetFileName(root).StartsWith("small-", StringComparison.Ordinal) ? "small" :
                    Path.GetFileName(root).StartsWith("alpha-", StringComparison.Ordinal) ? "alpha" : "large";
                object image = loadImage(Path.Combine(Path.GetDirectoryName(root), fixture, "generated-fixture.png"), 0);
                Console.WriteLine("Full-resolution Load succeeded; PRIVATE_MIB " +
                    (Process.GetCurrentProcess().PrivateMemorySize64 / 1048576.0).ToString("F2", CultureInfo.InvariantCulture));
                var digest = storeType.GetMethod("Digest", BindingFlags.Static | BindingFlags.NonPublic);
                var digestCall = new DynamicMethod("ProbeDigest", typeof(string), new[] { typeof(object) }, storeType.Module, true);
                var digestIl = digestCall.GetILGenerator();
                digestIl.Emit(OpCodes.Ldarg_0); digestIl.Emit(OpCodes.Castclass, digest.GetParameters()[0].ParameterType);
                digestIl.Emit(OpCodes.Call, digest); digestIl.Emit(OpCodes.Ret);
                var digestImage = (Func<object, string>)digestCall.CreateDelegate(typeof(Func<object, string>));
                string actual = digestImage(image);
                if (actual != Expected(Path.GetFileName(root))) throw new InvalidOperationException("Digest differs from the v1.4.6 baseline.");
                Console.WriteLine("Digest matches baseline: " + actual);
                return 0;
            }
            var constructor = storeType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
            var createCall = new DynamicMethod("ProbeStore", typeof(object), new[] { typeof(string) }, storeType.Module, true);
            var createIl = createCall.GetILGenerator();
            createIl.Emit(OpCodes.Ldarg_0); createIl.Emit(OpCodes.Newobj, constructor); createIl.Emit(OpCodes.Ret);
            var createStore = (Func<string, object>)createCall.CreateDelegate(typeof(Func<string, object>));
            var store = createStore(root);
            var items = (ICollection)storeType.GetField("Items", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(store);
            string name = Path.GetFileName(root);
            if (items.Count != (name == "empty" ? 0 : 1)) throw new InvalidOperationException("ImageStore did not restore the expected list.");
            Console.WriteLine("RESTORED " + items.Count + " images; app=" + app.GetName().Version);
            foreach (object item in items)
            {
                string hash = (string)item.GetType().GetField("Hash", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(item);
                if (hash != Expected(name)) throw new InvalidOperationException("Restored digest differs from the v1.4.6 baseline.");
                Console.WriteLine("STORED_DIGEST " + hash);
            }
            if (name == "small" || name == "alpha")
            {
                string copy = Path.Combine(root, "duplicate.png");
                File.Copy(Path.Combine(root, "generated-fixture.png"), copy, true);
                object duplicate = storeType.GetMethod("AddFile", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(store, new object[] { copy, false });
                if (duplicate != null || items.Count != 1 || !File.Exists(copy)) throw new InvalidOperationException("Duplicate images must stay unlisted and keep their files.");
                Console.WriteLine("Content deduplication preserves the duplicate file.");
            }
            return 0;
        }
        catch (Exception error)
        {
            var invocation = error as TargetInvocationException;
            Exception cause = invocation != null && invocation.InnerException != null ? invocation.InnerException : error;
            Console.WriteLine(cause.GetType().FullName + ": " + cause.Message);
            Console.WriteLine(cause.StackTrace);
            return cause is OutOfMemoryException ? 33 : 34;
        }
    }

    private static bool Run(string assembly, string root)
    {
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new Win32Exception();
        Process child = null;
        try
        {
            var limits = new ExtendedLimits();
            limits.Basic.Flags = 0x100 | 0x2000; // Limit only this child; close the job on cleanup.
            limits.ProcessMemory = new UIntPtr(192u * 1024 * 1024);
            uint size = (uint)Marshal.SizeOf(typeof(ExtendedLimits));
            if (!SetInformationJobObject(job, 9, ref limits, size)) throw new Win32Exception();
            var start = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,
                "--child \"" + assembly + "\" \"" + root + "\"") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            child = Process.Start(start);
            if (child.StandardOutput.ReadLine() != "READY") throw new InvalidOperationException("Probe child did not reach its input gate.");
            if (!AssignProcessToJobObject(job, child.Handle)) throw new Win32Exception();
            child.StandardInput.WriteLine("GO"); child.StandardInput.Flush();
            string output = child.StandardOutput.ReadToEnd(), errors = child.StandardError.ReadToEnd();
            child.WaitForExit();
            ExtendedLimits actual;
            if (!QueryInformationJobObject(job, 9, out actual, size, IntPtr.Zero)) throw new Win32Exception();
            Console.WriteLine("CASE " + Path.GetFileName(root) + " EXIT " + child.ExitCode + " PEAK_PRIVATE_MIB " +
                (actual.PeakProcessMemory.ToUInt64() / 1048576.0).ToString("F2", CultureInfo.InvariantCulture));
            Console.Write(output); Console.Write(errors);
            return child.ExitCode == 0;
        }
        finally
        {
            CloseHandle(job);
            if (child != null) { if (!child.HasExited) child.Kill(); child.Dispose(); }
        }
    }
}
