using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace SkyrimOutfit
{
    internal sealed partial class App
    {
        private void ApplyLanguage()
        {
            italian = language.SelectedIndex == 1;
            var words = new[] {
                "HomeNav|Compose|Componi", "PackNav|Package|Pacchetto", "SettingsNav|Settings|Impostazioni",
                "GuideNav|Guide|Guida", "LibraryHeading|BodySlide library|Libreria BodySlide", "VanillaHeading|Game equipment|Equipaggiamento del gioco",
                "CurrentNav|Installed outfit|Outfit installato", "CurrentHeading|Installed outfit|Outfit installato",
                "CurrentRefresh|Refresh|Aggiorna", "CurrentUnique|Use Unique Bodies by Race|Usa Unique Bodies by Race",
                "CurrentSearchLabel|Search equipment|Cerca equipaggiamento", "CurrentLowWeight|Low weight|Peso basso",
                "CurrentPreviewHeading|Installed mesh|Mesh installata",
                "CurrentCopy|Copy console command|Copia comando console",
                "BodiesNav|Bodies|Corpi", "BodiesSearchLabel|Search bodies|Cerca corpi", "BodyPresetSearchLabel|Search presets|Cerca preset",
                "BodyProjectsHeading|CBBE body projects|Progetti corpo CBBE", "AddBody|Add body to outfit|Aggiungi corpo all’outfit",
                "BodyPresetsHeading|BodySlide slider presets|Preset slider di BodySlide", "PresetHeading|Preset details|Dettagli preset",
                "OarHeading|Open Animation Replacer settings|Impostazioni di Open Animation Replacer",
                "OarSourceLabel|Configuration|Configurazione", "OarSearchLabel|Search settings|Cerca impostazioni",
                "OarReload|Reload|Ricarica", "OarOpen|Open INI…|Apri INI…", "OarSave|Save INI…|Salva INI…",
                "PosesNav|Poses|Pose", "PoseRefresh|Refresh|Aggiorna", "PoseSave|Save Favorites.json…|Salva Favorites.json…",
                "PoseSearchLabel|Search poses|Cerca pose", "PoseFavoritesHeading|Favorites|Preferiti",
                "PoseAdd|Add to favorites|Aggiungi ai preferiti", "PoseRemove|Remove from favorites|Rimuovi dai preferiti",
                "PosePreviewHeading|Pose preview|Anteprima posa", "PoseOpenPreview|3D preview|Anteprima 3D",
                "PoseShowImages|Gallery|Galleria", "PoseExternalPreview|Open in BodySlide|Apri in BodySlide",
                "PosePlay|Play|Riproduci",
                "DressComposePart|Use in Dress Up|Usa in Dress Up", "DressComposeTarget|Use in Dress Up|Usa in Dress Up",
                "DressSaveGame|Save to Skyrim|Salva in Skyrim", "DressExport|Export JSON…|Esporta JSON…",
                "DressOpen|Open profile…|Apri profilo…", "DressProfilesHeading|Custom outfits|Outfit personalizzati",
                "DressProfileNameLabel|Profile name|Nome del profilo", "DressNew|New outfit|Nuovo outfit",
                "DressRemoveOutfit|Remove outfit|Rimuovi outfit", "DressItemsHeading|Outfit equipment|Pezzi dell’outfit",
                "DressNameLabel|Name shown in game|Nome mostrato in gioco", "DressRemoveItems|Remove selected|Rimuovi selezione",
                "DressGoCompose|Open Compose|Apri Componi", "DressRefresh|Refresh equipment|Aggiorna equipaggiamento",
                "DressLibraryHeading|Game equipment|Equipaggiamento del gioco",
                "DressSearchLabel|Search|Cerca",
                "DressAddItems|Add selected equipment|Aggiungi equipaggiamento selezionato",
                "PieceSearchLabel|Search mods or parts|Cerca mod o pezzi", "TargetSearchLabel|Search base game and DLC|Cerca nel gioco base e nei DLC",
                "AddPieces|Add selected parts|Aggiungi pezzi", "InspectPiece|Inspect part|Vedi pezzo", "Variants|Variants and records|Varianti e record",
                "ShowOutfitView|Show outfit|Mostra outfit", "PreviewHeading|Live preview|Anteprima dal vivo", "Preview|Open in BodySlide|Apri in BodySlide",
                "ViewControls|Drag to rotate · scroll to zoom|Trascina per ruotare · rotella per lo zoom",
                "LiveHint|Add parts to see the outfit|Aggiungi pezzi per vedere l’outfit",
                "ComposeHeading|Compose outfit|Componi outfit", "OutfitNameLabel|Outfit name|Nome outfit",
                "MeshLabel|Destination|Destinazione", "NewOutfit|New outfit|Nuovo outfit", "RemovePiece|Remove part|Rimuovi pezzo",
                "KidHeading|Package modesty rules|Regole modestia del pacchetto", "KidOutfitsHeading|Outfits in the package|Outfit nel pacchetto",
                "KidRecordHeading|Game items affected|Capi del gioco interessati",
                "EnableModesty|Use a custom rule for this outfit|Usa una regola personalizzata per questo outfit",
                "ModestyTop|Cover upper body · slot 32|Copri sopra · slot 32",
                "ModestyBottom|Cover lower body · slots 49/52|Copri sotto · slot 49/52", "ModestyRaceLabel|Wearer race|Razza",
                "BodyPreviewHeading|Body preview|Anteprima corpo",
                "BodyResetPreset|Use project defaults|Usa valori del progetto",
                "ExportKid|Save KID…|Salva KID…",
                "PackageHeading|Replacer package|Pacchetto replacer", "PackNameLabel|Package name|Nome pacchetto",
                "EditOutfit|Edit outfit|Modifica outfit", "RemoveOutfit|Remove outfit|Rimuovi outfit",
                "ExportOdt|Save outfit report…|Salva riepilogo ODT…",
                "Export|Export mod ZIP|Esporta ZIP mod", "OpenPack|Open project|Apri progetto", "SavePack|Save project|Salva progetto",
                "SettingsHeading|Settings|Impostazioni", "VortexLabel|Vortex staging folder|Cartella staging di Vortex",
                "ManagerLabel|Mod manager|Gestore delle mod", "Mo2Label|MO2 instance configuration|Configurazione dell’istanza MO2",
                "BrowseMo2|Browse|Sfoglia",
                "BodyLabel|BodySlide folder|Cartella BodySlide", "BrowseVortex|Browse|Sfoglia", "BrowseBody|Browse|Sfoglia",
                "LanguageLabel|Language|Lingua", "AutoScan|Scan at startup|Scansiona all’avvio",
                "BadgeEditionLabel|Edition badge|Logo edizione",
                "FemaleOnly|Hide male-only destinations|Nascondi le destinazioni solo maschili",
                "PreviewSettingsHeading|Live preview|Anteprima dal vivo", "TexturedPreview|Use original textures in the 3D preview|Usa le texture originali nell’anteprima 3D",
                "TexturedPreviewHint|Turn off textures to see each part in a distinct color.|Disattiva le texture per distinguere i pezzi con colori diversi.",
                "AudioSettingsHeading|Audio|Audio", "MusicEnabled|Play background music|Riproduci musica di sottofondo",
                "EffectsEnabled|Play interface sounds|Riproduci effetti sonori dell’interfaccia", "BrowseMusic|Choose music|Scegli musica",
                "BrowseCrash|Choose error sound|Scegli suono di errore", "BrowseExportSound|Choose export sound|Scegli suono di esportazione",
                "ResetAudio|Use default audio files|Usa i file audio predefiniti",
                "Scan|Scan library|Scansiona libreria", "GuideHeading|Guide|Guida", "Notices|Scan details|Dettagli scansione"
            };
            foreach (string row in words)
            {
                string[] fields = row.Split('|');
                string label = T(fields[1], fields[2]);
                object control = window.FindName(fields[0]);
                var content = control as ContentControl;
                if (content != null) content.Content = label;
                else ((TextBlock)control).Text = label;
            }
            Find<TextBlock>("GuideText").Text = T(
                "1. Scan the library\nIn Settings, select your mod manager. In Vortex mode, choose the staging and BodySlide folders. In Mod Organizer 2 mode, select the instance’s ModOrganizer.ini and start Trousse with Run in MO2. Enable BodySlide and your source mods in the active profile before scanning.\n\n" +
                "2. Mix parts\nSelect a mod, then double-click a part or select several and add them. The live preview uses the original meshes and textures. Disable textures in Settings to give each part a distinct color. Drag to rotate and scroll to zoom. Inspect part shows one selected piece. Open in BodySlide gives you the assembled project and preset controls.\n\n" +
                "3. Pick the game item\nSearch official Skyrim and DLC equipment by BodySlide name, console ID, Editor ID or outfit group. In Variants and records, choose specific colors and any records that share their Armor Addon. Trousse generates a Variants ESP and a separate BodySlide output for those choices. Enable the ESP in Vortex and build the outfit in BodySlide.\n\n" +
                "4. Preview bodies and presets\nIn Bodies, select a CBBE project and preset. Trousse builds both weights in the background the first time and reuses the cached previews after restarting. Use project defaults clears the preset. Drag to rotate and scroll to zoom. Choose your preset again when building an outfit for the game.\n\n" +
                "5. Set modesty for packaged outfits\nAdd an outfit to the package, open KID and select it. Enable its custom rule and choose which parts the character should cover with animation. For Iron Armor with upper-body cover only, check Cover upper body and clear Cover lower body. Select the affected game items. The outfit ZIP includes an INI for all-race rules and an OAR patch for rules specific to a race. Save KID also exports the all-race rules separately.\n\n" +
                "6. Edit OAR and animation mods\nGeneral settings writes the OAR INI for all races. Select an animation mod, then choose a race to enable or disable a submod for that race. In Dynamic Feminine Female Modesty Animations, choose the player or NPC profile and use Main switches for modesty and detection. Use default follows the mod’s general setting. Export mod patch saves the edited user.json files in a Vortex ZIP. Deploy the patch after its source mods and before exporting any race-specific KID outfit rules.\n\n" +
                "7. Preview poses and manage favorites\nIn Poses, choose 3D preview to apply a compatible human HKX to the CBBE body, preset and weight chosen in Bodies. In 3D mode, selecting another pose updates the model. Use the timeline to scrub longer clips and Gallery to see the pack images. If a file needs BodySlide, use Open in BodySlide, then Animation → Load HKX… and paste the copied path. Save Favorites.json in PoserData and deploy again if you saved in Vortex staging.\n\n" +
                "8. Export and verify outfits\nThe mod ZIP includes an ODT with the game items, source mods and parts. See required_mods.txt for dependencies. Install the ZIP with your mod manager. In MO2, enable the package and run BodySlide from MO2. Keep its built meshes in an enabled output mod or Overwrite. In Vortex, deploy the package before building. Installed outfit shows the meshes visible to the current profile.\n\n" +
                "9. Make a Dress Up Lovers profile\nSelect a profile slot and create an outfit with the name that should appear in Skyrim. Add items from the equipment list or use Use in Dress Up on a BodySlide part or a game item in Compose. For a mashup that replaces a game item's mesh, add the destination item. Use Save to Skyrim or place the exported JSON in Data\\SKSE\\Plugins\\DressUpLovers\\User. In the Dress Up Lovers MCM, choose Backup Custom Outfits, the same profile slot, then Import profile. Import replaces the current custom outfits. Export your existing outfit profile in the MCM first to edit or keep those outfits. The game's editor can drop weapons when rebuilding a saved outfit.",
                "1. Scansiona la libreria\nNelle impostazioni, scegli il gestore delle mod. Per Vortex seleziona le cartelle di staging e BodySlide. Per Mod Organizer 2 seleziona il file ModOrganizer.ini dell’istanza e avvia Trousse con Esegui in MO2. Attiva BodySlide e le mod sorgenti nel profilo prima di scansionare.\n\n" +
                "2. Unisci i pezzi\nSeleziona una mod e aggiungi i pezzi con doppio clic o selezione multipla. L’anteprima dal vivo usa le mesh e le texture originali. Disattiva le texture nelle impostazioni per distinguere i pezzi con colori diversi. Trascina per ruotare e usa la rotella per lo zoom. Vedi pezzo mostra il singolo componente. Apri in BodySlide mostra il progetto assemblato e i preset.\n\n" +
                "3. Scegli il capo del gioco\nCerca l’equipaggiamento ufficiale di Skyrim e dei DLC per nome BodySlide, ID console, Editor ID o gruppo. In Varianti e record puoi selezionare un colore e i record che condividono il suo Armor Addon. Trousse genera un ESP Variants e un output BodySlide separato. Attiva l’ESP in Vortex e costruisci il vestito in BodySlide.\n\n" +
                "4. Guarda corpi e preset\nIn Corpi, scegli un progetto CBBE e un preset. Trousse costruisce entrambi i pesi in background al primo utilizzo e riusa le anteprime salvate anche dopo il riavvio. Usa valori del progetto rimuove il preset. Trascina per ruotare e usa la rotella per lo zoom. Scegli di nuovo il preset quando costruisci l’outfit per il gioco.\n\n" +
                "5. Imposta la modestia degli outfit nel pacchetto\nAggiungi un outfit al pacchetto, apri KID e selezionalo. Attiva la regola personalizzata e scegli quali parti il personaggio deve coprire con l’animazione. Per Iron Armor, con modestia solo sopra, spunta Copri sopra e lascia Copri sotto deselezionato. Seleziona i capi del gioco interessati. Lo ZIP degli outfit include l’INI per tutte le razze e la patch OAR per le regole specifiche. Salva KID esporta a parte le regole per tutte le razze.\n\n" +
                "6. Modifica OAR e le mod di animazione\nLe impostazioni generali scrivono l’INI di OAR, valido per tutte le razze. Seleziona una mod di animazione e poi una razza per attivare o disattivare un sottomod per quella razza. In Dynamic Feminine Female Modesty Animations, scegli il profilo PG o PNG e usa Interruttori principali per modestia e rilevamento. Usa il predefinito segue l’impostazione generale della mod. Esporta patch mod salva i file user.json in uno ZIP. Distribuisci la patch dopo le mod sorgenti e prima di esportare eventuali regole KID per razza degli outfit.\n\n" +
                "7. Visualizza le pose e gestisci i preferiti\nIn Pose, scegli Anteprima 3D per applicare un HKX umano compatibile al corpo CBBE, al preset e al peso scelti in Corpi. In modalità 3D il modello si aggiorna quando cambi posa. Usa la barra temporale per i clip più lunghi e Galleria per le immagini del pacchetto. Se un file richiede BodySlide, usa Apri in BodySlide e poi Animation → Load HKX… e incolla il percorso copiato. Salva Favorites.json in PoserData. Se salvi nello staging, ripeti il deploy in Vortex.\n\n" +
                "8. Esporta e controlla gli outfit\nLo ZIP della mod include un ODT con i capi del gioco, le mod sorgenti e i pezzi. Consulta required_mods.txt per le dipendenze. Installa lo ZIP con il tuo gestore di mod. In MO2 abilita il pacchetto e avvia BodySlide da MO2. Conserva le mesh in una mod di output abilitata o in Overwrite. In Vortex fai il deploy prima della build. Outfit installato mostra le mesh visibili al profilo corrente.\n\n" +
                "9. Crea un profilo Dress Up Lovers\nScegli uno slot del profilo e crea un outfit con il nome che vuoi vedere in Skyrim. Aggiungi i capi dalla lista dell’equipaggiamento oppure usa Usa in Dress Up su un pezzo BodySlide o un capo del gioco in Componi. Per un mashup che sostituisce una mesh del gioco, aggiungi il capo di destinazione. Usa Salva in Skyrim oppure copia il JSON in Data\\SKSE\\Plugins\\DressUpLovers\\User. Nel menu MCM di Dress Up Lovers scegli Backup Custom Outfits, lo stesso slot e Import profile. L’importazione sostituisce gli outfit personalizzati attuali. Se vuoi conservare o modificare gli outfit esistenti, esporta prima il profilo dal MCM. L’editor del gioco può rimuovere le armi quando ricostruisce un outfit.");
            if (!projectDirty && pack.Outfits.Count == 0)
            {
                updating = true;
                packName.Text = T("My replacers", "I miei replacer");
                updating = false;
            }
            int category = Math.Max(0, targetCategory.SelectedIndex);
            updating = true;
            targetCategory.ItemsSource = EquipmentCategoryLabels();
            targetCategory.SelectedIndex = category;
            System.Windows.Automation.AutomationProperties.SetName(targetCategory, T("Equipment category", "Categoria equipaggiamento"));
            updating = false;
            RefreshAudioLabels();
            Find<TextBlock>("AppVersionLabel").Text = T("Version ", "Versione ") + AppVersion;
            LocalizeInstalledPage();
            LocalizeBodyWeight();
            LocalizeModesty();
            LocalizeOar();
            LocalizePoses();
            LocalizeDressUp();
            RefreshTargets();
            RefreshTargetInfo();
            RefreshButtons();
            RefreshLive();
            if (Find<FrameworkElement>("BodiesPage").IsVisible) RefreshBodyPreview();
            var badge = Find<ComboBox>("BadgeEdition");
            int badgeIndex = settings.BadgeEdition == "anniversary" ? 2 : settings.BadgeEdition == "special" ? 1 : 0;
            updating = true;
            badge.ItemsSource = new[] { T("Automatic", "Automatico"), "Special Edition", "Anniversary Edition" };
            badge.SelectedIndex = badgeIndex;
            updating = false;
            RefreshGameBadges();
            RefreshModManager();
            if (window.IsLoaded) SavePreferences();
        }

        private string[] EquipmentCategoryLabels()
        {
            return T("All categories|Body|Boots and shoes|Gloves and gauntlets|Helmets and headwear|Amulets|Rings|Shields|Other",
                "Tutte le categorie|Corpo|Stivali e scarpe|Guanti e guantali|Elmi e copricapi|Amuleti|Anelli|Scudi|Altro").Split('|');
        }

        private string EquipmentView(string view) { return EquipmentView(view, italian); }

        internal static string EquipmentView(string view, bool italian)
        {
            return italian ? view.Replace("Female", "Donna").Replace("Male", "Uomo")
                .Replace("Shared", "Condiviso").Replace("first person", "prima persona") : view;
        }

        private string TranslateStatus(string value)
        {
            if (italian) return value;
            return value.Replace("Preparazione di Outfit Studio…", "Preparing Outfit Studio…")
                .Replace("Fusione in Outfit Studio", "Composing in Outfit Studio")
                .Replace("Verifica del progetto e delle partizioni", "Checking project and partitions")
                .Replace("Build di prova in BodySlide…", "Test build in BodySlide…")
                .Replace("Creazione dello ZIP della mod…", "Creating mod ZIP…")
                .Replace("Anteprima aperta in BodySlide. Chiudila per tornare alla composizione.", "Preview opened in BodySlide. Close it to continue composing.");
        }

        private void ShowError(Exception error)
        {
            PlayCrashSound();
            SetStatus(error.Message.Split('\n')[0].TrimEnd('\r'));
            var conflict = error as PackageConflict;
            if (conflict != null) { ShowPackageConflict(conflict); return; }
            if (!startupLoading) ShowDetails(T("Trousse · Error", "Trousse · Errore"), error.Message);
        }

        private void ShowDetails(string title, string message)
        {
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBox {
                Text = message, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 350,
                Style = (Style)window.FindResource(typeof(TextBox))
            });
            panel.Children.Add(new Button {
                Content = "OK", IsCancel = true, IsDefault = true, MinWidth = 76, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0), Style = (Style)window.FindResource("Accent")
            });
            Dialog(title, panel, 620).ShowDialog();
        }

        private Window Dialog(string title, FrameworkElement content, int width)
        {
            return new Window {
                Owner = window, Title = title, Content = content, Width = Math.Min(width, Math.Max(400, window.ActualWidth)),
                SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = window.Background, Foreground = window.Foreground, FontFamily = window.FontFamily
            };
        }

        private void ShowVariants()
        {
            if (destination == null) return;
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = destination.DisplayName(italian), FontWeight = FontWeights.SemiBold,
                FontSize = 21, Margin = new Thickness(0, 0, 0, 6) });
            panel.Children.Add(new TextBlock { Text = destination.Items.Count > 1
                ? T("Select one or more items to replace separately. Trousse exports an ESP for variants that have their own Armor Addon. Replacing the whole mesh affects every item below.",
                    "Seleziona uno o più capi da sostituire separatamente. Trousse esporta un ESP per le varianti che hanno un proprio Armor Addon. Sostituire l’intera mesh interessa tutti i capi sotto.")
                : T("The mesh is used by this game record. Outfit groups below show where it is assigned.",
                    "Questo record del gioco usa la mesh. I gruppi sotto mostrano dove viene assegnato."),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            var records = new ListBox { SelectionMode = SelectionMode.Extended, MaxHeight = 310,
                Style = (Style)window.FindResource(typeof(ListBox)), ItemContainerStyle = (Style)window.FindResource(typeof(ListBoxItem)) };
            var byRow = new Dictionary<ListBoxItem, GameItem>();
            foreach (var item in destination.Items.OrderBy(item => item.Name(italian), Files.Comparer))
            {
                HashSet<string> textures;
                item.TextureVariants.TryGetValue(GameRecords.MeshStem(destination.Output), out textures);
                var linked = new HashSet<string>(destination.Addons.Where(addon => addon.Users.Contains(item.RuleId))
                    .SelectMany(addon => addon.Users).Where(id => !Files.Comparer.Equals(id, item.RuleId)), Files.Comparer);
                var companionIds = destination.Items.Where(other => linked.Contains(other.RuleId)).Select(other => other.ConsoleId).ToArray();
                var row = new ListBoxItem { Content = new TextBlock {
                    Text = item.Name(italian) + " · " + item.ConsoleId + " · " + item.EditorId +
                        (textures == null ? "" : Environment.NewLine + T("Textures: ", "Texture: ") + String.Join(" · ", textures.OrderBy(name => name))) +
                        (companionIds.Length == 0 ? "" : Environment.NewLine + T("Shared Armor Addon with ", "Armor Addon condiviso con ") +
                            String.Join(", ", companionIds)) +
                        (item.Outfits.Count == 0 ? "" : Environment.NewLine + String.Join(" · ", item.Outfits.OrderBy(group => group))),
                    TextWrapping = TextWrapping.Wrap
                }, ToolTip = item.RuleId };
                records.Items.Add(row);
                byRow.Add(row, item);
                var selected = variantTargets;
                if (selected != null && selected.Contains(item.RuleId, Files.Comparer)) row.IsSelected = true;
            }
            panel.Children.Add(records);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var cancel = new Button { Content = T("Cancel", "Annulla"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0),
                Style = (Style)window.FindResource(typeof(Button)) };
            var copy = new Button { Content = T("Copy console commands", "Copia comandi console"), Margin = new Thickness(0, 0, 8, 0),
                Style = (Style)window.FindResource(typeof(Button)) };
            copy.Click += (sender, args) => Handle(() => {
                string commands = String.Join(Environment.NewLine, records.SelectedItems.Cast<ListBoxItem>()
                    .Select(row => "player.additem " + byRow[row].ConsoleId + " 1"));
                if (commands.Length > 0) Clipboard.SetText(commands);
            });
            var entire = new Button { Content = T("Replace whole mesh", "Sostituisci intera mesh"), Margin = new Thickness(0, 0, 8, 0),
                Style = (Style)window.FindResource(typeof(Button)) };
            var replace = new Button { Content = T("Replace selected variants", "Sostituisci varianti selezionate"), IsDefault = true,
                Style = (Style)window.FindResource("Accent") };
            actions.Children.Add(copy);
            actions.Children.Add(entire);
            actions.Children.Add(replace);
            actions.Children.Add(cancel);
            panel.Children.Add(actions);
            var dialog = Dialog(T("Vanilla variants and records", "Varianti vanilla e record"), panel, 790);
            entire.Click += (sender, args) => {
                if (variantTargets != null) { variantTargets = null; MarkDraft(); RefreshTargetInfo(); }
                dialog.Close();
            };
            replace.Click += (sender, args) => {
                var ids = records.SelectedItems.Cast<ListBoxItem>().Select(row => byRow[row].RuleId).ToList();
                if (ids.Count == 0)
                {
                    MessageBox.Show(dialog, T("Select the item variants to replace.", "Seleziona le varianti da sostituire."));
                    return;
                }
                try { VariantPatch.Resolve(destination, ids); }
                catch (InvalidDataException)
                {
                    MessageBox.Show(dialog, T("These variants share an Armor Addon with other items. Select the companion variants or replace the whole mesh.",
                        "Queste varianti condividono un Armor Addon con altri capi. Seleziona anche le varianti collegate oppure sostituisci l’intera mesh."));
                    return;
                }
                variantTargets = ids;
                MarkDraft();
                RefreshTargetInfo();
                dialog.Close();
            };
            dialog.ShowDialog();
        }

        private static BitmapImage ImageFromStream(Stream stream)
        {
            var result = new BitmapImage();
            result.BeginInit();
            result.CacheOption = BitmapCacheOption.OnLoad;
            result.StreamSource = stream;
            result.EndInit();
            result.Freeze();
            return result;
        }

        private void SavePreferences()
        {
            settings.Staging = vortexPath.Text.Trim();
            settings.Body = bodyPath.Text.Trim();
            settings.UseMo2 = UseModOrganizer;
            settings.Mo2Ini = Find<TextBox>("Mo2Ini").Text.Trim();
            settings.Italian = italian;
            settings.AutoScan = autoScan.IsChecked == true;
            settings.Female = femaleOnly.IsChecked == true;
            settings.TexturedPreview = texturedPreview.IsChecked == true;
            settings.MusicEnabled = musicEnabled.IsChecked == true;
            settings.EffectsEnabled = effectsEnabled.IsChecked == true;
            settings.Save();
        }
    }

    [DataContract]
    internal sealed class Preferences
    {
        [DataMember] public string Staging = "";
        [DataMember] public string Body = "";
        [DataMember] public bool UseMo2;
        [DataMember] public string Mo2Ini;
        [DataMember] public bool Italian, AutoScan = true, Female = true;
        [DataMember] public bool TexturedPreview = true, MusicEnabled = true, EffectsEnabled = true;
        [DataMember] public string MusicFile;
        [DataMember] public string CrashFile, ExportSoundFile;
        [DataMember] public bool? UseRaceMeshes;
        [DataMember] public string PreviewRace;
        [DataMember] public string BadgeEdition;
        private static readonly string PathToSettings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Trousse", "settings.json");
        internal static string BodyCachePath { get { return Path.Combine(Path.GetDirectoryName(PathToSettings), "preview-cache", "bodies-v1"); } }

        [OnDeserializing]
        private void AddedSettingsDefaults(StreamingContext context)
        {
            TexturedPreview = MusicEnabled = EffectsEnabled = true;
        }

        internal static Preferences Load()
        {
            Preferences value = null;
            try
            {
                if (File.Exists(PathToSettings))
                    using (var stream = File.OpenRead(PathToSettings))
                    {
                        value = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(stream);
                    }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SerializationException) { }
            if (value == null) value = new Preferences();
            value.ResolvePaths();
            return value;
        }

        private void ResolvePaths()
        {
            Staging = Staging ?? "";
            Body = Body ?? "";
            if (!Directory.Exists(Staging))
            {
                string suggested = Environment.GetEnvironmentVariable("TROUSSE_VORTEX_STAGING");
                if (Directory.Exists(suggested)) Staging = suggested;
                else if (String.IsNullOrWhiteSpace(Staging))
                {
                    string standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Vortex\skyrimse\mods");
                    if (Directory.Exists(standard)) Staging = standard;
                }
            }
            if (!HasBodySlide(Body))
            {
                string game = Environment.GetEnvironmentVariable("TROUSSE_SKYRIM_PATH");
                if (Directory.Exists(game))
                {
                    string suggested = Path.Combine(game, @"Data\CalienteTools\BodySlide");
                    if (HasBodySlide(suggested)) Body = suggested;
                }
            }
        }

        internal static bool HasBodySlide(string directory)
        {
            return Directory.Exists(directory) && File.Exists(Catalog.BodySlideExecutable(directory, "OutfitStudio"));
        }

        internal void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathToSettings));
                using (var stream = File.Create(PathToSettings)) new DataContractJsonSerializer(typeof(Preferences)).WriteObject(stream, this);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal sealed class ModGroup
    {
        internal static readonly ModGroup Empty = new ModGroup();
        internal string Id, Key;
        internal Piece[] Parts;
        public string Name { get; private set; }
        public string Badge { get { return Id == null ? "E" : "N↗"; } }
        public string BadgeHint { get { return Id == null ? "External mod" : "Nexus Mods"; } }
        public string BadgeColor { get { return Id == null ? "#a7b1b6" : "#eea562"; } }
        public ICommand OpenSource { get; private set; }

        internal static ModGroup[] Group(IEnumerable<Piece> pieces)
        {
            var result = new List<ModGroup>();
            foreach (var mod in pieces.GroupBy(piece => piece.NexusId == null ? piece.Mod : "Nexus:" + piece.NexusId, Files.Comparer))
            {
                Piece first = mod.First();
                var group = new ModGroup {
                    Key = mod.Key, Id = first.NexusId, Name = Regex.Replace(first.ModName,
                        @"\s*[-–]\s*(?:CBBE(?:\s+3BA)?|3BA|BHUNP|UNP)(?:\s+SE)?$", "", RegexOptions.IgnoreCase),
                    Parts = mod.GroupBy(piece => piece.Name + "|" + piece.Output, Files.Comparer)
                        .Select(options => options.OrderByDescending(piece => piece.ModFolder, Files.Comparer).First())
                        .OrderBy(piece => piece.ShortName, Files.Comparer).ToArray()
                };
                group.OpenSource = new NexusCommand(group.Id);
                result.Add(group);
            }
            return result.OrderBy(group => group.Name, Files.Comparer).ToArray();
        }

        private sealed class NexusCommand : ICommand
        {
            private readonly string id;
            internal NexusCommand(string id) { this.id = id; }
            public bool CanExecute(object parameter) { return id != null; }
            public void Execute(object parameter)
            {
                if (id != null) Process.Start(new ProcessStartInfo(PackageReports.NexusUrl(id)) { UseShellExecute = true });
            }
            public event EventHandler CanExecuteChanged { add { } remove { } }
        }
    }
}
