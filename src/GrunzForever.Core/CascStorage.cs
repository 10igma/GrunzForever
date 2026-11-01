using System.Buffers.Binary;
using System.IO;

namespace GrunzForever.Core;

public readonly record struct EKeyPrefix9(ulong High8, byte Byte8)
{
    public static EKeyPrefix9 FromSpan(ReadOnlySpan<byte> key)
    {
        if (key.Length < 9)
        {
            throw new ArgumentException("EKey muss mindestens 9 Bytes lang sein.", nameof(key));
        }

        ulong hi = BinaryPrimitives.ReadUInt64BigEndian(key.Slice(0, 8));
        return new EKeyPrefix9(hi, key[8]);
    }
}

public readonly record struct CascIndexEntry(int ArchiveIndex, long ArchiveOffset, int EncodedSize);

public sealed class WowBuildMetadata
{
    public required string FlavorDirectory { get; init; }
    public required string WowRootDirectory { get; init; }
    public required string Product { get; init; }
    public required string Version { get; init; }
    public required string BuildName { get; init; }
    public required string BuildKey { get; init; }
    public required string CdnKey { get; init; }
    public required byte[] RootCKey { get; init; }
    public byte[]? RootEKey { get; init; }
    public required byte[] EncodingCKey { get; init; }
    public required byte[] EncodingEKey { get; init; }
}

/// <summary>
/// High-speed local Blizzard CASC storage reader (.build.info -> build config -> .idx v7 -> data.xxx -> BLTE -> EN).
/// Read-only access to local installation archives.
/// </summary>
public sealed class CascStorage : IDisposable
{
    private readonly Dictionary<EKeyPrefix9, CascIndexEntry> _indexMap = new(capacity: 500_000);
    private readonly Dictionary<int, FileStream> _archiveStreams = [];
    private readonly object _streamLock = new();

    private byte[] _encodingData = [];
    private int _cKeyPageSize;
    private int _cKeyPageCount;
    private int _cKeyHeadersOffset;
    private int _cKeyPagesDataOffset;

    public WowBuildMetadata Build { get; }
    public string DataFilesDir { get; }
    public int IndexedEntryCount => _indexMap.Count;

    private CascStorage(WowBuildMetadata build, string dataFilesDir)
    {
        Build = build;
        DataFilesDir = dataFilesDir;
    }

    public static CascStorage Open(string flavorDirectory)
    {
        if (!Directory.Exists(flavorDirectory))
        {
            throw new DirectoryNotFoundException($"Spiele-Ordner nicht gefunden: {flavorDirectory}");
        }

        string wowRoot = Directory.GetParent(flavorDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName
                         ?? throw new InvalidOperationException("Übergeordneter World of Warcraft Ordner konnte nicht ermittelt werden.");

        string flavorInfoPath = Path.Combine(flavorDirectory, ".flavor.info");
        string product = "wowf";
        if (File.Exists(flavorInfoPath))
        {
            var lines = ReadAllLinesShared(flavorInfoPath)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.Contains('!'))
                .ToList();
            if (lines.Count > 0)
            {
                product = lines[0];
            }
        }

        string buildInfoPath = Path.Combine(wowRoot, ".build.info");
        if (!File.Exists(buildInfoPath))
        {
            throw new FileNotFoundException($".build.info nicht gefunden unter: {buildInfoPath}");
        }

        var buildMeta = ParseBuildAndConfig(flavorDirectory, wowRoot, buildInfoPath, product);
        string dataFilesDir = Path.Combine(wowRoot, "Data", "data");
        if (!Directory.Exists(dataFilesDir))
        {
            throw new DirectoryNotFoundException($"CASC Data\\data Verzeichnis fehlt: {dataFilesDir}");
        }

        var storage = new CascStorage(buildMeta, dataFilesDir);
        storage.LoadLocalIndices();
        storage.LoadEncodingTable();
        return storage;
    }

