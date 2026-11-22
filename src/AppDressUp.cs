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
        private readonly Dictionary<int, DressUpProfile> dressProfiles = new Dictionary<int, DressUpProfile>();
        private ListBox dressOutfits, dressItems, dressEquipment;
        private ComboBox dressSlots, dressCategory;
        private DressUpEquipment dressIndex;
        private DressUpProfile dressProfile;
        private DressUpOutfit dressOutfit;
        private DressItemRow[] dressLibrary = new DressItemRow[0];
        private string dressData;
        private int dressSlot = 1;
        private bool changingDress;

        private void ConfigureDressUp()
        {
            dressOutfits = Find<ListBox>("DressOutfits");
            dressItems = Find<ListBox>("DressItems");
            dressEquipment = Find<ListBox>("DressEquipment");
            dressSlots = Find<ComboBox>("DressProfileSlot");
            dressCategory = Find<ComboBox>("DressCategory");
            Bind("DressNav", async () => {
                Switch("DressPage");
                await LoadDressUp(false);
            });
            Bind("DressRefresh", async () => await LoadDressUp(true));
            Bind("DressGoCompose", () => Switch("HomePage"));
            Bind("DressComposePart", async () => await AddFromCompose(true));
            Bind("DressComposeTarget", async () => await AddFromCompose(false));
            Bind("DressNew", NewDressOutfit);
            Bind("DressRemoveOutfit", RemoveDressOutfit);
            Bind("DressAddItems", AddDressSelection);
            Bind("DressRemoveItems", RemoveDressItems);
            Bind("DressOpen", OpenDressProfile);
            Bind("DressExport", () => SaveDressProfile(false));
            Bind("DressSaveGame", () => SaveDressProfile(true));
            dressSlots.SelectionChanged += (s, e) => {
                if (changingDress || dressSlots.SelectedIndex < 0) return;
                Handle(() => {
                    try { SelectDressProfile(dressSlots.SelectedIndex + 1); }
                    finally
                    {
                        changingDress = true;
                        dressSlots.SelectedIndex = dressSlot - 1;
                        changingDress = false;
                    }
                });
            };
            dressOutfits.SelectionChanged += (s, e) => {
                if (changingDress) return;
                var row = dressOutfits.SelectedItem as DressOutfitRow;
                dressOutfit = row == null ? null : row.Outfit;
                RefreshDressOutfit();
            };
            dressItems.SelectionChanged += (s, e) => UpdateDressButtons();
            dressEquipment.SelectionChanged += (s, e) => UpdateDressButtons();
            dressEquipment.MouseDoubleClick += (s, e) => { if (IsListItem(e.OriginalSource)) Handle(AddDressSelection); };
            Find<TextBox>("DressName").TextChanged += (s, e) => {
                if (changingDress || dressOutfit == null) return;
                dressOutfit.Name = ((TextBox)s).Text;
                dressProfile.Dirty = true;
                var row = dressOutfits.SelectedItem as DressOutfitRow;
                if (row != null) row.Label = DressOutfitLabel(dressOutfit);
                UpdateDressButtons();
            };
            Find<TextBox>("DressProfileName").TextChanged += (s, e) => {
                if (changingDress || dressProfile == null) return;
                dressProfile.Name = ((TextBox)s).Text;
                dressProfile.Dirty = true;
            };
            Find<TextBox>("DressSearch").TextChanged += (s, e) => FilterDressEquipment();
            dressCategory.SelectionChanged += (s, e) => { if (!changingDress) FilterDressEquipment(); };
        }

        private void LocalizeDressUp()
        {
            changingDress = true;
            int selectedCategory = Math.Max(0, dressCategory.SelectedIndex);
            dressSlots.ItemsSource = Enumerable.Range(1, 5).Select(index => T("Profile ", "Profilo ") + index).ToArray();
            dressSlots.SelectedIndex = dressSlot - 1;
            dressCategory.ItemsSource = EquipmentCategoryLabels()
                .Concat(new[] { T("Weapons", "Armi") }).ToArray();
            dressCategory.SelectedIndex = selectedCategory;
            changingDress = false;
            RefreshDressLibrary();
            RefreshDressProfile();
        }

        private string DressDataPath()
        {
            if (catalog != null) return catalog.DataRoot;
            string body = bodyPath.Text.Trim().Trim('"');
            if (Preferences.HasBodySlide(body)) return Catalog.GameData(body);
            string game = Environment.GetEnvironmentVariable("TROUSSE_SKYRIM_PATH");
            return Directory.Exists(game) && Directory.Exists(Path.Combine(game, "Data")) ? Path.Combine(game, "Data") : null;
        }

        private async Task LoadDressUp(bool force)
        {
            try
            {
                string data = DressDataPath();
                if (!Files.Comparer.Equals(data, dressData))
                {
                    if (!CanLeaveDressProfiles()) return;
                    dressProfiles.Clear();
                    dressProfile = null;
                    dressOutfit = null;
                    dressIndex = null;
                    dressData = data;
                    RefreshDressLibrary();
                }
                if (dressProfile == null) SelectDressProfile(dressSlot);
                if (data == null || (!force && dressIndex != null)) return;
                await RunBusy(T("Reading Skyrim equipment records…", "Lettura dei record di equipaggiamento di Skyrim…"), async () => {
                    dressIndex = await Task.Run(() => GameRecords.ReadDressUpEquipment(data,
                        InstalledOutfits.ReadEnabled(InstalledOutfits.PluginListPath(data))));
                    RefreshDressLibrary();
                    RefreshDressOutfit();
                    SetStatus(T("Dress Up Lovers equipment loaded", "Equipaggiamento Dress Up Lovers caricato"));
                });
            }
            catch (Exception error) { ShowError(error); }
        }

        private bool CanLeaveDressProfiles()
        {
            return !dressProfiles.Values.Any(profile => profile.Dirty) ||
                Confirm(T("Discard unsaved Dress Up Lovers profile changes?", "Scartare le modifiche non salvate ai profili Dress Up Lovers?"));
        }

        private string DressGameFile(int slot)
        {
            return dressData == null ? null : Path.Combine(dressData, DressUpProfile.GameFolder, DressUpProfile.FileName(slot));
        }

        private void SelectDressProfile(int slot)
        {
            DressUpProfile profile;
            if (!dressProfiles.TryGetValue(slot, out profile))
            {
                string path = DressGameFile(slot);
                profile = File.Exists(path) ? DressUpProfile.Load(path) : new DressUpProfile();
                dressProfiles.Add(slot, profile);
            }
            dressSlot = slot;
            dressProfile = profile;
            dressOutfit = profile.Outfits.Values.FirstOrDefault();
            RefreshDressProfile();
        }

        private void RefreshDressProfile()
        {
            changingDress = true;
            dressSlots.SelectedIndex = dressSlot - 1;
            Find<TextBox>("DressProfileName").Text = dressProfile == null ? "" : dressProfile.Name;
            var rows = dressProfile == null ? new DressOutfitRow[0] : dressProfile.Outfits.Values.Select(outfit =>
                new DressOutfitRow { Outfit = outfit, Label = DressOutfitLabel(outfit) }).ToArray();
            dressOutfits.ItemsSource = rows;
            dressOutfits.SelectedItem = rows.FirstOrDefault(row => row.Outfit == dressOutfit);
            changingDress = false;
            RefreshDressOutfit();
        }

        private string DressOutfitLabel(DressUpOutfit outfit)
        {
            return (outfit.Slot + 1).ToString("D3") + " · " + (String.IsNullOrWhiteSpace(outfit.Name) ?
                T("Unnamed outfit", "Outfit senza nome") : outfit.Name.Trim());
        }

        private void RefreshDressOutfit()
        {
            changingDress = true;
            Find<TextBox>("DressName").Text = dressOutfit == null ? "" : dressOutfit.Name;
            dressItems.ItemsSource = dressOutfit == null ? null : dressOutfit.Items.Select(form =>
                DressRow(form, dressIndex == null ? null : dressIndex.Find(form))).ToArray();
            changingDress = false;
            RefreshDressNotices();
            UpdateDressButtons();
        }

        private void RefreshDressLibrary()
        {
            dressLibrary = dressIndex == null ? new DressItemRow[0] : dressIndex.Items.Values
                .Select(item => DressRow(dressIndex.Reference(item), item)).OrderBy(row => row.Label, Files.Comparer)
                .ThenBy(row => row.Reference, Files.Comparer).ToArray();
            FilterDressEquipment();
        }

        private void FilterDressEquipment()
        {
            string search = Find<TextBox>("DressSearch").Text;
            int category = dressCategory.SelectedIndex;
            var rows = dressLibrary.Where(row => (category <= 0 || row.Category == category) &&
                Match(search, row.Label, row.Reference, row.Item.EditorId, row.Item.ConsoleId)).ToArray();
            dressEquipment.ItemsSource = rows;
            Find<TextBlock>("DressResultCount").Text = rows.Length.ToString("N0");
            UpdateDressButtons();
        }

        private DressItemRow DressRow(DressUpForm form, GameItem item)
        {
            return new DressItemRow { Form = form, Item = item, Category = DressItemCategory(item),
                Label = item == null ? T("Unavailable equipment record", "Record di equipaggiamento non disponibile") : item.Name(italian) };
        }

        private static int DressItemCategory(GameItem item)
        {
            if (item == null) return (int)EquipmentCategory.Other;
            if (item.Weapon) return (int)EquipmentCategory.Other + 1;
            uint slots = item.Biped;
            if ((slots & (1U << 2)) != 0) return (int)EquipmentCategory.Body;
            if ((slots & ((1U << 7) | (1U << 8))) != 0) return (int)EquipmentCategory.Feet;
            if ((slots & ((1U << 3) | (1U << 4))) != 0) return (int)EquipmentCategory.Hands;
            if ((slots & (1U << 5)) != 0) return (int)EquipmentCategory.Amulets;
            if ((slots & (1U << 6)) != 0) return (int)EquipmentCategory.Rings;
            if ((slots & (1U << 9)) != 0) return (int)EquipmentCategory.Shields;
            return (slots & 0x3803) != 0 ? (int)EquipmentCategory.Head : (int)EquipmentCategory.Other;
        }

        private void RefreshDressNotices()
        {
            var info = Find<TextBlock>("DressStatus");
            info.Text = dressData == null ? T("Set up Skyrim in Settings to browse its installed equipment.",
                "Configura Skyrim nelle Impostazioni per sfogliare l’equipaggiamento installato.") :
                dressIndex == null ? T("Scan the installed equipment to find Skyrim items.", "Scansiona l’equipaggiamento per trovare i capi di Skyrim.") :
                !dressIndex.ModDeployed ? T("Dress Up Lovers is missing from Skyrim Data.", "Dress Up Lovers non è presente nella cartella Data di Skyrim.") :
                !dressIndex.LoadOrderVerified ? T("Using deployed plugins. The active load order could not be verified.",
                    "Sono mostrati i plugin distribuiti. Non è stato possibile verificare l’ordine di caricamento attivo.") :
                !dressIndex.ModEnabled ? T("Enable Dress Up Lovers in Vortex before importing the profile.",
                    "Attiva Dress Up Lovers in Vortex prima di importare il profilo.") :
                T("Dress Up Lovers detected", "Dress Up Lovers rilevato");
            info.ToolTip = dressIndex == null || dressIndex.Notices.Count == 0 ? null : String.Join(Environment.NewLine, dressIndex.Notices.Take(10));
            Find<TextBlock>("DressImportHint").Text = DressUpProfile.FileName(dressSlot) + " · " +
                T("In Skyrim, open Dress Up Lovers → Backup Custom Outfits → Import profile. Import replaces the current custom outfits.",
                    "In Skyrim, apri Dress Up Lovers → Backup Custom Outfits → Import profile. L’importazione sostituisce gli outfit personalizzati attuali.");
            var notice = Find<TextBlock>("DressOutfitNotice");
            var rows = dressItems.Items.Cast<DressItemRow>().ToArray();
            var warnings = new List<string>();
            if (rows.Any(row => row.Item == null) && dressIndex != null)
                warnings.Add(T("Check the plugins for unavailable items before importing.", "Controlla i plugin dei pezzi non disponibili prima dell’importazione."));
            if (rows.Any(row => row.Item != null && row.Item.Weapon))
                warnings.Add(T("The JSON includes weapons. The game editor may remove them when you edit the outfit.",
                    "Il JSON include le armi. L’editor in gioco può rimuoverle quando modifichi l’outfit."));
            uint slots = 0;
            foreach (var row in rows.Where(row => row.Item != null && !row.Item.Weapon))
            {
                if ((slots & row.Item.Biped) != 0)
                {
                    warnings.Add(T("Some pieces share equipment slots.", "Alcuni pezzi occupano gli stessi slot."));
                    break;
                }
                slots |= row.Item.Biped;
            }
            notice.Text = String.Join(Environment.NewLine, warnings);
        }

        private void UpdateDressButtons()
        {
            if (dressEquipment == null || !buttons.ContainsKey("DressSaveGame")) return;
            bool hasProfile = !busy && dressProfile != null;
            bool canSave = hasProfile && dressProfile.CanExport;
            buttons["DressComposePart"].IsEnabled = !busy && catalog != null && sourceList.SelectedItems.Count != 0;
            var target = targetList.SelectedItem as Target;
            buttons["DressComposeTarget"].IsEnabled = !busy && target != null && target.Items.Count != 0;
            buttons["DressNew"].IsEnabled = hasProfile && dressProfile.Outfits.Count < DressUpProfile.MaxOutfits;
            buttons["DressRemoveOutfit"].IsEnabled = hasProfile && dressOutfit != null;
            buttons["DressExport"].IsEnabled = canSave;
            buttons["DressSaveGame"].IsEnabled = canSave && dressIndex != null && dressIndex.ModDeployed;
            buttons["DressAddItems"].IsEnabled = hasProfile && dressEquipment.SelectedItems.Count > 0;
            buttons["DressRemoveItems"].IsEnabled = hasProfile && dressItems.SelectedItems.Count > 0;
            dressEquipment.IsEnabled = !busy;
            dressOutfits.IsEnabled = !busy;
            dressSlots.IsEnabled = !busy;
            dressItems.IsEnabled = !busy;
            Find<TextBox>("DressName").IsEnabled = hasProfile && dressOutfit != null;
            Find<TextBox>("DressProfileName").IsEnabled = hasProfile;
        }

        private void NewDressOutfit()
        {
            dressOutfit = dressProfile.Add(T("New outfit", "Nuovo outfit"));
            RefreshDressProfile();
            var name = Find<TextBox>("DressName");
            name.Focus();
            name.SelectAll();
        }

        private void RemoveDressOutfit()
        {
            if (dressOutfit == null || !Confirm(T("Remove this outfit from the profile?", "Rimuovere questo outfit dal profilo?"))) return;
            dressProfile.Outfits.Remove(dressOutfit.Slot);
            dressProfile.Dirty = true;
            dressOutfit = dressProfile.Outfits.Values.FirstOrDefault();
            RefreshDressProfile();
        }

        private void AddDressSelection()
        {
            var rows = dressEquipment.SelectedItems.Cast<DressItemRow>().ToArray();
            if (rows.Length != 0) AddDressForms(rows.Select(row => row.Form), rows[0].Label);
        }

        private void AddDressForms(IEnumerable<DressUpForm> forms, string suggestedName)
        {
            if (dressOutfit == null) dressOutfit = dressProfile.Add(suggestedName);
            int count = 0;
            foreach (var form in forms)
            {
                if (dressOutfit.Items.Any(item => Files.Comparer.Equals(item.Key, form.Key))) continue;
                dressOutfit.Items.Add(form);
                count++;
            }
            dressProfile.Dirty |= count > 0;
            RefreshDressProfile();
            SetStatus(count + T(" items added to the Dress Up Lovers outfit", " pezzi aggiunti all’outfit Dress Up Lovers"));
        }

        private void RemoveDressItems()
        {
            if (dressOutfit == null) return;
            foreach (var row in dressItems.SelectedItems.Cast<DressItemRow>().ToArray()) dressOutfit.Items.Remove(row.Form);
            dressProfile.Dirty = true;
            RefreshDressOutfit();
        }

        private async Task AddFromCompose(bool fromBodySlide)
        {
            var parts = sourceList.SelectedItems.Cast<Piece>().ToArray();
            var target = targetList.SelectedItem as Target;
            if (fromBodySlide ? parts.Length == 0 : target == null) return;
            await LoadDressUp(false);
            try
            {
                if (dressIndex == null || dressProfile == null || !Files.Comparer.Equals(DressDataPath(), dressData)) return;
                var selected = new List<GameItem>();
                var missing = new List<string>();
                if (fromBodySlide)
                {
                    foreach (var part in parts)
                        SelectDressRecords(part.Name, dressIndex.ForMesh(part.Output), selected, missing);
                }
                else
                {
                    var variants = target == destination ? variantTargets : null;
                    var items = target.Items.Where(item => variants == null || variants.Count == 0 || variants.Contains(item.RuleId, Files.Comparer))
                        .Select(item => dressIndex.Find(new DressUpForm(item.Plugin, item.Form, false))).Where(item => item != null);
                    SelectDressRecords(target.DisplayName(italian), items, selected, missing);
                }
                if (missing.Count > 0) ShowDetails(T("Dress Up Lovers equipment", "Equipaggiamento Dress Up Lovers"),
                    T("These selections have no matching installed equipment record. You can search for their in-game names in the Dress Up Lovers tab.",
                        "Queste selezioni non corrispondono a record di equipaggiamento installati. Puoi cercare il loro nome in gioco nella scheda Dress Up Lovers.") +
                    Environment.NewLine + Environment.NewLine + String.Join(Environment.NewLine, missing));
                if (selected.Count == 0) return;
                Switch("DressPage");
                AddDressForms(selected.Select(dressIndex.Reference), selected[0].Name(italian));
            }
            catch (Exception error) { ShowError(error); }
        }

        private void SelectDressRecords(string label, IEnumerable<GameItem> candidates, List<GameItem> chosen, List<string> missing)
        {
            var items = candidates.Distinct().OrderBy(item => item.Name(italian), Files.Comparer)
                .ThenBy(item => item.Plugin, Files.Comparer).ThenBy(item => item.Form).ToArray();
            if (items.Length == 0) { missing.Add(label); return; }
            if (items.Length == 1) { chosen.Add(items[0]); return; }
            var list = new ListBox { MaxHeight = 340, SelectionMode = SelectionMode.Extended,
                Style = (Style)window.FindResource(typeof(ListBox)), ItemContainerStyle = (Style)window.FindResource(typeof(ListBoxItem)),
                ItemsSource = items.Select(item => item.Name(italian) + " · " + item.Plugin + " · " + dressIndex.Reference(item).LocalId).ToArray() };
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            panel.Children.Add(list);
            var add = new Button { Content = T("Add selected equipment", "Aggiungi equipaggiamento selezionato"), IsDefault = true,
                IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0),
                Style = (Style)window.FindResource("Accent") };
            panel.Children.Add(add);
            var dialog = Dialog(T("Choose equipment for Dress Up Lovers", "Scegli l’equipaggiamento per Dress Up Lovers"), panel, 690);
            list.SelectionChanged += (s, e) => add.IsEnabled = list.SelectedItems.Count != 0;
            add.Click += (s, e) => dialog.DialogResult = true;
            if (dialog.ShowDialog() == true)
                chosen.AddRange(list.SelectedItems.Cast<string>().Select(row => items[list.Items.IndexOf(row)]));
        }

        private void OpenDressProfile()
        {
            var picker = new OpenFileDialog { Filter = "Dress Up Lovers JSON (*.json)|*.json", FileName = DressUpProfile.FileName(dressSlot) };
            string game = DressGameFile(dressSlot);
            if (game != null && Directory.Exists(Path.GetDirectoryName(game))) picker.InitialDirectory = Path.GetDirectoryName(game);
            if (picker.ShowDialog(window) != true) return;
            int slot = Enumerable.Range(1, 5).FirstOrDefault(index => Files.Comparer.Equals(Path.GetFileName(picker.FileName), DressUpProfile.FileName(index)));
            if (slot == 0) slot = dressSlot;
            DressUpProfile previous;
            if (dressProfiles.TryGetValue(slot, out previous) && previous.Dirty &&
                !Confirm(T("Replace the unsaved edits in this profile slot?", "Sostituire le modifiche non salvate in questo profilo?"))) return;
            dressProfiles[slot] = DressUpProfile.Load(picker.FileName);
            SelectDressProfile(slot);
            SetStatus(T("Dress Up Lovers profile opened", "Profilo Dress Up Lovers aperto"));
        }

        private void SaveDressProfile(bool toGame)
        {
            if (dressProfile == null || !dressProfile.CanExport) return;
            if (dressProfile.Outfits.Values.Any(outfit => outfit.Items.Count != 0 && String.IsNullOrWhiteSpace(outfit.Name)))
                throw new InvalidOperationException(T("Name each outfit that contains equipment.", "Dai un nome a ogni outfit che contiene equipaggiamento."));
            string path = DressGameFile(dressSlot);
            if (toGame)
            {
                if (path == null || !Files.Comparer.Equals(DressDataPath(), dressData) ||
                    !File.Exists(Path.Combine(dressData, "Dress Up Lovers.esp")))
                    throw new InvalidOperationException(T("Deploy Dress Up Lovers and refresh the equipment list.", "Distribuisci Dress Up Lovers e aggiorna l’elenco dell’equipaggiamento."));
                if (File.Exists(path) && !Confirm(T("Replace the selected Dress Up Lovers profile file? Trousse will keep a backup.",
                    "Sostituire il file del profilo Dress Up Lovers selezionato? Trousse conserverà una copia di sicurezza."))) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
            }
            else
            {
                var picker = new SaveFileDialog { Filter = "Dress Up Lovers JSON (*.json)|*.json",
                    FileName = DressUpProfile.FileName(dressSlot), DefaultExt = ".json" };
                if (picker.ShowDialog(window) != true) return;
                path = picker.FileName;
            }
            bool inGameFolder = dressData != null && Files.Comparer.Equals(Path.GetDirectoryName(path),
                Path.Combine(dressData, DressUpProfile.GameFolder));
            dressProfile.Save(path, toGame || inGameFolder);
            SetStatus(T("Saved · ", "Salvato · ") + path);
        }

        private sealed class DressItemRow
        {
            internal DressUpForm Form;
            internal GameItem Item;
            internal int Category;
            public string Label { get; set; }
            public string Reference { get { return Form.Plugin + " · " + Form.LocalId; } }
        }

        private sealed class DressOutfitRow : System.ComponentModel.INotifyPropertyChanged
        {
            internal DressUpOutfit Outfit;
            private string label;
            public string Label
            {
                get { return label; }
                set {
                    label = value;
                    var changed = PropertyChanged;
                    if (changed != null) changed(this, new System.ComponentModel.PropertyChangedEventArgs("Label"));
                }
            }
            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        }
    }
}
