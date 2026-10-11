using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Drawing = System.Drawing;

namespace Snapline
{
    // Native adaptation of the supplied skill: refract the backdrop at each
    // photo frame's actual position, keeping the screenshot in a separate layer.
    internal sealed class GlassBackdrop
    {
        private readonly byte[] pixels;
        private readonly int width, height;
        internal readonly double Scale;

        internal GlassBackdrop(BitmapSource image, double scale)
        {
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            width = converted.PixelWidth; height = converted.PixelHeight; Scale = scale;
            pixels = new byte[width * height * 4];
            converted.CopyPixels(pixels, width * 4, 0);
        }

        internal static bool Enabled
        {
            get { return !SystemParameters.HighContrast && !Equals(Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1), 0); }
        }

        internal static GlassBackdrop Capture(Drawing.Rectangle bounds, double scale)
        {
            if (!Enabled || bounds.Width < 1 || bounds.Height < 1) return null;
            try
            {
                using (var bitmap = new Drawing.Bitmap(bounds.Width, bounds.Height, Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (var graphics = Drawing.Graphics.FromImage(bitmap))
                        graphics.CopyFromScreen(bounds.Location, Drawing.Point.Empty, bounds.Size, Drawing.CopyPixelOperation.SourceCopy);
                    var data = bitmap.LockBits(new Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                        Drawing.Imaging.ImageLockMode.ReadOnly, Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try
                    {
                        var buffer = new byte[bitmap.Width * bitmap.Height * 4];
                        for (int y = 0; y < bitmap.Height; y++)
                            Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), buffer, y * bitmap.Width * 4, bitmap.Width * 4);
                        return new GlassBackdrop(BitmapSource.Create(bitmap.Width, bitmap.Height, 96 * scale, 96 * scale,
                            PixelFormats.Bgra32, null, buffer, bitmap.Width * 4), scale);
                    }
                    finally { bitmap.UnlockBits(data); }
                }
            }
            catch (Win32Exception) { return null; }
        }

        internal BitmapSource Bevel(Rect area, double radius)
        {
            return new BevelMap(area.Size, radius, Scale).Render(this, new Matrix(1, 0, 0, 1, area.X, area.Y));
        }

