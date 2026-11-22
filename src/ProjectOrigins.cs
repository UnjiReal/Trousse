using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SkyrimOutfit
{
    [DataContract]
    internal sealed class ProjectOrigin
    {
        [DataMember] public string Name, Output;
        [DataMember] public string[] Components;
    }

    internal static class ProjectOrigins
    {
        private const string Folder = "TrousseProvenance";

        internal static void Write(string body, string packId, IEnumerable<ProjectOrigin> outfits)
        {
            string folder = Path.Combine(body, Folder);
            Directory.CreateDirectory(folder);
            using (var stream = File.Create(Path.Combine(folder, packId + ".json")))
                new DataContractJsonSerializer(typeof(ProjectOrigin[])).WriteObject(stream, outfits.ToArray());
        }

        internal static string[] Read(IEnumerable<string> roots, HashSet<string> names, string stem, string baseMesh)
        {
            if (names.Count == 0) return new string[0];
            var results = new HashSet<string>(Files.Comparer);
            var serializer = new DataContractJsonSerializer(typeof(ProjectOrigin[]));
            foreach (string root in roots.Distinct(Files.Comparer))
            foreach (string file in Files.Children(Path.Combine(root, Folder), "*.json", false))
            {
                try
                {
                    using (var stream = File.OpenRead(file))
                    {
                        if (stream.Length > 2000000) continue;
                        var entries = (ProjectOrigin[])serializer.ReadObject(stream);
                        if (entries == null) continue;
                        foreach (var outfit in entries.Where(outfit => outfit != null && names.Contains(outfit.Name) &&
                            (Files.Comparer.Equals(outfit.Output, stem) || Files.Comparer.Equals(outfit.Output, baseMesh)) && outfit.Components != null))
                            results.UnionWith(outfit.Components.Where(component => !String.IsNullOrWhiteSpace(component)));
                    }
                }
                catch (IOException) { }
                catch (SerializationException) { }
                catch (UnauthorizedAccessException) { }
            }
            return results.OrderBy(component => component, Files.Comparer).ToArray();
        }
    }
}
