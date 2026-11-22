using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace SkyrimOutfit
{
    internal sealed class SmpConfiguration
    {
        internal const string ExtraDataName = "HDT Skinned Mesh Physics Object";
        internal readonly string[] Sources;
        internal readonly string Link;
        private readonly XDocument combined;
        private readonly string[] colliderNames;

        private SmpConfiguration(string[] sources, string link, XDocument combined, string[] colliderNames)
        {
            Sources = sources;
            Link = link;
            this.combined = combined;
            this.colliderNames = colliderNames;
        }

        internal bool IsCombined { get { return combined != null; } }

        internal static SmpConfiguration Create(Catalog catalog, IList<Piece> pieces, IList<Nif> meshes, string id, Recipe outfit)
        {
            var sources = meshes.SelectMany(mesh => mesh.Physics).Distinct(Files.Comparer).ToArray();
            if (sources.Length == 0)
                return new SmpConfiguration(sources, null, null, new string[0]);

            var merge = new Merge();
            foreach (var source in sources)
            {
                string relative = source.TrimStart('\\');
                if (relative.StartsWith(@"Data\", StringComparison.OrdinalIgnoreCase)) relative = relative.Substring(5);
                var candidates = new List<string> { Files.PathIn(catalog.DataRoot, relative) };
                for (int i = 0; i < pieces.Count; i++)
                {
                    if (!meshes[i].Physics.Contains(source)) continue;
                    var toolsFolder = Directory.GetParent(pieces[i].BodyRoot);
                    if (toolsFolder != null && toolsFolder.Parent != null)
                        candidates.Add(Files.PathIn(toolsFolder.Parent.FullName, relative));
                }
                string path = candidates.FirstOrDefault(File.Exists);
                try
                {
                    if (path == null) throw new FileNotFoundException("Per la fisica SMP serve il file " + source + " nella mod sorgente o in Data.");
                    var root = Files.Xml(path).Root;
                    if (sources.Length == 1)
                    {
                        var names = root.Descendants().Where(element =>
                                element.Name.LocalName == "per-vertex-shape" || element.Name.LocalName == "per-triangle-shape")
                            .Select(element => (string)element.Attribute("name"))
                            .Where(name => name != null && meshes.Any(mesh => mesh.GeometryNames.Contains(name)))
                            .Distinct(Files.Comparer).ToArray();
                        return new SmpConfiguration(sources, relative, null, names);
                    }
                    merge.Add(root, source);
                }
                catch (Exception error)
                {
                    var collision = error as ColliderConflict;
                    var affected = Enumerable.Range(0, pieces.Count).Where(i => meshes[i].Physics.Contains(source) ||
                        (collision != null && meshes[i].Physics.Contains(collision.PreviousSource))).Select(i => pieces[i].Id);
                    string message = error is XmlException ? "XML SMP non valido: " + source + " — " + error.Message : error.Message;
                    throw new PackageConflict(message, ConflictArea.Parts, new[] { outfit }, affected, error);
                }
            }
            return new SmpConfiguration(sources, @"meshes\SkyrimOutfit\" + id + ".xml",
                merge.Document(), merge.ColliderNames.Where(name => meshes.Any(mesh => mesh.GeometryNames.Contains(name))).ToArray());
        }

        internal void Save(string package)
        {
            if (!IsCombined) return;
            string path = Files.PathIn(package, Link);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            combined.Save(path);
        }

        internal void Verify(Nif mesh, string output, bool checkColliders)
        {
            if (Link != null && !mesh.HasRootPhysics)
                throw new InvalidDataException("La mesh ha perso il riferimento SMP del nodo principale: " + output + ".");
            if (!mesh.Physics.SetEquals(Link == null ? new string[0] : new[] { Link }))
                throw new InvalidDataException("Riferimento SMP inatteso in " + output + ". Atteso: " +
                    (Link ?? "nessuno") + ". Trovati: " + String.Join(", ", mesh.Physics.DefaultIfEmpty("nessuno")) + ".");
            if (checkColliders)
            {
                string[] missing = colliderNames.Where(name => !mesh.GeometryNames.Contains(name)).ToArray();
                if (missing.Length > 0)
                    throw new InvalidDataException("Le forme richieste dalla fisica SMP mancano in " + output +
                        ": " + String.Join(", ", missing) + ".");
            }
        }

        private sealed class Merge
        {
            private readonly List<XElement> templates = new List<XElement>();
            private readonly List<Definition> bones = new List<Definition>();
            private readonly List<XElement> colliders = new List<XElement>();
            private readonly List<XElement> constraints = new List<XElement>();
            private readonly Dictionary<string, Definition> boneByName = new Dictionary<string, Definition>(Files.Comparer);
            private readonly Dictionary<string, XElement> colliderByName = new Dictionary<string, XElement>(Files.Comparer);
            private readonly Dictionary<string, string> colliderSources = new Dictionary<string, string>(Files.Comparer);
            private readonly Dictionary<string, string> currentTemplates = new Dictionary<string, string>(Files.Comparer);

            internal IEnumerable<string> ColliderNames { get { return colliderByName.Keys; } }

            internal void Add(XElement root, string source)
            {
                if (root == null || root.Name != "system")
                    throw new InvalidDataException("La configurazione SMP deve avere una radice <system>: " + source);
                currentTemplates.Clear();
                foreach (var original in root.Elements())
                {
                    var node = new XElement(original);
                    string type = node.Name.LocalName;
                    switch (type)
                    {
                        case "bone-default":
                        case "generic-constraint-default":
                        case "stiffspring-constraint-default":
                        case "conetwist-constraint-default":
                        case "per-triangle-shape-default":
                        case "per-vertex-shape-default":
                            AddTemplate(node);
                            break;
                        case "bone":
                            AddBone(node, source);
                            break;
                        case "per-triangle-shape":
                        case "per-vertex-shape":
                            AddCollider(node, source);
                            break;
                        case "generic-constraint":
                        case "stiffspring-constraint":
                        case "conetwist-constraint":
                            ApplyTemplate(node);
                            constraints.Add(node);
                            break;
                        case "constraint-group":
                            AddGroup(node, source);
                            break;
                        default:
                            throw Unsupported(source, type);
                    }
                }
            }

            private void AddTemplate(XElement node)
            {
                // Anonymous defaults are sequential within a file. Naming each version preserves
                // that inheritance without passing it to the next source's anonymous default.
                string oldName = (string)node.Attribute("name") ?? "";
                string alias = "so-template-" + (templates.Count + 1);
                string family = node.Name.LocalName;
                node.SetAttributeValue("extends", Template(family, (string)node.Attribute("extends") ?? ""));
                node.SetAttributeValue("name", alias);
                currentTemplates[family + "|" + oldName] = alias;
                templates.Add(node);
            }

            private string Template(string family, string name)
            {
                string alias;
                if (currentTemplates.TryGetValue(family + "|" + name, out alias)) return alias;
                return currentTemplates.TryGetValue(family + "|", out alias) ? alias : null;
            }

            private void ApplyTemplate(XElement node)
            {
                node.SetAttributeValue("template",
                    Template(node.Name.LocalName + "-default", (string)node.Attribute("template") ?? ""));
            }

            private void AddBone(XElement node, string source)
            {
                string name = Name(node, source);
                bool explicitDefinition = node.HasElements || !String.IsNullOrWhiteSpace((string)node.Attribute("template"));
                ApplyTemplate(node);
                Definition previous;
                if (boneByName.TryGetValue(name, out previous))
                {
                    // A bare declaration often only provides a collision anchor for another garment.
                    // Use its detailed definition if the selected pieces also animate that bone.
                    if (explicitDefinition && !previous.Explicit)
                    {
                        previous.Node = node;
                        previous.Explicit = true;
                    }
                    return;
                }
                var bone = new Definition { Node = node, Explicit = explicitDefinition };
                boneByName.Add(name, bone);
                bones.Add(bone);
            }

            private void AddCollider(XElement node, string source)
            {
                string name = Name(node, source);
                ApplyTemplate(node);
                XElement previous;
                if (colliderByName.TryGetValue(name, out previous))
                {
                    if (!XNode.DeepEquals(WithoutComments(previous), WithoutComments(node)))
                        throw new ColliderConflict("I file SMP definiscono in modo diverso la stessa forma di collisione “" +
                            name + "” (" + colliderSources[name] + " / " + source + ").", colliderSources[name]);
                    return;
                }
                colliderByName.Add(name, node);
                colliderSources.Add(name, source);
                colliders.Add(node);
            }

            private void AddGroup(XElement group, string source)
            {
                foreach (var node in group.Elements().ToArray())
                {
                    switch (node.Name.LocalName)
                    {
                        case "generic-constraint-default":
                        case "stiffspring-constraint-default":
                        case "conetwist-constraint-default":
                            node.Remove();
                            AddTemplate(node);
                            break;
                        case "generic-constraint":
                        case "stiffspring-constraint":
                        case "conetwist-constraint":
                            ApplyTemplate(node);
                            break;
                        default:
                            throw Unsupported(source, node.Name.LocalName);
                    }
                }
                constraints.Add(group);
            }

            internal XDocument Document()
            {
                return new XDocument(new XElement("system", templates, bones.Select(bone => bone.Node), colliders, constraints));
            }

            private static string Name(XElement node, string source)
            {
                string name = (string)node.Attribute("name");
                if (String.IsNullOrWhiteSpace(name))
                    throw new InvalidDataException("Elemento SMP senza nome in " + source + ": " + node.Name + ".");
                return name;
            }

            private static XElement WithoutComments(XElement node)
            {
                var result = new XElement(node);
                foreach (var comment in result.DescendantNodes().OfType<XComment>().ToArray()) comment.Remove();
                return result;
            }

            private static InvalidDataException Unsupported(string source, string type)
            {
                return new InvalidDataException("La fusione SMP non supporta l’elemento “" + type + "” in " + source + ".");
            }

            private sealed class Definition
            {
                internal XElement Node;
                internal bool Explicit;
            }
        }

        private sealed class ColliderConflict : Exception
        {
            internal readonly string PreviousSource;
            internal ColliderConflict(string message, string source) : base(message) { PreviousSource = source; }
        }
    }
}
