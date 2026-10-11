using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Snapline
{
    // Native adaptation of the supplied liquid-glass and liquid-metal Skills.
    // The desktop snapshot stays in memory; labels and hit targets are separate.
    internal sealed class SettingsGlass : Grid
    {
        private readonly Image scene = new Image { Stretch = Stretch.Fill };
        private readonly Border viewport = new Border();
        private readonly Border tint = new Border();
        private readonly Image rim = new Image { Stretch = Stretch.Fill };
        private GlassBackdrop backdrop;
        private Rect desktop;
        private GlassBackdrop.BevelMap bevel;
        private Rect previous;

        internal SettingsGlass()
        {
            IsHitTestVisible = false;
            viewport.Child = scene;
            viewport.Effect = new BlurEffect { Radius = 10.5, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality };
            Children.Add(viewport); Children.Add(tint); Children.Add(rim);
            SizeChanged += delegate { bevel = null; Update(); };
            Loaded += delegate { Update(); };
        }

        protected override Size MeasureOverride(Size available)
        { base.MeasureOverride(available); return new Size(); }

        internal void SetBackdrop(GlassBackdrop value, Rect bounds)
        {
            backdrop = value; desktop = bounds; bevel = null; previous = Rect.Empty;
            scene.Source = null;
            Update();
        }

        internal void Update()
        {
            if (ActualWidth < 1 || ActualHeight < 1) return;
            var area = new Rect(0, 0, ActualWidth, ActualHeight);
            Clip = new RectangleGeometry(area, 24, 24);
            viewport.Clip = new RectangleGeometry(area, 24, 24);
            if (!GlassBackdrop.Enabled || backdrop == null) {
                viewport.Visibility = rim.Visibility = Visibility.Collapsed;
                tint.Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : new SolidColorBrush(Color.FromRgb(242, 245, 240));
                return;
            }
            viewport.Visibility = rim.Visibility = Visibility.Visible;
            if (!IsLoaded) return;
            Point origin = PointToScreen(new Point());
            var source = PresentationSource.FromVisual(this);
            double scale = source == null ? 1 : source.CompositionTarget.TransformToDevice.M11;
            var bounds = new Rect((origin.X - desktop.X * backdrop.Scale) / scale,
                (origin.Y - desktop.Y * backdrop.Scale) / scale, ActualWidth, ActualHeight);
            if (bounds == previous && bevel != null) return;
            // Crop before WPF layout: a translated full-screen Image is clipped
            // to its layout slot before translation and leaves the center empty.
            scene.Source = backdrop.Sample(bounds);
            if (bevel == null) bevel = new GlassBackdrop.BevelMap(area.Size, 24, Math.Min(scale, 1.5), 21);
            rim.Source = bevel.Render(backdrop, new Matrix(1, 0, 0, 1, bounds.X, bounds.Y));
            tint.Background = new SolidColorBrush(Color.FromArgb(backdrop.Luminance(bounds) < .28 ? (byte)161 : (byte)87, 247, 250, 247));
            previous = bounds;
        }
    }

    internal sealed class LiquidMetal : FrameworkElement
    {
        private readonly Control owner;
        private readonly SettingsWindow window;
        private readonly VisualCollection layers;
        private readonly Image body = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
        private readonly Image bloom = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
        private readonly Image rim = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
        private readonly DispatcherTimer timer;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private double pressedAt = -10;
        private bool loaded;
        internal int Frames { get; private set; }
        internal bool Animating { get { return timer.IsEnabled; } }
        internal bool FocusVisible { get { return window.KeyboardMode && owner.IsKeyboardFocused; } }

        internal LiquidMetal(Control owner, SettingsWindow window)
        {
            this.owner = owner; this.window = window;
            layers = new VisualCollection(this);
            layers.Add(bloom); layers.Add(body); layers.Add(rim);
            IsHitTestVisible = false;
            body.Effect = new BlurEffect { Radius = 1.8, KernelType = KernelType.Gaussian };
            bloom.Effect = new BlurEffect { Radius = 8, KernelType = KernelType.Gaussian };
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1000 / 30.0) };
            timer.Tick += delegate { Update(); };
            Loaded += delegate { loaded = true; Update(); };
            Unloaded += delegate { loaded = false; timer.Stop(); };
            SizeChanged += delegate { Update(); };
            owner.MouseEnter += delegate { Update(); };
            owner.MouseLeave += delegate { Update(); };
            owner.GotKeyboardFocus += delegate { Update(); };
            owner.LostKeyboardFocus += delegate { Update(); };
            owner.IsEnabledChanged += delegate { Update(); };
            owner.PreviewMouseDown += delegate { pressedAt = clock.Elapsed.TotalSeconds; Update(); };
            owner.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Space || e.Key == Key.Enter) pressedAt = clock.Elapsed.TotalSeconds; Update(); };
            var toggle = owner as ToggleButton;
            if (toggle != null) {
                toggle.Checked += delegate { Update(); };
                toggle.Unchecked += delegate { Update(); };
            }
        }

        protected override int VisualChildrenCount { get { return layers == null ? 0 : layers.Count; } }
        protected override Visual GetVisualChild(int index) { return layers[index]; }
        protected override Size MeasureOverride(Size available) { return new Size(); }
        protected override Size ArrangeOverride(Size size)
        {
            foreach (Image layer in layers) {
                layer.Measure(new Size(size.Width + 24, size.Height + 24));
                layer.Arrange(new Rect(-12, -12, size.Width + 24, size.Height + 24));
            }
            return size;
        }

        internal void Update()
        {
            if (!loaded || ActualWidth < 1 || ActualHeight < 1 || !owner.IsVisible) { timer.Stop(); return; }
            bool effects = GlassBackdrop.Enabled;
            double age = clock.Elapsed.TotalSeconds - pressedAt;
            bool active = effects && owner.IsEnabled && SystemParameters.ClientAreaAnimation &&
                (owner.IsMouseOver || FocusVisible || age < 2.5);
            if (active) timer.Start(); else timer.Stop();
            var toggle = owner as ToggleButton;
            bool selected = toggle != null && toggle.IsChecked == true;
            bool dark = owner.Name == "SaveSettings" || (owner is CheckBox && selected);
            double opacity = owner is RadioButton ? (selected ? .86 : owner.IsMouseOver ? .55 : .17) :
                owner is CheckBox ? (selected ? .9 : .35) : 1;
            foreach (Image layer in layers) { layer.Opacity = opacity * (owner.IsEnabled ? 1 : .4); layer.Visibility = effects ? Visibility.Visible : Visibility.Collapsed; }
            if (effects) {
                Point pointer = owner.IsMouseOver ? Mouse.GetPosition(owner) : new Point(ActualWidth / 2, ActualHeight / 2);
                double time = SystemParameters.ClientAreaAnimation ? clock.Elapsed.TotalSeconds + 3.5 : 3.5;
                var textures = Render(ActualWidth, ActualHeight, time, pointer, owner.IsMouseOver || FocusVisible, dark,
                    SystemParameters.ClientAreaAnimation ? age : -1);
                body.Source = textures[0]; rim.Source = textures[1]; bloom.Source = textures[2]; Frames++;
            }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawing)
        {
            if (!GlassBackdrop.Enabled) {
                var toggle = owner as ToggleButton;
                bool selected = toggle != null && toggle.IsChecked == true;
                bool dark = owner.Name == "SaveSettings" || (owner is CheckBox && selected);
                Brush fill = SystemParameters.HighContrast ? (dark ? SystemColors.HighlightBrush : SystemColors.WindowBrush) :
                    new SolidColorBrush(dark ? Color.FromRgb(53, 71, 80) : Color.FromRgb(208, 220, 224));
                drawing.DrawRoundedRectangle(fill, new Pen(SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brushes.White, 1),
                    new Rect(RenderSize), ActualHeight / 2, ActualHeight / 2);
            }
            if (FocusVisible) drawing.DrawRoundedRectangle(null, new Pen(SystemParameters.HighContrast ? SystemColors.WindowTextBrush :
                new SolidColorBrush(Color.FromRgb(41, 79, 104)), 2),
                new Rect(-4, -4, ActualWidth + 8, ActualHeight + 8), ActualHeight / 2 + 4, ActualHeight / 2 + 4);
        }

        // Laminar valley field, 21 spectral samples, crisp rim and separate bloom.
        // WPF applies softening and bloom to separate images, preserving the rim.
        internal static BitmapSource[] Render(double w, double h, double time, Point pointer, bool hover, bool dark, double age)
        {
            int width = (int)Math.Ceiling(w + 24), height = (int)Math.Ceiling(h + 24);
            var core = new byte[width * height * 4]; var edge = new byte[core.Length]; var glow = new byte[core.Length];
            double half = h / 2, straight = Math.Max(0, w / 2 - half);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) {
                double dx = x + .5 - width / 2.0, dy = y + .5 - height / 2.0;
                double qx = Math.Abs(dx) - straight, qy = Math.Abs(dy);
                double sd = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + qy * qy) - half;
                double pill = 1 - Smooth(-.7, .7, sd);
                if (sd > 3) continue;
                double px = dx / h, py = -dy / h;
                double distance = Math.Sqrt(Math.Pow(px - (pointer.X - w / 2) / h, 2) + Math.Pow(py + (pointer.Y - h / 2) / h, 2));
                double well = hover ? Math.Exp(-distance * distance / .3025) * .65 : 0;
                double rip = age >= 0 && age < 2.5 ? Math.Exp(-Math.Pow(Math.Abs((Math.Sqrt(px * px + py * py) - age * 1.85) / .20), 1.15)) * Math.Exp(-age * 1.35) : 0;
                double field = (py - Wig(px * .5, time, 0) * .55) * 2.4 * Math.Exp(2.2 * Wig(px * .32 + 9, time, 2.7));
                field += well * .30 + rip * 1.35 - .05;
                double silver = 0;
                for (int n = 0; n < 21; n++) silver += Tone(field + (Math.Pow(.5, 1.5) - Math.Pow(1 - n / 20.0, 1.5)) * .05);
                silver = Math.Min(1, silver / 21 * 1.4);
                double light = Smooth(-.26, .10, py - Wig(px * .5, time, 0) * .55);
                silver *= light * (hover ? 1 : .94);
                double r = .60 + .38 * silver, g = .63 + .36 * silver, b = .65 + .35 * silver;
                if (dark) { r *= .30; g *= .30; b *= .30; }
                int i = (y * width + x) * 4;
                Pixel(core, i, r, g, b, pill);
                double s = Math.Atan2(dy / half, dx / Math.Max(w / 2, 1)) / (2 * Math.PI) + .5;
                double hot = .20 + .62 * Band(s - time * .070, .075) + .44 * Band(s + time * .044 + .41, .135) + .30 * Band(s - time * .024 + .73, .20);
                double rim = (1 - Smooth(0, 1.6, Math.Abs(sd + .7))) * hot * (.70 + .30 * (-dy / half + 1) / 2) * (1 + well * .8 + rip * 1.6);
                Pixel(edge, i, .91, .97, 1, Math.Min(1, rim));
                Pixel(glow, i, .93, .98, 1, Math.Min(1, rim * (hover ? 1.6 : 1.25) + silver * pill * .14));
            }
            return new[] { Bitmap(core, width, height), Bitmap(edge, width, height), Bitmap(glow, width, height) };
        }

        private static BitmapSource Bitmap(byte[] pixels, int width, int height)
        {
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
            bitmap.Freeze(); return bitmap;
        }
        private static void Pixel(byte[] pixels, int i, double r, double g, double b, double alpha)
        {
            pixels[i] = (byte)Math.Round(Math.Min(1, b) * alpha * 255);
            pixels[i + 1] = (byte)Math.Round(Math.Min(1, g) * alpha * 255);
            pixels[i + 2] = (byte)Math.Round(Math.Min(1, r) * alpha * 255);
            pixels[i + 3] = (byte)Math.Round(alpha * 255);
        }
        private static double Smooth(double a, double b, double x) { x = Math.Max(0, Math.Min(1, (x - a) / (b - a))); return x * x * (3 - 2 * x); }
        private static double Fract(double x) { return x - Math.Floor(x); }
        private static double Band(double x, double width) { x = Fract(x); x = Math.Min(x, 1 - x); return Math.Exp(-x * x / (width * width)); }
        private static double Tone(double x) { x = Fract(x); return Smooth(.23, .27, x) * (1 - Smooth(.73, .77, x)); }
        private static double Hash(double x, double y)
        {
            double a = Fract(x * .1031), b = Fract(y * .1031), c = a;
            double d = a * (b + 33.33) + b * (c + 33.33) + c * (a + 33.33);
            a += d; b += d; c += d; return Fract((a + b) * c);
        }
        private static double Noise(double x, double y)
        {
            double ix = Math.Floor(x), iy = Math.Floor(y), fx = Smooth(0, 1, Fract(x)), fy = Smooth(0, 1, Fract(y));
            double a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
            return ((a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy) * 2 - 1;
        }
        private static double Wig(double x, double time, double seed)
        { return Noise(x, time * .150 + seed) * .60 + Noise(x * 2.07 + 4, time * .105 + seed) * .27 + Noise(x * 4.30 - 7, time * .080 + seed) * .13; }
    }
}
