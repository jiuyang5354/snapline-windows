using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Snapline
{
    internal sealed class AnnouncementWindow : Window
    {
        internal readonly TextBox Notes;
        internal readonly Button Dismiss;
        internal static string Version { get { return Updates.CurrentVersion.ToString(3); } }
        internal static bool Due(Settings settings) { return settings.AnnouncementSeenVersion != Version; }

        internal AnnouncementWindow()
        {
            Ui.Initialize();
            Title = "Snapline · 更新公告";
            Width = Math.Min(620, SystemParameters.WorkArea.Width - 24);
            Height = Math.Min(560, SystemParameters.WorkArea.Height - 24);
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Ui.Brush("Paper"); FontFamily = Ui.Font;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Snapline.ico"))
                Icon = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var panel = new Grid { Margin = new Thickness(24) };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition());
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(Ui.Text("更新公告", 24, false));
            var version = Ui.Text("v" + Version + " · Windows 预发布", 12, true);
            version.Margin = new Thickness(0, 8, 0, 20); Grid.SetRow(version, 1); panel.Children.Add(version);
            string notes;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Snapline.Announcement"))
            using (var reader = new StreamReader(stream)) notes = reader.ReadToEnd();
            Notes = new TextBox { Text = notes, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                FontSize = 13, Foreground = Ui.Brush("Ink"), Background = Ui.Brush("Surface"),
                BorderBrush = Ui.Brush("Line"), BorderThickness = new Thickness(1), Padding = new Thickness(14) };
            System.Windows.Automation.AutomationProperties.SetName(Notes, "本版更新内容");
            Grid.SetRow(Notes, 2); panel.Children.Add(Notes);
            Dismiss = Ui.Button("知道了", Close);
            Dismiss.Style = (Style)Application.Current.FindResource("Primary");
            Dismiss.IsDefault = true; Dismiss.IsCancel = true;
            Dismiss.HorizontalAlignment = HorizontalAlignment.Right; Dismiss.Margin = new Thickness(0, 20, 0, 0);
            Grid.SetRow(Dismiss, 3); panel.Children.Add(Dismiss);
            Content = panel;
        }
    }
}
