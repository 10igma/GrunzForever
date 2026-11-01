using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace GrunzForever.Core;

public sealed record HotfixEntry(
    int Index,
    int RegionId,
    int PushId,
    uint UniqueId,
    uint TableHash,
    string TableName,
    int RecordId,
    byte Status,
    byte[] Payload)
{
    public string StatusLabel => Status switch
    {
        1 => "Valid",
        2 => "Delete",
        3 => "Invalid",
        4 => "NotPublic",
        _ => $"Status_{Status}"
    };
}

public sealed class HotfixCacheDocument
{
    public required string FilePath { get; init; }
    public required int Version { get; init; }
    public required int Build { get; init; }
    public required IReadOnlyList<HotfixEntry> Entries { get; init; }

    public ILookup<uint, HotfixEntry> EntriesByTableHash => Entries.ToLookup(e => e.TableHash);
}

/// <summary>
/// Parses Blizzard's XFTH (HTFX v9) client hotfix cache (DBCache.bin) and merges
/// live server-pushed items, spells, quests, and NPC records into extracted WDC5 tables.
/// </summary>
public static class HotfixCacheReader
{
    public static HotfixCacheDocument Read(string dbCacheFilePath, DbdSchemaProvider schemaProvider)
    {
        byte[] bytes;
        using (var fs = new FileStream(dbCacheFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            bytes = new byte[fs.Length];
            int read = 0;
            while (read < bytes.Length)
            {
                int r = fs.Read(bytes, read, bytes.Length - read);
                if (r <= 0) break;
                read += r;
            }
        }
        if (bytes.Length < 44)
        {
            throw new InvalidDataException("DBCache.bin ist kleiner als der 44-Byte XFTH-Header.");
        }

        string magic = Encoding.ASCII.GetString(bytes, 0, 4);
        if (magic != "XFTH")
        {
            throw new InvalidDataException($"Ungültige DBCache.bin Signatur '{magic}' (erwartet 'XFTH').");
        }

        int version = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4));
        int build = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4));

        // XFTH v9 header is 44 bytes (4 magic + 4 version + 4 build + 32 SHA256 hash)
        int pos = version >= 8 ? 44 : 12;
        var entries = new List<HotfixEntry>(25_000);
        int idx = 0;

        while (pos + 32 <= bytes.Length)
        {
            if (bytes[pos] != (byte)'X' || bytes[pos + 1] != (byte)'F' ||
                bytes[pos + 2] != (byte)'T' || bytes[pos + 3] != (byte)'H')
            {
                break;
            }

            int regionId = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(pos + 4, 4));
            int pushId = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(pos + 8, 4));
            uint uniqueId = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 12, 4));
            uint tableHash = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 16, 4));
            int recordId = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(pos + 20, 4));
            int dataSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(pos + 24, 4));
            byte status = bytes[pos + 28];

            pos += 32;
            if (dataSize < 0 || pos + dataSize > bytes.Length)
            {
                break;
            }

            byte[] payload = dataSize > 0 ? bytes.AsSpan(pos, dataSize).ToArray() : [];
            pos += dataSize;

            string tableName = schemaProvider.TryGetByHash(tableHash, out var meta)
                ? meta.TableName
                : $"Unknown_{tableHash:X8}";

            entries.Add(new HotfixEntry(
                Index: idx++,
                RegionId: regionId,
                PushId: pushId,
                UniqueId: uniqueId,
                TableHash: tableHash,
                TableName: tableName,
                RecordId: recordId,
                Status: status,
                Payload: payload));
        }

        return new HotfixCacheDocument
        {
            FilePath = dbCacheFilePath,
            Version = version,
            Build = build,
            Entries = entries
        };
    }

    public static void ApplyHotfixesToTable(ExtractedTableData table, IEnumerable<HotfixEntry> hotfixes)
    {
        foreach (var hf in hotfixes)
        {
            if (hf.Status == 1 && hf.Payload.Length > 0)
            {
                bool existedInDb2 = table.RowsById.TryGetValue(hf.RecordId, out var existing) &&
                                    existing.Source is RowSourceKind.Db2 or RowSourceKind.Db2PlusHotfix;

                object?[] unpacked = Wdc5Reader.UnpackSequentialRecord(
                    hf.Payload,
                    hf.RecordId,
                    table.Columns,
                    knownRelationValue: null,
                    existingBaseRow: existing?.Values);

                table.RowsById[hf.RecordId] = new ExtractedRow
                {
                    Id = hf.RecordId,
                    Values = unpacked,
                    Source = existedInDb2 ? RowSourceKind.Db2PlusHotfix : RowSourceKind.HotfixOnly,
                    HotfixPushId = hf.PushId
                };
                table.HotfixUpsertCount++;
            }
            else if (hf.Status == 2) // Delete
            {
                if (table.RowsById.Remove(hf.RecordId))
                {
                    table.HotfixDeleteCount++;
                }
            }
        }
    }
}
