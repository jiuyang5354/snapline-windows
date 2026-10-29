using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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
        internal readonly HoverKeys Hover;
        internal readonly FileShelf Files;
        private readonly Action<string, string> launch;
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
        private bool clipboardFailureNotified;
        private bool disposed;
        private readonly Forms.ToolStripMenuItem clipboardMenu;
        private readonly Forms.ToolStripMenuItem toggleMenu;
        private readonly Forms.ToolStripMenuItem captureMenu;
        private readonly Forms.ToolStripMenuItem clearMenu;
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
        private SettingsWindow settingsWindow;
        private AnnouncementWindow announcementWindow;
        private Window fileToolsWindow;
        private bool DialogOpen { get { return settingsWindow != null || updateDialog != null || announcementWindow != null || fileToolsWindow != null; } }

        internal Controller(string root, bool watchFiles, Updates updateClient = null, Action<string, string> launch = null)
        {
            this.launch = launch;
            dispatcher = Dispatcher.CurrentDispatcher;
            updates = updateClient ?? new Updates();
            StartupDiagnostics.Phase = "Restoring screenshot history";
            Store = new ImageStore(root);
            StartupDiagnostics.Phase = "Initializing screenshot line and native input";
            Line = new LineWindow(this);
            Sink = new MessageSink(Store.Settings.HotkeyKey, Store.Settings.HotkeyModifiers, Store.Settings.CaptureHotkeyKey, Store.Settings.CaptureHotkeyModifiers, Store.Settings.ClearHotkeyKey, Store.Settings.ClearHotkeyModifiers);
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Snapline.ico")) icon = new Icon(stream);
            tray = new Forms.NotifyIcon { Icon = icon, Text = "Snapline · 截图晾衣绳", Visible = true };
            Files = new FileShelf(this, watchFiles);
            var menu = new Forms.ContextMenuStrip();
            toggleMenu = Add(menu, "显示 / 隐藏    " + (Sink.HotkeyRegistered ? Sink.HotkeyText : "点击托盘图标"), Toggle);
            Add(menu, "设置…", delegate { ShowSettings("General"); });
            Add(menu, "设置快捷键…", ShowHotkeySettings);
            captureMenu = Add(menu, "区域截图…    " + (Sink.CaptureHotkeyRegistered ? Sink.CaptureHotkeyText : "快捷键未启用"), CaptureRegion);
            latestMenu = Add(menu, "复制最近一张", delegate { CopyLatest(); });
            latestMenu.Enabled = Store.Items.Count > 0;
            Add(menu, "挂入图片…", Import);
            menu.Items.Add(new Forms.ToolStripSeparator());
            pauseMenu = new Forms.ToolStripMenuItem("暂停所有自动收集") { Checked = Store.Settings.CollectionPaused, CheckOnClick = true };
            pauseMenu.Click += delegate { SetCollectionPaused(pauseMenu.Checked); };
            menu.Items.Add(pauseMenu);
            clipboardMenu = new Forms.ToolStripMenuItem("收集剪贴板图片") { Checked = Store.Settings.ListenClipboard, CheckOnClick = true };
            clipboardMenu.Click += delegate { Store.Settings.ListenClipboard = clipboardMenu.Checked; Store.Save(); Line.RefreshStatus(); };
            menu.Items.Add(clipboardMenu);
            Add(menu, "选择截图文件夹…", ChooseFolder);
            Add(menu, "打开截图收件夹", delegate { Shell(Store.Inbox, null); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            clearMenu = Add(menu, "清空截图栏（保留文件）    " + Sink.ClearHotkeyText, ClearLine);
            menu.Items.Add(Files.Menu());
            Add(menu, "PDF 工具箱…", delegate { dispatcher.BeginInvoke(new Action(delegate { OpenFileTools(FileTool.Pdf, new string[0]); })); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            updateMenu = Add(menu, "检查更新…", delegate { if (availableUpdate != null) ShowUpdate(availableUpdate); else CheckUpdates(true); });
            autoUpdateMenu = new Forms.ToolStripMenuItem("自动检查更新（含预发布）") { Checked = Store.Settings.AutoCheckUpdates, CheckOnClick = true };
            autoUpdateMenu.Click += delegate { SetAutoUpdates(autoUpdateMenu.Checked); };
            menu.Items.Add(autoUpdateMenu);
            Add(menu, "更新公告…", delegate { ShowAnnouncement(); });
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
            menu.Closed += delegate { Busy = DialogOpen; };
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object sender, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Left) Toggle(); };
            tray.BalloonTipClicked += delegate { if (balloonUpdate != null) ShowUpdate(balloonUpdate); };
            Store.Changed += delegate { Line.Rebuild(); latestMenu.Enabled = Store.Items.Count > 0; };
            tray.Text = Store.Settings.CollectionPaused ? "Snapline · 已暂停自动收集" : "Snapline · 截图晾衣绳";
            lastSequence = Native.GetClipboardSequenceNumber();
            Sink.ClipboardChanged += delegate { uint sequence = Native.GetClipboardSequenceNumber(); dispatcher.BeginInvoke(new Action(delegate { CaptureClipboard(sequence, 0); })); };
            Sink.ToggleRequested += delegate {
                if (settingsWindow != null && settingsWindow.RecordingHotkey) settingsWindow.CaptureCurrent(Sink.HotkeyKey, Sink.HotkeyModifiers);
                else Toggle();
            };
            Sink.ClearRequested += delegate {
                if (settingsWindow != null && settingsWindow.RecordingHotkey) settingsWindow.CaptureCurrent(Sink.ClearHotkeyKey, Sink.ClearHotkeyModifiers);
                else if (!Busy && !DialogOpen) ClearLine();
            };
            Sink.CaptureRequested += delegate {
                if (settingsWindow != null && settingsWindow.RecordingHotkey) settingsWindow.CaptureCurrent(Sink.CaptureHotkeyKey, Sink.CaptureHotkeyModifiers);
                else CaptureRegion();
            };
            if (watchFiles)
            {
                StartupDiagnostics.Phase = "Initializing file watchers";
                watch = new CaptureWatch(Store, dispatcher);
                watch.Captured += Captured;
                watch.Error += Notify;
            }
            mouse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            mouse.Tick += Tick;
            mouse.Start();
            StartupDiagnostics.Phase = "Initializing hover shortcuts";
            Hover = new HoverKeys(HoverAction);
            if (!Hover.Registered) Notify("无法启用悬停快捷操作；可继续用鼠标复制、编辑和取下截图。");
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
            if (!Sink.CaptureHotkeyRegistered) Notify("区域截图快捷键 " + Sink.CaptureHotkeyText + " 无法注册。请在设置 → 快捷键中更改，仍可从截图栏或托盘截图。");
            if (!Sink.ClearHotkeyRegistered) Notify("清空截图栏快捷键 " + Sink.ClearHotkeyText + " 无法注册。请在设置 → 快捷键中更改，仍可使用截图栏按钮或托盘入口。");
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

        internal void Captured(Shot shot) { if (Store.Settings.ShowOnCapture) Peek(2.2); }

        private void Tick(object sender, EventArgs args)
        {
            var cursor = Native.Cursor();
            if (!Busy && !DialogOpen) Files.Poll(cursor, (Native.GetAsyncKeyState(1) & 0x8000) != 0);
            var display = Forms.Screen.FromPoint(cursor);
            var now = DateTime.UtcNow;
            bool edge = cursor.Y >= display.Bounds.Top && cursor.Y <= display.Bounds.Top + 3;
            if (!edge) suppressed = false;
            Line.UpdateInteraction(cursor);
            if (Busy || Files.IsInteracting) return;
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
            Shot shot; Exception error;
            if (TryCollectClipboard(delegate { return Native.GetClipboardSequenceNumber() == sequence ? Clipboard.GetDataObject() : null; },
                Store, out shot, out error, Native.ClipboardFormats, Native.ClipboardOwnerProcess)) {
                lastSequence = sequence;
                if (shot != null) Captured(shot);
                return;
            }
            if (error is OutOfMemoryException || error is OverflowException) {
                lastSequence = sequence;
                ReportClipboardFailure(Store.Root, error, ref clipboardFailureNotified, Notify);
                return;
            }
            if (attempt >= 7) { lastSequence = sequence; ReportClipboardFailure(Store.Root, error, ref clipboardFailureNotified, Notify); return; }
            var retry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            retry.Tick += delegate { retry.Stop(); CaptureClipboard(sequence, attempt + 1); };
            retry.Start();
        }

        internal static bool TryCollectClipboard(Func<System.Windows.IDataObject> read, ImageStore store, out Shot shot, out Exception error, Func<string[]> readFormats = null, Func<string> readOwner = null)
        {
            shot = null; error = null;
            try {
                string[] formats = readFormats == null ? null : readFormats();
                if (formats != null && !CanCollectClipboard(formats)) return true;
                if (readOwner != null && IsWpsClipboardOwner(readOwner())) return true;
                var image = ReadClipboardImage(read(), formats);
                if (image != null) shot = store.AddBitmap(image);
                return true;
            }
            catch (Exception ex) {
                if (!ImageStore.IsImageError(ex) && !(ex is ExternalException) && !(ex is OutOfMemoryException) && !(ex is OverflowException)) throw;
                error = ex;
                return false;
            }
        }

        internal static void ReportClipboardFailure(string root, Exception error, ref bool notified, Action<string> notify)
        {
            string log = StartupDiagnostics.WriteClipboardFailure(root, error);
            if (notified) return;
            notified = true;
            notify("已跳过无法处理的剪贴板图片，程序继续运行。本次运行不再重复提醒。" +
                (log == null ? "" : "错误记录：clipboard-error.log"));
        }

        internal static bool CanCollectClipboard(string[] formats, string ownerProcess = null)
        {
            if (IsWpsClipboardOwner(ownerProcess)) return false;
            foreach (string ignored in new[] { ClipboardOrigin, DataFormats.UnicodeText, DataFormats.Text, DataFormats.OemText,
                DataFormats.StringFormat, DataFormats.Rtf, DataFormats.CommaSeparatedValue, "XML Spreadsheet", "PDF", "Portable Document Format", "SVG", "image/svg+xml" })
                if (HasClipboardFormat(formats, ignored)) return false;
            foreach (string format in formats)
                if (format.StartsWith("Biff", StringComparison.OrdinalIgnoreCase) || format.IndexOf("Illustrator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    format.IndexOf("AICB", StringComparison.OrdinalIgnoreCase) >= 0 || IsOfficeObjectFormat(format)) return false;
            return HasClipboardFormat(formats, "PNG") || HasClipboardFormat(formats, DataFormats.Bitmap) ||
                HasClipboardFormat(formats, DataFormats.Dib) || HasClipboardFormat(formats, DataFormats.GetDataFormat(17).Name);
        }

        internal static BitmapSource ReadClipboardImage(System.Windows.IDataObject data, string[] formats = null)
        {
            if (data == null) return null;
            formats = formats ?? data.GetFormats(false);
            if (!CanCollectClipboard(formats)) return null;
            BitmapSource image = null;
            var source = HasClipboardFormat(formats, "PNG") ? data.GetData("PNG", false) as Stream : null;
            if (source != null) {
                using (source) {
                    if (source.CanSeek) source.Position = 0;
                    image = BitmapFrame.Create(source, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                }
                image.Freeze();
            }
            if (image == null && (HasClipboardFormat(formats, DataFormats.Bitmap) || HasClipboardFormat(formats, DataFormats.Dib) ||
                HasClipboardFormat(formats, DataFormats.GetDataFormat(17).Name))) {
                var bitmap = data.GetData(DataFormats.Bitmap, true) as BitmapSource;
                if (bitmap != null) { StartupDiagnostics.ImageWidth = bitmap.PixelWidth; StartupDiagnostics.ImageHeight = bitmap.PixelHeight; }
                image = RepairBitmapAlpha(bitmap);
            }
            return image;
        }

        // Office/WPS render editable objects beside their private shape data; those images are previews.
        private static readonly string[] OfficeObjectMarkers = { "GVML", "PowerPoint", "Office Drawing", "Kingsoft", "WPS" };
        internal static bool IsWpsClipboardOwner(string process)
        {
            return string.Equals(process, "wpp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(process, "wps", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(process, "et", StringComparison.OrdinalIgnoreCase);
        }
        internal static bool IsOfficeObjectFormat(string format)
        {
            if (string.IsNullOrEmpty(format)) return false;
            foreach (string marker in OfficeObjectMarkers)
                if (format.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool HasClipboardFormat(string[] formats, string name)
        { return Array.Exists(formats, delegate(string format) { return string.Equals(format, name, StringComparison.OrdinalIgnoreCase); }); }

        internal static BitmapSource RepairBitmapAlpha(BitmapSource image)
        {
            if (image == null) return null;
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int stride = checked(converted.PixelWidth * 4);
            int rows = Math.Min(converted.PixelHeight, Math.Max(1, 262144 / stride));
            var pixels = new byte[checked(stride * rows)];
            for (int y = 0; y < converted.PixelHeight; y += rows) {
                int count = Math.Min(rows, converted.PixelHeight - y);
                converted.CopyPixels(new Int32Rect(0, y, converted.PixelWidth, count), pixels, stride, 0);
                for (int i = 3; i < checked(stride * count); i += 4)
                    if (pixels[i] != 0) { image.Freeze(); return image; }
            }
            var fixedImage = new WriteableBitmap(image.PixelWidth, image.PixelHeight, image.DpiX, image.DpiY, PixelFormats.Bgra32, null);
            for (int y = 0; y < converted.PixelHeight; y += rows) {
                int count = Math.Min(rows, converted.PixelHeight - y);
                var rectangle = new Int32Rect(0, y, converted.PixelWidth, count);
                converted.CopyPixels(rectangle, pixels, stride, 0);
                for (int i = 3; i < checked(stride * count); i += 4) pixels[i] = 255;
                fixedImage.WritePixels(rectangle, pixels, stride, 0);
            }
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
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1)), false);
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
            Line.RefreshStatus();
            return true;
        }

        private void RefreshStartup()
        {
            try { startupMenu.Checked = Startup.Enabled(Forms.Application.ExecutablePath, Store.Root); }
            catch (System.Security.SecurityException) { startupMenu.Checked = false; }
            catch (UnauthorizedAccessException) { startupMenu.Checked = false; }
        }

        internal bool StartupEnabled { get { RefreshStartup(); return startupMenu.Checked; } }

        internal bool SetStartup(bool enabled, out string error)
        {
            error = null;
            try { Startup.Set(enabled, Forms.Application.ExecutablePath, Store.Root); }
            catch (Exception ex) {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException) && !(ex is System.Security.SecurityException)) throw;
                error = "开机启动更改失败：" + ex.Message;
            }
            RefreshStartup();
            return error == null;
        }

        internal bool ApplyPreferences(bool clipboard, bool paused, bool automatic, bool includeCursor, string folder, uint key, uint modifiers, out string error)
        { return ApplyPreferences(clipboard, paused, automatic, includeCursor, folder, key, modifiers, Store.Settings.CaptureHotkeyKey, Store.Settings.CaptureHotkeyModifiers, out error); }

        internal bool ApplyPreferences(bool clipboard, bool paused, bool automatic, bool includeCursor, string folder, uint key, uint modifiers, uint captureKey, uint captureModifiers, out string error)
        { return ApplyPreferences(clipboard, paused, automatic, includeCursor, folder, key, modifiers, captureKey, captureModifiers,
            Store.Settings.ClearHotkeyKey, Store.Settings.ClearHotkeyModifiers, out error); }

        internal bool ApplyPreferences(bool clipboard, bool paused, bool automatic, bool includeCursor, string folder,
            uint key, uint modifiers, uint captureKey, uint captureModifiers, uint clearKey, uint clearModifiers, out string error)
        {
            var settings = Store.Settings;
            bool oldClipboard = settings.ListenClipboard, oldPaused = settings.CollectionPaused, oldAutomatic = settings.AutoCheckUpdates;
            bool oldCursor = settings.IncludeCursor;
            string oldFolder = settings.WatchFolder;
            settings.ListenClipboard = clipboard; settings.CollectionPaused = paused;
            settings.AutoCheckUpdates = automatic; settings.WatchFolder = folder;
            settings.IncludeCursor = includeCursor;
            if (!ChangeHotkeys(key, modifiers, captureKey, captureModifiers, clearKey, clearModifiers, out error)) {
                settings.ListenClipboard = oldClipboard; settings.CollectionPaused = oldPaused;
                settings.AutoCheckUpdates = oldAutomatic; settings.WatchFolder = oldFolder;
                settings.IncludeCursor = oldCursor;
                return false;
            }
            clipboardMenu.Checked = clipboard; pauseMenu.Checked = paused; autoUpdateMenu.Checked = automatic;
            tray.Text = paused ? "Snapline · 已暂停自动收集" : "Snapline · 截图晾衣绳";
            lastSequence = Native.GetClipboardSequenceNumber();
            if (watch != null) {
                if (oldPaused != paused) watch.DiscardPending();
                if (!string.Equals(oldFolder, folder, StringComparison.OrdinalIgnoreCase)) watch.Restart();
            }
            Line.RefreshStatus();
            if (Updates.Due(settings, DateTime.UtcNow)) CheckUpdates(false);
            return true;
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
            if (settingsWindow != null) settingsWindow.UpdateChecking(true);
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
                    if (settingsWindow != null) settingsWindow.UpdateChecking(false);
                    try { Store.Save(); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        private void ShowUpdate(Release release)
        {
            if (disposed) return;
            if (updateDialog != null) { updateDialog.Activate(); return; }
            Busy = true;
            try
            {
                using (updateDialog = new UpdateDialog(release,
                    delegate(string path, IProgress<int> progress, CancellationToken cancel) { return updates.DownloadAsync(release, path, progress, cancel); },
                    delegate(string target) {
                        if (target == release.Page) Shell(target, null);
                        else Shell("explorer.exe", "/select,\"" + target + "\"");
                    },
                    delegate(string path) { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); })) updateDialog.ShowDialog();
            }
            finally { updateDialog = null; Busy = DialogOpen; }
        }

        internal void RequestUpdate() { if (availableUpdate != null) ShowUpdate(availableUpdate); else CheckUpdates(true); }
        internal void ShowAnnouncement(bool automatic = false)
        {
            if (disposed || announcementWindow != null || (automatic && (!AnnouncementWindow.Due(Store.Settings) || Busy || DialogOpen))) return;
            Busy = true; Line.Tuck();
            try
            {
                announcementWindow = new AnnouncementWindow();
                if (settingsWindow != null) { announcementWindow.Owner = settingsWindow; announcementWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
                announcementWindow.ShowDialog();
                if (!disposed && AnnouncementWindow.Due(Store.Settings))
                {
                    string previous = Store.Settings.AnnouncementSeenVersion;
                    Store.Settings.AnnouncementSeenVersion = AnnouncementWindow.Version;
                    try { Store.Save(); }
                    catch (Exception ex) {
                        Store.Settings.AnnouncementSeenVersion = previous;
                        if (!(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                        Notify("无法保存公告阅读记录，下次启动会再次显示：" + ex.Message);
                    }
                }
            }
            finally { announcementWindow = null; Busy = DialogOpen; }
        }
        internal bool ChangeInbox(string folder, out string error)
        {
            string previous = Store.Inbox;
            if (!Store.SetInbox(folder, out error)) return false;
            if (watch != null && !string.Equals(previous, Store.Inbox, StringComparison.OrdinalIgnoreCase)) watch.Restart();
            return true;
        }

        internal void OpenInbox() { Shell(Store.Inbox, null); }
        internal void OpenReleases() { Shell(Updates.Repository + "/releases", null); }

        internal void Open(Shot shot) { Line.Tuck(); Shell(shot.Path, null); }
        internal static string PaintPath()
        {
            string classic = Path.Combine(Environment.SystemDirectory, "mspaint.exe");
            if (File.Exists(classic)) return classic;
            string alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\mspaint.exe");
            return File.Exists(alias) ? alias : "mspaint.exe";
        }

        internal void Edit(Shot shot) { Line.Tuck(); Shell(PaintPath(), "\"" + shot.Path + "\""); }
        internal void Reveal(Shot shot) { Shell("explorer.exe", "/select,\"" + shot.Path + "\""); }

        internal void Shell(string path, string arguments)
        {
            try {
                if (launch != null) launch(path, arguments ?? "");
                else Process.Start(new ProcessStartInfo { FileName = path, Arguments = arguments ?? "", UseShellExecute = true });
            }
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
            finally { Busy = DialogOpen; }
        }

        private Action HoverAction(int key)
        {
            if (disposed || Busy || DialogOpen || Files.IsInteracting) return null;
            var point = Native.Cursor();
            var card = Line.ShotAt(new System.Windows.Point(point.X, point.Y));
            if (card == null) return null;
            Shot shot = card.Shot;
            return delegate {
                if (disposed || Busy || !Store.Items.Contains(shot)) return;
                if (key == 0x43) Copy(shot);
                else if (key == 0x41) Edit(shot);
                else Discard(shot);
            };
        }

        internal async void CaptureRegion()
        {
            if (disposed || Busy) return;
            Busy = true; Line.Tuck();
            try {
                await Task.Delay(250);
                if (disposed) return;
                var image = RegionCapture.Pick(Store.Settings.IncludeCursor);
                if (image != null) AcceptCapture(image);
            }
            catch (Exception ex) {
                if (!ImageStore.IsImageError(ex) && !(ex is System.ComponentModel.Win32Exception)) throw;
                Notify("截图失败：" + ex.Message);
            }
            finally { Busy = DialogOpen; }
        }

        internal void AcceptCapture(BitmapSource image)
        {
            var shot = Store.AddBitmap(image) ?? Store.Items.Find(item => item.Hash == ImageStore.Digest(image));
            if (shot != null) { Copy(shot); Captured(shot); }
        }

        internal void ClearLine()
        {
            try { Store.Clear(); }
            catch (Exception ex) { if (!(ex is IOException) && !(ex is UnauthorizedAccessException)) throw; Notify("无法清空截图栏：" + ex.Message); }
        }

        internal void Discard(Shot shot)
        {
            try { Store.Remove(shot, true); }
            catch (Exception ex) { if (!ImageStore.IsImageError(ex) && !(ex is OperationCanceledException)) throw; Notify("无法取下截图：" + ex.Message); }
        }

        internal void Detach(Shot shot)
        {
            try { Store.Remove(shot, false); }
            catch (Exception ex) { if (!(ex is IOException) && !(ex is UnauthorizedAccessException)) throw; Notify("无法取下截图：" + ex.Message); }
        }

        internal void Import()
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
            finally { Busy = DialogOpen; }
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
            finally { Busy = DialogOpen; }
        }

        internal bool ChangeHotkey(uint key, uint modifiers, out string error)
        { return ChangeHotkeys(key, modifiers, Store.Settings.CaptureHotkeyKey, Store.Settings.CaptureHotkeyModifiers, out error); }

        internal bool ChangeFileShelf(bool enabled, bool anyApp, bool keep, out string error)
        {
            var settings = Store.Settings;
            bool oldEnabled = settings.FileShelfEnabled, oldAny = settings.FileShelfAnyApp, oldKeep = settings.FileShelfKeepAfterDragOut;
            settings.FileShelfEnabled = enabled; settings.FileShelfAnyApp = anyApp; settings.FileShelfKeepAfterDragOut = keep;
            try { Store.Save(); }
            catch (Exception ex) {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                settings.FileShelfEnabled = oldEnabled; settings.FileShelfAnyApp = oldAny; settings.FileShelfKeepAfterDragOut = oldKeep;
                error = "无法保存暂存架设置：" + ex.Message; return false;
            }
            Files.Apply(); error = null; return true;
        }

        internal void FileShelfHotkey()
        {
            if (settingsWindow != null && settingsWindow.RecordingHotkey) settingsWindow.CaptureCurrent(0x44, 3);
            else OpenFileShelf();
        }

        internal void OpenFileShelf()
        {
            if (fileToolsWindow != null) { fileToolsWindow.Activate(); return; }
            if (settingsWindow == null) { Files.Toggle(); return; }
            settingsWindow.Close();
            dispatcher.BeginInvoke(new Action(Files.Toggle));
        }

        internal void OpenFileTools(FileTool tool, string[] paths)
        {
            if (fileToolsWindow != null) { fileToolsWindow.Activate(); return; }
            if (disposed || Files.Window.IsClosed || Busy || DialogOpen || (paths.Length == 0 && tool != FileTool.Pdf)) return;
            Busy = true; Line.Tuck(); Files.Window.Hide();
            var previousNavigation = ToolDesign.Navigate;
            var previousAppearance = ToolDesign.AppearanceChanged;
            FileTool? nextTool = null;
            Microsoft.Win32.UserPreferenceChangedEventHandler systemTheme = delegate {
                if (Store.Settings.Appearance == "System") dispatcher.BeginInvoke(new Action(delegate {
                    if (!disposed && fileToolsWindow != null) ToolDesign.ApplyAppearance("System");
                }));
            };
            try {
                ToolDesign.ApplyAppearance(Store.Settings.Appearance);
                ToolDesign.AppearanceChanged = delegate(bool dark) {
                    string previous = Store.Settings.Appearance; Store.Settings.Appearance = dark ? "Dark" : "Light";
                    try { Store.Save(); Files.Window.ApplyAppearance(Store.Settings.Appearance); }
                    catch (Exception ex) {
                        if (!(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                        Store.Settings.Appearance = previous; ToolDesign.ApplyAppearance(previous); Notify("外观保存失败：" + ex.Message);
                    }
                };
                ToolDesign.Navigate = delegate(Window current, string mode) {
                    if (current != fileToolsWindow) return;
                    switch (mode) {
                        case "pdf": nextTool = FileTool.Pdf; break;
                        case "convert": nextTool = FileTool.Convert; break;
                        case "compress": nextTool = FileTool.Compress; break;
                        case "rename": nextTool = FileTool.Rename; break;
                        case "zip": nextTool = FileTool.Zip; break;
                        default: return;
                    }
                    var pdf = current as PdfToolsWindow; var files = current as FileToolsWindow;
                    paths = (pdf != null ? pdf.Rows.AsEnumerable() : files.Rows).Select(row => row.Path).ToArray();
                    current.Close();
                };
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += systemTheme;
                do {
                    nextTool = null;
                    string output = Path.Combine(Files.Store.Root, "Processed");
                    fileToolsWindow = tool == FileTool.Pdf ? (Window)new PdfToolsWindow(paths, output, Files.Store.Add, path => Shell(path, null)) :
                        new FileToolsWindow(tool, paths, output, CommitFileRename, Files.Store.Add, path => Shell(path, null));
                    fileToolsWindow.ShowDialog();
                    if (!nextTool.HasValue) break;
                    tool = nextTool.Value;
                } while (!disposed && !Files.Window.IsClosed);
            }
            finally {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged -= systemTheme;
                ToolDesign.Navigate = previousNavigation; ToolDesign.AppearanceChanged = previousAppearance;
                fileToolsWindow = null; Busy = DialogOpen;
                if (!disposed && !Files.Window.IsClosed) { Files.Window.RefreshItems(); Files.Window.Restore(); }
            }
        }
        internal void CommitFileRename(IDictionary<string, string> changes)
        {
            Store.RemapPaths(changes);
            try { Files.Store.RemapPaths(changes); }
            catch {
                Store.RemapPaths(changes.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase));
                throw;
            }
            Files.Window.RefreshItems();
        }

        internal bool ChangeHotkeys(uint key, uint modifiers, uint captureKey, uint captureModifiers, out string error)
        { return ChangeHotkeys(key, modifiers, captureKey, captureModifiers, Store.Settings.ClearHotkeyKey, Store.Settings.ClearHotkeyModifiers, out error); }

        internal bool ChangeHotkeys(uint key, uint modifiers, uint captureKey, uint captureModifiers, uint clearKey, uint clearModifiers, out string error)
        {
            uint savedClearKey = Store.Settings.ClearHotkeyKey, savedClearModifiers = Store.Settings.ClearHotkeyModifiers;
            uint savedKey = Store.Settings.HotkeyKey, savedModifiers = Store.Settings.HotkeyModifiers;
            uint savedCaptureKey = Store.Settings.CaptureHotkeyKey, savedCaptureModifiers = Store.Settings.CaptureHotkeyModifiers;
            try {
                if (!Sink.TrySetHotkeys(key, modifiers, captureKey, captureModifiers, clearKey, clearModifiers, delegate {
                    Store.Settings.ClearHotkeyKey = clearKey; Store.Settings.ClearHotkeyModifiers = clearModifiers;
                    Store.Settings.HotkeyKey = key; Store.Settings.HotkeyModifiers = modifiers;
                    Store.Settings.CaptureHotkeyKey = captureKey; Store.Settings.CaptureHotkeyModifiers = captureModifiers;
                    Store.Save();
                }, out error)) return false;
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                Store.Settings.ClearHotkeyKey = savedClearKey; Store.Settings.ClearHotkeyModifiers = savedClearModifiers;
                Store.Settings.HotkeyKey = savedKey;
                Store.Settings.HotkeyModifiers = savedModifiers;
                Store.Settings.CaptureHotkeyKey = savedCaptureKey;
                Store.Settings.CaptureHotkeyModifiers = savedCaptureModifiers;
                error = "无法保存快捷键设置，请检查数据文件夹是否可写后再试。";
                return false;
            }
            toggleMenu.Text = "显示 / 隐藏    " + Sink.HotkeyText;
            captureMenu.Text = "区域截图…    " + Sink.CaptureHotkeyText;
            clearMenu.Text = "清空截图栏（保留文件）    " + Sink.ClearHotkeyText;
            Line.RefreshStatus();
            return true;
        }

        internal void ShowHotkeySettings()
        { ShowSettings("Keys"); }

        internal void ShowSettings(string page)
        {
            if (settingsWindow != null) { settingsWindow.ShowPage(page); settingsWindow.Activate(); return; }
            Busy = true;
            Line.Tuck();
            try
            {
                settingsWindow = new SettingsWindow(this);
                settingsWindow.ShowPage(page);
                settingsWindow.UpdateChecking(checkingUpdate);
                settingsWindow.ShowDialog();
            }
            finally { settingsWindow = null; Busy = DialogOpen; }
        }

        internal void Help()
        {
            Busy = true;
            try { MessageBox.Show("Win + Shift + S 或 PrintScreen 截图后，图片自动挂入。\n收集剪贴板图片也会收集从其他应用复制的图片，可在托盘菜单关闭。\n\n鼠标在屏幕顶端停留，或 " + Sink.HotkeyText + "：展开 / 收起。\n右键托盘 → 设置快捷键：录入单键或组合键，保存后立即生效。\n单击：复制图片。双击：用默认图片应用打开。\n长按 0.45 秒：用画图编辑，保存后刷新缩略图。\n鼠标指向截图时，C 复制、A 编辑、D / Delete 取下。\n拖入应用：发送图片或文件副本。\n拖入应用或文件夹：复制，截图继续留在绳上。\n右键：另存为、在文件夹中显示或取下。\n\n" + Sink.CaptureHotkeyText + " 或工具条、托盘 → 区域截图：拖动框选，Esc 取消。\n设置 → 快捷键：自定义显示、区域截图和清空截图栏按键。\n设置 → 截图 → 截图包含鼠标指针：默认关闭。\n开启后先框选，再摆放指针，按 Enter 截图；不改变 Win + Shift + S。\n\n托盘 → 复制最近一张：直接复制，无须展开。\n暂停所有自动收集：同时暂停剪贴板和文件夹，重启后保持。\n开机启动：可选，默认关闭；移动程序后请重新勾选。\n自动检查更新：默认开启，每 24 小时检查并提醒；可关闭。\n安装版下载 EXE 后点击安装更新，覆盖升级；便携版退出后解压 ZIP。\n\n叉号或 D / Delete：收件夹图片进入回收站；外部原文件保留。\n截图每 9 张一组，超过后新建一组，旧组仍保留并随重启恢复。\n在图片或分组条上滚轮向上看旧图、向下看新图；也可点前后箭头或“最新”。\n屏幕较窄时可继续翻看组内其余图片。\n" + Sink.ClearHotkeyText + "：清空全部截图分组，保留全部原文件。\n设置 → 截图：可选拖到未接收区域时取下卡片，默认关闭，Esc 取消保留。\n\nSnapline " + Updates.CurrentVersion + " · Windows 非官方移植版\n基于 Alejandro Buján 的 Tendedero 交互与 MIT 代码。", "Snapline · 使用说明", MessageBoxButton.OK, MessageBoxImage.Information); }
            finally { Busy = DialogOpen; }
        }

        public void Dispose()
        {
            disposed = true;
            if (announcementWindow != null) announcementWindow.Close();
            if (settingsWindow != null) settingsWindow.Close();
            mouse.Stop();
            updateTimer.Stop();
            updateCancel.Cancel();
            updates.Dispose();
            updateCancel.Dispose();
            if (watch != null) watch.Dispose();
            Hover.Dispose();
            Files.Dispose();
            Sink.Dispose();
            tray.Visible = false;
            tray.Dispose();
            icon.Dispose();
            Line.Close();
        }
    }
}
