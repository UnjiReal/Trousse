using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SkyrimOutfit
{
    internal static class Files
    {
        internal static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

        internal static XDocument Xml(string path)
        {
            using (var reader = XmlReader.Create(path, new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 240000000
            }))
                return XDocument.Load(reader);
        }

        internal static string Text(XElement element, string tag)
        {
            return ((string)element.Element(tag) ?? "").Trim();
        }

        internal static string PathIn(string root, string relative)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            relative = relative.Replace('/', '\\');
            if (Path.IsPathRooted(relative) || relative.Contains(":"))
                throw new InvalidDataException("Percorso non relativo: " + relative);
            string path = Path.GetFullPath(Path.Combine(root, relative));
            if (!Comparer.Equals(path, root) && !path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Percorso fuori dalla cartella dati: " + relative);
            return path;
        }

        internal static string Relative(string root, string path)
        {
            return path.Substring(root.TrimEnd('\\').Length).TrimStart('\\');
        }

        internal static string Id(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value.ToUpperInvariant())), 0, 12)
                    .Replace("-", "").ToLowerInvariant();
        }

        internal static string FileLabel(string name)
        {
            string label = Regex.Replace(name, @"[^a-zA-Z0-9_-]+", "-").Trim('-');
            return label.Length == 0 ? "outfit" : label.Substring(0, Math.Min(label.Length, 36));
        }

        internal static void Copy(string source, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(source, target, true);
            File.SetAttributes(target, FileAttributes.Normal);
        }

        internal static IEnumerable<string> Children(string root, string pattern, bool directories)
        {
            if (!Directory.Exists(root)) return Enumerable.Empty<string>();
            return directories ? Directory.EnumerateDirectories(root, pattern) : Directory.EnumerateFiles(root, pattern);
        }
    }

    internal sealed class Piece
    {
        internal string Id, Mod, BodyRoot, ProjectFile, Output, DataFolder, Source;
        internal string ModFolder, NexusId;
        internal string[] ShapeNames;
        public string Name { get; internal set; }
        public string ShortName { get { return Regex.Replace(Name, @"^(?:CBBE SE\s*-\s*)?(?:\[[^\]]+\]\s*)?", "").Trim(); } }
        public string ModName { get { return Mod; } }
        public string Mesh { get { return Output + (Weighted ? "_0.nif / _1.nif" : ".nif"); } }
        public int Shapes { get { return ShapeNames.Length; } }
        internal bool Weighted, Derived, HasSliderData;

        internal string FindData(string deployedRoot, IEnumerable<string> folders, string file)
        {
            foreach (string folder in folders)
            {
                foreach (string root in new[] { BodyRoot, deployedRoot }.Distinct(Files.Comparer))
                {
                    string candidate = Files.PathIn(Path.Combine(root, "ShapeData"), Path.Combine(folder, file));
                    if (File.Exists(candidate)) return candidate;
                }
            }
            throw new FileNotFoundException(Name + " richiede " + file + " in ShapeData.");
        }

        internal string FindNif(string deployedRoot)
        {
            return FindData(deployedRoot, new[] { DataFolder }, Source);
        }
    }

    internal enum EquipmentCategory { All, Body, Feet, Hands, Head, Amulets, Rings, Shields, Other }

    internal sealed class Target
    {
        internal string Output;
        internal bool Weighted;
        internal readonly List<Piece> References = new List<Piece>();
        internal List<GameItem> Items = new List<GameItem>();
        internal string AddonName, ViewKind;
        public string View { get; internal set; }
        internal bool HasSwap;
        internal bool FirstPerson, ThirdPerson;
        internal GameRecords.ArmorAddon[] Addons = new GameRecords.ArmorAddon[0];
        internal int[] Slots = new int[0];
        internal EquipmentCategory Category;
        public string Name { get; internal set; }
        public string ConsoleIds { get { return String.Join(" · ", Items.Select(item => item.ConsoleId).Distinct().OrderBy(id => id)); } }
        public string ShortIds { get { return Items.Count <= 2 ? ConsoleIds : Items[0].ConsoleId + " +" + (Items.Count - 1); } }
        internal string BodySlideName;
        internal string DisplayName(bool italian)
        {
            return BodySlideName ?? (Items.Count == 0 ? AddonName : Items.OrderBy(item => item.Name(italian).Length).First().Name(italian));
        }

        internal void NameFromBodySlide(IEnumerable<Piece> counterparts)
        {
            var names = counterparts.Select(piece => Regex.Match(piece.Name,
                @"^(CBBE|3BA) (?:Vanilla|Dawnguard|Dragonborn|Hearthfire[s]?) - ([^-]+) - (.+?)(?: \(Physics\))?$",
                RegexOptions.IgnoreCase)).Where(match => match.Success)
                .OrderBy(match => !Files.Comparer.Equals(match.Groups[1].Value, "CBBE"));
            var first = names.FirstOrDefault();
            if (first == null) return;
            BodySlideName = first.Groups[3].Value;
            string part = first.Groups[2].Value;
            if (part == "Feet" || part == "Hands" || part == "Hoods")
                BodySlideName += " · " + part;
        }
        public string Mesh { get { return Output + (Weighted ? "_0.nif / _1.nif" : ".nif"); } }
        public int Alternatives { get { return References.Select(piece => piece.Name).Distinct(Files.Comparer).Count(); } }

        internal int SuggestedSlot()
        {
            if (Slots.Length == 1) return Slots[0];
            string text = (Name + " " + Path.GetFileName(Output)).ToLowerInvariant();
            if (Regex.IsMatch(text, @"glove|gauntlet|bracer|\bhands?\b")) return 33;
            if (Regex.IsMatch(text, @"boot|shoe|\bfeet\b")) return 37;
            if (Regex.IsMatch(text, @"circlet")) return 42;
            if (Regex.IsMatch(text, @"hood|helmet|\bhat\b|\bhead\b|\bmask\b")) return 31;
            return 32;
        }

        internal string Key { get { return Output + (Weighted ? "|pair" : "|single"); } }

        internal EquipmentCategory FindCategory()
        {
            if (Slots.Contains(32)) return EquipmentCategory.Body;
            if (Slots.Contains(37) || Slots.Contains(38)) return EquipmentCategory.Feet;
            if (Slots.Contains(33) || Slots.Contains(34)) return EquipmentCategory.Hands;
            if (Slots.Contains(35)) return EquipmentCategory.Amulets;
            if (Slots.Contains(36)) return EquipmentCategory.Rings;
            if (Slots.Contains(39)) return EquipmentCategory.Shields;
            if (Slots.Any(slot => slot == 30 || slot == 31 || slot == 41 || slot == 42 || slot == 43)) return EquipmentCategory.Head;

            string name = (AddonName + " " + Path.GetFileName(Output)).ToLowerInvariant();
            if (Regex.IsMatch(name, @"glov|gauntlet|bracer|\bhands?\b")) return EquipmentCategory.Hands;
            if (Regex.IsMatch(name, @"boot|shoe|sandal|\bfeet\b")) return EquipmentCategory.Feet;
            if (Regex.IsMatch(name, @"amulet|necklace|pendant|talisman")) return EquipmentCategory.Amulets;
            if (Regex.IsMatch(name, @"\bring\b|ring(?:[a-z]+_?\d*|_?\d+)$")) return EquipmentCategory.Rings;
            if (Regex.IsMatch(name, @"shield|buckler")) return EquipmentCategory.Shields;
            if (Regex.IsMatch(name, @"hood|helmet|circlet|\bhat\b|\bhead\b|\bmask\b")) return EquipmentCategory.Head;
            if (Regex.IsMatch(name, @"cuirass|tunic|torso|outfit|robe|dress|\bbody\b")) return EquipmentCategory.Body;
            return EquipmentCategory.Other;
        }
    }

    internal sealed partial class Catalog
    {
        internal readonly List<Piece> Pieces = new List<Piece>();
        internal readonly List<Target> Targets = new List<Target>();
        internal readonly List<string> Notices = new List<string>();
        internal readonly Dictionary<string, HashSet<string>> Groups = new Dictionary<string, HashSet<string>>(Files.Comparer);
        internal readonly Dictionary<string, Piece> ById = new Dictionary<string, Piece>(Files.Comparer);
        internal readonly Dictionary<string, Target> ByTarget = new Dictionary<string, Target>(Files.Comparer);
        internal readonly string BodyRoot, DataRoot, StagingRoot;
        private readonly HashSet<string> stagingProjects = new HashSet<string>(Files.Comparer);

        Catalog(string bodyRoot, string dataRoot, string stagingRoot)
        {
            BodyRoot = bodyRoot;
            DataRoot = dataRoot;
            StagingRoot = stagingRoot;
        }

        internal static string GameData(string bodyRoot)
        {
            string config = Path.Combine(bodyRoot, "Config.xml");
            if (File.Exists(config))
            {
                string configured = Files.Text(Files.Xml(config).Root, "GameDataPath");
                if (Path.IsPathRooted(configured) && Directory.Exists(configured))
                    return Path.GetFullPath(configured);
            }
            var directory = new DirectoryInfo(bodyRoot);
            if (directory.Parent != null && directory.Parent.Parent != null &&
                Files.Comparer.Equals(directory.Parent.Parent.Name, "Data"))
                return directory.Parent.Parent.FullName;
            throw new DirectoryNotFoundException("Configura prima il percorso Data di Skyrim SE in BodySlide.");
        }

        internal static Catalog Scan(string staging, string bodyRoot)
        {
            staging = Path.GetFullPath(staging);
            bodyRoot = Path.GetFullPath(bodyRoot);
            if (!Directory.Exists(staging)) throw new DirectoryNotFoundException("Cartella Vortex Mods non trovata.");
            if (!File.Exists(Path.Combine(bodyRoot, "OutfitStudio.exe")))
                throw new FileNotFoundException("Scegli la cartella di BodySlide che contiene OutfitStudio.exe.");
            var catalog = new Catalog(bodyRoot, GameData(bodyRoot), staging);
            var vanilla = new HashSet<string>(Files.Comparer);
            foreach (string archive in Files.Children(catalog.DataRoot, "Skyrim - Meshes*.bsa", false))
            {
                try { vanilla.UnionWith(Bsa.MeshPaths(archive)); }
                catch (IOException error) { catalog.Notices.Add(Path.GetFileName(archive) + " — " + error.Message); }
            }
            if (vanilla.Count == 0)
                catalog.Notices.Add("The official Skyrim mesh archives could not be read.");
            var records = GameRecords.Read(catalog.DataRoot, catalog.Notices);

            foreach (string mod in Files.Children(staging, "*", true))
                foreach (string root in ModBodyRoots(mod))
                {
                    catalog.ReadProjects(root, mod, false);
                    catalog.ReadPresets(root, Path.GetFileName(mod), false);
                }
            catalog.ReadProjects(bodyRoot, null, true);
            catalog.ReadPresets(bodyRoot, "Skyrim Data", true);
            catalog.ReadGroups();

            foreach (var piece in catalog.Pieces)
            {
                if (!IsClothing(piece.Output)) continue;
                if (!vanilla.Contains(piece.Output + (piece.Weighted ? "_0.nif" : ".nif")) ||
                    (piece.Weighted && !vanilla.Contains(piece.Output + "_1.nif"))) continue;
                var candidate = new Target { Output = piece.Output, Weighted = piece.Weighted };
                Target target;
                if (!catalog.ByTarget.TryGetValue(candidate.Key, out target))
                {
                    target = candidate;
                    catalog.ByTarget.Add(target.Key, target);
                }
                if (!target.References.Any(reference => Files.Comparer.Equals(reference.Name, piece.Name)))
                    target.References.Add(piece);
            }
            foreach (string mesh in vanilla.Where(IsClothing))
            {
                if (!mesh.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)) continue;
                string output = mesh.Substring(0, mesh.Length - 4);
                bool weighted = output.EndsWith("_0", StringComparison.OrdinalIgnoreCase) &&
                    vanilla.Contains(output.Substring(0, output.Length - 2) + "_1.nif");
                if (output.EndsWith("_1", StringComparison.OrdinalIgnoreCase) &&
                    vanilla.Contains(output.Substring(0, output.Length - 2) + "_0.nif")) continue;
                if (weighted) output = output.Substring(0, output.Length - 2);
                var target = new Target { Output = output, Weighted = weighted };
                if (!catalog.ByTarget.ContainsKey(target.Key)) catalog.ByTarget.Add(target.Key, target);
            }
            var counterpartProjects = catalog.ByTarget.Values.SelectMany(target => target.References)
                .ToLookup(piece => EquipmentFamily(piece.Output), Files.Comparer);
            foreach (var target in catalog.ByTarget.Values)
            {
                target.Items = records.Items(target.Output);
                target.Addons = records.Addons(target.Output).ToArray();
                target.AddonName = records.AddonName(target.Output);
                target.ViewKind = target.View = records.View(target.Output);
                string stem = GameRecords.MeshStem(target.Output);
                bool firstNamed = Path.GetFileName(stem).StartsWith("1stperson", StringComparison.OrdinalIgnoreCase);
                target.FirstPerson = records.FirstPersonMeshes.Contains(stem) || firstNamed;
                target.ThirdPerson = records.ThirdPersonMeshes.Contains(stem) || !target.FirstPerson;
                target.HasSwap = records.HasSwap(target.Output);
                target.Slots = records.Slots(target.Output).ToArray();
                target.Category = target.FindCategory();
                target.NameFromBodySlide(target.References);
                if (target.BodySlideName == null)
                    target.NameFromBodySlide(counterpartProjects[EquipmentFamily(target.Output)]);
                target.Name = target.DisplayName(false);
            }
            catalog.Targets.AddRange(catalog.ByTarget.Values.OrderBy(target => target.Items.Count == 0)
                .ThenBy(target => target.Name, Files.Comparer).ThenBy(target => target.Output, Files.Comparer));
            catalog.Pieces.Sort((left, right) => {
                int order = Files.Comparer.Compare(left.Mod, right.Mod);
                return order == 0 ? Files.Comparer.Compare(left.Name, right.Name) : order;
            });
            return catalog;
        }

        private static string EquipmentFamily(string output)
        {
            string stem = GameRecords.MeshStem(output);
            return Regex.Replace(stem, @"(?<=[a-z])_?[fm](?=(?:_?var\w*|_?[01])?$)",
                "", RegexOptions.IgnoreCase);
        }

        private static bool IsClothing(string path)
        {
            if (!path.StartsWith("meshes\\", StringComparison.OrdinalIgnoreCase)) return false;
            return Regex.IsMatch(path, @"^meshes\\(?:dlc0[12]\\)?(?:armor|armors|clothes|clothing)\\", RegexOptions.IgnoreCase);
        }

        private static IEnumerable<string> ModBodyRoots(string mod)
        {
            foreach (string prefix in new[] { mod, Path.Combine(mod, "Data") })
            {
                string root = Path.Combine(prefix, @"CalienteTools\BodySlide");
                if (Directory.Exists(root)) yield return root;
            }
        }

        private void ReadProjects(string root, string mod, bool deployed)
        {
            string sliderSets = Path.Combine(root, "SliderSets");
            if (!Directory.Exists(sliderSets)) return;
            foreach (string file in Directory.EnumerateFiles(sliderSets, "*.*", SearchOption.AllDirectories)
                .Where(path => Files.Comparer.Equals(Path.GetExtension(path), ".osp") || Files.Comparer.Equals(Path.GetExtension(path), ".xml")))
            {
                try
                {
                    foreach (var definition in Files.Xml(file).Root.Elements("SliderSet"))
                    {
                        var output = definition.Element("OutputFile");
                        string name = (string)definition.Attribute("name");
                        string source = Files.Text(definition, "SourceFile");
                        if (String.IsNullOrWhiteSpace(name) || String.IsNullOrEmpty(source) || output == null) continue;
                        string folder = Files.Text(definition, "OutputPath").Replace('/', '\\').TrimEnd('\\');
                        string basename = output.Value.Trim();
                        if (basename.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
                            basename = basename.Substring(0, basename.Length - 4);
                        string mesh = folder.Length == 0 ? basename : Path.Combine(folder, basename);
                        string relativeProject = Files.Relative(sliderSets, file);
                        string stagedKey = relativeProject + "|" + name + "|" + mesh;
                        if (deployed && stagingProjects.Contains(stagedKey)) continue;
                        if (!deployed) stagingProjects.Add(stagedKey);
                        string id = Files.Id((deployed ? "Data" : Files.Relative(mod, root) + "|" + Path.GetFileName(mod))
                            + "|" + relativeProject + "|" + name);
                        if (ById.ContainsKey(id)) continue;
                        string weights = (string)output.Attribute("GenWeights");
                        string folderName = deployed ? null : Path.GetFileName(mod);
                        string modName, nexusId;
                        GetModIdentity(folderName, out modName, out nexusId);
                        var piece = new Piece {
                            Id = id, Mod = deployed ? "Skyrim Data" : modName,
                            ModFolder = mod, NexusId = nexusId,
                            BodyRoot = root, ProjectFile = file, Name = name,
                            DataFolder = Files.Text(definition, "DataFolder"), Source = source, Output = mesh,
                            Weighted = !Files.Comparer.Equals(weights, "false") && weights != "0",
                            Derived = definition.Element("BaseProject") != null,
                            HasSliderData = definition.Elements("Slider").Elements("Data").Any(),
                            ShapeNames = definition.Elements("Shape").Select(shape => shape.Value).ToArray()
                        };
                        Pieces.Add(piece);
                        ById.Add(id, piece);
                    }
                }
                catch (Exception error)
                {
                    if (!(error is XmlException || error is IOException || error is ArgumentException)) throw;
                    Notices.Add(Path.GetFileName(file) + " — " + error.Message);
                }
            }
        }

        internal static void GetModIdentity(string folderName, out string modName, out string nexusId)
        {
            var match = folderName == null ? Match.Empty : Regex.Match(folderName,
                @"^(.+?)(?:-(\d+)-[\w.-]+-\d{9,}(?:\.\d+)*| (\d+) \S+ \d{4}-\d{2}-\d{2}T\S+ \S+)$");
            modName = match.Success ? match.Groups[1].Value : folderName;
            nexusId = match.Success ? match.Groups[2].Value + match.Groups[3].Value : null;
        }

        private void ReadGroups()
        {
            string sliderGroups = Path.Combine(BodyRoot, "SliderGroups");
            if (!Directory.Exists(sliderGroups)) return;
            foreach (string file in Directory.EnumerateFiles(sliderGroups, "*.xml", SearchOption.AllDirectories))
            {
                try
                {
                    foreach (var group in Files.Xml(file).Root.Elements("Group"))
                    {
                        string name = (string)group.Attribute("name");
                        if (String.IsNullOrWhiteSpace(name)) continue;
                        foreach (var member in group.Elements("Member").Select(node => (string)node.Attribute("name")).Where(value => value != null))
                        {
                            HashSet<string> names;
                            if (!Groups.TryGetValue(member, out names)) Groups.Add(member, names = new HashSet<string>(Files.Comparer));
                            names.Add(name);
                        }
                    }
                }
                catch (Exception error)
                {
                    if (!(error is XmlException || error is IOException)) throw;
                    Notices.Add(Path.GetFileName(file) + " — " + error.Message);
                }
            }
        }
    }
}
