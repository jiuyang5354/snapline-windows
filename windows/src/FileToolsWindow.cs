using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Snapline
{
    internal sealed class FileToolRow : INotifyPropertyChanged
    {
        internal string Path;
        internal Action SelectionChanged;
        private bool selected;
        private string after;
        public string Name { get { return System.IO.Path.GetFileName(Path); } }
        public string FullPath { get { return Path; } }
        public string After { get { return after; } set { if (after == value) return; after = value; Changed("After"); } }
        public bool Selected { get { return selected; } set { if (selected == value) return; selected = value; Changed("Selected"); if (SelectionChanged != null) SelectionChanged(); } }
        internal void Remap(string path) { Path = path; Changed("Name"); Changed("FullPath"); }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Changed(string name) { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name)); }
    }

    internal sealed class FileToolsWindow : Window
    {
        internal readonly FileTool Tool;
        internal readonly List<FileToolRow> Rows;
        internal readonly TextBox TemplateInput, StartInput, DigitsInput, EdgeInput, OutputInput, ZipInput;
        internal readonly ComboBox FormatInput;
        internal readonly TextBox PdfNameInput;
        internal readonly Slider QualityInput;
        internal readonly Button ExecuteButton, CloseButton;
        internal readonly TextBox Status;
        internal readonly DataGrid FilesGrid;
        private readonly StackPanel options;
        private readonly Action<IDictionary<string, string>> commitRename;
        private readonly Action<string[]> addOutputs;
        private readonly Action<string> openFolder;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private bool running;
        private bool completed;
        private List<RenameEntry> plan;
        private long lastProgress;
        private readonly FrameworkElement pdfOptions;
        internal bool Running { get { return running; } }

        internal FileToolsWindow(FileTool tool, string[] paths, string defaultOutput,
            Action<IDictionary<string, string>> commitRename, Action<string[]> addOutputs, Action<string> openFolder)
        {
            Ui.Initialize(); ToolDesign.Initialize(); Tool = tool; this.commitRename = commitRename; this.addOutputs = addOutputs; this.openFolder = openFolder;
            Title = "Snapline · " + Label(tool); Width = Math.Min(800, SystemParameters.WorkArea.Width - 24);
            Height = Math.Min(700, SystemParameters.WorkArea.Height - 24); MinWidth = Math.Min(560, Width); MinHeight = Math.Min(520, Height);
            WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = ToolDesign.Brush("Paper"); FontFamily = Ui.Font;
            var root = new Grid { Margin = new Thickness(24) };
            for (int i = 0; i < 5; i++) root.RowDefinitions.Add(new RowDefinition { Height = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            root.Children.Add(Ui.Text(Label(tool), 24, false));
            options = new StackPanel { Margin = new Thickness(0, 10, 0, 14) }; Grid.SetRow(options, 1); root.Children.Add(options);
            options.Children.Add(Ui.Text(tool == FileTool.Rename ? "勾选要处理的文件。执行会更改原文件名，扩展名保留；下方先预览。" :
                "勾选要处理的项目。结果另存为新文件，原文件保留，同名时自动加编号。", 12, true));
            TemplateInput = Input("文件_{n}", 170); StartInput = Input("1", 64); DigitsInput = Input("3", 64);
            EdgeInput = Input(tool == FileTool.Compress ? "1920" : "0", 85); OutputInput = Input(defaultOutput, 420); OutputInput.IsReadOnly = true;
            ZipInput = Input("Snapline-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip", 280);
            FormatInput = new ComboBox { Width = 170, Margin = new Thickness(8, 0, 14, 0), VerticalContentAlignment = VerticalAlignment.Center };
            foreach (var format in tool == FileTool.Compress ? new[] { "JPEG", "PNG", "WebP", "WebP（无损）" } : new[] { "PNG", "JPEG", "BMP", "TIFF", "WebP", "WebP（无损）", "PDF（每张单独）", "PDF（合并多页）" }) FormatInput.Items.Add(format);
            FormatInput.SelectedIndex = 0;
            PdfNameInput = Input("合并图片.pdf", 220);
            var pageOrder = new StackPanel { Orientation = Orientation.Horizontal };
            pageOrder.Children.Add(Ui.Button("上移", delegate { MoveRow(-1); })); pageOrder.Children.Add(Ui.Button("下移", delegate { MoveRow(1); }));
            pdfOptions = Line("PDF 文件名", PdfNameInput, "页序", pageOrder); pdfOptions.Visibility = Visibility.Collapsed;
            QualityInput = new Slider { Minimum = 1, Maximum = 100, Value = 82, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 160, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            if (tool == FileTool.Rename) {
                options.Children.Add(Line("模板", TemplateInput, "起始", StartInput, "位数", DigitsInput));
                options.Children.Add(Ui.Text("{name} = 原名称（不含扩展名）    {n} = 连续编号；例如 项目_{n}、{name}_{n}", 12, true));
            }
            else {
                if (tool != FileTool.Zip) {
                    options.Children.Add(Line("输出格式", FormatInput, "长边像素", EdgeInput));
                    options.Children.Add(pdfOptions);
                    var qualityValue = Ui.Text("82", 12, false); qualityValue.Width = 32;
                    QualityInput.ValueChanged += delegate { qualityValue.Text = ((int)QualityInput.Value).ToString(); };
                    options.Children.Add(Line("有损质量", QualityInput, "", qualityValue));
                    options.Children.Add(Ui.Text("长边 0 保持原尺寸，只缩小不放大。PNG 保留透明；JPEG 透明区域填白。", 12, true));
                    options.Children.Add(Ui.Text("支持 PNG / JPEG / BMP / TIFF / 静态 GIF / WebP。PDF 按图片比例生成页面，不含 OCR 文字层。", 11, true));
                }
                else options.Children.Add(Line("ZIP 名称", ZipInput));
                var choose = Ui.Button("选择位置", ChooseOutput);
                options.Children.Add(Line("另存位置", OutputInput, "", choose));
            }
            Rows = FileBatch.Paths(paths).Select(path => new FileToolRow { Path = path,
                Selected = tool == FileTool.Zip || (File.Exists(path) && (tool == FileTool.Rename || FileBatch.IsInputImage(path))) }).ToList();
            FilesGrid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
                CanUserSortColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeaderWidth = 0,
                Background = ToolDesign.Brush("Surface"), Foreground = ToolDesign.Brush("Ink"), BorderBrush = ToolDesign.Brush("Line"),
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, HorizontalGridLinesBrush = ToolDesign.Brush("Line"),
                ItemsSource = Rows, SelectionMode = DataGridSelectionMode.Single, FontSize = 12 };
            var checkbox = new FrameworkElementFactory(typeof(CheckBox));
            checkbox.SetBinding(CheckBox.IsCheckedProperty, new Binding("Selected") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            checkbox.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center); checkbox.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            FilesGrid.Columns.Add(new DataGridTemplateColumn { Header = "处理", Width = 48, IsReadOnly = true,
                CellTemplate = new DataTemplate { VisualTree = checkbox } });
            FilesGrid.Columns.Add(new DataGridTextColumn { Header = "原文件 / 文件夹", Width = new DataGridLength(1, DataGridLengthUnitType.Star), Binding = new Binding("Name"), IsReadOnly = true });
            FilesGrid.Columns.Add(new DataGridTextColumn { Header = tool == FileTool.Rename ? "新名称预览" : "处理方式", Width = new DataGridLength(1, DataGridLengthUnitType.Star), Binding = new Binding("After"), IsReadOnly = true });
            var rowStyle = new Style(typeof(DataGridRow)); rowStyle.Setters.Add(new Setter(ToolTipProperty, new Binding("FullPath"))); FilesGrid.RowStyle = rowStyle;
            Grid.SetRow(FilesGrid, 2); root.Children.Add(FilesGrid);
            Status = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 95, MinHeight = 52,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = ToolDesign.Brush("Muted"), Margin = new Thickness(0, 12, 0, 8), FontSize = 12 };
            Grid.SetRow(Status, 3); root.Children.Add(Status);
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            CloseButton = Ui.Button("关闭", delegate { if (running) { cancellation.Cancel(); Status.Text = "正在取消，已完成的结果保留…"; } else Close(); });
            CloseButton.Margin = new Thickness(0, 0, 10, 0);
            ExecuteButton = Ui.Button(tool == FileTool.Rename ? "确认重命名" : "开始处理", delegate { Execute(); });
            ExecuteButton.Style = (Style)Application.Current.FindResource("Primary");
            footer.Children.Add(CloseButton); footer.Children.Add(ExecuteButton); Grid.SetRow(footer, 4); root.Children.Add(footer);
            Content = root;
            foreach (var input in new[] { TemplateInput, StartInput, DigitsInput, EdgeInput, ZipInput, PdfNameInput }) input.TextChanged += delegate { RefreshPreview(); };
            FormatInput.SelectionChanged += delegate { RefreshPreview(); };
            foreach (var row in Rows) row.SelectionChanged = delegate { Dispatcher.BeginInvoke(new Action(RefreshPreview)); };
            Closing += (sender, args) => { if (running) { args.Cancel = true; if (Tool != FileTool.Rename) cancellation.Cancel(); } };
            Closed += delegate { cancellation.Dispose(); };
            RefreshPreview(); ToolDesign.Apply(this);
        }

        internal static string Label(FileTool tool)
        { return tool == FileTool.Rename ? "批量重命名" : tool == FileTool.Convert ? "图片转格式" : tool == FileTool.Compress ? "图片压缩" : tool == FileTool.Pdf ? "PDF 工具" : "ZIP 打包"; }

        private static TextBox Input(string text, double width)
        { return new TextBox { Text = text, Width = width, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(8, 0, 14, 0), VerticalContentAlignment = VerticalAlignment.Center }; }

        private static WrapPanel Line(string label, FrameworkElement control, string label2 = null, FrameworkElement control2 = null, string label3 = null, FrameworkElement control3 = null)
        {
            var panel = new WrapPanel { Margin = new Thickness(0, 9, 0, 7) };
            foreach (var pair in new[] { new { Label = label, Control = control }, new { Label = label2, Control = control2 }, new { Label = label3, Control = control3 } }) {
                if (pair.Control == null) continue;
                var text = Ui.Text(pair.Label, 12, false); text.VerticalAlignment = VerticalAlignment.Center;
                panel.Children.Add(text); panel.Children.Add(pair.Control);
            }
            return panel;
        }

        private void ChooseOutput()
        {
            using (var dialog = new Forms.FolderBrowserDialog { Description = "选择处理结果的保存位置", SelectedPath = OutputInput.Text, ShowNewFolderButton = true })
                if (dialog.ShowDialog() == Forms.DialogResult.OK) { OutputInput.Text = dialog.SelectedPath; RefreshPreview(); }
        }

        internal void AddPaths(IEnumerable<string> paths)
        {
            if (running || completed) return;
            var existing = new HashSet<string>(Rows.Select(row => row.Path), StringComparer.OrdinalIgnoreCase);
            foreach (string path in FileBatch.Paths(paths)) if (existing.Add(path)) {
                var row = new FileToolRow { Path = path, Selected = Tool == FileTool.Zip || (File.Exists(path) && (Tool == FileTool.Rename || FileBatch.IsInputImage(path))) };
                row.SelectionChanged = delegate { Dispatcher.BeginInvoke(new Action(RefreshPreview)); }; Rows.Add(row);
            }
            FilesGrid.Items.Refresh(); RefreshPreview();
        }
        internal void AddFiles()
        {
            if (running || completed) return;
            var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true,
                Filter = Tool == FileTool.Convert || Tool == FileTool.Compress ? "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp" : "所有文件|*.*" };
            if (dialog.ShowDialog(this) == true) AddPaths(dialog.FileNames);
        }
        internal void AddFolder()
        {
            if (Tool != FileTool.Zip || running || completed) return;
            using (var dialog = new Forms.FolderBrowserDialog { Description = "选择要加入 ZIP 的文件夹" })
                if (dialog.ShowDialog() == Forms.DialogResult.OK) AddPaths(new[] { dialog.SelectedPath });
        }

        private string[] SelectedPaths() { return Rows.Where(row => row.Selected).Select(row => row.Path).ToArray(); }

        internal void MoveRow(int delta)
        {
            if (running || completed || (string)FormatInput.SelectedItem != "PDF（合并多页）") return;
            FilesGrid.CommitEdit(DataGridEditingUnit.Cell, true); FilesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var row = FilesGrid.SelectedItem as FileToolRow; int index = Rows.IndexOf(row), next = index + delta;
            if (index < 0 || next < 0 || next >= Rows.Count) return;
            Rows.RemoveAt(index); Rows.Insert(next, row); FilesGrid.Items.Refresh(); FilesGrid.SelectedItem = row; RefreshPreview();
        }

        internal void RefreshPreview()
        {
            if (running || completed) return;
            try {
                bool mergePdf = (string)FormatInput.SelectedItem == "PDF（合并多页）";
                pdfOptions.Visibility = Tool == FileTool.Convert && mergePdf ? Visibility.Visible : Visibility.Collapsed;
                var selected = SelectedPaths();
                foreach (var row in Rows.Where(row => !row.Selected)) row.After = "不处理";
                if (selected.Length == 0) throw new IOException("请勾选至少一个要处理的项目。");
                if (Tool == FileTool.Rename) {
                    int start, digits;
                    if (!int.TryParse(StartInput.Text, out start) || !int.TryParse(DigitsInput.Text, out digits)) throw new IOException("起始编号和位数须为整数。");
                    plan = FileBatch.PlanRename(selected, TemplateInput.Text, start, digits);
                    var names = plan.ToDictionary(e => e.Source, e => e.After, StringComparer.OrdinalIgnoreCase);
                    foreach (var row in Rows.Where(row => row.Selected)) row.After = names[row.Path];
                }
                else if (Tool == FileTool.Zip) { FileBatch.ValidateName(ZipInput.Text); foreach (var row in Rows.Where(row => row.Selected)) row.After = "保留原件，加入 ZIP"; }
                else {
                    int edge;
                    if (!int.TryParse(EdgeInput.Text, out edge) || edge < 0 || edge > 16384) throw new IOException("长边须为 0–16384 的整数。");
                    if (selected.Any(path => !File.Exists(path) || !FileBatch.IsInputImage(path))) throw new IOException("请勾选现有的 PNG、JPEG、BMP、TIFF、静态 GIF 或 WebP 图片。");
                    if (((string)FormatInput.SelectedItem).StartsWith("WebP") && !WebpCodec.Available) throw new IOException("WebP 需要 64 位 Windows 和完整的 Converters 文件夹。");
                    string pdfName = mergePdf ? PdfBatch.PdfName(PdfNameInput.Text) : null; int page = 0;
                    foreach (var row in Rows.Where(row => row.Selected)) row.After = mergePdf ? "第 " + (++page) + " 页 → " + pdfName : "另存 " + FormatInput.SelectedItem + (edge > 0 ? " · 长边 ≤ " + edge : " · 保持尺寸");
                }
                QualityInput.IsEnabled = (string)FormatInput.SelectedItem == "JPEG" || (string)FormatInput.SelectedItem == "WebP";
                ExecuteButton.IsEnabled = true; Status.Foreground = ToolDesign.Brush("Muted");
                Status.Text = "已选择 " + selected.Length + " / " + Rows.Count + " 项。" + (Tool == FileTool.Rename ? "确认后更改原文件名；冲突时停止，不覆盖其他文件。" :
                    Tool == FileTool.Compress ? "压缩后显示实际体积；无损格式不使用质量参数，结果可能比原图更大。" : mergePdf ? "按列表顺序一图一页；选中行后用上移/下移调整。合并遇到无效图片会停止，不生成缺页文档。" : "所有操作均在本机完成。");
            }
            catch (Exception ex) {
                if (!(ex is IOException) && !(ex is ArgumentException) && !(ex is OverflowException) && !(ex is UnauthorizedAccessException)) throw;
                foreach (var row in Rows.Where(row => row.Selected)) row.After = "待处理";
                ExecuteButton.IsEnabled = false; Status.Foreground = Brushes.Firebrick; Status.Text = ex.Message;
            }
        }

        internal async void Execute()
        {
            FilesGrid.CommitEdit(DataGridEditingUnit.Cell, true); FilesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            RefreshPreview(); if (running || completed || !ExecuteButton.IsEnabled) return;
            var selected = SelectedPaths(); string output = OutputInput.Text, format = (string)FormatInput.SelectedItem, zipName = ZipInput.Text, pdfName = PdfNameInput.Text;
            int quality = (int)QualityInput.Value, edge = 0; int.TryParse(EdgeInput.Text, out edge);
            running = true; options.IsEnabled = false; FilesGrid.IsEnabled = false; ExecuteButton.IsEnabled = false;
            CloseButton.Content = Tool == FileTool.Rename ? "正在重命名…" : "取消处理"; CloseButton.IsEnabled = Tool != FileTool.Rename;
            Status.Foreground = ToolDesign.Brush("Muted"); Status.Text = "正在处理…";
            var token = cancellation.Token;
            try {
                if (Tool == FileTool.Rename) {
                    var currentPlan = plan;
                    // Keep name changes and both path stores in one dispatcher turn. Watcher
                    // callbacks must not prune temporarily staged screenshot paths in between.
                    FileBatch.Rename(currentPlan, commitRename);
                    foreach (var row in Rows) {
                        var renamed = currentPlan.FirstOrDefault(entry => string.Equals(entry.Source, row.Path, StringComparison.OrdinalIgnoreCase));
                        if (renamed != null) row.Remap(renamed.Target);
                    }
                    Status.Text = "已重命名 " + currentPlan.Count + " 个文件，暂存架与截图栏的文件路径已同步。";
                }
                else {
                    BatchResult result;
                    if (Tool == FileTool.Zip) {
                        string zipped = await Task.Run(delegate { return FileBatch.Zip(selected, output, zipName, token,
                            name => Progress("正在打包：" + name)); });
                        result = new BatchResult(); result.Outputs.Add(zipped);
                    }
                    else result = await Task.Run(delegate { return FileBatch.Images(selected, output, format, quality, edge, token,
                        (done, total) => Progress("正在处理 " + done + " / " + total), pdfName); });
                    string summary = (result.Cancelled ? "已取消。" : "处理完成。") + "新增 " + result.Outputs.Count + " 个文件，失败 / 跳过 " + result.Errors.Count + " 个。\n保存位置：" + output;
                    if (result.InputBytes > 0) summary += "\n成功项体积：" + (result.InputBytes / 1024.0).ToString("N1") + " KB → " + (result.OutputBytes / 1024.0).ToString("N1") + " KB（" + (result.OutputBytes * 100.0 / result.InputBytes).ToString("F1") + "%）";
                    if (result.Errors.Count > 0) summary += "\n" + string.Join("\n", result.Errors);
                    if (result.Outputs.Count > 0) {
                        try { addOutputs(result.Outputs.ToArray()); }
                        catch (Exception ex) {
                            if (!(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                            summary += "\n文件已保存，但加入暂存架失败：" + ex.Message;
                        }
                    }
                    Status.Text = summary;
                }
                completed = true; ExecuteButton.Content = Tool == FileTool.Rename ? "已完成" : "打开输出文件夹";
                if (Tool != FileTool.Rename) { ExecuteButton.IsEnabled = true; ExecuteButton.Click += delegate { openFolder(output); }; }
            }
            catch (OperationCanceledException) { completed = true; Status.Text = "已取消 ZIP 打包，未完成的压缩包已清理，原文件保留。"; }
            catch (Exception ex) {
                // User-selected files can disappear or become unavailable while a batch runs.
                Status.Foreground = Brushes.Firebrick; Status.Text = "操作未完成：" + ex.Message;
                if (Tool == FileTool.Rename) { options.IsEnabled = true; FilesGrid.IsEnabled = true; }
                else completed = true;
            }
            finally {
                running = false; CloseButton.IsEnabled = true; CloseButton.Content = "关闭";
                if (!completed) RefreshPreview();
            }
        }

        private void Progress(string text)
        {
            long now = DateTime.UtcNow.Ticks;
            if (now - lastProgress < TimeSpan.TicksPerMillisecond * 100) return;
            lastProgress = now;
            Dispatcher.BeginInvoke(new Action(delegate { if (running) Status.Text = text; }));
        }
    }
}
