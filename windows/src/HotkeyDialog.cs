using System;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace Snapline
{
    internal sealed class HotkeyInput : Forms.TextBox
    {
        internal event Action Waiting;
        internal event Action<uint, uint> Recorded;

        internal HotkeyInput()
        {
            ReadOnly = true;
            ShortcutsEnabled = false;
            ImeMode = Forms.ImeMode.Disable;
            TextAlign = Forms.HorizontalAlignment.Center;
            AccessibleName = "快捷键录入";
        }

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            Text = "请按下按键或组合键…";
            if (Waiting != null) Waiting();
        }

        protected override bool IsInputKey(Forms.Keys keyData) { return true; }

        protected override bool ProcessCmdKey(ref Forms.Message message, Forms.Keys keyData)
        {
            Record((uint)(keyData & Forms.Keys.KeyCode), Hotkey.Modifiers(keyData, WindowsHeld()));
            return true;
        }

        protected override void OnKeyDown(Forms.KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            Record((uint)e.KeyCode, Hotkey.Modifiers(e.KeyData, WindowsHeld()));
        }

        private static bool WindowsHeld()
        {
            return (Native.GetAsyncKeyState(0x5b) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x5c) & 0x8000) != 0;
        }

        internal void Record(uint key, uint modifiers)
        {
            if (!Focused) return;
            if (Hotkey.IsModifier(key)) return;
            if (Recorded != null) Recorded(key, modifiers);
        }
    }

    internal sealed class HotkeyDialog : Forms.Form
    {
        private const string Hint = "支持单键和 Ctrl / Alt / Shift / Win 组合。单键会影响其他应用中的同名按键。";
        private readonly HotkeyInput input;
        private readonly Forms.Button save;
        private readonly Forms.Label status;
        private uint key;
        private uint modifiers;

        internal HotkeyDialog(uint currentKey, uint currentModifiers, Func<uint, uint, string> apply)
        {
            Text = "Snapline · 设置快捷键";
            ClientSize = new Size(460, 230);
            FormBorderStyle = Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = Forms.FormStartPosition.CenterScreen;
            AutoScaleMode = Forms.AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            Font = SystemFonts.MessageBoxFont;
            Controls.Add(new Forms.Label { Text = "显示 / 隐藏截图晾衣绳", Location = new Point(20, 16), Size = new Size(420, 22) });
            Controls.Add(new Forms.Label { Text = "当前快捷键：" + Hotkey.Text(currentKey, currentModifiers), Location = new Point(20, 43), Size = new Size(420, 22) });
            input = new HotkeyInput { Location = new Point(20, 77), Size = new Size(420, 30) };
            status = new Forms.Label { Text = Hint, Location = new Point(20, 116), Size = new Size(420, 60) };
            var restore = new Forms.Button { Text = "恢复默认", Location = new Point(20, 187), Size = new Size(100, 30) };
            save = new Forms.Button { Text = "保存", Location = new Point(250, 187), Size = new Size(90, 30), Enabled = false };
            var cancel = new Forms.Button { Text = "取消", Location = new Point(350, 187), Size = new Size(90, 30), DialogResult = Forms.DialogResult.Cancel };
            Controls.AddRange(new Forms.Control[] { input, status, restore, save, cancel });
            input.Waiting += delegate { key = 0; save.Enabled = false; status.Text = Hint; status.ForeColor = SystemColors.ControlText; };
            input.Recorded += SetCandidate;
            restore.Click += delegate { SetCandidate(Hotkey.DefaultKey, Hotkey.DefaultModifiers); };
            save.Click += delegate {
                string error = apply(key, modifiers);
                if (error == null) { DialogResult = Forms.DialogResult.OK; Close(); }
                else { status.Text = error; status.ForeColor = Color.Firebrick; }
            };
            AcceptButton = save;
            CancelButton = cancel;
            Shown += delegate { input.Focus(); };
        }

        private void SetCandidate(uint newKey, uint newModifiers)
        {
            string error = Hotkey.Validate(newKey, newModifiers);
            if (error != null) { status.Text = error; status.ForeColor = Color.Firebrick; save.Enabled = false; return; }
            key = newKey;
            modifiers = newModifiers;
            input.Text = Hotkey.Text(key, modifiers);
            status.Text = (modifiers == 0 ? "单键会影响其他应用中的同名按键。\n" : "") + "点击保存立即生效，重启后继续使用。";
            status.ForeColor = SystemColors.ControlText;
            save.Enabled = true;
            save.Focus();
        }

        internal void CaptureCurrent(uint currentKey, uint currentModifiers) { input.Record(currentKey, currentModifiers); }
    }
}
