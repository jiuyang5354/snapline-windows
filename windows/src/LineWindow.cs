using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        internal const double PanelHeight = 210;
        private readonly Controller controller;
        private readonly Canvas canvas = new Canvas();
        private readonly TranslateTransform slide = new TranslateTransform(0, -PanelHeight);
        private IntPtr handle;
        private int transition;
        internal bool Revealed { get; private set; }
        internal Forms.Screen Display { get; private set; }
        internal System.Drawing.Rectangle PixelBounds { get; private set; }

        internal LineWindow(Controller controller)
        {
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
                if (element is ShotCard) return true;
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
            double sag = Math.Min(30, width * 0.018);
            using (var drawing = geometry.Open())
            {
                drawing.BeginFigure(new Point(0, 10), false, false);
                drawing.QuadraticBezierTo(new Point(width / 2, 10 + sag * 2), new Point(width, 10), true, false);
            }
            geometry.Freeze();
            var stroke = new LinearGradientBrush(new GradientStopCollection {
                new GradientStop(Color.FromArgb(0, 195, 201, 208), 0), new GradientStop(Color.FromArgb(230, 195, 201, 208), 0.08),
                new GradientStop(Color.FromArgb(230, 195, 201, 208), 0.92), new GradientStop(Color.FromArgb(0, 195, 201, 208), 1)
            }, new Point(0, 0), new Point(1, 0));
            canvas.Children.Add(new Path { Data = geometry, Stroke = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
                StrokeThickness = 2.4, RenderTransform = new TranslateTransform(0, 1.4), IsHitTestVisible = false });
            canvas.Children.Add(new Path { Data = geometry, Stroke = stroke, StrokeThickness = 1.2, IsHitTestVisible = false });

            int capacity = Math.Max(1, Math.Min(ImageStore.MaxItems, (int)((width - 96) / 174)));
            var shots = controller.Store.Items.Skip(Math.Max(0, controller.Store.Items.Count - capacity)).ToArray();
            if (shots.Length == 0)
            {
                var hint = new Border {
                    Width = 310, CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 9, 18, 9),
                    Background = new SolidColorBrush(Color.FromArgb(220, 35, 39, 46)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(85, 255, 255, 255)), BorderThickness = new Thickness(1),
                    Child = new TextBlock { Text = "Win + Shift + S  截一张，挂在这里", Foreground = Brushes.White, FontSize = 12,
                        TextAlignment = TextAlignment.Center }, IsHitTestVisible = false
                };
                Canvas.SetLeft(hint, (width - hint.Width) / 2);
                Canvas.SetTop(hint, 50);
                canvas.Children.Add(hint);
            }
            for (int i = 0; i < shots.Length; i++)
            {
                double x = width / 2 - (shots.Length - 1) * 174 / 2 + i * 174;
                double fraction = x / width;
                var card = new ShotCard(shots[i], controller);
                Canvas.SetLeft(card, x - card.Width / 2);
                Canvas.SetTop(card, 10 + 4 * sag * fraction * (1 - fraction) - 9.5);
                canvas.Children.Add(card);
            }
        }

        internal void ShowCopied(Shot shot)
        {
            var card = canvas.Children.OfType<ShotCard>().FirstOrDefault(item => item.Shot == shot);
            if (card != null) card.Copied();
        }
    }
}
