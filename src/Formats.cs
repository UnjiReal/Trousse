using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Media3D;

namespace SkyrimOutfit
{
    internal static class Bsa
    {
        internal static IEnumerable<string> MeshPaths(string archive)
        {
            return LoadIndex(archive).Entries.Keys.Where(item => item.EndsWith(".nif", StringComparison.OrdinalIgnoreCase));
        }

        internal static Dictionary<string, byte[]> LoadFiles(string archive, Func<string, bool> include)
        {
            var index = LoadIndex(archive);
            var result = new Dictionary<string, byte[]>(Files.Comparer);
            using (var reader = new BinaryReader(File.OpenRead(archive)))
            foreach (var entry in index.Entries.Where(item => include(item.Key)))
                result.Add(entry.Key, ReadEntry(reader, index.Flags, entry.Value));
            return result;
        }

        private static byte[] ReadEntry(BinaryReader reader, uint flags, Tuple<uint, uint> entry)
        {
            reader.BaseStream.Position = entry.Item2;
            int size = (int)(entry.Item1 & 0x3FFFFFFF);
            bool compressed = ((flags & 4) != 0) != ((entry.Item1 & 0x40000000) != 0);
            if ((flags & 0x100) != 0)
            {
                int length = reader.ReadByte();
                reader.ReadBytes(length);
                size -= length + 1;
            }
            if (size < 0 || size > 30000000) throw new IOException("Invalid BSA entry size");
            if (!compressed) return reader.ReadBytes(size);
            int expanded = reader.ReadInt32();
            if (size < 4 || expanded < 0 || expanded > 60000000) throw new IOException("Invalid BSA compression size");
            return DecodeLz4(reader.ReadBytes(size - 4), expanded);
        }

        internal static Index LoadIndex(string archive)
        {
            var result = new Index();
            using (var reader = new BinaryReader(File.OpenRead(archive), Encoding.ASCII))
            {
                if (reader.ReadUInt32() != 0x00415342 || reader.ReadInt32() != 105)
                    throw new IOException("Formato BSA diverso da Skyrim SE.");
                int offset = reader.ReadInt32();
                var flags = reader.ReadUInt32();
                result.Flags = flags;
                int folderCount = reader.ReadInt32(), fileCount = reader.ReadInt32();
                reader.ReadInt32();
                int nameBytes = reader.ReadInt32();
                if ((flags & 3) != 3 || folderCount < 0 || folderCount > 100000 ||
                    fileCount < 0 || fileCount > 500000 || nameBytes < 0 || nameBytes > 30000000)
                    throw new IOException("Indice BSA non valido.");
                var counts = new int[folderCount];
                reader.BaseStream.Position = offset;
                long total = 0;
                for (int folder = 0; folder < folderCount; folder++)
                {
                    reader.ReadUInt64();
                    counts[folder] = reader.ReadInt32();
                    if (counts[folder] < 0) throw new IOException("Conteggio file BSA non valido.");
                    total += counts[folder];
                    reader.ReadInt32();
                    reader.ReadUInt64();
                }
                if (total != fileCount) throw new IOException("Conteggio file BSA non coerente.");
                var folders = new string[folderCount];
                var records = new List<Tuple<uint, uint>>(fileCount);
                for (int folder = 0; folder < folderCount; folder++)
                {
                    int length = reader.ReadByte();
                    folders[folder] = Encoding.ASCII.GetString(reader.ReadBytes(length)).TrimEnd('\0');
                    for (int file = 0; file < counts[folder]; file++)
                    {
                        reader.ReadUInt64();
                        records.Add(Tuple.Create(reader.ReadUInt32(), reader.ReadUInt32()));
                    }
                }
                var names = reader.ReadBytes(nameBytes);
                int start = 0, record = 0;
                for (int folder = 0; folder < folderCount; folder++)
                {
                    for (int file = 0; file < counts[folder]; file++)
                    {
                        if (start >= names.Length) throw new IOException("Indice dei nomi BSA incompleto.");
                        int end = Array.IndexOf(names, (byte)0, start);
                        if (end < 0) throw new IOException("Nome BSA incompleto.");
                        string item = Encoding.ASCII.GetString(names, start, end - start);
                        result.Entries[folders[folder] + "\\" + item] = records[record++];
                        start = end + 1;
                    }
                }
            }
            return result;
        }

