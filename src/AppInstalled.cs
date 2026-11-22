using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace SkyrimOutfit
{
    internal sealed partial class App
    {
        private InstalledOutfits installed;
        private LiveViewport installedViewport;
        private ListBox installedList;
        private ComboBox installedRace, installedSex, installedCategory, installedPerspective;
        private CheckBox installedUnique, installedLow;
        private int installedCheck;
        private bool changingInstalled;

        private void ConfigureInstalledPage()
        {
            installedList = Find<ListBox>("CurrentList");
            installedRace = Find<ComboBox>("CurrentRace");
            installedSex = Find<ComboBox>("CurrentSex");
            installedCategory = Find<ComboBox>("CurrentCategory");
            installedPerspective = Find<ComboBox>("CurrentPerspective");
            installedUnique = Find<CheckBox>("CurrentUnique");
            installedLow = Find<CheckBox>("CurrentLowWeight");
            installedViewport = new LiveViewport(Find<Viewport3D>("CurrentViewport"), Find<Border>("CurrentSurface"));
            Bind("CurrentNav", async () => { Switch("CurrentPage"); await CheckInstalledOutfits(); });
            Bind("CurrentRefresh", async () => await CheckInstalledOutfits());
            Bind("CurrentCopy", CopyInstalledCommand);
            Find<TextBox>("CurrentSearch").TextChanged += (s, e) => RefreshInstalledList();
            installedList.SelectionChanged += (s, e) => { if (!changingInstalled) RefreshInstalledMesh(); };
            installedUnique.Checked += (s, e) => InstalledOptionChanged();
            installedUnique.Unchecked += (s, e) => InstalledOptionChanged();
            installedRace.SelectionChanged += (s, e) => InstalledOptionChanged();
            installedSex.SelectionChanged += (s, e) => RefreshInstalledList();
            installedCategory.SelectionChanged += (s, e) => RefreshInstalledList();
            installedPerspective.SelectionChanged += (s, e) => RefreshInstalledList();
            installedLow.Checked += (s, e) => RefreshInstalledMesh();
            installedLow.Unchecked += (s, e) => RefreshInstalledMesh();
        }

        private void LocalizeInstalledPage()
        {
            int race = Array.FindIndex(InstalledOutfits.Races, name => Files.Comparer.Equals(name, settings.PreviewRace));
            changingInstalled = true;
            int sex = Math.Max(0, installedSex.SelectedIndex), category = Math.Max(0, installedCategory.SelectedIndex);
            int perspective = Math.Max(0, installedPerspective.SelectedIndex);
            installedRace.ItemsSource = RaceLabels();
            installedRace.SelectedIndex = Math.Max(0, race);
            installedSex.ItemsSource = T("All sexes|Female|Male|Shared", "Tutti i sessi|Donna|Uomo|Condiviso").Split('|');
            installedSex.SelectedIndex = sex;
            installedCategory.ItemsSource = EquipmentCategoryLabels();
            installedCategory.SelectedIndex = category;
            installedPerspective.ItemsSource = T("All views|Third person|First person", "Tutte le viste|Terza persona|Prima persona").Split('|');
            installedPerspective.SelectedIndex = perspective;
            changingInstalled = false;
            AutomationProperties.SetName(installedRace, T("Race", "Razza"));
            AutomationProperties.SetName(installedSex, T("Sex", "Sesso"));
            AutomationProperties.SetName(installedCategory, T("Equipment category", "Categoria equipaggiamento"));
            AutomationProperties.SetName(installedPerspective, T("View", "Vista"));
            AutomationProperties.SetName(installedList, T("Installed equipment", "Equipaggiamento installato"));
            UpdateRaceChoice();
            RefreshInstalledList();
        }

        private string[] RaceLabels()
        {
            return T("Nord|Argonian|Breton|Dark Elf|High Elf|Imperial|Khajiit|Orc|Redguard|Wood Elf|Elder",
                "Nord|Argoniano|Bretone|Elfo oscuro|Elfo alto|Imperiale|Khajiit|Orco|Redguard|Elfo dei boschi|Anziano").Split('|');
        }

        private async Task CheckInstalledOutfits()
        {
            int check = ++installedCheck;
            Catalog scanned = catalog;
            var previous = installedList.SelectedItem as InstalledChoice;
            string staging = vortexPath.Text.Trim().Trim('"');
            installed = null;
            UpdateRaceChoice();
            RefreshInstalledList();
            if (scanned == null) return;
            var detection = Find<TextBlock>("CurrentDetection");
            detection.Text = T("Checking the installed plugins…", "Controllo dei plugin installati…");
            try
            {
                string data = scanned.DataRoot;
                var found = await Task.Run(() => new InstalledOutfits(staging, data, InstalledOutfits.PluginListPath(data), scanned.BodyRoot));
                if (check != installedCheck) return;
                installed = found;
                installedViewport.ResetTextureCache();
                UpdateRaceChoice();
                RefreshInstalledList(previous);
            }
            catch (Exception error)
            {
                if (check == installedCheck)
                    detection.Text = T("Could not inspect Skyrim’s data files.", "Impossibile esaminare i file di Skyrim.") + " " + error.Message;
            }
        }

        private void UpdateRaceChoice()
        {
            var detection = Find<TextBlock>("CurrentDetection");
            string message = "";
            bool active = false, available = false;
            if (installed != null)
            {
                active = installed.State == RaceInstallation.Active;
                available = installed.CanPreviewRace;
                switch (installed.State)
                {
                    case RaceInstallation.Active:
                        message = T("Unique Bodies by Race is installed and its plugin is active.", "Unique Bodies by Race è installato e il plugin è attivo.");
                        break;
                    case RaceInstallation.Unverified:
                        message = T("Plugin found in Data. Its activation could not be verified.", "Plugin presente in Data. Impossibile verificarne l’attivazione.");
                        break;
                    case RaceInstallation.Disabled:
                        message = T("Racial files found in Data. The plugin was not listed as active.", "File per razza presenti in Data. Il plugin non risulta attivo nell’elenco del profilo.");
                        break;
                    case RaceInstallation.Staged:
                        message = T("Mod found in Vortex. Deploy it and enable its plugin.", "Mod trovata in Vortex. Fai il deploy e attiva il plugin.");
                        break;
                    default:
                        message = T("Unique Bodies by Race was not detected.", "Unique Bodies by Race non rilevato.");
                        break;
                }
            }
            changingInstalled = true;
            installedUnique.IsEnabled = available;
            installedUnique.IsChecked = available && (settings.UseRaceMeshes ?? active);
            installedRace.IsEnabled = installedUnique.IsChecked == true;
            changingInstalled = false;
            detection.Text = message;
            detection.ToolTip = installed == null ? null : installed.Warning;
        }

        private void InstalledOptionChanged()
        {
            if (changingInstalled) return;
            if (installedRace.SelectedIndex >= 0) settings.PreviewRace = InstalledOutfits.Races[installedRace.SelectedIndex];
            if (installedUnique.IsEnabled) settings.UseRaceMeshes = installedUnique.IsChecked == true;
            installedRace.IsEnabled = installedUnique.IsChecked == true;
            if (window.IsLoaded) SavePreferences();
            RefreshInstalledList();
        }

        private void RefreshInstalledList(InstalledChoice previous = null)
        {
            if (changingInstalled) return;
            var selected = previous ?? installedList.SelectedItem as InstalledChoice;
            string query = Find<TextBox>("CurrentSearch").Text;
            var category = (EquipmentCategory)Math.Max(0, installedCategory.SelectedIndex);
            int view = installedPerspective.SelectedIndex;
            string sex = new[] { "", "Female", "Male", "Shared" }[Math.Max(0, installedSex.SelectedIndex)];
            string race = installedUnique.IsChecked == true && installedRace.SelectedIndex >= 0
                ? InstalledOutfits.Races[installedRace.SelectedIndex] : null;
            var choices = installed == null || catalog == null ? new InstalledChoice[0] : catalog.Targets
                .Where(target => (category == EquipmentCategory.All || category == target.Category) &&
                    MatchesPerspective(target, view) &&
                    target.ViewKind.StartsWith(sex, StringComparison.Ordinal) &&
                    Match(query, target.DisplayName(italian), target.DisplayName(false), target.Output, target.AddonName,
                    String.Join(" ", target.Items.Select(item => item.EditorId + " " + item.ConsoleId + " " + item.Name(italian)))))
                .SelectMany(target => installed.Paths(target, race).Select(path => new InstalledChoice {
                    Target = target, Name = target.DisplayName(italian), Mesh = path, View = EquipmentView(target.ViewKind)
                }))
                .OrderBy(choice => choice.Target.Items.Count == 0).ThenBy(choice => choice.Name, Files.Comparer)
                .ThenBy(choice => choice.Target.ViewKind.Contains("first person"))
                .ThenBy(choice => choice.Mesh, Files.Comparer).ToArray();
            changingInstalled = true;
            installedList.ItemsSource = choices;
            installedList.SelectedItem = choices.FirstOrDefault(choice => selected != null &&
                choice.Target.Key == selected.Target.Key && Files.Comparer.Equals(choice.Mesh, selected.Mesh))
                ?? choices.FirstOrDefault(choice => choice.Target.Key == (selected == null ? (destination == null ? null : destination.Key) : selected.Target.Key))
                ?? choices.FirstOrDefault();
            changingInstalled = false;
            RefreshInstalledMesh();
        }

        internal static bool MatchesPerspective(Target target, int view)
        {
            return view == 1 ? target.ThirdPerson : view != 2 || target.FirstPerson;
        }

        private void UpdateInstalledButtons()
        {
            var choice = installedList.SelectedItem as InstalledChoice;
            buttons["CurrentCopy"].IsEnabled = !busy && choice != null && choice.Target.Items.Count > 0;
        }

        private void CopyInstalledCommand()
        {
            var choice = installedList.SelectedItem as InstalledChoice;
            if (choice == null || choice.Target.Items.Count == 0) return;
            GameItem selected = choice.Target.Items[0];
            if (choice.Target.Items.Count > 1)
            {
                var items = choice.Target.Items.OrderBy(item => item.Name(italian), Files.Comparer)
                    .ThenBy(item => item.ConsoleId).ToArray();
                var list = new ListBox { MaxHeight = 350, Style = (Style)window.FindResource(typeof(ListBox)),
                    ItemContainerStyle = (Style)window.FindResource(typeof(ListBoxItem)),
                    ItemsSource = items.Select(item => item.Name(italian) + " · " + item.ConsoleId + " · " + item.EditorId).ToArray(),
                    SelectedIndex = 0 };
                var copy = new Button { Content = T("Copy command", "Copia comando"), IsDefault = true,
                    HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0),
                    Style = (Style)window.FindResource("Accent") };
                var panel = new StackPanel { Margin = new Thickness(16) };
                panel.Children.Add(list);
                panel.Children.Add(copy);
                var dialog = Dialog(T("Choose the game item to add", "Scegli il capo da aggiungere"), panel, 650);
                copy.Click += (sender, args) => { if (list.SelectedIndex >= 0) dialog.DialogResult = true; };
                list.MouseDoubleClick += (sender, args) => { if (list.SelectedIndex >= 0) dialog.DialogResult = true; };
                if (dialog.ShowDialog() != true) return;
                selected = items[list.SelectedIndex];
            }
            string command = AddItemCommand(selected);
            Clipboard.SetText(command);
            SetStatus(T("Copied · ", "Copiato · ") + command);
        }

        internal static string AddItemCommand(GameItem item) { return "player.additem " + item.ConsoleId + " 1"; }

        private async void RefreshInstalledMesh()
        {
            UpdateInstalledButtons();
            var choice = installedList.SelectedItem as InstalledChoice;
            var hint = Find<TextBlock>("CurrentHint");
            var source = Find<TextBlock>("CurrentSource");
            var project = Find<TextBlock>("CurrentProject");
            project.Text = "";
            source.Text = "";
            source.ToolTip = null;
            hint.Visibility = Visibility.Visible;
            hint.Text = catalog == null ? T("Scan the BodySlide library in Settings.", "Scansiona la libreria BodySlide dalle impostazioni.")
                : installed == null ? T("Checking the installed files…", "Controllo dei file installati…")
                : choice == null ? T("No mesh matches this search.", "Nessuna mesh corrisponde alla ricerca.")
                : T("Loading the installed mesh…", "Caricamento della mesh installata…");
            await installedViewport.RenderInstalled(installed, catalog, choice == null ? null : choice.Target, choice == null ? null : choice.Mesh,
                installedLow.IsChecked == true, texturedPreview.IsChecked == true, (mesh, provenance, count) => {
                    if (choice == null) return;
                    if (mesh == null) hint.Text = T("This mesh was not found in the deployed game files.", "Mesh non trovata nei file distribuiti del gioco.");
                    else
                    {
                        hint.Visibility = count > 0 ? Visibility.Collapsed : Visibility.Visible;
                        if (count == 0) hint.Text = T("Mesh found. This NIF could not be previewed.", "Mesh trovata. Anteprima NIF non disponibile.");
                        source.Text = mesh.Relative + "  ·  " + (mesh.Archive == null ? T("Loose file in Data", "File libero in Data") : Path.GetFileName(mesh.Archive));
                        source.ToolTip = mesh.Path ?? mesh.Archive;
                        project.Text = DescribeInstalledSource(provenance, mesh.Archive == null);
                    }
                });
        }

        private string DescribeInstalledSource(InstalledSource source, bool loose)
        {
            if (source == null) return "";
            var lines = new List<string>();
            if (source.Evidence == ProjectEvidence.BuildLog)
                lines.Add(T("BodySlide build (log) · ", "Build BodySlide (log) · ") + String.Join(", ", source.Projects));
            else if (source.Evidence == ProjectEvidence.Geometry)
                lines.Add(T("Compatible BodySlide project · ", "Progetto BodySlide compatibile · ") + String.Join(", ", source.Projects));
            else if (loose)
                lines.Add(T("BodySlide project unidentified", "Progetto BodySlide non identificato"));
            if (source.Mods.Length != 0)
                lines.Add((source.Mods.Length == 1 ? T("Project mod · ", "Mod del progetto · ") :
                    T("Matching project mods · ", "Mod con progetti corrispondenti · ")) + String.Join(", ", source.Mods));
            if (source.Components.Length != 0)
                lines.Add(T("Components · ", "Componenti · ") + String.Join(" · ", source.Components));
            if (source.DeployedBy != null)
                lines.Add(T("Deployed by Vortex from · ", "Mod distribuita da Vortex · ") + source.DeployedBy);
            return String.Join(Environment.NewLine, lines);
        }

        private sealed class InstalledChoice
        {
            internal Target Target;
            public string Name { get; set; }
            public string Mesh { get; set; }
            public string View { get; set; }
            public string ShortIds { get { return Target.ShortIds; } }
            public string ConsoleIds { get { return Target.ConsoleIds; } }
        }
    }
}
