using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace SkyrimOutfit
{
    internal sealed partial class App
    {
        private readonly Window window;
        private readonly Dictionary<string, Button> buttons = new Dictionary<string, Button>();
        private readonly TextBox vortexPath, bodyPath, pieceSearch, targetSearch, outfitName, packName;
        private readonly TextBlock meshPath, targetInfo, status, liveHint;
        private readonly ListBox modList, sourceList, targetList, draftList, packList;
        private readonly CheckBox autoScan, femaleOnly;
        private readonly ComboBox language, targetCategory;
        private readonly CheckBox texturedPreview;
        private readonly LiveViewport viewport;
        private readonly Preferences settings;
        private Catalog catalog;
        private Pack pack = new Pack { Name = "My replacers" };
        private readonly List<DraftPiece> draft = new List<DraftPiece>();
        private ModGroup[] modGroups = new ModGroup[0];
        private Target destination;
        private List<string> variantTargets;
        private int editing = -1;
        private bool updating, draftDirty, projectDirty, busy, italian, inspecting;

        [STAThread]
        public static void Main()
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            new App().Launch(application);
        }

        internal App()
        {
            using (var layout = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))
                window = (Window)XamlReader.Load(layout);
            window.Width = Math.Min(window.Width, SystemParameters.WorkArea.Width);
            window.Height = Math.Min(window.Height, SystemParameters.WorkArea.Height);
            using (var icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("TrousseIcon.png"))
            {
                if (icon != null)
                {
                    window.Icon = ImageFromStream(icon);
                    Find<Image>("BrandIcon").Source = window.Icon;
                }
            }
            vortexPath = Find<TextBox>("VortexPath");
            bodyPath = Find<TextBox>("BodyPath");
            pieceSearch = Find<TextBox>("PieceSearch");
            targetSearch = Find<TextBox>("TargetSearch");
            outfitName = Find<TextBox>("OutfitName");
            packName = Find<TextBox>("PackName");
            meshPath = Find<TextBlock>("MeshPath");
            targetInfo = Find<TextBlock>("TargetInfo");
            status = Find<TextBlock>("Status");
            liveHint = Find<TextBlock>("LiveHint");
            modList = Find<ListBox>("ModList");
            sourceList = Find<ListBox>("SourceList");
            targetList = Find<ListBox>("TargetList");
            draftList = Find<ListBox>("DraftList");
            packList = Find<ListBox>("PackList");
            autoScan = Find<CheckBox>("AutoScan");
            femaleOnly = Find<CheckBox>("FemaleOnly");
            language = Find<ComboBox>("Language");
            targetCategory = Find<ComboBox>("TargetCategory");
            texturedPreview = Find<CheckBox>("TexturedPreview");
            viewport = new LiveViewport(Find<Viewport3D>("Viewport"), Find<Border>("PreviewSurface"));
            settings = Preferences.Load();
            vortexPath.Text = settings.Staging;
            bodyPath.Text = settings.Body;
            language.SelectedIndex = settings.Italian ? 1 : 0;
            autoScan.IsChecked = settings.AutoScan;
            femaleOnly.IsChecked = settings.Female;
            texturedPreview.IsChecked = settings.TexturedPreview;

            Bind("HomeNav", () => Switch("HomePage"));
            Bind("PackNav", () => Switch("PackPage"));
            Bind("SettingsNav", () => Switch("SettingsPage"));
            Bind("GuideNav", () => Switch("GuidePage"));
            Bind("BrowseVortex", () => Browse(vortexPath));
            Bind("BrowseBody", () => Browse(bodyPath));
            Bind("Scan", async () => await Scan());
            Bind("AddPieces", AddPieces);
            Bind("InspectPiece", () => { inspecting = true; RefreshLive(); });
            Bind("ShowOutfitView", () => { inspecting = false; RefreshLive(); });
            Bind("Variants", ShowVariants);
            Bind("NewOutfit", () => { if (CanLeaveDraft()) LoadDraft(-1); });
            Bind("Up", () => MovePiece(-1));
            Bind("Down", () => MovePiece(1));
            Bind("RemovePiece", RemovePiece);
            Bind("Commit", Commit);
            Bind("Preview", async () => await Preview());
            Bind("EditOutfit", EditSelected);
            Bind("RemoveOutfit", RemoveOutfit);
            Bind("Export", async () => await Export());
            Bind("ExportOdt", async () => await ExportOutfitReport());
            Bind("OpenPack", OpenPack);
            Bind("SavePack", SavePack);
            Bind("Notices", ShowNotices);
            ConfigureAudio();
            ConfigureInstalledPage();
            ConfigureModesty();
            ConfigureToolsPages();
            ConfigurePoses();
            ConfigureDressUp();
            ConfigureModManager();

            vortexPath.TextChanged += PathsChanged;
            bodyPath.TextChanged += PathsChanged;
            language.SelectionChanged += (sender, args) => ApplyLanguage();
            femaleOnly.Checked += (sender, args) => RefreshTargets();
            femaleOnly.Unchecked += (sender, args) => RefreshTargets();
            pieceSearch.TextChanged += (sender, args) => RefreshMods();
            targetSearch.TextChanged += (sender, args) => RefreshTargets();
            targetCategory.SelectionChanged += (sender, args) => { if (!updating) RefreshTargets(); };
            texturedPreview.Checked += (sender, args) => PreviewModeChanged();
            texturedPreview.Unchecked += (sender, args) => PreviewModeChanged();
            modList.SelectionChanged += (sender, args) => RefreshPieces();
            sourceList.SelectionChanged += (sender, args) => { RefreshButtons(); if (inspecting || draft.Count == 0) RefreshLive(); };
            draftList.SelectionChanged += (sender, args) => RefreshButtons();
            packList.SelectionChanged += (sender, args) => RefreshButtons();
            targetList.SelectionChanged += (sender, args) => { PickTarget(); UpdateDressButtons(); };
            sourceList.MouseDoubleClick += (sender, args) => { if (IsListItem(args.OriginalSource)) Handle(AddPieces); };
            packList.MouseDoubleClick += (sender, args) => { if (IsListItem(args.OriginalSource)) Handle(EditSelected); };
            outfitName.TextChanged += (sender, args) => MarkDraft();
            packName.TextChanged += (sender, args) => { if (!updating) projectDirty = true; };
            window.Closing += (sender, args) => {
                if (busy || !CanReplaceOar() || !CanReloadPoses() || !CanLeaveDressProfiles() || !CanLeaveProject()) args.Cancel = true;
                else SavePreferences();
            };
            var badgeEdition = Find<ComboBox>("BadgeEdition");
            badgeEdition.SelectionChanged += (sender, args) => {
                if (updating) return;
                settings.BadgeEdition = badgeEdition.SelectedIndex == 2 ? "anniversary" : badgeEdition.SelectedIndex == 1 ? "special" : null;
                RefreshGameBadges();
                if (window.IsLoaded) SavePreferences();
            };
            ApplyLanguage();
        }

        private string T(string english, string localized) { return italian ? localized : english; }
        private TElement Find<TElement>(string name) where TElement : class { return (TElement)window.FindName(name); }

        private void Bind(string name, Action action)
        {
            var button = Find<Button>(name);
            buttons.Add(name, button);
            button.Click += (sender, args) => Handle(action);
        }

        private void Handle(Action action)
        {
            if (busy) return;
            try { action(); }
            catch (Exception error) { ShowError(error); }
        }

        private void SetStatus(string message) { status.Text = message; }
        private void BackgroundStatus(string message)
        {
            window.Dispatcher.BeginInvoke(new Action(() => SetStatus(TranslateStatus(message))));
        }

        private void Switch(string page)
        {
            if (page != "BodiesPage") CancelBodyPreview();
            if (page != "PosesPage") StopPosePreview();
            foreach (string name in new[] { "HomePage", "CurrentPage", "BodiesPage", "OarPage", "PackPage", "KidPage", "PosesPage", "DressPage", "SettingsPage", "GuidePage" })
                Find<FrameworkElement>(name).Visibility = name == page ? Visibility.Visible : Visibility.Collapsed;
            var links = new[] { "Home", "Current", "Bodies", "Oar", "Pack", "Kid", "Poses", "Dress", "Settings", "Guide" };
            foreach (string link in links) buttons[link + "Nav"].FontWeight = page == link + "Page" ? FontWeights.Bold : FontWeights.Normal;
        }

        private static void Browse(TextBox field)
        {
            using (var picker = new Forms.FolderBrowserDialog { ShowNewFolderButton = false })
            {
                if (Directory.Exists(field.Text)) picker.SelectedPath = field.Text;
                if (picker.ShowDialog() == Forms.DialogResult.OK) field.Text = picker.SelectedPath;
            }
        }

        private void PathsChanged(object sender, TextChangedEventArgs args)
        {
            InvalidatePaths();
        }

        private void InvalidatePaths()
        {
            catalog = null;
            installed = null;
            installedCheck++;
            RefreshGameBadges();
            modGroups = new ModGroup[0];
            RefreshMods();
            RefreshTargets();
            RefreshBodies();
            RefreshKid();
            RefreshLive();
            SetStatus(T("Paths changed. Scan the library to continue.", "Percorsi modificati. Scansiona la libreria per continuare."));
        }

        private async Task Scan()
        {
            string staging = vortexPath.Text.Trim().Trim('"'), body = bodyPath.Text.Trim().Trim('"');
            string mo2Ini = UseModOrganizer ? Find<TextBox>("Mo2Ini").Text.Trim().Trim('"') : null;
            await RunBusy(T("Scanning BodySlide projects and official Skyrim archives…", "Scansione di BodySlide e degli archivi ufficiali…"), async () => {
                if (mo2Ini != null && !File.Exists(mo2Ini))
                    throw new FileNotFoundException(T("Select the instance’s ModOrganizer.ini in Settings.",
                        "Seleziona il file ModOrganizer.ini dell’istanza nelle impostazioni."));
                if (mo2Ini != null && !ModOrganizerProfile.IsVirtualized())
                    throw new InvalidOperationException(T("Launch Trousse with Run in Mod Organizer 2 to see the profile’s virtual Data folder.",
                        "Avvia Trousse con Esegui in Mod Organizer 2 per accedere alla cartella Data virtuale del profilo."));
                var result = await Task.Run(() => Catalog.Scan(staging, body, mo2Ini == null ? null : ModOrganizerProfile.Read(mo2Ini)));
                catalog = result;
                RefreshGameBadges();
                ResetPoseRig();
                installed = null;
                installedCheck++;
                modGroups = ModGroup.Group(result.Pieces);
                RefreshMods();
                string key = destination != null ? destination.Key : editing >= 0 ? pack.Outfits[editing].TargetKey : null;
                if (key != null)
                {
                    Target found;
                    destination = catalog.ByTarget.TryGetValue(key, out found) ? found : null;
                }
                RefreshTargets();
                RefreshBodies();
                RefreshKid();
                RefreshDraft(draftList.SelectedIndex);
                RefreshTargetInfo();
                RefreshLive();
                SavePreferences();
                SetStatus((result.Organizer == null ? "" : "MO2 · " + result.Organizer.Name + " · ") +
                    modGroups.Length + T(" mods · ", " mod · ") + result.Pieces.Count + T(" BodySlide parts · ", " pezzi BodySlide · ") +
                    result.Targets.Count + T(" official meshes", " mesh ufficiali"));
            });
        }

        private async Task RunBusy(string message, Func<Task> action)
        {
            if (busy) return;
            busy = true;
            RefreshButtons();
            SetStatus(message);
            Exception failure = null;
            try { await action(); }
            catch (Exception error) { failure = error; }
            finally { busy = false; RefreshButtons(); }
            if (failure != null) ShowError(failure);
        }

        private void RefreshMods()
        {
            string previous = (modList.SelectedItem as ModGroup ?? ModGroup.Empty).Key;
            ModGroup[] shown = modGroups.Where(group => Match(pieceSearch.Text, group.Name) ||
                group.Parts.Any(piece => Match(pieceSearch.Text, group.Name, piece.Name))).ToArray();
            modList.ItemsSource = shown;
            modList.SelectedItem = shown.FirstOrDefault(group => group.Key == previous) ?? shown.FirstOrDefault();
            RefreshPieces();
        }

        private void RefreshPieces()
        {
            var group = modList.SelectedItem as ModGroup;
            Piece[] parts = group == null ? new Piece[0] :
                group.Parts.Where(piece => Match(pieceSearch.Text, group.Name, piece.Name, piece.Output)).ToArray();
            sourceList.ItemsSource = parts;
            Find<TextBlock>("PieceCount").Text = parts.Length == 0 ? "" : parts.Length.ToString("N0");
        }

        private void RefreshTargets()
        {
            updating = true;
            var category = (EquipmentCategory)Math.Max(0, targetCategory.SelectedIndex);
            var targets = catalog == null ? new Target[0] : catalog.Targets.Where(target =>
                (femaleOnly.IsChecked != true || !target.ViewKind.StartsWith("Male", StringComparison.Ordinal)) &&
                (category == EquipmentCategory.All || category == target.Category) &&
                Match(targetSearch.Text, target.Output, target.AddonName, target.DisplayName(italian),
                    String.Join(" ", target.Items.Select(item => item.Name(italian) + " " + item.EditorId + " " + item.ConsoleId +
                        " " + String.Join(" ", item.Outfits.Concat(item.TextureVariants.Values.SelectMany(names => names)))))))
                .OrderBy(target => target.Items.Count == 0).ThenBy(target => target.DisplayName(italian), Files.Comparer)
                .ThenBy(target => target.ViewKind, Files.Comparer).ToArray();
            foreach (Target target in targets)
            {
                target.Name = target.DisplayName(italian);
                target.View = EquipmentView(target.ViewKind);
            }
            targetList.ItemsSource = targets;
            targetList.SelectedItem = destination;
            updating = false;
            Find<TextBlock>("TargetCount").Text = targets.Length == 0 ? "" : targets.Length.ToString("N0");
        }

        private static bool Match(string query, params string[] values)
        {
            return query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).All(token =>
                values.Any(value => value != null && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private void RefreshButtons()
        {
            foreach (var button in buttons.Values) button.IsEnabled = !busy;
            foreach (var field in new Control[] { vortexPath, bodyPath, pieceSearch, targetSearch, outfitName, packName,
                modList, sourceList, targetList, draftList, packList, language, targetCategory, autoScan, femaleOnly,
                Find<ComboBox>("ModManager"), Find<TextBox>("Mo2Ini") })
                field.IsEnabled = !busy;
            UpdateModestyAvailability();
            UpdateInstalledButtons();
            UpdatePoseButtons();
            UpdateToolsButtons();
            UpdateDressButtons();
            if (busy) return;
            buttons["AddPieces"].IsEnabled = sourceList.SelectedItems.Count != 0;
            buttons["InspectPiece"].IsEnabled = sourceList.SelectedItem != null;
            buttons["ShowOutfitView"].IsEnabled = inspecting;
            buttons["Commit"].IsEnabled = destination != null && draft.Count != 0 && (editing < 0 || draftDirty);
            buttons["Preview"].IsEnabled = catalog != null && destination != null && draft.Count != 0;
            buttons["Variants"].IsEnabled = destination != null && destination.Items.Count > 0;
            buttons["EditOutfit"].IsEnabled = packList.SelectedIndex >= 0;
            buttons["RemoveOutfit"].IsEnabled = packList.SelectedIndex >= 0;
            buttons["RemovePiece"].IsEnabled = draftList.SelectedIndex >= 0;
            buttons["Up"].IsEnabled = draftList.SelectedIndex > 0;
            buttons["Down"].IsEnabled = draftList.SelectedIndex >= 0 && draftList.SelectedIndex < draft.Count - 1;
            buttons["Export"].IsEnabled = catalog != null && (pack.Outfits.Count > 0 || (destination != null && draft.Count > 0));
            buttons["ExportOdt"].IsEnabled = buttons["Export"].IsEnabled;
            buttons["ExportKid"].IsEnabled = catalog != null && pack.Outfits.Any(recipe =>
                !String.IsNullOrEmpty(recipe.Modesty) && String.IsNullOrEmpty(recipe.ModestyRace));
            buttons["Notices"].IsEnabled = catalog != null && catalog.Notices.Count != 0;
            buttons["Commit"].Content = editing < 0 ? T("Add to package", "Aggiungi al pacchetto") : T("Save changes", "Salva modifiche");
        }

        private void PickTarget()
        {
            var target = targetList.SelectedItem as Target;
            if (target == null || target == destination || updating || busy) return;
            if (String.IsNullOrWhiteSpace(outfitName.Text) || (destination != null && outfitName.Text == destination.DisplayName(italian)))
                outfitName.Text = target.DisplayName(italian);
            destination = target;
            variantTargets = null;
            MarkDraft();
            RefreshTargetInfo();
        }

        private void RefreshTargetInfo()
        {
            meshPath.Text = destination == null ? T("Choose game equipment", "Scegli un capo del gioco") : destination.DisplayName(italian);
            if (destination != null && variantTargets != null && variantTargets.Count > 0)
                meshPath.Text += " · " + String.Join(", ", destination.Items.Where(item => variantTargets.Contains(item.RuleId, Files.Comparer))
                    .Select(item => item.ConsoleId));
            meshPath.ToolTip = destination == null ? null : destination.Mesh;
            targetInfo.Text = destination == null ? "" : destination.Items.Count +
                T(" game records", " record del gioco") + (destination.HasSwap ? T(" · texture variants", " · varianti texture") : "") +
                (destination.Items.Count == 0 ? "" : " · " + destination.ShortIds);
            targetInfo.ToolTip = destination == null ? null : destination.ConsoleIds;
        }

        private void AddPieces()
        {
            AddDraftParts(sourceList.SelectedItems.Cast<Piece>());
        }

        private void AddDraftParts(IEnumerable<Piece> pieces)
        {
            foreach (Piece piece in pieces)
                if (!draft.Any(row => Files.Comparer.Equals(row.Id, piece.Id))) draft.Add(new DraftPiece { Id = piece.Id });
            inspecting = false;
            MarkDraft();
            RefreshDraft(draft.Count - 1);
            RefreshLive();
        }

        private void RefreshDraft(int index)
        {
            var error = CurrentPackageConflict();
            foreach (var row in draft)
            {
                row.BuildError = error != null && error.PieceIds.Contains(row.Id) ? error.Message : null;
                Piece piece;
                if (catalog != null && catalog.ById.TryGetValue(row.Id, out piece))
                {
                    row.Name = piece.ShortName;
                    row.Detail = piece.ModName;
                }
                else
                {
                    row.Name = T("Part unavailable", "Componente non disponibile");
                    row.Detail = row.Id;
                }
            }
            draftList.ItemsSource = draft.ToArray();
            if (index >= 0 && index < draft.Count) draftList.SelectedIndex = index;
            RefreshPackageConflictHint(error);
            RefreshButtons();
        }

        private void PreviewModeChanged()
        {
            if (window.IsLoaded) SavePreferences();
            RefreshLive();
            if (Find<FrameworkElement>("CurrentPage").IsVisible) RefreshInstalledMesh();
            if (Find<FrameworkElement>("BodiesPage").IsVisible) RefreshBodyPreview();
        }

        private async void RefreshLive()
        {
            Catalog current = catalog;
            var selected = sourceList.SelectedItem as Piece;
            var pieces = current == null ? new Piece[0] : (inspecting || draft.Count == 0) && selected != null
                ? new[] { selected } : draft.Where(row => current.ById.ContainsKey(row.Id)).Select(row => current.ById[row.Id]).ToArray();
            liveHint.Text = pieces.Length == 0 ? T("Add parts to see the outfit", "Aggiungi pezzi per vedere l’outfit")
                : T("Loading meshes…", "Caricamento mesh…");
            liveHint.Visibility = Visibility.Visible;
            await viewport.Render(current, pieces, texturedPreview.IsChecked == true, (shown, unavailable, missing) => {
                liveHint.Visibility = shown > 0 ? Visibility.Collapsed : Visibility.Visible;
                if (unavailable > 0 && shown == 0)
                    liveHint.Text = T("Preview unavailable for these meshes", "Anteprima non disponibile per queste mesh");
                var controls = Find<TextBlock>("ViewControls");
                controls.Text = T("Drag to rotate · scroll to zoom", "Trascina per ruotare · rotella per lo zoom");
                controls.ToolTip = unavailable > 0 ? T("Some meshes need BodySlide", "Alcune mesh richiedono BodySlide")
                    : missing > 0 ? T("Some textures are unavailable", "Alcune texture non sono disponibili") : null;
            });
            await window.Dispatcher.InvokeAsync(RefreshButtons);
        }

        private void MovePiece(int direction)
        {
            int current = draftList.SelectedIndex, next = current + direction;
            if (current < 0 || next < 0 || next >= draft.Count) return;
            var piece = draft[current];
            draft.RemoveAt(current);
            draft.Insert(next, piece);
            MarkDraft();
            RefreshDraft(next);
            RefreshLive();
        }

        private void RemovePiece()
        {
            int index = draftList.SelectedIndex;
            if (index < 0) return;
            draft.RemoveAt(index);
            MarkDraft();
            RefreshDraft(Math.Min(index, draft.Count - 1));
            RefreshLive();
        }

        private void MarkDraft()
        {
            if (updating) return;
            if (CurrentPackageConflict() != null) ClearPackageConflict();
            draftDirty = true;
            RefreshButtons();
        }

        private Recipe ReadDraft()
        {
            if (draft.Count == 0) throw new InvalidOperationException(T("Add at least one part.", "Aggiungi almeno un componente."));
            if (String.IsNullOrWhiteSpace(outfitName.Text)) throw new InvalidOperationException(T("Give the outfit a name.", "Assegna un nome all’outfit."));
            if (destination == null) throw new InvalidOperationException(T("Select an official destination.", "Scegli una destinazione ufficiale."));
            var result = new Recipe {
                Name = outfitName.Text.Trim(), Output = destination.Output, Weighted = destination.Weighted,
                Slot = destination.SuggestedSlot(), Pieces = draft.Select(piece => piece.Id).ToList(),
                VariantIds = variantTargets == null ? null : variantTargets.ToList()
            };
            if (editing >= 0 && Files.Comparer.Equals(result.TargetKey, pack.Outfits[editing].TargetKey))
            {
                var saved = pack.Outfits[editing];
                var scope = variantTargets ?? destination.Items.Select(item => item.RuleId).ToList();
                if (!String.IsNullOrEmpty(saved.Modesty))
                {
                    result.Modesty = saved.Modesty;
                    result.ModestyRace = saved.ModestyRace;
                    var records = saved.RuleTargets == null ? scope : saved.RuleTargets.Intersect(scope, Files.Comparer).ToList();
                    result.RuleTargets = records.Count == 0 && saved.RuleTargets != null && saved.RuleTargets.Count > 0
                        ? scope.ToList() : records;
                }
            }
            return result;
        }

        private void Commit()
        {
            var recipe = ReadDraft();
            for (int i = 0; i < pack.Outfits.Count; i++)
            {
                if (i == editing) continue;
                if (Files.Comparer.Equals(pack.Outfits[i].Name, recipe.Name))
                    throw new PackageConflict(T("This package already has an outfit with that name.", "Il pacchetto contiene già un outfit con questo nome."),
                        ConflictArea.Name, new[] { recipe, pack.Outfits[i] });
                if (Composer.OutputFiles(pack.Outfits[i]).Intersect(Composer.OutputFiles(recipe), Files.Comparer).Any())
                    throw new PackageConflict(T("This package already has an outfit for that mesh.", "Il pacchetto contiene già un outfit per questa mesh."),
                        ConflictArea.Destination, new[] { recipe, pack.Outfits[i] });
            }
            if (editing < 0) { editing = pack.Outfits.Count; pack.Outfits.Add(recipe); }
            else pack.Outfits[editing] = recipe;
            draftDirty = false;
            projectDirty = true;
            RefreshPack(editing);
            SetStatus(recipe.Name + T(" added to the package", " nel pacchetto"));
        }

        private void RefreshPack(int index)
        {
            packList.ItemsSource = pack.Outfits.ToArray();
            packList.SelectedIndex = index;
            RefreshKid();
            RefreshButtons();
        }

        private void LoadDraft(int index)
        {
            conflictOnDraft = false;
            updating = true;
            editing = index;
            var recipe = index < 0 ? null : pack.Outfits[index];
            outfitName.Text = recipe == null ? "" : recipe.Name;
            destination = null;
            if (recipe != null && catalog != null) catalog.ByTarget.TryGetValue(recipe.TargetKey, out destination);
            variantTargets = recipe == null ? null : recipe.VariantIds;
            targetList.SelectedItem = destination;
            draft.Clear();
            if (recipe != null) draft.AddRange(recipe.Pieces.Select(id => new DraftPiece { Id = id }));
            updating = false;
            draftDirty = false;
            inspecting = false;
            RefreshDraft(-1);
            RefreshTargetInfo();
            RefreshLive();
        }

        private void EditSelected()
        {
            int index = packList.SelectedIndex;
            if (index < 0 || (index != editing && !CanLeaveDraft())) return;
            if (index != editing) LoadDraft(index);
            Switch("HomePage");
        }

        private void RemoveOutfit()
        {
            int index = packList.SelectedIndex;
            if (index < 0 || (index == editing && !CanLeaveDraft())) return;
            if (packageConflict != null && packageConflict.Outfits.Contains(pack.Outfits[index])) ClearPackageConflict();
            pack.Outfits.RemoveAt(index);
            if (index == editing) LoadDraft(-1);
            else if (index < editing) editing--;
            projectDirty = true;
            RefreshPack(Math.Min(index, pack.Outfits.Count - 1));
        }

        private bool CanLeaveDraft()
        {
            return !draftDirty || Confirm(T("Discard changes to this outfit?", "Scartare le modifiche all’outfit aperto?"));
        }

        private bool CanLeaveProject()
        {
            return (!projectDirty && !draftDirty) || Confirm(T("Discard changes to this project?", "Scartare le modifiche al progetto?"));
        }

        private bool Confirm(string text)
        {
            return MessageBox.Show(window, text, "Trousse", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        private void OpenPack()
        {
            if (!CanLeaveProject()) return;
            var picker = new OpenFileDialog { Filter = "Trousse project (*.json)|*.json" };
            if (picker.ShowDialog(window) != true) return;
            var loaded = Pack.Open(picker.FileName);
            ClearPackageConflict();
            pack = loaded;
            updating = true;
            packName.Text = pack.Name ?? "";
            updating = false;
            projectDirty = false;
            LoadDraft(pack.Outfits.Count == 0 ? -1 : 0);
            RefreshPack(editing);
            SetStatus(T("Project opened", "Progetto aperto"));
        }

        private void SavePack()
        {
            var picker = new SaveFileDialog { Filter = "Trousse project (*.json)|*.json",
                FileName = Files.FileLabel(packName.Text) + ".json", AddExtension = true };
            if (picker.ShowDialog(window) != true) return;
            if (draftDirty) Commit();
            pack.Name = packName.Text.Trim();
            pack.Save(picker.FileName);
            projectDirty = false;
            SetStatus(T("Saved · ", "Salvato · ") + picker.FileName);
        }

        private async Task Export()
        {
            PlayExportSound();
            var picker = new SaveFileDialog { Filter = "Mod ZIP (*.zip)|*.zip", FileName = Files.FileLabel(packName.Text) + ".zip", AddExtension = true };
            if (picker.ShowDialog(window) != true) return;
            await RunBusy(T("Preparing package…", "Preparazione del pacchetto…"), async () => {
                ClearPackageConflict();
                if (draftDirty) Commit();
                pack.Name = packName.Text.Trim();
                await Task.Run(() => {
                    using (var composer = new Composer(catalog, pack, BackgroundStatus))
                    {
                        composer.Build(pack, true, italian);
                        composer.VerifyBuild(pack);
                        composer.Archive(picker.FileName);
                    }
                });
                SetStatus(T("Exported and verified · ", "Esportato e verificato · ") + picker.FileName);
            });
        }

        private async Task ExportOutfitReport()
        {
            var picker = new SaveFileDialog { Filter = "OpenDocument Text (*.odt)|*.odt", DefaultExt = ".odt",
                FileName = Files.FileLabel(packName.Text) + "-outfits.odt" };
            if (picker.ShowDialog(window) != true) return;
            await RunBusy(T("Writing the outfit report…", "Scrittura del riepilogo outfit…"), async () => {
                if (draftDirty) Commit();
                var report = new Pack { Name = packName.Text.Trim(), Outfits = pack.Outfits };
                bool local = italian;
                await Task.Run(() => Composer.SaveOutfitReport(picker.FileName, catalog, report, local));
                SetStatus(T("Outfit report saved · ", "Riepilogo outfit salvato · ") + picker.FileName);
            });
        }

        private async Task Preview()
        {
            await RunBusy(T("Preparing BodySlide preview…", "Preparazione dell’anteprima BodySlide…"), async () => {
                var previewPack = new Pack { Name = "Trousse", Outfits = new List<Recipe> { ReadDraft() } };
                await Task.Run(() => {
                    using (var composer = new Composer(catalog, previewPack, BackgroundStatus))
                    {
                        composer.Build(previewPack, false);
                        composer.Preview();
                    }
                });
                SetStatus(T("Preview closed", "Anteprima chiusa"));
            });
        }

        private void ShowNotices()
        {
            ShowDetails(T("Scan details", "Dettagli scansione"), String.Join(Environment.NewLine, catalog.Notices));
        }

        private static bool IsListItem(object source)
        {
            var current = source as DependencyObject;
            while (current is System.Windows.Media.Visual)
            {
                if (current is ListBoxItem) return true;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private sealed class DraftPiece
        {
            internal string Id;
            public string Name { get; set; }
            public string Detail { get; set; }
            public string BuildError { get; set; }
            public bool HasBuildError { get { return BuildError != null; } }
        }
    }
}
