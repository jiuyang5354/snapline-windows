// Adapted from jiuyang5354/Dropline- v0.1.0 (MIT), commit 51f2d7c. See NOTICE.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Collections.Specialized;
using System.Windows.Interop;

namespace Snapline.Dropline
{
    internal enum ShelfState { Hidden, Tab, Open }

    // Native floating shelf; file ownership and transfer rules remain in the existing handlers.
    internal sealed class ShelfWindow : Window
    {
        private readonly ShelfStore store;
        private readonly Grid scene = new Grid();
        private readonly DispatcherTimer collapse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        private readonly DispatcherTimer hover = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        private readonly DispatcherTimer settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(295) };
        private readonly ScaleTransform progress = new ScaleTransform(0, 1);
        private readonly ScaleTransform edgeProgress = new ScaleTransform(0, 1);
        private readonly TranslateTransform edgeSlide = new TranslateTransform();
        private bool emptyReveal, edgeOpened, emptyVisited, edgeLatched;
        private DateTime? edgeSince, edgeAwaySince, lastEdgePoll;
        private readonly RectangleGeometry outline = new RectangleGeometry();
        private readonly RectangleGeometry revealClip = new RectangleGeometry();
        private System.Windows.Shapes.Path backdrop;
        private FrameworkElement panelContent;
        private FrameworkElement anchor;
        private WrapPanel files;
        private readonly List<Tuple<Animatable, DependencyProperty, double>> clocks = new List<Tuple<Animatable, DependencyProperty, double>>();
        private readonly Dictionary<string, ImageSource> thumbnails = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private FrameworkElement activeSurface;
        private StackPanel toolStrip;
        private Button toolLaunch;
        private int selectedTool;
        private string appearance = "Light";
        private Border shell;
        private static readonly string[] ToolLabels = { "重命名", "转格式", "压缩", "打包", "PDF" };
        private static readonly string[] ToolIcons = {
            "M3,4 L15,4 M9,4 L9,16 M5,16 L13,16 M16,10 L20,14 M15,17 L20,12",
            "M3,6 L16,6 M13,3 L16,6 L13,9 M17,14 L4,14 M7,11 L4,14 L7,17",
            "M3,3 L8,8 M8,4 L8,8 L4,8 M17,17 L12,12 M12,16 L12,12 L16,12 M3,17 L8,12 M4,12 L8,12 L8,16 M17,3 L12,8 M12,4 L12,8 L16,8",
            "M3,6 L10,2 L17,6 L17,15 L10,19 L3,15 Z M3,6 L10,10 L17,6 M10,10 L10,19 M6,4 L13,8",
            "M5,2 L12,2 L17,7 L17,18 L5,18 Z M12,2 L12,7 L17,7 M8,11 L14,11 M8,14 L12,14"
        };
        private bool expanded, busy, dark;
        private bool closed;
        internal bool IsClosed { get { return closed; } }
        private ShelfState state = ShelfState.Hidden;
        private bool externalDrag, draggingOut, dragOver;
        private Point pressPoint;
        private ShelfItem pressedItem;
        private Border dropHint;
        public bool KeepAfterDragOut { get; set; }
        internal event Action<string> Error;
        internal event Action<Snapline.FileTool, string[]> ToolRequested;
        internal bool IsInteracting { get { return draggingOut || dragOver || busy; } }
        internal bool Enabled = true;
        public IntPtr Handle { get; private set; }
        public ShelfState State { get { return state; } }
        private string status = "拖入暂存，拖出使用";
        private Brush Paper { get { return Brush(dark ? "#252726" : "#FAFAF7"); } }
        private Brush Ink { get { return Brush(dark ? "#EFEFE7" : "#262B28"); } }
        private Brush Muted { get { return Brush(dark ? "#A7ADA6" : "#7C837B"); } }
        private Brush Line { get { return Brush(dark ? "#424640" : "#E1E4DC"); } }
        private Brush Tile { get { return Brush(dark ? "#30332E" : "#F0F2EC"); } }
        private static Brush Brush(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex); b.Freeze(); return b; }
        private static readonly Brush Accent = Brush("#D95B31");

        public ShelfWindow(ShelfStore store)
        {
            this.store = store;
            Title = "Snapline · 文件暂存架";
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
            UseLayoutRounding = true; Content = scene; AllowDrop = true;
            Resources.Add(typeof(Button), XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>
              <Setter Property='Background' Value='Transparent'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Padding' Value='10,7'/>
              <Setter Property='Cursor' Value='Hand'/><Setter Property='FontSize' Value='12'/>
              <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='surface' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}' CornerRadius='8'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
                <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Opacity' Value='0.72'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='surface' Property='Opacity' Value='0.48'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='#D95B31'/><Setter TargetName='surface' Property='BorderThickness' Value='1'/></Trigger></ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value></Setter></Style>"));
            Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ScrollBar'>
              <Setter Property='Width' Value='7'/><Setter Property='Background' Value='Transparent'/>
              <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Track x:Name='PART_Track' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Orientation='Vertical' IsDirectionReversed='True' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}' ViewportSize='{TemplateBinding ViewportSize}'><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='#7C837B' Opacity='0.4' CornerRadius='3' Margin='2,0'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></ControlTemplate></Setter.Value></Setter>
            </Style>"));
            collapse.Tick += delegate { collapse.Stop(); CollapseIfIdle(); };
            hover.Tick += delegate { hover.Stop(); if (!IsInteracting && anchor.IsMouseOver) SetExpanded(true); };
            settle.Tick += delegate { FinishMotion(); };
            MouseLeave += delegate { hover.Stop(); if (!draggingOut && expanded) collapse.Start(); };
            MouseEnter += delegate { collapse.Stop(); };
            DragEnter += OnDragEnter; DragOver += OnDragOver;
            DragLeave += OnDragLeave;
            Drop += OnDrop;
            Closed += delegate { closed = true; collapse.Stop(); hover.Stop(); FinishMotion(); progress.Changed -= ProgressChanged; edgeProgress.Changed -= ProgressChanged; };
            progress.Changed += ProgressChanged;
            edgeProgress.Changed += ProgressChanged;
            store.Items.CollectionChanged += OnItemsChanged;
            SourceInitialized += delegate { Handle = new WindowInteropHelper(this).Handle; Native.MakeToolWindow(Handle); };
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemAppearanceChanged;
            Closed += delegate { store.Items.CollectionChanged -= OnItemsChanged; Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemAppearanceChanged; };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Escape) { SetExpanded(false, false); e.Handled = true; }
            };
            Rebuild();
        }
        private TextBlock Text(string text, double size, Brush color)
        { return new TextBlock { Text = text, FontSize = size, Foreground = color, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }; }
        private Button Button(string label, Action action)
        { var button = new Button { Content = label, Foreground = Ink }; button.Click += delegate { Run(action); }; return button; }
        private Border Frame(UIElement child, double radius)
        { return new Border { Child = child, Background = Paper, CornerRadius = new CornerRadius(radius), BorderBrush = Line, BorderThickness = new Thickness(1) }; }
        private static DropShadowEffect Shadow(double blur, double depth, double opacity)
        { return new DropShadowEffect { Color = Color.FromRgb(32, 41, 31), BlurRadius = blur, ShadowDepth = depth, Opacity = opacity, Direction = 270 }; }

        private bool MotionAllowed { get { return SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast && !(InputManager.Current.MostRecentInputDevice is KeyboardDevice); } }
        private void Tween(Animatable target, DependencyProperty property, double from, double to, int milliseconds, bool morph = false)
        {
            if (Math.Abs(from - to) < .001) { target.SetValue(property, to); return; }
            target.SetValue(property, from);
            // One segment, retargeted from the sampled visual position rather than replayed.
            var animation = new DoubleAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds)), morph ? new KeySpline(.77, 0, .175, 1) : new KeySpline(.23, 1, .32, 1)));
            target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
            int existing = clocks.FindIndex(pair => pair.Item1 == target && pair.Item2 == property);
            if (existing < 0) clocks.Add(Tuple.Create(target, property, to)); else clocks[existing] = Tuple.Create(target, property, to);
            settle.Interval = TimeSpan.FromMilliseconds(Math.Max(295, milliseconds + 25));
            settle.Stop(); settle.Start();
        }
        private void FinishMotion()
        {
            settle.Stop(); foreach (var pair in clocks) { pair.Item1.SetValue(pair.Item2, pair.Item3); pair.Item1.BeginAnimation(pair.Item2, null); } clocks.Clear();
            if (shell != null) shell.CacheMode = null; if (anchor != null) anchor.CacheMode = null;
            if (!closed && emptyReveal && state == ShelfState.Hidden && edgeProgress.ScaleX <= 0) Hide();
        }
        private void LayoutScene()
        { scene.Measure(new Size(Width, Height)); scene.Arrange(new Rect(0, 0, Width, Height)); scene.UpdateLayout(); }
        private void ProgressChanged(object sender, EventArgs e) { Present(); }
        private void Present()
        {
            if (shell == null || anchor == null || backdrop == null || panelContent == null) return;
            double p = emptyReveal ? 1 : Math.Max(0, Math.Min(1, progress.ScaleX));
            var rect = new Rect((Width - 188) * (1 - p) + 12 * p, ((Height - 220) / 2 + 58) * (1 - p) + 12 * p, 156 + (Width - 180) * p, 106 + (Height - 130) * p);
            outline.Rect = rect; outline.RadiusX = outline.RadiusY = 12 + 10 * p;
            revealClip.Rect = new Rect(rect.X - 12, rect.Y - 12, rect.Width, rect.Height);
            revealClip.RadiusX = revealClip.RadiusY = outline.RadiusX;
            backdrop.Opacity = Math.Min(1, p * 8); anchor.Opacity = emptyReveal ? 0 : Math.Max(0, 1 - p * 3);
            panelContent.Opacity = Math.Max(0, Math.Min(1, (p - .90) / .10));
            shell.Opacity = p == 0 ? 0 : 1;
            shell.IsHitTestVisible = p > .65; anchor.IsHitTestVisible = !emptyReveal && !expanded;
            edgeSlide.X = emptyReveal ? (Width + 12) * (1 - edgeProgress.ScaleX) : 0;
            activeSurface = emptyReveal || expanded ? (FrameworkElement)shell : anchor;
        }
        private void SetExpanded(bool value, bool animate = true)
        { SetState(value ? ShelfState.Open : store.Items.Count > 0 ? ShelfState.Tab : ShelfState.Hidden, animate); }
        private void Transition(bool value, bool animate = true)
        {
            collapse.Stop(); hover.Stop();
            bool changed = expanded != value; expanded = value;
            if (!animate || !MotionAllowed) { FinishMotion(); progress.ScaleX = value ? 1 : 0; Present(); return; }
            if (!changed) return;
            shell.CacheMode = new BitmapCache(); anchor.CacheMode = new BitmapCache();
            Tween(progress, ScaleTransform.ScaleXProperty, progress.ScaleX, value ? 1 : 0, value ? 270 : 180, value);
            Present();
        }
        private void HoverStack()
        {
            if (busy || !Enabled || externalDrag || draggingOut || dragOver) return;
            if (progress.ScaleX > .001) SetExpanded(true);
            else { hover.Stop(); hover.Start(); }
        }
        private void Rebuild()
        {
            if (closed) return;
            FinishMotion(); scene.Children.Clear(); scene.RenderTransform = edgeSlide;
            bool wasEmpty = emptyReveal;
            emptyReveal = store.Items.Count == 0;
            if (emptyReveal) { edgeProgress.ScaleX = state == ShelfState.Open ? 1 : 0; progress.ScaleX = 1; }
            else if (wasEmpty) { progress.ScaleX = state == ShelfState.Open ? 1 : 0; edgeProgress.ScaleX = 0; }
            collapse.Interval = TimeSpan.FromMilliseconds(emptyReveal ? 420 : 280);
            // Both surfaces persist across transitions; only opacity and transforms change.
            UpdateBounds();
            thumbnails.Clear();
            backdrop = new System.Windows.Shapes.Path { Data = outline, Fill = Paper, Stroke = Line, StrokeThickness = 1, IsHitTestVisible = false, Effect = Shadow(22, 5, .18) };
            scene.Children.Add(backdrop);
            Expanded(); Collapsed(); Present();
        }
        private UIElement Cover(ShelfItem item, double width, double height)
        {
            string path = item.Paths.FirstOrDefault();
            if (path != null && File.Exists(path) && new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(path).ToLowerInvariant())) {
                try {
                    ImageSource image; if (!thumbnails.TryGetValue(path, out image)) { image = ShelfStore.Thumbnail(path, 140); thumbnails[path] = image; }
                    return new Image { Source = image, Width = width, Height = height, Stretch = Stretch.UniformToFill };
                }
                catch (Exception ex) { if (!ImageStore.IsImageError(ex)) throw; }
            }
            var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            string type = path != null && Directory.Exists(path) ? "DIR" : (Path.GetExtension(path ?? "").TrimStart('.').ToUpperInvariant());
            content.Children.Add(Text(item.IsStack ? "FILES" : type.Length == 0 ? "FILE" : type, 16, Muted));
            var rule = new Border { Height = 2, Width = 28, Background = Accent, Margin = new Thickness(0, 9, 0, 0) }; content.Children.Add(rule);
            return new Grid { Width = width, Height = height, Background = Tile, Children = { content } };
        }
        private void Collapsed()
        {
            var stack = new Canvas { Width = 220, Height = 220, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), Background = Brushes.Transparent, Focusable = true };
            var items = store.Items.Take(3).ToArray();
            for (int index = Math.Max(0, items.Length - 1); index >= 0; index--) {
                var item = items.Length == 0 ? null : items[index];
                var content = new StackPanel();
                if (item != null) {
                    var image = Cover(item, 142, 70); image.Clip = new RectangleGeometry(new Rect(0, 0, 142, 70), 7, 7); image.Opacity = index == 0 ? 1 : .62;
                    content.Children.Add(image); var name = Text(item.Title, 10, Ink); name.Margin = new Thickness(4, 6, 4, 0); content.Children.Add(name);
                } else content.Children.Add(Text("＋  拖入暂存", 12, Muted));
                var back = Frame(content, 12); back.Width = 156; back.Height = 106; back.Padding = new Thickness(6);
                Canvas.SetLeft(back, 48 - index * 18); Canvas.SetTop(back, 58 - index * 19);
                back.RenderTransformOrigin = new Point(.5, .5); back.RenderTransform = new RotateTransform(-index * 4);
                Snapline.PhysicalCard.Attach(back);
                back.Effect = Shadow(15, 4, index == 0 ? .19 : .10); stack.Children.Add(back);
            }
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(3), Background = Accent, Margin = new Thickness(0, 0, 7, 0) });
            label.Children.Add(Text("暂存  ·  " + store.Items.Sum(item => item.Paths.Count), 11, Ink));
            var count = Frame(label, 12); count.Padding = new Thickness(11, 5, 11, 5); Canvas.SetLeft(count, 83); Canvas.SetTop(count, 181);
            stack.Children.Add(count); stack.ToolTip = "暂存架 · 点击或悬停展开";
            stack.MouseEnter += delegate(object sender, MouseEventArgs e) { if (e.StylusDevice == null) HoverStack(); };
            stack.MouseLeave += delegate { hover.Stop(); };
            stack.MouseLeftButtonUp += delegate { SetExpanded(true); };
            stack.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter || e.Key == Key.Space) { SetExpanded(true, false); e.Handled = true; } };
            anchor = stack; scene.Children.Add(stack);
        }
        private void Expanded()
        {
            var body = new DockPanel { Margin = new Thickness(18) };
            var header = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new StackPanel(); title.Children.Add(Text("Snapline", 10, Muted));
            var name = Text("暂存架", 23, Ink); name.FontWeight = FontWeights.SemiBold; name.Margin = new Thickness(0, 4, 0, 0); title.Children.Add(name); header.Children.Add(title);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            actions.Children.Add(Button("清空", delegate { Run(store.Clear); }));
            var close = Button("×", delegate { SetExpanded(false); }); close.ToolTip = "收起暂存架"; actions.Children.Add(close); Grid.SetColumn(actions, 1); header.Children.Add(actions);
            DockPanel.SetDock(header, Dock.Top); body.Children.Add(header);
            var footer = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            var strip = new DockPanel { LastChildFill = false };
            var summary = Text(store.Items.Sum(item => item.Paths.Count) + " 个文件 · " + store.Items.Count + " 组", 11, Muted); DockPanel.SetDock(summary, Dock.Left); strip.Children.Add(summary);
            var fold = Button("收起  ›", delegate { SetExpanded(false); }); DockPanel.SetDock(fold, Dock.Right); strip.Children.Add(fold); footer.Children.Add(strip);
            toolStrip = new StackPanel { Orientation = Orientation.Horizontal };
            var toolFrame = Frame(toolStrip, 12); toolFrame.Background = Tile; toolFrame.Padding = new Thickness(5); toolFrame.Margin = new Thickness(0, 8, 0, 6); footer.Children.Add(toolFrame);
            var toolAction = new DockPanel(); var hint = Text("选好工具后，先预览再执行", 10, Muted); DockPanel.SetDock(hint, Dock.Left); toolAction.Children.Add(hint);
            toolLaunch = Button("打开重命名  ›", delegate { LaunchTool((FileTool)selectedTool, store.Items.SelectMany(item => item.Paths).ToArray()); });
            toolLaunch.Foreground = Accent; DockPanel.SetDock(toolLaunch, Dock.Right); toolAction.Children.Add(toolLaunch); footer.Children.Add(toolAction);
            BuildTools(false);
            var caption = Text(status, 11, Muted); caption.TextWrapping = TextWrapping.Wrap; footer.Children.Add(caption);
            DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
            var drop = Frame(Text("＋  拖到这里，加入暂存", 12, Muted), 12); drop.Background = Tile; drop.Padding = new Thickness(0, 16, 0, 16); ((TextBlock)drop.Child).HorizontalAlignment = HorizontalAlignment.Center; drop.Margin = new Thickness(0, 12, 0, 0);
            dropHint = drop;
            DockPanel.SetDock(drop, Dock.Bottom); body.Children.Add(drop);
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            files = wrap;
            foreach (var item in store.Items) wrap.Children.Add(Item(item));
            if (store.Items.Count == 0) { var empty = Text("文件可以先放在这里\n需要时再拖走", 14, Muted); empty.TextWrapping = TextWrapping.Wrap; empty.Margin = new Thickness(16, 35, 16, 35); wrap.Children.Add(empty); }
            body.Children.Add(new ScrollViewer { Content = wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            var panel = Frame(body, 22); panel.Margin = new Thickness(12); panel.Background = Brushes.Transparent; panel.BorderBrush = Brushes.Transparent; panel.Clip = revealClip; panelContent = body;
            panel.MouseEnter += delegate { collapse.Stop(); if (!expanded && !busy && progress.ScaleX > .001) SetExpanded(true); };
            shell = panel; scene.Children.Add(panel);
        }
        private void BuildTools(bool animate)
        {
            var previous = toolStrip.Children.OfType<Button>().Select(button => button.TranslatePoint(new Point(0, 0), toolStrip).X).ToArray();
            toolStrip.Children.Clear();
            for (int i = 0; i < ToolLabels.Length; i++) {
                int index = i; var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                content.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(ToolIcons[i]), Stroke = selectedTool == i ? Accent : Muted, StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = 18, Height = 18, Stretch = Stretch.Uniform });
                if (selectedTool == i) {
                    var label = Text(ToolLabels[i], 11, Ink); label.Margin = new Thickness(7, 0, 0, 0); content.Children.Add(label);
                    if (animate) { var ink = Ink.CloneCurrentValue(); label.Foreground = ink; Tween(ink, System.Windows.Media.Brush.OpacityProperty, 0, 1, 125); }
                }
                var button = Button("", delegate { selectedTool = index; BuildTools(MotionAllowed); }); button.Content = content;
                button.Width = selectedTool == i ? 104 : 44; button.Height = 34; button.Padding = new Thickness(5); button.Margin = new Thickness(0, 0, 4, 0); button.ToolTip = ToolLabels[i];
                button.Background = selectedTool == i ? Paper : Brushes.Transparent; toolStrip.Children.Add(button);
            }
            toolLaunch.Content = "打开" + ToolLabels[selectedTool] + "  ›"; toolLaunch.IsEnabled = store.Items.Count > 0 || selectedTool == (int)Snapline.FileTool.Pdf;
            LayoutScene();
            if (InputManager.Current.MostRecentInputDevice is KeyboardDevice) ((Button)toolStrip.Children[selectedTool]).Focus();
            if (animate && previous.Length == ToolLabels.Length) for (int i = 0; i < ToolLabels.Length; i++) {
                var button = (Button)toolStrip.Children[i]; double target = button.TranslatePoint(new Point(0, 0), toolStrip).X;
                var shift = new TranslateTransform(); button.RenderTransform = shift; Tween(shift, TranslateTransform.XProperty, previous[i] - target, 0, 160);
            }
        }
        private void LaunchTool(FileTool tool, string[] paths)
        {
            if (paths.Length == 0 && tool != Snapline.FileTool.Pdf) return;
            busy = true; hover.Stop(); collapse.Stop(); FinishMotion();
            try { RequestTool(tool, paths); } finally { busy = false; }
        }
        private Border Item(ShelfItem item)
        {
            var content = new StackPanel { Margin = new Thickness(6) };
            var cover = Cover(item, 114, 72); cover.Clip = new RectangleGeometry(new Rect(0, 0, 114, 72), 8, 8);
            var image = Frame(cover, 8); image.ClipToBounds = true; content.Children.Add(image);
            var title = Text(item.Title, 11, Ink); title.Margin = new Thickness(2, 9, 0, 0); content.Children.Add(title);
            string path = item.Paths.FirstOrDefault(); var detail = Text(item.IsStack ? item.Paths.Count + " 个文件" : Directory.Exists(path) ? "文件夹" : Path.GetExtension(path).TrimStart('.').ToUpperInvariant() + " 文件", 10, Muted); detail.Margin = new Thickness(2, 4, 0, 0); content.Children.Add(detail);
            var tile = new Border { Child = content, Width = 132, Margin = new Thickness(0, 0, 4, 9), Background = Brushes.Transparent, CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand, ToolTip = item.Title };
            tile.MouseEnter += delegate { tile.Background = Tile; }; tile.MouseLeave += delegate { tile.Background = Brushes.Transparent; };
            tile.Tag = item;
            Snapline.PhysicalCard.Attach(tile);
            tile.PreviewMouseLeftButtonDown += (s, e) => {
                pressedItem = item; pressPoint = e.GetPosition(this);
                if (e.ClickCount == 2) { Open(item); e.Handled = true; }
            };
            tile.PreviewMouseMove += (s, e) => {
                if (pressedItem != item || e.LeftButton != MouseButtonState.Pressed) return;
                Point p = e.GetPosition(this);
                if (Math.Abs(p.X - pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                pressedItem = null; Run(delegate { DragOut(tile, item); });
            };
            tile.PreviewMouseLeftButtonUp += delegate { pressedItem = null; };
            tile.ContextMenu = MakeMenu(item);
            return tile;
        }

        private void UpdateBounds()
        {
            Rect area = SystemParameters.WorkArea;
            Width = Math.Min(380, area.Width - 24); Height = Math.Min(600, area.Height - 24);
            Left = area.Right - Width - 12; Top = area.Top + (area.Height - Height) / 2;
        }
        public void ExternalDragStarted()
        { if (!Enabled) return; externalDrag = true; hover.Stop(); collapse.Stop(); SetState(ShelfState.Open); }
        public void ExternalDragEnded()
        { externalDrag = false; collapse.Stop(); collapse.Start(); }
        public void Toggle()
        {
            if (!Enabled) return;
            ResetEmptyEdge();
            if (state == ShelfState.Open) SetState(store.Items.Count > 0 ? ShelfState.Tab : ShelfState.Hidden);
            else SetState(ShelfState.Open);
        }
        public void Restore() { ResetEmptyEdge(); SetState(Enabled && store.Items.Count > 0 ? ShelfState.Tab : ShelfState.Hidden); }
        private void CollapseIfIdle()
        {
            if (externalDrag || draggingOut || dragOver || busy) return;
            if (emptyReveal && (edgeOpened || !emptyVisited)) return;
            if (activeSurface != null && activeSurface.IsMouseOver) { collapse.Start(); return; }
            SetExpanded(false);
        }
        private void SetState(ShelfState next, bool animate = false)
        {
            if (closed) return;
            if (!Enabled) next = ShelfState.Hidden;
            state = next;
            if (store.Items.Count == 0) { SetEmptyState(next == ShelfState.Open, animate); return; }
            if (next == ShelfState.Hidden) { Transition(false, false); Hide(); return; }
            UpdateBounds();
            if (!IsVisible) Show();
            Transition(next == ShelfState.Open, animate);
        }
        private void SetEmptyState(bool open, bool animate)
        {
            collapse.Stop(); hover.Stop(); emptyReveal = true;
            bool wasOpen = expanded; expanded = open;
            if (!open) { if (wasOpen) edgeLatched = true; edgeSince = null; }
            UpdateBounds();
            if (open && !IsVisible) { edgeProgress.ScaleX = 0; Present(); Show(); }
            if (!animate || !MotionAllowed || !IsVisible) {
                FinishMotion(); edgeProgress.ScaleX = open ? 1 : 0; Present();
                if (!open) Hide();
                return;
            }
            shell.CacheMode = new BitmapCache();
            Tween(edgeProgress, ScaleTransform.ScaleXProperty, edgeProgress.ScaleX, open ? 1 : 0, open ? 320 : 220);
            Present();
        }
        internal void ResetEmptyEdge()
        { edgeSince = edgeAwaySince = lastEdgePoll = null; edgeOpened = emptyVisited = edgeLatched = false; }
        internal void PollEmptyEdge(System.Drawing.Point cursor, bool down, bool blocked, DateTime now)
        {
            if (closed || !Enabled || store.Items.Count != 0) { ResetEmptyEdge(); return; }
            if (lastEdgePoll != null && (now - lastEdgePoll.Value).TotalMilliseconds > 200) edgeSince = edgeAwaySince = null;
            lastEdgePoll = now;
            if (blocked) {
                edgeSince = edgeAwaySince = null;
                if (edgeOpened && state == ShelfState.Open) SetState(ShelfState.Hidden);
                return;
            }
            if (down || IsInteracting || externalDrag) { edgeSince = edgeAwaySince = null; return; }
            var display = System.Windows.Forms.Screen.PrimaryScreen;
            var bounds = display.Bounds; var work = display.WorkingArea;
            bool atEdge = cursor.X >= bounds.Right - 4 && cursor.X < bounds.Right && cursor.Y >= work.Top + 4 && cursor.Y < work.Bottom;
            if (!atEdge) { edgeSince = null; edgeLatched = false; }
            if (state != ShelfState.Open) {
                if (!atEdge || edgeLatched) return;
                if (edgeSince == null) edgeSince = now;
                if ((now - edgeSince.Value).TotalMilliseconds >= 280) {
                    edgeOpened = true; emptyVisited = false; edgeLatched = true; edgeSince = null;
                    SetState(ShelfState.Open, true);
                }
                return;
            }
            bool inside = false;
            if (IsVisible && shell.ActualWidth > 0) {
                var a = shell.PointToScreen(new Point()); var b = shell.PointToScreen(new Point(shell.ActualWidth, shell.ActualHeight));
                inside = cursor.X >= a.X && cursor.X < b.X && cursor.Y >= a.Y && cursor.Y < b.Y;
            }
            if (inside) emptyVisited = true;
            if (atEdge || inside) { edgeAwaySince = null; collapse.Stop(); return; }
            if (!edgeOpened && !emptyVisited) return;
            if (edgeAwaySince == null) edgeAwaySince = now;
            if ((now - edgeAwaySince.Value).TotalMilliseconds >= 420) { edgeAwaySince = null; SetState(ShelfState.Hidden, true); }
        }
        private void OnItemsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            Rebuild();
            if (state == ShelfState.Tab && store.Items.Count == 0) SetState(ShelfState.Hidden);
            else if (state == ShelfState.Hidden && store.Items.Count > 0) Restore();
        }
        internal void ApplyAppearance(string value)
        {
            appearance = value;
            bool next = value == "Dark" || (value == "System" && object.Equals(Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1), 0));
            if (dark == next) return;
            dark = next; Rebuild();
        }
        private void SystemAppearanceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        { if (appearance == "System") Dispatcher.BeginInvoke(new Action(delegate { ApplyAppearance(appearance); })); }
        private ContextMenu MakeMenu(ShelfItem item)
        {
            var menu = new ContextMenu();
            menu.Items.Add(MenuItem("打开", delegate { Open(item); }));
            menu.Items.Add(MenuItem("在资源管理器中显示", delegate { Reveal(item); }));
            menu.Items.Add(MenuItem("复制路径", delegate { Run(delegate { Clipboard.SetText(string.Join(Environment.NewLine, item.Paths)); }); }));
            if (item.IsStack)
            {
                menu.Items.Add(MenuItem("拆开这一叠", delegate
                {
                    Run(delegate { store.Split(item); });
                }));
            }
            menu.Items.Add(new Separator());
            foreach (var tool in new[] { Snapline.FileTool.Rename, Snapline.FileTool.Convert, Snapline.FileTool.Compress, Snapline.FileTool.Zip, Snapline.FileTool.Pdf }) {
                var selectedTool = tool;
                menu.Items.Add(MenuItem(Snapline.FileToolsWindow.Label(tool) + "…", delegate { RequestTool(selectedTool, item.Paths.ToArray()); }));
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("移出暂存架", delegate { Run(delegate { store.Remove(item); }); }));
            menu.Opened += delegate { busy = true; hover.Stop(); collapse.Stop(); };
            menu.Closed += delegate { busy = false; collapse.Start(); };
            return menu;
        }

        private ContextMenu ToolsMenu(Func<string[]> paths)
        {
            var menu = new ContextMenu();
            foreach (var tool in new[] { Snapline.FileTool.Rename, Snapline.FileTool.Convert, Snapline.FileTool.Compress, Snapline.FileTool.Zip, Snapline.FileTool.Pdf }) {
                var selectedTool = tool;
                menu.Items.Add(MenuItem(Snapline.FileToolsWindow.Label(tool) + "…", delegate { RequestTool(selectedTool, paths()); }));
            }
            menu.Opened += delegate { busy = true; hover.Stop(); collapse.Stop(); };
            menu.Closed += delegate { busy = false; collapse.Start(); };
            return menu;
        }

        private void RequestTool(Snapline.FileTool tool, string[] paths)
        { if (ToolRequested != null) ToolRequested(tool, paths); }

        internal void RefreshItems() { Rebuild(); }

        private static MenuItem MenuItem(string header, RoutedEventHandler click)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += click;
            return mi;
        }

        private void DragOut(UIElement source, ShelfItem item)
        {
            item.PruneMissing();
            if (item.Paths.Count == 0) { store.Remove(item); return; }

            var data = DragData(item);

            hover.Stop(); FinishMotion();
            draggingOut = true;
            DragDropEffects result;
            try
            {
                result = DragDrop.DoDragDrop(source, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            }
            catch (Exception ex) { if (!IsDropError(ex)) throw; Report(ex); return; }
            finally
            {
                draggingOut = false;
                var motion = Snapline.PhysicalCard.Get(source as FrameworkElement);
                if (motion != null) motion.Release();
            }

            if (result == DragDropEffects.None) { collapse.Start(); return; }
            Run(delegate { CompleteDragOut(item, result); });
            collapse.Start();
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            dragOver = true;
            collapse.Stop();
            SetState(ShelfState.Open);
            UpdateDropEffect(e);
            if (!draggingOut) dropHint.BorderBrush = Accent;
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            UpdateDropEffect(e);
        }

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            dragOver = false;
            dropHint.BorderBrush = Line;
            collapse.Start();
        }

        private void UpdateDropEffect(DragEventArgs e)
        {
            if (draggingOut) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
            try { e.Effects = DropEffect(e.Data, e.AllowedEffects); }
            catch (Exception ex) { if (!IsDropError(ex)) throw; e.Effects = DragDropEffects.None; }
            e.Handled = true;
        }

        internal static DragDropEffects DropEffect(IDataObject data, DragDropEffects allowed)
        {
            bool supported = data.GetDataPresent(DataFormats.FileDrop, false) || data.GetDataPresent("PNG", false) ||
                data.GetDataPresent(DataFormats.Bitmap, false) || data.GetDataPresent(DataFormats.Dib, false) ||
                data.GetDataPresent(DataFormats.UnicodeText, false) || data.GetDataPresent(DataFormats.Text, false);
            if (!supported) return DragDropEffects.None;
            // Metadata-only storage must never report Move to the source, which could delete its original data.
            return (allowed & DragDropEffects.Link) != 0 ? DragDropEffects.Link : allowed & DragDropEffects.Copy;
        }

        internal void CompleteDragOut(ShelfItem item, DragDropEffects result)
        {
            if (result == DragDropEffects.None) return;
            item.PruneMissing();
            if (!KeepAfterDragOut || item.Paths.Count == 0) store.Remove(item);
            else store.Save();
        }

        internal static DataObject DragData(ShelfItem item)
        {
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, item.Paths.ToArray(), false);
            return data;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            dragOver = false;
            dropHint.BorderBrush = Line;
            if (draggingOut) return;
            try
            {
                e.Effects = DropEffect(e.Data, e.AllowedEffects);
                if (e.Effects != DragDropEffects.None && !AcceptDrop(e.Data)) e.Effects = DragDropEffects.None;
            }
            catch (Exception ex) { if (!IsDropError(ex)) throw; e.Effects = DragDropEffects.None; Report(ex); }
            e.Handled = true;
            collapse.Start();
        }

        internal bool AcceptDrop(IDataObject data)
        {
            if (!Enabled) return false;
            var paths = new List<string>();
            if (data.GetDataPresent(DataFormats.FileDrop, false))
            {
                var files = data.GetData(DataFormats.FileDrop, false) as string[];
                if (files != null) paths.AddRange(files);
            }
            else if (data.GetDataPresent("PNG", false))
            {
                using (var stream = data.GetData("PNG", false) as Stream) {
                    if (stream != null) {
                        if (stream.CanSeek) stream.Position = 0;
                        var image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                        paths.Add(store.SaveImage(image));
                    }
                }
            }
            else if (data.GetDataPresent(DataFormats.Bitmap, false) || data.GetDataPresent(DataFormats.Dib, false))
            {
                var image = data.GetData(DataFormats.Bitmap, true) as BitmapSource;
                if (image != null) paths.Add(store.SaveImage(image));
            }
            else
            {
                string text = data.GetData(DataFormats.UnicodeText, false) as string ?? data.GetData(DataFormats.Text, false) as string;
                if (!string.IsNullOrEmpty(text))
                {
                    Uri uri;
                    string trimmed = text.Trim();
                    if (!trimmed.Contains("\n") && Uri.TryCreate(trimmed, UriKind.Absolute, out uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
                        paths.Add(store.SaveLink(uri));
                    else
                        paths.Add(store.SaveText(text));
                }
            }
            if (paths.Count == 0) return false;
            store.Add(paths);
            return true;
        }

        private static bool IsDropError(Exception ex)
        { return Snapline.ImageStore.IsImageError(ex) || ex is System.Runtime.InteropServices.ExternalException || ex is OutOfMemoryException || ex is OverflowException; }
        private void Report(Exception ex) { if (Error != null) Error("暂存架操作未完成：" + ex.Message); }
        private void Run(Action action) { try { action(); } catch (Exception ex) { if (!IsDropError(ex)) throw; Report(ex); } }

        private void Open(ShelfItem item)
        {
            if (item.IsStack) { Reveal(item); return; }
            Run(delegate { Process.Start(new ProcessStartInfo(item.Paths[0]) { UseShellExecute = true }); });
        }

        private void Reveal(ShelfItem item)
        {
            Run(delegate { Process.Start("explorer.exe", "/select,\"" + item.Paths[0] + "\""); });
        }

    }
}
