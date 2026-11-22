using System;
using System.IO;
using System.Linq;
using System.Windows.Media.Media3D;

namespace SkyrimOutfit
{
    internal sealed class HkxClip
    {
        private readonly HkxPack file;
        private readonly byte[] compressed;
        private readonly int[] offsets;
        private readonly int blockWidth, maskBytes, interleaved;
        private int activeBlock = -1;
        private Track[] tracks;
        internal readonly int TrackCount, Frames;
        internal readonly double Duration;
        internal readonly string SkeletonName;
        internal readonly string[] TrackNames;
        internal readonly short[] TrackToBone;
        internal readonly bool Additive;

        internal HkxClip(string path)
        {
            if (new FileInfo(path).Length > 32000000) throw new InvalidDataException("HKX animation is too large for the preview.");
            file = new HkxPack(File.ReadAllBytes(path));
            int animation = file.Objects("hkaSplineCompressedAnimation").DefaultIfEmpty(-1).First();
            interleaved = -1;
            if (animation < 0)
            {
                animation = file.Objects("hkaInterleavedUncompressedAnimation").DefaultIfEmpty(-1).First();
                if (animation < 0) throw new InvalidDataException("This HKX animation format is not supported.");
            }
            int p = file.PointerSize;
            int common = animation + file.ObjectBase;
            Duration = file.Scalar(common + 4);
            TrackCount = file.Number(common + 8);
            if (Duration < 0 || TrackCount < 1 || TrackCount > 1024) throw new InvalidDataException("Invalid HKX animation size.");
            TrackNames = new string[TrackCount];
            int annotations, ann = file.Array(common + 16 + p, p + file.ArraySize, 1024, out annotations);
            for (int i = 0; i < Math.Min(annotations, TrackCount); i++) TrackNames[i] = file.Text(ann + i * (p + file.ArraySize));

            int binding = file.Objects("hkaAnimationBinding").FirstOrDefault(item => file.Pointer(item + file.ObjectBase + p) == animation);
            if (binding != 0)
            {
                int count, pos = file.Array(binding + file.ObjectBase + 2 * p, 2, 1024, out count);
                TrackToBone = Enumerable.Range(0, count).Select(i => file.Index(pos + 2 * i)).ToArray();
                SkeletonName = file.Text(binding + file.ObjectBase);
                Additive = file.Byte(binding + file.ObjectBase + 2 * p + 2 * file.ArraySize) != 0;
            }
            else TrackToBone = new short[0];

            int body = common + 16 + p + file.ArraySize;
            if (!file.Objects("hkaSplineCompressedAnimation").Contains(animation))
            {
                int count;
                interleaved = file.Array(body, 48, 500000, out count);
                Frames = count / TrackCount;
                if (Frames < 1 || count % TrackCount != 0) throw new InvalidDataException("Invalid interleaved HKX transforms.");
                return;
            }
            Frames = file.Number(body);
            int blockCount = file.Number(body + 4);
            blockWidth = file.Number(body + 8) - 1;
            maskBytes = file.Number(body + 12);
            int arrays = body + 28 + (p == 8 ? 4 : 0);
            int length, offsetTable = file.Array(arrays, 4, 1024, out length);
            int size, blob = file.Array(arrays + 4 * file.ArraySize, 1, 32000000, out size);
            if (Frames < 1 || blockWidth < 1 || blockWidth > 255 || blockCount < 1 || blockCount != length ||
                blob < 0 || size < maskBytes || maskBytes < TrackCount * 4)
                throw new InvalidDataException("Invalid HKX spline layout.");
            compressed = file.Slice(blob, size);
            offsets = Enumerable.Range(0, blockCount).Select(i => file.Number(offsetTable + i * 4)).ToArray();
            for (int i = 0; i < offsets.Length; i++)
                if (offsets[i] < 0 || offsets[i] > size - maskBytes || (i > 0 && offsets[i] <= offsets[i - 1]))
                    throw new InvalidDataException("Invalid HKX block offsets.");
        }

