using System;
using System.IO;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace Snapline
{
    // Dropline's shelf shares Snapline's data root, settings, tray and application lifetime.
    internal sealed class FileShelf : IDisposable
    {
        internal readonly Dropline.ShelfStore Store;
        internal readonly Dropline.ShelfWindow Window;
        private readonly Controller controller;
        private readonly bool monitorInput;
        private Dropline.DragWatcher watcher;
        private bool hotkey;
        private bool leftDown;
        private bool available = true;
        private bool disposed;
        internal const int HotkeyId = 0xD10;
        internal bool IsInteracting { get { return Window.IsInteracting; } }
        internal bool HotkeyRegistered { get { return hotkey; } }

        internal FileShelf(Controller controller, bool monitorInput)
        {
            this.controller = controller; this.monitorInput = monitorInput;
            Store = new Dropline.ShelfStore(Path.Combine(controller.Store.Root, "FileShelf"));
            try { Store.Load(); }
            catch (IOException ex) { available = false; controller.Notify("文件暂存架暂不可用，截图功能仍可使用：" + ex.Message); }
            catch (UnauthorizedAccessException ex) { available = false; controller.Notify("文件暂存架暂不可用，截图功能仍可使用：" + ex.Message); }
            Window = new Dropline.ShelfWindow(Store);
            Window.Error += controller.Notify;
            Window.ToolRequested += controller.OpenFileTools;
            new WindowInteropHelper(Window).EnsureHandle();
            HwndSource.FromHwnd(Window.Handle).AddHook(Hook);
            Apply();
        }

        internal void Apply()
        {
            var settings = controller.Store.Settings;
            Window.Enabled = available && settings.FileShelfEnabled;
            Window.KeepAfterDragOut = settings.FileShelfKeepAfterDragOut;
            Window.ApplyAppearance(settings.Appearance);
            if (monitorInput && Window.Enabled) {
                if (watcher == null) {
                    watcher = new Dropline.DragWatcher();
                    watcher.IsOwnWindow = delegate(IntPtr root) { return root == Window.Handle; };
                    watcher.DragStarted += Window.ExternalDragStarted;
                    watcher.DragEnded += Window.ExternalDragEnded;
                }
                watcher.AnyApp = settings.FileShelfAnyApp;
                if (!hotkey) {
                    hotkey = Dropline.Native.RegisterHotKey(Window.Handle, HotkeyId,
                        Dropline.Native.MOD_CONTROL | Dropline.Native.MOD_ALT | Dropline.Native.MOD_NOREPEAT, 0x44);
                    if (!hotkey) controller.Notify("暂存架快捷键 Ctrl + Alt + D 已被占用，仍可从托盘或设置打开。");
                }
            }
            else {
                if (watcher != null) { watcher.Dispose(); watcher = null; }
                if (hotkey) { Dropline.Native.UnregisterHotKey(Window.Handle, HotkeyId); hotkey = false; }
            }
            Window.Restore();
        }

        internal void Poll(System.Drawing.Point cursor, bool down)
        {
            if (monitorInput && Window.Enabled && !disposed) {
                var display = Forms.Screen.PrimaryScreen;
                bool relevant = Store.Items.Count == 0 && (Window.State == Dropline.ShelfState.Open ||
                    (cursor.X >= display.Bounds.Right - 4 && cursor.X < display.Bounds.Right));
                bool blocked = relevant && Native.IsFullScreen(Native.GetForegroundWindow(), display.Bounds);
                Window.PollEmptyEdge(cursor, down, blocked, DateTime.UtcNow);
            }
            if (watcher == null) { leftDown = down; return; }
            int message = down ? (leftDown ? Dropline.Native.WM_MOUSEMOVE : Dropline.Native.WM_LBUTTONDOWN) : Dropline.Native.WM_LBUTTONUP;
            if (down || leftDown) watcher.ProcessMouse(message, new Dropline.Native.POINT { X = cursor.X, Y = cursor.Y });
            leftDown = down;
        }

        internal void Toggle()
        {
            if (!Window.Enabled) { controller.ShowSettings("Shelf"); return; }
            Window.Toggle();
        }

        internal Forms.ToolStripMenuItem Menu()
        {
            var menu = new Forms.ToolStripMenuItem("文件暂存架");
            menu.DropDownItems.Add("打开 / 收起    Ctrl + Alt + D", null, delegate { controller.OpenFileShelf(); });
            menu.DropDownItems.Add("暂存架设置…", null, delegate { controller.ShowSettings("Shelf"); });
            menu.DropDownItems.Add("打开剪藏文件夹", null, delegate { controller.Shell(Store.Clippings, null); });
            menu.DropDownItems.Add("清空暂存架（保留文件）", null, delegate {
                try { Store.Clear(); }
                catch (IOException ex) { controller.Notify("无法清空暂存架：" + ex.Message); }
                catch (UnauthorizedAccessException ex) { controller.Notify("无法清空暂存架：" + ex.Message); }
            });
            return menu;
        }

        private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId && hotkey) { controller.FileShelfHotkey(); handled = true; }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (watcher != null) watcher.Dispose();
            if (hotkey) Dropline.Native.UnregisterHotKey(Window.Handle, HotkeyId);
            var source = HwndSource.FromHwnd(Window.Handle);
            if (source != null) { source.RemoveHook(Hook); Window.Close(); }
        }
    }
}
