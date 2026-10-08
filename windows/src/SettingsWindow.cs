using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Snapline
{
    internal sealed class SettingsWindow : Window
    {
        private readonly Controller controller;
        internal readonly Border View;
        private uint key, modifiers;
        private string folder;
        private bool startup;
        internal bool RecordingHotkey { get { return Part<TextBox>("HotkeyInput").IsKeyboardFocusWithin; } }
        private const string KeyHelp = "支持单键和 Ctrl / Alt / Shift / Win 组合。";

        internal SettingsWindow(Controller controller)
        {
            this.controller = controller;
            View = Ui.Load<Border>("Settings");
            Title = "Snapline · 设置";
            Width = 844; Height = 520; ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None; AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = View;
            if (SystemParameters.WorkArea.Height < Height + 24 || SystemParameters.WorkArea.Width < Width + 24) {
                View.Width = 844; View.Height = 520;
                Width = Math.Min(844, SystemParameters.WorkArea.Width - 24);
                Height = Math.Min(520, SystemParameters.WorkArea.Height - 24);
                Content = new Viewbox { Child = View };
            }
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Snapline.ico"))
                Icon = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            ((Grid)((Grid)View.Child).Children[0]).MouseLeftButtonDown += delegate { DragMove(); };
            Click("CloseSettings", Close);
            Click("CancelSettings", Close);
            Click("SaveSettings", Save);
            Click("RestoreKeys", delegate { SetCandidate(Hotkey.DefaultKey, Hotkey.DefaultModifiers); });
            Click("OpenInbox", controller.OpenInbox);
            Click("ChooseFolder", ChooseFolder);
            Click("RemoveAll", delegate { controller.Store.Clear(); Feedback("已取下所有卡片，原文件保留", false); });
            Click("CheckUpdates", controller.RequestUpdate);
            Click("ReleasePage", controller.OpenReleases);
            Click("Usage", controller.Help);
            foreach (string name in new[] { "General", "Keys", "Storage", "About" }) {
                string page = name;
                Part<RadioButton>("Nav" + name).Checked += delegate { ShowPage(page); };
            }
            TextBox input = Part<TextBox>("HotkeyInput");
            InputMethod.SetIsInputMethodEnabled(input, false);
            input.GotKeyboardFocus += delegate { input.BorderBrush = Ui.Brush("Accent"); Part<TextBlock>("KeyHint").Text = "按下按键或组合键；Esc 恢复当前设置，Tab 离开输入框。"; };
            input.LostKeyboardFocus += delegate { input.BorderBrush = Ui.Brush("Line"); };
            Loaded += delegate { if (Part<StackPanel>("KeysPage").Visibility == Visibility.Visible) input.Focus(); };
            input.PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Tab) return;
                e.Handled = true;
                Key pressed = e.Key == Key.System ? e.SystemKey : e.Key;
                if (pressed == Key.Escape) { SetCandidate(controller.Sink.HotkeyKey, controller.Sink.HotkeyModifiers); return; }
                bool win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
                uint flags = ((Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 2u : 0u) |
                    ((Keyboard.Modifiers & ModifierKeys.Alt) != 0 ? 1u : 0u) |
                    ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 4u : 0u) | (win ? 8u : 0u);
                SetCandidate((uint)KeyInterop.VirtualKeyFromKey(pressed), flags);
            };
            var settings = controller.Store.Settings;
            Part<CheckBox>("CollectToggle").IsChecked = settings.ListenClipboard;
            Part<CheckBox>("PauseToggle").IsChecked = settings.CollectionPaused;
            Part<CheckBox>("UpdateToggle").IsChecked = settings.AutoCheckUpdates;
            startup = controller.StartupEnabled;
            Part<CheckBox>("StartupToggle").IsChecked = startup;
            folder = settings.WatchFolder;
            Part<TextBlock>("InboxPath").Text = controller.Store.Inbox;
            Part<TextBlock>("InboxPath").ToolTip = controller.Store.Inbox;
            RefreshFolder();
            Part<TextBlock>("VersionLabel").Text = "Windows · v" + Updates.CurrentVersion;
            Part<TextBlock>("AboutVersion").Text = "v" + Updates.CurrentVersion + " · Windows 预发布";
            SetCandidate(controller.Sink.HotkeyKey, controller.Sink.HotkeyModifiers);
            Part<TextBlock>("KeyHint").Text = KeyHelp;
            foreach (string name in new[] { "CollectToggle", "PauseToggle", "StartupToggle", "UpdateToggle" }) {
                Part<CheckBox>(name).Checked += delegate { Feedback("有未保存的修改", false); };
                Part<CheckBox>(name).Unchecked += delegate { Feedback("有未保存的修改", false); };
            }
            Feedback("设置保存后生效", false);
        }

        internal T Part<T>(string name) where T : class { return (T)View.FindName(name); }
        private void Click(string name, Action action) { Part<Button>(name).Click += delegate { action(); }; }
        private void Feedback(string text, bool error)
        { Part<TextBlock>("SettingsFeedback").Text = text; Part<TextBlock>("SettingsFeedback").ToolTip = text; Part<TextBlock>("SettingsFeedback").Foreground = Ui.Brush(error ? "Accent" : "Muted"); }

        internal void ShowPage(string page)
        {
            foreach (string name in new[] { "General", "Keys", "Storage", "About" })
                Part<StackPanel>(name + "Page").Visibility = name == page ? Visibility.Visible : Visibility.Collapsed;
            Part<RadioButton>("Nav" + page).IsChecked = true;
        }

        internal void CaptureCurrent(uint currentKey, uint currentModifiers)
        { if (RecordingHotkey) SetCandidate(currentKey, currentModifiers); }

        internal bool SetCandidate(uint candidate, uint flags)
        {
            if (Hotkey.IsModifier(candidate)) return false;
            string error = Hotkey.Validate(candidate, flags);
            Part<Button>("SaveSettings").IsEnabled = error == null;
            Part<TextBlock>("KeyHint").Foreground = Ui.Brush(error == null ? "Muted" : "Accent");
            if (error != null) { Part<TextBlock>("KeyHint").Text = error; return false; }
            key = candidate; modifiers = flags;
            Part<TextBox>("HotkeyInput").Text = Hotkey.Text(key, modifiers);
            Part<TextBlock>("KeyHint").Text = (flags == 0 ? "单键会影响其他应用中的同名按键。" : "组合已录入。") + "保存后生效。";
            Feedback("有未保存的修改", false);
            return true;
        }

        private void ChooseFolder()
        {
            using (var dialog = new Forms.FolderBrowserDialog { Description = "选择截图工具保存新图片的文件夹", SelectedPath = folder })
                if (dialog.ShowDialog() == Forms.DialogResult.OK) { folder = dialog.SelectedPath; RefreshFolder(); Feedback("有未保存的修改", false); }
        }

        private void RefreshFolder()
        { Part<TextBlock>("WatchPath").Text = folder; Part<TextBlock>("WatchPath").ToolTip = folder; }

        private void Save()
        {
            string error;
            if (!controller.ApplyPreferences(Part<CheckBox>("CollectToggle").IsChecked == true, Part<CheckBox>("PauseToggle").IsChecked == true,
                Part<CheckBox>("UpdateToggle").IsChecked == true, folder, key, modifiers, out error)) {
                Feedback(error, true);
                Part<TextBlock>("KeyHint").Text = error;
                Part<TextBlock>("KeyHint").Foreground = Ui.Brush("Accent");
                return;
            }
            bool desiredStartup = Part<CheckBox>("StartupToggle").IsChecked == true;
            if (desiredStartup != startup && !controller.SetStartup(desiredStartup, out error)) {
                Feedback("其他设置已保存；" + error, true);
                return;
            }
            DialogResult = true;
        }

        internal void UpdateChecking(bool checking)
        { Part<Button>("CheckUpdates").IsEnabled = !checking; Part<Button>("CheckUpdates").Content = checking ? "正在检查…" : "检查更新"; }
    }
}
