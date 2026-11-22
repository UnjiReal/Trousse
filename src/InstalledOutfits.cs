using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SkyrimOutfit
{
    internal enum RaceInstallation { Missing, Staged, Disabled, Unverified, Active }

    internal sealed class InstalledOutfits
    {
        internal static readonly string[] Races = { "nord", "argonian", "breton", "dark elf", "high elf", "imperial",
            "khajiit", "orc", "redguard", "wood elf", "elder" };
        private static readonly Regex UniqueBodies = new Regex(@"unique[ _-]*bodies[ _-]*by[ _-]*race", RegexOptions.IgnoreCase);
        private static readonly Regex PluginName = new Regex(@"^[^\\/:]+\.(esm|esp|esl)$", RegexOptions.IgnoreCase);
        private readonly Dictionary<string, Bsa.Index> indices = new Dictionary<string, Bsa.Index>(Files.Comparer);
        private readonly GameRecords.RacialMeshes racial;
        private readonly string[] meshArchives;
        internal IEnumerable<string> Archives { get { return meshArchives; } }
        internal readonly RaceInstallation State;
        internal readonly string DataRoot;
        internal readonly string Warning;
        internal bool CanPreviewRace { get { return racial != null; } }
        internal readonly OutfitProvenance Provenance;

        internal InstalledOutfits(string staging, string data, string pluginList, string bodyRoot = null)
        {
            DataRoot = data;
            string[] enabled = ReadEnabled(pluginList);
            var plugins = Files.Children(data, "*", false).Select(Path.GetFileName)
                .Where(file => PluginName.IsMatch(file) && UniqueBodies.IsMatch(file)).ToArray();
            var active = enabled == null ? new string[0] : enabled.Where(file => plugins.Contains(file, Files.Comparer)).ToArray();
            var deployed = plugins.Except(active, Files.Comparer)
                .OrderBy(file => !file.EndsWith(".esm", StringComparison.OrdinalIgnoreCase)).Concat(active).ToArray();

            if (plugins.Length == 0)
            {
                bool staged = Files.Children(staging, "*", true).Any(folder => UniqueBodies.IsMatch(Path.GetFileName(folder)));
                State = staged ? RaceInstallation.Staged : RaceInstallation.Missing;
            }
            else if (enabled == null) State = RaceInstallation.Unverified;
            else State = active.Any(file => !file.EndsWith(".esm", StringComparison.OrdinalIgnoreCase)) ? RaceInstallation.Active : RaceInstallation.Disabled;

            var notices = new List<string>();
            if (deployed.Length != 0)
                racial = new GameRecords.RacialMeshes(data, deployed, notices);
            Warning = notices.FirstOrDefault();

            string[] archives = Files.Children(data, "*.bsa", false).ToArray();
            var pluginsForArchives = (enabled ?? new string[0]).Concat(deployed).Distinct(Files.Comparer)
                .Reverse().Select(Path.GetFileNameWithoutExtension);
            meshArchives = pluginsForArchives.SelectMany(plugin => archives.Where(file => BelongsTo(file, plugin)))
                .Concat(archives.Where(file => Path.GetFileName(file).StartsWith("Skyrim - Meshes", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(file => file, Files.Comparer)).Distinct(Files.Comparer).ToArray();
            Provenance = new OutfitProvenance(staging, data, bodyRoot);
        }

        internal static string PluginListPath(string dataRoot)
        {
            string game = Directory.GetParent(dataRoot.TrimEnd('\\', '/')).FullName;
            var badge = GameBadge.Detect(game, null);
            string profile = badge.Edition == SkyrimEdition.Vr ? "Skyrim VR" :
                badge.Store == SkyrimStore.Gog || File.Exists(Path.Combine(game, "GalaxyPeer.dll")) ?
                    "Skyrim Special Edition GOG" : "Skyrim Special Edition";
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), profile, "plugins.txt");
        }

        internal static string[] ReadEnabled(string path)
        {
            try
            {
                string[] plugins = File.ReadLines(path).Select(line => line.Trim().TrimStart('\uFEFF')).Where(line => line.StartsWith("*", StringComparison.Ordinal))
                    .Select(line => line.Substring(1).Trim()).Where(file => PluginName.IsMatch(file)).ToArray();
                return plugins.Length == 0 ? null : plugins;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static bool BelongsTo(string archive, string plugin)
        {
            string name = Path.GetFileNameWithoutExtension(archive);
            return Files.Comparer.Equals(name, plugin) || name.StartsWith(plugin + " - ", StringComparison.OrdinalIgnoreCase);
        }

        internal IEnumerable<string> Paths(Target target, string race)
        {
            if (race == null) return new[] { target.Output };
            if (racial == null) return new string[0];
            string[] models = racial.For(target, race);
            if (models.Length != 0) return models;
            string path = "meshes\\" + race + "\\" + target.Output.Substring("meshes\\".Length);
            string full = Files.PathIn(DataRoot, path);
            return File.Exists(full + ".nif") || File.Exists(full + "_1.nif") || File.Exists(full + "_0.nif")
                ? new[] { path } : models;
        }

        internal InstalledMesh Find(string stem, bool weighted, bool lowWeight)
        {
            string first = stem + (lowWeight ? "_0.nif" : "_1.nif");
            string second = stem + (lowWeight ? "_1.nif" : "_0.nif");
            string single = stem + ".nif";
            var candidates = weighted ? new[] { first, second, single } : new[] { single, first, second };
            foreach (string relative in candidates)
            {
                string path = Files.PathIn(DataRoot, relative);
                if (File.Exists(path)) return new InstalledMesh(relative, path, null, null);
                foreach (string archive in meshArchives)
                {
                    Bsa.Index index = Index(archive);
                    if (index != null && index.Entries.ContainsKey(relative)) return new InstalledMesh(relative, null, archive, index);
                }
            }
            return null;
        }

        private Bsa.Index Index(string archive)
        {
            lock (indices)
            {
                Bsa.Index index;
                if (indices.TryGetValue(archive, out index)) return index;
                try { index = Bsa.LoadIndex(archive); }
                catch (IOException) { index = null; }
                catch (UnauthorizedAccessException) { index = null; }
                indices.Add(archive, index);
                return index;
            }
        }
    }

    internal sealed class InstalledMesh
    {
        internal readonly string Relative, Path, Archive;
        private readonly Bsa.Index index;

        internal InstalledMesh(string relative, string path, string archive, Bsa.Index index)
        {
            Relative = relative;
            Path = path;
            Archive = archive;
            this.index = index;
        }

        internal Nif Read()
        {
            if (Path != null) return new Nif(Path, true);
            using (var input = index.Open(Archive, Relative))
            using (var buffer = new MemoryStream())
            {
                if (input == null) throw new FileNotFoundException(Relative);
                input.CopyTo(buffer);
                return new Nif(Relative, buffer.ToArray(), true);
            }
        }
    }
}
