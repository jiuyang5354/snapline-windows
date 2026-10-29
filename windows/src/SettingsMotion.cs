using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Snapline
{
    // Native adaptation of the selected 21st Switch / Slide Tabs / Button / Panel interactions.
    internal sealed class SettingsMotion : IDisposable
    {
        private readonly SettingsWindow window;
        private readonly Func<bool> allowed;
        private readonly Dictionary<Tuple<DependencyObject, DependencyProperty>, AnimationClock> clocks =
            new Dictionary<Tuple<DependencyObject, DependencyProperty>, AnimationClock>();
        private CheckBox[] toggles = new CheckBox[0];
        private bool ready, pointer;
        internal int ActiveCount { get { return clocks.Count; } }
        private bool Animate { get { return ready && pointer && allowed(); } }

        internal SettingsMotion(SettingsWindow window, Func<bool> allowed)
        {
            this.window = window;
            this.allowed = allowed ?? delegate { return SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast; };
            window.PreviewMouseDown += delegate { SetPointerMode(true); };
            window.PreviewMouseMove += delegate { SetPointerMode(true); };
            window.PreviewKeyDown += delegate { SetPointerMode(false); };
            window.Loaded += delegate {
                toggles = Descendants(window.View).OfType<CheckBox>().ToArray();
                foreach (var toggle in toggles) {
                    toggle.Checked += delegate(object sender, RoutedEventArgs e) { UpdateToggle((CheckBox)sender); };
                    toggle.Unchecked += delegate(object sender, RoutedEventArgs e) { UpdateToggle((CheckBox)sender); };
                    toggle.SizeChanged += delegate(object sender, SizeChangedEventArgs e) { UpdateToggle((CheckBox)sender, false); };
                }
                foreach (var button in Descendants(window.View).OfType<Button>()) BindButton(button);
                foreach (string page in new[] { "General", "Capture", "Keys", "Storage", "Shelf", "About" })
                    window.Part<RadioButton>("Nav" + page).Checked += delegate { UpdateSelections(); };
                foreach (string mode in new[] { "Light", "Dark", "System" })
                    window.Part<RadioButton>("Theme" + mode).Checked += delegate { UpdateSelections(); };
                RefreshColors(); UpdateSelections(); ready = true;
            };
            window.Closed += delegate { Dispose(); };
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
        }

        internal void SetPointerMode(bool value)
        {
            pointer = value;
            if (!value || !allowed()) Stop();
        }

        private Color Color(string resource) { return ((SolidColorBrush)window.View.FindResource(resource)).Color; }

        internal void RefreshColors()
        {
            foreach (var toggle in toggles) UpdateToggle(toggle, false);
        }

        internal void UpdateToggle(CheckBox toggle) { UpdateToggle(toggle, Animate); }
        private void UpdateToggle(CheckBox toggle, bool animate)
        {
            toggle.ApplyTemplate();
            var track = toggle.Template.FindName("Track", toggle) as Border;
            var thumb = toggle.Template.FindName("Thumb", toggle) as FrameworkElement;
            if (track == null || thumb == null) return;
            if (thumb.RenderTransform.IsFrozen) thumb.RenderTransform = thumb.RenderTransform.CloneCurrentValue();
            ((System.Windows.Shapes.Shape)thumb).Fill = SystemParameters.HighContrast ? SystemColors.WindowBrush : Brushes.White;
            double travel = Math.Max(0, track.ActualWidth - thumb.ActualWidth - 6);
            Move(thumb.RenderTransform, TranslateTransform.XProperty, toggle.IsChecked == true ? travel : 0, 160, animate);
            var brush = track.Background as SolidColorBrush;
            if (brush == null || brush.IsFrozen) track.Background = brush = new SolidColorBrush(brush == null ? Color("Track") : brush.Color);
            Color target = Color(toggle.IsChecked == true ? "Accent" : "Track");
            Start(brush, SolidColorBrush.ColorProperty, target, new ColorAnimation(brush.Color, target, TimeSpan.FromMilliseconds(160)), animate);
        }

        private void BindButton(Button button)
        {
            button.MouseEnter += delegate { UpdateButton(button, true, button.IsPressed); };
            button.MouseLeave += delegate { UpdateButton(button, false, false); };
            button.AddHandler(Mouse.MouseDownEvent, new MouseButtonEventHandler(delegate(object sender, MouseButtonEventArgs e) {
                if (e.ChangedButton == MouseButton.Left) UpdateButton(button, true, button.IsPressed);
            }), true);
            button.AddHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler(delegate(object sender, MouseButtonEventArgs e) {
                if (e.ChangedButton == MouseButton.Left) UpdateButton(button, button.IsMouseOver, false);
            }), true);
            button.LostMouseCapture += delegate { UpdateButton(button, button.IsMouseOver, false); };
            button.IsEnabledChanged += delegate { UpdateButton(button, button.IsMouseOver, false); };
        }

        internal void UpdateButton(Button button, bool hover, bool pressed)
        {
            button.ApplyTemplate();
            var chrome = button.Template.FindName("Chrome", button) as Border;
            var wash = button.Template.FindName("HoverWash", button) as Border;
            if (chrome == null || wash == null) return;
            if (chrome.RenderTransform.IsFrozen) chrome.RenderTransform = chrome.RenderTransform.CloneCurrentValue();
            bool animate = Animate && button.IsEnabled;
            double scale = button.IsEnabled && pressed ? .98 : 1;
            Move(chrome.RenderTransform, ScaleTransform.ScaleXProperty, scale, pressed ? 80 : 120, animate);
            Move(chrome.RenderTransform, ScaleTransform.ScaleYProperty, scale, pressed ? 80 : 120, animate);
            Move(wash, UIElement.OpacityProperty, button.IsEnabled && hover ? .06 : 0, 100, animate);
        }

        internal void UpdateSelections()
        {
            Select("NavigationTabs", "NavSelection", new[] { "NavGeneral", "NavCapture", "NavKeys", "NavStorage", "NavShelf", "NavAbout" }, 140);
            Select("ThemeTabs", "ThemeSelection", new[] { "ThemeLight", "ThemeDark", "ThemeSystem" }, 180);
        }

        private void Select(string hostName, string markerName, string[] names, int ms)
        {
            var selected = names.Select(name => window.Part<RadioButton>(name)).FirstOrDefault(button => button.IsChecked == true);
            if (selected == null || selected.ActualWidth <= 0) return;
            var host = window.Part<Grid>(hostName);
            var marker = window.Part<Border>(markerName);
            Point point = selected.TranslatePoint(new Point(), host);
            marker.Width = selected.ActualWidth; marker.Height = selected.ActualHeight;
            Move(marker.RenderTransform, TranslateTransform.XProperty, point.X, ms, Animate);
            Move(marker.RenderTransform, TranslateTransform.YProperty, point.Y, ms, Animate);
        }

        internal void PageChanged(StackPanel page)
        {
            // State and hit testing update immediately. First opening and keyboard navigation stay direct.
            if (!Animate) return;
            if (!(page.RenderTransform is TranslateTransform)) page.RenderTransform = new TranslateTransform();
            var shift = (TranslateTransform)page.RenderTransform;
            page.Opacity = .85; shift.Y = 4;
            Move(page, UIElement.OpacityProperty, 1, 120, true);
            Move(shift, TranslateTransform.YProperty, 0, 120, true);
        }

        private void Move(DependencyObject target, DependencyProperty property, double value, int ms, bool animate)
        { Start(target, property, value, new DoubleAnimation((double)target.GetValue(property), value, TimeSpan.FromMilliseconds(ms)), animate); }

        private void Start(DependencyObject target, DependencyProperty property, object value, AnimationTimeline animation, bool animate)
        {
            var owner = (IAnimatable)target;
            var key = Tuple.Create(target, property);
            object current = target.GetValue(property);
            AnimationClock old;
            if (clocks.TryGetValue(key, out old)) { owner.ApplyAnimationClock(property, null); old.Controller.Remove(); clocks.Remove(key); }
            target.SetValue(property, value);
            if (!animate || Equals(current, value)) return;
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var numeric = animation as DoubleAnimation;
            if (numeric != null) numeric.EasingFunction = easing;
            var color = animation as ColorAnimation;
            if (color != null) color.EasingFunction = easing;
            animation.FillBehavior = FillBehavior.Stop;
            var clock = animation.CreateClock(); clocks[key] = clock;
            clock.Completed += delegate {
                AnimationClock active;
                if (clocks.TryGetValue(key, out active) && active == clock) { owner.ApplyAnimationClock(property, null); clocks.Remove(key); }
                clock.Controller.Remove();
            };
            owner.ApplyAnimationClock(property, clock, HandoffBehavior.SnapshotAndReplace);
        }

        private void Stop()
        {
            foreach (var pair in clocks) { ((IAnimatable)pair.Key.Item1).ApplyAnimationClock(pair.Key.Item2, null); pair.Value.Controller.Remove(); }
            clocks.Clear();
        }

        public void Dispose() { ready = false; Stop(); }
    }
}
