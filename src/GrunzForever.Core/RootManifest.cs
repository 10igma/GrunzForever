using System.Buffers.Binary;
using System.IO;

namespace GrunzForever.Core;

public readonly record struct RootFileEntry(
    int FileDataId,
    byte[] ContentKey,
    uint LocaleFlags,
    uint ContentFlags,
    ulong NameHash);

/// <summary>
/// Parses Blizzard CASC WoW Root Manifest (TSFM v1/v2) mapping FileDataID -> 16-byte ContentKey.
/// Prioritizes enUS (0x02) or All-Locales blocks.
/// </summary>
public sealed class RootManifest
{
    public const uint LocaleEnUS = 0x00000002;
    public const uint LocaleDeDE = 0x00000020;
    public const uint LocaleAll = 0xFFFFFFFF;
    private const uint FlagNoNameHash = 0x10000000;

    private readonly Dictionary<int, byte[]> _fileDataToCKey = new(capacity: 600_000);
    private readonly Dictionary<int, uint> _fileDataLocaleScore = new(capacity: 600_000);

    public int TotalBlocksParsed { get; private set; }
    public int TotalRecordsParsed { get; private set; }
    public int UniqueFileCount => _fileDataToCKey.Count;

    public static RootManifest Parse(ReadOnlySpan<byte> rootData, uint preferredLocaleMask = LocaleEnUS)
    {
        if (rootData.Length < 12)
        {
            throw new InvalidDataException("Root-Manifest ist zu klein.");
        }

        var manifest = new RootManifest();
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(rootData.Slice(0, 4));

        int pos;
        uint version = 0;
        bool allowNonNamedFiles = false;

        // 'TSFM' in little-endian = 0x4D465354 ("TSFM" ASCII bytes: 54 53 46 4D = 0x4D465354)
        if (magic == 0x4D465354u)
        {
            uint field1 = BinaryPrimitives.ReadUInt32LittleEndian(rootData.Slice(4, 4));
            uint field2 = BinaryPrimitives.ReadUInt32LittleEndian(rootData.Slice(8, 4));

            // In TSFM v2+, field1 is headerSize (e.g. 24 = 0x18) and field2 is version (1 or 2)
            if (field1 is >= 16 and <= 128 && field2 is >= 1 and <= 5)
            {
                int headerSize = (int)field1;
                version = field2;
                uint totalFiles = BinaryPrimitives.ReadUInt32LittleEndian(rootData.Slice(12, 4));
                uint namedFiles = BinaryPrimitives.ReadUInt32LittleEndian(rootData.Slice(16, 4));
                allowNonNamedFiles = totalFiles != namedFiles;
                pos = headerSize;
            }
            else
            {
                // Classic TSFM v1 (12-byte header: magic, totalFiles, namedFiles)
                uint totalFiles = field1;
                uint namedFiles = field2;
                allowNonNamedFiles = totalFiles != namedFiles;
                version = 1;
                pos = 12;
            }
        }
        else
        {
            // Legacy headerless root
            pos = 0;
        }

        int blockHdrSize = version >= 2 ? 17 : 12;

        while (pos + blockHdrSize <= rootData.Length)
        {
            int numRecords = BinaryPrimitives.ReadInt32LittleEndian(rootData.Slice(pos, 4));
            if (numRecords < 0 || numRecords > 5_000_000)
            {
                throw new InvalidDataException($"Ungültige Blockgrösse ({numRecords}) in Root-Manifest bei Offset {pos}.");
            }

            uint contentFlags;
            uint localeFlags;

            if (version >= 2)
            {
                localeFlags = BinaryPrimitives.ReadUInt32LittleEndian(rootData.Slice(pos + 4, 4));
                uint f1 = BinaryPrimitives.ReadUInt32LittleEndian(rootData.Slice(pos + 8, 4));
                uint f2 = BinaryPrimitives.ReadUInt32LittleEndian(rootData.Slice(pos + 12, 4));
                byte f3 = rootData[pos + 16];
                contentFlags = f1 | f2 | ((uint)f3 << 17);
                pos += 17;
            }
            else
            {
                contentFlags = BinaryPrimitives.ReadUInt32LittleEndian(rootData.Slice(pos + 4, 4));
                localeFlags = BinaryPrimitives.ReadUInt32LittleEndian(rootData.Slice(pos + 8, 4));
                pos += 12;
            }

            bool hasNameHashes = !(allowNonNamedFiles && (contentFlags & FlagNoNameHash) != 0);

            int deltasBytes = numRecords * 4;
            int cKeysBytes = numRecords * 16;
            int nameHashesBytes = hasNameHashes ? numRecords * 8 : 0;
            int totalPayload = deltasBytes + cKeysBytes + nameHashesBytes;

            if (pos + totalPayload > rootData.Length)
            {
                throw new InvalidDataException($"Root-Block überschreitet Dateiende bei Offset {pos} (benötigt {totalPayload} B).");
            }

            ReadOnlySpan<byte> deltasSpan = rootData.Slice(pos, deltasBytes);
            ReadOnlySpan<byte> cKeysSpan = rootData.Slice(pos + deltasBytes, cKeysBytes);
            pos += totalPayload;

            uint score = ScoreLocaleAndFlags(localeFlags, contentFlags, preferredLocaleMask);
            int fileDataId = -1;

            for (int i = 0; i < numRecords; i++)
            {
                int delta = BinaryPrimitives.ReadInt32LittleEndian(deltasSpan.Slice(i * 4, 4));
                fileDataId = fileDataId + 1 + delta;

                if (!manifest._fileDataLocaleScore.TryGetValue(fileDataId, out uint existingScore) || score > existingScore)
                {
                    manifest._fileDataLocaleScore[fileDataId] = score;
                    manifest._fileDataToCKey[fileDataId] = cKeysSpan.Slice(i * 16, 16).ToArray();
                }
            }

            manifest.TotalBlocksParsed++;
            manifest.TotalRecordsParsed += numRecords;
        }

        return manifest;
    }

    public bool TryGetContentKey(int fileDataId, out byte[] cKey) =>
        _fileDataToCKey.TryGetValue(fileDataId, out cKey!);

    public IEnumerable<KeyValuePair<int, byte[]>> AllEntries => _fileDataToCKey;

    private static uint ScoreLocaleAndFlags(uint localeFlags, uint contentFlags, uint preferredLocale)
    {
        uint score = 1;
        if ((localeFlags & preferredLocale) != 0)
        {
            score += 100;
        }
        else if (localeFlags == LocaleAll)
        {
            score += 80;
        }
        else if ((localeFlags & LocaleEnUS) != 0)
        {
            score += 60;
        }

        // Prefer standard non-LowViolence blocks (LowViolence = 0x80)
        if ((contentFlags & 0x80u) == 0)
        {
            score += 10;
        }

        return score;
    }
}
