using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SkyrimOutfit
{
    internal sealed class BodyPreviewCache
    {
        private const int MemoryEntries = 18;
        private const long DiskBudget = 350L * 1024 * 1024;
        private readonly string folder;
        private readonly SemaphoreSlim build = new SemaphoreSlim(1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly LinkedList<string> recent = new LinkedList<string>();
        private readonly Dictionary<string, PreviewGeometry[]> memory = new Dictionary<string, PreviewGeometry[]>();
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Piece, InputKey> inputKeys =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Piece, InputKey>();

        internal BodyPreviewCache(string folder) { this.folder = folder; }
        internal void CancelBuilds() { lifetime.Cancel(); }

        internal async Task<PreviewGeometry[]> Get(Catalog catalog, Piece body, BodyPreset preset, bool lowWeight, CancellationToken token)
        {
            int weight = body.Weighted && lowWeight ? 0 : 1;
            string project = inputKeys.GetValue(body, piece => new InputKey(catalog, piece)).Value;
            string key = Files.Id(project + "\n" + (preset == null ? "" : preset.Definition.ToString(SaveOptions.DisableFormatting)));
            string selected = key + "-" + weight;
            PreviewGeometry[] shapes = Recall(selected);
            if (shapes != null) return shapes;
            string path = Path.Combine(folder, selected + ".nif");
            shapes = Read(path);
            if (shapes != null) return Remember(selected, shapes);

            await Task.Delay(125, token).ConfigureAwait(false);
            await build.WaitAsync(token).ConfigureAwait(false);
            try
            {
                shapes = Recall(selected) ?? Read(path);
                if (shapes != null) return Remember(selected, shapes);
                token.ThrowIfCancellationRequested();
                // A completed build remains useful when the user selects a different body during it.
                byte[][] meshes = Composer.BuildBodyPreviewMeshes(catalog, body, preset, lifetime.Token);
                var low = Decode(meshes[0]);
                var high = body.Weighted ? Decode(meshes[1]) : low;
                for (int i = 0; i < meshes.Length; i++) Save(Path.Combine(folder, key + "-" + i + ".nif"), meshes[i]);
                Trim();
                Remember(key + "-0", low);
                Remember(key + "-1", high);
                return weight == 0 ? low : high;
            }
            finally { build.Release(); }
        }

        private PreviewGeometry[] Recall(string key)
        {
            lock (memory)
            {
                PreviewGeometry[] result;
                if (!memory.TryGetValue(key, out result)) return null;
                recent.Remove(key);
                recent.AddLast(key);
                return result;
            }
        }

        private PreviewGeometry[] Remember(string key, PreviewGeometry[] shapes)
        {
            lock (memory)
            {
                recent.Remove(key);
                recent.AddLast(key);
                memory[key] = shapes;
                if (memory.Count > MemoryEntries)
                {
                    memory.Remove(recent.First.Value);
                    recent.RemoveFirst();
                }
            }
            return shapes;
        }

        private static PreviewGeometry[] Decode(byte[] bytes)
        {
            var result = new Nif("BodySlide preview", bytes, true, readSkin: true).PreviewShapes.ToArray();
            if (result.Length == 0) throw new InvalidDataException("Empty BodySlide preview.");
            return result;
        }

        private static PreviewGeometry[] Read(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var shapes = Decode(File.ReadAllBytes(path));
                try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                return shapes;
            }
            catch (InvalidDataException) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private void Save(string path, byte[] bytes)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            finally
            {
                try { File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private void Trim()
        {
            try
            {
                if (!Directory.Exists(folder)) return;
                var entries = new DirectoryInfo(folder).GetFiles("*.nif").OrderByDescending(file => file.LastWriteTimeUtc);
                long bytes = 0;
                foreach (var entry in entries)
                {
                    bytes += entry.Length;
                    if (bytes > DiskBudget) entry.Delete();
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private sealed class InputKey
        {
            internal readonly string Value;
            internal InputKey(Catalog catalog, Piece body)
            {
                var inputs = Composer.BodyInputs(catalog, body).Distinct(Files.Comparer).OrderBy(path => path, Files.Comparer)
                    .Select(path => {
                        var file = new FileInfo(path);
                        return path + "|" + (file.Exists ? file.Length.ToString(CultureInfo.InvariantCulture) + "|" +
                            file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : "-");
                    });
                Value = String.Join("\n", new[] { "body-preview-1", catalog.DataRoot, body.Id }.Concat(inputs));
            }
        }
    }
}
