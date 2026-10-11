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
        private readonly Release release;
        private readonly Func<string, IProgress<int>, CancellationToken, Task> transfer;
        private readonly Action<string> open;
        private readonly Action<string> install;
        private CancellationTokenSource cancel;
        private bool downloading;
        private string downloadedPath;

        internal UpdateDialog(Release release, Func<string, IProgress<int>, CancellationToken, Task> transfer, Action<string> open, Action<string> install)
        {
            this.release = release; this.transfer = transfer; this.open = open; this.install = install;
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
            status = new Forms.Label { Text = release.IsInstaller
                ? "下载并校验 EXE 安装包，完成后点击“安装更新”。\n安装时沿用原目录覆盖，截图与设置保留。"
                : "下载并校验便携包；完成后退出旧版，解压运行新版。\n截图和快捷键设置会继续保留。", Location = new Point(20, 253), Size = new Size(500, 46) };
            progress = new Forms.ProgressBar { Location = new Point(20, 306), Size = new Size(500, 8), Visible = false };
            var page = new Forms.Button { Text = "查看发布页", Location = new Point(20, 334), Size = new Size(110, 32) };
            download = new Forms.Button { Text = release.IsInstaller ? "下载安装包…" : "下载新版…", Location = new Point(295, 334), Size = new Size(110, 32) };
            close = new Forms.Button { Text = "稍后", Location = new Point(415, 334), Size = new Size(105, 32), DialogResult = Forms.DialogResult.Cancel };
            Controls.AddRange(new Forms.Control[] { status, progress, page, download, close });
            page.Click += delegate { open(release.Page); };
            download.Click += async delegate {
                if (downloadedPath != null && release.IsInstaller)
                {
                    try
                    {
                        Updates.Verify(release, downloadedPath);
                        install(downloadedPath);
                        status.Text = "安装向导已打开，请在向导中完成覆盖升级。\n安装器会正常退出旧版，保留截图与设置。";
                        download.Text = "安装程序已打开"; download.Enabled = false;
                    }
                    catch (Exception ex)
                    {
                        if (!Updates.IsError(ex) && !(ex is System.ComponentModel.Win32Exception) && !(ex is InvalidOperationException)) throw;
                        status.Text = "无法开始安装：" + ex.Message;
                        downloadedPath = null; download.Text = "重新下载…"; download.Enabled = true;
                    }
                    return;
                }
                using (var dialog = SaveDialog())
                {
                    if (dialog.ShowDialog(this) != Forms.DialogResult.OK) return;
                    await DownloadTo(dialog.FileName);
                }
            };
            FormClosing += delegate(object sender, Forms.FormClosingEventArgs e) {
                if (downloading) { e.Cancel = true; status.Text = "正在取消下载…"; cancel.Cancel(); }
            };
            CancelButton = close;
            Shown += delegate { download.Focus(); };
        }

        internal Forms.SaveFileDialog SaveDialog()
        {
            return new Forms.SaveFileDialog { Filter = release.IsInstaller ? "EXE 安装包|*.exe" : "ZIP 更新包|*.zip",
                DefaultExt = release.IsInstaller ? "exe" : "zip", AddExtension = true, FileName = release.Package.Name };
        }

        internal async Task DownloadTo(string path)
        {
            cancel = new CancellationTokenSource();
            downloading = true; download.Enabled = false;
            progress.Value = 0; progress.Style = Forms.ProgressBarStyle.Marquee; progress.Visible = true;
            close.Text = "取消下载"; status.Text = "正在下载 " + release.Tag + "…";
            try
            {
                await transfer(path, new Progress<int>(delegate(int value) {
                    if (!IsDisposed) { progress.Style = Forms.ProgressBarStyle.Continuous; progress.Value = value; }
                }), cancel.Token);
                if (release.IsInstaller)
                {
                    downloadedPath = path; download.Text = "安装更新"; download.Enabled = true;
                    status.Text = "安装包下载完成，SHA-256 校验通过。\n点击“安装更新”，在原目录覆盖升级；截图与设置保留。";
                }
                else
                {
                    download.Text = "已下载";
                    status.Text = "下载完成，SHA-256 校验通过。\n退出旧版后解压运行新版；图片与设置会保留。";
                }
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
                downloading = false; cancel.Dispose(); cancel = null; close.Text = "关闭";
            }
        }
    }
}
