using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
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
        private readonly Forms.ToolStripMenuItem toggleMenu;
        private readonly Forms.ToolStripMenuItem latestMenu;
        private readonly Forms.ToolStripMenuItem pauseMenu;
        private readonly Forms.ToolStripMenuItem updateMenu;
        private readonly Forms.ToolStripMenuItem autoUpdateMenu;
        private readonly Forms.ToolStripMenuItem startupMenu;
        private readonly Updates updates;
        private readonly CancellationTokenSource updateCancel = new CancellationTokenSource();
        private readonly DispatcherTimer updateTimer;
        private Release availableUpdate;
        private Release balloonUpdate;
        private bool checkingUpdate;
        private UpdateDialog updateDialog;
        private HotkeyDialog hotkeyDialog;

        internal Controller(string root, bool watchFiles, Updates updateClient = null)
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            updates = updateClient ?? new Updates();
            Store = new ImageStore(root);
            Line = new LineWindow(this);
            Sink = new MessageSink(Store.Settings.HotkeyKey, Store.Settings.HotkeyModifiers);
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Snapline.ico")) icon = new Icon(stream);
            tray = new Forms.NotifyIcon { Icon = icon, Text = "Snapline · 截图晾衣绳", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            toggleMenu = Add(menu, "显示 / 隐藏    " + (Sink.HotkeyRegistered ? Sink.HotkeyText : "点击托盘图标"), Toggle);
            Add(menu, "设置快捷键…", ShowHotkeySettings);
            latestMenu = Add(menu, "复制最近一张", delegate { CopyLatest(); });
            latestMenu.Enabled = Store.Items.Count > 0;
            Add(menu, "挂入图片…", Import);
            menu.Items.Add(new Forms.ToolStripSeparator());
            pauseMenu = new Forms.ToolStripMenuItem("暂停所有自动收集") { Checked = Store.Settings.CollectionPaused, CheckOnClick = true };
            pauseMenu.Click += delegate { SetCollectionPaused(pauseMenu.Checked); };
            menu.Items.Add(pauseMenu);
            clipboardMenu = new Forms.ToolStripMenuItem("收集剪贴板图片") { Checked = Store.Settings.ListenClipboard, CheckOnClick = true };
            clipboardMenu.Click += delegate { Store.Settings.ListenClipboard = clipboardMenu.Checked; Store.Save(); };
            menu.Items.Add(clipboardMenu);
            Add(menu, "选择截图文件夹…", ChooseFolder);
            Add(menu, "打开截图收件夹", delegate { Shell(Store.Inbox, null); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            Add(menu, "全部取下（保留文件）", Store.Clear);
            menu.Items.Add(new Forms.ToolStripSeparator());
            updateMenu = Add(menu, "检查更新…", delegate { if (availableUpdate != null) ShowUpdate(availableUpdate); else CheckUpdates(true); });
            autoUpdateMenu = new Forms.ToolStripMenuItem("自动检查更新（含预发布）") { Checked = Store.Settings.AutoCheckUpdates, CheckOnClick = true };
            autoUpdateMenu.Click += delegate { SetAutoUpdates(autoUpdateMenu.Checked); };
            menu.Items.Add(autoUpdateMenu);
            Add(menu, "打开版本发布页", delegate { Shell(Updates.Repository + "/releases", null); });
            startupMenu = new Forms.ToolStripMenuItem("开机启动") { CheckOnClick = true };
            startupMenu.Click += delegate {
                try { Startup.Set(startupMenu.Checked, Forms.Application.ExecutablePath, Store.Root); }
                catch (Exception ex) { if (!(ex is UnauthorizedAccessException) && !(ex is System.Security.SecurityException) && !(ex is IOException)) throw; Notify("无法更改开机启动设置：" + ex.Message); }
                RefreshStartup();
            };
            menu.Items.Add(startupMenu);
            menu.Items.Add(new Forms.ToolStripSeparator());
            Add(menu, "使用说明", Help);
            Add(menu, "退出", delegate { Application.Current.Shutdown(); });
            menu.Opened += delegate { Busy = true; RefreshStartup(); };
            menu.Closed += delegate { Busy = hotkeyDialog != null || updateDialog != null; };
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object sender, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Left) Toggle(); };
            tray.BalloonTipClicked += delegate { if (balloonUpdate != null) ShowUpdate(balloonUpdate); };
            Store.Changed += delegate { Line.Rebuild(); latestMenu.Enabled = Store.Items.Count > 0; };
            tray.Text = Store.Settings.CollectionPaused ? "Snapline · 已暂停自动收集" : "Snapline · 截图晾衣绳";
            lastSequence = Native.GetClipboardSequenceNumber();
            Sink.ClipboardChanged += delegate { uint sequence = Native.GetClipboardSequenceNumber(); dispatcher.BeginInvoke(new Action(delegate { CaptureClipboard(sequence, 0); })); };
            Sink.ToggleRequested += delegate {
                if (hotkeyDialog != null) hotkeyDialog.CaptureCurrent(Sink.HotkeyKey, Sink.HotkeyModifiers);
                else Toggle();
            };
            if (watchFiles)
            {
                watch = new CaptureWatch(Store, dispatcher);
                watch.Captured += Captured;
                watch.Error += Notify;
            }
            mouse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            mouse.Tick += Tick;
            mouse.Start();
            updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            updateTimer.Tick += delegate {
                updateTimer.Interval = TimeSpan.FromHours(1);
                if (Updates.Due(Store.Settings, DateTime.UtcNow)) CheckUpdates(false);
            };
        }

        internal void Welcome()
        {
            updateTimer.Start();
            if (!Sink.HotkeyRegistered) Notify("快捷键无法注册。可在托盘设置其他快捷键，或点击托盘图标、靠近屏幕顶端展开。");
            else if (Sink.HotkeyKey != Store.Settings.HotkeyKey || Sink.HotkeyModifiers != Store.Settings.HotkeyModifiers)
                Notify("保存的快捷键无法注册，本次使用 " + Sink.HotkeyText + " 展开 / 收起。可从托盘重新设置。");
            if (!Sink.ClipboardRegistered) Notify("剪贴板监听注册失败。仍可从托盘挂入图片或监听截图文件夹。");
            if (Store.Items.Count == 0)
            {
                tray.ShowBalloonTip(5000, "Snapline · 截图晾衣绳", "按 Win + Shift + S 截图；鼠标停在屏幕顶端即可展开。右键托盘图标打开菜单。", Forms.ToolTipIcon.Info);
                Peek(6);
            }
        }

        private static Forms.ToolStripMenuItem Add(Forms.ContextMenuStrip menu, string text, Action action)
        {
            var item = new Forms.ToolStripMenuItem(text);
            item.Click += delegate { action(); };
            menu.Items.Add(item);
            return item;
        }

        internal void Notify(string message) { balloonUpdate = null; if (!disposed) tray.ShowBalloonTip(5000, "Snapline", message, Forms.ToolTipIcon.Warning); }

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
            if (disposed || Store.Settings.CollectionPaused || !Store.Settings.ListenClipboard || sequence == lastSequence || Native.GetClipboardSequenceNumber() != sequence) return;
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

        internal bool CopyLatest()
        {
            Store.Prune();
            if (Store.Items.Count == 0) { Notify("还没有可复制的图片。"); return false; }
            if (!Copy(Store.Items[Store.Items.Count - 1])) return false;
            balloonUpdate = null;
            tray.ShowBalloonTip(1800, "Snapline", "已复制最近一张图片。", Forms.ToolTipIcon.Info);
            return true;
        }

        internal bool SetCollectionPaused(bool paused)
        {
            bool previous = Store.Settings.CollectionPaused;
            Store.Settings.CollectionPaused = paused;
            try { Store.Save(); }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                Store.Settings.CollectionPaused = previous;
                pauseMenu.Checked = previous;
                Notify("无法保存暂停设置：" + ex.Message);
                return false;
            }
            if (watch != null) watch.DiscardPending();
            lastSequence = Native.GetClipboardSequenceNumber();
            pauseMenu.Checked = paused;
            tray.Text = paused ? "Snapline · 已暂停自动收集" : "Snapline · 截图晾衣绳";
            return true;
        }

        private void RefreshStartup()
        {
            try { startupMenu.Checked = Startup.Enabled(Forms.Application.ExecutablePath, Store.Root); }
            catch (System.Security.SecurityException) { startupMenu.Checked = false; }
            catch (UnauthorizedAccessException) { startupMenu.Checked = false; }
        }

        internal void SetAutoUpdates(bool enabled)
        {
            bool previous = Store.Settings.AutoCheckUpdates;
            Store.Settings.AutoCheckUpdates = enabled;
            try { Store.Save(); }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                Store.Settings.AutoCheckUpdates = previous;
                Notify("无法保存更新设置：" + ex.Message);
            }
            autoUpdateMenu.Checked = Store.Settings.AutoCheckUpdates;
            if (Updates.Due(Store.Settings, DateTime.UtcNow)) CheckUpdates(false);
        }

        private async void CheckUpdates(bool manual)
        {
            if (checkingUpdate || disposed) return;
            checkingUpdate = true;
            updateMenu.Enabled = false;
            updateMenu.Text = "正在检查更新…";
            Store.Settings.UpdateCheckedUtcTicks = DateTime.UtcNow.Ticks;
            try
            {
                var release = await updates.CheckAsync(updateCancel.Token);
                if (disposed) return;
                availableUpdate = release;
                if (manual)
                {
                    if (release != null) ShowUpdate(release);
                    else MessageBox.Show("当前 v" + Updates.CurrentVersion + "，暂无可下载的新版本。", "Snapline · 检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (Store.Settings.AutoCheckUpdates && release != null && Store.Settings.UpdateNotifiedTag != release.Tag)
                {
                    Store.Settings.UpdateNotifiedTag = release.Tag;
                    balloonUpdate = release;
                    tray.ShowBalloonTip(8000, "Snapline · 发现 " + release.Tag, "点击查看更新说明并下载" + (release.Prerelease ? "（预发布版本）" : "") + "。", Forms.ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                if (!Updates.IsError(ex)) throw;
                if (manual && !disposed) MessageBox.Show("暂时无法检查更新，请稍后重试或查看发布页。\n" + ex.Message, "Snapline · 检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                checkingUpdate = false;
                if (!disposed)
                {
                    updateMenu.Enabled = true;
                    updateMenu.Text = availableUpdate == null ? "检查更新…" : "更新到 " + availableUpdate.Tag + "…";
                    try { Store.Save(); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        private void ShowUpdate(Release release)
        {
            if (disposed || hotkeyDialog != null) return;
            if (updateDialog != null) { updateDialog.Activate(); return; }
            Busy = true;
            try
            {
                using (updateDialog = new UpdateDialog(release,
                    delegate(string path, IProgress<int> progress, CancellationToken cancel) { return updates.DownloadAsync(release, path, progress, cancel); },
                    delegate(string target) {
                        if (target == release.Page) Shell(target, null);
                        else Shell("explorer.exe", "/select,\"" + target + "\"");
                    })) updateDialog.ShowDialog();
            }
            finally { updateDialog = null; Busy = false; }
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

        internal bool ChangeHotkey(uint key, uint modifiers, out string error)
        {
            uint oldKey = Sink.HotkeyKey, oldModifiers = Sink.HotkeyModifiers;
            uint savedKey = Store.Settings.HotkeyKey, savedModifiers = Store.Settings.HotkeyModifiers;
            bool wasRegistered = Sink.HotkeyRegistered;
            if (!Sink.TrySetHotkey(key, modifiers, out error)) return false;
            Store.Settings.HotkeyKey = key;
            Store.Settings.HotkeyModifiers = modifiers;
            try { Store.Save(); }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                Store.Settings.HotkeyKey = savedKey;
                Store.Settings.HotkeyModifiers = savedModifiers;
                string restoreError;
                if (wasRegistered) Sink.TrySetHotkey(oldKey, oldModifiers, out restoreError);
                else Sink.ClearHotkey();
                error = "无法保存快捷键设置，请检查数据文件夹是否可写后再试。";
                return false;
            }
            toggleMenu.Text = "显示 / 隐藏    " + Sink.HotkeyText;
            return true;
        }

        internal void ShowHotkeySettings()
        {
            Busy = true;
            try
            {
                using (hotkeyDialog = new HotkeyDialog(Sink.HotkeyKey, Sink.HotkeyModifiers, delegate(uint key, uint modifiers) {
                    string error;
                    return ChangeHotkey(key, modifiers, out error) ? null : error;
                })) hotkeyDialog.ShowDialog();
            }
            finally { hotkeyDialog = null; Busy = false; }
        }

        private void Help()
        {
            Busy = true;
            try { MessageBox.Show("Win + Shift + S 或 PrintScreen 截图后，图片自动挂入。\n收集剪贴板图片也会收集从其他应用复制的图片，可在托盘菜单关闭。\n\n鼠标在屏幕顶端停留，或 " + Sink.HotkeyText + "：展开 / 收起。\n右键托盘 → 设置快捷键：录入单键或组合键，保存后立即生效。\n单击：复制图片。双击：用默认图片应用打开。\n长按 0.45 秒：用画图编辑，保存后刷新缩略图。\n拖入应用：发送图片或文件副本。\n拖入文件夹：由目标应用决定复制或移动；Shift 拖动可请求移动。\n右键：另存为、在文件夹中显示或取下。\n\n托盘 → 复制最近一张：直接复制，无须展开。\n暂停所有自动收集：同时暂停剪贴板和文件夹，重启后保持。\n开机启动：可选，默认关闭；移动程序后请重新勾选。\n自动检查更新：默认开启，每 24 小时检查并提醒；可关闭。\n下载新版后退出旧版，解压运行；图片和设置保留。\n\n叉号：收件夹里的图片进入回收站；外部原文件保留。\n最多保留 12 张，屏幕较窄时显示最近几张；旧文件仍在收件夹。\n\nSnapline " + Updates.CurrentVersion + " · Windows 非官方移植版\n基于 Alejandro Buján 的 Tendedero 交互与 MIT 代码。", "Snapline · 使用说明", MessageBoxButton.OK, MessageBoxImage.Information); }
            finally { Busy = false; }
        }

        public void Dispose()
        {
            disposed = true;
            mouse.Stop();
            updateTimer.Stop();
            updateCancel.Cancel();
            updates.Dispose();
            updateCancel.Dispose();
            if (watch != null) watch.Dispose();
            Sink.Dispose();
            tray.Visible = false;
            tray.Dispose();
            icon.Dispose();
            Line.Close();
        }
    }
}
