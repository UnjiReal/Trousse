using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SkyrimOutfit
{
    internal sealed partial class App
    {
        private bool startupLoading;

        private static string AppVersion
        {
            get
            {
                var assembly = Assembly.GetExecutingAssembly();
                var version = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(assembly, typeof(AssemblyInformationalVersionAttribute));
                return version == null ? assembly.GetName().Version.ToString(3) : version.InformationalVersion;
            }
        }

        private void Launch(Application application)
        {
            application.MainWindow = window;
            Window splash = MakeSplash();
            splash.ContentRendered += async (sender, args) => {
                if (startupLoading) return;
                startupLoading = true;
                try { await PrepareStartup(); }
                catch (Exception error) { ShowError(error); }
                finally
                {
                    startupLoading = false;
                    window.Show();
                    splash.Close();
                    window.Activate();
                }
            };
            splash.Show();
            application.Run();
        }

        private async Task PrepareStartup()
        {
            if (!Directory.Exists(vortexPath.Text) || !Preferences.HasBodySlide(bodyPath.Text))
            {
                Switch("SettingsPage");
                SetStatus(T("Set your paths in Settings, then scan the library.", "Imposta i percorsi e scansiona la libreria dalle impostazioni."));
            }
            else if (autoScan.IsChecked == true) await Scan();
            else SetStatus(T("Scan the library from Settings.", "Scansiona la libreria dalle impostazioni."));
        }

        private Window MakeSplash()
        {
            double width = Math.Min(800, SystemParameters.WorkArea.Width - 42);
            var image = new Image { Stretch = Stretch.UniformToFill, Height = width * 9 / 16 };
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("MarketSplash.jpg"))
                if (resource != null) image.Source = ImageFromStream(resource);

            var footer = new StackPanel { Margin = new Thickness(22, 12, 22, 17) };
            var title = new DockPanel();
            var version = new TextBlock { Text = AppVersion, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(191, 182, 168)), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(version, Dock.Right);
            title.Children.Add(version);
            title.Children.Add(new TextBlock { Text = "Trousse", FontFamily = new FontFamily("Georgia"), FontSize = 30, FontWeight = FontWeights.SemiBold });
            footer.Children.Add(title);
            footer.Children.Add(new TextBlock { Text = T("Preparing the outfit library…", "Preparazione della libreria outfit…"), Margin = new Thickness(0, 5, 0, 10) });
            footer.Children.Add(new ProgressBar { Height = 3, IsIndeterminate = true, Foreground = new SolidColorBrush(Color.FromRgb(209, 172, 119)) });
            var layout = new StackPanel();
            layout.Children.Add(image);
            layout.Children.Add(footer);

            return new Window {
                Title = "Trousse", Icon = window.Icon, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen, SizeToContent = SizeToContent.Height,
                Width = width, ShowInTaskbar = false, Background = window.Background,
                Foreground = window.Foreground, Content = layout
            };
        }
    }
}
