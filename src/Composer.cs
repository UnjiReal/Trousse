using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;

namespace SkyrimOutfit
{
    [DataContract]
    internal sealed class Pack
    {
        [DataMember] public string Name = "I miei replacer";
        [DataMember] public List<Recipe> Outfits = new List<Recipe>();

        internal static Pack Open(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var result = (Pack)new DataContractJsonSerializer(typeof(Pack)).ReadObject(stream);
                if (result == null || result.Outfits == null || result.Outfits.Any(outfit => outfit == null ||
                    outfit.Pieces == null || outfit.Output == null))
                    throw new InvalidDataException("Il file non contiene un progetto outfit valido.");
                return result;
            }
        }

        internal void Save(string path)
        {
            using (var stream = File.Create(path))
                new DataContractJsonSerializer(typeof(Pack)).WriteObject(stream, this);
        }
    }

    [DataContract]
    internal sealed class Recipe
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public string Output;
        [DataMember] public bool Weighted;
        [DataMember] public int Slot;
        [DataMember] public List<string> Pieces = new List<string>();
        [DataMember(EmitDefaultValue = false)] public string Modesty;
        [DataMember(EmitDefaultValue = false)] public string ModestyRace;
        [DataMember(EmitDefaultValue = false)] public List<string> RuleTargets;
        [DataMember(EmitDefaultValue = false)] public List<string> VariantIds;
        public string Mesh { get { return Output + (Weighted ? "_0.nif / _1.nif" : ".nif"); } }
        public int PieceCount { get { return Pieces.Count; } }
        public string BuildError { get; internal set; }
        public bool HasBuildError { get { return BuildError != null; } }
        internal string TargetKey { get { return Output + (Weighted ? "|pair" : "|single"); } }
    }

    internal sealed class Composer : IDisposable
    {
        private readonly Catalog catalog;
        private readonly string workspace, stage, package, packageBody;
        private readonly string studio, bodySlide;
        private readonly Dictionary<string, string> normalized = new Dictionary<string, string>(Files.Comparer);
        private readonly List<Job> generated = new List<Job>();
        private readonly string packId;
        private readonly Action<string> status;

        internal Composer(Catalog catalog, Pack pack, Action<string> status)
            : this(catalog, "SO-" + Files.FileLabel(pack.Name ?? "") + "-" + Guid.NewGuid().ToString("N").Substring(0, 10), status)
        {
            Validate(pack);
        }

        private Composer(Catalog catalog, string id, Action<string> status)
        {
            this.catalog = catalog;
            this.status = status;
            packId = id;
            workspace = Path.Combine(Path.GetTempPath(), "SkyrimOutfit-" + Guid.NewGuid().ToString("N"));
            stage = Path.Combine(workspace, "engine");
            package = Path.Combine(workspace, "mod");
            packageBody = Path.Combine(package, @"CalienteTools\BodySlide");
            studio = Path.Combine(stage, "OutfitStudio.exe");
            bodySlide = Path.Combine(workspace, "projects", "BodySlide.exe");
        }

        internal static PreviewGeometry[] BuildBodyPreview(Catalog catalog, Piece piece, BodyPreset preset, bool lowWeight,
            CancellationToken token)
        {
            using (var composer = new Composer(catalog, "TrousseBodyPreview", null))
                return new Nif("BodySlide", composer.BuildBodyPreviewMeshes(piece, preset, token)[lowWeight && piece.Weighted ? 0 : 1],
                    true).PreviewShapes.ToArray();
        }

        internal static byte[][] BuildBodyPreviewMeshes(Catalog catalog, Piece piece, BodyPreset preset, CancellationToken token)
        {
            using (var composer = new Composer(catalog, "TrousseBodyPreview", null))
                return composer.BuildBodyPreviewMeshes(piece, preset, token);
        }

        private byte[][] BuildBodyPreviewMeshes(Piece piece, BodyPreset preset, CancellationToken token)
        {
            string stem = BuildBodyNif(piece, preset, token);
            if (!piece.Weighted)
            {
                var body = File.ReadAllBytes(stem + ".nif");
                return new[] { body, body };
            }
            return new[] { File.ReadAllBytes(stem + "_0.nif"), File.ReadAllBytes(stem + "_1.nif") };
        }

        internal static void PreviewPose(Catalog catalog, Piece piece, BodyPreset preset, bool lowWeight)
        {
            using (var composer = new Composer(catalog, "TroussePosePreview", null))
            {
                string stem = composer.BuildBodyNif(piece, preset, CancellationToken.None);
                composer.OpenPreview(stem + (piece.Weighted ? (lowWeight ? "_0" : "_1") : "") + ".nif");
            }
        }

        private string BuildBodyNif(Piece piece, BodyPreset preset, CancellationToken token)
        {
            string projectDir = Path.GetDirectoryName(bodySlide);
            Directory.CreateDirectory(Path.Combine(stage, "SliderSets"));
            string sourceFile = Normalize(piece);
            token.ThrowIfCancellationRequested();
            var project = Files.Xml(sourceFile);
            var set = project.Root.Element("SliderSet");
            const string projectName = "Trousse Body Preview";
            set.SetAttributeValue("name", projectName);
            set.SetElementValue("OutputPath", @"meshes\TroussePreview");
            var output = set.Element("OutputFile");
            output.Value = "Body";
            output.SetAttributeValue("GenWeights", piece.Weighted ? "true" : "false");
            project.Save(sourceFile);

            CopyTree(stage, projectDir);
            CopyTree(Path.Combine(catalog.BodyRoot, "res"), Path.Combine(projectDir, "res"));
            File.Copy(Path.Combine(catalog.BodyRoot, "BodySlide.exe"), bodySlide);
            Directory.CreateDirectory(Path.Combine(projectDir, "SliderGroups"));
            new XElement("SliderGroups", new XElement("Group", new XAttribute("name", packId),
                new XElement("Member", new XAttribute("name", projectName)))).Save(Path.Combine(projectDir, @"SliderGroups\Preview.xml"));
            var settings = new XElement("BodySlideConfig", new XElement("SelectedOutfit", projectName));
            if (preset != null)
            {
                var names = new HashSet<string>(set.Elements("Slider").Select(slider => (string)slider.Attribute("name")), Files.Comparer);
                var clone = new XElement(preset.Definition);
                foreach (var slider in clone.Elements("SetSlider").Where(slider => !names.Contains((string)slider.Attribute("name"))).ToArray())
                    slider.Remove();
                if (!clone.Elements("SetSlider").Any()) throw new InvalidDataException("This preset has no sliders in common with " + piece.Name + ".");
                clone.SetAttributeValue("name", projectName);
                clone.SetAttributeValue("set", projectName);
                clone.Elements("Group").Remove();
                clone.AddFirst(new XElement("Group", new XAttribute("name", packId)));
                Directory.CreateDirectory(Path.Combine(projectDir, "SliderPresets"));
                new XElement("SliderPresets", clone).Save(Path.Combine(projectDir, @"SliderPresets\Preview.xml"));
                settings.Add(new XElement("SelectedPreset", projectName));
            }
            settings.Save(Path.Combine(projectDir, "BodySlide.xml"));
            string target = Path.Combine(workspace, "built");
            Directory.CreateDirectory(target);
            CreateConfig(target).Save(Path.Combine(projectDir, "Config.xml"));
            token.ThrowIfCancellationRequested();
            Execute(bodySlide, "--groupbuild " + Quote(packId) + " --targetdir " + Quote(target) + " --quiet", "Log_BS.txt", 45000, token);
            string stem = Path.Combine(target, @"meshes\TroussePreview\Body");
            if (!(piece.Weighted ? File.Exists(stem + "_0.nif") && File.Exists(stem + "_1.nif") : File.Exists(stem + ".nif")))
                throw new IOException("BodySlide did not produce a preview mesh." + Environment.NewLine + ReadLog(projectDir, "Log_BS.txt"));
            return stem;
        }

        internal void Build(Pack pack, bool includeModesty = true, bool italian = false)
        {
            if (includeModesty) ModestyRules.Resolve(catalog, pack.Outfits);
            status("Preparazione di Outfit Studio…");
            var variants = new VariantPatch();
            foreach (Recipe recipe in pack.Outfits)
            {
                try { variants.Add(catalog.ByTarget[recipe.TargetKey], recipe); }
                catch (InvalidDataException error) { throw PackageConflict.At(error, recipe, ConflictArea.Destination); }
            }
            PrepareEngine();
            int number = 0;
            foreach (Recipe recipe in pack.Outfits)
            {
                try { BuildOutfit(pack, recipe, ++number); }
                catch (PackageConflict) { throw; }
                catch (Exception error) { throw PackageConflict.At(error, recipe); }
            }
            WriteGroupsAndManifest(pack, generated);
            if (includeModesty) variants.Save(package, packId);
            ProjectOrigins.Write(packageBody, packId, generated.Select(job => new ProjectOrigin {
                Name = job.Name, Output = VariantPatch.Output(job.Recipe),
                Components = job.Pieces.Select(piece => piece.ModName + " / " + piece.Name).ToArray()
            }));
            PackageReports.Write(package, catalog, pack, includeModesty, italian,
                generated.ToDictionary(job => job.Recipe, job => job.Pieces), generated.Any(job => job.Physics.Link != null));
        }

        private void BuildOutfit(Pack pack, Recipe recipe, int number)
        {
            var steps = new XElement("AutomationScript", new XAttribute("version", "1"));
            string id = packId + "-" + number;
            string displayName = pack.Name.Trim() + " - " + recipe.Name.Trim();
            var pieces = ResolvePieces(recipe);
            var job = new Job { Recipe = recipe, Id = id, Name = displayName, Pieces = pieces };
            var inspections = pieces.Select(piece => ReadPiece(recipe, piece)).ToList();
            job.Physics = SmpConfiguration.Create(catalog, pieces, inspections, id, recipe);
            job.NeedsTorso = recipe.Slot == 32 && !IsFirstPerson(recipe) && pieces.Any(IsCaressSmp);
            var bodyShapes = new Dictionary<string, Piece>(Files.Comparer);
            var inputs = new List<Tuple<string, string>>();
            for (int i = 0; i < pieces.Count; i++)
            {
                Piece piece = pieces[i];
                var incomingBody = piece.ShapeNames.Where(IsBody).ToArray();
                string sourceFile;
                try { sourceFile = Normalize(piece); }
                catch (Exception error) { throw PackageConflict.At(error, recipe, ConflictArea.Parts, new[] { piece.Id }); }
                string sourceSet = piece.Name;
                if (bodyShapes.Count != 0 && i > 0)
                {
                    var duplicate = incomingBody.Where(shape => bodyShapes.ContainsKey(BodyPart(shape))).ToArray();
                    if (duplicate.Length != 0)
                    {
                        if (duplicate.Length == piece.ShapeNames.Length)
                            throw new PackageConflict(piece.Name + " contiene solo un corpo già presente in " + recipe.Name + ".",
                                ConflictArea.Parts, new[] { recipe }, duplicate.Select(shape => bodyShapes[BodyPart(shape)].Id).Concat(new[] { piece.Id }));
                        if (duplicate.Any(shape => shape.IndexOf(',') >= 0))
                            throw new PackageConflict("Nome forma con virgola non supportato: " + piece.Name,
                                ConflictArea.Parts, new[] { recipe }, new[] { piece.Id });
                        string trimmed = id + "-pezzo-" + i;
                        steps.Add(Step("ClearProject", ""));
                        steps.Add(Add(sourceFile, sourceSet));
                        steps.Add(Step("DeleteShape", piece.Name, "TargetMeshes", String.Join(", ", duplicate)));
                        steps.Add(Save(trimmed, trimmed, @"meshes\skyrimoutfit\temporaneo", true));
                        sourceFile = Path.Combine(stage, "SliderSets", trimmed + ".osp");
                        sourceSet = trimmed;
                    }
                }
                foreach (string shape in incomingBody)
                    if (!bodyShapes.ContainsKey(BodyPart(shape))) bodyShapes.Add(BodyPart(shape), piece);
                inputs.Add(Tuple.Create(sourceFile, sourceSet));
            }
            steps.Add(Step("ClearProject", ""));
            foreach (var input in inputs) steps.Add(Add(input.Item1, input.Item2));
            if (job.Physics.Link != null)
                steps.Add(Step("SetExtraData", displayName, "BlockType", "NiStringExtraData",
                    "Name", SmpConfiguration.ExtraDataName, "Value", job.Physics.Link));
            steps.Add(Save(id, displayName, VariantPatch.Output(recipe), recipe.Weighted));
            string automation = Path.Combine(stage, @"Automations\Compose.xml");
            steps.Save(automation);
            // Outfit Studio can retain root extra data after ClearProject. Give each output its own process.
            status("Fusione in Outfit Studio (" + number + "/" + pack.Outfits.Count + ") · " + recipe.Name);
            Execute(studio, "--automation Compose --single-instance no", "Log_OS.txt", 180000);
            status("Verifica del progetto e delle partizioni · " + recipe.Name);
            PackageJob(job);
            generated.Add(job);
        }

        private Nif ReadPiece(Recipe recipe, Piece piece)
        {
            try { return new Nif(piece.FindNif(catalog.BodyRoot)); }
            catch (Exception error) { throw PackageConflict.At(error, recipe, ConflictArea.Parts, new[] { piece.Id }); }
        }

        internal static void SaveOutfitReport(string path, Catalog catalog, Pack pack, bool italian)
        {
            using (var composer = new Composer(catalog, pack, null))
            {
                var components = pack.Outfits.ToDictionary(outfit => outfit, composer.ResolvePieces);
                PackageReports.WriteOutfitDocument(path, catalog, pack, italian, components);
            }
        }

        private List<Piece> ResolvePieces(Recipe recipe)
        {
            var pieces = recipe.Pieces.Select(pieceId => catalog.ById[pieceId]).ToList();
            var defaultBody = FindMissingSmpBody(recipe, pieces);
            if (defaultBody != null) pieces.Add(defaultBody);
            return pieces;
        }

        private bool IsFirstPerson(Recipe recipe)
        {
            Target target;
            return catalog.ByTarget.TryGetValue(recipe.TargetKey, out target) &&
                (target.ViewKind.Contains("first person") || Path.GetFileName(recipe.Output).StartsWith("1stperson", StringComparison.OrdinalIgnoreCase));
        }

        private Piece FindMissingSmpBody(Recipe recipe, List<Piece> pieces)
        {
            if (recipe.Slot != 32 || IsFirstPerson(recipe) ||
                pieces.Any(piece => piece.ShapeNames.Any(shape => BodyPart(shape) == "body"))) return null;
            if (!pieces.Any(IsCaressSmp)) return null;
            var body = catalog.Pieces.FirstOrDefault(piece => Catalog.IsBodyProject(piece) && !piece.Derived &&
                Files.Comparer.Equals(piece.Name, "CBBE 3BBB Body Amazing"));
            if (body == null)
                throw new PackageConflict("Caress of Venus (3BA) richiede un corpo. Installa CBBE 3BA o aggiungi il tuo progetto corpo dalla scheda Corpi.",
                    ConflictArea.Parts, new[] { recipe }, pieces.Where(IsCaressSmp).Select(piece => piece.Id));
            return body;
        }

        private static bool IsCaressSmp(Piece piece)
        {
            return Regex.IsMatch(piece.Name, @"\bCoV\b.*\bSMP\b.*\b3BA\b", RegexOptions.IgnoreCase);
        }

        internal void Archive(string destination)
        {
            status("Creazione dello ZIP per Vortex…");
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                ZipFile.CreateFromDirectory(package, temporary, CompressionLevel.Optimal, false);
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal void VerifyBuild(Pack pack)
        {
            status("Build di prova in BodySlide…");
            string isolated = Path.Combine(workspace, "verify");
            Directory.CreateDirectory(isolated);
            PrepareBodySlide();
            Execute(bodySlide, "--groupbuild " + Quote(packId) + " --targetdir " + Quote(isolated) +
                " --quiet --trimorphs", "Log_BS.txt", 120000 + 12000 * pack.Outfits.Count);
            foreach (var job in generated)
            {
                try { VerifyOutfit(job, isolated); }
                catch (Exception error) { throw PackageConflict.At(error, job.Recipe); }
            }
        }

        private void VerifyOutfit(Job job, string isolated)
        {
            var recipe = job.Recipe;
            var allowedSlots = new HashSet<int>(CompatibleSlots(recipe)) { recipe.Slot };
            foreach (string output in OutputFiles(recipe))
            {
                string nif = Files.PathIn(isolated, output);
                if (!File.Exists(nif)) throw new InvalidOperationException("BodySlide non ha generato " + output +
                    "." + Environment.NewLine + ReadLog(Path.GetDirectoryName(bodySlide), "Log_BS.txt"));
                var inspected = new Nif(nif);
                if (inspected.GeometryCount == 0)
                    throw new InvalidDataException("BodySlide ha generato una mesh senza geometria: " + output + ".");
                if (inspected.Slots.Any(slot => !allowedSlots.Contains(slot)))
                    throw new InvalidDataException("La build BodySlide ha cambiato le partizioni di " + output + ".");
                if (job.NeedsTorso && !inspected.HasTorsoSkin)
                    throw new InvalidDataException("La build BodySlide ha perso il corpo nello slot 32: " + output);
                job.Physics.Verify(inspected, output, true);
            }
        }

        internal void Preview()
        {
            string projects = PrepareBodySlide();
            status("Anteprima aperta in BodySlide. Chiudila per tornare alla composizione.");
            var osp = Directory.GetFiles(Path.Combine(projects, "SliderSets"), "*.osp").Single();
            OpenPreview(osp);
        }

        private void OpenPreview(string source)
        {
            using (var process = Launch(bodySlide, "--preview " + Quote(source), false))
            {
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("BodySlide non ha aperto l’anteprima." + Environment.NewLine +
                        ReadLog(Path.GetDirectoryName(bodySlide), "Log_BS.txt"));
            }
        }

        private string PrepareBodySlide()
        {
            string projects = Path.GetDirectoryName(bodySlide);
            CopyTree(packageBody, projects);
            foreach (var job in generated.Where(item => item.Physics.IsCombined))
            {
                // BodySlide can resolve a physics XML beside its source NIF during a local preview.
                string mesh = Files.PathIn(Path.Combine(projects, "ShapeData"), job.ShapePath);
                Files.Copy(Files.PathIn(package, job.Physics.Link),
                    Path.Combine(Path.GetDirectoryName(mesh), Path.GetFileName(job.Physics.Link)));
            }
            Files.Copy(Path.Combine(catalog.BodyRoot, "BodySlide.exe"), bodySlide);
            CopyTree(Path.Combine(stage, "res"), Path.Combine(projects, "res"));
            foreach (string folder in new[] { "SliderCategories", "SliderPresets" })
                CopyTree(Path.Combine(catalog.BodyRoot, folder), Path.Combine(projects, folder));
            var config = Files.Xml(Path.Combine(stage, "Config.xml"));
            config.Root.SetElementValue("ProjectPath", "");
            config.Save(Path.Combine(projects, "Config.xml"));
            string settings = Path.Combine(catalog.BodyRoot, "BodySlide.xml");
            if (File.Exists(settings)) Files.Copy(settings, Path.Combine(projects, "BodySlide.xml"));
            return projects;
        }

        private void PrepareEngine()
        {
            var version = FileVersionInfo.GetVersionInfo(Path.Combine(catalog.BodyRoot, "OutfitStudio.exe"));
            if (version.FileMajorPart < 6 || (version.FileMajorPart == 6 && version.FileMinorPart < 1))
                throw new InvalidOperationException("Serve Outfit Studio 6.1.0 o successivo per unire progetti con gli slider.");
            Files.Copy(Path.Combine(catalog.BodyRoot, "OutfitStudio.exe"), studio);
            foreach (string folder in new[] { "res", "RefTemplates", "ConversionSets" })
                CopyTree(Path.Combine(catalog.BodyRoot, folder), Path.Combine(stage, folder));
            foreach (string folder in new[] { "Automations", "SliderSets", "ShapeData", "output" })
                Directory.CreateDirectory(Path.Combine(stage, folder));
            CreateConfig(Path.Combine(stage, "output")).Save(Path.Combine(stage, "Config.xml"));
        }

        private XDocument CreateConfig(string output)
        {
            string configFile = Path.Combine(catalog.BodyRoot, "Config.xml");
            XDocument config = File.Exists(configFile) ? Files.Xml(configFile) : new XDocument(new XElement("Config"));
            config.Root.SetElementValue("TargetGame", "4");
            config.Root.SetElementValue("GameDataPath", catalog.DataRoot.TrimEnd('\\') + "\\");
            config.Root.SetElementValue("ProjectPath", "");
            config.Root.SetElementValue("OutputDataPath", output.TrimEnd('\\') + "\\");
            config.Root.SetElementValue("WarnMissingGamePath", "false");
            config.Root.SetElementValue("LogLevel", "3");
            return config;
        }

        private string Normalize(Piece piece)
        {
            string path;
            if (normalized.TryGetValue(piece.Id, out path)) return path;
            if (piece.Derived)
                throw new InvalidOperationException(piece.Name + " è un progetto derivato BodySlide 6.1. Salvalo da Outfit Studio come progetto autonomo prima di importarlo.");
            string folder = packId + @"\sorgenti\" + piece.Id;
            string data = Files.PathIn(Path.Combine(stage, "ShapeData"), folder);
            var clone = SourceSet(piece);
            clone.Elements("ReferenceInfo").Remove();
            clone.SetAttributeValue("id", null);
            clone.SetElementValue("DataFolder", folder);
            clone.SetElementValue("SourceFile", "source.nif");
            Files.Copy(piece.FindNif(catalog.BodyRoot), Path.Combine(data, "source.nif"));
            var shapes = clone.Elements("Shape").ToList();
            var used = new Dictionary<string, string>(Files.Comparer);
            foreach (var node in clone.Elements("Slider").Elements("Data"))
            {
                string original = node.Value.Trim();
                if (original.Length == 0) continue;
                string file, suffix;
                SplitMorph(original, out file, out suffix);
                string originalFile = FindMorph(catalog, piece, shapes, node, file);
                string local;
                if (!used.TryGetValue(originalFile, out local))
                {
                    local = "morph-" + (used.Count + 1) + Path.GetExtension(file).ToLowerInvariant();
                    Files.Copy(originalFile, Path.Combine(data, local));
                    used.Add(originalFile, local);
                }
                node.Value = local + suffix;
                node.SetAttributeValue("local", "true");
            }
            foreach (var shape in shapes) shape.SetAttributeValue("DataFolder", null);
            var document = new XDocument(new XElement("SliderSetInfo", new XAttribute("version", "2"), clone));
            path = Path.Combine(stage, "SliderSets", "source-" + piece.Id + ".osp");
            document.Save(path);
            normalized.Add(piece.Id, path);
            return path;
        }

        private static XElement SourceSet(Piece piece)
        {
            var set = Files.Xml(piece.ProjectFile).Root.Elements("SliderSet")
                .FirstOrDefault(candidate => (string)candidate.Attribute("name") == piece.Name);
            if (set == null) throw new InvalidDataException("Il progetto è cambiato. Ripeti la scansione: " + piece.Name);
            return set;
        }

        private static string FindMorph(Catalog catalog, Piece piece, List<XElement> shapes, XElement node, string file)
        {
            string target = (string)node.Attribute("target") ?? "";
            var shape = shapes.FirstOrDefault(item => ((string)item.Attribute("target") ?? item.Value) == target);
            string external = shape == null ? null : (string)shape.Attribute("DataFolder");
            var folders = IsTrue((string)node.Attribute("local")) || String.IsNullOrWhiteSpace(external)
                ? new[] { piece.DataFolder } : external.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            return piece.FindData(catalog.BodyRoot, folders, file);
        }

        internal static IEnumerable<string> BodyInputs(Catalog catalog, Piece piece)
        {
            yield return piece.ProjectFile;
            yield return piece.FindNif(catalog.BodyRoot);
            yield return Path.Combine(catalog.BodyRoot, "BodySlide.exe");
            yield return Path.Combine(catalog.BodyRoot, "Config.xml");
            var set = SourceSet(piece);
            var shapes = set.Elements("Shape").ToList();
            var sources = set.Elements("Slider").Elements("Data")
                .Where(node => !String.IsNullOrWhiteSpace(node.Value))
                .Select(node => {
                    string file, suffix;
                    SplitMorph(node.Value.Trim(), out file, out suffix);
                    return new { Node = node, File = file };
                }).GroupBy(source => new { source.File, Local = (string)source.Node.Attribute("local"),
                    Target = (string)source.Node.Attribute("target") }).Select(group => group.First());
            foreach (var source in sources) yield return FindMorph(catalog, piece, shapes, source.Node, source.File);
        }

        private void PackageJob(Job job)
        {
            string filename = job.Id + ".osp";
            var document = Files.Xml(Path.Combine(stage, "SliderSets", filename));
            XElement set = document.Root.Element("SliderSet");
            if (set == null || !Files.Comparer.Equals((string)set.Attribute("name"), job.Name) || !set.Elements("Shape").Any())
                throw new InvalidDataException("Outfit Studio non ha salvato le forme di " + job.Name + ".");
            set.Elements("ReferenceInfo").Remove();
            string dataFolder = Files.Text(set, "DataFolder");
            string shapePath = Path.Combine(dataFolder, Files.Text(set, "SourceFile"));
            job.ShapePath = shapePath;
            string source = Files.PathIn(Path.Combine(stage, "ShapeData"), shapePath);
            string target = Files.PathIn(Path.Combine(packageBody, "ShapeData"), shapePath);
            var nif = new Nif(source);
            nif.FitSlots(job.Recipe.Slot, CompatibleSlots(job.Recipe), job.NeedsTorso);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            nif.Save(target, job.Physics.Link);
            var packaged = new Nif(target);
            if (job.NeedsTorso && !packaged.HasTorsoSkin)
                throw new InvalidDataException("Il progetto ha perso il corpo nello slot 32: " + job.Name);
            job.Physics.Verify(packaged, job.Name, true);
            job.Physics.Save(package);

            var shapes = set.Elements("Shape").ToList();
            var used = new Dictionary<string, string>(Files.Comparer);
            foreach (var node in set.Elements("Slider").Elements("Data"))
            {
                string file, suffix;
                SplitMorph(node.Value.Trim(), out file, out suffix);
                string shapeTarget = (string)node.Attribute("target") ?? "";
                var shape = shapes.FirstOrDefault(item => ((string)item.Attribute("target") ?? item.Value) == shapeTarget);
                string external = shape == null ? null : (string)shape.Attribute("DataFolder");
                var folders = IsTrue((string)node.Attribute("local")) || String.IsNullOrWhiteSpace(external)
                    ? new[] { dataFolder } : external.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                var found = folders.Select(folder => Files.PathIn(Path.Combine(stage, "ShapeData"), Path.Combine(folder, file)))
                    .FirstOrDefault(File.Exists);
                if (found == null) throw new FileNotFoundException(job.Name + " — dati slider mancanti: " + file);
                string local;
                if (!used.TryGetValue(found, out local))
                {
                    local = "slider-" + (used.Count + 1) + Path.GetExtension(file).ToLowerInvariant();
                    Files.Copy(found, Files.PathIn(Path.Combine(packageBody, "ShapeData"), Path.Combine(dataFolder, local)));
                    used.Add(found, local);
                }
                node.Value = local + suffix;
                node.SetAttributeValue("local", "true");
            }
            foreach (var shape in shapes) shape.SetAttributeValue("DataFolder", null);
            if (job.Pieces.Any(piece => piece.HasSliderData) && used.Count == 0)
                throw new InvalidDataException("Il progetto risultante ha perso gli slider: " + job.Name);
            Directory.CreateDirectory(Path.Combine(packageBody, "SliderSets"));
            document.Save(Path.Combine(packageBody, "SliderSets", filename));
        }

        private IEnumerable<int> CompatibleSlots(Recipe recipe)
        {
            Target target;
            if (!catalog.ByTarget.TryGetValue(recipe.TargetKey, out target)) return new int[0];
            if (target.Slots.Contains(recipe.Slot)) return target.Slots;
            foreach (var reference in target.References)
            {
                try
                {
                    var slots = new Nif(reference.FindNif(catalog.BodyRoot)).Slots.Distinct().ToArray();
                    if (slots.Contains(recipe.Slot)) return slots;
                }
                catch (IOException) { }
                catch (InvalidDataException) { }
            }
            return new int[0];
        }

        private void WriteGroupsAndManifest(Pack pack, List<Job> jobs)
        {
            var groups = new Dictionary<string, HashSet<string>>(Files.Comparer) {
                { packId, new HashSet<string>(jobs.Select(job => job.Name), Files.Comparer) }
            };
            foreach (var job in jobs)
            {
                var inherited = new HashSet<string>(Files.Comparer);
                foreach (var piece in job.Pieces)
                {
                    HashSet<string> sourceGroups;
                    if (catalog.Groups.TryGetValue(piece.Name, out sourceGroups)) inherited.UnionWith(sourceGroups);
                    if (Regex.IsMatch(piece.Name, @"\b(?:3BA|3BBB)\b", RegexOptions.IgnoreCase)) inherited.Add("3BA");
                    if (Regex.IsMatch(piece.Name, @"\bCBBE\b", RegexOptions.IgnoreCase)) inherited.Add("CBBE");
                }
                foreach (string group in inherited)
                {
                    HashSet<string> members;
                    if (!groups.TryGetValue(group, out members)) groups.Add(group, members = new HashSet<string>(Files.Comparer));
                    members.Add(job.Name);
                }
            }
            Directory.CreateDirectory(Path.Combine(packageBody, "SliderGroups"));
            new XDocument(new XElement("SliderGroups", groups.Select(group =>
                new XElement("Group", new XAttribute("name", group.Key), group.Value.Select(member =>
                    new XElement("Member", new XAttribute("name", member)))))))
                .Save(Path.Combine(packageBody, "SliderGroups", packId + ".xml"));

            var readme = new StringBuilder().AppendLine(pack.Name.Trim()).AppendLine()
                .AppendLine("Installa lo ZIP in Vortex e fai Deploy Mods.")
                .AppendLine("In BodySlide cerca il nome del pacchetto, scegli il preset ed esegui Build.")
                .AppendLine("Per il Batch Build seleziona il gruppo " + packId + " e scegli questi progetti se compare un conflitto.")
                .AppendLine("Mantieni disponibili le texture e la fisica delle mod sorgenti e la base corpo usata dagli outfit.")
                .AppendLine("La destinazione delle mesh costruite segue la configurazione di BodySlide.").AppendLine();
            foreach (var job in jobs)
            {
                readme.AppendLine(job.Name).AppendLine(job.Recipe.Mesh + " · slot " + job.Recipe.Slot);
                if (job.Recipe.VariantIds != null && job.Recipe.VariantIds.Count > 0)
                    readme.AppendLine("  Variants: " + String.Join(", ", job.Recipe.VariantIds))
                        .AppendLine("  Enable the generated Variants ESP in Vortex before starting Skyrim.");
                foreach (var piece in job.Pieces) readme.AppendLine("  " + piece.Mod + " / " + piece.Name);
                if (job.Physics.Link != null) readme.AppendLine("  SMP: " + job.Physics.Link);
                if (job.Physics.IsCombined)
                    readme.AppendLine("  Configurazioni SMP riunite: " + String.Join(", ", job.Physics.Sources));
                readme.AppendLine();
            }
            File.WriteAllText(Path.Combine(package, packId + ".txt"), readme.ToString(), Encoding.UTF8);
        }

        internal static string ValidateMesh(string output)
        {
            string cleaned = (output ?? "").Trim().Replace('/', '\\');
            var segments = cleaned.Split('\\');
            if (segments.Length < 3 || !Files.Comparer.Equals(segments[0], "meshes") || segments.Any(segment =>
                segment.Length == 0 || segment == "." || segment == ".." || segment.EndsWith(".") ||
                segment.EndsWith(" ") || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                Regex.IsMatch(segment, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)))
                throw new InvalidDataException("Destinazione mesh non valida: " + output);
            return cleaned;
        }

        internal static IEnumerable<string> OutputFiles(Recipe recipe)
        {
            string output = VariantPatch.Output(recipe);
            return recipe.Weighted ? new[] { output + "_0.nif", output + "_1.nif" } : new[] { output + ".nif" };
        }

        private void Validate(Pack pack)
        {
            if (String.IsNullOrWhiteSpace(pack.Name) || pack.Outfits.Count == 0)
                throw new InvalidOperationException("Assegna un nome al pacchetto e aggiungi almeno un outfit.");
            if (pack.Name.Length > 72 || pack.Name.Any(Char.IsControl))
                throw new InvalidOperationException("Usa un nome pacchetto di massimo 72 caratteri, su una riga.");
            var names = new Dictionary<string, Recipe>(Files.Comparer);
            var outputs = new Dictionary<string, Recipe>(Files.Comparer);
            foreach (var recipe in pack.Outfits)
            {
                if (String.IsNullOrWhiteSpace(recipe.Name) || recipe.Name.Length > 100 || recipe.Name.Any(Char.IsControl))
                    throw new PackageConflict("Ogni outfit deve avere un nome distinto di massimo 100 caratteri.", ConflictArea.Name, new[] { recipe });
                Recipe previous;
                if (names.TryGetValue(recipe.Name.Trim(), out previous))
                    throw new PackageConflict("Due outfit usano lo stesso nome: " + recipe.Name, ConflictArea.Name, new[] { recipe, previous });
                names.Add(recipe.Name.Trim(), recipe);
                string mesh;
                try { mesh = ValidateMesh(recipe.Output); }
                catch (InvalidDataException error) { throw PackageConflict.At(error, recipe, ConflictArea.Destination); }
                if (!Files.Comparer.Equals(recipe.Output, mesh))
                    throw new PackageConflict("Riapri e salva la destinazione di " + recipe.Name + " per normalizzarne il percorso.",
                        ConflictArea.Destination, new[] { recipe });
                if (!catalog.ByTarget.ContainsKey(recipe.TargetKey))
                    throw new PackageConflict("Select an official Skyrim or DLC mesh for " + recipe.Name + ".", ConflictArea.Destination, new[] { recipe });
                if (recipe.Slot < 30 || recipe.Slot > 61)
                    throw new PackageConflict("Seleziona uno slot Skyrim da 30 a 61 per " + recipe.Name + ".", ConflictArea.Destination, new[] { recipe });
                foreach (string output in OutputFiles(recipe))
                {
                    if (outputs.TryGetValue(output, out previous))
                        throw new PackageConflict("Due outfit nel pacchetto scrivono sullo stesso file: " + output,
                            ConflictArea.Destination, new[] { recipe, previous });
                    outputs.Add(output, recipe);
                }
                var duplicates = recipe.Pieces.GroupBy(id => id, Files.Comparer).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
                if (recipe.Pieces.Count == 0 || duplicates.Length != 0)
                    throw new PackageConflict("Scegli almeno un componente distinto per " + recipe.Name + ".",
                        ConflictArea.Parts, new[] { recipe }, duplicates);
                foreach (var piece in recipe.Pieces)
                    if (piece == null || !catalog.ById.ContainsKey(piece))
                        throw new PackageConflict("Componente di " + recipe.Name + " non trovato. Controlla Vortex e il catalogo.",
                            ConflictArea.Parts, new[] { recipe }, new[] { piece });
            }
        }

        private static bool IsBody(string shape) { return BodyPart(shape) != ""; }

        internal static string BodyPart(string shape)
        {
            string name = Regex.Replace(shape.ToLowerInvariant(), @"[\s_.-]", "");
            if (Regex.IsMatch(name, @"^(3ba|3bbb|cbbe|cbbe3ba|cbbe3bbb|unp|uunp|bhunp|himbo|femalebody|malebody|body|bodyreference)$"))
                return "body";
            if (Regex.IsMatch(name, @"^(3ba|3bbb|cbbe|cbbe3ba)?(anus|vagina|hands|feet)$"))
                return Regex.Replace(name, @"^(cbbe3ba|cbbe|3ba|3bbb)", "");
            return "";
        }

        private static bool IsTrue(string value) { return value == "1" || Files.Comparer.Equals(value, "true"); }

        private static void SplitMorph(string data, out string file, out string suffix)
        {
            string value = data.Replace('/', '\\');
            int separator = value.LastIndexOf('\\');
            if (value.EndsWith(".bsd", StringComparison.OrdinalIgnoreCase))
            {
                file = value;
                suffix = "";
                return;
            }
            if (separator > 0 && value.Substring(0, separator).EndsWith(".osd", StringComparison.OrdinalIgnoreCase))
            {
                file = value.Substring(0, separator);
                suffix = value.Substring(separator);
                return;
            }
            throw new InvalidDataException("Riferimento slider non riconosciuto: " + data);
        }

        private static XElement Step(string type, string note, params string[] pairs)
        {
            var step = new XElement("Step", new XAttribute("type", type));
            if (note.Length != 0) step.Add(new XElement("Note", note));
            for (int p = 0; p < pairs.Length; p += 2) step.Add(new XElement(pairs[p], pairs[p + 1]));
            return step;
        }

        private static XElement Add(string path, string set)
        {
            return Step("AddProject", set, "SourceFile", path, "Set", set, "AppendNewSliders", "true");
        }

        private static XElement Save(string id, string displayName, string output, bool weighted)
        {
            return Step("SaveProject", displayName, "DisplayName", displayName, "OutputFileName", Path.GetFileName(output),
                "OutputDataPath", Path.GetDirectoryName(output), "SliderSetFile", id + ".osp",
                "ShapeDataFolder", id, "ShapeDataFile", "outfit.nif", "GenWeights", weighted ? "true" : "false");
        }

        private static void CopyTree(string source, string target)
        {
            if (!Directory.Exists(source)) return;
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file) == "__folder_managed_by_vortex") continue;
                Files.Copy(file, Path.Combine(target, Files.Relative(source, file)));
            }
        }

        private static string Quote(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }

        private Process Launch(string executable, string arguments, bool hidden)
        {
            return Process.Start(new ProcessStartInfo(executable, arguments) {
                WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false,
                WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
                CreateNoWindow = hidden
            });
        }

        private void Execute(string executable, string arguments, string log, int milliseconds,
            CancellationToken token = default(CancellationToken))
        {
            token.ThrowIfCancellationRequested();
            using (var process = Launch(executable, arguments, true))
            using (token.Register(() => StopProcess(process)))
            {
                if (!process.WaitForExit(milliseconds))
                {
                    StopProcess(process);
                    process.WaitForExit();
                    throw new TimeoutException(Path.GetFileName(executable) + " ha superato il tempo previsto.");
                }
                token.ThrowIfCancellationRequested();
                string details = ReadLog(Path.GetDirectoryName(executable), log);
                if (process.ExitCode == 0 && !details.Contains("Failed to load shape data")) return;
                throw new InvalidOperationException(Path.GetFileName(executable) + " ha segnalato un errore (" +
                    process.ExitCode + ")." + Environment.NewLine + details);
            }
        }

        private static void StopProcess(Process process)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { if (!process.HasExited) throw; }
        }

        private static string ReadLog(string directory, string name)
        {
            string path = Path.Combine(directory, name);
            string[] lines = File.Exists(path) ? File.ReadAllLines(path) : new string[0];
            return String.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - 22)));
        }

        public void Dispose()
        {
            try { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private sealed class Job
        {
            internal string Id, Name, ShapePath;
            internal Recipe Recipe;
            internal List<Piece> Pieces;
            internal SmpConfiguration Physics;
            internal bool NeedsTorso;
        }
    }
}
