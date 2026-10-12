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
        private readonly System.Windows.Shapes.Rectangle lens = new System.Windows.Shapes.Rectangle { Fill = Brushes.White };
        private readonly Border fallback = new Border();
        private readonly SettingsGlassEffect optics = new SettingsGlassEffect();
        private GlassBackdrop backdrop;
        private Rect desktop;
        private Size mapped;
        private Rect previous = Rect.Empty;
        private bool? darkPalette;
        internal event Action<bool> PaletteChanged;
        internal int MapBuilds { get; private set; }
        internal Point SampleOrigin { get; private set; }

        internal SettingsGlass()
        {
            IsHitTestVisible = false;
            lens.Effect = optics;
            Children.Add(fallback); Children.Add(lens);
            SizeChanged += delegate { Update(); };
            Loaded += delegate { Update(); };
        }

        protected override Size MeasureOverride(Size available)
        { base.MeasureOverride(available); return new Size(); }

        internal void SetBackdrop(GlassBackdrop value, Rect bounds)
        {
            backdrop = value; desktop = bounds; mapped = new Size(); previous = Rect.Empty;
            if (backdrop != null) optics.SetBackdrop(backdrop.Frosted(22));
            Update();
        }

        internal void Update()
        {
            if (ActualWidth < 1 || ActualHeight < 1) return;
            var area = new Rect(0, 0, ActualWidth, ActualHeight);
            if (mapped != area.Size) Clip = new RectangleGeometry(area, 24, 24);
            bool enabled = GlassBackdrop.Enabled && backdrop != null;
            lens.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            fallback.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            if (!enabled) {
                fallback.Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : new SolidColorBrush(Color.FromRgb(242, 245, 240));
                SetPalette(false);
                return;
            }
            if (!IsLoaded) return;
            Point origin = PointToScreen(new Point()), end = PointToScreen(new Point(ActualWidth, ActualHeight));
            double scale = (end.X - origin.X) / ActualWidth;
            var bounds = new Rect((origin.X - desktop.X * backdrop.Scale) / backdrop.Scale,
                (origin.Y - desktop.Y * backdrop.Scale) / backdrop.Scale,
                (end.X - origin.X) / backdrop.Scale, (end.Y - origin.Y) / backdrop.Scale);
            if (bounds == previous) return;
            if (mapped != area.Size) { optics.SetOptics(area.Size, Math.Min(scale, 1.5)); mapped = area.Size; MapBuilds++; }
            SampleOrigin = new Point((origin.X - desktop.X * backdrop.Scale) / backdrop.Snapshot.PixelWidth,
                (origin.Y - desktop.Y * backdrop.Scale) / backdrop.Snapshot.PixelHeight);
            optics.Origin = SampleOrigin;
            optics.Extent = new Point((end.X - origin.X) / backdrop.Snapshot.PixelWidth, (end.Y - origin.Y) / backdrop.Snapshot.PixelHeight);
            optics.Deflection = new Point(18 * scale / backdrop.Snapshot.PixelWidth, 18 * scale / backdrop.Snapshot.PixelHeight);
            bool dark = backdrop.Luminance(bounds) < .38;
            optics.Veil = dark ? .10 : .28;
            SetPalette(dark);
            previous = bounds;
        }
        private void SetPalette(bool dark)
        { if (darkPalette == dark) return; darkPalette = dark; if (PaletteChanged != null) PaletteChanged(dark); }
    }

    internal sealed class LiquidMetal : FrameworkElement
    {
        // The metal texture is arranged this many pixels outside the control on
        // each side so the exterior glow is not clipped.
        private const double Bleed = 12;
        private readonly Control owner;
        private readonly SettingsWindow window;
        private readonly VisualCollection layers;
        private readonly Image body = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
        private readonly DispatcherTimer timer;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private double pressedAt = -10;
        private bool loaded;
        private bool hasFrame, lastHover, lastDark;
        private bool rendering;
        private Size lastSize;
        internal int Frames { get; private set; }
        internal bool Animating { get { return timer.IsEnabled; } }
        internal bool FocusVisible { get { return window.KeyboardMode && owner.IsKeyboardFocused; } }

        internal LiquidMetal(Control owner, SettingsWindow window)
        {
            this.owner = owner; this.window = window;
            layers = new VisualCollection(this);
            layers.Add(body);
            IsHitTestVisible = false;
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
            body.Measure(new Size(size.Width + Bleed * 2, size.Height + Bleed * 2));
            body.Arrange(new Rect(-Bleed, -Bleed, size.Width + Bleed * 2, size.Height + Bleed * 2));
            return size;
        }

        internal void Update()
        {
            if (!loaded || ActualWidth < 1 || ActualHeight < 1 || !owner.IsVisible) { timer.Stop(); return; }
            bool effects = GlassBackdrop.Enabled;
            double age = clock.Elapsed.TotalSeconds - pressedAt;
            bool over = owner.IsMouseOver;
            bool active = effects && owner.IsEnabled && SystemParameters.ClientAreaAnimation &&
                (over || FocusVisible || age < 2.5);
            if (active) timer.Start(); else timer.Stop();
            var toggle = owner as ToggleButton;
            bool selected = toggle != null && toggle.IsChecked == true;
            bool dark = owner.Name == "SaveSettings" || (owner is CheckBox && selected);
            double opacity = owner is RadioButton ? (selected ? .86 : over ? .55 : .17) :
                owner is CheckBox ? (selected ? .9 : .35) : 1;
            body.Opacity = opacity * (owner.IsEnabled ? 1 : .4);
            body.Visibility = effects ? Visibility.Visible : Visibility.Collapsed;
            bool hovered = over || FocusVisible;
            if (effects && !rendering && (active || !hasFrame || lastHover != hovered || lastDark != dark || lastSize != RenderSize)) {
                Point pointer = over ? Mouse.GetPosition(owner) : new Point(ActualWidth / 2, ActualHeight / 2);
                double time = SystemParameters.ClientAreaAnimation ? clock.Elapsed.TotalSeconds + 3.5 : 3.5;
                double rippleAge = SystemParameters.ClientAreaAnimation ? age : -1;
                Size size = RenderSize;
                rendering = true;
                // Pixel computation never occupies the input/drag dispatcher.
                // Freeze the result on the worker before handing it to WPF.
                System.Threading.ThreadPool.QueueUserWorkItem(delegate {
                    var textures = Render(size.Width, size.Height, time, pointer, hovered, dark, rippleAge);
                    if (Dispatcher.HasShutdownStarted) return;
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate {
                        rendering = false;
                        if (!loaded || !IsVisible) return;
                        body.Source = textures[0]; Frames++;
                        hasFrame = true; lastHover = hovered; lastDark = dark; lastSize = size;
                        var currentToggle = owner as ToggleButton;
                        bool currentDark = owner.Name == "SaveSettings" || (owner is CheckBox && currentToggle.IsChecked == true);
                        if (lastHover != (owner.IsMouseOver || FocusVisible) || lastSize != RenderSize || lastDark != currentDark) Update();
                    }));
                });
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

        // Port the approved preview's field, self-refraction, spectral weights,
        // half-resolution softening, quarter-resolution bloom and compositor.
        // Keep float intermediates: clipping the bright rim before bloom loses it.
        internal static BitmapSource[] Render(double w, double h, double time, Point pointer, bool hover, bool dark, double age)
        {
            int width = (int)Math.Ceiling(w + 24), height = (int)Math.Ceiling(h + 24);
            // The reference softens at half resolution. Evaluate the field at
            // that resolution too; the crisp rim remains at the full pixel size.
            var scene = new Field((width + 1) / 2, (height + 1) / 2); var rim = new Field(width, height);
            double half = h / 2, straight = Math.Max(0, w / 2 - half);
            double pointerX = (pointer.X - w / 2) / h, pointerY = (pointer.Y - half) / h;
            var offsets = new double[21]; var weights = new double[21, 3]; var totals = new double[3];
            for (int n = 0; n < 21; n++) {
                double k = n / 20.0;
                offsets[n] = (Math.Pow(.5, 1.5) - Math.Pow(1 - k, 1.5)) * .05;
                for (int c = 0; c < 3; c++) { weights[n, c] = Clamp(1.5 - Math.Abs(4 * k - (3 - c))); totals[c] += weights[n, c]; }
            }
            for (int y = 0; y < scene.Height; y++)
            for (int x = 0; x < scene.Width; x++) {
                double dx = (x + .5) * width / scene.Width - width / 2.0, dy = (y + .5) * height / scene.Height - height / 2.0;
                double sd = Distance(dx, dy, straight, half), pill = 1 - Smooth(-1, 1, sd);
                int i = (y * scene.Width + x) * 4;
                double px = dx / h, py = dy / h;
                double well = Well(px, py, pointerX, pointerY, hover), rip = Ripple(px, py, age);
                if (pill > .0015) {
                    double warpX = px - pointerX + 1e-5, warpY = py - pointerY + 1e-5;
                    double length = Math.Sqrt(warpX * warpX + warpY * warpY);
                    double qx = px + warpX / length * well * .38, qy = py + warpY / length * well * .38;
                    double step = 1 / h;
                    double value = Surface(qx, qy, time);
                    double gx = (Surface(qx + step, qy, time) - value) / step;
                    double gy = (Surface(qx, qy + step, time) - value) / step;
                    value = Surface(qx - gx * .18 / 2.4, qy - gy * .18 / 2.4, time) + rip * 1.35;
                    double lit = Smooth(-.26, .10, qy - Wig(qx * .5, time, 0) * .55);
                    double gain = 1.90 * lit * (.45 + .55 * lit) * (1 + rip * 1.15 + well * .60) * (hover ? 1 : .68);
                    for (int n = 0; n < 21; n++) {
                        double tone = Tone(value + offsets[n]);
                        for (int c = 0; c < 3; c++) scene.Pixels[i + c] += (float)(weights[n, c] * tone);
                    }
                    for (int c = 0; c < 3; c++) scene.Pixels[i + c] *= (float)(gain * pill / totals[c]);
                }
                scene.Pixels[i + 3] = (float)pill;
            }
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) {
                double dx = x + .5 - width / 2.0, dy = y + .5 - height / 2.0;
                double sd = Distance(dx, dy, straight, half);
                if (sd > 3.75 || sd < -5.25) continue;
                int i = (y * width + x) * 4;
                double well = Well(dx / h, dy / h, pointerX, pointerY, hover), rip = Ripple(dx / h, dy / h, age);
                double s = Perimeter(dx, -dy, straight, half), top = .825 - .175 * dy / half;
                double lift = 1 + (age >= 0 && age < .12 ? .85 : 0) + rip * 1.6 + well * .8;
                for (int c = 0; c < 3; c++) {
                    double band = 1 - Smooth(0, 1.575, Math.Abs(sd + .825 + (.42 - .42 * c)));
                    double coordinate = s + .03 - .03 * c;
                    double hot = .20 + .62 * Band(coordinate - time * .070, .075) + .44 * Band(coordinate + time * .0441 + .41, .135) + .30 * Band(coordinate - time * .0238 + .73, .20);
                    rim.Pixels[i + c] = (float)(band * hot * .82 * top * lift);
                }
            }
            var soft = scene;
            double sigma = .12 * h * .5 * .95;
            int iterations = Math.Min(4, Math.Max(1, (int)Math.Ceiling(sigma / 3)));
            for (int n = 0; n < iterations; n++) soft = Blur(Blur(soft, sigma / Math.Sqrt(iterations) / 1.95, true), sigma / Math.Sqrt(iterations) / 1.95, false);
            var bloom = Down(soft, rim, (width + 3) / 4, (height + 3) / 4);
            foreach (double radius in new[] { 1.0, 2.3, 5.2, 9.0 }) {
                double amount = radius * 1.3 * (h / 4) / 129;
                bloom = Blur(Blur(bloom, amount, true), amount, false);
            }
            var core = new byte[width * height * 4]; var edge = new byte[core.Length]; var glow = new byte[core.Length];
            var light = new double[3]; var outside = new double[3];
            var inner = new double[4]; var outer = new double[4];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) {
                int i = (y * width + x) * 4;
                double dx = x + .5 - width / 2.0, dy = y + .5 - height / 2.0;
                double pill = 1 - Smooth(-1, 1, Distance(dx, dy, straight, half)), u = (x + .5) / width, v = (y + .5) / height;
                for (int k = 0; k < 4; k++) inner[k] = 0;
                Tap(soft, u, v, 1, inner);
                for (int k = 0; k < 3; k++) outer[k] = 0;
                Tap(bloom, u, v, 1, outer, 3);
                double alpha = Math.Max(.001, inner[3]);
                double veil = 1 - Smooth(.46, .88, Math.Abs(dy) / half), rip = Ripple(dx / h, dy / h, age);
                // Push the exterior bloom harder so the rim reads as an emitting
                // liquid-metal edge instead of a flat silver outline.
                double glowGain = 2.20 * (hover ? 1.30 : 1);
                double shadow = Distance(dx, dy - half * .62, straight * .94, half * .94);
                double occlusion = .62 * Math.Exp(-Math.Max(shadow, 0) / (half * .75));
                double luminance = 0, bloomLuminance = 0;
                for (int c = 0; c < 3; c++) {
                    light[c] = Power175(Math.Max(0, inner[c] / alpha)) * pill * (1 - .35 * veil) + rim.Pixels[i + c];
                    light[c] += rip * rip * .45 * pill * (1 - .58 * veil);
                    outer[c] *= glowGain * (1 - occlusion * (1 - pill));
                    luminance += (light[c] + outer[c] * (1 - .7 * pill)) * (c == 0 ? .2126 : c == 1 ? .7152 : .0722);
                    bloomLuminance += outer[c] * (c == 0 ? .2126 : c == 1 ? .7152 : .0722);
                }
                double silver = Clamp(luminance * .7), halo = 0;
                for (int c = 0; c < 3; c++) {
                    outside[c] = Clamp((bloomLuminance * .84 + outer[c] * .16) * (c == 0 ? .93 : c == 1 ? .98 : 1.05) + rim.Pixels[i + c] * .48);
                    outside[c] *= 1 - Smooth(8, 12, Math.Max(0, Distance(dx, dy, straight, half)));
                    halo = Math.Max(halo, outside[c]);
                    light[c] = Clamp(((c == 0 ? .60 : c == 1 ? .63 : .65) + (c == 0 ? .38 : c == 1 ? .36 : .35) * silver) * (dark ? .30 : 1) + rim.Pixels[i + c] * .70);
                }
                // Shader output is already premultiplied; do not multiply the
                // exterior halo by its alpha a second time when making Pbgra32.
                Write(core, i, light[0] * pill + outside[0] * (1 - pill), light[1] * pill + outside[1] * (1 - pill), light[2] * pill + outside[2] * (1 - pill), pill + halo * (1 - pill));
                Write(edge, i, Clamp(rim.Pixels[i]), Clamp(rim.Pixels[i + 1]), Clamp(rim.Pixels[i + 2]), Clamp(Math.Max(rim.Pixels[i], Math.Max(rim.Pixels[i + 1], rim.Pixels[i + 2]))));
                Write(glow, i, outside[0], outside[1], outside[2], halo);
            }
            var bitmaps = new[] { Bitmap(core, width, height), Bitmap(edge, width, height), Bitmap(glow, width, height) };
            return bitmaps;
        }

        private sealed class Field
        {
            internal readonly int Width, Height;
            internal readonly float[] Pixels;
            internal Field(int width, int height) { Width = width; Height = height; Pixels = new float[width * height * 4]; }
        }
        private static double Sample(Field field, double u, double v, int channel)
        {
            double x = Math.Max(0, Math.Min(field.Width - 1, u * field.Width - .5)), y = Math.Max(0, Math.Min(field.Height - 1, v * field.Height - .5));
            int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, field.Width - 1), y1 = Math.Min(y0 + 1, field.Height - 1);
            double fx = x - x0, fy = y - y0;
            var p = field.Pixels;
            return (p[(y0 * field.Width + x0) * 4 + channel] * (1 - fx) + p[(y0 * field.Width + x1) * 4 + channel] * fx) * (1 - fy) +
                (p[(y1 * field.Width + x0) * 4 + channel] * (1 - fx) + p[(y1 * field.Width + x1) * 4 + channel] * fx) * fy;
        }

        // Bilinear tap that resolves the four indices once and accumulates every
        // channel. Blur and Down read all four channels at the same coordinate,
        // so this removes three quarters of the index arithmetic in both.
        private static void Tap(Field field, double u, double v, double weight, double[] into, int channels = 4)
        {
            double x = Math.Max(0, Math.Min(field.Width - 1, u * field.Width - .5)), y = Math.Max(0, Math.Min(field.Height - 1, v * field.Height - .5));
            int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, field.Width - 1), y1 = Math.Min(y0 + 1, field.Height - 1);
            double fx = x - x0, fy = y - y0, tl = (1 - fx) * (1 - fy), tr = fx * (1 - fy), bl = (1 - fx) * fy, br = fx * fy;
            var p = field.Pixels;
            int a = (y0 * field.Width + x0) * 4, b = (y0 * field.Width + x1) * 4;
            int c = (y1 * field.Width + x0) * 4, d = (y1 * field.Width + x1) * 4;
            for (int k = 0; k < channels; k++) into[k] += (p[a + k] * tl + p[b + k] * tr + p[c + k] * bl + p[d + k] * br) * weight;
        }

        private static Field Down(Field source, Field add, int width, int height)
        {
            var result = new Field(width, height);
            var sum = new double[4];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) {
                for (int k = 0; k < 4; k++) sum[k] = 0;
                for (int sy = -1; sy <= 1; sy += 2)
                for (int sx = -1; sx <= 1; sx += 2) {
                    double u = (x + .5 + sx * .25) / width, v = (y + .5 + sy * .25) / height;
                    Tap(source, u, v, .25, sum);
                    // The rim contributes light, not coverage: read its colour
                    // only, leaving the downsampled mask to the scene field.
                    if (add != null) Tap(add, u, v, .25, sum, 3);
                }
                int o = (y * width + x) * 4;
                for (int k = 0; k < 4; k++) result.Pixels[o + k] = (float)sum[k];
            }
            return result;
        }

        private static Field Blur(Field source, double radius, bool horizontal)
        {
            var result = new Field(source.Width, source.Height);
            double du = horizontal ? radius / source.Width : 0, dv = horizontal ? 0 : radius / source.Height;
            var positions = new[] { 1.4118, 3.2941, 5.1765 }; var weights = new[] { .2969, .0944, .0104 };
            var sum = new double[4];
            for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++) {
                for (int k = 0; k < 4; k++) sum[k] = 0;
                double u = (x + .5) / source.Width, v = (y + .5) / source.Height;
                Tap(source, u, v, .1964, sum);
                for (int n = 0; n < 3; n++) {
                    Tap(source, u + du * positions[n], v + dv * positions[n], weights[n], sum);
                    Tap(source, u - du * positions[n], v - dv * positions[n], weights[n], sum);
                }
                int o = (y * source.Width + x) * 4;
                for (int k = 0; k < 4; k++) result.Pixels[o + k] = (float)sum[k];
            }
            return result;
        }
        private static double Distance(double x, double y, double straight, double radius)
        { x = Math.Max(Math.Abs(x) - straight, 0); return Math.Sqrt(x * x + y * y) - radius; }
        private static double Perimeter(double x, double y, double straight, double radius)
        {
            double perimeter = 4 * straight + 2 * Math.PI * radius, s, angle;
            if (x >= straight) { angle = Math.Atan2(y, x - straight); if (angle < 0) angle += 2 * Math.PI; s = angle <= Math.PI / 2 ? radius * angle : perimeter - radius * (2 * Math.PI - angle); }
            else if (x <= -straight) { angle = Math.Atan2(y, x + straight); if (angle < 0) angle += 2 * Math.PI; s = radius * Math.PI / 2 + 2 * straight + radius * (angle - Math.PI / 2); }
            else s = y >= 0 ? radius * Math.PI / 2 + straight - x : radius * Math.PI * 1.5 + 2 * straight + x + straight;
            return s / perimeter;
        }
        private static double Well(double x, double y, double pointerX, double pointerY, bool hover)
        { x -= pointerX; y -= pointerY; return hover ? Math.Exp(-(x * x + y * y) / .3025) * .65 : 0; }
        private static double Ripple(double x, double y, double age)
        {
            if (age < 0 || age > 4) return 0;
            double facet = 1 + .18 * Math.Cos(6 * Math.Atan2(y, x) + age * 2.1);
            double distance = (Math.Sqrt(x * x + y * y) - age * 1.85 * facet) / .20;
            return Math.Exp(-Math.Pow(Math.Abs(distance) + .0001, 1.15)) * Math.Exp(-age * 1.35);
        }
        private static double Surface(double x, double y, double time)
        { return (y - Wig(x * .5, time, 0) * .55) * 2.4 * Math.Exp(2.2 * Wig(x * .32 + 9, time, 2.7)) + .12 * Fbm(x * .8 * 1.6 + time * .05, y * 1.7 * 1.6 - time * .03) - .05; }
        private static double Fbm(double x, double y)
        {
            double sum = 0, amount = 1, total = 0;
            for (int n = 0; n < 4; n++) { sum += amount * Noise(x, y); total += amount; x = x * 2.03 + 11.7; y = y * 2.03 + 11.7; amount *= .32; }
            return sum / total;
        }

        private static BitmapSource Bitmap(byte[] pixels, int width, int height)
        {
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
            bitmap.Freeze(); return bitmap;
        }
        private static void Write(byte[] pixels, int i, double r, double g, double b, double alpha)
        {
            alpha = Clamp(alpha);
            pixels[i] = (byte)Math.Round(Math.Min(alpha, Clamp(b)) * 255);
            pixels[i + 1] = (byte)Math.Round(Math.Min(alpha, Clamp(g)) * 255);
            pixels[i + 2] = (byte)Math.Round(Math.Min(alpha, Clamp(r)) * 255);
            pixels[i + 3] = (byte)Math.Round(alpha * 255);
        }
        private static double Clamp(double value) { return Math.Max(0, Math.Min(1, value)); }
        // x^1.75 == x * (x^3)^(1/4). Two square roots replace a transcendental
        // call in the compositor's inner loop, for nonnegative inputs.
        private static double Power175(double x)
        { return x <= 0 ? 0 : x * Math.Sqrt(Math.Sqrt(x * x * x)); }
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