    private static WowBuildMetadata ParseBuildAndConfig(
        string flavorDir,
        string wowRoot,
        string buildInfoPath,
        string targetProduct)
    {
        string[] lines = ReadAllLinesShared(buildInfoPath);
        if (lines.Length < 2)
        {
            throw new InvalidDataException(".build.info enthält keine Build-Zeilen.");
        }

        string[] headers = lines[0].Split('|')
            .Select(h => h.Split('!')[0].Trim())
            .ToArray();

        int idxProduct = Array.FindIndex(headers, h => h.Equals("Product", StringComparison.OrdinalIgnoreCase));
        int idxVersion = Array.FindIndex(headers, h => h.Equals("Version", StringComparison.OrdinalIgnoreCase));
        int idxBuildKey = Array.FindIndex(headers, h => h.Equals("Build Key", StringComparison.OrdinalIgnoreCase));
        int idxCdnKey = Array.FindIndex(headers, h => h.Equals("CDN Key", StringComparison.OrdinalIgnoreCase));

        string? version = null;
        string? buildKey = null;
        string? cdnKey = null;

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            string[] cols = lines[i].Split('|');
            if (idxProduct >= 0 && idxProduct < cols.Length &&
                cols[idxProduct].Trim().Equals(targetProduct, StringComparison.OrdinalIgnoreCase))
            {
                version = idxVersion >= 0 && idxVersion < cols.Length ? cols[idxVersion].Trim() : "unknown";
                buildKey = idxBuildKey >= 0 && idxBuildKey < cols.Length ? cols[idxBuildKey].Trim() : null;
                cdnKey = idxCdnKey >= 0 && idxCdnKey < cols.Length ? cols[idxCdnKey].Trim() : null;
                break;
            }
        }

        if (string.IsNullOrEmpty(buildKey))
        {
            throw new InvalidDataException($"Produkt '{targetProduct}' wurde in .build.info nicht gefunden.");
        }

