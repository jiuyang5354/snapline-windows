using System;
using System.Collections.Generic;
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
        private static extern bool OpenClipboard(IntPtr window);
        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint EnumClipboardFormats(uint format);
        [DllImport("kernel32.dll")]
        private static extern void SetLastError(uint error);
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

        internal static string[] ClipboardFormats()
        {
            if (!OpenClipboard(IntPtr.Zero)) throw new System.ComponentModel.Win32Exception();
            try {
                var formats = new List<string>();
                uint format = 0;
                while (true) {
                    SetLastError(0);
                    format = EnumClipboardFormats(format);
                    if (format == 0) {
                        int error = Marshal.GetLastWin32Error();
                        if (error != 0) throw new System.ComponentModel.Win32Exception(error);
                        return formats.ToArray();
                    }
                    formats.Add(System.Windows.DataFormats.GetDataFormat((int)format).Name);
                }
            }
            finally { CloseClipboard(); }
        }

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

    internal static class Hotkey
    {
        internal const uint DefaultKey = 0x54;
        internal const uint DefaultCaptureKey = 0x53;
        internal const uint DefaultModifiers = 3;

        internal static bool IsModifier(uint key)
        {
            return (key >= 0x10 && key <= 0x12) || key == 0x5b || key == 0x5c || (key >= 0xa0 && key <= 0xa5);
        }

        internal static string Validate(uint key, uint modifiers)
        {
            if (key < 8 || key > 254 || IsModifier(key) || (modifiers & ~15u) != 0)
                return "请按下一个普通按键，可配合 Ctrl、Alt、Shift 或 Win。";
            if (key == 0x7b) return "F12 是 Windows 保留键，请选择其他按键。";
            return null;
        }

        internal static uint Modifiers(Forms.Keys keyData, bool windows)
        {
            return ((keyData & Forms.Keys.Control) != 0 ? 2u : 0u) |
                ((keyData & Forms.Keys.Alt) != 0 ? 1u : 0u) |
                ((keyData & Forms.Keys.Shift) != 0 ? 4u : 0u) | (windows ? 8u : 0u);
        }

        internal static string Text(uint key, uint modifiers)
        {
            string text = ((modifiers & 2) != 0 ? "Ctrl + " : "") + ((modifiers & 1) != 0 ? "Alt + " : "") +
                ((modifiers & 4) != 0 ? "Shift + " : "") + ((modifiers & 8) != 0 ? "Win + " : "");
            return text + new Forms.KeysConverter().ConvertToString((Forms.Keys)key);
        }
    }

    internal sealed class MessageSink : IDisposable
    {
        private readonly HwndSource source;
        internal event Action ClipboardChanged;
        internal event Action ToggleRequested;
        internal event Action CaptureRequested;
        internal bool HotkeyRegistered { get; private set; }
        internal int HotkeyError { get; private set; }
        internal uint HotkeyKey { get; private set; }
        internal uint HotkeyModifiers { get; private set; }
        internal int HotkeyId { get; private set; }
        internal string HotkeyText { get { return Hotkey.Text(HotkeyKey, HotkeyModifiers); } }
        internal bool CaptureHotkeyRegistered { get; private set; }
        internal uint CaptureHotkeyKey { get; private set; }
        internal uint CaptureHotkeyModifiers { get; private set; }
        internal int CaptureHotkeyId { get; private set; }
        internal string CaptureHotkeyText { get { return Hotkey.Text(CaptureHotkeyKey, CaptureHotkeyModifiers); } }
        internal bool ClipboardRegistered { get; private set; }
        internal IntPtr Handle { get { return source.Handle; } }

        internal MessageSink(uint key, uint modifiers, uint captureKey, uint captureModifiers)
        {
            source = new HwndSource(new HwndSourceParameters("Snapline messages") {
                ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0
            });
            source.AddHook(Hook);
            ClipboardRegistered = Native.AddClipboardFormatListener(source.Handle);
            HotkeyKey = Hotkey.DefaultKey;
            HotkeyModifiers = Hotkey.DefaultModifiers;
            CaptureHotkeyKey = captureKey;
            CaptureHotkeyModifiers = captureModifiers;
            string error;
            if (!TrySetHotkey(key, modifiers, out error) && (key != Hotkey.DefaultKey || modifiers != Hotkey.DefaultModifiers))
                TrySetHotkey(Hotkey.DefaultKey, Hotkey.DefaultModifiers, out error);
            if (!HotkeyRegistered && HotkeyError == 1409) TrySetHotkey(Hotkey.DefaultKey, 7, out error);
            TrySetCaptureHotkey(captureKey, captureModifiers, out error);
        }

        internal bool TrySetHotkey(uint key, uint modifiers, out string error)
        {
            error = Hotkey.Validate(key, modifiers);
            if (error != null) return false;
            if (CaptureHotkeyRegistered && key == CaptureHotkeyKey && modifiers == CaptureHotkeyModifiers) {
                error = "显示 / 隐藏与区域截图不能使用同一快捷键。"; return false;
            }
            if (HotkeyRegistered && key == HotkeyKey && modifiers == HotkeyModifiers) return true;
            int nextId = ReserveHotkey(key, modifiers, 0, out error);
            if (nextId == 0)
            {
                HotkeyError = Marshal.GetLastWin32Error();
                return false;
            }
            // Reserve the replacement before releasing the current binding.
            ClearHotkey();
            HotkeyId = nextId;
            HotkeyKey = key;
            HotkeyModifiers = modifiers;
            HotkeyRegistered = true;
            HotkeyError = 0;
            return true;
        }

        private bool TrySetCaptureHotkey(uint key, uint modifiers, out string error)
        {
            error = Hotkey.Validate(key, modifiers);
            if (error != null) return false;
            if (HotkeyRegistered && key == HotkeyKey && modifiers == HotkeyModifiers) {
                error = "显示 / 隐藏与区域截图不能使用同一快捷键。"; return false;
            }
            int id = ReserveHotkey(key, modifiers, 0, out error);
            if (id == 0) return false;
            CaptureHotkeyId = id;
            CaptureHotkeyKey = key;
            CaptureHotkeyModifiers = modifiers;
            CaptureHotkeyRegistered = true;
            return true;
        }

        private int ReserveHotkey(uint key, uint modifiers, int pendingId, out string error)
        {
            error = null;
            if (HotkeyRegistered && key == HotkeyKey && modifiers == HotkeyModifiers) return HotkeyId;
            if (CaptureHotkeyRegistered && key == CaptureHotkeyKey && modifiers == CaptureHotkeyModifiers) return CaptureHotkeyId;
            int id = 1;
            while ((HotkeyRegistered && id == HotkeyId) || (CaptureHotkeyRegistered && id == CaptureHotkeyId) || id == pendingId) id++;
            if (Native.RegisterHotKey(source.Handle, id, modifiers | 0x4000, key)) return id;
            error = "这个按键已被占用或被 Windows 保留，请换一个按键组合。";
            return 0;
        }

        private void ReleaseReserved(int id)
        {
            if (id != 0 && !(HotkeyRegistered && id == HotkeyId) && !(CaptureHotkeyRegistered && id == CaptureHotkeyId))
                Native.UnregisterHotKey(source.Handle, id);
        }

        internal bool TrySetHotkeys(uint key, uint modifiers, uint captureKey, uint captureModifiers, Action save, out string error)
        {
            error = Hotkey.Validate(key, modifiers) ?? Hotkey.Validate(captureKey, captureModifiers);
            if (error != null) return false;
            if (key == captureKey && modifiers == captureModifiers) {
                error = "显示 / 隐藏与区域截图不能使用同一快捷键。"; return false;
            }
            int nextToggle = ReserveHotkey(key, modifiers, 0, out error);
            if (nextToggle == 0) { error = "显示 / 隐藏：" + error; return false; }
            int nextCapture = ReserveHotkey(captureKey, captureModifiers, nextToggle, out error);
            if (nextCapture == 0) { ReleaseReserved(nextToggle); error = "区域截图：" + error; return false; }
            try { if (save != null) save(); }
            catch { ReleaseReserved(nextToggle); ReleaseReserved(nextCapture); throw; }
            // Keep existing registrations until settings are saved, including when the two bindings swap roles.
            if (HotkeyRegistered && HotkeyId != nextToggle && HotkeyId != nextCapture) Native.UnregisterHotKey(source.Handle, HotkeyId);
            if (CaptureHotkeyRegistered && CaptureHotkeyId != nextToggle && CaptureHotkeyId != nextCapture) Native.UnregisterHotKey(source.Handle, CaptureHotkeyId);
            HotkeyId = nextToggle; HotkeyKey = key; HotkeyModifiers = modifiers; HotkeyRegistered = true; HotkeyError = 0;
            CaptureHotkeyId = nextCapture; CaptureHotkeyKey = captureKey; CaptureHotkeyModifiers = captureModifiers; CaptureHotkeyRegistered = true;
            return true;
        }

        internal void ClearHotkey()
        {
            if (HotkeyRegistered) Native.UnregisterHotKey(source.Handle, HotkeyId);
            HotkeyRegistered = false;
        }

        private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == Native.WM_CLIPBOARDUPDATE && ClipboardChanged != null) ClipboardChanged();
            if (message == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId && HotkeyRegistered && ToggleRequested != null) ToggleRequested();
            if (message == Native.WM_HOTKEY && wParam.ToInt32() == CaptureHotkeyId && CaptureHotkeyRegistered && CaptureRequested != null) CaptureRequested();
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            Native.RemoveClipboardFormatListener(source.Handle);
            ClearHotkey();
            if (CaptureHotkeyRegistered) Native.UnregisterHotKey(source.Handle, CaptureHotkeyId);
            CaptureHotkeyRegistered = false;
            source.Dispose();
        }
    }
}