        private static byte[] DecodeLz4(byte[] input, int length)
        {
            var output = new byte[length];
            int written = 0;
            if (input.Length >= 7 && BitConverter.ToUInt32(input, 0) == 0x184D2204)
            {
                int flags = input[4], offset = 7 + ((flags & 8) != 0 ? 8 : 0) + ((flags & 1) != 0 ? 4 : 0);
                while (offset + 4 <= input.Length)
                {
                    uint block = BitConverter.ToUInt32(input, offset);
                    offset += 4;
                    if (block == 0) break;
                    int count = (int)(block & 0x7FFFFFFF);
                    if (count > input.Length - offset) throw new IOException("Invalid LZ4 frame");
                    if ((block & 0x80000000) != 0)
                    {
                        if (count > output.Length - written) throw new IOException("LZ4 output overflow");
                        Buffer.BlockCopy(input, offset, output, written, count);
                        written += count;
                    }
                    else DecodeBlock(input, offset, offset + count, output, ref written);
                    offset += count + ((flags & 0x10) != 0 ? 4 : 0);
                }
            }
            else DecodeBlock(input, 0, input.Length, output, ref written);
            if (written != length) throw new IOException("Incomplete LZ4 output");
            return output;
        }

        private static void DecodeBlock(byte[] input, int offset, int end, byte[] output, ref int written)
        {
            while (offset < end)
            {
                byte token = input[offset++];
                int literal = Lz4Size(token >> 4, input, ref offset, end);
                if (literal > end - offset || literal > output.Length - written) throw new IOException("Invalid LZ4 literal");
                Buffer.BlockCopy(input, offset, output, written, literal);
                offset += literal;
                written += literal;
                if (offset == end) break;
                if (offset + 2 > end) throw new IOException("Invalid LZ4 match");
                int distance = input[offset] | (input[offset + 1] << 8);
                offset += 2;
                int match = Lz4Size(token & 15, input, ref offset, end) + 4;
                if (distance == 0 || distance > written || match > output.Length - written) throw new IOException("Invalid LZ4 match");
                for (int i = 0; i < match; i++) { output[written] = output[written - distance]; written++; }
            }
        }

        private static int Lz4Size(int size, byte[] input, ref int offset, int end)
        {
            if (size < 15) return size;
            byte next;
            do
            {
                if (offset >= end) throw new IOException("Incomplete LZ4 size");
                next = input[offset++];
                size += next;
            } while (next == 255);
            return size;
        }

        internal sealed class Index
        {
            internal uint Flags;
            internal readonly Dictionary<string, Tuple<uint, uint>> Entries = new Dictionary<string, Tuple<uint, uint>>(Files.Comparer);

            internal Stream Open(string archive, string path)
            {
                Tuple<uint, uint> entry;
                if (!Entries.TryGetValue(path, out entry)) return null;
                using (var reader = new BinaryReader(File.OpenRead(archive)))
                    return new MemoryStream(ReadEntry(reader, Flags, entry), false);
            }
        }
    }

    internal sealed class Nif
    {
        private readonly byte[] content;
        private readonly List<int> partitions = new List<int>();
        private readonly Dictionary<string, List<int>> shapePartitions = new Dictionary<string, List<int>>(Files.Comparer);
        private readonly Dictionary<int, int> physicsValues = new Dictionary<int, int>();
        private readonly int stringCountOffset, stringTableEnd, stringCount, maxStringLength;
        internal readonly HashSet<string> Physics = new HashSet<string>(Files.Comparer);
        internal readonly HashSet<string> GeometryNames = new HashSet<string>(Files.Comparer);
        internal IEnumerable<int> Slots { get { return partitions.Select(offset => (int)BitConverter.ToUInt16(content, offset)); } }
        internal int GeometryCount { get; private set; }
        internal bool HasRootPhysics { get; private set; }
        internal readonly List<PreviewGeometry> PreviewShapes = new List<PreviewGeometry>();
        internal Dictionary<string, string> BoneParents { get; private set; }

