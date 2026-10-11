using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Snapline
{
    internal sealed class HoverKeys : IDisposable
    {
        private delegate IntPtr KeyboardProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int kind, KeyboardProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string module);
        private readonly KeyboardProc callback;
        private readonly Func<int, Action> action;
        private readonly Dispatcher dispatcher;
        private readonly HashSet<int> pressed = new HashSet<int>();
        private readonly HashSet<int> consumed = new HashSet<int>();
        private IntPtr hook;
        internal bool Registered { get { return hook != IntPtr.Zero; } }

        internal HoverKeys(Func<int, Action> action)
        {
            this.action = action;
            dispatcher = Dispatcher.CurrentDispatcher;
            callback = Handle;
            hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        }

        private IntPtr Handle(int code, IntPtr message, IntPtr data)
        {
            if (code < 0) return CallNextHookEx(hook, code, message, data);
            int key = Marshal.ReadInt32(data), kind = message.ToInt32();
            if (key != 0x41 && key != 0x43 && key != 0x44 && key != 0x2e) return CallNextHookEx(hook, code, message, data);
            if (kind == 0x101 || kind == 0x105)
            {
                pressed.Remove(key);
                if (consumed.Remove(key)) return new IntPtr(1);
            }
            else if (kind == 0x100 || kind == 0x104)
            {
                if (consumed.Contains(key)) return new IntPtr(1);
                if (pressed.Add(key) && (Marshal.ReadInt32(data, 8) & 0x20) == 0 &&
                    (Native.GetAsyncKeyState(0x10) & 0x8000) == 0 && (Native.GetAsyncKeyState(0x11) & 0x8000) == 0 &&
                    (Native.GetAsyncKeyState(0x12) & 0x8000) == 0 && (Native.GetAsyncKeyState(0x5b) & 0x8000) == 0 &&
                    (Native.GetAsyncKeyState(0x5c) & 0x8000) == 0)
                {
                    Action execute = action(key);
                    if (execute != null) { consumed.Add(key); dispatcher.BeginInvoke(execute); return new IntPtr(1); }
                }
            }
            return CallNextHookEx(hook, code, message, data);
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            pressed.Clear(); consumed.Clear();
        }
    }
}
