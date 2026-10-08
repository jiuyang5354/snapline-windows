using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Snapline
{
    internal sealed class LineWindow : Window
    {
        internal const double PanelHeight = 320;
        private readonly Controller controller;
        private readonly Canvas canvas = new Canvas();
        private readonly TranslateTransform slide = new TranslateTransform(0, -PanelHeight);
        private IntPtr handle;
        private int transition;
        internal Button LatestButton, PauseButton, SettingsButton;
        internal TextBlock StatusText, HotkeyText;
        private TextBlock pauseLabel, pauseGlyph, countText;
        private Ellipse statusDot;
        internal bool Revealed { get; private set; }
        internal Forms.Screen Display { get; private set; }
        internal System.Drawing.Rectangle PixelBounds { get; private set; }

        internal LineWindow(Controller controller)
        {
            Ui.Initialize();
            this.controller = controller;
            Title = "Snapline — 截图晾衣绳";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            Height = PanelHeight;
            Content = canvas;
            canvas.RenderTransform = slide;
            SourceInitialized += delegate {
                handle = new WindowInteropHelper(this).Handle;
                Native.SetWindowLong(handle, Native.GWL_EXSTYLE,
                    Native.GetWindowLong(handle, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT);
                HwndSource.FromHwnd(handle).AddHook(Hook);
            };
            SizeChanged += delegate { Rebuild(); };
        }

        private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x0021) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
            if (message == 0x0084)
            {
                long packed = lParam.ToInt64();
                var point = new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff));
                handled = true;
                return new IntPtr(IsCardAt(point) ? 1 : -1);
            }
            return IntPtr.Zero;
        }

        internal bool IsCardAt(Point screenPoint)
        {
            if (!Revealed || !IsVisible) return false;
            var element = InputHitTest(PointFromScreen(screenPoint)) as DependencyObject;
            while (element != null)
            {
                if (element is ShotCard || element is Button) return true;
                element = VisualTreeHelper.GetParent(element);
            }
            return false;
        }

        internal void UpdateInteraction(System.Drawing.Point point)
        {
            if (handle == IntPtr.Zero) return;
            int style = Native.GetWindowLong(handle, Native.GWL_EXSTYLE);
            bool accepts = controller.Busy || IsCardAt(new Point(point.X, point.Y));
            int next = accepts ? style & ~Native.WS_EX_TRANSPARENT : style | Native.WS_EX_TRANSPARENT;
            if (next != style) Native.SetWindowLong(handle, Native.GWL_EXSTYLE, next);
        }

        internal void Reveal(Forms.Screen display)
        {
            Display = display;
            double scale = Native.Scale(display);
            var work = display.WorkingArea;
            Width = work.Width / scale;
            Height = PanelHeight;
            if (handle == IntPtr.Zero) new WindowInteropHelper(this).EnsureHandle();
            Native.SetWindowPos(handle, Native.Topmost, work.Left, work.Top, work.Width, (int)Math.Ceiling(PanelHeight * scale), 0x0010);
            PixelBounds = new System.Drawing.Rectangle(work.Left, work.Top, work.Width, (int)Math.Ceiling(PanelHeight * scale));
            if (!IsVisible) Show();
            Rebuild();
            ++transition;
            Revealed = true;
            slide.BeginAnimation(TranslateTransform.YProperty, SystemParameters.ClientAreaAnimation
                ? new DoubleAnimation(0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }
                : null);
            if (!SystemParameters.ClientAreaAnimation) slide.Y = 0;
        }

        internal void Tuck()
        {
            if (!Revealed) return;
            Revealed = false;
            int current = ++transition;
            Native.SetWindowLong(handle, Native.GWL_EXSTYLE, Native.GetWindowLong(handle, Native.GWL_EXSTYLE) | Native.WS_EX_TRANSPARENT);
            if (!SystemParameters.ClientAreaAnimation) { slide.BeginAnimation(TranslateTransform.YProperty, null); slide.Y = -PanelHeight; Hide(); return; }
            var animation = new DoubleAnimation(-PanelHeight, TimeSpan.FromMilliseconds(220)) {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            animation.Completed += delegate { if (current == transition && !Revealed) Hide(); };
            slide.BeginAnimation(TranslateTransform.YProperty, animation);
        }

        internal void Rebuild()
        {
            double width = ActualWidth > 0 ? ActualWidth : Width;
            if (double.IsNaN(width) || width < 1) return;
            canvas.Children.Clear();
            var geometry = new StreamGeometry();
            double sag = Math.Min(14, width * 0.008);
            using (var drawing = geometry.Open())
            {
                drawing.BeginFigure(new Point(8, 72), false, false);
                drawing.QuadraticBezierTo(new Point(width / 2, 72 + sag * 2), new Point(width - 8, 72), true, false);
            }
            geometry.Freeze();
            canvas.Children.Add(new Path { Data = geometry, Stroke = Ui.Brush("Muted"), Opacity = 0.6, StrokeThickness = 1.2, IsHitTestVisible = false });
            canvas.Children.Add(Toolbar(width));

            int capacity = Math.Max(1, Math.Min(ImageStore.MaxItems, (int)((width - 96) / 222)));
            var shots = controller.Store.Items.Skip(Math.Max(0, controller.Store.Items.Count - capacity)).ToArray();
            if (shots.Length == 0)
            {
                var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                content.Children.Add(Ui.Text("等第一张截图挂上来", 19, false));
                content.Children.Add(new TextBlock { Text = "按 Win + Shift + S 截图，复制后自动收集", Foreground = Ui.Brush("Muted"), FontFamily = Ui.Font,
                    FontSize = 12, Margin = new Thickness(0, 10, 0, 16), TextAlignment = TextAlignment.Center });
                var import = Ui.Button("挂入图片", controller.Import);
                import.Focusable = false; import.HorizontalAlignment = HorizontalAlignment.Center;
                content.Children.Add(import);
                var hint = new Border { Width = Math.Min(490, width - 48), CornerRadius = new CornerRadius(6), Padding = new Thickness(20),
                    Background = Ui.Brush("Surface"), BorderBrush = Ui.Brush("Line"), BorderThickness = new Thickness(1), Child = content };
                Canvas.SetLeft(hint, (width - hint.Width) / 2);
                Canvas.SetTop(hint, 112);
                canvas.Children.Add(hint);
            }
            for (int i = 0; i < shots.Length; i++)
            {
                double x = width / 2 - (shots.Length - 1) * 222 / 2 + i * 222;
                double fraction = x / width;
                var card = new ShotCard(shots[i], controller);
                Canvas.SetLeft(card, x - card.Width / 2);
                Canvas.SetTop(card, 72 + 4 * sag * fraction * (1 - fraction) - 11);
                canvas.Children.Add(card);
            }
            var help = new Border { Background = Ui.Brush("Surface"), Padding = new Thickness(9, 3, 9, 3), CornerRadius = new CornerRadius(3),
                Child = Ui.Text("单击复制 · 双击预览 · 长按编辑 · 拖出使用", 11, true), IsHitTestVisible = false };
            help.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(help, (width - help.DesiredSize.Width) / 2);
            Canvas.SetTop(help, 296);
            canvas.Children.Add(help);
            RefreshStatus();
        }

        private Border Toolbar(double width)
        {
            bool compact = width < 760;
            var grid = new Grid { Margin = new Thickness(14, 0, 7, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var status = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            status.Children.Add(new Path { Data = Geometry.Parse("M 1,9 L 20,9 M 7,3 L 7,17 M 14,3 L 14,17 M 7,3 Q 10.5,0 14,3"),
                Stroke = Ui.Brush("Accent"), StrokeThickness = 2, Width = 22, Height = 20, Margin = new Thickness(0, 0, 9, 0) });
            status.Children.Add(new TextBlock { Text = "Snapline", FontFamily = new FontFamily("Segoe UI"), FontSize = 15, FontWeight = FontWeights.SemiBold,
                Foreground = Ui.Brush("Ink"), VerticalAlignment = VerticalAlignment.Center });
            statusDot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(16, 0, 7, 0) };
            status.Children.Add(statusDot);
            StatusText = Ui.Text("", 12, true); StatusText.VerticalAlignment = VerticalAlignment.Center;
            StatusText.Visibility = width < 600 ? Visibility.Collapsed : Visibility.Visible;
            status.Children.Add(StatusText);
            countText = Ui.Text("", 12, true); countText.VerticalAlignment = VerticalAlignment.Center;
            countText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            status.Children.Add(countText);
            grid.Children.Add(status);
            var commands = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            HotkeyText = Ui.Text("", 12, true); HotkeyText.FontFamily = new FontFamily("Consolas");
            HotkeyText.VerticalAlignment = VerticalAlignment.Center; HotkeyText.Margin = new Thickness(0, 0, 16, 0);
            HotkeyText.Visibility = width < 1040 ? Visibility.Collapsed : Visibility.Visible;
            commands.Children.Add(HotkeyText);
            LatestButton = ToolbarButton("\uE8C8", "复制最近一张", compact, delegate { controller.CopyLatest(); });
            commands.Children.Add(LatestButton);
            PauseButton = ToolbarButton("\uE769", "暂停收集", compact, delegate { controller.SetCollectionPaused(!controller.Store.Settings.CollectionPaused); });
            var pauseContent = (StackPanel)PauseButton.Content;
            pauseGlyph = (TextBlock)pauseContent.Children[0];
            pauseLabel = (TextBlock)pauseContent.Children[1];
            commands.Children.Add(PauseButton);
            SettingsButton = ToolbarButton("\uE713", "设置", compact, delegate { controller.ShowSettings("General"); });
            commands.Children.Add(SettingsButton);
            Grid.SetColumn(commands, 1); grid.Children.Add(commands);
            var bar = new Border { Width = Math.Max(1, width - 48), Height = 46, CornerRadius = new CornerRadius(6), Background = Ui.Brush("Surface"),
                BorderBrush = Ui.Brush("Line"), BorderThickness = new Thickness(1), Child = grid };
            Canvas.SetLeft(bar, 24); Canvas.SetTop(bar, 12);
            return bar;
        }

        private Button ToolbarButton(string glyph, string label, bool compact, Action action)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13, Foreground = Ui.Brush("Ink"), VerticalAlignment = VerticalAlignment.Center });
            var text = Ui.Text(label, 12, false); text.Margin = new Thickness(8, 0, 0, 0);
            text.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            content.Children.Add(text);
            var button = Ui.Button(label, action); button.Content = content; button.Focusable = false;
            button.Style = (Style)Application.Current.FindResource("Bare"); button.Padding = new Thickness(10, 8, 10, 8); button.ToolTip = label;
            AutomationProperties.SetName(button, label);
            return button;
        }

        internal void RefreshStatus()
        {
            if (LatestButton == null) return;
            bool paused = controller.Store.Settings.CollectionPaused;
            StatusText.Text = paused ? "收集已暂停" : controller.Store.Settings.ListenClipboard ? "正在收集" : "剪贴板收集已关闭";
            statusDot.Fill = Ui.Brush(paused ? "Accent" : "Success");
            countText.Text = " · " + controller.Store.Items.Count + " 张";
            pauseLabel.Text = paused ? "恢复收集" : "暂停收集";
            pauseGlyph.Text = paused ? "\uE768" : "\uE769";
            PauseButton.ToolTip = paused ? "恢复所有自动收集" : "暂停所有自动收集";
            LatestButton.IsEnabled = controller.Store.Items.Count > 0;
            HotkeyText.Text = controller.Sink.HotkeyRegistered ? controller.Sink.HotkeyText : "快捷键未注册";
        }

        internal void ShowCopied(Shot shot)
        {
            var card = canvas.Children.OfType<ShotCard>().FirstOrDefault(item => item.Shot == shot);
            if (card != null) card.Copied();
        }
    }
}