        internal Nif(string path, bool readPreview = false) : this(path, File.ReadAllBytes(path), readPreview) { }

        internal Nif(string path, byte[] bytes, bool readPreview, bool readSkin = false, bool readParents = false)
        {
            content = bytes;
            using (var reader = new BinaryReader(new MemoryStream(content), Encoding.UTF8))
            {
                int lineEnd = Array.IndexOf(content, (byte)10);
                if (lineEnd < 0 || lineEnd > 100) throw new InvalidDataException("Intestazione NIF non valida: " + path);
                reader.BaseStream.Position = lineEnd + 1;
                uint version = reader.ReadUInt32();
                byte endian = reader.ReadByte();
                uint userVersion = reader.ReadUInt32();
                int blocks = Count(reader, 100000);
                if (version != 0x14020007 || endian != 1 || userVersion != 12 || reader.ReadUInt32() != 100)
                    throw new InvalidDataException("Serve una mesh Skyrim Special Edition: " + Path.GetFileName(path));
                for (int i = 0; i < 3; i++) reader.ReadBytes(reader.ReadByte());
                int typeCount = reader.ReadUInt16();
                var types = Enumerable.Range(0, typeCount).Select(i => ReadString(reader)).ToArray();
                var indices = Enumerable.Range(0, blocks).Select(i => reader.ReadUInt16()).ToArray();
                var sizes = Enumerable.Range(0, blocks).Select(i => Count(reader, content.Length)).ToArray();
                stringCountOffset = (int)reader.BaseStream.Position;
                stringCount = Count(reader, 100000);
                maxStringLength = Count(reader, content.Length);
                var strings = Enumerable.Range(0, stringCount).Select(i => ReadString(reader)).ToArray();
                stringTableEnd = (int)reader.BaseStream.Position;
                int groupCount = Count(reader, 100000);
                reader.BaseStream.Position += 4L * groupCount;
                var blockOffsets = new int[blocks];
                var skinPartitions = new Dictionary<int, List<int>>();
                var geometrySkins = new Dictionary<string, int>(Files.Comparer);
                for (int block = 0; block < blocks; block++)
                {
                    long start = reader.BaseStream.Position, end = start + sizes[block];
                    blockOffsets[block] = (int)start;
                    if (end > content.Length || indices[block] >= types.Length)
                        throw new InvalidDataException("Blocchi NIF non validi: " + path);
                    string type = types[indices[block]];
                    if (type == "BSTriShape" || type == "BSDynamicTriShape" || type == "BSSubIndexTriShape" ||
                        type == "NiTriShape" || type == "NiTriStrips" || type == "BSLODTriShape")
                    {
                        GeometryCount++;
                        if (sizes[block] >= 4)
                        {
                            uint name = reader.ReadUInt32();
                            if (name < strings.Length)
                            {
                                GeometryNames.Add(strings[name]);
                                int extras = sizes[block] >= 8 ? reader.ReadInt32() : -1;
                                long skin = 88L + 4L * extras;
                                if (type != "NiTriShape" && type != "NiTriStrips" && extras >= 0 && skin + 4 <= sizes[block])
                                {
                                    reader.BaseStream.Position = start + skin;
                                    geometrySkins[strings[name]] = reader.ReadInt32();
                                }
                            }
                        }
                    }
                    if (type == "BSDismemberSkinInstance" && sizes[block] >= 20)
                    {
                        reader.BaseStream.Position = start + 12;
                        long parts = reader.BaseStream.Position + 4 + 4L * Count(reader, 100000);
                        if (parts + 4 > end) throw new InvalidDataException("Partizioni NIF non valide: " + path);
                        reader.BaseStream.Position = parts;
                        int count = Count(reader, 100000);
                        if (parts + 4 + count * 4L != end) throw new InvalidDataException("Partizioni NIF non valide: " + path);
                        var offsets = Enumerable.Range(0, count).Select(part => (int)parts + 6 + part * 4).ToList();
                        skinPartitions.Add(block, offsets);
                        partitions.AddRange(offsets);
                    }
                    if (type == "NiStringExtraData" && sizes[block] == 8)
                    {
                        uint name = reader.ReadUInt32(), value = reader.ReadUInt32();
                        if (name < strings.Length && Files.Comparer.Equals(strings[name], SmpConfiguration.ExtraDataName))
                        {
                            physicsValues.Add(block, (int)start + 4);
                            if (value < strings.Length)
                                Physics.Add(Regex.Replace(strings[value].Replace('/', '\\'), @"\\+", @"\"));
                        }
                    }
                    reader.BaseStream.Position = end;
                }
                foreach (var entry in geometrySkins)
                {
                    List<int> offsets;
                    if (skinPartitions.TryGetValue(entry.Value, out offsets)) shapePartitions.Add(entry.Key, offsets);
                }
                if (physicsValues.Count > 0)
                {
                    int rootCount = Count(reader, blocks);
                    var roots = Enumerable.Range(0, rootCount).Select(i => reader.ReadInt32()).ToArray();
                    foreach (int root in roots)
                    {
                        if (root < 0 || root >= blocks || sizes[root] < 8) continue;
                        reader.BaseStream.Position = blockOffsets[root] + 4;
                        int extraCount = reader.ReadInt32();
                        if (extraCount < 0 || extraCount > (sizes[root] - 8) / 4) continue;
                        for (int i = 0; i < extraCount; i++)
                            if (physicsValues.ContainsKey(reader.ReadInt32())) HasRootPhysics = true;
                    }
                }
                if (readPreview || readParents)
                {
                    var blockTypes = indices.Select(index => types[index]).ToArray();
                    if (readParents) BoneParents = ReadBoneParents(reader, blockOffsets, sizes, strings, blockTypes);
                    for (int block = 0; block < blocks; block++)
                    {
                        if (!readPreview) break;
                        string type = blockTypes[block];
                        if (type != "BSTriShape" && type != "BSDynamicTriShape" && type != "BSSubIndexTriShape") continue;
                        var geometry = ReadPreviewShape(reader, block, blockOffsets, sizes, strings, blockTypes, readSkin);
                        if (geometry != null) PreviewShapes.Add(geometry);
                    }
                }
            }
        }

        private static PreviewGeometry ReadPreviewShape(BinaryReader r, int block, int[] offsets, int[] sizes, string[] strings, string[] types,
            bool readSkin)
        {
            int start = offsets[block], end = start + sizes[block];
            if (end - start < 112) return null;
            r.BaseStream.Position = start;
            uint nameId = r.ReadUInt32();
            if (nameId >= strings.Length) return null;
            string name = strings[nameId];
            if (Regex.IsMatch(name, @"^(?:virtualground|box(?:full|s|arm|low)?|.*collision.*)$", RegexOptions.IgnoreCase)) return null;
            int extras = r.ReadInt32();
            if (extras < 0 || 112L + 4L * extras > end - start) return null;
            r.BaseStream.Position = start + 16 + 4L * extras;
            float tx = r.ReadSingle(), ty = r.ReadSingle(), tz = r.ReadSingle();
            var rotation = Enumerable.Range(0, 9).Select(i => r.ReadSingle()).ToArray();
            float scale = r.ReadSingle();
            r.BaseStream.Position = start + 88 + 4L * extras;
            int skin = r.ReadInt32();
            int shader = r.ReadInt32(), alpha = r.ReadInt32();
            ulong desc = r.ReadUInt64();
            int stride = (int)(desc & 15) * 4;
            int faceCount = r.ReadUInt16(), count = r.ReadUInt16(), dataSize = r.ReadInt32();
            var vertices = new List<Point3D>();
            var faces = new List<int>();
            var uv = new List<Point>();
            if (dataSize > 0 && stride >= 12 && count > 0 && count * stride + 6L * faceCount <= end - r.BaseStream.Position)
            {
                ReadVertices(r, count, stride, desc, vertices, uv);
                ReadFaces(r, faceCount, vertices.Count, faces);
                if (skin < 0)
                {
                    for (int i = 0; i < vertices.Count; i++)
                    {
                        var p = vertices[i];
                        vertices[i] = new Point3D(tx + scale * (rotation[0]*p.X + rotation[1]*p.Y + rotation[2]*p.Z),
                            ty + scale * (rotation[3]*p.X + rotation[4]*p.Y + rotation[5]*p.Z),
                            tz + scale * (rotation[6]*p.X + rotation[7]*p.Y + rotation[8]*p.Z));
                    }
                }
            }
            else if (skin >= 0 && skin < offsets.Length && sizes[skin] >= 8)
            {
                r.BaseStream.Position = offsets[skin] + 4;
                int partition = r.ReadInt32();
                if (partition < 0 || partition >= offsets.Length || sizes[partition] < 20) return null;
                r.BaseStream.Position = offsets[partition];
                int partitionEnd = offsets[partition] + sizes[partition];
                int partitions = r.ReadInt32(), vertexBytes = r.ReadInt32(), vertexStride = r.ReadInt32();
                ulong vertexDesc = r.ReadUInt64();
                if (partitions < 1 || partitions > 1000 || vertexStride < 12 || vertexStride > 256 ||
                    vertexBytes < vertexStride || vertexBytes > partitionEnd - r.BaseStream.Position ||
                    vertexBytes % vertexStride != 0 || vertexBytes / vertexStride > 65535) return null;
                ReadVertices(r, vertexBytes / vertexStride, vertexStride, vertexDesc, vertices, uv);
                for (int i = 0; i < partitions; i++)
                {
                    if (r.BaseStream.Position + 10 > partitionEnd) return null;
                    int verts = r.ReadUInt16(), tris = r.ReadUInt16(), bones = r.ReadUInt16(), strips = r.ReadUInt16(), weights = r.ReadUInt16();
                    if (!Skip(r, 2L * bones, partitionEnd) || !FlagSkip(r, 2L * verts, partitionEnd) ||
                        !FlagSkip(r, 4L * verts * weights, partitionEnd)) return null;
                    int stripLength = 0;
                    for (int s = 0; s < strips; s++) stripLength += r.ReadUInt16();
                    if (!FlagSkip(r, strips == 0 ? tris * 6L : stripLength * 2L, partitionEnd) ||
                        !FlagSkip(r, (long)verts * weights, partitionEnd) || !Skip(r, 10, partitionEnd) ||
                        r.BaseStream.Position + 6L * tris > partitionEnd) return null;
                    ReadFaces(r, tris, vertices.Count, faces);
                }
            }
            if (faces.Count == 0) return null;
            var geometry = new PreviewGeometry { Name = name, Positions = vertices, Indices = faces, TextureCoordinates = uv,
                UsesAlpha = alpha >= 0 && alpha < types.Length && types[alpha] == "NiAlphaProperty" };
            if (uv.Count > 0) ReadDiffuse(r, shader, offsets, sizes, types, geometry);
            if (readSkin) geometry.Skin = ReadSkin(r, skin, offsets, sizes, strings, types, vertices.Count);
            return geometry;
        }

        private static Dictionary<string, string> ReadBoneParents(BinaryReader reader, int[] offsets, int[] sizes, string[] names, string[] types)
        {
            var result = new Dictionary<string, string>(Files.Comparer);
            for (int block = 0; block < offsets.Length; block++)
            {
                if (types[block] != "NiNode" && types[block] != "BSFadeNode") continue;
                int start = offsets[block], end = start + sizes[block];
                if (sizes[block] < 76) continue;
                reader.BaseStream.Position = start;
                uint parent = reader.ReadUInt32();
                int extras = reader.ReadInt32();
                if (parent >= names.Length || extras < 0 || start + 76L + extras * 4L > end) continue;
                reader.BaseStream.Position = start + 72L + extras * 4L;
                int count = reader.ReadInt32();
                if (count < 0 || reader.BaseStream.Position + count * 4L > end) continue;
                var children = new int[count];
                for (int i = 0; i < count; i++) children[i] = reader.ReadInt32();
                foreach (int child in children)
                {
                    if (child < 0 || child >= offsets.Length || sizes[child] < 4) continue;
                    reader.BaseStream.Position = offsets[child];
                    uint name = reader.ReadUInt32();
                    if (name < names.Length && !Files.Comparer.Equals(names[name], names[parent]))
                        result[names[name]] = names[parent];
                }
            }
            return result;
        }

        private static PreviewBone[] ReadSkin(BinaryReader reader, int skin, int[] offsets, int[] sizes, string[] names, string[] types,
            int vertices)
        {
            if (skin < 0 || skin >= offsets.Length || sizes[skin] < 16 ||
                (types[skin] != "NiSkinInstance" && types[skin] != "BSDismemberSkinInstance")) return null;
            reader.BaseStream.Position = offsets[skin];
            int data = reader.ReadInt32(), partition = reader.ReadInt32();
            reader.ReadInt32(); // Skeleton root block.
            int count = reader.ReadInt32();
            if (count < 1 || count > 1000 || 16 + count * 4 > sizes[skin]) return null;
            var bones = new PreviewBone[count];
            var refs = Enumerable.Range(0, count).Select(i => reader.ReadInt32()).ToArray();
            for (int i = 0; i < count; i++)
            {
                int bone = refs[i];
                if (bone < 0 || bone >= offsets.Length || sizes[bone] < 4) return null;
                reader.BaseStream.Position = offsets[bone];
                uint name = reader.ReadUInt32();
                if (name >= names.Length) return null;
                bones[i] = new PreviewBone(names[name]);
            }
            if (data >= 0 && data < offsets.Length && sizes[data] >= 57 && types[data] == "NiSkinData")
            {
                int end = offsets[data] + sizes[data];
                reader.BaseStream.Position = offsets[data] + 52;
                if (reader.ReadInt32() != count) return null;
                if (reader.ReadByte() != 0)
                {
                    foreach (var bone in bones)
                    {
                        if (!Skip(reader, 68, end) || reader.BaseStream.Position + 2 > end) return null;
                        int weights = reader.ReadUInt16();
                        if (reader.BaseStream.Position + weights * 6L > end) return null;
                        for (int index = 0; index < weights; index++)
                        {
                            int vertex = reader.ReadUInt16();
                            float weight = reader.ReadSingle();
                            if (vertex < vertices && weight > 0 && weight <= 1)
                                bone.Weights.Add(new PreviewWeight(vertex, weight));
                        }
                    }
                    return bones;
                }
            }
            return ReadPartitionSkin(reader, partition, offsets, sizes, bones, vertices) ? bones : null;
        }

        private static bool ReadPartitionSkin(BinaryReader reader, int partition, int[] offsets, int[] sizes, PreviewBone[] bones, int vertices)
        {
            if (partition < 0 || partition >= offsets.Length || sizes[partition] < 20) return false;
            reader.BaseStream.Position = offsets[partition];
            int end = offsets[partition] + sizes[partition];
            int partitions = reader.ReadInt32(), vertexBytes = reader.ReadInt32();
            if (partitions < 1 || partitions > 1000 || !Skip(reader, 12L + vertexBytes, end)) return false;
            var seen = new bool[vertices];
            for (int part = 0; part < partitions; part++)
            {
                if (reader.BaseStream.Position + 10 > end) return false;
                int count = reader.ReadUInt16(), triangles = reader.ReadUInt16(), boneCount = reader.ReadUInt16();
                int strips = reader.ReadUInt16(), width = reader.ReadUInt16();
                if (width < 1 || width > 8 || boneCount > bones.Length || reader.BaseStream.Position + boneCount * 2L + 1 > end)
                    return false;
                var palette = Enumerable.Range(0, boneCount).Select(i => (int)reader.ReadUInt16()).ToArray();
                if (reader.ReadByte() == 0 || reader.BaseStream.Position + count * 2L + 1 > end) return false;
                var map = Enumerable.Range(0, count).Select(i => (int)reader.ReadUInt16()).ToArray();
                if (reader.ReadByte() == 0 || reader.BaseStream.Position + count * width * 4L + strips * 2L > end) return false;
                var weights = Enumerable.Range(0, count * width).Select(i => reader.ReadSingle()).ToArray();
                int stripLength = 0;
                for (int i = 0; i < strips; i++) stripLength += reader.ReadUInt16();
                if (!FlagSkip(reader, strips == 0 ? triangles * 6L : stripLength * 2L, end) ||
                    reader.BaseStream.Position + count * width + 1L > end || reader.ReadByte() == 0) return false;
                for (int v = 0; v < count; v++)
                {
                    int vertex = map[v];
                    bool use = vertex < vertices && !seen[vertex];
                    if (use) seen[vertex] = true;
                    for (int w = 0; w < width; w++)
                    {
                        int bone = reader.ReadByte();
                        float weight = weights[v * width + w];
                        if (use && bone < palette.Length && palette[bone] < bones.Length && weight > 0 && weight <= 1)
                            bones[palette[bone]].Weights.Add(new PreviewWeight(vertex, weight));
                    }
                }
                if (!Skip(reader, 10 + triangles * 6L, end)) return false;
            }
            return true;
        }

        private static void ReadDiffuse(BinaryReader reader, int shader, int[] offsets, int[] sizes, string[] types,
            PreviewGeometry geometry)
        {
            if (shader < 0 || shader >= types.Length || sizes[shader] < 44 || types[shader] != "BSLightingShaderProperty") return;
            int start = offsets[shader];
            // Skyrim SE writes the lighting shader type before the NiObjectNET fields.
            reader.BaseStream.Position = start + 8;
            int extras = reader.ReadInt32();
            if (extras < 0 || 44L + 4L * extras > sizes[shader]) return;
            reader.BaseStream.Position = start + 24 + 4L * extras;
            float offsetX = reader.ReadSingle(), offsetY = reader.ReadSingle();
            float scaleX = reader.ReadSingle(), scaleY = reader.ReadSingle();
            int set = reader.ReadInt32();
            if (set < 0 || set >= types.Length || sizes[set] < 8 || types[set] != "BSShaderTextureSet") return;
            reader.BaseStream.Position = offsets[set];
            int count = reader.ReadInt32(), length = reader.ReadInt32();
            if (count < 1 || length < 1 || length > sizes[set] - 8 || length > 32768) return;
            geometry.DiffuseTexture = Encoding.UTF8.GetString(reader.ReadBytes(length)).TrimEnd('\0');
            if (offsetX == 0 && offsetY == 0 && scaleX == 1 && scaleY == 1) return;
            if (new[] { offsetX, offsetY, scaleX, scaleY }.Any(value => Single.IsNaN(value) || Single.IsInfinity(value))) return;
            var coordinates = geometry.TextureCoordinates;
            for (int i = 0; i < coordinates.Count; i++)
                coordinates[i] = new Point(coordinates[i].X * scaleX + offsetX, coordinates[i].Y * scaleY + offsetY);
        }

        private static bool Skip(BinaryReader reader, long count, int end)
        {
            if (count < 0 || reader.BaseStream.Position + count > end) return false;
            reader.BaseStream.Position += count;
            return true;
        }

        private static bool FlagSkip(BinaryReader reader, long count, int end)
        {
            return reader.BaseStream.Position < end && (reader.ReadByte() == 0 || Skip(reader, count, end));
        }

        private static void ReadVertices(BinaryReader reader, int count, int stride, ulong descriptor, List<Point3D> vertices,
            List<Point> coordinates)
        {
            int uvOffset = (int)((descriptor >> 8) & 15) * 4;
            bool hasUv = (descriptor & (1UL << 45)) != 0 && uvOffset >= 12 && uvOffset + 4 <= stride;
            for (int i = 0; i < count; i++)
            {
                long start = reader.BaseStream.Position;
                float x = reader.ReadSingle(), y = reader.ReadSingle(), z = reader.ReadSingle();
                if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z) ||
                    Math.Abs(x) > 50000 || Math.Abs(y) > 50000 || Math.Abs(z) > 50000)
                    throw new InvalidDataException("Invalid preview vertex");
                vertices.Add(new Point3D(x, y, z));
                if (hasUv)
                {
                    reader.BaseStream.Position = start + uvOffset;
                    coordinates.Add(new Point(Half(reader.ReadUInt16()), Half(reader.ReadUInt16())));
                }
                reader.BaseStream.Position = start + stride;
            }
        }

