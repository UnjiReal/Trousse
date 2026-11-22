using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SkyrimOutfit
{
    internal sealed partial class App
    {
        private PoserFavorites poses;
        private string posesStaging, posesData;
        private bool posesDirty, changingPosePacks;
        private ListBox poseLibrary, poseFavorites;
        private ComboBox posePacks;
        private PoseEntry previewPose;
        private string poseAnimation;
        private string[] poseImages = new string[0];
        private int poseImageIndex;
        private bool poseImageExact;
        private LiveViewport poseViewport;
        private bool showPose3D, changingPoseFrame;
        private CancellationTokenSource poseRequest;
        private PoseRig.PoseModel activePose;
        private Catalog rigCatalog;
        private Task<PoseRig> rig;
        private DispatcherTimer poseTimer;
        private Stopwatch poseClock;
        private double startFrame, pendingFrame;
        private bool calculatingFrame;

        private void ConfigurePoses()
        {
            poseLibrary = Find<ListBox>("PoseLibrary");
            poseFavorites = Find<ListBox>("PoseFavorites");
            posePacks = Find<ComboBox>("PosePacks");
            poseViewport = new LiveViewport(Find<Viewport3D>("PoseViewport"), Find<Border>("PosePreviewSurface"));
            poseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(55) };
            poseTimer.Tick += (s, e) => AdvancePose();
            Find<Slider>("PoseFrame").PreviewMouseLeftButtonDown += (s, e) => PausePose();
            Find<Slider>("PoseFrame").ValueChanged += async (s, e) => {
                if (!changingPoseFrame) await ShowPoseFrame(e.NewValue);
            };
            window.Closed += (s, e) => StopPosePreview();
            Bind("PosesNav", async () => {
                Switch("PosesPage");
                await ScanPoses(false);
                if (showPose3D && activePose == null && poseRequest == null) await LoadPoseModel();
            });
            Bind("PoseRefresh", async () => await ScanPoses(true));
            Bind("PoseAdd", AddPoseFavorites);
            Bind("PoseRemove", RemovePoseFavorites);
            Bind("PoseSave", SavePoseFavorites);
            Bind("PoseImagePrevious", () => MovePoseImage(-1));
            Bind("PoseImageNext", () => MovePoseImage(1));
            Bind("PoseOpenPreview", async () => { showPose3D = true; await LoadPoseModel(); });
            Bind("PoseShowImages", () => { showPose3D = false; StopPosePreview(); ShowPoseImage(); });
            Bind("PoseExternalPreview", async () => await OpenPosePreview());
            Bind("PosePlay", TogglePosePlayback);
            poseLibrary.SelectionChanged += (s, e) => RefreshPosePreview(poseLibrary.SelectedItem as PoseEntry);
            poseFavorites.SelectionChanged += (s, e) => {
                var favorite = poseFavorites.SelectedItem as PoseEntry;
                RefreshPosePreview(favorite == null || poses == null ? poseLibrary.SelectedItem as PoseEntry :
                    poses.Poses.FirstOrDefault(pose => Files.Comparer.Equals(pose.Raw, favorite.Raw)) ?? favorite);
            };
            posePacks.SelectionChanged += (s, e) => { if (!changingPosePacks) RefreshPoseList(); };
            Find<TextBox>("PoseSearch").TextChanged += (s, e) => RefreshPoseList();
            poseLibrary.MouseDoubleClick += (s, e) => { if (IsListItem(e.OriginalSource)) Handle(AddPoseFavorites); };
        }

        private bool CanReloadPoses()
        {
            return !posesDirty || Confirm(T("Discard unsaved pose favorites?", "Scartare le modifiche ai preferiti delle pose?"));
        }

        private async Task ScanPoses(bool force)
        {
            string staging = vortexPath.Text.Trim().Trim('"');
            string body = bodyPath.Text.Trim().Trim('"');
            string data = catalog == null ? null : catalog.DataRoot;
            if (data == null && Preferences.HasBodySlide(body)) data = Catalog.GameData(body);
            if (poses != null && !force && Files.Comparer.Equals(staging, posesStaging) && Files.Comparer.Equals(data, posesData)) return;
            if (!CanReloadPoses()) return;
            await RunBusy(T("Scanning Poser Hotkeys pose packs…", "Scansione pacchetti pose di Poser Hotkeys…"), async () => {
                poses = await Task.Run(() => PoserFavorites.Scan(staging, data));
                posesData = data;
                posesStaging = staging;
                posesDirty = false;
                LocalizePoses();
                SetStatus(poses.Poses.Count + T(" poses found", " pose trovate"));
            });
        }

        private void LocalizePoses()
        {
            if (Find<FrameworkElement>("PosesPage").Visibility != Visibility.Visible) return;
            var previous = posePacks.SelectedItem as string;
            changingPosePacks = true;
            string all = T("All pose packs", "Tutti i pacchetti pose");
            var packs = poses == null ? new string[0] : poses.Poses.Select(pose => pose.Pack).Distinct(Files.Comparer)
                .OrderBy(pack => pack, Files.Comparer).ToArray();
            posePacks.ItemsSource = new[] { all }.Concat(packs).ToArray();
            posePacks.SelectedItem = packs.Contains(previous, Files.Comparer) ? previous : all;
            changingPosePacks = false;
            RefreshPoseList();
            RefreshPoseFavorites();
            RefreshPosePreview(poseLibrary.SelectedItem as PoseEntry, true);
        }

        private void RefreshPoseList()
        {
            string pack = posePacks.SelectedIndex <= 0 ? null : posePacks.SelectedItem as string;
            string query = Find<TextBox>("PoseSearch").Text;
            var entries = poses == null ? new PoseEntry[0] : poses.Poses.Where(pose =>
                (pack == null || Files.Comparer.Equals(pose.Pack, pack)) && Match(query, pose.Name, pose.Event, pose.Pack)).ToArray();
            poseLibrary.ItemsSource = entries;
            Find<TextBlock>("PoseCount").Text = entries.Length.ToString("N0");
            RefreshPosePreview(poseLibrary.SelectedItem as PoseEntry);
        }

        private void RefreshPoseFavorites()
        {
            poseFavorites.ItemsSource = poses == null ? null : poses.Favorites
                .Select(raw => PoseEntry.Parse(raw, "")).ToArray();
            var hint = Find<TextBlock>("PoseDestination");
            hint.Text = poses == null ? "" : poses.Destination ?? T("Choose where to save Favorites.json.", "Scegli dove salvare Favorites.json.");
            if (poses != null && poses.Notices.Count != 0) hint.Text += " · " + poses.Notices.Count +
                T(" unreadable packs", " pacchetti illeggibili");
            hint.ToolTip = poses == null ? null : String.Join(Environment.NewLine, new[] { poses.Destination }.Concat(poses.Notices));
            UpdatePoseButtons();
        }

        private void UpdatePoseButtons()
        {
            buttons["PoseAdd"].IsEnabled = !busy && poses != null && poseLibrary.SelectedItems.Count > 0;
            buttons["PoseRemove"].IsEnabled = !busy && poses != null && poseFavorites.SelectedItems.Count > 0;
            buttons["PoseSave"].IsEnabled = !busy && poses != null;
            buttons["PoseImagePrevious"].IsEnabled = poseImageIndex > 0;
            buttons["PoseImageNext"].IsEnabled = poseImageIndex + 1 < poseImages.Length;
            buttons["PoseOpenPreview"].IsEnabled = !busy && poseAnimation != null && GetPoseBody() != null;
        }

        private async void RefreshPosePreview(PoseEntry pose, bool force = false)
        {
            if (!force && pose == previewPose) return;
            StopPosePreview();
            previewPose = pose;
            var assets = poses == null ? null : poses.Assets;
            poseAnimation = assets == null ? null : assets.Animation(pose);
            poseImageExact = false;
            poseImages = assets == null ? new string[0] : assets.Images(pose, out poseImageExact);
            poseImageIndex = 0;
            Find<TextBlock>("PosePreviewName").Text = pose == null ? "" : pose.Name + " · " + pose.Event;
            PoseAvailabilityHint();
            if (showPose3D && Find<FrameworkElement>("PosesPage").Visibility == Visibility.Visible) await LoadPoseModel();
            else ShowPoseImage();
        }

        private void PoseAvailabilityHint()
        {
            Find<TextBlock>("PoseAnimationHint").Text = previewPose == null ? "" : poseAnimation == null ?
                T("No matching HKX animation was found.", "Nessuna animazione HKX corrispondente trovata.") :
                GetPoseBody() == null ? T("Scan the library with an installed CBBE body to use the 3D preview.",
                    "Scansiona la libreria con un corpo CBBE installato per usare l’anteprima 3D.") :
                "";
            Find<TextBlock>("PoseAnimationHint").ToolTip = poseAnimation;
        }

        private void MovePoseImage(int step)
        {
            int nextIndex = poseImageIndex + step;
            if (nextIndex < 0 || nextIndex >= poseImages.Length) return;
            poseImageIndex = nextIndex;
            ShowPoseImage();
        }

        private void ShowPoseImage()
        {
            Find<Viewport3D>("PoseViewport").Visibility = Visibility.Collapsed;
            var image = Find<Image>("PoseImage");
            var placeholder = Find<TextBlock>("PoseImagePlaceholder");
            image.Source = null;
            placeholder.Visibility = Visibility.Visible;
            placeholder.Text = previewPose == null ? T("Select a pose.", "Seleziona una posa.") :
                T("This pack has no preview images.", "Il pacchetto non contiene immagini di anteprima.");
            Find<DockPanel>("PoseImageControls").Visibility = poseImages.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (poseImages.Length > 0)
            {
                var caption = Find<TextBlock>("PoseImageCaption");
                caption.Text = (poseImageExact ? T("Pose image", "Immagine della posa") : T("Pack gallery", "Galleria del pacchetto")) +
                    " · " + (poseImageIndex + 1) + "/" + poseImages.Length;
                caption.ToolTip = Path.GetFileName(poseImages[poseImageIndex]);
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(poseImages[poseImageIndex], UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 1200;
                    bitmap.EndInit();
                    image.Source = bitmap;
                    placeholder.Visibility = Visibility.Collapsed;
                }
                catch (Exception error)
                {
                    if (!(error is IOException || error is FormatException || error is NotSupportedException ||
                        error is ArgumentException || error is UnauthorizedAccessException)) throw;
                    placeholder.Text = T("This image could not be opened.", "Impossibile aprire questa immagine.");
                }
            }
            UpdatePoseButtons();
        }

        private void ResetPoseRig()
        {
            StopPosePreview();
            rigCatalog = null;
            rig = null;
        }

        private void StopPosePreview()
        {
            if (poseRequest != null) poseRequest.Cancel();
            poseRequest = null;
            activePose = null;
            if (poseTimer == null) return;
            PausePose();
            Find<DockPanel>("PoseTimeline").Visibility = Visibility.Collapsed;
            buttons["PoseExternalPreview"].Visibility = Visibility.Collapsed;
            poseViewport.ClearPose();
        }

        private async Task LoadPoseModel()
        {
            StopPosePreview();
            var current = catalog;
            var body = GetPoseBody();
            string animation = poseAnimation;
            if (body == null || animation == null) { ShowPoseImage(); PoseAvailabilityHint(); return; }
            var request = new CancellationTokenSource();
            poseRequest = request;
            var token = request.Token;
            var preset = Find<ListBox>("BodyPresets").SelectedItem as BodyPreset;
            bool lowWeight = Find<ComboBox>("BodyWeight").SelectedIndex == 1, textured = texturedPreview.IsChecked == true;
            if (rigCatalog != current)
            {
                rigCatalog = current;
                rig = Task.Run(() => new PoseRig(current));
            }
            var skeleton = rig;
            var message = Find<TextBlock>("PoseImagePlaceholder");
            message.Visibility = Visibility.Visible;
            message.Text = T("Loading pose…", "Caricamento posa…");
            Find<Image>("PoseImage").Source = null;
            Find<DockPanel>("PoseImageControls").Visibility = Visibility.Collapsed;
            Find<Viewport3D>("PoseViewport").Visibility = Visibility.Visible;
            Find<TextBlock>("PoseAnimationHint").Text = "";
            try
            {
                var model = await Task.Run(async () => {
                    var clip = new HkxClip(animation);
                    var geometry = await bodyCache.Get(current, body, preset, lowWeight, token).ConfigureAwait(false);
                    return (await skeleton.ConfigureAwait(false)).Attach(clip, geometry);
                }, token);
                if (token.IsCancellationRequested) return;
                await poseViewport.ShowPose(current, body, model, textured, token);
                if (token.IsCancellationRequested) return;
                activePose = model;
                var frame = Find<Slider>("PoseFrame");
                changingPoseFrame = true;
                frame.Maximum = Math.Max(1, model.Frames - 1);
                frame.Value = 0;
                pendingFrame = 0;
                changingPoseFrame = false;
                message.Visibility = Visibility.Collapsed;
                Find<DockPanel>("PoseTimeline").Visibility = model.Frames > 2 ? Visibility.Visible : Visibility.Collapsed;
                Find<TextBlock>("PoseAnimationHint").Text = T("Drag to rotate · scroll to zoom", "Trascina per ruotare · rotella per lo zoom");
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested) ShowPoseFailure(error);
            }
            finally
            {
                if (poseRequest == request) poseRequest = null;
                request.Dispose();
            }
        }

        private void ShowPoseFailure(Exception error)
        {
            StopPosePreview();
            ShowPoseImage();
            buttons["PoseExternalPreview"].Visibility = Visibility.Visible;
            var hint = Find<TextBlock>("PoseAnimationHint");
            hint.Text = T("Preview unavailable for this pose. In BodySlide, choose Animation → Load HKX… and paste the path.",
                "Anteprima non disponibile per questa posa. In BodySlide scegli Animation → Load HKX… e incolla il percorso.");
            hint.ToolTip = error.Message;
        }

        private void TogglePosePlayback()
        {
            if (poseTimer.IsEnabled) { PausePose(); return; }
            if (activePose == null || activePose.Frames < 3) return;
            startFrame = Find<Slider>("PoseFrame").Value;
            poseClock = Stopwatch.StartNew();
            poseTimer.Start();
            buttons["PosePlay"].Content = T("Pause", "Pausa");
        }

        private void PausePose()
        {
            poseTimer.Stop();
            buttons["PosePlay"].Content = T("Play", "Riproduci");
        }

        private void AdvancePose()
        {
            if (activePose == null) { PausePose(); return; }
            double steps = activePose.Frames - 1;
            double duration = Math.Max(0.05, activePose.Duration);
            Find<Slider>("PoseFrame").Value = (startFrame + poseClock.Elapsed.TotalSeconds * steps / duration) % steps;
        }

        private async Task ShowPoseFrame(double frame)
        {
            pendingFrame = frame;
            if (calculatingFrame || activePose == null) return;
            calculatingFrame = true;
            PoseRig.PoseModel model = null;
            try
            {
                while (activePose != null)
                {
                    model = activePose;
                    double requestedFrame = pendingFrame;
                    var points = await Task.Run(() => model.Sample(requestedFrame));
                    if (model == activePose) poseViewport.UpdatePoseFrame(points);
                    if (requestedFrame == pendingFrame && model == activePose) break;
                }
            }
            catch (Exception error) { if (activePose != null && model == activePose) ShowPoseFailure(error); }
            finally { calculatingFrame = false; }
        }

        private Piece GetPoseBody()
        {
            if (catalog == null) return null;
            var selected = Find<ListBox>("BodyProjects").SelectedItem as Piece;
            if (selected != null && !selected.Derived && catalog.ById.ContainsKey(selected.Id))
                return catalog.ById[selected.Id];
            return catalog.Pieces.Where(piece => !piece.Derived && Catalog.IsBodyProject(piece))
                .OrderByDescending(piece => Files.Comparer.Equals(piece.Name, "CBBE 3BBB Body Amazing")).FirstOrDefault();
        }

        private async Task OpenPosePreview()
        {
            var current = catalog;
            var body = GetPoseBody();
            if (busy || body == null || poseAnimation == null) return;
            if (!File.Exists(poseAnimation)) throw new FileNotFoundException(T("Reload the pose library.", "Aggiorna la libreria pose."), poseAnimation);
            var preset = Find<ListBox>("BodyPresets").SelectedItem as BodyPreset;
            bool lowWeight = Find<ComboBox>("BodyWeight").SelectedIndex == 1;
            Clipboard.SetText(poseAnimation);
            await RunBusy(T("Opening BodySlide preview…", "Apertura anteprima BodySlide…"), async () => {
                await Task.Run(() => Composer.PreviewPose(current, body, preset, lowWeight));
                SetStatus(T("BodySlide preview closed.", "Anteprima BodySlide chiusa."));
            });
        }

        private void AddPoseFavorites()
        {
            if (poses == null || busy) return;
            foreach (var entry in poseLibrary.SelectedItems.Cast<PoseEntry>())
                if (!poses.Favorites.Contains(entry.Raw))
                {
                    poses.Favorites.Add(entry.Raw);
                    posesDirty = true;
                }
            RefreshPoseFavorites();
        }

        private void RemovePoseFavorites()
        {
            if (poses == null || busy) return;
            var selected = poseFavorites.SelectedItems.Cast<PoseEntry>().Select(entry => entry.Raw).ToArray();
            foreach (string favorite in selected) poses.Favorites.Remove(favorite);
            posesDirty = posesDirty || selected.Length != 0;
            RefreshPoseFavorites();
        }

        private void SavePoseFavorites()
        {
            if (poses == null) return;
            var picker = new SaveFileDialog { FileName = "Favorites.json", DefaultExt = ".json", Filter = "Poser Hotkeys (Favorites.json)|Favorites.json" };
            if (poses.Destination != null) picker.InitialDirectory = Path.GetDirectoryName(poses.Destination);
            if (picker.ShowDialog(window) != true) return;
            if (!Files.Comparer.Equals(Path.GetFileName(picker.FileName), "Favorites.json"))
                throw new InvalidDataException(T("Use the file name Favorites.json.", "Usa il nome Favorites.json."));
            poses.Save(picker.FileName);
            posesDirty = false;
            RefreshPoseFavorites();
            SetStatus(T("Saved · ", "Salvato · ") + picker.FileName);
        }
    }
}
