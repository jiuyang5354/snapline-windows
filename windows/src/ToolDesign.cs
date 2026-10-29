using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace Snapline
{
    internal static class ToolDesign
    {
        internal static Action<Window, string> Navigate { get; set; }
        internal static Action<bool> AppearanceChanged { get; set; }
        private static bool initialized, dark;
        private sealed class Tone : System.ComponentModel.INotifyPropertyChanged
        {
            public Color Color { get; private set; }
            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
            internal void Set(string value) { Color = (Color)ColorConverter.ConvertFromString(value); if (PropertyChanged != null) PropertyChanged(this, new System.ComponentModel.PropertyChangedEventArgs("Color")); }
        }
        private static readonly Dictionary<string, Tone> tones = new Dictionary<string, Tone>();
        private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(State), typeof(ToolDesign));
        private sealed class State
        {
            internal Border Empty, Indicator;
            internal TextBlock Count;
            internal bool Keyboard;
            internal long MotionVersion;
        }
        private static readonly string[] Keys = { "Paper", "Surface", "SurfaceSoft", "Tint", "Ink", "Muted", "Line", "Accent", "Hover", "Active", "PrimaryFill", "PrimaryHover", "PrimaryText", "Success", "Track" };
        private static readonly string[] Light = { "#F6F6F3", "#FFFFFF", "#FAFAF8", "#F0F1ED", "#252A25", "#777D74", "#E6E8E2", "#AD5636", "#F0F1EC", "#F7EBE4", "#30392E", "#45513F", "#FFFFFF", "#4D6C49", "#999F94" };
        private static readonly string[] Dark = { "#1B1E1B", "#232823", "#212521", "#171B17", "#ECEEE6", "#A5AD9F", "#343B33", "#E6A17F", "#2D352B", "#3F332A", "#D8DFCF", "#C4D0B9", "#20271B", "#A3BD96", "#697660" };
        internal static void Initialize()
        {
            if (initialized) return;
            Ui.Initialize();
            foreach (string file in new[] { "ToolTheme", "ToolIcons" })
                using (var stream = typeof(ToolDesign).Assembly.GetManifestResourceStream("Snapline." + file + ".xaml"))
                    Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream));
            for (int i = 0; i < Keys.Length; i++) {
                var tone = new Tone(); tone.Set(Light[i]); tones.Add(Keys[i], tone);
                var brush = new SolidColorBrush(); BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding("Color") { Source = tone }); Application.Current.Resources["Tool." + Keys[i]] = brush;
            }
            initialized = true;
        }
        internal static void Theme(bool value)
        {
            dark = value;
            for (int i = 0; i < Keys.Length; i++) tones[Keys[i]].Set((dark ? Dark : Light)[i]);
            foreach (Window window in Application.Current.Windows) foreach (var button in Logical(window).OfType<Button>().Where(button => button.Tag as string == "ThemeToggle")) button.Content = dark ? "浅色 ☼" : "深色 ◐";
        }
        internal static void ApplyAppearance(string value)
        {
            Initialize();
            Theme(value == "Dark" || (value == "System" && Equals(Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1), 0)));
        }
        internal static Brush Brush(string name) { return (Brush)Application.Current.Resources["Tool." + name]; }
        private static TextBlock Text(string value, double size, string color = "Ink")
        { return new TextBlock { Text = value, FontSize = size, FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), Foreground = Brush(color), TextWrapping = TextWrapping.Wrap }; }
        private static Border Panel(UIElement child, string brush = "Surface", double radius = 10)
        { return new Border { Child = child, Background = Brush(brush), CornerRadius = new CornerRadius(radius), BorderBrush = Brush("Line"), BorderThickness = new Thickness(1) }; }
        internal static FrameworkElement Icon(string name, double size = 20)
        {
            Geometry geometry = Application.Current.TryFindResource("Qi." + name) as Geometry;
            var path = new Path { Data = geometry, StrokeThickness = 1.65, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            path.SetResourceReference(Shape.StrokeProperty, "Tool.Muted");
            var canvas = new Canvas { Width = 24, Height = 24 }; canvas.Children.Add(path);
            return new Viewbox { Child = canvas, Width = size, Height = size, Stretch = Stretch.Uniform };
        }
        private static UIElement Label(string value, string icon)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var glyph = Icon(icon, 17); glyph.Margin = new Thickness(0, 0, 10, 0); row.Children.Add(glyph);
            row.Children.Add(new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center }); return row;
        }
        private static void Route(Window window, string value)
        {
            var pdf = window as PdfToolsWindow; var files = window as FileToolsWindow;
            if ((pdf != null && pdf.Running) || (files != null && files.Running)) return;
            if (Navigate != null) Navigate(window, value);
        }
        private static Button Button(string value, Action action)
        { var button = new Button { Content = value, Style = (Style)Application.Current.FindResource("ToolButton") }; button.Click += delegate { action(); }; return button; }

        private static Grid Build(Window window, string title, string subtitle, UIElement navigation, UIElement files, UIElement inspector, TextBox status, Button close, Button execute, string mode)
        {
            window.Width = Math.Min(1180, SystemParameters.WorkArea.Width - 48); window.Height = Math.Min(790, SystemParameters.WorkArea.Height - 40);
            window.MinWidth = Math.Min(960, window.Width); window.MinHeight = Math.Min(630, window.Height); window.FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
            window.UseLayoutRounding = true; window.SnapsToDevicePixels = true; window.WindowStyle = WindowStyle.None;
            WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 42, ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(10), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
            var state = new State(); window.SetValue(StateProperty, state);
            window.PreviewKeyDown += delegate { state.Keyboard = true; };
            window.PreviewMouseDown += delegate { state.Keyboard = false; };
            var root = new Grid { Background = Brush("Paper") }; root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(43) }); root.RowDefinitions.Add(new RowDefinition());
            var caption = new Grid { Margin = new Thickness(19, 0, 8, 0) }; caption.ColumnDefinitions.Add(new ColumnDefinition()); caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var dot = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(7), Background = Brush("PrimaryFill"), Child = new TextBlock { Text = "S", Foreground = Brush("PrimaryText"), FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }; brand.Children.Add(dot);
            var name = Text("Snapline", 12); name.FontWeight = FontWeights.SemiBold; name.Margin = new Thickness(9, 0, 0, 0); brand.Children.Add(name);
            var separator = Text(" /  文件工作台", 11, "Muted"); separator.Margin = new Thickness(9, 0, 0, 0); brand.Children.Add(separator); caption.Children.Add(brand);
            var system = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(system, 1); caption.Children.Add(system);
            var minimize = Button("−", delegate { window.WindowState = WindowState.Minimized; });
            var maximize = Button("□", delegate { window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; });
            var exit = Button("×", window.Close);
            foreach (var button in new[] { minimize, maximize, exit }) { button.Width = 37; button.Margin = new Thickness(1, 4, 1, 4); button.Padding = new Thickness(0); button.BorderThickness = new Thickness(0); button.Background = Brushes.Transparent; WindowChrome.SetIsHitTestVisibleInChrome(button, true); system.Children.Add(button); }
            root.Children.Add(caption);
            var body = new Grid(); Grid.SetRow(body, 1); root.Children.Add(body); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(188) }); body.ColumnDefinitions.Add(new ColumnDefinition());
            var rail = new Grid { Margin = new Thickness(12, 18, 12, 17) }; rail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); rail.RowDefinitions.Add(new RowDefinition()); rail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var railHeader = new StackPanel(); var context = Text(mode == "pdf" ? "文档工具" : "文件工具", 11, "Muted"); context.Margin = new Thickness(12, 2, 0, 14); railHeader.Children.Add(context); rail.Children.Add(railHeader);
            Grid.SetRow(navigation, 1); rail.Children.Add(navigation);
            var switcher = Button("切换工具  ▾", delegate {
                var menu = new ContextMenu(); foreach (var item in new[] { new { Id = "pdf", Name = "PDF 工具箱" }, new { Id = "convert", Name = "图片转格式" }, new { Id = "compress", Name = "图片压缩" }, new { Id = "rename", Name = "批量重命名" }, new { Id = "zip", Name = "ZIP 打包" } }) {
                    string next = item.Id; var choice = new MenuItem { Header = item.Name }; choice.Click += delegate { Route(window, next); }; menu.Items.Add(choice);
                }
                menu.PlacementTarget = rail; menu.IsOpen = true;
            });
            switcher.ToolTip = "在五个真实文件工具之间切换"; Grid.SetRow(switcher, 2); rail.Children.Add(switcher);
            var sidebar = new Border { Background = Brush("Tint"), BorderBrush = Brush("Line"), BorderThickness = new Thickness(0, 1, 1, 0), Child = rail }; body.Children.Add(sidebar);
            var workspace = new Grid(); workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); workspace.RowDefinitions.Add(new RowDefinition()); workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Grid.SetColumn(workspace, 1); body.Children.Add(workspace);
            var header = new Grid { Margin = new Thickness(26, 22, 26, 21) }; header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var headings = new StackPanel(); var mainTitle = Text(title, 26); mainTitle.FontWeight = FontWeights.SemiBold; headings.Children.Add(mainTitle); var sub = Text(subtitle, 12, "Muted"); sub.Margin = new Thickness(0, 7, 14, 0); headings.Children.Add(sub); header.Children.Add(headings);
            var appearance = Button(dark ? "浅色 ☼" : "深色 ◐", delegate { Theme(!dark); if (AppearanceChanged != null) AppearanceChanged(dark); }); appearance.Tag = "ThemeToggle"; appearance.VerticalAlignment = VerticalAlignment.Top; Grid.SetColumn(appearance, 1); header.Children.Add(appearance); workspace.Children.Add(header);
            var content = new Grid { Margin = new Thickness(26, 0, 26, 18) }; content.ColumnDefinitions.Add(new ColumnDefinition()); content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(286) }); Grid.SetRow(content, 1); workspace.Children.Add(content);
            content.Children.Add(files); Grid.SetColumn(inspector, 2); content.Children.Add(inspector);
            var footer = new Grid { Margin = new Thickness(26, 0, 26, 0), MinHeight = 85 }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            status.Width = double.NaN; status.MinHeight = 0; status.MaxHeight = 70; status.Margin = new Thickness(0, 13, 22, 13); status.Padding = new Thickness(0); status.FontSize = 11; status.BorderThickness = new Thickness(0); status.Background = Brushes.Transparent; status.VerticalAlignment = VerticalAlignment.Center; status.Style = null; footer.Children.Add(status);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; close.Margin = new Thickness(0, 0, 9, 0); actions.Children.Add(close); actions.Children.Add(execute); Grid.SetColumn(actions, 1); footer.Children.Add(actions);
            var footerBorder = new Border { BorderBrush = Brush("Line"), BorderThickness = new Thickness(0, 1, 0, 0), Child = footer }; Grid.SetRow(footerBorder, 2); workspace.Children.Add(footerBorder);
            window.Content = new Border { Child = root, BorderBrush = Brush("Line"), BorderThickness = new Thickness(1) };
            StyleControls(root, status); execute.Style = (Style)Application.Current.FindResource("ToolPrimary"); execute.MinWidth = 112;
            ToolSheen.Attach(execute);
            window.Closed += delegate { state.Indicator = null; };
            return root;
        }
        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        private static void StyleControls(DependencyObject root, TextBox status)
        {
            // Logical children exist before native templates are applied.
            foreach (var text in Logical(root).OfType<TextBlock>()) {
                if (ReferenceEquals(text.Foreground, Ui.Brush("Muted"))) text.Foreground = Brush("Muted");
                else if (ReferenceEquals(text.Foreground, Ui.Brush("Ink"))) text.Foreground = Brush("Ink");
            }
            var controls = Logical(root).OfType<Control>().ToArray();
            foreach (var control in controls) {
                if (control is Button) { control.Style = (Style)Application.Current.FindResource("ToolButton"); BindPress((Button)control); }
                else if (control is TextBox && control != status) control.Style = (Style)Application.Current.FindResource("ToolInput");
                else if (control is PasswordBox) control.Style = (Style)Application.Current.FindResource("ToolPassword");
                else if (control is ComboBox) control.Style = (Style)Application.Current.FindResource("ToolCombo");
                else if (control is Slider) control.Style = (Style)Application.Current.FindResource("ToolSlider");
                if (control is TextBox && ((TextBox)control).IsReadOnly) control.SetBinding(FrameworkElement.ToolTipProperty, new Binding("Text") { Source = control });
            }
        }
        private static IEnumerable<DependencyObject> Logical(DependencyObject root)
        {
            yield return root;
            foreach (object item in LogicalTreeHelper.GetChildren(root)) {
                var child = item as DependencyObject; if (child != null) foreach (var descendant in Logical(child)) yield return descendant;
            }
        }
        private static void BindPress(Button button)
        {
            button.RenderTransformOrigin = new Point(.5, .5); var scale = new ScaleTransform(1, 1); button.RenderTransform = scale;
            Action<double> move = delegate(double value) {
                bool animate = SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast && Mouse.LeftButton == MouseButtonState.Pressed;
                foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty }) {
                    double current = (double)scale.GetValue(property); scale.BeginAnimation(property, null); scale.SetValue(property, value);
                    if (animate) scale.BeginAnimation(property, new DoubleAnimation(current, value, TimeSpan.FromMilliseconds(100)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
                }
            };
            button.PreviewMouseLeftButtonDown += delegate { move(.98); }; button.PreviewMouseLeftButtonUp += delegate { move(1); }; button.LostMouseCapture += delegate { move(1); }; button.MouseLeave += delegate { move(1); };
        }
        private static UIElement FilePanel(Window window, DataGrid grid, UIElement toolbar, string subtitle, Button add)
        {
            grid.BorderThickness = new Thickness(0); grid.Background = Brush("Surface"); grid.GridLinesVisibility = DataGridGridLinesVisibility.None;
            grid.RowStyle = (Style)Application.Current.FindResource("ToolRow"); grid.CellStyle = (Style)Application.Current.FindResource("ToolCell"); grid.ColumnHeaderStyle = (Style)Application.Current.FindResource("ToolColumn");
            grid.EnableRowVirtualization = grid.EnableColumnVirtualization = true;
            grid.Columns[0].Width = 42;
            var checkbox = new FrameworkElementFactory(typeof(CheckBox)); checkbox.SetValue(FrameworkElement.StyleProperty, Application.Current.FindResource("ToolCheck")); checkbox.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); checkbox.SetBinding(CheckBox.IsCheckedProperty, new Binding("Selected") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            ((DataGridTemplateColumn)grid.Columns[0]).CellTemplate = new DataTemplate { VisualTree = checkbox };
            var fileRow = new FrameworkElementFactory(typeof(DockPanel)); fileRow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 8, 10, 8));
            var fileIcon = new FrameworkElementFactory(typeof(Viewbox)); fileIcon.SetValue(FrameworkElement.WidthProperty, 21.0); fileIcon.SetValue(FrameworkElement.HeightProperty, 21.0); fileIcon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 12, 0)); fileIcon.SetValue(DockPanel.DockProperty, Dock.Left);
            var canvas = new FrameworkElementFactory(typeof(Canvas)); canvas.SetValue(FrameworkElement.WidthProperty, 24.0); canvas.SetValue(FrameworkElement.HeightProperty, 24.0);
            var glyphPath = new FrameworkElementFactory(typeof(Path)); glyphPath.SetBinding(Path.DataProperty, new Binding("FullPath") { Converter = new FileGeometryConverter() }); glyphPath.SetResourceReference(Shape.StrokeProperty, "Tool.Muted"); glyphPath.SetValue(Shape.StrokeThicknessProperty, 1.5); glyphPath.SetValue(Shape.StrokeLineJoinProperty, PenLineJoin.Round); canvas.AppendChild(glyphPath); fileIcon.AppendChild(canvas); fileRow.AppendChild(fileIcon);
            var file = new FrameworkElementFactory(typeof(StackPanel)); file.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
            var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding("Name")); name.SetValue(TextBlock.FontSizeProperty, 12.0); name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); file.AppendChild(name);
            var kind = new FrameworkElementFactory(typeof(TextBlock)); kind.SetBinding(TextBlock.TextProperty, new Binding("FullPath") { Converter = new KindConverter() }); kind.SetValue(TextBlock.FontSizeProperty, 10.0); kind.SetResourceReference(TextBlock.ForegroundProperty, "Tool.Muted"); kind.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 5, 0, 0)); file.AppendChild(kind);
            fileRow.AppendChild(file); grid.Columns[1] = new DataGridTemplateColumn { Header = "文件", CellTemplate = new DataTemplate { VisualTree = fileRow }, Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true };
            if (grid.Columns.Count > 2) { grid.Columns[2].Width = new DataGridLength(.85, DataGridLengthUnitType.Star); var column = grid.Columns[2] as DataGridTextColumn; if (column != null) column.ElementStyle = new Style(typeof(TextBlock)) { Setters = { new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis), new Setter(TextBlock.FontSizeProperty, 11.0), new Setter(TextBlock.ForegroundProperty, Brush("Muted")) } }; }
            var content = new Grid(); content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); content.RowDefinitions.Add(new RowDefinition()); content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new Grid { Margin = new Thickness(18, 17, 18, 12) }; var label = Text("待处理文件", 14); label.FontWeight = FontWeights.SemiBold; heading.Children.Add(label);
            var count = Text("", 11, "Muted"); count.HorizontalAlignment = HorizontalAlignment.Right; count.VerticalAlignment = VerticalAlignment.Center; heading.Children.Add(count); content.Children.Add(heading);
            if (toolbar != null) { if (toolbar is FrameworkElement) ((FrameworkElement)toolbar).Margin = new Thickness(14, 0, 14, 13); Grid.SetRow(toolbar, 1); content.Children.Add(toolbar); }
            var table = new Grid(); Grid.SetRow(table, 2); content.Children.Add(table); table.Children.Add(grid);
            var emptyContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28) };
            var glyph = Icon("file-pdf", 39); glyph.Margin = new Thickness(0, 0, 0, 17); emptyContent.Children.Add(glyph); var emptyTitle = Text("添加文件，开始整理", 16); emptyTitle.HorizontalAlignment = HorizontalAlignment.Center; emptyContent.Children.Add(emptyTitle);
            var emptyHelp = Text("选择左侧工具，然后添加对应的 PDF 或图片。", 12, "Muted"); emptyHelp.MaxWidth = 240; emptyHelp.TextAlignment = TextAlignment.Center; emptyHelp.Margin = new Thickness(0, 9, 0, 16); emptyContent.Children.Add(emptyHelp);
            if (add != null) { var choose = Button("添加文件", delegate { add.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); }); choose.HorizontalAlignment = HorizontalAlignment.Center; emptyContent.Children.Add(choose); }
            var empty = new Border { Child = emptyContent, Background = Brush("Surface"), Visibility = Visibility.Collapsed }; table.Children.Add(empty);
            var note = Text(subtitle, 10, "Muted"); note.Margin = new Thickness(18, 12, 18, 12); Grid.SetRow(note, 3); content.Children.Add(note);
            window.Loaded += delegate { var state = (State)window.GetValue(StateProperty); state.Empty = empty; state.Count = count; Update(window, grid); };
            return Panel(content);
        }
        private static void Update(Window window, DataGrid grid)
        {
            var state = window.GetValue(StateProperty) as State; if (state == null) return;
            if (state.Empty != null) state.Empty.Visibility = grid.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (state.Count != null) state.Count.Text = grid.Items.Count.ToString("00") + " 个文件";
        }
        internal static void UpdatePdf(PdfToolsWindow window)
        {
            window.FilesGrid.Items.Filter = delegate(object value) { var row = (FileToolRow)value; return window.Action == PdfAction.Images ? FileBatch.IsInputImage(row.Path) : PdfBatch.IsPdf(row.Path); };
            Update(window, window.FilesGrid);
        }
        private static UIElement Inspector(StackPanel parameters)
        {
            parameters.Margin = new Thickness(18, 19, 18, 19);
            foreach (var text in parameters.Children.OfType<TextBlock>()) { text.TextWrapping = TextWrapping.Wrap; if (text.FontSize < 13) text.LineHeight = 18; }
            return Panel(new ScrollViewer { Content = parameters, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, "SurfaceSoft");
        }
        internal static void Apply(PdfToolsWindow window)
        {
            var old = (Grid)window.Content; var body = (Grid)old.Children[1]; var files = (Grid)body.Children[1]; var toolbar = (WrapPanel)files.Children[0];
            var parameters = (StackPanel)((ScrollViewer)body.Children[2]).Content; ((ScrollViewer)body.Children[2]).Content = null;
            var footer = (Grid)old.Children[3]; var progress = footer.Children[0] as ProgressBar; ((Panel)footer.Children[1]).Children.Clear(); footer.Children.Clear();
            if (progress != null) { progress.Width = 55; toolbar.Children.Add(progress); }
            files.Children.Clear(); body.Children.Clear(); old.Children.Clear(); window.Content = null;
            string[] icons = { "file-png", "album-line", "scissors-horiz", "album-line", "file-pdf", "file-pdf", "file-pdf", "file-pdf", "list-lock", "list-lock" };
            window.ToolList.Margin = new Thickness(0); window.ToolList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            foreach (var button in toolbar.Children.OfType<Button>()) { button.Margin = new Thickness(0, 0, 5, 0); button.Padding = new Thickness(10, 8, 10, 8); }
            for (int i = 0; i < window.ToolList.Items.Count; i++) { var item = (ListBoxItem)window.ToolList.Items[i]; item.Style = (Style)Application.Current.FindResource("ToolNav"); item.ClearValue(Control.PaddingProperty); item.ClearValue(Control.FontSizeProperty); item.Content = Label(PdfToolsWindow.Labels[i], icons[i]); }
            var navigation = new Grid(); var highlight = new Border { CornerRadius = new CornerRadius(7), Height = 37, Background = Brush("Active"), VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, RenderTransform = new TranslateTransform() }; navigation.Children.Add(highlight); navigation.Children.Add(window.ToolList);
            var filePanel = FilePanel(window, window.FilesGrid, toolbar, "勾选处理范围 · 上移 / 下移调整合并顺序", toolbar.Children.OfType<Button>().First());
            Build(window, "PDF 工具箱", "整理页面，组合文档。让文件各就其位。", navigation, filePanel, Inspector(parameters), window.Status, window.CloseButton, window.ExecuteButton, "pdf");
            ((State)window.GetValue(StateProperty)).Indicator = highlight;
            Action<bool> select = delegate(bool animate) {
                var item = window.ToolList.SelectedItem as ListBoxItem; if (item == null || !item.IsArrangeValid) return;
                double target = item.TranslatePoint(new Point(0, 0), navigation).Y; var transform = (TranslateTransform)highlight.RenderTransform; double start = transform.Y; transform.BeginAnimation(TranslateTransform.YProperty, null); transform.Y = target;
                var state = (State)window.GetValue(StateProperty); long version = ++state.MotionVersion;
                if (animate && !state.Keyboard && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast) {
                    var animation = new DoubleAnimation(start, target, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
                    animation.Completed += delegate { if (state.MotionVersion == version) transform.BeginAnimation(TranslateTransform.YProperty, null); };
                    transform.BeginAnimation(TranslateTransform.YProperty, animation);
                }
            };
            window.Loaded += delegate { select(false); }; window.ToolList.SelectionChanged += delegate { window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new System.Action(delegate { if (window.IsLoaded) select(true); })); }; window.SizeChanged += delegate { select(false); };
            UpdatePdf(window);
        }
        internal static void Apply(FileToolsWindow window)
        {
            var old = (Grid)window.Content; var options = (StackPanel)old.Children[1]; var footer = (StackPanel)old.Children[4]; footer.Children.Clear(); old.Children.Clear(); window.Content = null;
            options.Margin = new Thickness(0); foreach (var line in options.Children.OfType<WrapPanel>()) {
                var controls = line.Children.Cast<FrameworkElement>().ToArray(); line.Children.Clear(); line.Margin = new Thickness(0, 0, 0, 7);
                for (int i = 0; i + 1 < controls.Length; i += 2) {
                    if (i > 0 && controls[i + 1] is TextBlock && controls[i] is TextBlock && ((TextBlock)controls[i]).Text.Length == 0) continue;
                    var field = new StackPanel { Margin = new Thickness(0, 0, 0, 5) }; field.SetBinding(FrameworkElement.WidthProperty, new Binding("ActualWidth") { Source = options }); var label = controls[i] as TextBlock;
                    if (label != null) { label.FontSize = 11; label.Foreground = Brush("Muted"); if (label.Text.Length == 0) label.Visibility = Visibility.Collapsed; field.Children.Add(label); }
                    var input = controls[i + 1]; input.Width = double.NaN; input.HorizontalAlignment = HorizontalAlignment.Stretch; input.Margin = new Thickness(0, 6, 0, 0); field.Children.Add(input); line.Children.Add(field);
                    if (input is Slider && label != null) label.SetBinding(TextBlock.TextProperty, new Binding("Value") { Source = input, StringFormat = "有损质量 · {0:0}" });
                }
            }
            foreach (var text in options.Children.OfType<TextBlock>()) {
                text.TextWrapping = TextWrapping.Wrap; text.LineHeight = 18; text.Margin = new Thickness(0, 3, 0, 12); text.FontSize = 11;
                if (text.Text.StartsWith("勾选") || text.Text.StartsWith("支持 ")) text.Visibility = Visibility.Collapsed;
                if (text.Text.StartsWith("长边 0")) text.Text = "长边 0 保持尺寸，只缩小、不放大。";
            }
            var parameters = new StackPanel(); var heading = Text("处理选项", 17); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 0, 0, 16); parameters.Children.Add(heading); parameters.Children.Add(options);
            string mode = window.Tool == FileTool.Convert ? "convert" : window.Tool == FileTool.Compress ? "compress" : window.Tool == FileTool.Rename ? "rename" : "zip";
            var nav = new StackPanel();
            foreach (var item in new[] { new { Id = "pdf", Name = "PDF 工具箱", Icon = "file-pdf" }, new { Id = "convert", Name = "图片转格式", Icon = "file-png" }, new { Id = "compress", Name = "图片压缩", Icon = "file-slim" }, new { Id = "rename", Name = "批量重命名", Icon = "album-line" }, new { Id = "zip", Name = "ZIP 打包", Icon = "file-zip" } }) {
                string next = item.Id; var button = Button(item.Name, delegate { Route(window, next); }); button.Content = Label(item.Name, item.Icon); button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Margin = new Thickness(0, 2, 0, 2); button.BorderThickness = new Thickness(0); button.Background = mode == item.Id ? Brush("Active") : Brushes.Transparent; if (mode == item.Id) button.Foreground = Brush("Accent"); nav.Children.Add(button);
            }
            string subtitle = window.Tool == FileTool.Rename ? "先确认新名称，再整齐地批量更改。" : window.Tool == FileTool.Zip ? "把零散文件，收进一个压缩包。" : window.Tool == FileTool.Compress ? "减轻文件体积，保留清晰细节。" : "换一种格式，继续你的工作。";
            var toolbar = new WrapPanel(); var add = Button("添加文件", window.AddFiles); toolbar.Children.Add(add);
            if (window.Tool == FileTool.Zip) toolbar.Children.Add(Button("添加文件夹", window.AddFolder));
            foreach (var button in toolbar.Children.OfType<Button>()) button.Margin = new Thickness(0, 0, 5, 0);
            window.FilesGrid.IsEnabledChanged += delegate { toolbar.IsEnabled = window.FilesGrid.IsEnabled; };
            var files = FilePanel(window, window.FilesGrid, toolbar, window.Tool == FileTool.Rename ? "新名称实时预览 · 扩展名保留" : "原文件保留 · 同名结果自动编号", add);
            ((System.Collections.Specialized.INotifyCollectionChanged)window.FilesGrid.Items).CollectionChanged += delegate { Update(window, window.FilesGrid); };
            Build(window, FileToolsWindow.Label(window.Tool), subtitle, nav, files, Inspector(parameters), window.Status, window.CloseButton, window.ExecuteButton, mode);
        }
        private sealed class KindConverter : IValueConverter
        {
            public object Convert(object value, Type type, object parameter, CultureInfo culture) { string extension = System.IO.Path.GetExtension((string)value).TrimStart('.'); return extension.Length == 0 ? "文件夹 / 文件" : extension.ToUpperInvariant() + "  文件"; }
            public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
        }
        private sealed class FileGeometryConverter : IValueConverter
        {
            public object Convert(object value, Type type, object parameter, CultureInfo culture) { string extension = System.IO.Path.GetExtension((string)value).ToLowerInvariant(); return Application.Current.FindResource("Qi." + (extension == ".pdf" ? "file-pdf" : extension == ".zip" ? "file-zip" : "file-png")); }
            public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
        }
    }
}