        private static double Half(ushort bits)
        {
            int exponent = (bits >> 10) & 31;
            int fraction = bits & 1023;
            if (exponent == 31) return 0;
            double value = exponent == 0 ? fraction / 16777216.0 : (1 + fraction / 1024.0) * Math.Pow(2, exponent - 15);
            return (bits & 0x8000) == 0 ? value : -value;
        }

        private static void ReadFaces(BinaryReader reader, int count, int vertices, List<int> faces)
        {
            for (int i = 0; i < count; i++)
            {
                int a = reader.ReadUInt16(), b = reader.ReadUInt16(), c = reader.ReadUInt16();
                if (a >= vertices || b >= vertices || c >= vertices) continue;
                faces.Add(a); faces.Add(b); faces.Add(c);
            }
        }

        internal void FitSlots(int primary, IEnumerable<int> compatible, bool torsoOnly)
        {
            var allowed = new HashSet<int>(compatible) { primary };
            foreach (int offset in partitions)
            {
                if (allowed.Contains(BitConverter.ToUInt16(content, offset))) continue;
                content[offset] = (byte)primary;
                content[offset + 1] = (byte)(primary >> 8);
            }
            if (torsoOnly)
                foreach (int offset in shapePartitions.Where(shape => Composer.BodyPart(shape.Key) == "body").SelectMany(shape => shape.Value))
                {
                    content[offset] = 32;
                    content[offset + 1] = 0;
                }
        }