        string configPath = Path.Combine(
            wowRoot, "Data", "config",
            buildKey[..2], buildKey.Substring(2, 2), buildKey);

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"CASC Build-Config nicht gefunden: {configPath}");
        }

        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in ReadAllLinesShared(configPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            kv[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }

        string[] rootParts = (kv.GetValueOrDefault("root") ?? throw new InvalidDataException("Build-Config enthält keinen 'root'-Eintrag."))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] encParts = (kv.GetValueOrDefault("encoding") ?? throw new InvalidDataException("Build-Config enthält keinen 'encoding'-Eintrag."))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (encParts.Length < 2)
        {
            throw new InvalidDataException("Build-Config 'encoding' benötigt CKey und EKey.");
        }

        return new WowBuildMetadata
        {
            FlavorDirectory = flavorDir,
            WowRootDirectory = wowRoot,
            Product = targetProduct,
            Version = version ?? "unknown",
            BuildName = kv.GetValueOrDefault("build-name") ?? targetProduct,
            BuildKey = buildKey,
            CdnKey = cdnKey ?? string.Empty,
            RootCKey = Convert.FromHexString(rootParts[0]),
            RootEKey = rootParts.Length >= 2 ? Convert.FromHexString(rootParts[1]) : null,
            EncodingCKey = Convert.FromHexString(encParts[0]),
            EncodingEKey = Convert.FromHexString(encParts[1])
        };
    }

    private void LoadLocalIndices()
    {
        // Group .idx files in Data\data by 2-hex-char bucket prefix and load the newest version per bucket
        var latestIdxFiles = Directory.EnumerateFiles(DataFilesDir, "*.idx")
            .Select(p => new FileInfo(p))
            .Where(fi => fi.Name.Length >= 10)
            .GroupBy(fi => fi.Name[..2].ToLowerInvariant())
            .Select(g => g.OrderByDescending(fi => fi.Name, StringComparer.OrdinalIgnoreCase).First())
            .ToList();

        foreach (var idxFile in latestIdxFiles)
        {
            byte[] buf = ReadAllBytesShared(idxFile.FullName);
            if (buf.Length < 0x28) continue;

            ushort idxVersion = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(0x08, 2));
            if (idxVersion != 7) continue;

            byte sizeBytes = buf[0x0C];   // typically 4
            byte offsetBytes = buf[0x0D]; // typically 5
            byte keyBytes = buf[0x0E];    // typically 9
            byte segmentBits = buf[0x0F]; // typically 30 (1 GB segments)

            int entryStride = keyBytes + offsetBytes + sizeBytes;
            if (keyBytes < 9 || offsetBytes != 5 || sizeBytes != 4)
            {
                continue;
            }

            int entriesBytes = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(0x20, 4));
            int pos = 0x28;
            int end = Math.Min(buf.Length, pos + entriesBytes);
            long offsetMask = (1L << segmentBits) - 1L;

            while (pos + entryStride <= end)
            {
                var keyPrefix = EKeyPrefix9.FromSpan(buf.AsSpan(pos, 9));

                // 5-byte Big-Endian combined ArchiveIndex + ArchiveOffset
                long packedOffset =
                    ((long)buf[pos + keyBytes] << 32) |
                    ((long)buf[pos + keyBytes + 1] << 24) |
                    ((long)buf[pos + keyBytes + 2] << 16) |
                    ((long)buf[pos + keyBytes + 3] << 8) |
                    buf[pos + keyBytes + 4];

                int encodedSize = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(pos + keyBytes + 5, 4));
                if (encodedSize > 30)
                {
                    int archiveIndex = (int)(packedOffset >> segmentBits);
                    long archiveOffset = packedOffset & offsetMask;
                    _indexMap[keyPrefix] = new CascIndexEntry(archiveIndex, archiveOffset, encodedSize);
                }

                pos += entryStride;
            }
        }
    }

    private void LoadEncodingTable()
    {
        byte[]? rawDecoded = ReadFileByEKey(Build.EncodingEKey);
        if (rawDecoded == null || rawDecoded.Length < 22)
        {
            throw new InvalidDataException("CASC 'encoding'-Tabelle konnte nicht geladen oder dekomprimiert werden.");
        }

        if (rawDecoded[0] != (byte)'E' || rawDecoded[1] != (byte)'N')
        {
            throw new InvalidDataException("Ungültige Signatur in CASC 'encoding'-Tabelle (erwartet 'EN').");
        }

        int cKeyLen = rawDecoded[3];
        int eKeyLen = rawDecoded[4];
        ushort cKeyPageSizeKb = BinaryPrimitives.ReadUInt16BigEndian(rawDecoded.AsSpan(5, 2));
        uint cKeyPageCount = BinaryPrimitives.ReadUInt32BigEndian(rawDecoded.AsSpan(9, 4));
        int eSpecSize = (int)BinaryPrimitives.ReadUInt32BigEndian(rawDecoded.AsSpan(18, 4));

        if (cKeyLen != 16 || eKeyLen != 16)
        {
            throw new NotSupportedException($"Nicht unterstützte Schlüssellängen in 'EN': CKey={cKeyLen}, EKey={eKeyLen}");
        }

        _encodingData = rawDecoded;
        _cKeyPageSize = cKeyPageSizeKb * 1024;
        _cKeyPageCount = (int)cKeyPageCount;
        _cKeyHeadersOffset = 22 + eSpecSize;
        _cKeyPagesDataOffset = _cKeyHeadersOffset + (_cKeyPageCount * 32);
    }

    public bool TryGetEKeysForCKey(ReadOnlySpan<byte> cKey16, out List<byte[]> eKeys)
    {
        eKeys = [];
        if (cKey16.Length < 16 || _cKeyPageCount <= 0)
        {
            return false;
        }

        ReadOnlySpan<byte> target = cKey16.Slice(0, 16);

        // Binary search CKey page headers (each header = 16B FirstCKey + 16B PageMD5)
        int lo = 0;
        int hi = _cKeyPageCount - 1;
        int candidatePage = -1;

        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            var firstKey = _encodingData.AsSpan(_cKeyHeadersOffset + mid * 32, 16);
            int cmp = firstKey.SequenceCompareTo(target);
            if (cmp <= 0)
            {
                candidatePage = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (candidatePage < 0)
        {
            return false;
        }

        return ScanCKeyPage(candidatePage, target, eKeys) ||
               (candidatePage > 0 && ScanCKeyPage(candidatePage - 1, target, eKeys));
    }

    private bool ScanCKeyPage(int pageIndex, ReadOnlySpan<byte> targetCKey, List<byte[]> eKeys)
    {
        int pageStart = _cKeyPagesDataOffset + pageIndex * _cKeyPageSize;
        int pageEnd = Math.Min(_encodingData.Length, pageStart + _cKeyPageSize);
        int pos = pageStart;

        while (pos + 22 <= pageEnd)
        {
            byte keyCount = _encodingData[pos];
            if (keyCount == 0)
            {
                break; // End-of-page zero padding
            }

            int recordLen = 1 + 5 + 16 + (keyCount * 16);
            if (pos + recordLen > pageEnd)
            {
                break;
            }

            var entryCKey = _encodingData.AsSpan(pos + 6, 16);
            if (entryCKey.SequenceEqual(targetCKey))
            {
                for (int k = 0; k < keyCount; k++)
                {
                    eKeys.Add(_encodingData.AsSpan(pos + 22 + k * 16, 16).ToArray());
                }
                return true;
            }

            pos += recordLen;
        }

        return false;
    }

    public bool HasEKeyLocally(ReadOnlySpan<byte> eKey) =>
        eKey.Length >= 9 && _indexMap.ContainsKey(EKeyPrefix9.FromSpan(eKey));

    public byte[]? ReadFileByCKey(ReadOnlySpan<byte> cKey16)
    {
        if (!TryGetEKeysForCKey(cKey16, out var eKeys))
        {
            return null;
        }

        foreach (byte[] eKey in eKeys)
        {
            try
            {
                byte[]? decoded = ReadFileByEKey(eKey);
                if (decoded != null)
                {
                    return decoded;
                }
            }
            catch (EncryptedBlteException)
            {
                // Try alternative EKey if available
            }
        }

        return null;
    }

    public byte[]? ReadFileByEKey(ReadOnlySpan<byte> eKey)
    {
        if (eKey.Length < 9)
        {
            return null;
        }

        var prefix = EKeyPrefix9.FromSpan(eKey);
        if (!_indexMap.TryGetValue(prefix, out var entry))
        {
            return null;
        }

        if (entry.EncodedSize <= 8)
        {
            return null;
        }

        byte[] rawEntry = new byte[entry.EncodedSize];
        int totalRead = 0;
        lock (_streamLock)
        {
            var fs = GetOrOpenArchiveStream(entry.ArchiveIndex);
            fs.Position = entry.ArchiveOffset;
            while (totalRead < entry.EncodedSize)
            {
                int r = fs.Read(rawEntry, totalRead, entry.EncodedSize - totalRead);
                if (r <= 0) break;
                totalRead += r;
            }
        }

        if (totalRead < 8)
        {
            return null;
        }

        // Standard CASC data.xxx entries begin with a 30-byte header followed by 'BLTE'
        if (totalRead >= 38 &&
            rawEntry[30] == (byte)'B' && rawEntry[31] == (byte)'L' &&
            rawEntry[32] == (byte)'T' && rawEntry[33] == (byte)'E')
        {
            return BlteDecoder.Decode(rawEntry.AsSpan(30, totalRead - 30));
        }

        // Direct BLTE frame without 30-byte wrapper
        if (rawEntry[0] == (byte)'B' && rawEntry[1] == (byte)'L' &&
            rawEntry[2] == (byte)'T' && rawEntry[3] == (byte)'E')
        {
            return BlteDecoder.Decode(rawEntry.AsSpan(0, totalRead));
        }

        throw new InvalidDataException(
            $"Kein BLTE-Header in data.{entry.ArchiveIndex:D3} @ 0x{entry.ArchiveOffset:X} (Size={entry.EncodedSize}, Hex={Convert.ToHexString(rawEntry.AsSpan(0, Math.Min(48, totalRead)))}).");
    }

    public byte[] LoadRootManifestBytes()
    {
        if (Build.RootEKey != null)
        {
            byte[]? byEKey = ReadFileByEKey(Build.RootEKey);
            if (byEKey != null) return byEKey;
        }

        return ReadFileByCKey(Build.RootCKey)
               ?? throw new InvalidDataException("CASC 'root'-Manifest konnte weder über EKey noch CKey geladen werden.");
    }

    private FileStream GetOrOpenArchiveStream(int archiveIndex)
    {
        if (_archiveStreams.TryGetValue(archiveIndex, out var existing))
        {
            return existing;
        }

        string archivePath = Path.Combine(DataFilesDir, $"data.{archiveIndex:D3}");
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"CASC Archiv-Segment fehlt: {archivePath}");
        }

        var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 65536);
        _archiveStreams[archiveIndex] = fs;
        return fs;
    }

    private static byte[] ReadAllBytesShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] buf = new byte[fs.Length];
        int read = 0;
        while (read < buf.Length)
        {
            int r = fs.Read(buf, read, buf.Length - read);
            if (r <= 0) break;
            read += r;
        }
        return buf;
    }

    private static string[] ReadAllLinesShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        var list = new List<string>();
        while (sr.ReadLine() is { } line)
        {
            list.Add(line);
        }
        return list.ToArray();
    }

    public void Dispose()
    {
        lock (_streamLock)
        {
            foreach (var fs in _archiveStreams.Values)
            {
                fs.Dispose();
            }
            _archiveStreams.Clear();
        }
    }
}
