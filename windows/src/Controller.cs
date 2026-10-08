using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Snapline
{
    internal sealed class Controller : IDisposable
    {
        internal const string ClipboardOrigin = "Snapline.Origin";
        internal readonly ImageStore Store;
        internal readonly LineWindow Line;
        internal readonly MessageSink Sink;
        internal bool Busy;
        private readonly Forms.NotifyIcon tray;
        private readonly CaptureWatch watch;
        private readonly DispatcherTimer mouse;
        private readonly Dispatcher dispatcher;
        private readonly Icon icon;
        private bool pinned;
        private bool suppressed;
        private DateTime peekUntil;
        private DateTime? edgeSince;
        private DateTime? awaySince;
        private string edgeDisplay;
        private uint lastSequence;
        private bool disposed;
        private readonly Forms.ToolStripMenuItem clipboardMenu;

        internal Controller(string root, bool watchFiles)
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            Store = new ImageStore(root);
            Line = new LineWindow(this);
            Sink = new MessageSink();
            Store.Changed += delegate { Line.Rebuild(); };
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Snapline.ico")) icon = new Icon(stream);
            tray = new Forms.NotifyIcon { Icon = icon, Text = "Snapline · 截图晾衣绳", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            Add(menu, "显示 / 隐藏    " + (Sink.HotkeyRegistered ? Sink.HotkeyText : "点击托盘图标"), Toggle);
            Add(menu, "挂入图片…", Import);
            menu.Items.Add(new Forms.ToolStripSeparator());
            clipboardMenu = new Forms.ToolStripMenuItem("收集剪贴板图片") { Checked = Store.Settings.ListenClipboard, CheckOnClick = true };
            clipboardMenu.Click += delegate { Store.Settings.ListenClipboard = clipboardMenu.Checked; Store.Save(); };
            menu.Items.Add(clipboardMenu);
            Add(menu, "选择截图文件夹…", ChooseFolder);
            Add(menu, "打开截图收件夹", delegate { Shell(Store.Inbox, null); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            Add(menu, "全部取下（保留文件）", Store.Clear);
            Add(menu, "使用说明", Help);
            Add(menu, "退出", delegate { Application.Current.Shutdown(); });
            menu.Opened += delegate { Busy = true; };
            menu.Closed += delegate { Busy = false; };
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object sender, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Left) Toggle(); };
            lastSequence = Native.GetClipboardSequenceNumber();
            Sink.ClipboardChanged += delegate { uint sequence = Native.GetClipboardSequenceNumber(); dispatcher.BeginInvoke(new Action(delegate { CaptureClipboard(sequence, 0); })); };
            Sink.ToggleRequested += Toggle;
            if (watchFiles)
            {
                watch = new CaptureWatch(Store, dispatcher);
                watch.Captured += Captured;
                watch.Error += Notify;
            }
            mouse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            mouse.Tick += Tick;
            mouse.Start();
        }

        internal void Welcome()
        {
            if (!Sink.HotkeyRegistered) Notify("Ctrl + Alt + T 已被其他程序占用。仍可点击托盘图标或靠近屏幕顶端展开。");
            else if (Sink.HotkeyText != "Ctrl + Alt + T") Notify("Ctrl + Alt + T 已被占用，本次使用 " + Sink.HotkeyText + " 展开 / 收起。");
            if (!Sink.ClipboardRegistered) Notify("剪贴板监听注册失败。仍可从托盘挂入图片或监听截图文件夹。");
            if (Store.Items.Count == 0)
            {
                tray.ShowBalloonTip(5000, "Snapline · 截图晾衣绳", "按 Win + Shift + S 截图；鼠标停在屏幕顶端即可展开。右键托盘图标打开菜单。", Forms.ToolTipIcon.Info);
                Peek(6);
            }
        }

        private static void Add(Forms.ContextMenuStrip menu, string text, Action action)
        {
            var item = new Forms.ToolStripMenuItem(text);
            item.Click += delegate { action(); };
            menu.Items.Add(item);
        }

        internal void Notify(string message) { if (!disposed) tray.ShowBalloonTip(5000, "Snapline", message, Forms.ToolTipIcon.Warning); }

        internal void Toggle()
        {
            if (Line.Revealed) { pinned = false; suppressed = true; Line.Tuck(); }
            else
            {
                pinned = true;
                var display = Forms.Screen.FromPoint(Native.Cursor());
                if (!Native.IsFullScreen(Native.GetForegroundWindow(), display.Bounds)) Line.Reveal(display);
            }
        }

        private void Peek(double seconds)
        {
            var display = Forms.Screen.FromPoint(Native.Cursor());
            peekUntil = DateTime.UtcNow.AddSeconds(seconds);
            if (!Native.IsFullScreen(Native.GetForegroundWindow(), display.Bounds)) Line.Reveal(display);
        }

        private void Captured(Shot shot) { Peek(2.2); }

        private void Tick(object sender, EventArgs args)
        {
            var cursor = Native.Cursor();
            var display = Forms.Screen.FromPoint(cursor);
            var now = DateTime.UtcNow;
            bool edge = cursor.Y >= display.Bounds.Top && cursor.Y <= display.Bounds.Top + 3;
            if (!edge) suppressed = false;
            Line.UpdateInteraction(cursor);
            if (Busy) return;
            if (Native.IsFullScreen(Native.GetForegroundWindow(), (Line.Revealed ? Line.Display : display).Bounds))
            {
                Line.Tuck(); edgeSince = null; return;
            }
            if (!Line.Revealed)
            {
                if (pinned) { Line.Reveal(display); return; }
                if (edge && !suppressed && (Native.GetAsyncKeyState(1) & 0x8000) == 0)
                {
                    if (edgeDisplay != display.DeviceName) { edgeDisplay = display.DeviceName; edgeSince = null; }
                    if (edgeSince == null) edgeSince = now;
                    if ((now - edgeSince.Value).TotalMilliseconds >= 280) { Line.Reveal(display); edgeSince = null; }
                }
                else edgeSince = null;
                return;
            }
            bool inside = Line.PixelBounds.Contains(cursor);
            if (inside) pinned = false;
            if (edge && (Native.GetAsyncKeyState(1) & 0x8000) != 0)
            {
                Line.Tuck(); suppressed = true; return;
            }
            if (inside || pinned || now < peekUntil) awaySince = null;
            else
            {
                if (awaySince == null) awaySince = now;
                if ((now - awaySince.Value).TotalMilliseconds >= 420) { Line.Tuck(); awaySince = null; }
            }
        }

        private void CaptureClipboard(uint sequence, int attempt)
        {
            if (disposed || !Store.Settings.ListenClipboard || sequence == lastSequence || Native.GetClipboardSequenceNumber() != sequence) return;
            try
            {
                var data = Clipboard.GetDataObject();
                if (data != null && Array.IndexOf(data.GetFormats(false), ClipboardOrigin) >= 0) { lastSequence = sequence; return; }
                BitmapSource image = null;
                var source = data == null ? null : data.GetData("PNG", false) as Stream;
                if (source != null)
                {
                    if (source.CanSeek) source.Position = 0;
                    image = BitmapFrame.Create(source, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    image.Freeze();
                }
                if (image == null && Clipboard.ContainsImage()) image = RepairBitmapAlpha(Clipboard.GetImage());
                if (image != null)
                {
                    var shot = Store.AddBitmap(image);
                    if (shot != null) Captured(shot);
                }
                lastSequence = Native.GetClipboardSequenceNumber();
            }
            catch (Exception ex)
            {
                if (!ImageStore.IsImageError(ex) && !(ex is ExternalException)) throw;
                if (attempt >= 7) { lastSequence = sequence; Notify("暂时无法读取剪贴板中的图片，请重新截图或从托盘挂入。"); return; }
                var retry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                retry.Tick += delegate { retry.Stop(); CaptureClipboard(sequence, attempt + 1); };
                retry.Start();
            }
        }

        internal static BitmapSource RepairBitmapAlpha(BitmapSource image)
        {
            if (image == null) return null;
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int stride = checked(converted.PixelWidth * 4);
            var pixels = new byte[checked(stride * converted.PixelHeight)];
            converted.CopyPixels(pixels, stride, 0);
            bool alphaPresent = false;
            for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { alphaPresent = true; break; }
            if (alphaPresent) { image.Freeze(); return image; }
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            var fixedImage = BitmapSource.Create(image.PixelWidth, image.PixelHeight, image.DpiX, image.DpiY, PixelFormats.Bgra32, null, pixels, stride);
            fixedImage.Freeze();
            return fixedImage;
        }

        internal DataObject Data(Shot shot)
        {
            var image = ImageStore.Load(shot.Path, 0);
            var data = new DataObject();
            data.SetImage(image);
            data.SetData("PNG", new MemoryStream(ImageStore.Png(image)), false);
            data.SetData(DataFormats.FileDrop, new[] { shot.Path });
            data.SetData(ClipboardOrigin, "Snapline", false);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(Store.Owns(shot.Path) ? 2 : 1)), false);
            return data;
        }

        internal bool Copy(Shot shot)
        {
            try
            {
                Clipboard.SetDataObject(Data(shot), true);
                lastSequence = Native.GetClipboardSequenceNumber();
                Line.ShowCopied(shot);
                return true;
            }
            catch (Exception ex) { if (!ImageStore.IsImageError(ex) && !(ex is ExternalException)) throw; Notify("复制失败：" + ex.Message); return false; }
        }

        internal void Open(Shot shot) { Line.Tuck(); Shell(shot.Path, null); }
        internal void Edit(Shot shot) { Line.Tuck(); Shell("mspaint.exe", "\"" + shot.Path + "\""); }
        internal void Reveal(Shot shot) { Shell("explorer.exe", "/select,\"" + shot.Path + "\""); }

        private void Shell(string path, string arguments)
        {
            try { Process.Start(new ProcessStartInfo { FileName = path, Arguments = arguments ?? "", UseShellExecute = true }); }
            catch (Exception ex) { if (!(ex is System.ComponentModel.Win32Exception) && !(ex is InvalidOperationException)) throw; Notify("无法打开：" + ex.Message); }
        }

        internal void Export(Shot shot)
        {
            Busy = true;
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG 图片|*.png", FileName = System.IO.Path.GetFileNameWithoutExtension(shot.Path) + ".png" };
                if (dialog.ShowDialog() == true)
                {
                    File.WriteAllBytes(dialog.FileName, ImageStore.Png(ImageStore.Load(shot.Path, 0)));
                    Store.Remove(shot, false);
                }
            }
            catch (Exception ex) { if (!ImageStore.IsImageError(ex)) throw; Notify("保存失败：" + ex.Message); }
            finally { Busy = false; }
        }

        internal void Discard(Shot shot)
        {
            try { Store.Remove(shot, true); }
            catch (Exception ex) { if (!ImageStore.IsImageError(ex) && !(ex is OperationCanceledException)) throw; Notify("无法取下截图：" + ex.Message); }
        }

        private void Import()
        {
            Busy = true;
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog {
                    Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff", Multiselect = true
                };
                if (dialog.ShowDialog() == true)
                    foreach (string path in dialog.FileNames)
                    {
                        try { var shot = Store.AddFile(path, true); if (shot != null) Captured(shot); }
                        catch (Exception ex) { if (!ImageStore.IsImageError(ex)) throw; Notify("无法读取图片：" + System.IO.Path.GetFileName(path)); }
                    }
            }
            finally { Busy = false; }
        }

        private void ChooseFolder()
        {
            Busy = true;
            try
            {
                using (var dialog = new Forms.FolderBrowserDialog { Description = "选择截图工具保存新图片的文件夹", SelectedPath = Store.Settings.WatchFolder })
                    if (dialog.ShowDialog() == Forms.DialogResult.OK)
                    {
                        Store.Settings.WatchFolder = dialog.SelectedPath;
                        Store.Save();
                        if (watch != null) watch.Restart();
                    }
            }
            finally { Busy = false; }
        }

        private void Help()
        {
            Busy = true;
            try { MessageBox.Show("Win + Shift + S 或 PrintScreen 截图后，图片自动挂入。\n收集剪贴板图片也会收集从其他应用复制的图片，可在托盘菜单关闭。\n\n鼠标在屏幕顶端停留，或 " + Sink.HotkeyText + "：展开 / 收起。\n单击：复制图片。双击：用默认图片应用打开。\n长按 0.45 秒：用画图编辑，保存后刷新缩略图。\n拖入应用：发送图片或文件副本。\n拖入文件夹：由目标应用决定复制或移动；Shift 拖动可请求移动。\n右键：另存为、在文件夹中显示或取下。\n\n叉号：收件夹里的图片进入回收站；外部原文件保留。\n最多保留 12 张，屏幕较窄时显示最近几张；旧文件仍在收件夹。\n\nSnapline 1.0 · Windows 非官方移植版\n基于 Alejandro Buján 的 Tendedero 交互与 MIT 代码。", "Snapline · 使用说明", MessageBoxButton.OK, MessageBoxImage.Information); }
            finally { Busy = false; }
        }

        public void Dispose()
        {
            disposed = true;
            mouse.Stop();
            if (watch != null) watch.Dispose();
            Sink.Dispose();
            tray.Visible = false;
            tray.Dispose();
            icon.Dispose();
            Line.Close();
        }
    }
}
