using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Snapline
{
    internal sealed class SettingsGlassEffect : ShaderEffect
    {
        private static readonly PixelShader Shader = Load();
        private static PixelShader Load()
        {
            var shader = new PixelShader();
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Snapline.SettingsGlass.ps")) shader.SetStreamSource(stream);
            shader.Freeze(); return shader;
        }
        internal static readonly DependencyProperty BackdropProperty = RegisterPixelShaderSamplerProperty("Backdrop", typeof(SettingsGlassEffect), 0, SamplingMode.Bilinear);
        internal static readonly DependencyProperty OpticsProperty = RegisterPixelShaderSamplerProperty("Optics", typeof(SettingsGlassEffect), 1, SamplingMode.Bilinear);
        internal static readonly DependencyProperty OriginProperty = DependencyProperty.Register("Origin", typeof(Point), typeof(SettingsGlassEffect), new UIPropertyMetadata(new Point(), PixelShaderConstantCallback(0)));
        internal static readonly DependencyProperty ExtentProperty = DependencyProperty.Register("Extent", typeof(Point), typeof(SettingsGlassEffect), new UIPropertyMetadata(new Point(1, 1), PixelShaderConstantCallback(1)));
        internal static readonly DependencyProperty DeflectionProperty = DependencyProperty.Register("Deflection", typeof(Point), typeof(SettingsGlassEffect), new UIPropertyMetadata(new Point(), PixelShaderConstantCallback(2)));
        internal static readonly DependencyProperty VeilProperty = DependencyProperty.Register("Veil", typeof(double), typeof(SettingsGlassEffect), new UIPropertyMetadata(.28, PixelShaderConstantCallback(3)));
        internal Point Origin { set { SetValue(OriginProperty, value); } }
        internal Point Extent { set { SetValue(ExtentProperty, value); } }
        internal Point Deflection { set { SetValue(DeflectionProperty, value); } }
        internal double Veil { set { SetValue(VeilProperty, value); } }
        internal SettingsGlassEffect()
        {
            PixelShader = Shader;
            foreach (var property in new[] { BackdropProperty, OpticsProperty, OriginProperty, ExtentProperty, DeflectionProperty, VeilProperty }) UpdateShaderValue(property);
        }
        internal void SetBackdrop(BitmapSource image)
        { var brush = new ImageBrush(image) { Stretch = Stretch.Fill }; brush.Freeze(); SetValue(BackdropProperty, brush); }
        internal void SetOptics(Size size, double scale)
        {
            int width = Math.Max(1, (int)Math.Ceiling(size.Width * scale)), height = Math.Max(1, (int)Math.Ceiling(size.Height * scale));
            double radius = 24 * scale, bevel = 21 * scale;
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) {
                double px = x + .5 - width / 2.0, py = y + .5 - height / 2.0;
                double qx = Math.Abs(px) - width / 2.0 + radius, qy = Math.Abs(py) - height / 2.0 + radius;
                double ax = Math.Max(qx, 0), ay = Math.Max(qy, 0), length = Math.Sqrt(ax * ax + ay * ay);
                double distance = length + Math.Min(Math.Max(qx, qy), 0) - radius;
                int i = (y * width + x) * 4;
                if (distance >= 0) continue;
                double dx = 0, dy = 0, highlight = 0;
                if (distance > -bevel) {
                    double nx = length > .0001 ? ax / length * Math.Sign(px) : qx > qy ? Math.Sign(px) : 0;
                    double ny = length > .0001 ? ay / length * Math.Sign(py) : qx > qy ? 0 : Math.Sign(py);
                    double t = Math.Max(0, Math.Min(1, -distance / bevel)), inv = 1 - t;
                    double slope = Math.Pow(inv, 3) / Math.Pow(Math.Max(.00001, 1 - Math.Pow(inv, 4)), .75) * 2.5;
                    double incident = Math.Atan(slope), bend = Math.Sin(incident - Math.Asin(Math.Sin(incident) / 1.5));
                    dx = nx * bend; dy = ny * bend;
                    double nz = 1 / Math.Sqrt(dx * dx * 4 + dy * dy * 4 + 1);
                    double lit = Math.Max(0, (-dx * 2 * -.5 - dy * 2 * -.7 + .7) * nz / 1.109);
                    highlight = Math.Min(.65, Math.Pow(lit, 16) * .8 + Math.Pow(1 - nz, 2.5) * .4);
                }
                // RGB channels store displacement and directional light, not a
                // rendered bright border. The shader bends the same background.
                pixels[i] = (byte)Math.Round(highlight * 255);
                pixels[i + 1] = (byte)Math.Round(128 + Math.Max(-127, Math.Min(127, dy * 127)));
                pixels[i + 2] = (byte)Math.Round(128 + Math.Max(-127, Math.Min(127, dx * 127)));
                pixels[i + 3] = 255;
            }
            var map = BitmapSource.Create(width, height, 96 * scale, 96 * scale, PixelFormats.Bgra32, null, pixels, width * 4); map.Freeze();
            var brush = new ImageBrush(map) { Stretch = Stretch.Fill }; brush.Freeze(); SetValue(OpticsProperty, brush);
        }
    }
}
