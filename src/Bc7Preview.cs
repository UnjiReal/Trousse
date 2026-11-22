using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SkyrimOutfit
{
    internal static class Bc7Preview
    {
        private const int PreviewSize = 1024;
        private static readonly int[] Subsets = { 3, 2, 3, 2, 1, 1, 1, 2 };
        private static readonly int[] ColorBits = { 4, 6, 5, 7, 5, 7, 7, 5 };
        private static readonly int[] AlphaBits = { 0, 0, 0, 0, 6, 8, 7, 5 };
        private static readonly int[] PBits = { 1, 1, 0, 1, 0, 0, 1, 1 };
        private static readonly int[] IndexBits = { 3, 3, 2, 2, 2, 2, 4, 2 };
        private static readonly int[] SecondIndexBits = { 0, 0, 0, 0, 3, 2, 0, 0 };
        private static readonly int[][] Weights = {
            new[] { 0, 21, 43, 64 }, new[] { 0, 9, 18, 27, 37, 46, 55, 64 },
            new[] { 0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64 }
        };

        // BC7 partition and anchor tables from Sergii Kudlai's bcdec (The Unlicense).
        // https://github.com/iOrange/bcdec
        // Lower 32 bits: two bits per texel for the subset. Upper bits: one anchor flag per texel.
        private static readonly ulong[] Patterns = {
            0x800150505050UL, 0x800140404040UL, 0x800154545454UL, 0x800154505040UL,
            0x800150404000UL, 0x800155545450UL, 0x800155545040UL, 0x800154504000UL,
            0x800150400000UL, 0x800155555450UL, 0x800155544000UL, 0x800154400000UL,
            0x800155555440UL, 0x800155550000UL, 0x800155555500UL, 0x800155000000UL,
            0x800155150100UL, 0x000500004054UL, 0x010115010000UL, 0x000500405054UL,
            0x000500004050UL, 0x010115050100UL, 0x010105010000UL, 0x800140505054UL,
            0x000500404050UL, 0x010105010100UL, 0x000514141414UL, 0x000505141450UL,
            0x010101155440UL, 0x010100555500UL, 0x000515014054UL, 0x000505414150UL,
            0x800144444444UL, 0x800155005500UL, 0x004111441144UL, 0x010105055050UL,
            0x000505500550UL, 0x010111114444UL, 0x800141144114UL, 0x800144111144UL,
            0x000515055054UL, 0x010101055040UL, 0x000505041050UL, 0x000505455150UL,
            0x000514414114UL, 0x800150050550UL, 0x800141411414UL, 0x004100141400UL,
            0x004100041504UL, 0x000500105410UL, 0x004110541000UL, 0x010104150400UL,
            0x800150410514UL, 0x800141051450UL, 0x000505415014UL, 0x000514054150UL,
            0x800141050514UL, 0x800141505014UL, 0x800140011554UL, 0x800154150140UL,
            0x800150505500UL, 0x000500555050UL, 0x000515151010UL, 0x800154540404UL,
            0x8009aa685050UL, 0x01096a5a5040UL, 0x81015a5a4200UL, 0x80095450a0a8UL,
            0x8101a5a50000UL, 0x8009a0a05050UL, 0x80095555a0a0UL, 0x81015a5a5050UL,
            0x8101aa550000UL, 0x8101aa555500UL, 0x8041aaaa5500UL, 0x804190909090UL,
            0x804194949494UL, 0x8021a4a4a4a4UL, 0x8009a9a59450UL, 0x01092a0a4250UL,
            0x8009a5945040UL, 0x01090a425054UL, 0x8101a5a5a500UL, 0x800955a0a0a0UL,
            0x8009a8a85454UL, 0x01096a6a4040UL, 0x8041a4a45000UL, 0x05011a1a0500UL,
            0x00290050a4a4UL, 0x8101aaa59090UL, 0x014114696914UL, 0x044169691400UL,
            0x8101a08585a0UL, 0x8021aa821414UL, 0x840150a4a450UL, 0x81016a5a0200UL,
            0x8101a9a58000UL, 0x80095090a0a8UL, 0x8009a8a09050UL, 0x042124242424UL,
            0x044100aa5500UL, 0x050124924924UL, 0x030124499224UL, 0x840150a50a50UL,
            0x8041500aa550UL, 0x8009aaaa4444UL, 0x810166660000UL, 0x8021a5a0a5a0UL,
            0x800950a050a0UL, 0x804169286928UL, 0x804144aaaa44UL, 0x810166666600UL,
            0x8009aa444444UL, 0x800954a854a8UL, 0x802195809580UL, 0x802196969600UL,
            0x8021a85454a8UL, 0x810180959580UL, 0x8021aa141414UL, 0x840196960000UL,
            0x8021aaaa1414UL, 0x8401a05050a0UL, 0x8101a0a5a5a0UL, 0xa00196000000UL,
            0x800940804080UL, 0x9001a9a8a9a8UL, 0x8009aaaaaa44UL, 0x01092a4a5254UL,
        };

        internal static BitmapSource TryRead(Stream stream)
        {
            if (stream.Length < 148) return null;
            byte[] header = new byte[148];
            if (stream.Read(header, 0, header.Length) != header.Length) return null;
            if (BitConverter.ToUInt32(header, 0) != 0x20534444 || BitConverter.ToInt32(header, 4) != 124 ||
                BitConverter.ToUInt32(header, 84) != 0x30315844) return null;
            int format = BitConverter.ToInt32(header, 128);
            if (format < 97 || format > 99) return null;

            int width = BitConverter.ToInt32(header, 16);
            int height = BitConverter.ToInt32(header, 12);
            int mips = BitConverter.ToInt32(header, 28);
            if (width < 1 || width > 8192 || height < 1 || height > 8192 || mips > 14) return null;

            // A stored mip saves decoding and memory for large outfits.
            for (int level = 1; level < mips && Math.Max(width, height) > PreviewSize; level++)
            {
                stream.Position += BlockCount(width) * BlockCount(height) * 16L;
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
            }
            if (stream.Length - stream.Position < BlockCount(width) * BlockCount(height) * 16L) return null;
            return ReadPixels(stream, width, height);
        }

        private static int BlockCount(int length) { return (length + 3) / 4; }

        private static BitmapSource ReadPixels(Stream stream, int width, int height)
        {
            int step = Math.Max(1, (Math.Max(width, height) + PreviewSize - 1) / PreviewSize);
            int columns = (width + step - 1) / step;
            int rows = (height + step - 1) / step;
            int stride = columns * 4;
            byte[] pixels = new byte[stride * rows];
            byte[] blocks = new byte[BlockCount(width) * 16];
            int[] endpoints = new int[24];
            int[] indices = new int[16];
            byte[] tile = new byte[64];
            for (int y = 0; y < height; y += 4)
            {
                if (stream.Read(blocks, 0, blocks.Length) != blocks.Length) return null;
                for (int x = 0; x < width; x += 4)
                {
                    var bits = new BlockBits(blocks, x * 4);
                    DecodeBlock(ref bits, endpoints, indices, tile);
                    for (int dy = 0; dy < Math.Min(4, height - y); dy++)
                    for (int dx = 0; dx < Math.Min(4, width - x); dx++)
                    {
                        if ((y + dy) % step != 0 || (x + dx) % step != 0) continue;
                        int destination = (y + dy) / step * stride + (x + dx) / step * 4;
                        Buffer.BlockCopy(tile, (dy * 4 + dx) * 4, pixels, destination, 4);
                    }
                }
            }
            return BitmapSource.Create(columns, rows, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        }

        private static void DecodeBlock(ref BlockBits bits, int[] endpoints, int[] indices, byte[] tile)
        {
            int mode = 0;
            while (mode < 8 && bits.Read(1) == 0) mode++;
            if (mode == 8) { Array.Clear(tile, 0, tile.Length); return; }

            int subsets = Subsets[mode];
            ulong pattern = 1UL << 32;
            if (subsets > 1)
                pattern = Patterns[(subsets - 2) * 64 + bits.Read(mode == 0 ? 4 : 6)];
            int secondary = SecondIndexBits[mode];
            int rotation = secondary == 0 ? 0 : bits.Read(2);
            bool swapIndices = mode == 4 && bits.Read(1) == 1;
            ReadEndpoints(ref bits, mode, subsets * 2, endpoints);

            int indexBits = IndexBits[mode];
            int[] primaryWeights = Weights[indexBits - 2];
            int[] secondaryWeights = secondary == 0 ? primaryWeights : Weights[secondary - 2];
            for (int texel = 0; texel < 16; texel++)
            {
                int fixup = (int)((pattern >> (32 + texel)) & 1);
                indices[texel] = primaryWeights[bits.Read(indexBits - fixup)];
            }
            for (int texel = 0; texel < 16; texel++)
            {
                int mainWeight = indices[texel];
                int extraWeight = secondary == 0 ? mainWeight : secondaryWeights[bits.Read(secondary - (texel == 0 ? 1 : 0))];
                int colorWeight = swapIndices ? extraWeight : mainWeight;
                int alphaWeight = swapIndices ? mainWeight : extraWeight;
                int endpoint = (int)((pattern >> (texel * 2)) & 3) * 8;
                int output = texel * 4;
                tile[output + 2] = Interpolate(endpoints, endpoint, colorWeight);
                tile[output + 1] = Interpolate(endpoints, endpoint + 1, colorWeight);
                tile[output] = Interpolate(endpoints, endpoint + 2, colorWeight);
                tile[output + 3] = Interpolate(endpoints, endpoint + 3, alphaWeight);
                if (rotation != 0)
                {
                    byte alpha = tile[output + 3];
                    tile[output + 3] = tile[output + 3 - rotation];
                    tile[output + 3 - rotation] = alpha;
                }
            }
        }

        private static void ReadEndpoints(ref BlockBits bits, int mode, int count, int[] endpoints)
        {
            int colorBits = ColorBits[mode], alphaBits = AlphaBits[mode], p = PBits[mode];
            for (int channel = 0; channel < 4; channel++)
            {
                int precision = channel == 3 ? alphaBits : colorBits;
                for (int i = 0; i < count; i++)
                    endpoints[i * 4 + channel] = precision == 0 ? 0 : bits.Read(precision);
            }
            int extension = 0;
            for (int i = 0; i < count; i++)
            {
                // Mode 1 shares the endpoint extension bit within each pair.
                if (p != 0 && (mode != 1 || i % 2 == 0)) extension = bits.Read(1);
                for (int channel = 0; channel < 4; channel++)
                {
                    int offset = i * 4 + channel;
                    int precision = (channel == 3 ? alphaBits : colorBits) + p;
                    int value = ((endpoints[offset] << p) | extension) << (8 - precision);
                    endpoints[offset] = channel == 3 && alphaBits == 0 ? 255 : value | (value >> precision);
                }
            }
        }

        private static byte Interpolate(int[] endpoints, int offset, int weight)
        {
            return (byte)((endpoints[offset] * (64 - weight) + endpoints[offset + 4] * weight + 32) >> 6);
        }

        private struct BlockBits
        {
            private ulong low, high;

            internal BlockBits(byte[] data, int offset)
            {
                low = BitConverter.ToUInt64(data, offset);
                high = BitConverter.ToUInt64(data, offset + 8);
            }

            internal int Read(int count)
            {
                int value = (int)(low & ((1UL << count) - 1));
                low = (low >> count) | (high << (64 - count));
                high >>= count;
                return value;
            }
        }
    }
}
