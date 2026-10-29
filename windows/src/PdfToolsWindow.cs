using System;
using System.Collections.ObjectModel;
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
    internal sealed class PdfToolsWindow : Window
    {
        internal static readonly PdfAction[] Actions = { PdfAction.Images, PdfAction.Merge, PdfAction.Split, PdfAction.Extract, PdfAction.DeletePages,
            PdfAction.Rotate, PdfAction.PageNumbers, PdfAction.Watermark, PdfAction.Protect, PdfAction.Unprotect };
        internal static readonly string[] Labels = { "图片合成 PDF", "合并 PDF", "逐页拆分", "提取 / 排列页面", "删除页面", "旋转页面", "添加页码", "文字水印", "设置打开密码", "移除打开密码" };
        private static readonly string[] Hints = {
            "按文件列表顺序，一张图片生成一页。页面保持图片比例，不含 OCR 文字层。",
            "按文件列表顺序合并 PDF。任何一个文件无效时会停止，不生成缺页文档。",
            "把所选页面分别保存为独立 PDF，文件名带原页码。",
            "例如 1-3,5；也可输入 3,1,2 调整页序，或重复页码复制页面。",
            "删除指定页码，其他页面保持原顺序。不会删除原 PDF 文件。",
            "仅旋转所选页面；留空页码则处理全部页面。",
            "在所选页面底部叠加原页码与总页数，不替换原有文字。",
            "给所选页面叠加半透明文字水印，支持中文。",
            "为整个 PDF 设置新的打开密码，采用 AES-256。请自行保存密码。",
            "需要填写原密码；只移除你能够正常打开的 PDF 的保护。" };
        internal readonly ObservableCollection<FileToolRow> Rows = new ObservableCollection<FileToolRow>();
        internal readonly ListBox ToolList;
        internal readonly DataGrid FilesGrid;
        internal readonly TextBox PagesInput, NameInput, WatermarkInput, OutputInput, Status;
        internal readonly PasswordBox InputPassword, NewPassword, ConfirmPassword;
        internal readonly ComboBox RotationInput;
        internal readonly Button ExecuteButton, CloseButton;
        private readonly StackPanel parameters;
        private readonly WrapPanel toolbar;
        private readonly TextBlock toolTitle, hint, selectedCount;
        private readonly FrameworkElement pageOptions, nameOptions, rotationOptions, watermarkOptions, inputPasswordOptions, passwordOptions;
        private readonly ProgressBar progress;
        private readonly Action<string[]> addOutputs;
        private readonly Action<string> openFolder;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private bool ready, running, completed, imageMode, configured;
        private string completedOutput;
        private long lastProgress;
        internal bool Running { get { return running; } }
        internal PdfAction Action { get { return Actions[Math.Max(0, ToolList.SelectedIndex)]; } }

        internal PdfToolsWindow(string[] paths, string output, Action<string[]> addOutputs, Action<string> openFolder)
        {
            Ui.Initialize(); ToolDesign.Initialize(); this.addOutputs = addOutputs; this.openFolder = openFolder;
            Title = "Snapline · PDF 工具箱"; Width = Math.Min(1040, SystemParameters.WorkArea.Width - 24); Height = Math.Min(740, SystemParameters.WorkArea.Height - 24);
            MinWidth = Math.Min(740, Width); MinHeight = Math.Min(540, Height); Background = ToolDesign.Brush("Paper"); FontFamily = Ui.Font;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            heading.Children.Add(Ui.Text("PDF 工具箱", 25, false)); heading.Children.Add(Ui.Text("文件留在本机 · 结果另存 · 原文件保留", 12, true)); root.Children.Add(heading);
            var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) });
            body.ColumnDefinitions.Add(new ColumnDefinition()); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(238) });
            Grid.SetRow(body, 1); root.Children.Add(body);
            ToolList = new ListBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(0, 0, 16, 0) };
            foreach (string label in Labels) ToolList.Items.Add(new ListBoxItem { Content = label, Padding = new Thickness(9, 9, 5, 9), FontSize = 12 }); body.Children.Add(ToolList);
            var files = new Grid { Margin = new Thickness(0, 0, 18, 0) }; files.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            files.RowDefinitions.Add(new RowDefinition()); files.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Grid.SetColumn(files, 1); body.Children.Add(files);
            toolbar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            toolbar.Children.Add(Ui.Button("添加文件", AddFiles)); toolbar.Children.Add(Ui.Button("上移", delegate { MoveRow(-1); }));
            toolbar.Children.Add(Ui.Button("下移", delegate { MoveRow(1); })); toolbar.Children.Add(Ui.Button("移出列表", delegate { var row = FilesGrid.SelectedItem as FileToolRow; if (row != null) { Rows.Remove(row); Refresh(); } })); files.Children.Add(toolbar);
            FilesGrid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserSortColumns = false,
                HeadersVisibility = DataGridHeadersVisibility.Column, RowHeaderWidth = 0, SelectionMode = DataGridSelectionMode.Single,
                ItemsSource = Rows, Background = ToolDesign.Brush("Surface"), Foreground = ToolDesign.Brush("Ink"), BorderBrush = ToolDesign.Brush("Line"),
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, HorizontalGridLinesBrush = ToolDesign.Brush("Line"), FontSize = 12 };
            var check = new FrameworkElementFactory(typeof(CheckBox)); check.SetBinding(CheckBox.IsCheckedProperty, new Binding("Selected") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            FilesGrid.Columns.Add(new DataGridTemplateColumn { Header = "处理", CellTemplate = new DataTemplate { VisualTree = check }, Width = 44 });
            FilesGrid.Columns.Add(new DataGridTextColumn { Header = "文件 / 页序", Binding = new Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
            var rowStyle = new Style(typeof(DataGridRow)); rowStyle.Setters.Add(new Setter(ToolTipProperty, new Binding("FullPath"))); FilesGrid.RowStyle = rowStyle;
            Grid.SetRow(FilesGrid, 1); files.Children.Add(FilesGrid);
            selectedCount = Ui.Text("", 11, true); selectedCount.Margin = new Thickness(0, 10, 0, 0); selectedCount.TextWrapping = TextWrapping.Wrap; Grid.SetRow(selectedCount, 2); files.Children.Add(selectedCount);
            parameters = new StackPanel(); var scroll = new ScrollViewer { Content = parameters, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(scroll, 2); body.Children.Add(scroll);
            toolTitle = Ui.Text("", 18, false); hint = Ui.Text("", 12, true); hint.TextWrapping = TextWrapping.Wrap; hint.Margin = new Thickness(0, 9, 0, 14);
            parameters.Children.Add(toolTitle); parameters.Children.Add(hint);
            NameInput = Input("合并文档.pdf"); PagesInput = Input(""); WatermarkInput = Input("样稿"); WatermarkInput.MaxLength = 64;
            RotationInput = new ComboBox { Margin = new Thickness(0, 5, 0, 12), ItemsSource = new[] { "90°", "180°", "270°" }, SelectedIndex = 0 };
            InputPassword = Password(); NewPassword = Password(); ConfirmPassword = Password();
            nameOptions = Field("输出文件名", NameInput); pageOptions = Field("页码 / 顺序（留空为全部）", PagesInput);
            rotationOptions = Field("顺时针旋转", RotationInput); watermarkOptions = Field("水印文字", WatermarkInput);
            inputPasswordOptions = Field("原 PDF 密码（如有）", InputPassword);
            var passwords = new StackPanel(); passwords.Children.Add(Field("新的打开密码", NewPassword)); passwords.Children.Add(Field("再次输入密码", ConfirmPassword)); passwordOptions = passwords;
            foreach (var field in new[] { nameOptions, pageOptions, rotationOptions, watermarkOptions, inputPasswordOptions, passwordOptions }) parameters.Children.Add(field);
            OutputInput = Input(output); OutputInput.IsReadOnly = true; parameters.Children.Add(Field("另存位置", OutputInput)); parameters.Children.Add(Ui.Button("选择位置", ChooseOutput));
            var note = Ui.Text("加密输入在处理后沿用原密码，除非选择移除密码。交互表单与数字签名文档暂不处理。", 11, true); note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(0, 14, 0, 0); parameters.Children.Add(note);
            Status = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 52, MaxHeight = 95, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = ToolDesign.Brush("Muted"), Margin = new Thickness(0, 16, 0, 10) }; Grid.SetRow(Status, 2); root.Children.Add(Status);
            var footer = new Grid(); footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            progress = new ProgressBar { Height = 3, Margin = new Thickness(0, 0, 18, 0), Visibility = Visibility.Collapsed }; footer.Children.Add(progress);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(buttons, 1); footer.Children.Add(buttons);
            CloseButton = Ui.Button("关闭", delegate { if (running) { cancellation.Cancel(); Status.Text = "正在取消，已完成文件保留…"; } else Close(); });
            ExecuteButton = Ui.Button("开始处理", Execute); ExecuteButton.Style = (Style)Application.Current.FindResource("Primary");
            buttons.Children.Add(CloseButton); buttons.Children.Add(ExecuteButton); Grid.SetRow(footer, 3); root.Children.Add(footer); Content = root;
            AddPaths(paths); ToolList.SelectedIndex = Rows.Any(row => FileBatch.IsInputImage(row.Path)) && !Rows.Any(row => PdfBatch.IsPdf(row.Path)) ? 0 : 1;
            ready = true; Configure(); ToolList.SelectionChanged += delegate {
                if (completed) {
                    completed = false; completedOutput = null; ExecuteButton.Content = "开始处理";
                    toolbar.IsEnabled = FilesGrid.IsEnabled = parameters.IsEnabled = true;
                }
                Configure();
            };
            foreach (var input in new[] { PagesInput, NameInput, WatermarkInput }) input.TextChanged += delegate { Refresh(); };
            foreach (var input in new[] { InputPassword, NewPassword, ConfirmPassword }) input.PasswordChanged += delegate { Refresh(); };
            RotationInput.SelectionChanged += delegate { Refresh(); };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (running) { e.Cancel = true; cancellation.Cancel(); } };
            Closed += delegate { cancellation.Dispose(); }; ToolDesign.Apply(this);
        }
        private static TextBox Input(string value) { return new TextBox { Text = value, Padding = new Thickness(7), Margin = new Thickness(0, 5, 0, 12) }; }
        private static PasswordBox Password() { return new PasswordBox { Padding = new Thickness(7), Margin = new Thickness(0, 5, 0, 12), MaxLength = 64 }; }
        private static FrameworkElement Field(string label, FrameworkElement input)
        { var field = new StackPanel(); field.Children.Add(Ui.Text(label, 12, false)); field.Children.Add(input); return field; }
        private bool Supports(string path) { return File.Exists(path) && (Action == PdfAction.Images ? FileBatch.IsInputImage(path) : PdfBatch.IsPdf(path)); }
        internal void AddPaths(string[] paths)
        {
            foreach (string path in FileBatch.Paths(paths)) if (!Rows.Any(row => string.Equals(row.Path, path, StringComparison.OrdinalIgnoreCase))) {
                var row = new FileToolRow { Path = path, Selected = File.Exists(path) }; row.SelectionChanged = delegate { if (ready) Dispatcher.BeginInvoke(new System.Action(Refresh)); }; Rows.Add(row);
            }
        }
        private void AddFiles()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = Action == PdfAction.Images ? "图片|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif;*.webp" : "PDF 文件|*.pdf" };
            if (dialog.ShowDialog(this) == true) { AddPaths(dialog.FileNames); Refresh(); }
        }
        private void ChooseOutput()
        { using (var dialog = new Forms.FolderBrowserDialog { Description = "选择 PDF 处理结果的保存位置", SelectedPath = OutputInput.Text, ShowNewFolderButton = true }) if (dialog.ShowDialog() == Forms.DialogResult.OK) { OutputInput.Text = dialog.SelectedPath; Refresh(); } }
        private void Configure()
        {
            if (!ready) return;
            if (ToolList.SelectedIndex < 0) { ToolList.SelectedIndex = 0; return; }
            bool images = Action == PdfAction.Images;
            if (!configured || imageMode != images) {
                ready = false;
                foreach (var row in Rows) row.Selected = Supports(row.Path);
                ready = true;
            }
            ToolDesign.UpdatePdf(this); configured = true;
            imageMode = images; toolTitle.Text = Labels[ToolList.SelectedIndex]; hint.Text = Hints[ToolList.SelectedIndex];
            if (NameInput.Text == "合并文档.pdf" || NameInput.Text == "合并图片.pdf") NameInput.Text = images ? "合并图片.pdf" : "合并文档.pdf";
            nameOptions.Visibility = images || Action == PdfAction.Merge ? Visibility.Visible : Visibility.Collapsed;
            pageOptions.Visibility = images || Action == PdfAction.Protect || Action == PdfAction.Unprotect ? Visibility.Collapsed : Visibility.Visible;
            rotationOptions.Visibility = Action == PdfAction.Rotate ? Visibility.Visible : Visibility.Collapsed;
            watermarkOptions.Visibility = Action == PdfAction.Watermark ? Visibility.Visible : Visibility.Collapsed;
            inputPasswordOptions.Visibility = images ? Visibility.Collapsed : Visibility.Visible;
            passwordOptions.Visibility = Action == PdfAction.Protect ? Visibility.Visible : Visibility.Collapsed; Refresh();
        }
        internal void MoveRow(int delta)
        {
            if (running || completed) return;
            var row = FilesGrid.SelectedItem as FileToolRow; int index = Rows.IndexOf(row), next = index + delta;
            if (index < 0 || next < 0 || next >= Rows.Count) return;
            Rows.Move(index, next); FilesGrid.SelectedItem = row; Refresh();
        }
        internal void Refresh()
        {
            if (!ready || running || completed) return;
            try {
                var selected = Rows.Where(row => row.Selected).ToArray(); selectedCount.Text = "已选择 " + selected.Length + " / " + Rows.Count + " 个文件 · 列表顺序即处理顺序";
                if (selected.Length == 0) { ExecuteButton.IsEnabled = false; Status.Foreground = ToolDesign.Brush("Muted"); Status.Text = Action == PdfAction.Images ? "添加并勾选图片后即可合成 PDF，原文件保留。" : "添加并勾选 PDF 后即可开始；结果另存，原文件保留。"; return; }
                if (selected.Any(row => !Supports(row.Path))) throw new IOException(Action == PdfAction.Images ? "当前工具只处理现有图片，请取消其他文件的勾选。" : "当前工具只处理现有 PDF，请取消其他文件的勾选。");
                if (Action == PdfAction.Merge || Action == PdfAction.Images) PdfBatch.PdfName(NameInput.Text);
                if (Action == PdfAction.Protect && NewPassword.Password.Length == 0 && ConfirmPassword.Password.Length == 0) { ExecuteButton.IsEnabled = false; Status.Foreground = ToolDesign.Brush("Muted"); Status.Text = "设置并再次输入打开密码后即可开始。请自行保存密码。"; return; }
                if (Action == PdfAction.Protect && (NewPassword.Password.Length == 0 || NewPassword.Password != ConfirmPassword.Password)) throw new IOException("请填写两次相同的非空密码。");
                if (Action == PdfAction.Watermark && string.IsNullOrWhiteSpace(WatermarkInput.Text)) throw new IOException("请输入水印文字。");
                ExecuteButton.IsEnabled = true; Status.Foreground = ToolDesign.Brush("Muted"); Status.Text = "将在本机处理并另存，原文件保留。页码会在读取文档后验证，同名输出自动加编号。";
            } catch (IOException ex) { ExecuteButton.IsEnabled = false; Status.Foreground = Brushes.Firebrick; Status.Text = ex.Message; }
        }
        private void Progress(int done, int total)
        {
            long now = DateTime.UtcNow.Ticks; if (done < total && now - lastProgress < TimeSpan.TicksPerMillisecond * 100) return; lastProgress = now;
            Dispatcher.BeginInvoke(new System.Action(delegate { if (running) Status.Text = "正在处理 " + done + " / " + total + "…"; }));
        }
        internal async void Execute()
        {
            if (completed) { openFolder(completedOutput); return; }
            FilesGrid.CommitEdit(DataGridEditingUnit.Cell, true); FilesGrid.CommitEdit(DataGridEditingUnit.Row, true); Refresh();
            if (running || !ExecuteButton.IsEnabled) return;
            string[] selected = Rows.Where(row => row.Selected).Select(row => row.Path).ToArray(); string output = OutputInput.Text;
            var options = new PdfOptions { Action = Action, Pages = PagesInput.Text, Name = NameInput.Text, Text = WatermarkInput.Text,
                InputPassword = InputPassword.Password, Password = NewPassword.Password, Rotation = new[] { 90, 180, 270 }[RotationInput.SelectedIndex] };
            running = true; ToolList.IsEnabled = toolbar.IsEnabled = FilesGrid.IsEnabled = parameters.IsEnabled = ExecuteButton.IsEnabled = false;
            progress.Visibility = Visibility.Visible; progress.IsIndeterminate = true; CloseButton.Content = "取消处理"; Status.Text = "正在本机处理…";
            try {
                var token = cancellation.Token; var result = await Task.Run(delegate { return PdfBatch.Process(selected, output, options, token, Progress); });
                Status.Text = (result.Cancelled ? "已取消。" : "处理完成。") + "新增 " + result.Outputs.Count + " 个文件，失败 / 跳过 " + result.Errors.Count + " 个。\n保存位置：" + output;
                if (result.Errors.Count > 0) Status.Text += "\n" + string.Join("\n", result.Errors);
                if (result.Errors.Count > 0 && result.Outputs.Count == 0) Status.Foreground = Brushes.Firebrick;
                if (result.Outputs.Count > 0) {
                    try { addOutputs(result.Outputs.ToArray()); } catch (Exception ex) { if (!ImageStore.IsImageError(ex)) throw; Status.Text += "\n结果已保存，加入暂存架失败：" + ex.Message; }
                    completed = true; completedOutput = output; ExecuteButton.Content = "打开结果文件夹";
                }
            } catch (Exception ex) { Status.Text = "处理未完成：" + ex.Message; Status.Foreground = Brushes.Firebrick; }
            finally {
                running = false; progress.IsIndeterminate = false; progress.Visibility = Visibility.Collapsed; CloseButton.Content = "关闭";
                ToolList.IsEnabled = !cancellation.IsCancellationRequested;
                toolbar.IsEnabled = FilesGrid.IsEnabled = parameters.IsEnabled = !completed && !cancellation.IsCancellationRequested;
                ExecuteButton.IsEnabled = completed || !cancellation.IsCancellationRequested;
                string status = Status.Text; var foreground = Status.Foreground; ready = false;
                InputPassword.Clear(); NewPassword.Clear(); ConfirmPassword.Clear();
                ready = true;
                if (!completed && !cancellation.IsCancellationRequested) Refresh();
                Status.Text = status; Status.Foreground = foreground;
            }
        }
    }
}
