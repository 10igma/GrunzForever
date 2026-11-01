using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace GrunzForever.Core;

public sealed class EncryptedBlteException(string message) : Exception(message);

/// <summary>
/// Decodes Blizzard BLTE (Block Table Encoded) container streams (modes 'N', 'Z', 'F').
/// </summary>
public static class BlteDecoder
{
    public static byte[] Decode(ReadOnlySpan<byte> blteData)
    {
        if (blteData.Length < 8 ||
            blteData[0] != (byte)'B' ||
            blteData[1] != (byte)'L' ||
            blteData[2] != (byte)'T' ||
            blteData[3] != (byte)'E')
        {
            throw new InvalidDataException("Ungültiger BLTE-Header (Magic 'BLTE' fehlt).");
        }

        uint headerSize = BinaryPrimitives.ReadUInt32BigEndian(blteData.Slice(4, 4));
        if (headerSize == 0)
        {
            using var singleOut = new MemoryStream();
            DecodeChunk(blteData.Slice(8), expectedDecompressedSize: 0, singleOut);
            return singleOut.ToArray();
        }

        if (blteData.Length < headerSize || headerSize < 12)
        {
            throw new InvalidDataException($"Ungültige BLTE-Headergrösse ({headerSize} Bytes bei Puffergrösse {blteData.Length}).");
        }

        byte flags = blteData[8];
        int chunkCount = (blteData[9] << 16) | (blteData[10] << 8) | blteData[11];
        if (chunkCount <= 0)
        {
            return [];
        }

        int entrySize = flags == 0x10 ? 40 : 24;
        if (12 + chunkCount * entrySize > headerSize)
        {
            throw new InvalidDataException($"BLTE Chunk-Tabelle überschreitet HeaderSize (Flags=0x{flags:X2}, Chunks={chunkCount}).");
        }

        long totalDecompressed = 0;
        var chunks = new (int CompSize, int DecompSize)[chunkCount];
        int tableOffset = 12;

        for (int i = 0; i < chunkCount; i++)
        {
            int compSize = (int)BinaryPrimitives.ReadUInt32BigEndian(blteData.Slice(tableOffset, 4));
            int decompSize = (int)BinaryPrimitives.ReadUInt32BigEndian(blteData.Slice(tableOffset + 4, 4));
            chunks[i] = (compSize, decompSize);
            totalDecompressed += decompSize;
            tableOffset += entrySize;
        }

        using var output = new MemoryStream(totalDecompressed is > 0 and <= int.MaxValue ? (int)totalDecompressed : 65536);
        int dataOffset = (int)headerSize;

        for (int i = 0; i < chunkCount; i++)
        {
            var (compSize, decompSize) = chunks[i];
            if (compSize <= 0 || dataOffset + compSize > blteData.Length)
            {
                throw new InvalidDataException($"BLTE Chunk #{i} überschreitet Dateigrenze (Offset={dataOffset}, Size={compSize}, Total={blteData.Length}).");
            }

            DecodeChunk(blteData.Slice(dataOffset, compSize), decompSize, output);
            dataOffset += compSize;
        }

        return output.ToArray();
    }

    public static bool TryDecode(ReadOnlySpan<byte> blteData, out byte[]? decoded, out string? error)
    {
        try
        {
            decoded = Decode(blteData);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            decoded = null;
            error = ex.Message;
            return false;
        }
    }

    private static void DecodeChunk(ReadOnlySpan<byte> chunk, int expectedDecompressedSize, Stream output)
    {
        if (chunk.IsEmpty)
        {
            return;
        }

        char mode = (char)chunk[0];
        ReadOnlySpan<byte> payload = chunk.Slice(1);

        switch (mode)
        {
            case 'N': // Plain uncompressed data
                output.Write(payload);
                break;

            case 'Z': // Zlib compressed block
            {
                using var ms = new MemoryStream(payload.ToArray(), writable: false);
                using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
                zlib.CopyTo(output);
                break;
            }

            case 'F': // Nested BLTE frame
            {
                byte[] nested = Decode(payload);
                output.Write(nested, 0, nested.Length);
                break;
            }

            case 'E': // TaktKey encrypted block (used in WDC5 multi-section DB2s for locked TACT sections)
                if (expectedDecompressedSize > 0)
                {
                    // Per CASC/WDC5 specification, locked TACT sections are zero-filled to preserve section offsets.
                    byte[] zeros = new byte[expectedDecompressedSize];
                    output.Write(zeros, 0, zeros.Length);
                    break;
                }
                throw new EncryptedBlteException("BLTE-Block ist mit einem verschlüsselten TaktKey ('E') ohne Grössenangabe geschützt.");

            default:
                throw new NotSupportedException($"Unbekannter BLTE-Kompressionsmodus '{mode}' (0x{chunk[0]:X2}).");
        }
    }
}
