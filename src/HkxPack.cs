using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Media.Media3D;

namespace SkyrimOutfit
{
    internal sealed class HkxPack
    {
        private readonly byte[] bytes;
        private readonly Dictionary<int, int> pointers = new Dictionary<int, int>();
        private readonly Dictionary<string, List<int>> objects = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        private readonly int dataStart, dataEnd;
        internal readonly int PointerSize;
        internal int ArraySize { get { return PointerSize + 8; } }
        internal int ObjectBase { get { return 2 * PointerSize; } }

        internal HkxPack(byte[] bytes)
        {
            this.bytes = bytes;
            if (bytes.Length < 208 || Number(0) != 0x57e0e057 || Number(4) != 0x10c0c010 ||
                Number(12) != 8 || bytes[17] != 1 || Number(20) != 3)
                throw new InvalidDataException("Expected a Skyrim Havok 2010 packfile.");
            PointerSize = bytes[16];
            if (PointerSize != 4 && PointerSize != 8) throw new InvalidDataException("Unknown HKX pointer format.");
            var sections = new int[3];
            for (int i = 0; i < sections.Length; i++) sections[i] = Number(64 + i * 48 + 20);
            int section = 64 + 2 * 48;
            dataStart = sections[2];
            dataEnd = Address(dataStart, Number(section + 24));
            int global = Address(dataStart, Number(section + 28));
            int classes = Address(dataStart, Number(section + 32));
            int end = Address(dataStart, Number(section + 36));
            if (dataStart < 208 || dataEnd > global || global > classes || classes > end)
                throw new InvalidDataException("Invalid HKX section boundaries.");
            for (int i = dataEnd; i + 8 <= global; i += 8)
                if (Number(i) >= 0) pointers[Address(dataStart, Number(i))] = Address(dataStart, Number(i + 4));
            for (int i = global; i + 12 <= classes; i += 12)
                if (Number(i) >= 0 && Number(i + 4) == 2)
                    pointers[Address(dataStart, Number(i))] = Address(dataStart, Number(i + 8));
            for (int i = classes; i + 12 <= end; i += 12)
            {
                if (Number(i) < 0) continue;
                int typeSection = Number(i + 4);
                if (typeSection < 0 || typeSection >= sections.Length) throw new InvalidDataException("Invalid HKX class section.");
                string name = StringAt(Address(sections[typeSection], Number(i + 8)));
                List<int> entries;
                if (!objects.TryGetValue(name, out entries)) objects.Add(name, entries = new List<int>());
                entries.Add(Address(dataStart, Number(i)));
            }
        }

        internal IEnumerable<int> Objects(string name)
        {
            List<int> entries;
            return objects.TryGetValue(name, out entries) ? entries : (IEnumerable<int>)new int[0];
        }

        internal int Pointer(int field)
        {
            int pointer;
            if (!pointers.TryGetValue(field, out pointer)) return -1;
            if (pointer < dataStart || pointer >= dataEnd) throw new InvalidDataException("Invalid HKX object reference.");
            return pointer;
        }

        internal int Array(int field, int stride, int maximum, out int count)
        {
            count = Number(field + PointerSize);
            if (count == 0) return -1;
            int first = Pointer(field);
            if (first < 0 || count < 0 || count > maximum || first + (long)count * stride > dataEnd)
                throw new InvalidDataException("Invalid HKX array.");
            return first;
        }

        internal string Text(int field)
        {
            int pointer = Pointer(field);
            return pointer < 0 ? "" : StringAt(pointer);
        }

        internal byte[] Slice(int address, int count)
        {
            var result = new byte[count];
            Buffer.BlockCopy(bytes, address, result, 0, count);
            return result;
        }

        private string StringAt(int offset)
        {
            int end = System.Array.IndexOf(bytes, (byte)0, offset, Math.Min(1024, bytes.Length - offset));
            if (end < 0) throw new InvalidDataException("Invalid HKX string.");
            return Encoding.UTF8.GetString(bytes, offset, end - offset);
        }

        private int Address(int origin, int displacement)
        {
            if (origin < 0 || displacement < 0 || (long)origin + displacement > bytes.Length)
                throw new InvalidDataException("Invalid HKX offset.");
            return origin + displacement;
        }

        internal int Number(int offset) { Address(offset, 4); return BitConverter.ToInt32(bytes, offset); }
        internal short Index(int offset) { Address(offset, 2); return BitConverter.ToInt16(bytes, offset); }
        internal int Byte(int offset) { Address(offset, 1); return bytes[offset]; }
        internal double Scalar(int offset)
        {
            Address(offset, 4);
            float value = BitConverter.ToSingle(bytes, offset);
            if (Single.IsNaN(value) || Single.IsInfinity(value)) throw new InvalidDataException("Invalid HKX transform.");
            return value;
        }

        internal HkxTransform Transform(int offset)
        {
            return new HkxTransform(new Vector3D(Scalar(offset), Scalar(offset + 4), Scalar(offset + 8)),
                new Quaternion(Scalar(offset + 16), Scalar(offset + 20), Scalar(offset + 24), Scalar(offset + 28)),
                new Vector3D(Scalar(offset + 32), Scalar(offset + 36), Scalar(offset + 40)));
        }
    }

    internal struct HkxTransform
    {
        internal readonly Vector3D Position, Scale;
        internal readonly Quaternion Rotation;

        internal HkxTransform(Vector3D position, Quaternion rotation, Vector3D scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        internal Matrix3D Matrix(Vector3D translation)
        {
            var result = Matrix3D.Identity;
            result.Scale(Scale);
            var rotation = Rotation;
            if (rotation.LengthSquared() > 0.00001)
            {
                rotation.Normalize();
                result.Rotate(rotation);
            }
            result.Translate(translation);
            return result;
        }
    }

    internal static class HkxQuaternion
    {
        internal static double LengthSquared(this Quaternion quaternion)
        {
            return quaternion.X * quaternion.X + quaternion.Y * quaternion.Y + quaternion.Z * quaternion.Z + quaternion.W * quaternion.W;
        }
    }
}
