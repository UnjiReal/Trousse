using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SkyrimOutfit
{
    internal sealed partial class App
    {
        private bool UseModOrganizer { get { return Find<ComboBox>("ModManager").SelectedIndex == 1; } }

        private void ConfigureModManager()
        {
            var manager = Find<ComboBox>("ModManager");
            var ini = Find<TextBox>("Mo2Ini");
            string[] args = Environment.GetCommandLineArgs();
            int option = Array.FindIndex(args, arg => Files.Comparer.Equals(arg, "--mo2"));
            manager.SelectedIndex = option >= 0 || (settings.UseMo2 &&
                String.IsNullOrEmpty(Environment.GetEnvironmentVariable("TROUSSE_VORTEX_STAGING"))) ? 1 : 0;
            ini.Text = option >= 0 && option + 1 < args.Length ? args[option + 1] : settings.Mo2Ini ?? "";
            RefreshModManager();
            ini.TextChanged += PathsChanged;
            manager.SelectionChanged += (sender, argsChanged) => {
                RefreshModManager();
                InvalidatePaths();
            };
            Bind("BrowseMo2", () => {
                var picker = new OpenFileDialog { Filter = "ModOrganizer.ini|ModOrganizer.ini|INI (*.ini)|*.ini", CheckFileExists = true };
                if (File.Exists(ini.Text)) picker.InitialDirectory = Path.GetDirectoryName(ini.Text);
                if (picker.ShowDialog(window) == true) ini.Text = picker.FileName;
            });
        }

        private string ModOrganizerHint()
        {
            return T("Select the instance’s ModOrganizer.ini. Start Trousse with Run in MO2 to use the active profile.",
                "Seleziona il file ModOrganizer.ini dell’istanza. Avvia Trousse con Esegui in MO2 per usare il profilo attivo.");
        }

        private void RefreshModManager()
        {
            Find<FrameworkElement>("VortexPaths").Visibility = UseModOrganizer ? Visibility.Collapsed : Visibility.Visible;
            Find<FrameworkElement>("Mo2Paths").Visibility = UseModOrganizer ? Visibility.Visible : Visibility.Collapsed;
            Find<TextBlock>("Mo2Hint").Text = ModOrganizerHint();
        }

        private string SelectedDataPath()
        {
            if (catalog != null) return catalog.DataRoot;
            if (UseModOrganizer) return null;
            string body = bodyPath.Text.Trim().Trim('"');
            return Preferences.HasBodySlide(body) ? Catalog.GameData(body) : null;
        }
    }
}
