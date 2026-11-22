using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SkyrimOutfit
{
    internal sealed class PreviewTextures
    {
        private readonly Dictionary<string, BitmapSource> images = new Dictionary<string, BitmapSource>(Files.Comparer);
        private readonly Dictionary<string, Bsa.Index> archives = new Dictionary<string, Bsa.Index>(Files.Comparer);

        internal BitmapSource Load(string dataRoot, string modFolder, PreviewGeometry geometry, IEnumerable<string> installedArchives = null)
        {
            if (String.IsNullOrWhiteSpace(geometry.DiffuseTexture)) return null;
            string path = geometry.DiffuseTexture.Trim().Replace('/', '\\').TrimStart('\\');
            if (path.StartsWith("data\\", StringComparison.OrdinalIgnoreCase)) path = path.Substring(5);
            if (!path.StartsWith("textures\\", StringComparison.OrdinalIgnoreCase))
                path = "textures\\" + path;

            string key = dataRoot + "|" + modFolder + "|" + path + "|" + geometry.UsesAlpha;
            BitmapSource image;
            lock (images) if (images.TryGetValue(key, out image)) return image;
            image = Open(dataRoot, modFolder, path, geometry.UsesAlpha, installedArchives);
            lock (images)
            {
                if (images.Count >= 48) images.Clear();
                images[key] = image;
            }
            return image;
        }

        private BitmapSource Open(string data, string mod, string path, bool useAlpha, IEnumerable<string> installedArchives)
        {
            string[] sourceRoots = mod == null ? new string[0] : new[] { mod, Path.Combine(mod, "Data") };
            try
            {
                foreach (string root in sourceRoots.Concat(new[] { data }))
                {
                    string file = Files.PathIn(root, path);
                    if (!File.Exists(file)) continue;
                    using (var stream = File.OpenRead(file))
                    {
                        var image = Decode(stream, useAlpha);
                        if (image != null) return image;
                    }
                }
                var candidates = sourceRoots.SelectMany(root => Files.Children(root, "*.bsa", false))
                    .Concat(installedArchives ?? Enumerable.Empty<string>())
                    .Concat(Files.Children(data, "Skyrim - Textures*.bsa", false).OrderByDescending(name => name, Files.Comparer));
                foreach (string archive in candidates)
                {
                    using (var stream = OpenArchive(archive, path))
                        if (stream != null)
                        {
                            var image = Decode(stream, useAlpha);
                            if (image != null) return image;
                        }
                }
            }
            catch (IOException) { }
            catch (InvalidDataException) { }
            catch (UnauthorizedAccessException) { }
            catch (NotSupportedException) { }
            catch (ArgumentException) { }
            catch (FormatException) { }
            catch (System.Runtime.InteropServices.COMException) { }
            return null;
        }

        private Stream OpenArchive(string archive, string path)
        {
            Bsa.Index index;
            lock (archives)
            {
                if (!archives.TryGetValue(archive, out index))
                {
                    try { index = Bsa.LoadIndex(archive); }
                    catch (IOException) { index = null; }
                    if (archives.Count >= 22) archives.Clear();
                    archives.Add(archive, index);
                }
            }
            return index == null ? null : index.Open(archive, path);
        }

        private static BitmapSource Decode(Stream stream, bool useAlpha)
        {
            BitmapSource image;
            try
            {
                image = Bc7Preview.TryRead(stream);
                if (image == null)
                {
                    stream.Position = 0;
                    var decoded = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    image = decoded.Frames[0];
                }
            }
            catch (NotSupportedException) { return null; }
            catch (FormatException) { return null; }
            catch (System.Runtime.InteropServices.COMException) { return null; }
            double scale = Math.Min(1, 1024.0 / Math.Max(image.PixelWidth, image.PixelHeight));
            if (scale < 1) image = new TransformedBitmap(image, new ScaleTransform(scale, scale));
            PixelFormat format = useAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32;
            if (image.Format != format) image = new FormatConvertedBitmap(image, format, null, 0);
            int stride = image.PixelWidth * 4;
            byte[] pixels = new byte[stride * image.PixelHeight];
            image.CopyPixels(pixels, stride, 0);
            var result = BitmapSource.Create(image.PixelWidth, image.PixelHeight, 96, 96, format, null, pixels, stride);
            result.Freeze();
            return result;
        }
    }
}
