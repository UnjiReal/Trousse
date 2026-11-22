using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SkyrimOutfit
{
    internal sealed partial class App
    {
        private CheckBox modestyEnabled, modestyTop, modestyBottom;
        private ComboBox modestyRaces;
        private ListBox kidOutfits, kidRecords;
        private bool loadingKid;

        private void ConfigureModesty()
        {
            modestyEnabled = Find<CheckBox>("EnableModesty");
            modestyTop = Find<CheckBox>("ModestyTop");
            modestyBottom = Find<CheckBox>("ModestyBottom");
            modestyRaces = Find<ComboBox>("ModestyRace");
            kidOutfits = Find<ListBox>("KidOutfits");
            kidRecords = Find<ListBox>("KidRecords");
            kidOutfits.SelectionChanged += (s, e) => { if (!loadingKid) LoadKidOutfit(); };
            kidRecords.SelectionChanged += (s, e) => SaveKidRule();
            foreach (var control in new[] { modestyEnabled, modestyTop, modestyBottom })
            {
                control.Checked += (s, e) => SaveKidRule();
                control.Unchecked += (s, e) => SaveKidRule();
            }
            modestyRaces.SelectionChanged += (s, e) => SaveKidRule();
            Bind("KidNav", () => { Switch("KidPage"); RefreshKid(); });
            Bind("ExportKid", ExportKid);
        }

        private void LocalizeModesty()
        {
            loadingKid = true;
            modestyRaces.ItemsSource = new[] { T("All races", "Tutte le razze") }.Concat(RaceLabels()).ToArray();
            System.Windows.Automation.AutomationProperties.SetName(modestyRaces, T("Wearer race", "Razza di chi lo indossa"));
            loadingKid = false;
            RefreshKid();
        }

        private void RefreshKid()
        {
            if (Find<FrameworkElement>("KidPage").Visibility != Visibility.Visible) return;
            var previous = kidOutfits.SelectedItem as Recipe;
            loadingKid = true;
            kidOutfits.ItemsSource = pack.Outfits.ToArray();
            kidOutfits.SelectedItem = pack.Outfits.Contains(previous) ? previous : pack.Outfits.FirstOrDefault();
            loadingKid = false;
            Find<TextBlock>("KidEmpty").Text = pack.Outfits.Count == 0
                ? T("Add an outfit to the package to create its rule.", "Aggiungi un outfit al pacchetto per configurarne la regola.")
                : catalog == null ? T("Scan the library to identify the game items.", "Scansiona la libreria per identificare i capi del gioco.") : "";
            LoadKidOutfit();
        }

        private void LoadKidOutfit()
        {
            loadingKid = true;
            var recipe = kidOutfits.SelectedItem as Recipe;
            string keyword = recipe == null ? null : recipe.Modesty;
            modestyEnabled.IsChecked = !String.IsNullOrEmpty(keyword);
            modestyTop.IsChecked = keyword == null || !ModestyRules.CoversTop(keyword);
            modestyBottom.IsChecked = keyword == null || !ModestyRules.CoversBottom(keyword);
            modestyRaces.SelectedIndex = recipe == null ? 0 : Array.IndexOf(ModestyRules.RaceForms, recipe.ModestyRace) + 1;
            Target target;
            var items = recipe == null || catalog == null || !catalog.ByTarget.TryGetValue(recipe.TargetKey, out target)
                ? new GameItem[0] : target.Items.Where(item => recipe.VariantIds == null || recipe.VariantIds.Count == 0 ||
                    recipe.VariantIds.Contains(item.RuleId, Files.Comparer)).OrderBy(item => item.Name(italian), Files.Comparer).ToArray();
            var entries = items.Select(item => new KidRecord { Item = item, Caption = item.Name(italian) + " · " + item.ConsoleId + " · " + item.EditorId }).ToArray();
            kidRecords.ItemsSource = entries;
            foreach (var record in entries)
                if (recipe.RuleTargets == null || recipe.RuleTargets.Contains(record.Item.RuleId, Files.Comparer)) kidRecords.SelectedItems.Add(record);
            loadingKid = false;
            RefreshKidHint();
            RefreshButtons();
        }

        private void UpdateModestyAvailability()
        {
            bool available = !busy && kidOutfits.SelectedItem != null && kidRecords.Items.Count != 0;
            kidOutfits.IsEnabled = !busy;
            modestyEnabled.IsEnabled = available;
            kidRecords.IsEnabled = modestyTop.IsEnabled = modestyBottom.IsEnabled = modestyRaces.IsEnabled =
                available && modestyEnabled.IsChecked == true;
        }

        private void SaveKidRule()
        {
            var recipe = kidOutfits.SelectedItem as Recipe;
            if (loadingKid || busy || recipe == null || kidRecords.Items.Count == 0) return;
            bool enabled = modestyEnabled.IsChecked == true;
            recipe.Modesty = enabled ? ModestyRules.Animation(modestyTop.IsChecked == true, modestyBottom.IsChecked == true) : null;
            int index = modestyRaces.SelectedIndex - 1;
            recipe.ModestyRace = enabled && index >= 0 ? ModestyRules.RaceForms[index] : null;
            recipe.RuleTargets = enabled ? kidRecords.SelectedItems.Cast<KidRecord>().Select(record => record.Item.RuleId).ToList() : null;
            projectDirty = true;
            RefreshKidHint();
            RefreshButtons();
        }

        private void RefreshKidHint()
        {
            var recipe = kidOutfits.SelectedItem as Recipe;
            string message = T("Select the zones the character should cover with an animation.",
                "Seleziona le zone che il personaggio deve coprire con l’animazione.");
            if (recipe != null && catalog != null && kidRecords.Items.Count == 0)
                message = T("This outfit has no matching game items.", "Nessun capo del gioco corrisponde a questo outfit.");
            else if (modestyEnabled.IsChecked == true && kidRecords.SelectedItems.Count == 0)
                message = T("Select at least one game item before exporting.", "Seleziona almeno un capo del gioco prima di esportare.");
            else if (modestyEnabled.IsChecked == true && modestyRaces.SelectedIndex > 0)
                message = T("This race uses the OAR patch included in the mod ZIP.", "Per questa razza viene usata la patch OAR inclusa nello ZIP della mod.");
            Find<TextBlock>("KidAnimationHint").Text = message;
        }

        private void ExportKid()
        {
            var rules = ModestyRules.Resolve(catalog, pack.Outfits.Where(outfit => String.IsNullOrEmpty(outfit.ModestyRace)));
            if (rules.Length == 0) return;
            var picker = new SaveFileDialog {
                Filter = "Keyword Item Distributor (*_KID.ini)|*_KID.ini", DefaultExt = ".ini", AddExtension = true,
                FileName = ModestyRules.KidName(packName.Text)
            };
            var folders = Files.Children(vortexPath.Text.Trim().Trim('"'), "Custom_modesty_KID*", true)
                .Where(folder => File.Exists(Path.Combine(folder, "Custom_modesty_KID.ini"))).Take(2).ToArray();
            if (folders.Length == 1) picker.InitialDirectory = folders[0];
            if (picker.ShowDialog(window) != true) return;
            if (!picker.FileName.EndsWith("_KID.ini", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(T("Give the file a name ending in _KID.ini.", "Usa un nome che termini in _KID.ini."));
            ModestyRules.WriteKid(picker.FileName, rules);
            SetStatus(T("KID saved · ", "KID salvato · ") + picker.FileName);
        }

        private sealed class KidRecord
        {
            internal GameItem Item;
            public string Caption { get; internal set; }
        }
    }
}
