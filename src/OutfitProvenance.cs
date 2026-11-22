using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace SkyrimOutfit
{
    internal enum ProjectEvidence { None, Geometry, BuildLog }

    internal sealed class InstalledSource
    {
        internal readonly string[] Projects, Mods, Components;
        internal readonly string DeployedBy;
        internal readonly ProjectEvidence Evidence;

        internal InstalledSource(string owner, ProjectEvidence evidence, string[] projects, string[] mods, string[] components)
        {
            DeployedBy = owner;
            Evidence = evidence;
            Projects = projects;
            Mods = mods;
            Components = components;
        }
    }

    internal sealed class OutfitProvenance
    {
        private static readonly Regex LogHeader = new Regex(@"^\[#Log\s+(.+)\]$");
        private static readonly Regex LogTime = new Regex(@"^\[(\d{2}:\d{2}:\d{2})\]");
        private static readonly Regex Building = new Regex(@"Building set '(.+)' with options:");
        private static readonly Regex NexusSuffix = new Regex(@"(?:-\d+-[\w.-]+-\d{9,}(?:\.\d+)*| \d+ \S+ \d{4}-\d{2}-\d{2}T\S+ \S+)$");
        private readonly Dictionary<string, DeploymentFile> deployment;
        private readonly ILookup<string, Build> builds;
        private readonly string staging;

        internal OutfitProvenance(string staging, string data, string body)
        {
            this.staging = staging;
            deployment = ReadDeployment(Path.Combine(data, "vortex.deployment.json"));
            builds = body == null ? Enumerable.Empty<Build>().ToLookup(build => build.Mesh, Files.Comparer)
                : ReadBuilds(Path.Combine(body, "Log_BS.txt")).ToLookup(build => build.Mesh, Files.Comparer);
        }

        internal InstalledSource Describe(InstalledMesh mesh, string baseMesh, Catalog catalog, PreviewGeometry[] shapes)
        {
            string owner = DeploymentOwner(mesh);
            var empty = new string[0];
            if (mesh.Path == null) return new InstalledSource(owner, ProjectEvidence.None, empty, empty, empty);
            string stem = GameRecords.MeshStem(mesh.Relative);
            var candidates = catalog.Pieces.Where(piece => SameOutput(piece, stem, baseMesh)).ToArray();
            Build log = FindBuild(mesh, baseMesh);
            var evidence = log != null ? ProjectEvidence.BuildLog : ProjectEvidence.None;
            Piece[] projects;
            if (log != null)
            {
                projects = candidates.Where(piece => Files.Comparer.Equals(piece.Name, log.Project)).ToArray();
                if (projects.Length == 0)
                    projects = catalog.Pieces.Where(piece => Files.Comparer.Equals(piece.Name, log.Project)).ToArray();
            }
            else
            {
                var signature = Signatures(shapes).ToArray();
                projects = signature.Length == 0 ? new Piece[0] : candidates
                    .Where(piece => SameGeometry(piece, catalog.BodyRoot, signature)).ToArray();
                if (projects.Length != 0) evidence = ProjectEvidence.Geometry;
            }
            var deployedProjects = projects.Where(project => IsDeployedProject(project, catalog.BodyRoot)).ToArray();
            if (deployedProjects.Length != 0) projects = deployedProjects;
            var names = log == null ? projects.Select(piece => piece.Name).Distinct(Files.Comparer).ToArray() : new[] { log.Project };
            var mods = projects.Where(piece => piece.ModFolder != null).Select(piece => piece.ModName).Distinct(Files.Comparer).ToArray();
            var projectNames = new HashSet<string>(names, Files.Comparer);
            string[] components = ProjectOrigins.Read(projects.Select(piece => piece.BodyRoot).Concat(new[] { catalog.BodyRoot }), projectNames, stem, baseMesh);
            return new InstalledSource(owner, evidence, names, mods, components);
        }

        private bool IsDeployedProject(Piece project, string body)
        {
            try
            {
                string relative = Files.Relative(project.BodyRoot, project.ProjectFile);
                string deployed = Files.PathIn(body, relative);
                if (!File.Exists(deployed)) return false;
                string folder = project.ModFolder == null ? null : Path.GetFileName(project.ModFolder);
                DeploymentFile entry;
                if (folder != null && deployment.TryGetValue(@"CalienteTools\BodySlide\" + relative, out entry) &&
                    !Files.Comparer.Equals(folder, entry.Source)) return false;
                return Files.Comparer.Equals(deployed, project.ProjectFile) || SameFile(deployed, project.ProjectFile);
            }
            catch (IOException) { return false; }
            catch (InvalidDataException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private Build FindBuild(InstalledMesh mesh, string baseMesh)
        {
            DateTime written = File.GetLastWriteTime(mesh.Path);
            string weightSuffix = mesh.Relative.EndsWith("_0.nif", StringComparison.OrdinalIgnoreCase) ? "_0.nif"
                : mesh.Relative.EndsWith("_1.nif", StringComparison.OrdinalIgnoreCase) ? "_1.nif" : ".nif";
            var possible = builds[mesh.Relative].Concat(builds[baseMesh + weightSuffix]).Distinct().ToArray();
            foreach (var build in possible.OrderBy(build => Math.Abs((build.Written - written).TotalSeconds)).ThenByDescending(build => build.Written))
            {
                if (Math.Abs((build.Written - written).TotalSeconds) <= 6) return build;
                if (File.Exists(build.Path) && Math.Abs((build.Written - File.GetLastWriteTime(build.Path)).TotalSeconds) <= 6 &&
                    SameFile(mesh.Path, build.Path)) return build;
            }
            return null;
        }

        private static bool SameOutput(Piece piece, string stem, string baseMesh)
        {
            string output = GameRecords.MeshStem(piece.Output);
            return Files.Comparer.Equals(output, stem) || Files.Comparer.Equals(output, baseMesh);
        }

        private static IEnumerable<string> Signatures(IEnumerable<PreviewGeometry> shapes)
        {
            return shapes.Where(shape => Composer.BodyPart(shape.Name) == "")
                .Select(shape => shape.Name + "|" + shape.Positions.Count + "|" + shape.Indices.Count + "|" +
                    (shape.DiffuseTexture ?? "").Replace('/', '\\'));
        }

        private static bool SameGeometry(Piece piece, string body, string[] expected)
        {
            try
            {
                var available = Signatures(new Nif(piece.FindNif(body), true).PreviewShapes).ToList();
                foreach (string signature in expected)
                {
                    int index = available.FindIndex(value => Files.Comparer.Equals(value, signature));
                    if (index < 0) return false;
                    available.RemoveAt(index);
                }
                return true;
            }
            catch (IOException) { return false; }
            catch (InvalidDataException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private string DeploymentOwner(InstalledMesh mesh)
        {
            string file = mesh.Archive == null ? mesh.Relative : Path.GetFileName(mesh.Archive);
            DeploymentFile entry;
            if (!deployment.TryGetValue(file, out entry)) return null;
            if (mesh.Path != null)
            {
                try
                {
                    string root = Files.PathIn(staging, entry.Source);
                    if (!SameFile(mesh.Path, Files.PathIn(root, entry.Relative)) &&
                        !SameFile(mesh.Path, Files.PathIn(Path.Combine(root, "Data"), entry.Relative))) return null;
                }
                catch (IOException) { return null; }
                catch (InvalidDataException) { return null; }
                catch (UnauthorizedAccessException) { return null; }
            }
            return NexusSuffix.Replace(entry.Source, "");
        }

        private static bool SameFile(string left, string right)
        {
            var from = new FileInfo(left);
            var to = new FileInfo(right);
            if (!from.Exists || !to.Exists || from.Length != to.Length) return false;
            using (var first = from.OpenRead())
            using (var second = to.OpenRead())
            using (var sha = SHA256.Create())
                return sha.ComputeHash(first).SequenceEqual(sha.ComputeHash(second));
        }

        internal static string DeployedMod(string data, string relative)
        {
            DeploymentFile entry;
            return ReadDeployment(Path.Combine(data, "vortex.deployment.json"), relative).TryGetValue(relative, out entry)
                ? entry.Source : null;
        }

        private static Dictionary<string, DeploymentFile> ReadDeployment(string file, string only = null)
        {
            var sources = new Dictionary<string, DeploymentFile>(Files.Comparer);
            try
            {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = JsonReaderWriterFactory.CreateJsonReader(stream, new XmlDictionaryReaderQuotas { MaxStringContentLength = 65536 }))
                {
                    if (!reader.ReadToFollowing("files")) return sources;
                    var serializer = new DataContractJsonSerializer(typeof(DeploymentFile));
                    using (var entries = reader.ReadSubtree())
                    while (!entries.EOF)
                    {
                        if (entries.Depth != 1 || entries.NodeType != XmlNodeType.Element) { entries.Read(); continue; }
                        var entry = (DeploymentFile)serializer.ReadObject(entries, false);
                        if (entry == null || String.IsNullOrWhiteSpace(entry.Source) || String.IsNullOrWhiteSpace(entry.Relative) ||
                            entry.Source.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                        string relative = (entry.Target + "\\" + entry.Relative).Replace('/', '\\').TrimStart('\\');
                        if (only != null)
                        {
                            if (Files.Comparer.Equals(only, relative)) { sources[relative] = entry; break; }
                            continue;
                        }
                        if (relative.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".bsa", StringComparison.OrdinalIgnoreCase) ||
                            relative.StartsWith(@"CalienteTools\BodySlide\SliderSets\", StringComparison.OrdinalIgnoreCase))
                            sources[relative] = entry;
                    }
                }
            }
            catch (IOException) { }
            catch (XmlException) { }
            catch (SerializationException) { }
            catch (UnauthorizedAccessException) { }
            return sources;
        }

        private static List<Build> ReadBuilds(string file)
        {
            var result = new List<Build>();
            DateTime day = DateTime.MinValue;
            TimeSpan previous = TimeSpan.Zero;
            DateTime success = DateTime.MinValue;
            string project = null;
            try
            {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    if (stream.Length > 16000000) { stream.Seek(-16000000, SeekOrigin.End); reader.ReadLine(); }
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        Match header = LogHeader.Match(line);
                        Match timestamp = LogTime.Match(line);
                        DateTime date;
                        TimeSpan time;
                        if (header.Success)
                        {
                            if (DateTime.TryParseExact(Regex.Replace(header.Groups[1].Value, @"\s+", " "), "ddd MMM d HH:mm:ss yyyy",
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) day = date.Date;
                            else day = DateTime.MinValue;
                            project = null;
                            success = DateTime.MinValue;
                            previous = TimeSpan.Zero;
                        }
                        else if (timestamp.Success && TimeSpan.TryParse(timestamp.Groups[1].Value, out time))
                        {
                            if (day != DateTime.MinValue && previous - time > TimeSpan.FromHours(12)) day = day.AddDays(1);
                            previous = time;
                            success = DateTime.MinValue;
                            var build = Building.Match(line);
                            if (build.Success) project = line.IndexOf("Cleaning = True", StringComparison.OrdinalIgnoreCase) < 0 ? build.Groups[1].Value : null;
                            if (project != null && day != DateTime.MinValue && line.Contains("Successfully processed the following files:"))
                                success = day.Add(time);
                        }
                        else if (success != DateTime.MinValue)
                        {
                            string path = line.Trim().Replace('/', '\\');
                            int boundary = path.LastIndexOf("\\meshes\\", StringComparison.OrdinalIgnoreCase);
                            if (boundary >= 0 && Path.IsPathRooted(path) && path.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
                                result.Add(new Build { Project = project, Path = path, Mesh = path.Substring(boundary + 1), Written = success });
                        }
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return result;
        }

        private sealed class Build
        {
            internal string Project, Path, Mesh;
            internal DateTime Written;
        }

        [DataContract]
        private sealed class DeploymentFile
        {
            [DataMember(Name = "source")] public string Source { get; set; }
            [DataMember(Name = "relPath")] public string Relative { get; set; }
            [DataMember(Name = "target")] public string Target { get; set; }
        }
    }
}