        internal bool HasTorsoSkin
        {
            get
            {
                return shapePartitions.Any(shape => Composer.BodyPart(shape.Key) == "body" && shape.Value.Count != 0
                    && shape.Value.All(offset => BitConverter.ToUInt16(content, offset) == 32));
            }
        }

        internal void Save(string path, string physicsLink)
        {
            if (physicsLink == null)
            {
                File.WriteAllBytes(path, content);
                return;
            }
            if (!HasRootPhysics)
                throw new InvalidDataException("La mesh generata non ha un riferimento SMP sul nodo principale: " + path);
            // The Outfit Studio save can restore the first donor's root metadata. Redirect the
            // physics blocks using a new string index without changing other NIF strings or blocks.
            byte[] value = Encoding.UTF8.GetBytes(physicsLink);
            byte[] index = BitConverter.GetBytes(stringCount);
            foreach (int offset in physicsValues.Values) Buffer.BlockCopy(index, 0, content, offset, index.Length);
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(content, 0, stringCountOffset);
                writer.Write(stringCount + 1);
                writer.Write(Math.Max(maxStringLength, value.Length));
                int start = stringCountOffset + 8;
                writer.Write(content, start, stringTableEnd - start);
                writer.Write(value.Length);
                writer.Write(value);
                writer.Write(content, stringTableEnd, content.Length - stringTableEnd);
            }
        }

        private static int Count(BinaryReader reader, int limit)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > limit) throw new InvalidDataException("Contatore NIF non valido.");
            return count;
        }

        private static string ReadString(BinaryReader reader)
        {
            return Encoding.UTF8.GetString(reader.ReadBytes(Count(reader, 1000000))).TrimEnd('\0');
        }
    }

    internal sealed class PreviewGeometry
    {
        internal string Name;
        internal List<Point3D> Positions;
        internal List<int> Indices;
        internal List<Point> TextureCoordinates;
        internal string DiffuseTexture;
        internal bool UsesAlpha;
        internal PreviewBone[] Skin;
    }

    internal sealed class PreviewBone
    {
        internal readonly string Name;
        internal readonly List<PreviewWeight> Weights = new List<PreviewWeight>();
        internal PreviewBone(string name) { Name = name; }
    }

    internal struct PreviewWeight
    {
        internal readonly int Vertex;
        internal readonly float Weight;
        internal PreviewWeight(int vertex, float weight) { Vertex = vertex; Weight = weight; }
    }
}
