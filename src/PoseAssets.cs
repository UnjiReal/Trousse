using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SkyrimOutfit
{
    internal sealed class PoseAssets
    {
        private const string Animations = @"Meshes\actors\character\animations";
        private static readonly Regex EventFile = new Regex(
            @"(?:^|\s)(?<event>\S+)\s+(?:""(?<file>[^""]+\.hkx)""|(?<file>[^\s""]+\.hkx))(?=\s|$)", RegexOptions.IgnoreCase);
        private static readonly HashSet<string> ImageExtensions =
            new HashSet<string>(new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp" }, Files.Comparer);
        private static readonly HashSet<string> ImageFolders =
            new HashSet<string>(new[] { "Screenshot", "Screenshots", "Preview", "Previews", "Images" }, Files.Comparer);

        private readonly string data;
        private readonly string[] roots;
        private readonly Dictionary<string, List<Clip>> clips = new Dictionary<string, List<Clip>>(Files.Comparer);
        private readonly Dictionary<string, string[]> images = new Dictionary<string, string[]>(Files.Comparer);

        internal PoseAssets(string data, string[] roots)
        {
            this.data = data;
            this.roots = roots;
            foreach (string root in (new[] { data }).Concat(roots).Where(root => !String.IsNullOrEmpty(root)))
            {
                string animations = Path.Combine(root, Animations);
                foreach (string file in FindFiles(animations, "FNIS_*_List.txt", SearchOption.AllDirectories))
                    IndexClips(animations, file);
            }
        }

        private void IndexClips(string animations, string list)
        {
            string name = Path.GetFileNameWithoutExtension(list);
            string group = name.Substring(5, name.Length - 10);
            string folder = Files.Relative(animations, Path.GetDirectoryName(list));
            try
            {
                foreach (string line in File.ReadLines(list))
                {
                    if (line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                    var match = EventFile.Match(line);
                    if (!match.Success) continue;
                    string file = match.Groups["file"].Value;
                    if (Path.IsPathRooted(file)) continue;
                    string path;
                    try { path = Files.PathIn(animations, Path.Combine(folder, file)); }
                    catch (InvalidDataException) { continue; }
                    catch (ArgumentException) { continue; }
                    if (!File.Exists(path)) continue;
                    string animation = match.Groups["event"].Value;
                    List<Clip> entries;
                    if (!clips.TryGetValue(animation, out entries)) clips.Add(animation, entries = new List<Clip>());
                    if (!entries.Any(clip => Files.Comparer.Equals(clip.Group, group) && Files.Comparer.Equals(clip.Filename, Path.GetFileName(path))))
                        entries.Add(new Clip { Group = group, Filename = Path.GetFileName(path), Path = path });
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
        }

        internal string Animation(PoseEntry pose)
        {
            if (pose == null || String.IsNullOrWhiteSpace(pose.PackKey)) return null;
            List<Clip> entries;
            if (clips.TryGetValue(pose.Event, out entries))
            {
                var matching = entries.Where(clip => Files.Comparer.Equals(clip.Group, pose.PackKey)).ToArray();
                if (matching.Length == 1) return matching[0].Path;
                if (matching.Length == 0 && entries.Count == 1) return entries[0].Path;
            }
            var packRoots = PackRoots(pose.PackKey);
            if (!String.IsNullOrEmpty(data)) packRoots = new[] { data }.Concat(packRoots);
            return packRoots.SelectMany(root => FindFiles(Path.Combine(root, Animations, pose.PackKey), "*.hkx", SearchOption.TopDirectoryOnly))
                .FirstOrDefault(file => Files.Comparer.Equals(Path.GetFileNameWithoutExtension(file), pose.Event));
        }

        internal string[] Images(PoseEntry pose, out bool exact)
        {
            exact = false;
            if (pose == null || String.IsNullOrEmpty(pose.PackKey)) return new string[0];
            string[] gallery;
            if (!images.TryGetValue(pose.PackKey, out gallery))
            {
                gallery = PackRoots(pose.PackKey)
                    .SelectMany(root => ImageFiles(Path.GetDirectoryName(root) == null || !Files.Comparer.Equals(Path.GetFileName(root), "Data")
                        ? root : Path.GetDirectoryName(root)))
                    .GroupBy(Path.GetFileName, Files.Comparer).Select(group => group.First())
                    .OrderBy(Path.GetFileName, Files.Comparer).ToArray();
                images.Add(pose.PackKey, gallery);
            }
            var matches = gallery.Where(file => Files.Comparer.Equals(Path.GetFileNameWithoutExtension(file), pose.Event) ||
                Files.Comparer.Equals(Path.GetFileNameWithoutExtension(file), pose.Name)).ToArray();
            exact = matches.Length != 0;
            return exact ? matches : gallery;
        }

        private IEnumerable<string> PackRoots(string key)
        {
            return roots.Where(root => File.Exists(Path.Combine(root, PoserFavorites.DataFolder, key + ".json")));
        }

        private static IEnumerable<string> ImageFiles(string root)
        {
            string[] subfolders;
            try { subfolders = Directory.GetDirectories(root); }
            catch (IOException) { return new string[0]; }
            catch (UnauthorizedAccessException) { return new string[0]; }
            var folders = subfolders.Where(folder => ImageFolders.Contains(Path.GetFileName(folder)));
            return FindFiles(root, "*", SearchOption.TopDirectoryOnly)
                .Concat(folders.SelectMany(folder => FindFiles(folder, "*", SearchOption.AllDirectories)))
                .Where(file => ImageExtensions.Contains(Path.GetExtension(file)));
        }

        private static string[] FindFiles(string folder, string glob, SearchOption depth)
        {
            try { return Directory.Exists(folder) ? Directory.GetFiles(folder, glob, depth) : new string[0]; }
            catch (IOException) { return new string[0]; }
            catch (UnauthorizedAccessException) { return new string[0]; }
        }

        private sealed class Clip
        {
            internal string Group, Filename, Path;
        }
    }
}