        internal HkxTransform[] Sample(double frame)
        {
            frame = Math.Max(0, Math.Min(Frames - 1, frame));
            var transforms = new HkxTransform[TrackCount];
            if (interleaved >= 0)
            {
                int first = (int)Math.Round(frame) * TrackCount;
                for (int i = 0; i < transforms.Length; i++) transforms[i] = file.Transform(interleaved + (first + i) * 48);
                return transforms;
            }
            int block = Math.Min(offsets.Length - 1, (int)(frame / blockWidth));
            if (block != activeBlock)
            {
                var decoded = ReadBlock(block);
                tracks = decoded;
                activeBlock = block;
            }
            double time = frame - block * blockWidth;
            for (int i = 0; i < tracks.Length; i++) transforms[i] = tracks[i].Sample(time);
            return transforms;
        }

        private Track[] ReadBlock(int block)
        {
            int first = offsets[block], end = block + 1 == offsets.Length ? compressed.Length : offsets[block + 1];
            if (first + maskBytes > end) throw new InvalidDataException("Truncated HKX mask.");
            var result = new Track[TrackCount];
            using (var stream = new MemoryStream(compressed, first, end - first, false))
            using (var reader = new BinaryReader(stream))
            {
                stream.Position = maskBytes;
                for (int i = 0; i < result.Length; i++)
                {
                    int flags = first + i * 4;
                    int packing = compressed[flags];
                    result[i] = new Track {
                        Position = ReadVector(reader, compressed[flags + 1], packing & 3, 0),
                        Rotation = ReadRotation(reader, compressed[flags + 2], packing >> 2 & 15),
                        Scale = ReadVector(reader, compressed[flags + 3], packing >> 6, 1)
                    };
                    Align(reader, 4);
                }
            }
            return result;
        }

        private static Curve ReadVector(BinaryReader reader, int flags, int packing, double identity)
        {
            var curve = new Curve(reader, (flags & 0x70) != 0);
            Align(reader, 4);
            var minimum = new double[3];
            var span = new double[3];
            for (int axis = 0; axis < 3; axis++)
            {
                if ((flags & (16 << axis)) != 0)
                {
                    minimum[axis] = Scalar(reader);
                    span[axis] = Scalar(reader) - minimum[axis];
                }
                else minimum[axis] = (flags & (1 << axis)) != 0 ? Scalar(reader) : identity;
            }
            for (int point = 0; point < curve.Points.Length; point++)
            {
                var values = new double[3];
                for (int axis = 0; axis < 3; axis++)
                {
                    double value = minimum[axis];
                    if ((flags & (16 << axis)) != 0)
                    {
                        if (packing > 1) throw new InvalidDataException("Unknown HKX scalar quantization.");
                        value += span[axis] * (packing == 0 ? reader.ReadByte() / 255.0 : reader.ReadUInt16() / 65535.0);
                    }
                    values[axis] = value;
                }
                curve.Points[point] = new Point4D(values[0], values[1], values[2], 0);
            }
            Align(reader, 4);
            return curve;
        }

        private static Curve ReadRotation(BinaryReader reader, int flags, int packing)
        {
            var curve = new Curve(reader, (flags & 0xF0) != 0);
            if (flags != 0)
            {
                int alignment;
                switch (packing)
                {
                    case 0: case 5: alignment = 4; break;
                    case 1: alignment = 1; break;
                    case 2: alignment = 2; break;
                    default: throw new InvalidDataException("This HKX rotation format is not supported.");
                }
                Align(reader, alignment);
                for (int point = 0; point < curve.Points.Length; point++) curve.Points[point] = UnpackRotation(reader, packing);
            }
            else curve.Points[0] = new Point4D(0, 0, 0, 1);
            Align(reader, 4);
            return curve;
        }

