using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SkyrimOutfit
{
    internal sealed partial class App
    {
        private const string MainOarIni = "OpenAnimationReplacer.ini";
        private OarSettings oar;
        private OarModule[] oarModules = new OarModule[0];
        private OarField[] oarFields = new OarField[0];
        private string oarData;
        private bool changingOar, indexedOar;

        private ComboBox OarSources { get { return Find<ComboBox>("OarSources"); } }
        private ComboBox OarSubmods { get { return Find<ComboBox>("OarSubmods"); } }
        private ComboBox OarSections { get { return Find<ComboBox>("OarSections"); } }
        private ComboBox OarRaces { get { return Find<ComboBox>("OarRaces"); } }
        private OarModule SelectedModule
        {
            get
            {
                var selection = OarSources.SelectedItem as OarSourceRow;
                return selection == null ? null : selection.Module;
            }
        }

        private void ConfigureOar()
        {
            Bind("OarNav", async () => {
                Switch("OarPage");
                await DiscoverOar(false);
            });
            Bind("OarOpen", OpenOarIni);
            Bind("OarReload", async () => { if (CanReplaceOar()) await DiscoverOar(true); });
            Bind("OarSave", SaveOar);
            OarSources.SelectionChanged += (sender, args) => { if (!changingOar) Handle(SelectOarSource); };
            OarSubmods.SelectionChanged += (sender, args) => { if (!changingOar) Handle(RefreshOarFields); };
            OarRaces.SelectionChanged += (sender, args) => { if (!changingOar) Handle(RefreshOarFields); };
            OarSections.SelectionChanged += (sender, args) => { if (!changingOar) FilterOarFields(); };
            Find<TextBox>("OarSearch").TextChanged += (sender, args) => FilterOarFields();
        }

        private async Task DiscoverOar(bool reload)
        {
            try
            {
                string data = SelectedDataPath();
                if (indexedOar && !reload && Files.Comparer.Equals(oarData, data)) return;
                if (indexedOar && !reload && !CanReplaceOar()) return;
                await RunBusy(T("Reading OAR configurations…", "Lettura configurazioni OAR…"), async () => {
                    string ini = oar != null && reload ? oar.Source : data == null ? null : Path.Combine(data, @"SKSE\Plugins", MainOarIni);
                    var modules = data == null ? new OarModule[0] : await Task.Run(() => OarModule.Scan(data));
                    var settings = ini == null ? oar : new OarSettings(ini, true);
                    oar = settings;
                    oarData = data;
                    oarModules = modules;
                    indexedOar = true;
                    LocalizeOar();
                    SetStatus(T("OAR configurations loaded", "Configurazioni OAR caricate"));
                });
            }
            catch (Exception error) { ShowError(error); }
        }

        private void LocalizeOar()
        {
            if (Find<FrameworkElement>("OarPage").Visibility != Visibility.Visible) return;
            var module = SelectedModule;
            var document = OarSubmods.SelectedItem as OarDocumentRow;
            changingOar = true;
            int race = Math.Max(0, OarRaces.SelectedIndex);
            OarRaces.ItemsSource = new[] { T("Default · all races", "Predefinito · tutte le razze") }.Concat(RaceLabels()).ToArray();
            OarRaces.SelectedIndex = race;
            var sources = new[] { new OarSourceRow { Label = T("General settings · OAR", "Impostazioni generali · OAR") } }
                .Concat(oarModules.Select(entry => new OarSourceRow { Label = entry.Name, Module = entry })).ToArray();
            OarSources.ItemsSource = sources;
            OarSources.SelectedItem = sources.FirstOrDefault(row => row.Module == module) ?? sources[0];
            changingOar = false;
            SelectOarSource(document == null ? null : document.Document);
        }

        private void SelectOarSource() { SelectOarSource(null); }

        private void SelectOarSource(OarDocument previous)
        {
            changingOar = true;
            try
            {
                var module = SelectedModule;
                if (module != null && module.Ini && module.Settings == null)
                    module.Settings = new OarSettings(module.Folder);
                bool json = module != null && !module.Ini;
                OarSubmods.Visibility = json ? Visibility.Visible : Visibility.Collapsed;
                OarRaces.Visibility = json ? Visibility.Visible : Visibility.Collapsed;
                var documents = json ? module.Open() : new OarDocument[0];
                var choices = documents.Select(document => new OarDocumentRow {
                    Document = document, Label = document.IsMod ? T("Mod presets", "Preset della mod") : document.Name
                }).ToList();
                bool hasSwitches = documents.Any(document => document.ToggleName != null);
                if (hasSwitches) choices.Insert(0, new OarDocumentRow { Label = T("Main switches", "Interruttori principali") });
                OarSubmods.ItemsSource = choices;
                OarSubmods.SelectedItem = choices.FirstOrDefault(row => row.Document == previous) ?? choices.FirstOrDefault();
                buttons["OarSave"].Content = json ? T("Export mod patch…", "Esporta patch mod…") : T("Save INI…", "Salva INI…");
            }
            catch
            {
                OarSubmods.ItemsSource = null;
                oarFields = new OarField[0];
                FilterOarFields();
                UpdateOarButtons();
                throw;
            }
            finally { changingOar = false; }
            RefreshOarFields();
        }

        private void RefreshOarFields()
        {
            var module = SelectedModule;
            var row = OarSubmods.SelectedItem as OarDocumentRow;
            var document = row == null ? null : row.Document;
            int race = OarRaces.SelectedIndex - 1;
            string file, detail;
            if (module == null || module.Ini)
            {
                OarSettings ini = module == null ? oar : module.Settings;
                oarFields = ini == null ? new OarField[0] : ini.Entries.Select(entry => OarOptions.IniField(entry, italian, module == null)).ToArray();
                file = ini == null ? T("Open an OAR INI or configure the Skyrim path.",
                    "Apri un INI di OAR o configura il percorso di Skyrim.") : ini.Source;
                detail = file;
            }
            else if (race >= 0)
            {
                var scoped = document == null ? module.Documents.Where(item => item.ToggleName != null)
                    : document.IsMod ? Enumerable.Empty<OarDocument>() : new[] { document };
                oarFields = scoped.Select(item => RaceField(item, ModestyRules.RaceForms[race])).ToArray();
                file = oarFields.Length == 0
                    ? T("Select a submod or Main switches to set activation for this race.",
                        "Seleziona un sottomod o Interruttori principali per regolare l’attivazione per questa razza.")
                    : T("Choose which features can run for this race, then export the mod patch.",
                        "Scegli quali funzioni attivare per questa razza, poi esporta la patch della mod.");
                detail = module.Folder;
            }
            else if (document == null)
            {
                oarFields = (module.Documents ?? new OarDocument[0]).Where(item => item.ToggleName != null)
                    .SelectMany(item => item.Fields(italian).Where(field => field.Kind == "Flag").Take(1)).ToArray();
                file = T("Turn features on or off, then export a mod patch.", "Attiva o disattiva le funzioni, poi esporta una patch.");
                detail = module.Folder;
            }
            else
            {
                oarFields = document.Fields(italian);
                file = document.Name + " · " + Path.GetFileName(document.FilePath);
                detail = document.FilePath + Environment.NewLine + document.Description;
            }
            if (module != null && module.Unreadable.Count > 0)
            {
                file += " · " + module.Unreadable.Count + T(" unreadable configurations", " configurazioni illeggibili");
                detail += Environment.NewLine + String.Join(Environment.NewLine, module.Unreadable);
            }
            var path = Find<TextBlock>("OarPath");
            path.Text = file;
            path.ToolTip = detail;
            changingOar = true;
            var sections = oarFields.Select(OarSectionName).Distinct(Files.Comparer).Select(key => new OarChoice(key, GroupLabel(key))).ToArray();
            OarSections.ItemsSource = new[] { new OarChoice("", T("All settings", "Tutte le impostazioni")) }.Concat(sections).ToArray();
            OarSections.SelectedIndex = 0;
            changingOar = false;
            FilterOarFields();
            UpdateOarButtons();
        }

        private OarField RaceField(OarDocument document, string race)
        {
            var options = new[] {
                new OarChoice("inherit", T("Use default", "Usa il predefinito")),
                new OarChoice("on", T("Enabled for this race", "Attivo per questa razza")),
                new OarChoice("off", T("Disabled for this race", "Disattivo per questa razza"))
            };
            var label = document.ToggleName == null ? document.Name : document.Fields(italian).First(field =>
                field.Hint.StartsWith("disabled · ", StringComparison.Ordinal)).Label;
            return new OarField(() => document.Race.Choice(race), value => document.Race.Set(race, value), "Choice", false, options) {
                Label = label, Context = T("Race activation", "Attivazione per razza"), Hint = document.Description
            };
        }

        private string GroupLabel(string key)
        {
            switch (key)
            {
                case "conditionPresets": return T("Condition presets", "Preset delle condizioni");
                case "conditions": return T("Conditions", "Condizioni");
                case "pairedConditions": return T("Paired conditions", "Condizioni in coppia");
                case "replacementAnimDatas": return T("Animations and variants", "Animazioni e varianti");
                default: return OarOptions.HumanName(key);
            }
        }

        private static string OarSectionName(OarField field)
        {
            int slash = field.Context.IndexOf(" / ", StringComparison.Ordinal);
            return slash < 0 ? field.Context : field.Context.Substring(0, slash);
        }

        private void FilterOarFields()
        {
            var section = OarSections.SelectedItem as OarChoice;
            string query = Find<TextBox>("OarSearch").Text;
            Find<ListBox>("OarEntries").ItemsSource = oarFields.Where(field =>
                (section == null || section.Value == "" || section.Value == OarSectionName(field))
                && Match(query, field.Label, field.Context, field.Hint)).ToArray();
        }

        private void UpdateOarButtons()
        {
            var module = SelectedModule;
            buttons["OarSave"].IsEnabled = !busy && (module == null ? oar != null :
                module.Ini ? module.Settings != null : module.Documents != null);
        }

        private IEnumerable<OarDocument> OpenOarDocuments()
        {
            return oarModules.Where(module => module.Documents != null).SelectMany(module => module.Documents);
        }

        private bool CanReplaceOar()
        {
            bool dirty = oar != null && oar.Entries.Any(entry => entry.IsChanged) ||
                oarModules.Any(module => module.Settings != null && module.Settings.Entries.Any(entry => entry.IsChanged)) ||
                OpenOarDocuments().Any(document => document.IsUnsaved);
            return !dirty || Confirm(T("Discard unsaved OAR changes?", "Scartare le modifiche OAR non salvate?"));
        }

        private void OpenOarIni()
        {
            if (oar != null && oar.Entries.Any(entry => entry.IsChanged) &&
                !Confirm(T("Discard unsaved INI changes?", "Scartare le modifiche non salvate nell’INI?"))) return;
            var dialog = new OpenFileDialog { FileName = MainOarIni, Filter = "OAR INI (*.ini)|*.ini" };
            if (dialog.ShowDialog(window) != true) return;
            oar = new OarSettings(dialog.FileName, Files.Comparer.Equals(Path.GetFileName(dialog.FileName), MainOarIni));
            bool alreadySelected = OarSources.SelectedIndex == 0;
            OarSources.SelectedIndex = 0;
            if (alreadySelected) RefreshOarFields();
        }

        private void SaveOar()
        {
            var module = SelectedModule;
            if (module != null && !module.Ini)
            {
                var documents = OpenOarDocuments().Where(document => document.IsChanged).ToArray();
                if (documents.Length == 0)
                {
                    SetStatus(T("Change an OAR mod setting before exporting.", "Modifica un’impostazione della mod OAR prima di esportare."));
                    return;
                }
                var archive = new SaveFileDialog { FileName = "Trousse-OAR-settings.zip", Filter = "Mod ZIP (*.zip)|*.zip", DefaultExt = ".zip" };
                if (archive.ShowDialog(window) != true) return;
                OarModule.Export(archive.FileName, oarData, oarModules);
                SetStatus(T("OAR patch saved. Install the ZIP with your mod manager.", "Patch OAR salvata. Installa lo ZIP con il gestore delle mod."));
                return;
            }
            var ini = module == null ? oar : module.Settings;
            if (ini == null) return;
            var picker = new SaveFileDialog {
                FileName = Path.GetFileName(ini.Source), DefaultExt = ".ini",
                Filter = "OAR INI (*.ini)|*.ini", InitialDirectory = Path.GetDirectoryName(ini.Source)
            };
            if (picker.ShowDialog(window) != true) return;
            ini.Save(picker.FileName);
            if (module == null) oar = new OarSettings(picker.FileName, Files.Comparer.Equals(Path.GetFileName(picker.FileName), MainOarIni));
            else module.Settings = new OarSettings(picker.FileName);
            RefreshOarFields();
            SetStatus(T("OAR settings saved. Restart Skyrim to apply them.", "Impostazioni OAR salvate. Riavvia Skyrim per applicarle."));
        }

        private sealed class OarSourceRow
        {
            internal OarModule Module;
            public string Label { get; internal set; }
        }

        private sealed class OarDocumentRow
        {
            internal OarDocument Document;
            public string Label { get; internal set; }
        }
    }
}
