using System;
using System.IO;
using System.Media;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace SkyrimOutfit
{
    internal sealed partial class App
    {
        private const string MusicResource = "BackgroundMusic.mp3", CrashResource = "CrashEffect.mp3", ExportResource = "PackageEffect.mp3";
        private readonly MediaPlayer music = new MediaPlayer { Volume = 0.25 };
        private readonly MediaPlayer effects = new MediaPlayer { Volume = 0.5 };
        private CheckBox musicEnabled, effectsEnabled;
        private string audioDirectory;
        private SystemSound effectFallback;

        private void ConfigureAudio()
        {
            musicEnabled = Find<CheckBox>("MusicEnabled");
            effectsEnabled = Find<CheckBox>("EffectsEnabled");
            musicEnabled.IsChecked = settings.MusicEnabled;
            effectsEnabled.IsChecked = settings.EffectsEnabled;
            musicEnabled.Checked += (s, e) => AudioChanged();
            musicEnabled.Unchecked += (s, e) => AudioChanged();
            effectsEnabled.Checked += (s, e) => AudioChanged();
            effectsEnabled.Unchecked += (s, e) => AudioChanged();
            music.MediaEnded += (s, e) => {
                if (musicEnabled.IsChecked != true || music.Source == null) return;
                music.Position = TimeSpan.Zero;
                music.Play();
            };
            music.MediaFailed += (s, e) => SetStatus(T("Background music could not be played.", "Impossibile riprodurre la musica di sottofondo."));
            effects.MediaFailed += (s, e) => { if (effectsEnabled.IsChecked == true && effectFallback != null) effectFallback.Play(); };
            window.Loaded += (s, e) => UpdateAudioPlayback();
            window.Closed += (s, e) => CloseAudio();
            Bind("BrowseMusic", () => ChooseAudio("BrowseMusic", path => settings.MusicFile = path));
            Bind("BrowseCrash", () => ChooseAudio("BrowseCrash", path => settings.CrashFile = path));
            Bind("BrowseExportSound", () => ChooseAudio("BrowseExportSound", path => settings.ExportSoundFile = path));
            Bind("ResetAudio", () => {
                settings.MusicFile = settings.CrashFile = settings.ExportSoundFile = null;
                AudioChanged();
            });
        }

        private void ChooseAudio(string button, Action<string> set)
        {
            var picker = new OpenFileDialog {
                Title = (string)Find<Button>(button).Content,
                Filter = "Audio (*.wav, *.mp3, *.m4a, *.wma)|*.wav;*.mp3;*.m4a;*.wma"
            };
            if (picker.ShowDialog(window) != true) return;
            set(picker.FileName);
            AudioChanged();
        }

        private void AudioChanged()
        {
            if (window.IsLoaded) SavePreferences();
            RefreshAudioLabels();
            UpdateAudioPlayback();
        }

        private void RefreshAudioLabels()
        {
            AudioLabel("MusicSelection", settings.MusicFile, MusicResource, "app background music.mp3");
            AudioLabel("CrashSelection", settings.CrashFile, CrashResource, "CRASH.mp3");
            AudioLabel("ExportSoundSelection", settings.ExportSoundFile, ExportResource, "PACKAGE COMPLETE.mp3");
        }

        private void AudioLabel(string control, string selected, string resource, string file)
        {
            var label = Find<TextBlock>(control);
            bool custom = File.Exists(selected);
            bool embedded = Array.IndexOf(Assembly.GetExecutingAssembly().GetManifestResourceNames(), resource) >= 0;
            label.Text = custom ? Path.GetFileName(selected) : embedded ? file + T(" · built in", " · incorporato")
                : T("Windows sound", "Suono di Windows");
            label.ToolTip = custom ? selected : null;
        }

        private void UpdateAudioPlayback()
        {
            if (effectsEnabled.IsChecked != true) effects.Close();
            if (!window.IsLoaded || musicEnabled.IsChecked != true) { music.Close(); return; }
            var source = AudioSource(settings.MusicFile, MusicResource);
            if (source == music.Source) return;
            if (source == null) { music.Close(); return; }
            music.Open(source);
            music.Play();
        }

        private void PlayCrashSound() { PlayAudioEffect(settings.CrashFile, CrashResource, SystemSounds.Hand); }
        private void PlayExportSound() { PlayAudioEffect(settings.ExportSoundFile, ExportResource, SystemSounds.Asterisk); }

        private void PlayAudioEffect(string selected, string resource, SystemSound fallback)
        {
            if (effectsEnabled.IsChecked != true) return;
            var source = AudioSource(selected, resource);
            if (source == null) { fallback.Play(); return; }
            effectFallback = fallback;
            if (source != effects.Source) effects.Open(source);
            effects.Position = TimeSpan.Zero;
            effects.Play();
        }

        private Uri AudioSource(string selected, string resource)
        {
            try
            {
                if (File.Exists(selected) && new FileInfo(selected).Length > 0) return new Uri(Path.GetFullPath(selected));
                using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                {
                    if (input == null) return null;
                    if (audioDirectory == null)
                        audioDirectory = Path.Combine(Path.GetTempPath(), "Trousse-audio-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(audioDirectory);
                    string path = Path.Combine(audioDirectory, resource);
                    if (!File.Exists(path) || new FileInfo(path).Length != input.Length)
                        using (var output = File.Create(path)) input.CopyTo(output);
                    return new Uri(path);
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private void CloseAudio()
        {
            music.Close();
            effects.Close();
            if (audioDirectory == null) return;
            try
            {
                foreach (string file in Directory.EnumerateFiles(audioDirectory)) File.Delete(file);
                Directory.Delete(audioDirectory);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
