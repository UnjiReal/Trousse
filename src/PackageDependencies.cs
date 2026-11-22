using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace SkyrimOutfit
{
    internal sealed class PackageDependencies
    {
        private readonly Dictionary<string, string> nexus = new Dictionary<string, string>(Files.Comparer);
        private readonly HashSet<string> manual = new HashSet<string>(Files.Comparer);

        internal PackageDependencies(Catalog catalog, IEnumerable<Piece> pieces, bool usesSmp, ModestyRule[] rules)
        {
            Add("BodySlide and Outfit Studio 6.1+", "201");
            foreach (var piece in pieces)
                Add(piece.ModFolder == null ? Path.GetFileName(piece.ProjectFile) + " · " + piece.Name : piece.ModName, piece.NexusId);
            if (usesSmp)
            {
                const string dll = @"SKSE\Plugins\hdtSMP64.dll";
                string owner = OutfitProvenance.DeployedMod(catalog.DataRoot, dll);
                if (owner == null && File.Exists(Path.Combine(catalog.DataRoot, dll)))
                {
                    var candidates = Files.Children(catalog.StagingRoot, "*", true).Where(folder =>
                        File.Exists(Path.Combine(folder, dll)) || File.Exists(Path.Combine(folder, "Data", dll))).Take(2).ToArray();
                    if (candidates.Length == 1) owner = Path.GetFileName(candidates[0]);
                }
                string mod, id;
                Catalog.GetModIdentity(owner, out mod, out id);
                Add(mod ?? "HDT-SMP runtime", id);
            }
            if (rules.Length > 0)
            {
                Add("Dynamic Feminine Female Modesty Animations", "104374");
                Add("Open Animation Replacer", "92109");
                if (rules.Any(rule => String.IsNullOrEmpty(rule.Race))) Add("Keyword Item Distributor", "55728");
            }
        }

        private void Add(string name, string id)
        {
            uint number;
            if (UInt32.TryParse(id, out number) && number != 0)
            {
                if (!nexus.ContainsKey(id)) nexus.Add(id, name);
            }
            else manual.Add(name);
        }

        internal void Write(string package, string title, bool italian)
        {
            var ordered = nexus.OrderBy(item => item.Value, Files.Comparer).ToArray();
            var requirements = ordered.Select(item => item.Value + " · " + PackageReports.NexusUrl(item.Key));
            var text = new[] { title, "", italian ? "Dipendenze riconosciute da Vortex" : "Dependencies tracked by Vortex" }
                .Concat(requirements);
            if (manual.Count > 0)
                text = text.Concat(new[] { "", italian ? "Dipendenze da verificare a mano" : "Dependencies to check manually" })
                    .Concat(manual.OrderBy(name => name, Files.Comparer));
            File.WriteAllLines(Path.Combine(package, "required_mods.txt"), text, new UTF8Encoding(false));

            // Vortex consumes these rules during ordinary ZIP installation.
            var instructions = ordered.Select(item => new {
                type = "rule",
                rule = new {
                    type = "requires",
                    reference = new {
                        description = item.Value, gameId = "skyrimse", versionMatch = "*",
                        repo = new { repository = "nexus", gameId = "skyrimspecialedition", modId = item.Key }
                    }
                }
            }).ToArray();
            File.WriteAllText(Path.Combine(package, "vortex_override_instructions.json"),
                new JavaScriptSerializer().Serialize(instructions), new UTF8Encoding(false));
        }
    }
}
