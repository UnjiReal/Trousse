using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace SkyrimOutfit
{
    internal sealed class DressUpForm
    {
        internal readonly string Plugin;
        internal readonly uint Form;
        internal readonly bool Light;
        internal string Key { get { return Plugin + "|" + Form.ToString("X6", CultureInfo.InvariantCulture); } }
        internal string LocalId { get { return Form.ToString(Light ? "X3" : "X6", CultureInfo.InvariantCulture); } }

        internal DressUpForm(string plugin, uint form, bool light)
        {
            if (!IsPluginName(plugin) || form == 0 || form > (light ? 0xFFF : 0xFFFFFF))
                throw new InvalidDataException("Invalid Dress Up Lovers form reference.");
            Plugin = plugin;
            Form = form;
            Light = light;
        }

        internal static bool IsPluginName(string name)
        {
            return name != null && Regex.IsMatch(name, @"^[^\\/:|\r\n]+\.(esm|esp|esl)$", RegexOptions.IgnoreCase);
        }

        internal static DressUpForm Parse(string plugin, string id)
        {
            uint form;
            if (id == null || (id.Length != 3 && id.Length != 6) ||
                !UInt32.TryParse(id, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out form))
                throw new InvalidDataException("Invalid Dress Up Lovers FormID.");
            return new DressUpForm(plugin, form, id.Length == 3);
        }
    }

    internal sealed class DressUpOutfit
    {
        internal readonly int Slot;
        internal string Name = "", Tag = "";
        internal readonly List<DressUpForm> Items = new List<DressUpForm>();
        internal DressUpOutfit(int slot) { Slot = slot; }
    }

    internal sealed class DressUpProfile
    {
        internal const int MaxOutfits = 300;
        internal const string GameFolder = @"SKSE\Plugins\DressUpLovers\User";
        private readonly Dictionary<string, object> document;
        private readonly int originalLength;
        internal string Name = "";
        internal bool Dirty;
        internal readonly SortedDictionary<int, DressUpOutfit> Outfits = new SortedDictionary<int, DressUpOutfit>();

        internal DressUpProfile() : this(new Dictionary<string, object>()) { }

        private DressUpProfile(Dictionary<string, object> document)
        {
            this.document = document;
            var numbers = Section(document, "int");
            object value;
            if (numbers.TryGetValue("profile_length", out value) || numbers.TryGetValue("length", out value))
            {
                if (!(value is int) || (int)value < 0 || (int)value > MaxOutfits)
                    throw new InvalidDataException("Invalid Dress Up Lovers profile length.");
                originalLength = (int)value;
            }
            if (Section(document, "string").TryGetValue("name", out value)) Name = value as string ?? "";
            var entries = Section(document, "stringList");
            foreach (var entry in entries)
            {
                int slot;
                if (!Int32.TryParse(entry.Key, NumberStyles.None, CultureInfo.InvariantCulture, out slot)) continue;
                if (slot < 0 || slot >= MaxOutfits || Outfits.ContainsKey(slot))
                    throw new InvalidDataException("Invalid Dress Up Lovers outfit slot.");
                var fields = entry.Value as IList;
                if (fields == null) throw new InvalidDataException("Invalid Dress Up Lovers outfit.");
                if (fields.Count == 0) continue;
                if (fields.Count < 2 || fields.Count % 2 != 0 || fields.Cast<object>().Any(field => !(field is string)))
                    throw new InvalidDataException("Invalid Dress Up Lovers outfit.");
                var outfit = new DressUpOutfit(slot) { Name = (string)fields[0], Tag = (string)fields[1] };
                for (int i = 2; i < fields.Count; i += 2)
                    outfit.Items.Add(DressUpForm.Parse((string)fields[i], (string)fields[i + 1]));
                if (outfit.Items.Count != 0) Outfits.Add(slot, outfit);
            }
        }

        internal static string FileName(int slot) { return "CustomOutfit_Profile" + slot + ".json"; }

        internal static DressUpProfile Load(string path)
        {
            var json = Serializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            if (json == null || !json.ContainsKey("int") || !json.ContainsKey("stringList"))
                throw new InvalidDataException("Choose a Dress Up Lovers custom outfit profile.");
            return new DressUpProfile(json);
        }

        internal DressUpOutfit Add(string name)
        {
            if (Outfits.Count >= MaxOutfits) throw new InvalidOperationException("The Dress Up Lovers profile is full.");
            int slot = Enumerable.Range(0, MaxOutfits).First(index => !Outfits.ContainsKey(index));
            var outfit = new DressUpOutfit(slot) { Name = name };
            Outfits.Add(slot, outfit);
            Dirty = true;
            return outfit;
        }

        internal bool CanExport { get { return Outfits.Values.Any(outfit => outfit.Items.Count != 0); } }

        internal void Save(string path, bool backup)
        {
            var outfits = Outfits.Values.Where(outfit => outfit.Items.Count != 0).ToArray();
            if (outfits.Length == 0) throw new InvalidOperationException("Add an item before saving the profile.");
            var json = new Dictionary<string, object>(document);
            var entries = new Dictionary<string, object>(Section(document, "stringList"));
            foreach (string key in entries.Keys.Where(key => key.Length != 0 && key.All(Char.IsDigit)).ToArray())
                entries.Remove(key);
            foreach (var outfit in outfits)
                entries.Add(outfit.Slot.ToString(CultureInfo.InvariantCulture), new[] { outfit.Name, outfit.Tag }
                    .Concat(outfit.Items.SelectMany(item => new[] { item.Plugin, item.LocalId })).ToArray());
            var counts = new Dictionary<string, object>(Section(document, "int"));
            counts.Remove("length");
            counts["profile_length"] = Math.Max(originalLength, Math.Max(5, outfits.Last().Slot + 1));
            counts["count"] = outfits.Length;
            var labels = new Dictionary<string, object>(Section(document, "string"));
            labels["name"] = Name;
            json["int"] = counts;
            json["string"] = labels;
            json["stringList"] = entries;

            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, Serializer().Serialize(json), new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(temp, path, backup ? path + "." + Guid.NewGuid().ToString("N") + ".bak" : null);
                else File.Move(temp, path);
                Dirty = false;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private static Dictionary<string, object> Section(Dictionary<string, object> root, string name)
        {
            object value;
            if (!root.TryGetValue(name, out value)) return new Dictionary<string, object>();
            var section = value as Dictionary<string, object>;
            if (section == null) throw new InvalidDataException("Invalid Dress Up Lovers JSON section.");
            return section;
        }

        private static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 8000000 }; }
    }
}
