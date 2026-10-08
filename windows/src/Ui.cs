using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace Snapline
{
    internal static class Ui
    {
        internal static readonly FontFamily Font = new FontFamily("Microsoft YaHei UI");

        internal static void Initialize()
        {
            if (Application.Current.Resources.Contains("Surface")) return;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Snapline.Theme.xaml"))
                Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
        }

        internal static T Load<T>(string name) where T : class
        {
            Initialize();
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Snapline." + name + ".xaml"))
                return (T)XamlReader.Load(stream);
        }

        internal static Brush Brush(string name) { return (Brush)Application.Current.FindResource(name); }
        internal static TextBlock Text(string text, double size, bool muted)
        { return new TextBlock { Text = text, FontFamily = Font, FontSize = size, Foreground = Brush(muted ? "Muted" : "Ink") }; }

        internal static Button Button(string text, Action action)
        {
            var button = new Button { Content = text, Style = (Style)Application.Current.FindResource(typeof(Button)), FontFamily = Font };
            button.Click += delegate { action(); };
            return button;
        }
    }
}
