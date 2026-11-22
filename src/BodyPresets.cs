using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace SkyrimOutfit
{
    internal sealed class BodyPreset
    {
        public string Name { get; internal set; }
        public string Groups { get; internal set; }
        public string BodySet { get; internal set; }
        public string Mod { get; internal set; }
        public string FilePath { get; internal set; }
        internal string[] Sliders;
        internal XElement Definition;
    }

    internal sealed partial class Catalog
    {
        internal readonly List<BodyPreset> Presets = new List<BodyPreset>();
        private readonly HashSet<string> presetKeys = new HashSet<string>(Files.Comparer);

        private void ReadPresets(string root, string mod, bool deployed)
        {
            string folder = Path.Combine(root, "SliderPresets");
            if (!Directory.Exists(folder)) return;
            foreach (string file in Directory.EnumerateFiles(folder, "*.xml", SearchOption.AllDirectories))
            {
                try
                {
                    foreach (var preset in Files.Xml(file).Root.Elements("Preset"))
                    {
                        string name = (string)preset.Attribute("name");
                        if (String.IsNullOrWhiteSpace(name)) continue;
                        string[] sliders = preset.Elements("SetSlider").Select(value => (string)value.Attribute("name") +
                            " · " + (string)value.Attribute("size") + "  " + (string)value.Attribute("value") + "%").ToArray();
                        string groups = String.Join(", ", preset.Elements("Group")
                            .Select(group => (string)group.Attribute("name")).Where(group => !String.IsNullOrWhiteSpace(group)));
                        string body = (string)preset.Attribute("set") ?? "";
                        string identity = Files.Relative(folder, file) + "|" + name + "|" + body + "|" + groups + "|" + String.Join(";", sliders);
                        if (deployed && presetKeys.Contains(identity)) continue;
                        if (!deployed) presetKeys.Add(identity);
                        Presets.Add(new BodyPreset {
                            Name = name, Groups = groups, BodySet = body, Mod = mod, FilePath = file, Sliders = sliders, Definition = preset
                        });
                    }
                }
                catch (Exception error)
                {
                    if (!(error is XmlException || error is IOException || error is UnauthorizedAccessException)) throw;
                    Notices.Add(Path.GetFileName(file) + " — " + error.Message);
                }
            }
        }

        internal static bool IsBodyProject(Piece piece)
        {
            return GameRecords.MeshStem(piece.Output).EndsWith(@"\femalebody", StringComparison.OrdinalIgnoreCase)
                && piece.ShapeNames.Any(shape => Composer.BodyPart(shape) == "body")
                && (piece.Name.IndexOf("CBBE", StringComparison.OrdinalIgnoreCase) >= 0
                    || piece.Name.IndexOf("3BA", StringComparison.OrdinalIgnoreCase) >= 0
                    || piece.Name.IndexOf("3BBB", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
