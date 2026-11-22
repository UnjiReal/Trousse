using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SkyrimOutfit
{
    internal sealed class DressUpEquipment
    {
        internal readonly bool LoadOrderVerified;
        internal readonly bool ModDeployed;
        internal readonly bool ModEnabled;
        internal readonly List<string> Notices = new List<string>();
        internal readonly Dictionary<string, GameItem> Items = new Dictionary<string, GameItem>(Files.Comparer);
        internal readonly Dictionary<string, List<GameItem>> ByMesh = new Dictionary<string, List<GameItem>>(Files.Comparer);
        internal readonly HashSet<string> LightPlugins = new HashSet<string>(Files.Comparer);

        internal DressUpEquipment(string data, string[] enabled)
        {
            LoadOrderVerified = enabled != null;
            const string plugin = "Dress Up Lovers.esp";
            ModDeployed = File.Exists(Path.Combine(data, plugin));
            ModEnabled = enabled != null && enabled.Contains(plugin, Files.Comparer);
        }

        internal GameItem Find(DressUpForm form)
        {
            GameItem found;
            return Items.TryGetValue(form.Key, out found) ? found : null;
        }

        internal IEnumerable<GameItem> ForMesh(string path)
        {
            List<GameItem> found;
            return ByMesh.TryGetValue(GameRecords.MeshStem(path), out found) ? found : Enumerable.Empty<GameItem>();
        }

        internal DressUpForm Reference(GameItem item)
        {
            return new DressUpForm(item.Plugin, item.Form, LightPlugins.Contains(item.Plugin));
        }
    }

    internal sealed partial class GameRecords
    {
        internal static DressUpEquipment ReadDressUpEquipment(string data, string[] enabled)
        {
            var result = new DressUpEquipment(data, enabled);
            var deployed = Files.Children(data, "*", false).Select(Path.GetFileName)
                .Where(DressUpForm.IsPluginName).ToArray();
            string creationList = Path.Combine(Directory.GetParent(data).FullName, "Skyrim.ccc");
            string[] creations = new string[0];
            try
            {
                if (File.Exists(creationList))
                    creations = File.ReadLines(creationList).Select(line => line.Trim()).Where(DressUpForm.IsPluginName).ToArray();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            var order = Official.Concat(creations).Concat((IEnumerable<string>)enabled ?? deployed.OrderBy(file => file, Files.Comparer))
                .Distinct(Files.Comparer).Where(file => deployed.Contains(file, Files.Comparer)).ToArray();
            var records = new Dictionary<string, Record>(Files.Comparer);
            var strings = ReadStrings(data, result.Notices);
            foreach (string file in order)
                ReadPlugin(data, file, strings, records, result.Notices, RecordScope.OutfitEquipment, result.LightPlugins);
            foreach (var entry in records)
            {
                GameItem item = entry.Value.Item;
                if (item == null || item.Form == 0 || (result.LightPlugins.Contains(item.Plugin) && item.Form > 0xFFF)) continue;
                result.Items.Add(entry.Key, item);
                if (item.Weapon) continue;
                foreach (string link in ArmorLinks(entry.Value, records))
                {
                    Record addon;
                    if (!records.TryGetValue(link, out addon) || addon.Type != "ARMA") continue;
                    foreach (string mesh in addon.Meshes)
                    {
                        List<GameItem> items;
                        if (!result.ByMesh.TryGetValue(mesh, out items))
                            result.ByMesh.Add(mesh, items = new List<GameItem>());
                        if (!items.Contains(item)) items.Add(item);
                    }
                }
            }
            return result;
        }

        private static void ReadLooseEquipmentStrings(string data, string file, Dictionary<string, Dictionary<uint, string>> strings)
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            foreach (string language in new[] { "english", "italian" })
            {
                string name = stem + "_" + language + ".strings";
                string path = Path.Combine(data, "Strings", name);
                if (!strings.ContainsKey(name) && File.Exists(path)) strings.Add(name, ParseStrings(File.ReadAllBytes(path)));
            }
        }
    }
}
