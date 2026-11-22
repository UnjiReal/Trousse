using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SkyrimOutfit
{
    internal sealed class ModestyRule
    {
        internal GameItem Item;
        internal Recipe Outfit;
        internal string Keyword, Race;
        internal uint Slots;

        internal IEnumerable<string> Keywords
        {
            get
            {
                yield return Keyword;
                // The torso branch requires Modesty on an occupied slot 32, even when
                // NoModestyBottom excludes the lower-body animation.
                if (Keyword == ModestyRules.Bottom) yield return ModestyRules.Exposed;
            }
        }
    }

    internal static class ModestyRules
    {
        internal const string Top = "NoModestyTop", Bottom = "NoModestyBottom", Both = "NoModestyAll", Exposed = "Modesty";
        internal static readonly string[] RaceForms = { "13746", "13740", "13741", "13742", "13743", "13744", "13745", "13747", "13748", "13749", "67CD8" };

        internal static string Coverage(bool top, bool bottom) { return top ? (bottom ? Both : Top) : (bottom ? Bottom : Exposed); }
        internal static bool CoversTop(string keyword) { return keyword == Top || keyword == Both || keyword == "NoModesty"; }
        internal static bool CoversBottom(string keyword) { return keyword == Bottom || keyword == Both || keyword == "NoModesty"; }
        internal static string Animation(bool top, bool bottom) { return Coverage(!top, !bottom); }

        internal static ModestyRule[] Resolve(Catalog catalog, IEnumerable<Recipe> outfits)
        {
            var rules = new Dictionary<string, ModestyRule>(Files.Comparer);
            foreach (Recipe outfit in outfits.Where(recipe => !String.IsNullOrEmpty(recipe.Modesty)))
            {
                Target target;
                if (!(CoversTop(outfit.Modesty) || CoversBottom(outfit.Modesty) || outfit.Modesty == Exposed) ||
                    !catalog.ByTarget.TryGetValue(outfit.TargetKey, out target) || outfit.RuleTargets == null || outfit.RuleTargets.Count == 0)
                    throw new PackageConflict("Select valid game records for the modesty rule in " + outfit.Name + ".",
                        ConflictArea.Outfit, new[] { outfit });
                if (!String.IsNullOrEmpty(outfit.ModestyRace) && !RaceForms.Contains(outfit.ModestyRace, Files.Comparer))
                    throw new PackageConflict("Invalid race in the modesty rule for " + outfit.Name + ".", ConflictArea.Outfit, new[] { outfit });
                foreach (string record in outfit.RuleTargets)
                {
                    GameItem item = target.Items.FirstOrDefault(entry => Files.Comparer.Equals(entry.RuleId, record));
                    if (item == null) throw new PackageConflict("The modesty rule no longer matches the game records in " + outfit.Name + ".",
                        ConflictArea.Outfit, new[] { outfit });
                    string key = item.RuleId + "|" + outfit.ModestyRace;
                    ModestyRule previous;
                    if (rules.TryGetValue(key, out previous) && previous.Keyword != outfit.Modesty)
                        throw new PackageConflict("Conflicting modesty rules for " + item.Name(false) + ".",
                            ConflictArea.Outfit, new[] { outfit, previous.Outfit });
                    uint slots = item.Biped;
                    if (slots == 0) foreach (int slot in target.Slots) slots |= 1U << (slot - 30);
                    if (slots == 0) slots = 1U << (outfit.Slot - 30);
                    rules[key] = new ModestyRule { Item = item, Outfit = outfit, Keyword = outfit.Modesty, Race = outfit.ModestyRace, Slots = slots };
                }
            }
            return rules.Values.OrderBy(rule => rule.Item.RuleId, Files.Comparer).ThenBy(rule => rule.Race, Files.Comparer).ToArray();
        }

        internal static string KidName(string name)
        {
            string label = name.Trim();
            return "Trousse-" + Files.FileLabel(label) + "-" + Files.Id(label).Substring(0, 8) + "_KID.ini";
        }

        internal static void WriteKid(string path, IEnumerable<ModestyRule> rules)
        {
            var lines = new List<string> { "; Trousse - Dynamic Feminine Female Modesty Animations", "" };
            foreach (var rule in rules)
            {
                lines.Add("; " + rule.Item.EditorId);
                if (String.IsNullOrEmpty(rule.Race))
                    lines.AddRange(rule.Keywords.Select(keyword => "Keyword = " + keyword + "|Armor|" + rule.Item.RuleId));
                else lines.Add("; Race " + rule.Race + " / " + rule.Item.RuleId + " / " + rule.Keyword + " is handled by the OAR files in the mod ZIP.");
                lines.Add("");
            }
            File.WriteAllLines(path, lines, Encoding.ASCII);
        }
    }
}
