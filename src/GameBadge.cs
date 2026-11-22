using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace SkyrimOutfit
{
    internal enum SkyrimEdition { Unknown, Classic, Special, Anniversary, Vr }
    internal enum SkyrimStore { Unknown, Steam, Gog }

    internal sealed class GameBadge
    {
        internal SkyrimEdition Edition;
        internal SkyrimStore Store;

        internal static GameBadge Detect(string root, string editionOverride)
        {
            var result = new GameBadge();
            if (!Directory.Exists(root)) return result;
            bool vr = File.Exists(Path.Combine(root, "SkyrimVR.exe"));
            bool special = File.Exists(Path.Combine(root, "SkyrimSE.exe"));
            bool classic = File.Exists(Path.Combine(root, "TESV.exe"));
            if (!(vr || special || classic)) return result;
            string[] receipts = Files.Children(root, "goggame-*.info", false).ToArray();
            bool gog = receipts.Length != 0 || File.Exists(Path.Combine(root, "GalaxyPeer64.dll"));
            if (gog) result.Store = SkyrimStore.Gog;
            else if (File.Exists(Path.Combine(root, "steam_api64.dll")) || File.Exists(Path.Combine(root, "steam_api.dll")) ||
                Regex.IsMatch(root, @"(?:^|[\\/])steamapps[\\/]", RegexOptions.IgnoreCase)) result.Store = SkyrimStore.Steam;
            result.Edition = vr ? SkyrimEdition.Vr : special ? SkyrimEdition.Special : SkyrimEdition.Classic;
            if (result.Edition == SkyrimEdition.Special && (editionOverride == "anniversary" ||
                (editionOverride != "special" && HasAnniversary(root, receipts)))) result.Edition = SkyrimEdition.Anniversary;
            return result;
        }

        private static bool HasAnniversary(string root, IEnumerable<string> receipts)
        {
            foreach (string file in receipts)
            {
                try
                {
                    if (new FileInfo(file).Length < 200000 && Regex.IsMatch(File.ReadAllText(file),
                        "\"name\"\\s*:\\s*\"[^\"]*Skyrim[^\"]*Anniversary", RegexOptions.IgnoreCase)) return true;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            string list = Path.Combine(root, "Skyrim.ccc");
            try
            {
                if (!File.Exists(list)) return false;
                var items = File.ReadAllLines(list).Select(line => line.Trim()).Where(line =>
                    Regex.IsMatch(line, @"^cc[\w-]+\.(esm|esl)$", RegexOptions.IgnoreCase)).Distinct(Files.Comparer).ToArray();
                return items.Length >= 65 && items.Count(item => File.Exists(Path.Combine(root, "Data", item))) >= 65;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    internal sealed partial class App
    {
        private void RefreshGameBadges()
        {
            string game = Environment.GetEnvironmentVariable("TROUSSE_SKYRIM_PATH");
            try
            {
                string body = bodyPath.Text.Trim().Trim('"');
                if (Directory.Exists(body)) game = Directory.GetParent(Catalog.GameData(body).TrimEnd('\\', '/')).FullName;
            }
            catch (IOException) { }
            catch (System.Xml.XmlException) { }
            catch (UnauthorizedAccessException) { }
            GameBadge badge;
            try { badge = GameBadge.Detect(game, settings.BadgeEdition); }
            catch (IOException) { badge = new GameBadge(); }
            catch (UnauthorizedAccessException) { badge = new GameBadge(); }

            SetBadge("StoreBadge", badge.Store == SkyrimStore.Gog ? "GogLogo.png" : badge.Store == SkyrimStore.Steam ? "SteamLogo.png" : null,
                badge.Store == SkyrimStore.Gog ? "GOG" : "Steam");
            string artwork = badge.Edition == SkyrimEdition.Vr ? "SkyrimVr.png" : badge.Edition == SkyrimEdition.Anniversary ? "SkyrimAnniversary.png"
                : badge.Edition != SkyrimEdition.Unknown ? "SkyrimStandard.png" : null;
            string title = badge.Edition == SkyrimEdition.Classic ? "Skyrim" : badge.Edition == SkyrimEdition.Vr ? "Skyrim VR"
                : badge.Edition == SkyrimEdition.Anniversary ? "Skyrim Anniversary Edition" : "Skyrim Special Edition";
            SetBadge("SkyrimBadge", artwork, title);
        }

        private void SetBadge(string control, string resource, string tooltip)
        {
            var image = Find<Image>(control);
            using (var stream = resource == null ? null : Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                image.Source = stream == null ? null : ImageFromStream(stream);
                image.Visibility = image.Source == null ? Visibility.Collapsed : Visibility.Visible;
                image.ToolTip = tooltip;
                System.Windows.Automation.AutomationProperties.SetName(image, tooltip);
            }
        }
    }
}
