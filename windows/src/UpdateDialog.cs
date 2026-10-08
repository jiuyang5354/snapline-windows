using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Forms = System.Windows.Forms;

namespace Snapline
{
    internal sealed class UpdateDialog : Forms.Form
    {
        private readonly Forms.Label status;
        private readonly Forms.ProgressBar progress;
        private readonly Forms.Button download;
        private readonly Forms.Button close;
        private CancellationTokenSource cancel;
        private bool downloading;

        internal UpdateDialog(Release release, Func<string, IProgress<int>, CancellationToken, Task> transfer, Action<string> open)
        {
            Text = "Snapline · 发现新版本";
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = Forms.AutoScaleMode.Dpi;
            ClientSize = new Size(540, 390);
            FormBorderStyle = Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = Forms.FormStartPosition.CenterScreen;
            Controls.Add(new Forms.Label { Text = "当前 v" + Updates.CurrentVersion + " → " + release.Tag + (release.Prerelease ? "（预发布）" : ""), Location = new Point(20, 18), Size = new Size(500, 28) });
            Controls.Add(new Forms.TextBox { Text = (release.Notes ?? "发布页暂无更新说明。").Replace("\r\n", "\n").Replace("\n", "\r\n"), Multiline = true, ReadOnly = true, ScrollBars = Forms.ScrollBars.Vertical, Location = new Point(20, 52), Size = new Size(500, 190) });
            status = new Forms.Label { Text = "下载并校验便携包；完成后退出旧版，解压运行新版。\n截图和快捷键设置会继续保留。", Location = new Point(20, 253), Size = new Size(500, 46) };
            progress = new Forms.ProgressBar { Location = new Point(20, 306), Size = new Size(500, 8), Visible = false };
            var page = new Forms.Button { Text = "查看发布页", Location = new Point(20, 334), Size = new Size(110, 32) };
            download = new Forms.Button { Text = "下载新版…", Location = new Point(295, 334), Size = new Size(110, 32) };
            close = new Forms.Button { Text = "稍后", Location = new Point(415, 334), Size = new Size(105, 32), DialogResult = Forms.DialogResult.Cancel };
            Controls.AddRange(new Forms.Control[] { status, progress, page, download, close });
            page.Click += delegate { open(release.Page); };
            download.Click += async delegate {
                string path;
                using (var dialog = new Forms.SaveFileDialog { Filter = "ZIP 更新包|*.zip", DefaultExt = "zip", AddExtension = true, FileName = release.Package.Name })
                {
                    if (dialog.ShowDialog(this) != Forms.DialogResult.OK) return;
                    path = dialog.FileName;
                }
                cancel = new CancellationTokenSource();
                downloading = true;
                download.Enabled = false;
                progress.Value = 0;
                progress.Style = Forms.ProgressBarStyle.Marquee;
                progress.Visible = true;
                close.Text = "取消下载";
                status.Text = "正在下载 " + release.Tag + "…";
                try
                {
                    await transfer(path, new Progress<int>(delegate(int value) {
                        if (!IsDisposed) { progress.Style = Forms.ProgressBarStyle.Continuous; progress.Value = value; }
                    }), cancel.Token);
                    status.Text = "下载完成，SHA-256 校验通过。\n退出旧版后解压运行新版；图片与设置会保留。";
                    close.Text = "关闭";
                    download.Text = "已下载";
                    open(path);
                }
                catch (Exception ex)
                {
                    if (!Updates.IsError(ex)) throw;
                    status.Text = "下载未完成：" + (ex is OperationCanceledException ? "已取消或网络超时。" : ex.Message);
                    download.Enabled = true;
                }
                finally
                {
                    downloading = false;
                    cancel.Dispose(); cancel = null;
                    close.Text = "关闭";
                }
            };
            FormClosing += delegate(object sender, Forms.FormClosingEventArgs e) {
                if (downloading) { e.Cancel = true; cancel.Cancel(); status.Text = "正在取消下载…"; }
            };
            CancelButton = close;
            Shown += delegate { download.Focus(); };
        }
    }
}
