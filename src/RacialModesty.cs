using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace SkyrimOutfit
{
    internal sealed class RacialModesty
    {
        private const string Root = @"Meshes\actors\character\animations\OpenAnimationReplacer";
        private const string PresetPrefix = "Trousse coverage ";
        private const string OriginalField = "trousseOriginalCondition";
        private const string AnySlot = "IsWornHasKeyword", SlotKeyword = "IsWornInSlotHasKeyword";
        private readonly ModestyRule[] rules;
        private readonly Dictionary<string, Dictionary<string, object>> presets = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 24000000 };

        private RacialModesty(ModestyRule[] rules) { this.rules = rules; }

        internal static int Write(string data, string package, ModestyRule[] rules)
        {
            var scoped = rules.Where(rule => !String.IsNullOrEmpty(rule.Race)).ToArray();
            if (scoped.Length == 0) return 0;
            int written = 0;
            foreach (string parent in new[] { "KP_nudePC", "KP_nudeNPC" })
            {
                string source = Path.Combine(data, Root, parent);
                if (File.Exists(Path.Combine(source, "config.json")))
                    written += new RacialModesty(scoped).WritePack(source, Path.Combine(package, Root, parent));
            }
            if (written == 0)
                throw new InvalidDataException("Race-specific modesty requires the deployed Dynamic Feminine Female Modesty Animations OAR configuration (KP_nudePC or KP_nudeNPC). Select All races for a KID-only rule.");
            return written;
        }

        private int WritePack(string source, string output)
        {
            var root = ReadConfig(source);
            var originals = ArrayField(root, "conditionPresets").OfType<Dictionary<string, object>>()
                .Where(preset => !Text(preset, "name").StartsWith(PresetPrefix, StringComparison.Ordinal)).ToList();
            foreach (var preset in originals) RewriteConditions(preset);
            int count = 0;
            foreach (string child in Directory.EnumerateDirectories(source))
            {
                if (!File.Exists(Path.Combine(child, "config.json"))) continue;
                var config = ReadConfig(child);
                if (!RewriteConditions(config)) continue;
                WriteOverride(Path.Combine(output, Path.GetFileName(child)), config);
                count++;
            }
            if (presets.Count == 0) return 0;
            root["conditionPresets"] = originals.Concat(presets.Values.OrderBy(value => Text(value, "name"), StringComparer.Ordinal)).ToArray();
            WriteOverride(output, root);
            return count + 1;
        }

        private Dictionary<string, object> ReadConfig(string folder)
        {
            var config = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(folder, "config.json")));
            string user = Path.Combine(folder, "user.json");
            if (File.Exists(user))
                foreach (var option in json.Deserialize<Dictionary<string, object>>(File.ReadAllText(user))) config[option.Key] = option.Value;
            return config;
        }

        private void WriteOverride(string directory, Dictionary<string, object> config)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "user.json"), json.Serialize(config), new UTF8Encoding(false));
        }

        private bool RewriteConditions(Dictionary<string, object> config)
        {
            bool changed = false;
            foreach (var key in new[] { "conditions", "Conditions" })
            {
                IList children = ArrayField(config, key);
                for (int i = 0; i < children.Count; i++)
                {
                    var child = children[i] as Dictionary<string, object>;
                    if (child == null) continue;
                    var original = Field(child, OriginalField) as Dictionary<string, object> ?? RestorePreset(child);
                    if (original != null) child = original;
                    var word = Field(child, "Keyword") as Dictionary<string, object>;
                    string keyword = word == null ? "" : Text(word, "editorID");
                    string kind = Text(child, "condition");
                    if (kind == AnySlot && KnownKeyword(keyword))
                    {
                        children[i] = Preset(child, keyword, -1);
                        changed = true;
                    }
                    else if (kind == SlotKeyword && KnownKeyword(keyword))
                    {
                        var slotData = Field(child, "Slot") as Dictionary<string, object>;
                        decimal slot;
                        if (slotData != null && Decimal.TryParse(Convert.ToString(Field(slotData, "value"), CultureInfo.InvariantCulture),
                            NumberStyles.Float, CultureInfo.InvariantCulture, out slot) && slot >= 0 && slot < 32 && slot == Decimal.Truncate(slot))
                        {
                            children[i] = Preset(child, keyword, (int)slot);
                            changed = true;
                        }
                    }
                    else if (RewriteConditions(child)) changed = true;
                }
            }
            return changed;
        }

        private static Dictionary<string, object> RestorePreset(Dictionary<string, object> child)
        {
            string name = Text(child, "Preset");
            if (Text(child, "condition") != "PRESET" || !name.StartsWith(PresetPrefix, StringComparison.Ordinal)) return null;
            string[] parts = name.Substring(PresetPrefix.Length).Split(' ');
            if (parts.Length != 2 || !KnownKeyword(parts[0])) return null;
            var original = KeywordCondition(parts[1] == "any" ? AnySlot : SlotKeyword, parts[0]);
            if (parts[1] != "any")
            {
                int number;
                if (!Int32.TryParse(parts[1], out number) || number < 30 || number > 61) return null;
                original.Add("Slot", new Dictionary<string, object> { { "value", number - 30 } });
            }
            foreach (var property in new[] { "negated", "disabled", "comment" })
            {
                object value = Field(child, property);
                if (value != null) original[property] = value;
            }
            return original;
        }

        private Dictionary<string, object> Preset(Dictionary<string, object> original, string keyword, int slot)
        {
            string name = PresetPrefix + keyword + " " + (slot < 0 ? "any" : (slot + 30).ToString());
            if (!presets.ContainsKey(name))
                presets.Add(name, new Dictionary<string, object> {
                    { "name", name }, { "description", "Race-specific coverage for the outfits in this Trousse package." },
                    { "conditions", new[] { slot < 0 ? AnyPredicate(keyword) : SlotPredicate(keyword, slot) } }
                });
            var replacement = Condition("PRESET");
            replacement["requiredVersion"] = "2.2.0.0";
            replacement.Add("Preset", name);
            replacement.Add(OriginalField, original);
            foreach (var property in new[] { "negated", "disabled", "comment" })
            {
                object value = Field(original, property);
                if (value != null) replacement[property] = value;
            }
            return replacement;
        }

        private Dictionary<string, object> AnyPredicate(string keyword)
        {
            var selected = Combine("OR", rules.Select(Matches));
            var regular = KeywordCondition(AnySlot, keyword);
            var allSlots = Combine("OR", Enumerable.Range(0, 32).Select(slot => SlotPredicate(keyword, slot)));
            return Combine("OR", new[] { Combine("AND", new[] { regular, Not(selected) }), Combine("AND", new[] { selected, allSlots }) });
        }

        private Dictionary<string, object> SlotPredicate(string keyword, int slot)
        {
            var candidates = rules.Where(rule => (rule.Slots & (1U << slot)) != 0).ToArray();
            var standard = KeywordCondition(SlotKeyword, keyword);
            standard.Add("Slot", new Dictionary<string, object> { { "value", slot } });
            if (candidates.Length == 0) return standard;
            var alternatives = new List<Dictionary<string, object>> {
                Combine("AND", new[] { standard, Not(Combine("OR", candidates.Select(Matches))) })
            };
            alternatives.AddRange(candidates.Where(rule => rule.Keywords.Contains(keyword)).Select(Matches));
            return Combine("OR", alternatives);
        }

        private static Dictionary<string, object> Matches(ModestyRule rule)
        {
            var worn = Condition("IsWorn");
            worn.Add("Form", Form(rule.Item.Plugin, rule.Item.Form.ToString("X")));
            var race = Condition("IsRace");
            race.Add("Race", Form("Skyrim.esm", rule.Race));
            return Combine("AND", new[] { worn, race });
        }

        private static Dictionary<string, object> Form(string plugin, string form)
        {
            return new Dictionary<string, object> { { "pluginName", plugin }, { "formID", form } };
        }

        private static Dictionary<string, object> KeywordCondition(string kind, string word)
        {
            var condition = Condition(kind);
            condition.Add("Keyword", new Dictionary<string, object> { { "editorID", word } });
            return condition;
        }

        private static Dictionary<string, object> Condition(string name)
        {
            return new Dictionary<string, object> { { "condition", name }, { "requiredVersion", "1.0.0.0" } };
        }

        private static Dictionary<string, object> Combine(string operation, IEnumerable<Dictionary<string, object>> items)
        {
            var entries = items.ToArray();
            if (entries.Length == 1) return entries[0];
            var result = Condition(operation);
            result.Add("Conditions", entries);
            return result;
        }

        private static Dictionary<string, object> Not(Dictionary<string, object> condition)
        {
            return new Dictionary<string, object>(condition) { { "negated", true } };
        }

        private static bool KnownKeyword(string keyword)
        {
            return ModestyRules.CoversTop(keyword) || ModestyRules.CoversBottom(keyword) || keyword == ModestyRules.Exposed;
        }

        private static object Field(Dictionary<string, object> source, string key)
        {
            object value;
            return source.TryGetValue(key, out value) ? value : null;
        }

        private static string Text(Dictionary<string, object> source, string key) { return Field(source, key) as string ?? ""; }
        private static IList ArrayField(Dictionary<string, object> source, string key) { return Field(source, key) as IList ?? new object[0]; }
    }
}
