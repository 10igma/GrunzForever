using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace GrunzForever.Core;

/// <summary>
/// Native decoder for Blizzard BLP2 texture files (Paletted, DXT1/BC1, DXT3/BC2, DXT5/BC3, and Uncompressed BGRA),
/// including a zero-dependency RFC-2083 PNG encoder.
/// </summary>
public static class BlpDecoder
{
    public static (int Width, int Height, byte[] RgbaPixels) DecodeToRgba32(ReadOnlySpan<byte> blp)
    {
        if (blp.Length < 1172 || Encoding.ASCII.GetString(blp.Slice(0, 4)) != "BLP2")
        {
            throw new InvalidDataException("Ungültige BLP2-Textur.");
        }

        byte colorEncoding = blp[8];
        byte alphaSize = blp[9];
        byte preferredFormat = blp[10];
        int width = BinaryPrimitives.ReadInt32LittleEndian(blp.Slice(12, 4));
        int height = BinaryPrimitives.ReadInt32LittleEndian(blp.Slice(16, 4));
        int mip0Offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(blp.Slice(20, 4));
        int mip0Size = (int)BinaryPrimitives.ReadUInt32LittleEndian(blp.Slice(84, 4));

        if (width <= 0 || height <= 0 || mip0Offset <= 0 || mip0Offset + mip0Size > blp.Length)
        {
            throw new InvalidDataException($"Ungültige BLP2-Dimensionen ({width}x{height}) oder Mip0-Grenzen.");
        }

        ReadOnlySpan<byte> mip0 = blp.Slice(mip0Offset, mip0Size);
        byte[] rgba = new byte[width * height * 4];

        switch (colorEncoding)
        {
            case 1:
                DecodePaletted(blp.Slice(148, 1024), mip0, width, height, alphaSize, rgba);
                break;

            case 2:
                DecodeDxtc(mip0, width, height, alphaSize, preferredFormat, rgba);
                break;

            case 3:
            case 4:
                DecodeUncompressedBgra(mip0, width, height, rgba);
                break;

            default:
                throw new NotSupportedException($"Nicht unterstütztes BLP2 ColorEncoding={colorEncoding}.");
        }

        return (width, height, rgba);
    }

    public static byte[] DecodeToPngBytes(ReadOnlySpan<byte> blp)
    {
        var (w, h, rgba) = DecodeToRgba32(blp);
        return EncodePng(w, h, rgba);
    }

    private static void DecodePaletted(
        ReadOnlySpan<byte> paletteBgra,
        ReadOnlySpan<byte> mip0,
        int width,
        int height,
        byte alphaSize,
        Span<byte> rgba)
    {
        int pixelCount = width * height;
        ReadOnlySpan<byte> indices = mip0.Slice(0, Math.Min(pixelCount, mip0.Length));
        ReadOnlySpan<byte> alphaSpan = mip0.Length > pixelCount ? mip0.Slice(pixelCount) : ReadOnlySpan<byte>.Empty;

        for (int i = 0; i < indices.Length; i++)
        {
            int palOff = indices[i] * 4;
            byte b = paletteBgra[palOff + 0];
            byte g = paletteBgra[palOff + 1];
            byte r = paletteBgra[palOff + 2];

            byte a = 255;
            if (alphaSize == 8 && i < alphaSpan.Length)
            {
                a = alphaSpan[i];
            }
            else if (alphaSize == 4 && (i >> 1) < alphaSpan.Length)
            {
                int nib = (i & 1) == 0 ? (alphaSpan[i >> 1] & 0x0F) : (alphaSpan[i >> 1] >> 4);
                a = (byte)(nib * 17);
            }
            else if (alphaSize == 1 && (i >> 3) < alphaSpan.Length)
            {
                a = ((alphaSpan[i >> 3] >> (i & 7)) & 1) != 0 ? (byte)255 : (byte)0;
            }

            int dst = i * 4;
            rgba[dst + 0] = r;
            rgba[dst + 1] = g;
            rgba[dst + 2] = b;
            rgba[dst + 3] = a;
        }
    }