        private static Point4D UnpackRotation(BinaryReader reader, int packing)
        {
            if (packing == 5) return new Point4D(Scalar(reader), Scalar(reader), Scalar(reader), Scalar(reader));
            if (packing == 0)
            {
                uint bits = reader.ReadUInt32();
                double radius = ((bits >> 18) & 1023) / 1023.0;
                double w = 1 - radius * radius;
                double angular = bits & 0x3FFFF;
                double ring = Math.Floor(Math.Sqrt(angular));
                double theta = ring == 0 ? 0 : Math.PI * (angular - ring * ring) / (4 * ring);
                double phi = Math.PI * ring / 1022;
                double length = Math.Sqrt(1 - w * w);
                return new Point4D(Math.Sin(phi) * Math.Cos(theta) * length * Sign(bits, 28),
                    Math.Sin(phi) * Math.Sin(theta) * length * Sign(bits, 29),
                    Math.Cos(phi) * length * Sign(bits, 30), w * Sign(bits, 31));
            }
            int a, b, c, omitted, sign, mask;
            if (packing == 1)
            {
                ulong bits = reader.ReadUInt32() | ((ulong)reader.ReadByte() << 32);
                mask = 4095;
                a = (int)(bits & 4095);
                b = (int)(bits >> 12 & 4095);
                c = (int)(bits >> 24 & 4095);
                omitted = (int)(bits >> 36 & 3);
                sign = Sign(bits, 38);
            }
            else
            {
                a = reader.ReadUInt16(); b = reader.ReadUInt16(); c = reader.ReadUInt16();
                omitted = (a >> 15) | ((b >> 14) & 2);
                sign = Sign((uint)c, 15);
                mask = 32767;
            }
            int bias = mask / 2;
            double unit = Math.Sqrt(0.5) / bias;
            double x = ((a & mask) - bias) * unit, y = ((b & mask) - bias) * unit, z = ((c & mask) - bias) * unit;
            double restored = sign * Math.Sqrt(Math.Max(0, 1 - x * x - y * y - z * z));
            switch (omitted)
            {
                case 0: return new Point4D(restored, x, y, z);
                case 1: return new Point4D(x, restored, y, z);
                case 2: return new Point4D(x, y, restored, z);
                default: return new Point4D(x, y, z, restored);
            }
        }

        private static int Sign(ulong bits, int shift) { return (bits >> shift & 1) == 0 ? 1 : -1; }
        private static void Align(BinaryReader reader, int width)
        {
            long current = reader.BaseStream.Position;
            reader.BaseStream.Position = (current + width - 1) / width * width;
        }

        private static double Scalar(BinaryReader reader)
        {
            float value = reader.ReadSingle();
            if (Single.IsNaN(value) || Single.IsInfinity(value)) throw new InvalidDataException("Invalid HKX spline value.");
            return value;
        }

        private sealed class Track
        {
            internal Curve Position, Rotation, Scale;
            internal HkxTransform Sample(double frame)
            {
                var p = Position.Sample(frame); var q = Rotation.Sample(frame); var s = Scale.Sample(frame);
                return new HkxTransform(new Vector3D(p.X, p.Y, p.Z), new Quaternion(q.X, q.Y, q.Z, q.W), new Vector3D(s.X, s.Y, s.Z));
            }
        }

        private sealed class Curve
        {
            private readonly byte[] knots;
            private readonly int degree;
            internal readonly Point4D[] Points;
            internal Curve(BinaryReader reader, bool spline)
            {
                int count = spline ? reader.ReadUInt16() + 1 : 1;
                degree = spline ? reader.ReadByte() : 0;
                if (count > 1024 || degree > 8 || degree >= count) throw new InvalidDataException("Invalid HKX spline degree.");
                if (spline)
                {
                    int length = count + degree + 1;
                    knots = reader.ReadBytes(length);
                    if (knots.Length != length) throw new EndOfStreamException();
                    for (int i = 1; i < knots.Length; i++)
                        if (knots[i] < knots[i - 1]) throw new InvalidDataException("Invalid HKX spline knots.");
                }
                Points = new Point4D[count];
            }

            internal Point4D Sample(double frame)
            {
                if (knots == null || Points.Length == 1) return Points[0];
                int last = Points.Length - 1;
                double time = Math.Max(knots[degree], Math.Min(knots[last + 1], frame));
                int span = degree;
                while (span < last && time >= knots[span + 1]) span++;
                if (degree == 0) return Points[span];
                var work = new Point4D[degree + 1];
                for (int i = 0; i <= degree; i++) work[i] = Points[span - degree + i];
                for (int step = 1; step <= degree; step++)
                for (int i = degree; i >= step; i--)
                {
                    int left = span - degree + i;
                    int width = knots[left + degree + 1 - step] - knots[left];
                    double amount = width == 0 ? 0 : (time - knots[left]) / width;
                    Point4D a = work[i - 1], b = work[i];
                    work[i] = new Point4D(a.X + amount * (b.X - a.X), a.Y + amount * (b.Y - a.Y),
                        a.Z + amount * (b.Z - a.Z), a.W + amount * (b.W - a.W));
                }
                return work[degree];
            }
        }
    }
}
