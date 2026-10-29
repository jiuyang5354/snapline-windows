using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Snapline
{
    internal sealed class SettingsWindow : Window
    {
        private readonly Controller controller;
        internal readonly Border View;
        internal readonly SettingsMotion Motion;
        private string currentPage = "General";
        private uint key, modifiers, captureKey, captureModifiers, clearKey, clearModifiers;
        private string keyError, captureError, clearError;
        private string folder;
        private string inboxFolder;
        private bool startup;
        private bool feedbackError;
        private string appearance;
        internal bool RecordingHotkey { get { return Part<TextBox>("HotkeyInput").IsKeyboardFocusWithin || Part<TextBox>("CaptureHotkeyInput").IsKeyboardFocusWithin || Part<TextBox>("ClearHotkeyInput").IsKeyboardFocusWithin; } }
        private const string KeyHelp = "支持单键和 Ctrl / Alt / Shift / Win 组合。";

        internal SettingsWindow(Controller controller, Func<bool> animations = null)
        {
            this.controller = controller;
            View = Ui.Load<Border>("Settings");
            Title = "Snapline · 设置";
            Width = 960; Height = 660; ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None; AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = View;
            if (SystemParameters.WorkArea.Height < Height + 24 || SystemParameters.WorkArea.Width < Width + 24) {
                View.Width = 960; View.Height = 660;
                Width = Math.Min(960, SystemParameters.WorkArea.Width - 24);
                Height = Math.Min(660, SystemParameters.WorkArea.Height - 24);
                Content = new Viewbox { Child = View };
            }
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Snapline.ico"))
                Icon = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Part<Grid>("SettingsTitlebar").MouseLeftButtonDown += delegate { DragMove(); };
            Click("CloseSettings", Close);
            Click("CancelSettings", Close);
            Click("SaveSettings", Save);
            Click("RestoreKeys", delegate { SetCandidate(Hotkey.DefaultKey, Hotkey.DefaultModifiers); });
            Click("RestoreCaptureKeys", delegate { SetCaptureCandidate(Hotkey.DefaultCaptureKey, Hotkey.DefaultModifiers); });
            Click("RestoreClearKeys", delegate { SetClearCandidate(Hotkey.DefaultClearKey, Hotkey.DefaultModifiers); });
            Click("OpenInbox", controller.OpenInbox);
            Click("ChooseInbox", ChooseInbox);
            Click("ChooseFolder", ChooseFolder);
            Click("RemoveAll", delegate { controller.Store.Clear(); Feedback("已取下所有卡片，原文件保留", false); });
            Click("CheckUpdates", controller.RequestUpdate);
            Click("ViewAnnouncement", delegate { controller.ShowAnnouncement(); });
            Click("ReleasePage", controller.OpenReleases);
            Click("Usage", controller.Help);
            Click("OpenShelf", controller.OpenFileShelf);
            Part<Button>("OpenShelf").IsEnabled = controller.Files.Window.Enabled;
            Part<Button>("OpenShelf").ToolTip = "关闭设置并打开暂存架；未保存的修改不生效。";
            Click("OpenClippings", delegate { controller.Shell(controller.Files.Store.Clippings, null); });
            Click("ClearShelf", delegate {
                try { controller.Files.Store.Clear(); Feedback("暂存架已清空，原文件与剪藏文件保留", false); }
                catch (System.IO.IOException ex) { Feedback("无法清空暂存架：" + ex.Message, true); }
                catch (UnauthorizedAccessException ex) { Feedback("无法清空暂存架：" + ex.Message, true); }
            });
            foreach (string name in new[] { "General", "Capture", "Keys", "Storage", "Shelf", "About" }) {
                string page = name;
                Part<RadioButton>("Nav" + name).Checked += delegate { ShowPage(page); };
            }
            BindInput("HotkeyInput", 0);
            BindInput("CaptureHotkeyInput", 1);
            BindInput("ClearHotkeyInput", 2);
            Loaded += delegate { if (Part<StackPanel>("KeysPage").Visibility == Visibility.Visible) Part<TextBox>("HotkeyInput").Focus(); };
            var settings = controller.Store.Settings;
            appearance = settings.Appearance == "Dark" || settings.Appearance == "System" ? settings.Appearance : "Light";
            foreach (string mode in new[] { "Light", "Dark", "System" }) {
                string current = mode;
                Part<RadioButton>("Theme" + mode).Checked += delegate { appearance = current; ApplyAppearance(); Feedback("有未保存的修改", false); };
            }
            Part<RadioButton>("Theme" + appearance).IsChecked = true;
            ApplyAppearance();
            Part<CheckBox>("CollectToggle").IsChecked = settings.ListenClipboard;
            Part<CheckBox>("PauseToggle").IsChecked = settings.CollectionPaused;
            Part<CheckBox>("ShowOnCaptureToggle").IsChecked = settings.ShowOnCapture;
            Part<CheckBox>("UpdateToggle").IsChecked = settings.AutoCheckUpdates;
            Part<CheckBox>("CursorToggle").IsChecked = settings.IncludeCursor;
            Part<CheckBox>("BlankDropToggle").IsChecked = settings.RemoveOnBlankDrop;
            Part<CheckBox>("ShelfToggle").IsChecked = settings.FileShelfEnabled;
            Part<CheckBox>("ShelfAnyAppToggle").IsChecked = settings.FileShelfAnyApp;
            Part<CheckBox>("ShelfKeepToggle").IsChecked = settings.FileShelfKeepAfterDragOut;
            startup = controller.StartupEnabled;
            Part<CheckBox>("StartupToggle").IsChecked = startup;
            folder = settings.WatchFolder;
            inboxFolder = controller.Store.Inbox;
            RefreshInbox();
            RefreshFolder();
            Part<TextBlock>("VersionLabel").Text = "Windows · v" + Updates.CurrentVersion;
            Part<TextBlock>("AboutVersion").Text = "v" + Updates.CurrentVersion + " · Windows 预发布";
            SetCandidate(controller.Sink.HotkeyKey, controller.Sink.HotkeyModifiers);
            SetCaptureCandidate(controller.Sink.CaptureHotkeyKey, controller.Sink.CaptureHotkeyModifiers);
            SetClearCandidate(controller.Sink.ClearHotkeyKey, controller.Sink.ClearHotkeyModifiers);
            RefreshKeyHint(KeyHelp);
            foreach (string name in new[] { "CollectToggle", "PauseToggle", "ShowOnCaptureToggle", "StartupToggle", "UpdateToggle", "CursorToggle", "BlankDropToggle", "ShelfToggle", "ShelfAnyAppToggle", "ShelfKeepToggle" }) {
                Part<CheckBox>(name).Checked += delegate { Feedback("有未保存的修改", false); };
                Part<CheckBox>(name).Unchecked += delegate { Feedback("有未保存的修改", false); };
            }
            Feedback("设置保存后生效", false);
            Motion = new SettingsMotion(this, animations);
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemAppearanceChanged;
            Closed += delegate { Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemAppearanceChanged; };
        }

        private void SystemAppearanceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        { if (appearance == "System") Dispatcher.BeginInvoke(new Action(ApplyAppearance)); }

        internal T Part<T>(string name) where T : class { return (T)View.FindName(name); }
        internal void ApplyAppearance()
        {
            bool dark = appearance == "Dark" || (appearance == "System" && Equals(Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1), 0));
            string[] names = { "Paper", "Surface", "Sidebar", "Tint", "Ink", "Muted", "Line", "Accent", "Track", "NavSelected", "PrimaryFill", "PrimaryText" };
            string[] colors = dark ? new[] { "#1D1F20", "#27292B", "#191B1C", "#2B2D2F", "#EEEFEA", "#A3A6A5", "#383B3D", "#EF8B61", "#55595B", "#303335", "#E3E6E3", "#202322" } :
                new[] { "#FAFAF7", "#FFFFFF", "#F2F2EE", "#EEEFEA", "#262B28", "#70766F", "#E2E4DD", "#C74F2D", "#CDD1C9", "#E5E8DF", "#2A302B", "#FFFFFF" };
            if (SystemParameters.HighContrast) {
                for (int i = 0; i < names.Length; i++) View.Resources[names[i]] =
                    names[i] == "Ink" || names[i] == "Muted" ? SystemColors.WindowTextBrush :
                    names[i] == "Accent" || names[i] == "PrimaryFill" ? SystemColors.HighlightBrush :
                    names[i] == "PrimaryText" ? SystemColors.HighlightTextBrush :
                    names[i] == "Line" || names[i] == "Track" ? SystemColors.WindowTextBrush : SystemColors.WindowBrush;
                RefreshKeyHint(Part<TextBlock>("KeyHint").Text);
                if (Motion != null) Motion.RefreshColors();
                return;
            }
            for (int i = 0; i < names.Length; i++) {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i])); brush.Freeze(); View.Resources[names[i]] = brush;
            }
            Part<TextBlock>("SettingsFeedback").Foreground = feedbackError ? Brushes.Firebrick : Brush("Muted");
            RefreshKeyHint(Part<TextBlock>("KeyHint").Text);
            if (Motion != null) Motion.RefreshColors();
        }

        private Brush Brush(string name) { return (Brush)View.FindResource(name); }
        private void Click(string name, Action action) { Part<Button>(name).Click += delegate { action(); }; }
        private void Feedback(string text, bool error)
        { feedbackError = error; Part<TextBlock>("SettingsFeedback").Text = text; Part<TextBlock>("SettingsFeedback").ToolTip = text; Part<TextBlock>("SettingsFeedback").Foreground = error ? Brushes.Firebrick : Brush("Muted"); }

        internal void ShowPage(string page)
        {
            bool changed = currentPage != page;
            currentPage = page;
            foreach (string name in new[] { "General", "Capture", "Keys", "Storage", "Shelf", "About" })
                Part<StackPanel>(name + "Page").Visibility = name == page ? Visibility.Visible : Visibility.Collapsed;
            Part<RadioButton>("Nav" + page).IsChecked = true;
            if (changed && Motion != null) Motion.PageChanged(Part<StackPanel>(page + "Page"));
        }

        internal void CaptureCurrent(uint currentKey, uint currentModifiers)
        {
            if (Part<TextBox>("ClearHotkeyInput").IsKeyboardFocusWithin) SetClearCandidate(currentKey, currentModifiers);
            else if (Part<TextBox>("CaptureHotkeyInput").IsKeyboardFocusWithin) SetCaptureCandidate(currentKey, currentModifiers);
            else if (RecordingHotkey) SetCandidate(currentKey, currentModifiers);
        }

        private void BindInput(string name, int binding)
        {
            TextBox input = Part<TextBox>(name);
            InputMethod.SetIsInputMethodEnabled(input, false);
            input.GotKeyboardFocus += delegate { input.BorderBrush = Brush("Accent"); RefreshKeyHint("按下按键或组合键；Esc 恢复当前设置，Tab 离开输入框。"); };
            input.LostKeyboardFocus += delegate { input.BorderBrush = Brush("Line"); };
            input.PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Tab) return;
                e.Handled = true;
                Key pressed = e.Key == Key.System ? e.SystemKey : e.Key;
                if (pressed == Key.Escape) {
                    SetBinding(binding == 2 ? controller.Sink.ClearHotkeyKey : binding == 1 ? controller.Sink.CaptureHotkeyKey : controller.Sink.HotkeyKey,
                        binding == 2 ? controller.Sink.ClearHotkeyModifiers : binding == 1 ? controller.Sink.CaptureHotkeyModifiers : controller.Sink.HotkeyModifiers, binding); return;
                }
                bool win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
                uint flags = ((Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 2u : 0u) |
                    ((Keyboard.Modifiers & ModifierKeys.Alt) != 0 ? 1u : 0u) |
                    ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 4u : 0u) | (win ? 8u : 0u);
                SetBinding((uint)KeyInterop.VirtualKeyFromKey(pressed), flags, binding);
            };
        }

        internal bool SetCandidate(uint candidate, uint flags)
        { return SetBinding(candidate, flags, 0); }

        internal bool SetCaptureCandidate(uint candidate, uint flags)
        { return SetBinding(candidate, flags, 1); }

        internal bool SetClearCandidate(uint candidate, uint flags)
        { return SetBinding(candidate, flags, 2); }

        private bool SetBinding(uint candidate, uint flags, int binding)
        {
            if (Hotkey.IsModifier(candidate)) return false;
            string error = Hotkey.Validate(candidate, flags);
            if (binding == 2) clearError = error; else if (binding == 1) captureError = error; else keyError = error;
            if (error != null) { RefreshKeyHint(KeyHelp); return false; }
            if (binding == 2) { clearKey = candidate; clearModifiers = flags; }
            else if (binding == 1) { captureKey = candidate; captureModifiers = flags; }
            else { key = candidate; modifiers = flags; }
            Part<TextBox>(binding == 2 ? "ClearHotkeyInput" : binding == 1 ? "CaptureHotkeyInput" : "HotkeyInput").Text = Hotkey.Text(candidate, flags);
            RefreshKeyHint((flags == 0 ? "单键会影响其他应用中的同名按键。" : "组合已录入。") + "保存后生效。");
            Feedback("有未保存的修改", false);
            return true;
        }

        private void RefreshKeyHint(string text)
        {
            string error = keyError ?? captureError ?? clearError;
            if (error == null && ((clearKey == key && clearModifiers == modifiers) || (clearKey == captureKey && clearModifiers == captureModifiers)))
                error = "清空截图栏必须使用独立的快捷键。";
            if (error == null && key == captureKey && modifiers == captureModifiers) error = "显示 / 隐藏与区域截图不能使用同一快捷键。";
            Part<Button>("SaveSettings").IsEnabled = error == null;
            Part<TextBlock>("KeyHint").Foreground = Brush(error == null ? "Muted" : "Accent");
            Part<TextBlock>("KeyHint").Text = error ?? text;
        }

        private void ChooseFolder()
        {
            using (var dialog = new Forms.FolderBrowserDialog { Description = "选择截图工具保存新图片的文件夹", SelectedPath = folder })
                if (dialog.ShowDialog() == Forms.DialogResult.OK) { folder = dialog.SelectedPath; RefreshFolder(); Feedback("有未保存的修改", false); }
        }

        private void ChooseInbox()
        {
            using (var dialog = new Forms.FolderBrowserDialog { Description = "选择截图收件夹；仅新截图保存到此处，旧图片保留原位置", SelectedPath = inboxFolder, ShowNewFolderButton = true })
                if (dialog.ShowDialog() == Forms.DialogResult.OK) SetInboxCandidate(dialog.SelectedPath);
        }

        internal bool SetInboxCandidate(string folder)
        {
            string prepared, error;
            if (!ImageStore.TryPrepareInbox(folder, out prepared, out error)) { Feedback(error, true); return false; }
            inboxFolder = prepared;
            RefreshInbox(); Feedback("有未保存的修改", false);
            return true;
        }

        private void RefreshInbox()
        { Part<TextBlock>("InboxPath").Text = inboxFolder; Part<TextBlock>("InboxPath").ToolTip = inboxFolder; }

        private void RefreshFolder()
        { Part<TextBlock>("WatchPath").Text = folder; Part<TextBlock>("WatchPath").ToolTip = folder; }

        private void Save()
        {
            string error;
            if (!controller.ChangeInbox(inboxFolder, out error)) { Feedback(error, true); return; }
            if (!controller.ApplyPreferences(Part<CheckBox>("CollectToggle").IsChecked == true, Part<CheckBox>("PauseToggle").IsChecked == true,
                Part<CheckBox>("UpdateToggle").IsChecked == true, Part<CheckBox>("CursorToggle").IsChecked == true,
                folder, key, modifiers, captureKey, captureModifiers, clearKey, clearModifiers, out error)) {
                Feedback(error, true);
                Part<TextBlock>("KeyHint").Text = error;
                Part<TextBlock>("KeyHint").Foreground = Brush("Accent");
                return;
            }
            bool desiredStartup = Part<CheckBox>("StartupToggle").IsChecked == true;
            if (!controller.ChangeFileShelf(Part<CheckBox>("ShelfToggle").IsChecked == true, Part<CheckBox>("ShelfAnyAppToggle").IsChecked == true,
                Part<CheckBox>("ShelfKeepToggle").IsChecked == true, out error)) { Feedback("其他设置已保存；" + error, true); return; }
            if (desiredStartup != startup && !controller.SetStartup(desiredStartup, out error)) {
                Feedback("其他设置已保存；" + error, true);
                return;
            }
            string previousAppearance = controller.Store.Settings.Appearance;
            bool previousBlankDrop = controller.Store.Settings.RemoveOnBlankDrop;
            bool previousShowOnCapture = controller.Store.Settings.ShowOnCapture;
            controller.Store.Settings.ShowOnCapture = Part<CheckBox>("ShowOnCaptureToggle").IsChecked == true;
            controller.Store.Settings.RemoveOnBlankDrop = Part<CheckBox>("BlankDropToggle").IsChecked == true;
            controller.Store.Settings.Appearance = appearance;
            try { controller.Store.Save(); }
            catch (System.IO.IOException ex) { controller.Store.Settings.Appearance = previousAppearance; controller.Store.Settings.RemoveOnBlankDrop = previousBlankDrop; controller.Store.Settings.ShowOnCapture = previousShowOnCapture; Feedback("设置保存失败：" + ex.Message, true); return; }
            catch (UnauthorizedAccessException ex) { controller.Store.Settings.Appearance = previousAppearance; controller.Store.Settings.RemoveOnBlankDrop = previousBlankDrop; controller.Store.Settings.ShowOnCapture = previousShowOnCapture; Feedback("设置保存失败：" + ex.Message, true); return; }
            controller.Files.Window.ApplyAppearance(appearance);
            DialogResult = true;
        }

        internal void UpdateChecking(bool checking)
        { Part<Button>("CheckUpdates").IsEnabled = !checking; Part<Button>("CheckUpdates").Content = checking ? "正在检查…" : "检查更新"; }
    }
}
