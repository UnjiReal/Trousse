using System.IO;
using System.Text;

namespace SkyrimOutfit
{
    // ODF requires a stored mimetype entry at a fixed offset. ZipArchive on .NET Framework
    // writes even NoCompression entries as DEFLATE, so keep these small documents stored.
    internal static class OdfArchive
    {
        internal static void Write(string path, Entry[] entries)
        {
            using (var writer = new BinaryWriter(File.Open(path, FileMode.CreateNew)))
            {
                foreach (var entry in entries)
                {
                    entry.Offset = checked((uint)writer.BaseStream.Position);
                    writer.Write(0x04034B50U); // Local file header
                    entry.WriteFields(writer);
                    writer.Write(entry.Name);
                    writer.Write(entry.Data);
                }
                uint directoryOffset = checked((uint)writer.BaseStream.Position);
                foreach (var entry in entries)
                {
                    writer.Write(0x02014B50U); // Central directory file header
                    writer.Write((ushort)10); // Version made by
                    entry.WriteFields(writer);
                    writer.Write((ushort)0); // Comment length
                    writer.Write((ushort)0); // Disk number
                    writer.Write((ushort)0); // Internal attributes
                    writer.Write(0U); // External attributes
                    writer.Write(entry.Offset);
                    writer.Write(entry.Name);
                }
                uint directorySize = checked((uint)writer.BaseStream.Position - directoryOffset);
                ushort count = checked((ushort)entries.Length);
                writer.Write(0x06054B50U); // End of central directory
                writer.Write(0U); // Disk numbers
                writer.Write(count);
                writer.Write(count);
                writer.Write(directorySize);
                writer.Write(directoryOffset);
                writer.Write((ushort)0); // Comment length
            }
        }

        internal sealed class Entry
        {
            internal readonly byte[] Name, Data;
            internal uint Offset;
            private readonly uint crc;

            internal Entry(string name, byte[] data)
            {
                Name = Encoding.ASCII.GetBytes(name);
                Data = data;
                uint remaining = UInt32Max;
                foreach (byte value in data)
                {
                    remaining ^= value;
                    for (int bit = 0; bit < 8; bit++)
                        remaining = (remaining >> 1) ^ ((remaining & 1) != 0 ? 0xEDB88320U : 0U);
                }
                crc = ~remaining;
            }

            private const uint UInt32Max = 0xFFFFFFFFU;

            internal void WriteFields(BinaryWriter writer)
            {
                writer.Write((ushort)10); // ZIP 1.0 is enough for stored entries
                writer.Write((ushort)0); // Flags
                writer.Write((ushort)0); // Stored compression method
                writer.Write(0x00210000U); // DOS time and date, 1980-01-01
                writer.Write(crc);
                writer.Write(Data.Length);
                writer.Write(Data.Length);
                writer.Write(checked((ushort)Name.Length));
                writer.Write((ushort)0); // Extra field length
            }
        }
    }
}