    private static void DecodeUncompressedBgra(ReadOnlySpan<byte> mip0, int width, int height, Span<byte> rgba)
    {
        int count = Math.Min(width * height, mip0.Length / 4);
        for (int i = 0; i < count; i++)
        {
            int s = i * 4;
            rgba[s + 0] = mip0[s + 2]; // R
            rgba[s + 1] = mip0[s + 1]; // G
            rgba[s + 2] = mip0[s + 0]; // B
            rgba[s + 3] = mip0[s + 3]; // A
        }
    }

    private static void DecodeDxtc(
        ReadOnlySpan<byte> mip0,
        int width,
        int height,
        byte alphaSize,
        byte preferredFormat,
        Span<byte> rgba)
    {
        bool isDxt3 = preferredFormat == 1;
        bool isDxt5 = preferredFormat == 7;
        int blockSize = (isDxt3 || isDxt5) ? 16 : 8;

        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;
        int pos = 0;

        Span<byte> alphas = stackalloc byte[16];
        Span<(byte R, byte G, byte B, byte A)> colors = stackalloc (byte, byte, byte, byte)[4];
        Span<byte> alphaLut = stackalloc byte[8];

        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                if (pos + blockSize > mip0.Length)
                {
                    return;
                }

                ReadOnlySpan<byte> block = mip0.Slice(pos, blockSize);
                pos += blockSize;

                ReadOnlySpan<byte> colorBlock;
                if (isDxt3)
                {
                    ulong aBits = BinaryPrimitives.ReadUInt64LittleEndian(block.Slice(0, 8));
                    for (int p = 0; p < 16; p++)
                    {
                        alphas[p] = (byte)(((aBits >> (p * 4)) & 0x0FUL) * 17UL);
                    }
                    colorBlock = block.Slice(8, 8);
                }
                else if (isDxt5)
                {
                    byte a0 = block[0];
                    byte a1 = block[1];
                    alphaLut[0] = a0;
                    alphaLut[1] = a1;
                    if (a0 > a1)
                    {
                        for (int k = 1; k <= 6; k++)
                        {
                            alphaLut[k + 1] = (byte)(((7 - k) * a0 + k * a1 + 3) / 7);
                        }
                    }
                    else
                    {
                        for (int k = 1; k <= 4; k++)
                        {
                            alphaLut[k + 1] = (byte)(((5 - k) * a0 + k * a1 + 2) / 5);
                        }
                        alphaLut[6] = 0;
                        alphaLut[7] = 255;
                    }

                    ulong idxBits = 0;
                    for (int b = 0; b < 6; b++)
                    {
                        idxBits |= (ulong)block[2 + b] << (b * 8);
                    }

                    for (int p = 0; p < 16; p++)
                    {
                        int aIdx = (int)((idxBits >> (p * 3)) & 0x07UL);
                        alphas[p] = alphaLut[aIdx];
                    }
                    colorBlock = block.Slice(8, 8);
                }
                else
                {
                    alphas.Fill(255);
                    colorBlock = block.Slice(0, 8);
                }

                ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(colorBlock.Slice(0, 2));
                ushort c1 = BinaryPrimitives.ReadUInt16LittleEndian(colorBlock.Slice(2, 2));
                uint cIndices = BinaryPrimitives.ReadUInt32LittleEndian(colorBlock.Slice(4, 4));

                colors[0] = UnpackRgb565(c0);
                colors[1] = UnpackRgb565(c1);

                bool fourColorMode = isDxt3 || isDxt5 || c0 > c1 || alphaSize == 0;
                if (fourColorMode)
                {
                    colors[2] = (
                        (byte)((2 * colors[0].R + colors[1].R + 1) / 3),
                        (byte)((2 * colors[0].G + colors[1].G + 1) / 3),
                        (byte)((2 * colors[0].B + colors[1].B + 1) / 3),
                        255);
                    colors[3] = (
                        (byte)((colors[0].R + 2 * colors[1].R + 1) / 3),
                        (byte)((colors[0].G + 2 * colors[1].G + 1) / 3),
                        (byte)((colors[0].B + 2 * colors[1].B + 1) / 3),
                        255);
                }
                else
                {
                    colors[2] = (
                        (byte)((colors[0].R + colors[1].R) / 2),
                        (byte)((colors[0].G + colors[1].G) / 2),
                        (byte)((colors[0].B + colors[1].B) / 2),
                        255);
                    colors[3] = (0, 0, 0, 0);
                }

                for (int py = 0; py < 4; py++)
                {
                    int y = by * 4 + py;
                    if (y >= height) continue;

                    for (int px = 0; px < 4; px++)
                    {
                        int x = bx * 4 + px;
                        if (x >= width) continue;

                        int pIdx = py * 4 + px;
                        int cIdx = (int)((cIndices >> (pIdx * 2)) & 0x03u);
                        var c = colors[cIdx];

                        int dst = (y * width + x) * 4;
                        rgba[dst + 0] = c.R;
                        rgba[dst + 1] = c.G;
                        rgba[dst + 2] = c.B;
                        rgba[dst + 3] = (isDxt3 || isDxt5) ? alphas[pIdx] : c.A;
                    }
                }
            }
        }
    }

    private static (byte R, byte G, byte B, byte A) UnpackRgb565(ushort v)
    {
        int r5 = (v >> 11) & 0x1F;
        int g6 = (v >> 5) & 0x3F;
        int b5 = v & 0x1F;
        return (
            (byte)((r5 * 255 + 15) / 31),
            (byte)((g6 * 255 + 31) / 63),
            (byte)((b5 * 255 + 15) / 31),
            255);
    }

    public static byte[] EncodePng(int width, int height, ReadOnlySpan<byte> rgba)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        // IHDR
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(4, 4), height);
        ihdr[8] = 8; // bit depth 8
        ihdr[9] = 6; // color type RGBA
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WritePngChunk(ms, "IHDR"u8, ihdr);

        // IDAT (filter byte 0 per scanline)
        int stride = width * 4;
        byte[] rawScanlines = new byte[height * (stride + 1)];
        for (int y = 0; y < height; y++)
        {
            int dstRow = y * (stride + 1);
            rawScanlines[dstRow] = 0; // Filter: None
            rgba.Slice(y * stride, stride).CopyTo(rawScanlines.AsSpan(dstRow + 1, stride));
        }

        byte[] compressed;
        using (var zOut = new MemoryStream())
        {
            using (var zs = new ZLibStream(zOut, CompressionLevel.Optimal, leaveOpen: true))
            {
                zs.Write(rawScanlines);
            }
            compressed = zOut.ToArray();
        }
        WritePngChunk(ms, "IDAT"u8, compressed);

        // IEND
        WritePngChunk(ms, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return ms.ToArray();
    }

    private static void WritePngChunk(Stream outStream, ReadOnlySpan<byte> type4, ReadOnlySpan<byte> data)
    {
        Span<byte> lenBuf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lenBuf, (uint)data.Length);
        outStream.Write(lenBuf);
        outStream.Write(type4);
        if (!data.IsEmpty)
        {
            outStream.Write(data);
        }

        uint crc = ComputeCrc32(type4, data);
        BinaryPrimitives.WriteUInt32BigEndian(lenBuf, crc);
        outStream.Write(lenBuf);
    }

    private static readonly uint[] CrcTable = CreateCrcTable();

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : (c >> 1);
            }
            table[n] = c;
        }
        return table;
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte x in a)
        {
            c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        }
        foreach (byte x in b)
        {
            c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        }
        return c ^ 0xFFFFFFFFu;
    }
}
