using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace SkyrimOutfit
{
    internal sealed class GameItem
    {
        internal string Plugin, EditorId, English, Italian;
        internal bool Weapon;
        internal uint Form;
        internal uint Biped;
        internal readonly HashSet<string> Outfits = new HashSet<string>(Files.Comparer);
        internal readonly Dictionary<string, HashSet<string>> TextureVariants = new Dictionary<string, HashSet<string>>(Files.Comparer);
        public string RuleId { get { return "0x" + Form.ToString("X6") + "~" + Plugin; } }
        public string ConsoleId { get { return GameRecords.ConsoleId(Plugin, Form); } }
        internal string Name(bool italian) { return (italian ? Italian ?? English : English) ?? GameRecords.Humanize(EditorId); }
    }

    internal sealed partial class GameRecords
    {
        private static readonly string[] Official = { "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm" };
        private readonly Dictionary<string, List<GameItem>> byMesh = new Dictionary<string, List<GameItem>>(Files.Comparer);
        private readonly Dictionary<string, string> addonNames = new Dictionary<string, string>(Files.Comparer);
        private readonly HashSet<string> swapped = new HashSet<string>(Files.Comparer);
        private readonly Dictionary<string, string> views = new Dictionary<string, string>(Files.Comparer);
        internal readonly HashSet<string> FirstPersonMeshes = new HashSet<string>(Files.Comparer);
        internal readonly HashSet<string> ThirdPersonMeshes = new HashSet<string>(Files.Comparer);
        private readonly Dictionary<string, HashSet<int>> slots = new Dictionary<string, HashSet<int>>(Files.Comparer);
        private readonly Dictionary<string, List<ArmorAddon>> addons = new Dictionary<string, List<ArmorAddon>>(Files.Comparer);

        internal static string ConsoleId(string plugin, uint localForm)
        {
            int index = Array.FindIndex(Official, file => Files.Comparer.Equals(file, plugin));
            return (index < 0 ? "XX" : index.ToString("X2")) + localForm.ToString("X6");
        }

        internal static GameRecords Read(string data, List<string> notices)
        {
            var result = new GameRecords();
            var records = new Dictionary<string, Record>(Files.Comparer);
            var strings = ReadStrings(data, notices);
            foreach (string file in Official)
                ReadPlugin(data, file, strings, records, notices, RecordScope.Vanilla);
            var items = records.Where(record => record.Value.Item != null)
                .ToDictionary(record => record.Key, record => record.Value.Item, Files.Comparer);
            foreach (var outfit in records.Values.Where(record => record.Type == "OTFT"))
                foreach (string key in Expand(outfit.Links, records, new HashSet<string>(Files.Comparer)))
                {
                    GameItem item;
                    if (items.TryGetValue(key, out item)) item.Outfits.Add(Humanize(outfit.Editor));
                }
            foreach (var record in records.Values.Where(entry => entry.Type == "ARMA"))
            {
                foreach (var model in record.Addon.Models)
                {
                    var paths = model.Key == "MOD4" || model.Key == "MOD5" ? result.FirstPersonMeshes : result.ThirdPersonMeshes;
                    paths.Add(model.Value);
                }
                foreach (string mesh in record.Meshes)
                {
                    List<ArmorAddon> meshAddons;
                    if (!result.addons.TryGetValue(mesh, out meshAddons)) result.addons.Add(mesh, meshAddons = new List<ArmorAddon>());
                    meshAddons.Add(record.Addon);
                    result.addonNames[mesh] = Humanize(record.Editor);
                    if (record.HasSwap) result.swapped.Add(mesh);
                    if (!result.views.ContainsKey(mesh)) result.views.Add(mesh, record.Views[mesh]);
                    else if (result.views[mesh] != record.Views[mesh]) result.views[mesh] = "Shared";
                    HashSet<int> bits;
                    if (!result.slots.TryGetValue(mesh, out bits)) result.slots.Add(mesh, bits = new HashSet<int>());
                    for (int bit = 0; bit < 32; bit++) if ((record.Biped & (1U << bit)) != 0) bits.Add(30 + bit);
                }
            }
            foreach (var record in records.Values.Where(entry => entry.Item != null))
                foreach (string key in ArmorLinks(record, records))
                {
                    Record addon;
                    if (!records.TryGetValue(key, out addon) || addon.Addon == null) continue;
                    addon.Addon.Users.Add(record.Item.RuleId);
                    foreach (string mesh in addon.Meshes)
                    {
                        List<GameItem> attached;
                        if (!result.byMesh.TryGetValue(mesh, out attached)) result.byMesh.Add(mesh, attached = new List<GameItem>());
                        if (!attached.Contains(record.Item)) attached.Add(record.Item);
                        HashSet<string> textures;
                        var swaps = addon.Textures.Where(group => Files.Comparer.Equals(group.Key, mesh))
                            .SelectMany(group => group.Value).Where(records.ContainsKey)
                            .Select(form => records[form].Editor).Where(name => !String.IsNullOrWhiteSpace(name)).ToArray();
                        if (swaps.Length == 0) continue;
                        if (!record.Item.TextureVariants.TryGetValue(mesh, out textures))
                            record.Item.TextureVariants.Add(mesh, textures = new HashSet<string>(Files.Comparer));
                        textures.UnionWith(swaps);
                    }
                }
            return result;
        }

        internal IEnumerable<ArmorAddon> Addons(string output)
        {
            List<ArmorAddon> matches;
            return addons.TryGetValue(MeshStem(output), out matches) ? matches : Enumerable.Empty<ArmorAddon>();
        }

        private enum RecordScope { Vanilla, Armor, OutfitEquipment }

        private static void ReadPlugin(string data, string file, Dictionary<string, Dictionary<uint, string>> strings,
            Dictionary<string, Record> records, List<string> notices, RecordScope scope, ISet<string> lightPlugins = null)
        {
            string path = Path.Combine(data, file);
            if (!File.Exists(path)) return;
            try
            {
                using (var reader = new BinaryReader(File.OpenRead(path)))
                {
                    if (Tag(reader) != "TES4") throw new IOException("Invalid TES4 header");
                    int length = reader.ReadInt32();
                    uint flags = reader.ReadUInt32();
                    bool localized = (flags & 0x80) != 0;
                    if (lightPlugins != null && ((flags & 0x200) != 0 || file.EndsWith(".esl", StringComparison.OrdinalIgnoreCase)))
                        lightPlugins.Add(file);
                    if (localized && scope == RecordScope.OutfitEquipment)
                        ReadLooseEquipmentStrings(data, file, strings);
                    reader.ReadBytes(12);
                    if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
                        throw new IOException("Invalid TES4 header size");
                    string[] masters = Parts(reader.ReadBytes(length)).Where(part => part.Tag == "MAST")
                        .Select(part => Text(part.Data)).Concat(new[] { file }).ToArray();
                    while (reader.BaseStream.Position + 24 <= reader.BaseStream.Length)
                    {
                        long start = reader.BaseStream.Position;
                        string group = Tag(reader);
                        int size = reader.ReadInt32();
                        string type = Tag(reader);
                        reader.ReadBytes(12);
                        long end = start + (group == "GRUP" ? size : size + 24L);
                        if (end < reader.BaseStream.Position || end > reader.BaseStream.Length) throw new IOException("Invalid TES record size");
                        if (group == "GRUP" && (type == "ARMA" || type == "ARMO" ||
                            (scope == RecordScope.Vanilla && (type == "OTFT" || type == "LVLI" || type == "TXST")) ||
                            (scope == RecordScope.OutfitEquipment && type == "WEAP")))
                            ReadGroup(reader, end, masters, localized, strings, records, scope);
                        reader.BaseStream.Position = end;
                    }
                }
            }
            catch (IOException error) { notices.Add(file + " — " + error.Message); }
            catch (UnauthorizedAccessException error) { notices.Add(file + " — " + error.Message); }
        }

        internal sealed class RacialMeshes
        {
            private readonly Dictionary<string, Record> records = new Dictionary<string, Record>(Files.Comparer);
            private readonly HashSet<string> knownModels;

            internal RacialMeshes(string data, IEnumerable<string> plugins, List<string> notices)
            {
                var strings = new Dictionary<string, Dictionary<uint, string>>(Files.Comparer);
                foreach (string plugin in plugins) ReadPlugin(data, plugin, strings, records, notices, RecordScope.Armor);
                knownModels = new HashSet<string>(records.Values.SelectMany(record => record.Meshes), Files.Comparer);
            }

            internal string[] For(Target target, string race)
            {
                string prefix = "meshes\\" + race + "\\";
                string direct = prefix + target.Output.Substring("meshes\\".Length);
                if (knownModels.Contains(direct)) return new[] { direct };

                uint slots = 0;
                foreach (int slot in target.Slots) slots |= 1U << (slot - 30);
                var candidates = new HashSet<string>(Files.Comparer);
                foreach (GameItem item in target.Items)
                {
                    Record armor;
                    if (!records.TryGetValue(item.Plugin + "|" + item.Form.ToString("X6"), out armor)) continue;
                    foreach (string link in ArmorLinks(armor, records))
                    {
                        Record addon;
                        if (!records.TryGetValue(link, out addon) || (slots != 0 && (slots & addon.Biped) == 0)) continue;
                        foreach (string mesh in addon.Meshes)
                        {
                            string view = addon.Views[mesh];
                            if (mesh.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                                (target.ViewKind == "Shared" || view == "Shared" || view == target.ViewKind)) candidates.Add(mesh);
                        }
                    }
                }
                return candidates.OrderBy(path => path, Files.Comparer).ToArray();
            }
        }

        internal static string MeshStem(string path)
        {
            string value = path.Replace('/', '\\').Trim('\\');
            if (!value.StartsWith("meshes\\", StringComparison.OrdinalIgnoreCase)) value = "meshes\\" + value;
            return Regex.Replace(value, @"(?:_[01])?\.nif$", "", RegexOptions.IgnoreCase);
        }

        internal List<GameItem> Items(string output)
        {
            List<GameItem> items;
            return byMesh.TryGetValue(MeshStem(output), out items) ? items : new List<GameItem>();
        }

        internal bool HasSwap(string output) { return swapped.Contains(MeshStem(output)); }
        internal string View(string output)
        {
            string value;
            return views.TryGetValue(MeshStem(output), out value) ? value : "";
        }

        internal IEnumerable<int> Slots(string output)
        {
            HashSet<int> value;
            return slots.TryGetValue(MeshStem(output), out value) ? value.AsEnumerable() : Enumerable.Empty<int>();
        }

        internal string AddonName(string output)
        {
            string value;
            return addonNames.TryGetValue(MeshStem(output), out value) ? value : Humanize(Path.GetFileName(output));
        }

        internal static string Humanize(string value)
        {
            value = Regex.Replace(value ?? "", @"^DLC[012]\s*|(?:AA|Addon|Outfit)$", "", RegexOptions.IgnoreCase);
            value = Regex.Replace(value.Replace('_', ' '), @"(?<=[a-z])(?=[A-Z])|(?<=[A-Za-z])(?=\d)", " ");
            value = Regex.Replace(value, @"^(?:Armor|Clothes)\s+(?=\w+\s)", "", RegexOptions.IgnoreCase).Trim();
            return value.Length == 0 ? "Outfit" : Char.ToUpper(value[0]) + value.Substring(1);
        }

        private static void ReadGroup(BinaryReader reader, long end, string[] masters, bool localized,
            Dictionary<string, Dictionary<uint, string>> strings, Dictionary<string, Record> all, RecordScope scope)
        {
            while (reader.BaseStream.Position + 24 <= end)
            {
                string type = Tag(reader);
                int size = reader.ReadInt32();
                uint flags = reader.ReadUInt32(), id = reader.ReadUInt32();
                uint revision = reader.ReadUInt32(), version = reader.ReadUInt32();
                if (size < 0 || reader.BaseStream.Position + size > end) throw new IOException("Invalid outfit record");
                byte[] bytes = reader.ReadBytes(size);
                if ((flags & 0x20) != 0)
                {
                    string deleted = scope == RecordScope.OutfitEquipment ? Key(id, masters) : null;
                    if (deleted != null) all.Remove(deleted);
                    continue;
                }
                if ((flags & 0x40000) != 0)
                {
                    if (bytes.Length < 6) continue;
                    using (var zip = new DeflateStream(new MemoryStream(bytes, 6, bytes.Length - 6), CompressionMode.Decompress))
                    using (var expanded = new MemoryStream()) { zip.CopyTo(expanded); bytes = expanded.ToArray(); }
                }
                var fields = Parts(bytes).ToArray();
                string key = Key(id, masters);
                if (key == null) continue;
                var record = new Record { Type = type, Editor = Text(Field(fields, "EDID")) };
                if (type == "ARMA")
                {
                    record.Addon = new ArmorAddon {
                        Key = key, Id = id, Flags = flags & ~0x40000U, Revision = revision,
                        Version = version, Masters = masters, Fields = fields
                    };
                    var modelTypes = new Dictionary<string, string> { { "MOD2", "Male" }, { "MOD3", "Female" },
                        { "MOD4", "Male · first person" }, { "MOD5", "Female · first person" } };
                    foreach (var field in fields.Where(field => modelTypes.ContainsKey(field.Tag)))
                    {
                        string model = Text(field.Data);
                        if (!model.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)) continue;
                        string stem = MeshStem(model);
                        record.Addon.Models[field.Tag] = stem;
                        record.Views[stem] = record.Views.ContainsKey(stem) ? "Shared" : modelTypes[field.Tag];
                        string digit = field.Tag.Substring(3);
                        var textures = AlternateTextures(Field(fields, "MO" + digit + "S"), masters).ToList();
                        if (digit == "2" || digit == "3")
                        {
                            byte[] skin = Field(fields, digit == "2" ? "NAM0" : "NAM1");
                            if (skin.Length == 4) textures.Add(Key(BitConverter.ToUInt32(skin, 0), masters));
                        }
                        record.Textures.Add(new KeyValuePair<string, string[]>(stem, textures.Where(value => value != null).ToArray()));
                    }
                    record.Meshes = record.Views.Keys.ToArray();
                    byte[] biped = Field(fields, "BOD2");
                    if (biped.Length < 4) biped = Field(fields, "BODT");
                    if (biped.Length >= 4) record.Biped = BitConverter.ToUInt32(biped, 0);
                    record.HasSwap = record.Textures.Any(group => group.Value.Length > 0);
                }
                if (type == "ARMO" || type == "WEAP")
                {
                    string plugin = masters[id >> 24];
                    byte[] full = Field(fields, "FULL");
                    uint fullId = full.Length == 4 ? BitConverter.ToUInt32(full, 0) : 0;
                    string stem = Path.GetFileNameWithoutExtension(masters.Last());
                    record.Item = new GameItem { Form = id & 0xFFFFFF, Plugin = plugin, EditorId = record.Editor, Weapon = type == "WEAP",
                        English = localized ? Localized(strings, stem + "_english.strings", fullId) : Text(full),
                        Italian = localized ? Localized(strings, stem + "_italian.strings", fullId) : null };
                    byte[] typeSlots = Field(fields, "BOD2");
                    if (typeSlots.Length < 4) typeSlots = Field(fields, "BODT");
                    if (typeSlots.Length >= 4) record.Item.Biped = BitConverter.ToUInt32(typeSlots, 0);
                    if (String.IsNullOrWhiteSpace(record.Item.English))
                        record.Item.English = Localized(strings, Path.GetFileNameWithoutExtension(plugin) + "_english.strings", fullId);
                    if (scope == RecordScope.OutfitEquipment && String.IsNullOrWhiteSpace(record.Item.English) &&
                        String.IsNullOrWhiteSpace(record.Item.Italian) && String.IsNullOrWhiteSpace(record.Editor))
                        record.Item = null;
                    else if (String.IsNullOrWhiteSpace(record.Item.English))
                        record.Item.English = Humanize(record.Editor);
                    record.Links = fields.Where(field => field.Tag == "MODL" && field.Data.Length == 4)
                        .Select(field => Key(BitConverter.ToUInt32(field.Data, 0), masters)).Where(link => link != null).ToArray();
                    byte[] template = Field(fields, "TNAM");
                    if (template.Length == 4) record.Template = Key(BitConverter.ToUInt32(template, 0), masters);
                }
                if (type == "OTFT")
                {
                    byte[] input = Field(fields, "INAM");
                    record.Links = Enumerable.Range(0, input.Length / 4)
                        .Select(i => Key(BitConverter.ToUInt32(input, i * 4), masters)).Where(link => link != null).ToArray();
                }
                if (type == "LVLI")
                    record.Links = fields.Where(field => field.Tag == "LVLO" && field.Data.Length >= 8)
                        .Select(field => Key(BitConverter.ToUInt32(field.Data, 4), masters)).Where(link => link != null).ToArray();
                all[key] = record;
            }
        }

        private static IEnumerable<string> AlternateTextures(byte[] data, string[] masters)
        {
            if (data.Length < 4) yield break;
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                uint count = reader.ReadUInt32();
                for (uint index = 0; index < count && reader.BaseStream.Position + 12 <= data.Length; index++)
                {
                    uint nameLength = reader.ReadUInt32();
                    if (nameLength > data.Length - reader.BaseStream.Position - 8) yield break;
                    reader.BaseStream.Position += nameLength;
                    yield return Key(reader.ReadUInt32(), masters);
                    reader.ReadInt32();
                }
            }
        }

        private static IEnumerable<string> Expand(IEnumerable<string> links, Dictionary<string, Record> all, HashSet<string> visited)
        {
            foreach (string link in links)
            {
                Record record;
                if (!visited.Add(link) || !all.TryGetValue(link, out record)) continue;
                if (record.Item != null) yield return link;
                else if (record.Type == "LVLI")
                    foreach (var item in Expand(record.Links, all, visited)) yield return item;
            }
        }

        private static IEnumerable<string> ArmorLinks(Record record, Dictionary<string, Record> all)
        {
            var visited = new HashSet<string>(Files.Comparer);
            while (record.Links.Length == 0 && record.Template != null && visited.Add(record.Template))
            {
                Record template;
                if (!all.TryGetValue(record.Template, out template)) break;
                record = template;
            }
            return record.Links;
        }

        private static string Key(uint value, string[] masters)
        {
            uint master = value >> 24;
            return value != 0 && master < masters.Length ? masters[master] + "|" + (value & 0xFFFFFF).ToString("X6") : null;
        }

        private static Dictionary<string, Dictionary<uint, string>> ReadStrings(string data, List<string> notices)
        {
            var found = new Dictionary<string, Dictionary<uint, string>>(Files.Comparer);
            var names = new HashSet<string>(Official.SelectMany(file => new[] { "_english.strings", "_italian.strings" }
                .Select(language => Path.GetFileNameWithoutExtension(file) + language)), Files.Comparer);
            foreach (string archive in new[] { "Skyrim - Interface.bsa", "Skyrim - Misc.bsa", "Skyrim - Voices_en0.bsa", "Skyrim - Voices_it0.bsa" })
            {
                string path = Path.Combine(data, archive);
                if (!File.Exists(path)) continue;
                try
                {
                    foreach (var item in Bsa.LoadFiles(path, name => names.Contains(Path.GetFileName(name))))
                        found[Path.GetFileName(item.Key)] = ParseStrings(item.Value);
                }
                catch (IOException error) { notices.Add(archive + " — " + error.Message); }
            }
            foreach (string name in names)
            {
                string file = Path.Combine(data, "Strings", name);
                if (File.Exists(file)) found[name] = ParseStrings(File.ReadAllBytes(file));
            }
            return found;
        }

        private static Dictionary<uint, string> ParseStrings(byte[] data)
        {
            var result = new Dictionary<uint, string>();
            int count = data.Length < 8 ? 0 : BitConverter.ToInt32(data, 0);
            if (count < 0 || count > (data.Length - 8) / 8) return result;
            int start = 8 + 8 * count;
            for (int i = 0; i < count; i++)
            {
                int offset = BitConverter.ToInt32(data, 12 + i * 8);
                if (offset < 0 || offset >= data.Length - start) continue;
                int last = Array.IndexOf(data, (byte)0, start + offset);
                if (last < 0) continue;
                string value = Encoding.UTF8.GetString(data, start + offset, last - start - offset);
                if (value.IndexOf('\uFFFD') >= 0)
                    value = Encoding.GetEncoding(1252).GetString(data, start + offset, last - start - offset);
                result[BitConverter.ToUInt32(data, 8 + i * 8)] = value;
            }
            return result;
        }

        private static string Localized(Dictionary<string, Dictionary<uint, string>> strings, string name, uint id)
        {
            Dictionary<uint, string> table;
            string value;
            return strings.TryGetValue(name, out table) && table.TryGetValue(id, out value) ? value : null;
        }

        private static IEnumerable<FieldData> Parts(byte[] data)
        {
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                int next = -1;
                while (reader.BaseStream.Position + 6 <= data.Length)
                {
                    string tag = Tag(reader);
                    int size = reader.ReadUInt16();
                    if (tag == "XXXX" && size == 4)
                    {
                        next = reader.ReadInt32();
                        continue;
                    }
                    if (next >= 0) { size = next; next = -1; }
                    if (size > data.Length - reader.BaseStream.Position) yield break;
                    yield return new FieldData { Tag = tag, Data = reader.ReadBytes(size) };
                }
            }
        }
        private static byte[] Field(IEnumerable<FieldData> fields, string tag)
        {
            var field = fields.FirstOrDefault(item => item.Tag == tag);
            return field == null ? new byte[0] : field.Data;
        }
        private static string Tag(BinaryReader reader) { return Encoding.ASCII.GetString(reader.ReadBytes(4)); }
        private static string Text(byte[] value) { return Encoding.UTF8.GetString(value).TrimEnd('\0'); }

        internal sealed class FieldData { internal string Tag; internal byte[] Data; }
        internal sealed class ArmorAddon
        {
            internal string Key;
            internal uint Id, Flags, Revision, Version;
            internal string[] Masters;
            internal FieldData[] Fields;
            internal readonly Dictionary<string, string> Models = new Dictionary<string, string>(Files.Comparer);
            internal readonly HashSet<string> Users = new HashSet<string>(Files.Comparer);
        }
        private sealed class Record
        {
            internal string Type, Editor, Template;
            internal GameItem Item;
            internal ArmorAddon Addon;
            internal string[] Meshes = new string[0], Links = new string[0];
            internal readonly Dictionary<string, string> Views = new Dictionary<string, string>(Files.Comparer);
            internal readonly List<KeyValuePair<string, string[]>> Textures = new List<KeyValuePair<string, string[]>>();
            internal uint Biped;
            internal bool HasSwap;
        }
    }
}