        internal double Luminance(Rect area)
        {
            double total = 0;
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++) {
                int sx = Math.Max(0, Math.Min(width - 1, (int)((area.X + area.Width * (x + .5) / 8) * Scale)));
                int sy = Math.Max(0, Math.Min(height - 1, (int)((area.Y + area.Height * (y + .5) / 8) * Scale)));
                int i = (sy * width + sx) * 4;
                total += (.2126 * pixels[i + 2] + .7152 * pixels[i + 1] + .0722 * pixels[i]) / 255;
            }
            return total / 64;
        }

        internal BitmapSource Sample(Rect area)
        {
            int w = Math.Max(1, (int)Math.Ceiling(area.Width * Scale));
            int h = Math.Max(1, (int)Math.Ceiling(area.Height * Scale));
            int left = (int)Math.Floor(area.X * Scale), top = (int)Math.Floor(area.Y * Scale);
            var output = new byte[w * h * 4];
            for (int y = 0; y < h; y++) {
                int sy = Math.Max(0, Math.Min(height - 1, top + y));
                if (left >= 0 && left + w <= width)
                    Buffer.BlockCopy(pixels, (sy * width + left) * 4, output, y * w * 4, w * 4);
                else for (int x = 0; x < w; x++) {
                    int sx = Math.Max(0, Math.Min(width - 1, left + x));
                    Buffer.BlockCopy(pixels, (sy * width + sx) * 4, output, (y * w + x) * 4, 4);
                }
                // Screen-copy APIs can supply zero alpha for visible desktop pixels.
                for (int x = 0; x < w; x++) output[(y * w + x) * 4 + 3] = 255;
            }
            var result = BitmapSource.Create(w, h, 96 * Scale, 96 * Scale, PixelFormats.Bgra32, null, output, w * 4);
            result.Freeze(); return result;
        }

        internal sealed class BevelMap
        {
            private struct RimPixel
            {
                internal int Offset;
                internal double X, Y, Dx, Dy, Alpha, Highlight;
            }

            private readonly List<RimPixel> rim = new List<RimPixel>();
            private readonly int width, height;
            private readonly double scale;

            internal BevelMap(Size size, double radius, double scale, double bevelWidth = 11)
            {
                this.scale = scale;
                width = Math.Max(1, (int)Math.Ceiling(size.Width * scale));
                height = Math.Max(1, (int)Math.Ceiling(size.Height * scale));
                double r = Math.Min(radius * scale, Math.Min(width, height) / 2.0);
                double bevel = Math.Min(bevelWidth * scale, r);
                // Cache geometry and Snell deflection; animation only resamples the backdrop.
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    double px = x + 0.5 - width / 2.0, py = y + 0.5 - height / 2.0;
                    double qx = Math.Abs(px) - (width / 2.0 - r), qy = Math.Abs(py) - (height / 2.0 - r);
                    double ax = Math.Max(qx, 0), ay = Math.Max(qy, 0);
                    double length = Math.Sqrt(ax * ax + ay * ay);
                    double distance = length + Math.Min(Math.Max(qx, qy), 0) - r;
                    if (distance >= 0 || distance <= -bevel) continue;
                    double nx, ny;
                    if (length > 0.0001) { nx = ax / length * Math.Sign(px); ny = ay / length * Math.Sign(py); }
                    else { nx = qx > qy ? Math.Sign(px) : 0; ny = qx > qy ? 0 : Math.Sign(py); }
                    double t = Math.Max(0, Math.Min(1, -distance / bevel));
                    double inv = 1 - t;
                    double slope = Math.Pow(inv, 3) / Math.Pow(Math.Max(0.00001, 1 - Math.Pow(inv, 4)), 0.75) * 2.5;
                    double incident = Math.Atan(slope);
                    double deflection = Math.Sin(incident - Math.Asin(Math.Sin(incident) / 1.5));
                    double nz = 1 / Math.Sqrt(deflection * deflection * 4 + 1);
                    double light = Math.Max(0, (-nx * deflection * 0.5 - ny * deflection * 0.7 + 0.7) * nz / 1.109);
                    rim.Add(new RimPixel {
                        Offset = (y * width + x) * 4, X = (x + 0.5) / scale, Y = (y + 0.5) / scale,
                        Dx = nx * deflection * 11, Dy = ny * deflection * 11,
                        Highlight = Math.Min(0.65, Math.Pow(light, 16) * 0.8 + Math.Pow(1 - nz, 2.5) * 0.4),
                        Alpha = Math.Min(1, -distance) * (1 - Math.Pow(t, 1.5)) * 0.92
                    });
                }
            }

            internal BitmapSource Render(GlassBackdrop backdrop, Matrix position)
            {
                var output = new byte[width * height * 4];
                foreach (var pixel in rim)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        double dispersion = c == 0 ? 0.96 : c == 2 ? 1.04 : 1;
                        var point = position.Transform(new Point(pixel.X + pixel.Dx * dispersion, pixel.Y + pixel.Dy * dispersion));
                        int sx = Math.Max(0, Math.Min(backdrop.width - 1, (int)Math.Floor(point.X * backdrop.Scale)));
                        int sy = Math.Max(0, Math.Min(backdrop.height - 1, (int)Math.Floor(point.Y * backdrop.Scale)));
                        int source = (sy * backdrop.width + sx) * 4;
                        output[pixel.Offset + c] = (byte)Math.Round((backdrop.pixels[source + c] * (1 - pixel.Highlight) + 255 * pixel.Highlight) * pixel.Alpha);
                    }
                    output[pixel.Offset + 3] = (byte)Math.Round(pixel.Alpha * 255);
                }
                var result = BitmapSource.Create(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32, null, output, width * 4);
                result.Freeze(); return result;
            }
        }
    }

    internal sealed class GlassFrame : Grid
    {
        private readonly GlassBackdrop backdrop;
        private readonly GlassBackdrop.BevelMap bevel;
        private readonly Image material = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
        private Matrix? previous;

        internal GlassFrame(Size size, double radius, GlassBackdrop backdrop, Image photo)
        {
            Width = size.Width; Height = size.Height;
            this.backdrop = backdrop;
            bool enabled = GlassBackdrop.Enabled;
            if (enabled && backdrop != null)
            {
                bevel = new GlassBackdrop.BevelMap(size, radius, backdrop.Scale);
                Children.Add(material);
                Loaded += delegate { CompositionTarget.Rendering += UpdateMaterial; UpdateMaterial(null, EventArgs.Empty); };
                Unloaded += delegate { CompositionTarget.Rendering -= UpdateMaterial; previous = null; };
            }
            Children.Add(new Border {
                CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1), IsHitTestVisible = false,
                Background = enabled ? (Brush)new SolidColorBrush(Color.FromArgb(10, 244, 250, 255)) : SystemColors.WindowBrush,
                BorderBrush = enabled ? (Brush)new LinearGradientBrush(Color.FromArgb(205, 255, 255, 255), Color.FromArgb(44, 255, 255, 255), 55) : SystemColors.WindowTextBrush
            });
            Children.Add(new Border {
                Margin = new Thickness(2), CornerRadius = new CornerRadius(Math.Max(0, radius - 2)),
                BorderThickness = new Thickness(0.6), BorderBrush = new LinearGradientBrush(Color.FromArgb(95, 255, 255, 255), Color.FromArgb(8, 255, 255, 255), 55),
                IsHitTestVisible = false
            });
            photo.HorizontalAlignment = HorizontalAlignment.Center; photo.VerticalAlignment = VerticalAlignment.Center;
            Children.Add(photo);
        }

        private void UpdateMaterial(object sender, EventArgs args)
        {
            var window = Window.GetWindow(this);
            if (window == null || !IsVisible) return;
            var transform = TransformToAncestor(window);
            Point origin = transform.Transform(new Point()), x = transform.Transform(new Point(1, 0)), y = transform.Transform(new Point(0, 1));
            var position = new Matrix(x.X - origin.X, x.Y - origin.Y, y.X - origin.X, y.Y - origin.Y, origin.X, origin.Y);
            if (previous.HasValue && previous.Value == position) return;
            material.Source = bevel.Render(backdrop, position);
            previous = position;
        }
    }
}
