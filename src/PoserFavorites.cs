using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace SkyrimOutfit
{
    internal sealed class PoseEntry
    {
        internal string Raw, PackKey;
        public string Name { get; internal set; }
        public string Event { get; internal set; }
        public string Pack { get; internal set; }

        internal static PoseEntry Parse(string raw, string pack, string packKey = null)
        {
            int split = raw.LastIndexOf('|');
            return new PoseEntry {
                Raw = raw, Pack = pack, PackKey = packKey, Name = split > 0 ? raw.Substring(0, split) : raw,
                Event = split >= 0 ? raw.Substring(split + 1) : raw
            };
        }
    }

    internal sealed class PoserFavorites
    {
        internal const string DataFolder = @"SKSE\Plugins\PoserHotKeys\PoserData";
        private const string FileName = "Favorites.json";
        private Dictionary<string, object> config;
        private byte[] sourceBytes;
        internal string Destination { get; private set; }
        internal PoseAssets Assets { get; private set; }
        internal readonly List<PoseEntry> Poses = new List<PoseEntry>();
        internal readonly List<string> Favorites = new List<string>();
        internal readonly List<string> Notices = new List<string>();

        internal static PoserFavorites Scan(string staging, string data)
        {
            string deployed = String.IsNullOrEmpty(data) ? null : Path.Combine(data, DataFolder);
            var mods = String.IsNullOrWhiteSpace(staging) ? new string[0] : Files.Children(staging, "*", true)
                .SelectMany(mod => new[] { mod, Path.Combine(mod, "Data") }).Where(mod => Directory.Exists(Path.Combine(mod, DataFolder))).ToArray();
            string deployedFavorites = deployed == null ? null : Path.Combine(deployed, FileName);
            var baseMods = mods.Where(mod => File.Exists(Path.Combine(mod, "Poser Hotkeys.esp")) ||
                Regex.IsMatch(Path.GetFileName(mod), @"(?:^|\D)17743(?:\D|$)")).ToArray();
            string primary = baseMods.Length == 1 ? baseMods[0] : baseMods.FirstOrDefault(mod =>
                File.Exists(deployedFavorites) && File.Exists(Path.Combine(mod, DataFolder, FileName)) &&
                File.ReadAllBytes(deployedFavorites).SequenceEqual(File.ReadAllBytes(Path.Combine(mod, DataFolder, FileName))));
            string target = primary == null ? null : Path.Combine(primary, DataFolder, FileName);
            if (target == null && (File.Exists(deployedFavorites) || deployed != null && Directory.Exists(deployed)))
                target = deployedFavorites;
            var result = new PoserFavorites(File.Exists(target) ? target : File.Exists(deployedFavorites) ? deployedFavorites : null, target);
            var seen = new HashSet<string>(Files.Comparer);
            var roots = new[] { deployed }.Concat(mods.Select(mod => Path.Combine(mod, DataFolder)));
            foreach (string folder in roots.Where(root => root != null).Distinct(Files.Comparer))
                foreach (string file in Files.Children(folder, "*.json", false))
                {
                    string filename = Path.GetFileName(file);
                    if (Files.Comparer.Equals(filename, FileName) || !seen.Add(filename)) continue;
                    try { result.ReadPack(file); }
                    catch (Exception error)
                    {
                        if (!(error is IOException || error is ArgumentException || error is InvalidOperationException || error is UnauthorizedAccessException)) throw;
                        result.Notices.Add(filename + " · " + error.Message);
                    }
                }
            result.Assets = new PoseAssets(data, mods);
            return result;
        }

        private PoserFavorites(string source, string destination)
        {
            Destination = destination;
            sourceBytes = File.Exists(destination) ? File.ReadAllBytes(destination) : null;
            config = source == null ? new Dictionary<string, object>() : ReadObject(File.ReadAllText(source));
            var list = Map(config, "stringList", false);
            if (list != null) Favorites.AddRange(Values(list, "favorites"));
        }

        private void ReadPack(string path)
        {
            var pack = ReadObject(File.ReadAllText(path));
            var lists = Map(pack, "stringList", false);
            if (lists == null) return;
            var metadata = Map(pack, "string", false);
            object title;
            string name = metadata != null && metadata.TryGetValue("name", out title) && title is string
                ? (string)title : Path.GetFileNameWithoutExtension(path);
            string keyName = Path.GetFileNameWithoutExtension(path);
            foreach (var group in lists.Keys.Where(key => !Files.Comparer.Equals(key, "packnames")))
            {
                string label = Files.Comparer.Equals(name, group) ? name : name + " · " + group;
                Poses.AddRange(Values(lists, group).Select(value => PoseEntry.Parse(value, label, keyName)));
            }
        }

        private static Dictionary<string, object> ReadObject(string text)
        {
            var result = Json().Deserialize<Dictionary<string, object>>(text);
            if (result == null) throw new InvalidDataException("Empty Poser Hotkeys configuration.");
            return result;
        }

        private static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 12000000 }; }

        private static Dictionary<string, object> Map(Dictionary<string, object> root, string key, bool create)
        {
            object value;
            if (root.TryGetValue(key, out value) && value is Dictionary<string, object>) return (Dictionary<string, object>)value;
            if (!create) return null;
            var nested = new Dictionary<string, object>();
            root[key] = nested;
            return nested;
        }

        private static IEnumerable<string> Values(Dictionary<string, object> root, string key)
        {
            object raw;
            var values = root.TryGetValue(key, out raw) ? raw as IList : null;
            return values == null ? Enumerable.Empty<string>() : values.OfType<string>().Where(value => !String.IsNullOrWhiteSpace(value));
        }

        internal void Save(string path)
        {
            if (Files.Comparer.Equals(Path.GetFullPath(path), Destination)
                && (File.Exists(path) != (sourceBytes != null) || sourceBytes != null && !sourceBytes.SequenceEqual(File.ReadAllBytes(path))))
                throw new IOException("Favorites.json changed on disk. Reload the pose library before overwriting it.");
            var output = ReadObject(Json().Serialize(config));
            var lists = Map(output, "stringList", true);
            string[] old = Values(lists, "favorites").ToArray();
            var integers = Map(output, "intList", true);
            object rawIndex;
            var oldIndexes = integers.TryGetValue("selectedpose", out rawIndex) ? rawIndex as IList : null;
            int previous = oldIndexes != null && oldIndexes.Count != 0 && oldIndexes[0] is int ? (int)oldIndexes[0] : 0;
            int selected = previous >= 0 && previous < old.Length ? Favorites.IndexOf(old[previous]) : -1;
            lists["favorites"] = Favorites.ToArray();
            lists["packnames"] = new[] { "favorites" };
            integers["selectedpose"] = new[] { Math.Max(0, selected) };
            Map(output, "int", true)["selectedpack"] = 0;
            Map(output, "string", true)["name"] = "Favorites";
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, Json().Serialize(output), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                Destination = Path.GetFullPath(path);
                config = output;
                sourceBytes = File.ReadAllBytes(path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
