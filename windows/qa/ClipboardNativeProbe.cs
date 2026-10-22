using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

// Read metadata only: no GetClipboardData, IDataObject payload access, or clipboard writes.
internal static class ClipboardNativeProbe
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    private static void Check(bool ok, string text)
    {
        if (!ok) throw new Exception("FAIL: " + text);
        Console.WriteLine("PASS: " + text);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try {
            var assembly = Assembly.LoadFrom(args[0]);
            var native = assembly.GetType("Snapline.Native").GetMethod("ClipboardFormats", BindingFlags.Static | BindingFlags.NonPublic);
            uint sequence = GetClipboardSequenceNumber();
            var formats = (string[])native.Invoke(null, null);
            Check(formats.All(name => !string.IsNullOrEmpty(name)), "Native clipboard enumeration returns resolved format names");
            Check(GetClipboardSequenceNumber() == sequence, "Metadata enumeration does not change the clipboard sequence");
            Check(OpenClipboard(IntPtr.Zero), "Native enumeration releases the clipboard lock");
            CloseClipboard();
            var second = (string[])native.Invoke(null, null);
            Check(GetClipboardSequenceNumber() == sequence && formats.SequenceEqual(second),
                "Repeated enumeration preserves clipboard metadata and remains usable");
            Console.WriteLine("PASS 4 read-only native clipboard checks; no payload reads, clipboard writes or keyboard/mouse input.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
