using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Snapline
{
    internal sealed class ToolSheen : ShaderEffect
    {
        private static readonly PixelShader shader = Load();
        private static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(ToolSheen), 0);
        private static readonly DependencyProperty CenterProperty = DependencyProperty.Register("Center", typeof(Point), typeof(ToolSheen), new UIPropertyMetadata(new Point(.5, .5), PixelShaderConstantCallback(0)));
        private static PixelShader Load()
        {
            var value = new PixelShader();
            using (var stream = typeof(ToolSheen).Assembly.GetManifestResourceStream("Snapline.ToolSheen.ps")) value.SetStreamSource(stream);
            return value;
        }
        private ToolSheen() { PixelShader = shader; UpdateShaderValue(InputProperty); UpdateShaderValue(CenterProperty); }
        internal static void Attach(Button button)
        {
            ToolSheen effect = null;
            button.MouseEnter += delegate {
                if ((RenderCapability.Tier >> 16) < 1 || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast || !button.IsEnabled) return;
                effect = new ToolSheen(); button.Effect = effect;
            };
            button.MouseMove += delegate(object sender, System.Windows.Input.MouseEventArgs e) {
                if (effect == null) return;
                Point point = e.GetPosition(button); effect.SetValue(CenterProperty, new Point(point.X / Math.Max(1, button.ActualWidth), point.Y / Math.Max(1, button.ActualHeight)));
            };
            Action stop = delegate { button.Effect = null; effect = null; };
            button.MouseLeave += delegate { stop(); }; button.IsEnabledChanged += delegate { if (!button.IsEnabled) stop(); }; button.Unloaded += delegate { stop(); };
        }
    }
}
