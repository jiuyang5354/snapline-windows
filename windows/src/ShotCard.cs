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
            Width = 204;
            Height = 205;
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

            var image = new Image { Source = shot.Thumbnail, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var content = new Grid { ClipToBounds = true };
            content.RowDefinitions.Add(new RowDefinition());
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            content.Children.Add(image);
            var metadata = new Grid { Margin = new Thickness(4, 8, 4, 0) };
            metadata.ColumnDefinitions.Add(new ColumnDefinition());
            metadata.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = Ui.Text(System.IO.Path.GetFileNameWithoutExtension(shot.Path), 11, false);
            name.TextTrimming = TextTrimming.CharacterEllipsis; name.Margin = new Thickness(0, 0, 8, 0);
            var time = Ui.Text(System.IO.File.GetLastWriteTime(shot.Path).ToString("HH:mm"), 10, true);
            Grid.SetColumn(time, 1); metadata.Children.Add(name); metadata.Children.Add(time);
            Grid.SetRow(metadata, 1); content.Children.Add(metadata);
            var frame = new Border {
                Width = 204, Height = 183, Padding = new Thickness(6),
                CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1),
                BorderBrush = Ui.Brush("Line"), Background = Ui.Brush("Surface"), Child = content,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 22, 0, 0),
                Effect = new DropShadowEffect { Color = Color.FromRgb(50, 57, 46), BlurRadius = 12, ShadowDepth = 3, Opacity = 0.10 }
            };
            Children.Add(frame);
            var pin = new Path { Data = Geometry.Parse("M 4,1 L 4,30 M 13,1 L 13,30 M 4,1 Q 8.5,-2 13,1 M 1,11 L 16,11"),
                Width = 19, Height = 33, Stroke = Ui.Brush("Muted"), StrokeThickness = 2.4,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
            Children.Add(pin);

            remove = new Button {
                Content = "×", FontFamily = new FontFamily("Segoe UI"), FontSize = 16, Width = 22, Height = 22,
                Foreground = Ui.Brush("Ink"), Background = Ui.Brush("Surface"), Style = (Style)Application.Current.FindResource(typeof(Button)),
                BorderBrush = Ui.Brush("Line"), BorderThickness = new Thickness(1),
                Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 29, 7, 0), Opacity = 0, Focusable = false,
                ToolTip = controller.Store.Owns(shot.Path) ? "取下截图：收件夹图片进入回收站" : "取下截图：保留外部原文件"
            };
            AutomationProperties.SetName(remove, "取下截图");
            remove.Click += delegate { controller.Discard(Shot); };
            Children.Add(remove);
            badge = new Border {
                Child = new TextBlock { Text = "✓ 已复制", FontFamily = Ui.Font, Foreground = Ui.Brush("Surface"), FontSize = 12 },
                Background = Ui.Brush("Accent"), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 0, 40),
                Padding = new Thickness(11, 6, 11, 6), VerticalAlignment = VerticalAlignment.Bottom,
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
            MouseEnter += delegate { remove.Opacity = 1; ScaleTo(1.025); };
            MouseLeave += delegate { remove.Opacity = 0; if (down == null) ScaleTo(1); };
            ContextMenu = Menu();
            ContextMenu.Opened += delegate { controller.Busy = true; };
            ContextMenu.Closed += delegate { controller.Busy = false; };
            Sway(3);
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
            ScaleTo(IsMouseOver ? 1.025 : 1);
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
