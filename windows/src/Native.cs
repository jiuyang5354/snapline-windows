using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace Snapline
{
    internal static class Native
    {
        internal const int WM_CLIPBOARDUPDATE = 0x031D;
        internal const int WM_HOTKEY = 0x0312;
        internal const int GWL_EXSTYLE = -20;
        internal const int WS_EX_TRANSPARENT = 0x20;
        internal const int WS_EX_TOOLWINDOW = 0x80;
        internal const int WS_EX_NOACTIVATE = 0x08000000;
        internal static readonly IntPtr Topmost = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")]
        internal static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")]
        internal static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        internal static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")]
        internal static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        internal static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        internal static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")]
        internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);

        internal static Point Cursor()
        {
            POINT point;
            GetCursorPos(out point);
            return new Point(point.X, point.Y);
        }

        internal static double Scale(Forms.Screen screen)
        {
            uint x, y;
            var point = new POINT { X = screen.Bounds.Left + 1, Y = screen.Bounds.Top + 1 };
            return GetDpiForMonitor(MonitorFromPoint(point, 2), 0, out x, out y) == 0 ? x / 96.0 : 1;
        }

        internal static bool IsFullScreen(IntPtr hwnd, Rectangle screen)
        {
            if (hwnd == IntPtr.Zero) return false;
            var name = new StringBuilder(128);
            GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() == "Progman" || name.ToString() == "WorkerW" || name.ToString() == "Shell_TrayWnd") return false;
            RECT rect;
            if (!GetClientRect(hwnd, out rect)) return false;
            var origin = new POINT();
            if (!ClientToScreen(hwnd, ref origin)) return false;
            return origin.X <= screen.Left && origin.Y <= screen.Top &&
                origin.X + rect.Right >= screen.Right && origin.Y + rect.Bottom >= screen.Bottom;
        }

        internal static string ScreenshotsFolder()
        {
            var id = new Guid("b7bede81-df94-4682-a7d8-57a52620b86f");
            IntPtr path;
            if (SHGetKnownFolderPath(ref id, 0x4000, IntPtr.Zero, out path) == 0)
            {
                try { return Marshal.PtrToStringUni(path); }
                finally { Marshal.FreeCoTaskMem(path); }
            }
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        }
    }

    internal sealed class MessageSink : IDisposable
    {
        private readonly HwndSource source;
        internal event Action ClipboardChanged;
        internal event Action ToggleRequested;
        internal bool HotkeyRegistered { get; private set; }
        internal int HotkeyError { get; private set; }
        internal string HotkeyText { get; private set; }
        internal bool ClipboardRegistered { get; private set; }
        internal IntPtr Handle { get { return source.Handle; } }

        internal MessageSink()
        {
            source = new HwndSource(new HwndSourceParameters("Snapline messages") {
                ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0
            });
            source.AddHook(Hook);
            ClipboardRegistered = Native.AddClipboardFormatListener(source.Handle);
            HotkeyText = "Ctrl + Alt + T";
            HotkeyRegistered = Native.RegisterHotKey(source.Handle, 1, 0x4003, 0x54);
            if (!HotkeyRegistered)
            {
                HotkeyError = Marshal.GetLastWin32Error();
                if (HotkeyError == 1409)
                {
                    HotkeyText = "Ctrl + Alt + Shift + T";
                    HotkeyRegistered = Native.RegisterHotKey(source.Handle, 1, 0x4007, 0x54);
                    if (!HotkeyRegistered) HotkeyError = Marshal.GetLastWin32Error();
                }
            }
        }

        private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == Native.WM_CLIPBOARDUPDATE && ClipboardChanged != null) ClipboardChanged();
            if (message == Native.WM_HOTKEY && wParam.ToInt32() == 1 && ToggleRequested != null) ToggleRequested();
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            Native.RemoveClipboardFormatListener(source.Handle);
            if (HotkeyRegistered) Native.UnregisterHotKey(source.Handle, 1);
            source.Dispose();
        }
    }
}
