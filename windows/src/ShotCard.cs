using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Snapline
{
    internal sealed class ShotCard : Grid
    {
        internal readonly Shot Shot;
        private readonly Controller controller;
        private readonly RotateTransform rotation;
        private readonly ScaleTransform scale = new ScaleTransform(1, 1);
        private readonly Border badge;
        private readonly Button remove;
        private readonly DispatcherTimer hold;
        private readonly DispatcherTimer click;
        private Point? down;
        private bool held;

        internal ShotCard(Shot shot, Controller controller)
        {
            Shot = shot;
            this.controller = controller;
            Width = 160;
            Focusable = false;
            Cursor = Cursors.Hand;
            RenderTransformOrigin = new Point(0.5, 0.02);
            rotation = new RotateTransform(shot.Tilt);
            var transforms = new TransformGroup();
            transforms.Children.Add(scale);
            transforms.Children.Add(rotation);
            RenderTransform = transforms;
            AutomationProperties.SetName(this, "截图 " + System.IO.Path.GetFileName(shot.Path));
            ToolTip = "单击复制 · 双击预览 · 长按编辑 · 拖出使用";

            double fit = Math.Min(136.0 / shot.Thumbnail.PixelWidth, 104.0 / shot.Thumbnail.PixelHeight);
            double photoWidth = shot.Thumbnail.PixelWidth * fit;
            double photoHeight = shot.Thumbnail.PixelHeight * fit;
            Height = photoHeight + 48;
            var image = new Image { Source = shot.Thumbnail, Width = photoWidth, Height = photoHeight, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var frame = new Border {
                Width = photoWidth + 14, Height = photoHeight + 14, Padding = new Thickness(6),
                CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
                BorderBrush = new LinearGradientBrush(Color.FromArgb(210, 255, 255, 255), Color.FromArgb(65, 255, 255, 255), 90),
                Background = new SolidColorBrush(Color.FromArgb(115, 50, 55, 63)), Child = image,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 14, 0, 0),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 5, Opacity = 0.22 }
            };
            Children.Add(frame);
            image.Clip = new RectangleGeometry(new Rect(0, 0, photoWidth, photoHeight), 4, 4);

            var pin = new Grid { Width = 9, Height = 26, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
            pin.Children.Add(new Rectangle {
                RadiusX = 3.5, RadiusY = 3.5,
                Fill = new LinearGradientBrush(new GradientStopCollection {
                    new GradientStop(Color.FromRgb(170, 177, 181), 0), new GradientStop(Color.FromRgb(244, 246, 247), 0.35),
                    new GradientStop(Color.FromRgb(205, 210, 215), 0.65), new GradientStop(Color.FromRgb(137, 145, 152), 1)
                }, new Point(0, 0), new Point(1, 0)),
                Stroke = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), StrokeThickness = 0.6,
                Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.3 }
            });
            pin.Children.Add(new Rectangle {
                Width = 5, Height = 1.5, RadiusX = 1, RadiusY = 1, Fill = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)),
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 9, 0, 0)
            });
            Children.Add(pin);

            remove = new Button {
                Content = "×", FontFamily = new FontFamily("Segoe UI"), FontSize = 16, Width = 22, Height = 22,
                Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(220, 40, 44, 50)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), BorderThickness = new Thickness(1),
                Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness((160 - frame.Width) / 2 + 3, 17, 0, 0), Opacity = 0, Focusable = false,
                ToolTip = "取下这张截图"
            };
            AutomationProperties.SetName(remove, "取下截图");
            remove.Click += delegate { controller.Discard(Shot); };
            Children.Add(remove);
            badge = new Border {
                Child = new TextBlock { Text = "✓ 已复制", Foreground = Brushes.White, FontSize = 11 },
                Background = new SolidColorBrush(Color.FromArgb(235, 40, 44, 50)), CornerRadius = new CornerRadius(11),
                Padding = new Thickness(10, 4, 10, 4), VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Hidden, IsHitTestVisible = false
            };
            Children.Add(badge);

            hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            hold.Tick += delegate { hold.Stop(); if (down != null) { held = true; EndPress(); controller.Edit(Shot); } };
            click = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime) };
            click.Tick += delegate { click.Stop(); controller.Copy(Shot); };
            PreviewMouseLeftButtonDown += Press;
            PreviewMouseLeftButtonUp += Release;
            PreviewMouseMove += Move;
            LostMouseCapture += delegate { hold.Stop(); down = null; controller.Busy = false; };
            MouseEnter += delegate { remove.Opacity = 1; ScaleTo(1.035); };
            MouseLeave += delegate { remove.Opacity = 0; if (down == null) ScaleTo(1); };
            ContextMenu = Menu();
            ContextMenu.Opened += delegate { controller.Busy = true; };
            ContextMenu.Closed += delegate { controller.Busy = false; };
            Sway(10);
        }

        private ContextMenu Menu()
        {
            var menu = new ContextMenu();
            Add(menu, "复制图片", delegate { controller.Copy(Shot); });
            Add(menu, "打开预览", delegate { controller.Open(Shot); });
            Add(menu, "用画图编辑", delegate { controller.Edit(Shot); });
            Add(menu, "另存为…", delegate { controller.Export(Shot); });
            Add(menu, "在文件夹中显示", delegate { controller.Reveal(Shot); });
            menu.Items.Add(new Separator());
            Add(menu, "取下截图", delegate { controller.Discard(Shot); });
            return menu;
        }

        private static void Add(ContextMenu menu, string label, Action action)
        {
            var item = new MenuItem { Header = label };
            item.Click += delegate { action(); };
            menu.Items.Add(item);
        }

        private void Press(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled || FromRemove(e.OriginalSource as DependencyObject)) return;
            e.Handled = true;
            if (e.ClickCount == 2) { click.Stop(); EndPress(); controller.Open(Shot); return; }
            down = e.GetPosition(this);
            held = false;
            controller.Busy = true;
            CaptureMouse();
            hold.Start();
            ScaleTo(0.95);
        }

        private void Release(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled || FromRemove(e.OriginalSource as DependencyObject)) return;
            bool shouldCopy = down != null && !held;
            EndPress();
            if (shouldCopy) { click.Stop(); click.Start(); }
            e.Handled = true;
        }

        private bool FromRemove(DependencyObject source)
        {
            while (source != null && source != this)
            {
                if (source == remove) return true;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        private void Move(object sender, MouseEventArgs e)
        {
            if (down == null || held || e.LeftButton != MouseButtonState.Pressed) return;
            var position = e.GetPosition(this);
            if (Math.Abs(position.X - down.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - down.Value.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            click.Stop();
            EndPress();
            controller.Busy = true;
            Opacity = 0.45;
            try { DragDrop.DoDragDrop(this, controller.Data(Shot), DragDropEffects.Copy | DragDropEffects.Move); }
            finally { Opacity = 1; controller.Busy = false; controller.Store.Prune(); }
        }

        private void EndPress()
        {
            hold.Stop();
            down = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
            controller.Busy = false;
            ScaleTo(IsMouseOver ? 1.035 : 1);
        }

        private void ScaleTo(double value)
        {
            if (!SystemParameters.ClientAreaAnimation) { scale.ScaleX = scale.ScaleY = value; return; }
            var animation = new DoubleAnimation(value, TimeSpan.FromMilliseconds(160));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }

        internal void Sway(double amount)
        {
            if (!SystemParameters.ClientAreaAnimation || down != null) return;
            var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1.2) };
            double[] angles = { amount, -amount * 0.5, amount * 0.25, -amount * 0.1, 0 };
            for (int i = 0; i < angles.Length; i++)
                animation.KeyFrames.Add(new EasingDoubleKeyFrame(Shot.Tilt + angles[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * 300)),
                    new SineEase { EasingMode = EasingMode.EaseInOut }));
            rotation.BeginAnimation(RotateTransform.AngleProperty, animation);
        }

        internal void Copied()
        {
            badge.Visibility = Visibility.Visible;
            Sway(3);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            timer.Tick += delegate { timer.Stop(); badge.Visibility = Visibility.Hidden; };
            timer.Start();
        }
    }
}
