using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SkyrimOutfit
{
    internal sealed class VariantPatch
    {
        private readonly Dictionary<string, Override> changes = new Dictionary<string, Override>(Files.Comparer);

        internal static string Output(Recipe recipe)
        {
            return recipe.VariantIds == null || recipe.VariantIds.Count == 0 ? recipe.Output :
                @"meshes\Trousse\Variants\" + Files.Id(recipe.TargetKey.ToLowerInvariant() + "|" +
                    String.Join("|", recipe.VariantIds.OrderBy(id => id, Files.Comparer).Select(id => id.ToLowerInvariant()))) +
                    @"\" + Path.GetFileName(recipe.Output);
        }

        internal static GameRecords.ArmorAddon[] Resolve(Target target, IEnumerable<string> records)
        {
            var selection = new HashSet<string>(records, Files.Comparer);
            var matches = target.Addons.Where(addon => addon.Users.Overlaps(selection)).ToArray();
            if (selection.Count == 0 || matches.Any(addon => !addon.Users.IsSubsetOf(selection)) ||
                !selection.SetEquals(matches.SelectMany(addon => addon.Users)))
                throw new InvalidDataException("The selected items share an Armor Addon with other records. Select its companion variants or pick a different variant.");
            return matches;
        }

        internal void Add(Target target, Recipe recipe)
        {
            if (recipe.VariantIds == null || recipe.VariantIds.Count == 0) return;
            string mesh = GameRecords.MeshStem(target.Output);
            string path = Output(recipe).Substring("meshes\\".Length) + (recipe.Weighted ? "_1.nif" : ".nif");
            foreach (var addon in Resolve(target, recipe.VariantIds))
            {
                Override edit;
                if (!changes.TryGetValue(addon.Key, out edit))
                    changes.Add(addon.Key, edit = new Override { Addon = addon });
                foreach (var model in addon.Models.Where(model => Files.Comparer.Equals(model.Value, mesh)))
                {
                    Recipe previous;
                    if (edit.Owners.TryGetValue(model.Key, out previous))
                        throw new PackageConflict("More than one outfit targets the same Armor Addon and view: " + recipe.Name + " / " + previous.Name,
                            ConflictArea.Destination, new[] { recipe, previous });
                    edit.Owners.Add(model.Key, recipe);
                    edit.Paths.Add(model.Key, path);
                }
            }
        }

        internal void Save(string package, string packId)
        {
            if (changes.Count == 0) return;
            var masterGroups = changes.Values.GroupBy(change => String.Join("|", change.Addon.Masters), Files.Comparer).ToArray();
            // Keep each original file's master indices intact, including FormIDs in opaque fields.
            for (int group = 0; group < masterGroups.Length; group++)
            {
                var edits = masterGroups[group].OrderBy(change => change.Addon.Id).ToArray();
                string output = Path.Combine(package, packId + "-Variants" + (group == 0 ? "" : "-" + (group + 1)) + ".esp");
                using (var writer = new BinaryWriter(File.Create(output)))
                {
                    byte[] header = Payload(fields => {
                        Field(fields, "HEDR", Payload(meta => { meta.Write(1.7f); meta.Write(edits.Length + 1); meta.Write(0x800); }));
                        foreach (string master in edits[0].Addon.Masters)
                        {
                            Field(fields, "MAST", ZString(master));
                            Field(fields, "DATA", new byte[8]);
                        }
                    });
                    Record(writer, "TES4", header, 0, 0, 0, 44);
                    byte[] records = Payload(groupWriter => {
                        foreach (Override edit in edits)
                        {
                            var addon = edit.Addon;
                            Record(groupWriter, "ARMA", EditFields(edit), addon.Flags, addon.Id, addon.Revision, addon.Version);
                        }
                    });
                    writer.Write(Encoding.ASCII.GetBytes("GRUP"));
                    writer.Write(24 + records.Length);
                    writer.Write(Encoding.ASCII.GetBytes("ARMA"));
                    writer.Write(new byte[12]);
                    writer.Write(records);
                }
            }
        }

        private static byte[] EditFields(Override edit)
        {
            return Payload(writer => {
                var replacedFields = new HashSet<string>(Files.Comparer);
                foreach (string tag in edit.Paths.Keys)
                {
                    replacedFields.Add("MO" + tag[3] + "T");
                    replacedFields.Add("MO" + tag[3] + "S");
                }
                foreach (var field in edit.Addon.Fields)
                {
                    string replacement;
                    if (edit.Paths.TryGetValue(field.Tag, out replacement)) Field(writer, field.Tag, ZString(replacement));
                    else if (!replacedFields.Contains(field.Tag)) Field(writer, field.Tag, field.Data);
                }
            });
        }

        private static byte[] ZString(string value) { return Encoding.UTF8.GetBytes(value + "\0"); }

        private static byte[] Payload(Action<BinaryWriter> write)
        {
            using (var buffer = new MemoryStream())
            using (var writer = new BinaryWriter(buffer))
            {
                write(writer);
                return buffer.ToArray();
            }
        }

        private static void Field(BinaryWriter writer, string tag, byte[] data)
        {
            if (data.Length > UInt16.MaxValue)
            {
                writer.Write(Encoding.ASCII.GetBytes("XXXX"));
                writer.Write((ushort)4);
                writer.Write(data.Length);
            }
            writer.Write(Encoding.ASCII.GetBytes(tag));
            writer.Write(data.Length > UInt16.MaxValue ? (ushort)0 : (ushort)data.Length);
            writer.Write(data);
        }

        private static void Record(BinaryWriter writer, string type, byte[] data, uint flags, uint id, uint revision, uint version)
        {
            writer.Write(Encoding.ASCII.GetBytes(type));
            writer.Write(data.Length);
            writer.Write(flags);
            writer.Write(id);
            writer.Write(revision);
            writer.Write(version);
            writer.Write(data);
        }

        private sealed class Override
        {
            internal GameRecords.ArmorAddon Addon;
            internal readonly Dictionary<string, string> Paths = new Dictionary<string, string>(Files.Comparer);
            internal readonly Dictionary<string, Recipe> Owners = new Dictionary<string, Recipe>(Files.Comparer);
        }
    }
}
