using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace SkyrimOutfit
{
    internal sealed partial class App
    {
        private LiveViewport bodyPreview;
        private BodyPreviewCache bodyCache;
        private CancellationTokenSource bodyPreviewRequest;
        private bool updatingBodies;

        private void ConfigureToolsPages()
        {
            bodyPreview = new LiveViewport(Find<Viewport3D>("BodyViewport"), Find<Border>("BodyPreviewSurface"));
            bodyCache = new BodyPreviewCache(Preferences.BodyCachePath);
            Bind("BodiesNav", () => { Switch("BodiesPage"); RefreshBodies(); });
            ConfigureOar();
            Bind("AddBody", () => {
                var body = Find<ListBox>("BodyProjects").SelectedItem as Piece;
                if (body == null || catalog == null || !catalog.ById.ContainsKey(body.Id)) return;
                AddDraftParts(new[] { body });
                Switch("HomePage");
            });
            Find<TextBox>("BodiesSearch").TextChanged += (sender, args) => RefreshBodies();
            Find<TextBox>("BodyPresetSearch").TextChanged += (sender, args) => RefreshBodies();
            Find<ListBox>("BodyProjects").SelectionChanged += (sender, args) => {
                if (updatingBodies) return;
                UpdateToolsButtons();
                RefreshBodyPreview();
            };
            Find<ListBox>("BodyPresets").SelectionChanged += (sender, args) => {
                if (updatingBodies) return;
                RefreshPresetDetails();
                RefreshBodyPreview();
            };
            Bind("BodyResetPreset", () => Find<ListBox>("BodyPresets").SelectedIndex = -1);
            var weight = Find<ComboBox>("BodyWeight");
            weight.SelectionChanged += (sender, args) => { if (!updatingBodies) RefreshBodyPreview(); };
            window.Closed += (sender, args) => {
                CancelBodyPreview();
                bodyCache.CancelBuilds();
            };
        }

        private void CancelBodyPreview() { if (bodyPreviewRequest != null) bodyPreviewRequest.Cancel(); }

        private async void RefreshBodyPreview()
        {
            if (Find<FrameworkElement>("BodiesPage").Visibility != Visibility.Visible) return;
            CancelBodyPreview();
            var request = new CancellationTokenSource();
            bodyPreviewRequest = request;
            var current = catalog;
            var selected = Find<ListBox>("BodyProjects").SelectedItem as Piece;
            var preset = Find<ListBox>("BodyPresets").SelectedItem as BodyPreset;
            var weight = Find<ComboBox>("BodyWeight");
            bool lowWeight = weight.SelectedIndex == 1;
            weight.IsEnabled = selected != null && selected.Weighted;
            buttons["BodyResetPreset"].IsEnabled = !busy && preset != null;
            var hint = Find<TextBlock>("BodyPreviewHint");
            bool hasBody = current != null && selected != null;
            hint.Visibility = Visibility.Visible;
            hint.ToolTip = null;
            hint.Text = hasBody ? T("Loading body preview…", "Caricamento anteprima corpo…") : T("Select a body project.", "Seleziona un progetto corpo.");
            Find<TextBlock>("BodyPreviewControls").Text = !hasBody ? "" : 
                (preset == null ? T("Project defaults", "Valori del progetto") : preset.Name) + " · " +
                T("Drag to rotate · scroll to zoom", "Trascina per ruotare · rotella per lo zoom");
            Exception failure = null;
            try
            {
                await bodyPreview.RenderBody(bodyCache, current, selected, preset, lowWeight, texturedPreview.IsChecked == true, request.Token,
                    (shown, unavailable, missing) => {
                        hint.Visibility = shown > 0 ? Visibility.Collapsed : Visibility.Visible;
                        if (shown == 0 && hasBody) hint.Text = T("Preview unavailable for this body.", "Anteprima non disponibile per questo corpo.");
                    });
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { failure = error; }
            finally
            {
                window.Dispatcher.Invoke(new Action(() => {
                    if (bodyPreviewRequest == request)
                    {
                        bodyPreviewRequest = null;
                        if (failure != null && !request.IsCancellationRequested)
                        {
                            hint.Text = T("Preview unavailable for this body.", "Anteprima non disponibile per questo corpo.");
                            hint.ToolTip = failure.Message;
                        }
                    }
                    request.Dispose();
                }));
            }
        }

        private void LocalizeBodyWeight()
        {
            var weight = Find<ComboBox>("BodyWeight");
            int index = Math.Max(0, weight.SelectedIndex);
            updatingBodies = true;
            weight.ItemsSource = new[] { T("High weight (100)", "Peso alto (100)"), T("Low weight (0)", "Peso basso (0)") };
            weight.SelectedIndex = index;
            updatingBodies = false;
        }

        private void UpdateToolsButtons()
        {
            buttons["AddBody"].IsEnabled = !busy && catalog != null && Find<ListBox>("BodyProjects").SelectedItem != null;
            buttons["BodyResetPreset"].IsEnabled = !busy && Find<ListBox>("BodyPresets").SelectedItem != null;
            UpdateOarButtons();
        }

        private void RefreshBodies()
        {
            if (Find<FrameworkElement>("BodiesPage").Visibility != Visibility.Visible) return;
            var projects = Find<ListBox>("BodyProjects");
            var presets = Find<ListBox>("BodyPresets");
            var selectedBody = projects.SelectedItem as Piece;
            var selectedPreset = presets.SelectedItem as BodyPreset;
            string query = Find<TextBox>("BodiesSearch").Text;
            string presetQuery = Find<TextBox>("BodyPresetSearch").Text;
            var bodies = catalog == null ? new Piece[0] : catalog.Pieces.Where(Catalog.IsBodyProject)
                .Where(piece => Match(query, piece.Name, piece.Mod))
                .OrderBy(piece => piece.Name, Files.Comparer).ThenBy(piece => piece.Mod, Files.Comparer).ToArray();
            var matches = catalog == null ? new BodyPreset[0] : catalog.Presets
                .Where(preset => Match(presetQuery, preset.Name, preset.Groups, preset.Mod, preset.BodySet))
                .OrderBy(preset => preset.Name, Files.Comparer).ThenBy(preset => preset.Mod, Files.Comparer).ToArray();
            updatingBodies = true;
            projects.ItemsSource = bodies;
            projects.SelectedItem = bodies.Contains(selectedBody) ? selectedBody : bodies.FirstOrDefault();
            presets.ItemsSource = matches;
            presets.SelectedItem = matches.Contains(selectedPreset) ? selectedPreset : null;
            updatingBodies = false;
            RefreshPresetDetails();
            UpdateToolsButtons();
            RefreshBodyPreview();
        }

        private void RefreshPresetDetails()
        {
            var preset = Find<ListBox>("BodyPresets").SelectedItem as BodyPreset;
            var details = Find<TextBlock>("PresetDetails");
            details.Text = preset == null ? "" : String.Join(Environment.NewLine, new[] {
                preset.BodySet, preset.Groups, System.IO.Path.GetFileName(preset.FilePath)
            }.Where(value => !String.IsNullOrWhiteSpace(value)));
            details.ToolTip = preset == null ? null : preset.FilePath;
            Find<ListBox>("PresetSliders").ItemsSource = preset == null ? null : preset.Sliders;
        }

    }
}
