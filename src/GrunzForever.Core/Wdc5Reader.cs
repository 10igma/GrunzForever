using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace GrunzForever.Core;

public enum RowSourceKind
{
    Db2,
    Db2PlusHotfix,
    HotfixOnly
}

public sealed class ExtractedRow
{
    public required int Id { get; init; }
    public required object?[] Values { get; set; }
    public RowSourceKind Source { get; set; } = RowSourceKind.Db2;
    public int? HotfixPushId { get; set; }
}

public sealed class ExtractedTableData
{
    public required string TableName { get; init; }
    public required uint TableHash { get; init; }
    public required uint LayoutHash { get; init; }
    public required IReadOnlyList<DbdColumnLayout> Columns { get; init; }
    public Dictionary<int, ExtractedRow> RowsById { get; } = [];

    public int BaseDb2RowCount { get; set; }
    public int HotfixUpsertCount { get; set; }
    public int HotfixDeleteCount { get; set; }
}

/// <summary>
/// Bit-accurate reader for Blizzard WDC5 (and WDC3/WDC4) .db2 client tables,
/// supporting multi-section files, sparse offset_map records, bitpacking, pallet/common data,
/// virtual string pools, copy tables, and relationship foreign-key maps.
/// </summary>
public static class Wdc5Reader
{
    private readonly record struct SectionHeader(
        ulong TactKeyHash,
        int FileOffset,
        int RecordCount,
        int StringTableSize,
        int OffsetRecordsEnd,
        int IdListSize,
        int RelationshipDataSize,
        int OffsetMapIdCount,
        int CopyTableCount);

    private readonly record struct FieldStorageInfo(
        ushort FieldOffsetBits,
        ushort FieldSizeBits,
        uint AdditionalDataSize,
        uint StorageType,
        uint Val1,
        uint Val2,
        uint Val3);

    public static uint PeekLayoutHash(ReadOnlySpan<byte> db2Bytes)
    {
        if (db2Bytes.Length < 72) return 0;
        string magic = Encoding.ASCII.GetString(db2Bytes.Slice(0, 4));
        int metaOffset = magic == "WDC5" ? 136 : 4;
        if (db2Bytes.Length < metaOffset + 24) return 0;
        return BinaryPrimitives.ReadUInt32LittleEndian(db2Bytes.Slice(metaOffset + 20, 4));
    }

    public static uint PeekTableHash(ReadOnlySpan<byte> db2Bytes)
    {
        if (db2Bytes.Length < 72) return 0;
        string magic = Encoding.ASCII.GetString(db2Bytes.Slice(0, 4));
        int metaOffset = magic == "WDC5" ? 136 : 4;
        if (db2Bytes.Length < metaOffset + 20) return 0;
        return BinaryPrimitives.ReadUInt32LittleEndian(db2Bytes.Slice(metaOffset + 16, 4));
    }

    public static ExtractedTableData Read(
        ReadOnlySpan<byte> db2Bytes,
        string tableName,
        DbdTableSchema? dbdSchema)
    {
        if (db2Bytes.Length < 72)
        {
            throw new InvalidDataException($"{tableName}: DB2-Datei zu klein ({db2Bytes.Length} B).");
        }

        string magic = Encoding.ASCII.GetString(db2Bytes.Slice(0, 4));
        if (magic is not ("WDC5" or "WDC4" or "WDC3"))
        {
            throw new NotSupportedException($"{tableName}: Nicht unterstützter DB2-Header '{magic}'.");
        }

        int metaPos = magic == "WDC5" ? 136 : 4;
        int headerSize = metaPos + 68;

        int totalRecordCount = BinaryPrimitives.ReadInt32LittleEndian(db2Bytes.Slice(metaPos + 0, 4));
        int fieldCount = BinaryPrimitives.ReadInt32LittleEndian(db2Bytes.Slice(metaPos + 4, 4));
        int recordSize = BinaryPrimitives.ReadInt32LittleEndian(db2Bytes.Slice(metaPos + 8, 4));
        int totalStringSize = BinaryPrimitives.ReadInt32LittleEndian(db2Bytes.Slice(metaPos + 12, 4));
        uint tableHash = BinaryPrimitives.ReadUInt32LittleEndian(db2Bytes.Slice(metaPos + 16, 4));
        uint layoutHash = BinaryPrimitives.ReadUInt32LittleEndian(db2Bytes.Slice(metaPos + 20, 4));
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(db2Bytes.Slice(metaPos + 36, 2));
        ushort idIndex = BinaryPrimitives.ReadUInt16LittleEndian(db2Bytes.Slice(metaPos + 38, 2));
        int totalFieldCount = BinaryPrimitives.ReadInt32LittleEndian(db2Bytes.Slice(metaPos + 40, 4));
        int fsiSize = BinaryPrimitives.ReadInt32LittleEndian(db2Bytes.Slice(metaPos + 52, 4));
        int commonDataSize = BinaryPrimitives.ReadInt32LittleEndian(db2Bytes.Slice(metaPos + 56, 4));
        int palletDataSize = BinaryPrimitives.ReadInt32LittleEndian(db2Bytes.Slice(metaPos + 60, 4));
        int sectionCount = BinaryPrimitives.ReadInt32LittleEndian(db2Bytes.Slice(metaPos + 64, 4));

        bool hasOffsetMap = (flags & 0x01) != 0;
        bool hasNonInlineId = (flags & 0x04) != 0;

        int cursor = headerSize;

        // 1. Section headers (40 bytes each)
        var sections = new SectionHeader[sectionCount];
        for (int s = 0; s < sectionCount; s++)
        {
            var sh = db2Bytes.Slice(cursor, 40);
            sections[s] = new SectionHeader(
                TactKeyHash: BinaryPrimitives.ReadUInt64LittleEndian(sh.Slice(0, 8)),
                FileOffset: BinaryPrimitives.ReadInt32LittleEndian(sh.Slice(8, 4)),
                RecordCount: BinaryPrimitives.ReadInt32LittleEndian(sh.Slice(12, 4)),
                StringTableSize: BinaryPrimitives.ReadInt32LittleEndian(sh.Slice(16, 4)),
                OffsetRecordsEnd: BinaryPrimitives.ReadInt32LittleEndian(sh.Slice(20, 4)),
                IdListSize: BinaryPrimitives.ReadInt32LittleEndian(sh.Slice(24, 4)),
                RelationshipDataSize: BinaryPrimitives.ReadInt32LittleEndian(sh.Slice(28, 4)),
                OffsetMapIdCount: BinaryPrimitives.ReadInt32LittleEndian(sh.Slice(32, 4)),
                CopyTableCount: BinaryPrimitives.ReadInt32LittleEndian(sh.Slice(36, 4)));
            cursor += 40;
        }

        // 2. Skip field_structure (totalFieldCount * 4 bytes)
        cursor += totalFieldCount * 4;

        // 3. Read field_storage_info (24 bytes each)
        int fsiCount = fsiSize / 24;
        var fsi = new FieldStorageInfo[fsiCount];
        for (int i = 0; i < fsiCount; i++)
        {
            var fsSpan = db2Bytes.Slice(cursor, 24);
            fsi[i] = new FieldStorageInfo(
                FieldOffsetBits: BinaryPrimitives.ReadUInt16LittleEndian(fsSpan.Slice(0, 2)),
                FieldSizeBits: BinaryPrimitives.ReadUInt16LittleEndian(fsSpan.Slice(2, 2)),
                AdditionalDataSize: BinaryPrimitives.ReadUInt32LittleEndian(fsSpan.Slice(4, 4)),
                StorageType: BinaryPrimitives.ReadUInt32LittleEndian(fsSpan.Slice(8, 4)),
                Val1: BinaryPrimitives.ReadUInt32LittleEndian(fsSpan.Slice(12, 4)),
                Val2: BinaryPrimitives.ReadUInt32LittleEndian(fsSpan.Slice(16, 4)),
                Val3: BinaryPrimitives.ReadUInt32LittleEndian(fsSpan.Slice(20, 4)));
            cursor += 24;
        }

        // 4. Slice pallet_data & common_data per column
        ReadOnlySpan<byte> palletPool = db2Bytes.Slice(cursor, palletDataSize);
        cursor += palletDataSize;
        ReadOnlySpan<byte> commonPool = db2Bytes.Slice(cursor, commonDataSize);
        cursor += commonDataSize;

        var palletOffsets = new (int Offset, int Length)[fsiCount];
        var commonMaps = new Dictionary<int, uint>?[fsiCount];
        int pCur = 0;
        int cCur = 0;

        for (int i = 0; i < fsiCount; i++)
        {
            int addSz = (int)fsi[i].AdditionalDataSize;
            if (fsi[i].StorageType is 3 or 4 && addSz > 0)
            {
                palletOffsets[i] = (pCur, addSz);
                pCur += addSz;
            }
            else if (fsi[i].StorageType == 2)
            {
                var map = new Dictionary<int, uint>(addSz / 8);
                if (addSz > 0)
                {
                    var cSlice = commonPool.Slice(cCur, addSz);
                    for (int e = 0; e + 8 <= cSlice.Length; e += 8)
                    {
                        int rId = BinaryPrimitives.ReadInt32LittleEndian(cSlice.Slice(e, 4));
                        uint val = BinaryPrimitives.ReadUInt32LittleEndian(cSlice.Slice(e + 4, 4));
                        map[rId] = val;
                    }
                    cCur += addSz;
                }
                commonMaps[i] = map;
            }
        }

        // 5. Resolve final column list (matching DBD layout or synthesizing fallback from FSI)
        List<DbdColumnLayout> columns = ResolveEffectiveColumns(dbdSchema, fsi, hasOffsetMap, hasNonInlineId, idIndex, sections.Any(s => s.RelationshipDataSize > 0));

        // Map each column in `columns` to its FSI index (-1 for non-inline ID / non-inline relation)
        int[] colToFsi = new int[columns.Count];
        int nextFsi = 0;
        int idColOutputIdx = -1;
        int nonInlineRelOutputIdx = -1;

        for (int c = 0; c < columns.Count; c++)
        {
            var col = columns[c];
            if (col.IsId)
            {
                idColOutputIdx = c;
            }

            if (col.IsNonInline)
            {
                colToFsi[c] = -1;
                if (col.IsRelation)
                {
                    nonInlineRelOutputIdx = c;
                }
            }
            else
            {
                colToFsi[c] = nextFsi < fsiCount ? nextFsi++ : -1;
            }
        }

        // 6. Build virtual string pool for normal (non-sparse) tables
        byte[] virtualStringPool = [];
        long totalVirtualRecordBytes = 0;
        if (!hasOffsetMap)
        {
            totalVirtualRecordBytes = sections.Sum(s => (long)s.RecordCount * recordSize);
            int sumStrBytes = sections.Sum(s => s.StringTableSize);
            if (sumStrBytes > 0)
            {
                virtualStringPool = new byte[sumStrBytes];
                int dst = 0;
                foreach (var sec in sections)
                {
                    if (sec.StringTableSize > 0)
                    {
                        int strFileStart = sec.FileOffset + sec.RecordCount * recordSize;
                        if (strFileStart >= 0 && strFileStart + sec.StringTableSize <= db2Bytes.Length)
                        {
                            db2Bytes.Slice(strFileStart, sec.StringTableSize).CopyTo(virtualStringPool.AsSpan(dst));
                        }
                        dst += sec.StringTableSize;
                    }
                }
            }
        }

        var tableData = new ExtractedTableData
        {
            TableName = tableName,
            TableHash = tableHash,
            LayoutHash = layoutHash,
            Columns = columns
        };

        long sectionVirtualRecordBase = 0;

        // 7. Parse each section
        foreach (var sec in sections)
        {
            if (sec.RecordCount == 0 && sec.CopyTableCount == 0 && sec.OffsetMapIdCount == 0)
            {
                continue;
            }

            // Skip locked TACT sections that were zero-filled by BLTE
            if (sec.TactKeyHash != 0 && IsLockedZeroSection(db2Bytes, sec, recordSize, hasOffsetMap))
            {
                sectionVirtualRecordBase += (long)sec.RecordCount * recordSize;
                continue;
            }

            if (hasOffsetMap)
            {
                ParseSparseSection(db2Bytes, sec, columns, idColOutputIdx, nonInlineRelOutputIdx, tableData);
            }
            else
            {
                ParseNormalSection(
                    db2Bytes,
                    sec,
                    recordSize,
                    hasNonInlineId,
                    columns,
                    colToFsi,
                    idColOutputIdx,
                    nonInlineRelOutputIdx,
                    fsi,
                    palletPool,
                    palletOffsets,
                    commonMaps,
                    virtualStringPool,
                    sectionVirtualRecordBase,
                    totalVirtualRecordBytes,
                    tableData);
            }

            sectionVirtualRecordBase += (long)sec.RecordCount * recordSize;
        }

        tableData.BaseDb2RowCount = tableData.RowsById.Count;
        return tableData;
    }

    private static void ParseNormalSection(
        ReadOnlySpan<byte> db2Bytes,
        SectionHeader sec,
        int recordSize,
        bool hasNonInlineId,
        List<DbdColumnLayout> columns,
        int[] colToFsi,
        int idColOutputIdx,
        int nonInlineRelOutputIdx,
        FieldStorageInfo[] fsi,
        ReadOnlySpan<byte> palletPool,
        (int Offset, int Length)[] palletOffsets,
        Dictionary<int, uint>?[] commonMaps,
        byte[] virtualStringPool,
        long sectionVirtualRecordBase,
        long totalVirtualRecordBytes,
        ExtractedTableData tableData)
    {
        int recordsStart = sec.FileOffset;
        int recordsBytes = sec.RecordCount * recordSize;
        int stringStart = recordsStart + recordsBytes;
        int idListStart = stringStart + sec.StringTableSize;
        int copyTableStart = idListStart + sec.IdListSize;
        int relDataStart = copyTableStart + sec.CopyTableCount * 8;

        ReadOnlySpan<byte> idListSpan = sec.IdListSize > 0
            ? db2Bytes.Slice(idListStart, sec.IdListSize)
            : ReadOnlySpan<byte>.Empty;

        Dictionary<int, uint>? relByRowIdx = sec.RelationshipDataSize >= 12
            ? ParseRelationshipMap(db2Bytes.Slice(relDataStart, sec.RelationshipDataSize))
            : null;

        for (int r = 0; r < sec.RecordCount; r++)
        {
            ReadOnlySpan<byte> rowSpan = db2Bytes.Slice(recordsStart + r * recordSize, recordSize);
            int recordId = hasNonInlineId && (r * 4 + 4 <= idListSpan.Length)
                ? BinaryPrimitives.ReadInt32LittleEndian(idListSpan.Slice(r * 4, 4))
                : r + 1;

            var values = new object?[columns.Count];

            for (int c = 0; c < columns.Count; c++)
            {
                var col = columns[c];
                if (col.IsNonInline)
                {
                    if (col.IsId)
                    {
                        values[c] = recordId;
                    }
                    else if (col.IsRelation)
                    {
                        uint fk = relByRowIdx != null && relByRowIdx.TryGetValue(r, out uint foundFk) ? foundFk : 0u;
                        values[c] = col.IsUnsigned ? fk : (int)fk;
                    }
                    continue;
                }

                int fIdx = colToFsi[c];
                if (fIdx < 0 || fIdx >= fsi.Length)
                {
                    continue;
                }

                long rowVirtBase = sectionVirtualRecordBase + (long)r * recordSize;
                values[c] = ReadColumnFromNormalRow(
                    rowSpan,
                    recordId,
                    col,
                    fsi[fIdx],
                    palletPool,
                    palletOffsets[fIdx],
                    commonMaps[fIdx],
                    virtualStringPool,
                    rowVirtBase,
                    totalVirtualRecordBytes);

                if (col.IsId && !hasNonInlineId && values[c] != null)
                {
                    recordId = Convert.ToInt32(values[c]);
                }
            }

            if (idColOutputIdx >= 0 && values[idColOutputIdx] == null)
            {
                values[idColOutputIdx] = recordId;
            }

            tableData.RowsById[recordId] = new ExtractedRow
            {
                Id = recordId,
                Values = values,
                Source = RowSourceKind.Db2
            };
        }

        // Apply copy_table (clones existing rows under new IDs)
        if (sec.CopyTableCount > 0)
        {
            ReadOnlySpan<byte> copySpan = db2Bytes.Slice(copyTableStart, sec.CopyTableCount * 8);
            ApplyCopyTable(copySpan, idColOutputIdx, tableData);
        }
    }

    private static void ParseSparseSection(
        ReadOnlySpan<byte> db2Bytes,
        SectionHeader sec,
        List<DbdColumnLayout> columns,
        int idColOutputIdx,
        int nonInlineRelOutputIdx,
        ExtractedTableData tableData)
    {
        int idListPos = sec.OffsetRecordsEnd;
        int copyPos = idListPos + sec.IdListSize;
        int offsetMapPos = copyPos + sec.CopyTableCount * 8;
        int relPos = offsetMapPos + sec.OffsetMapIdCount * 6;
        int offsetMapIdListPos = relPos + sec.RelationshipDataSize;

        int entryCount = sec.OffsetMapIdCount > 0 ? sec.OffsetMapIdCount : sec.RecordCount;
        ReadOnlySpan<byte> idSource = sec.IdListSize >= entryCount * 4
            ? db2Bytes.Slice(idListPos, entryCount * 4)
            : db2Bytes.Slice(offsetMapIdListPos, entryCount * 4);

        ReadOnlySpan<byte> offsetMapSpan = db2Bytes.Slice(offsetMapPos, entryCount * 6);

        Dictionary<int, uint>? relByRowIdx = sec.RelationshipDataSize >= 12
            ? ParseRelationshipMap(db2Bytes.Slice(relPos, sec.RelationshipDataSize))
            : null;

        for (int r = 0; r < entryCount; r++)
        {
            int recordId = BinaryPrimitives.ReadInt32LittleEndian(idSource.Slice(r * 4, 4));
            int recOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(offsetMapSpan.Slice(r * 6, 4));
            int recLen = BinaryPrimitives.ReadUInt16LittleEndian(offsetMapSpan.Slice(r * 6 + 4, 2));

            if (recLen <= 0 || recOffset < 0 || recOffset + recLen > db2Bytes.Length)
            {
                continue;
            }

            uint? relValue = relByRowIdx != null && relByRowIdx.TryGetValue(r, out uint fk) ? fk : null;
            object?[] values = UnpackSequentialRecord(
                db2Bytes.Slice(recOffset, recLen),
                recordId,
                columns,
                relValue);

            tableData.RowsById[recordId] = new ExtractedRow
            {
                Id = recordId,
                Values = values,
                Source = RowSourceKind.Db2
            };
        }

        if (sec.CopyTableCount > 0)
        {
            ReadOnlySpan<byte> copySpan = db2Bytes.Slice(copyPos, sec.CopyTableCount * 8);
            ApplyCopyTable(copySpan, idColOutputIdx, tableData);
        }
    }

    /// <summary>
    /// Unpacks a sequential variable-length record (used by both WDC5 HasOffsetMap tables like ItemSparse
    /// AND XFTH v9 DBCache.bin server hotfix records!).
    /// </summary>
    public static object?[] UnpackSequentialRecord(
        ReadOnlySpan<byte> payload,
        int recordId,
        IReadOnlyList<DbdColumnLayout> columns,
        uint? knownRelationValue = null,
        object?[]? existingBaseRow = null)
    {
        var values = new object?[columns.Count];
        int pos = 0;

        for (int c = 0; c < columns.Count; c++)
        {
            var col = columns[c];

            if (col.IsNonInline)
            {
                if (col.IsId)
                {
                    values[c] = recordId;
                }
                else if (col.IsRelation)
                {
                    if (knownRelationValue.HasValue)
                    {
                        values[c] = col.IsUnsigned ? knownRelationValue.Value : (int)knownRelationValue.Value;
                    }
                    else if (pos + 4 <= payload.Length)
                    {
                        uint trailingRel = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(pos, 4));
                        pos += 4;
                        values[c] = col.IsUnsigned ? trailingRel : (int)trailingRel;
                    }
                    else if (existingBaseRow != null && c < existingBaseRow.Length)
                    {
                        values[c] = existingBaseRow[c];
                    }
                    else
                    {
                        values[c] = 0;
                    }
                }
                continue;
            }

            if (col.ArrayLength <= 1)
            {
                values[c] = ReadSequentialScalar(payload, ref pos, col);
                if (col.IsId && values[c] != null)
                {
                    // Inline ID inside hotfix/sparse payload
                }
            }
            else
            {
                var arr = new object?[col.ArrayLength];
                for (int a = 0; a < col.ArrayLength; a++)
                {
                    arr[a] = ReadSequentialScalar(payload, ref pos, col);
                }
                values[c] = arr;
            }
        }

        return values;
    }

    private static object? ReadSequentialScalar(ReadOnlySpan<byte> buf, ref int pos, DbdColumnLayout col)
    {
        if (col.BaseKind == DbdBaseKind.String)
        {
            if (pos >= buf.Length) return string.Empty;
            int start = pos;
            while (pos < buf.Length && buf[pos] != 0)
            {
                pos++;
            }
            string str = Encoding.UTF8.GetString(buf.Slice(start, pos - start));
            if (pos < buf.Length && buf[pos] == 0)
            {
                pos++; // consume '\0'
            }
            return str;
        }

        if (col.BaseKind == DbdBaseKind.Float)
        {
            if (pos + 4 > buf.Length) return 0f;
            float f = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(pos, 4)));
            pos += 4;
            return f;
        }

        // Integer
        switch (col.BitWidth)
        {
            case 8:
                if (pos + 1 > buf.Length) return 0;
                byte b = buf[pos++];
                return col.IsUnsigned ? b : (sbyte)b;

            case 16:
                if (pos + 2 > buf.Length) return 0;
                ushort u16 = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(pos, 2));
                pos += 2;
                return col.IsUnsigned ? u16 : (short)u16;

            case 64:
                if (pos + 8 > buf.Length) return 0L;
                ulong u64 = BinaryPrimitives.ReadUInt64LittleEndian(buf.Slice(pos, 8));
                pos += 8;
                return col.IsUnsigned ? u64 : (long)u64;

            default: // 32
                if (pos + 4 > buf.Length) return 0;
                uint u32 = BinaryPrimitives.ReadUInt32LittleEndian(buf.Slice(pos, 4));
                pos += 4;
                return col.IsUnsigned ? u32 : (int)u32;
        }
    }

    private static object? ReadColumnFromNormalRow(
        ReadOnlySpan<byte> rowSpan,
        int recordId,
        DbdColumnLayout col,
        FieldStorageInfo fsi,
        ReadOnlySpan<byte> palletPool,
        (int Offset, int Length) palletSliceInfo,
        Dictionary<int, uint>? commonMap,
        byte[] virtualStringPool,
        long rowVirtBase,
        long totalVirtualRecordBytes)
    {
        switch (fsi.StorageType)
        {
            case 0: // None (inline fixed bit-width scalar or array)
            {
                if (col.ArrayLength <= 1)
                {
                    return DecodeInlineElement(rowSpan, fsi.FieldOffsetBits, fsi.FieldSizeBits, col, virtualStringPool, rowVirtBase, totalVirtualRecordBytes);
                }

                int elemBits = col.ArrayLength > 0 && fsi.FieldSizeBits >= col.ArrayLength
                    ? fsi.FieldSizeBits / col.ArrayLength
                    : col.BitWidth;

                var arr = new object?[col.ArrayLength];
                for (int a = 0; a < col.ArrayLength; a++)
                {
                    arr[a] = DecodeInlineElement(
                        rowSpan,
                        fsi.FieldOffsetBits + a * elemBits,
                        elemBits,
                        col,
                        virtualStringPool,
                        rowVirtBase,
                        totalVirtualRecordBytes);
                }
                return arr;
            }

            case 1: // Bitpacked unsigned
            case 5: // Bitpacked signed
            {
                int bits = fsi.Val2 > 0 ? (int)fsi.Val2 : fsi.FieldSizeBits;
                bool signed = fsi.StorageType == 5 || !col.IsUnsigned;
                if (fsi.StorageType == 1 && col.IsUnsigned) signed = false;
                if (fsi.StorageType == 1 && !col.IsUnsigned) signed = false; // StorageType 1 is unsigned bitpack unless negative values use type 5

                ulong raw = ReadBitsUInt64(rowSpan, fsi.FieldOffsetBits, bits);
                if (col.BaseKind == DbdBaseKind.Float)
                {
                    return BitConverter.UInt32BitsToSingle((uint)raw);
                }

                if (fsi.StorageType == 5)
                {
                    return SignExtend(raw, bits);
                }

                return col.BitWidth <= 32 && !col.IsUnsigned ? (long)(uint)raw : raw;
            }

            case 2: // CommonData
            {
                uint raw32 = commonMap != null && commonMap.TryGetValue(recordId, out uint mapped)
                    ? mapped
                    : fsi.Val1;
                return Interpret32BitSlot(raw32, col);
            }

            case 3: // BitpackedIndexed (Pallet scalar)
            {
                int idxBits = fsi.Val2 > 0 ? (int)fsi.Val2 : fsi.FieldSizeBits;
                int idx = (int)ReadBitsUInt64(rowSpan, fsi.FieldOffsetBits, idxBits);
                int byteOff = palletSliceInfo.Offset + idx * 4;
                if (byteOff < 0 || byteOff + 4 > palletPool.Length)
                {
                    return Interpret32BitSlot(0, col);
                }
                uint raw32 = BinaryPrimitives.ReadUInt32LittleEndian(palletPool.Slice(byteOff, 4));
                return Interpret32BitSlot(raw32, col);
            }

            case 4: // BitpackedIndexedArray (Pallet array)
            {
                int idxBits = fsi.Val2 > 0 ? (int)fsi.Val2 : fsi.FieldSizeBits;
                int arrCount = fsi.Val3 > 0 ? (int)fsi.Val3 : Math.Max(1, col.ArrayLength);
                int idx = (int)ReadBitsUInt64(rowSpan, fsi.FieldOffsetBits, idxBits);
                int baseByteOff = palletSliceInfo.Offset + idx * arrCount * 4;

                var arr = new object?[arrCount];
                for (int a = 0; a < arrCount; a++)
                {
                    int off = baseByteOff + a * 4;
                    uint raw32 = (off >= 0 && off + 4 <= palletPool.Length)
                        ? BinaryPrimitives.ReadUInt32LittleEndian(palletPool.Slice(off, 4))
                        : 0u;
                    arr[a] = Interpret32BitSlot(raw32, col);
                }
                return arrCount == 1 && col.ArrayLength <= 1 ? arr[0] : arr;
            }

            default:
                return null;
        }
    }

    private static object? DecodeInlineElement(
        ReadOnlySpan<byte> rowSpan,
        int bitOffset,
        int bitSize,
        DbdColumnLayout col,
        byte[] virtualStringPool,
        long rowVirtBase,
        long totalVirtualRecordBytes)
    {
        if (col.BaseKind == DbdBaseKind.String)
        {
            uint rawStrOff = (uint)ReadBitsUInt64(rowSpan, bitOffset, 32);
            if (rawStrOff == 0 || virtualStringPool.Length == 0)
            {
                return string.Empty;
            }

            long fieldVirtOffset = rowVirtBase + (bitOffset >> 3);
            long poolIndex = (fieldVirtOffset + rawStrOff) - totalVirtualRecordBytes;
            if (poolIndex < 0 || poolIndex >= virtualStringPool.Length)
            {
                return string.Empty;
            }

            int start = (int)poolIndex;
            int end = start;
            while (end < virtualStringPool.Length && virtualStringPool[end] != 0)
            {
                end++;
            }
            return Encoding.UTF8.GetString(virtualStringPool, start, end - start);
        }

        if (col.BaseKind == DbdBaseKind.Float)
        {
            uint bits = (uint)ReadBitsUInt64(rowSpan, bitOffset, 32);
            return BitConverter.UInt32BitsToSingle(bits);
        }

        ulong raw = ReadBitsUInt64(rowSpan, bitOffset, bitSize);
        if (col.IsUnsigned)
        {
            return raw;
        }

        return SignExtend(raw, bitSize);
    }

    private static object Interpret32BitSlot(uint raw32, DbdColumnLayout col)
    {
        if (col.BaseKind == DbdBaseKind.Float)
        {
            return BitConverter.UInt32BitsToSingle(raw32);
        }

        if (col.IsUnsigned)
        {
            return raw32;
        }

        return col.BitWidth switch
        {
            8 => (sbyte)(byte)raw32,
            16 => (short)(ushort)raw32,
            _ => (int)raw32
        };
    }

    private static long SignExtend(ulong raw, int bits)
    {
        if (bits <= 0 || bits >= 64)
        {
            return (long)raw;
        }

        int shift = 64 - bits;
        return (long)(raw << shift) >> shift;
    }

    private static ulong ReadBitsUInt64(ReadOnlySpan<byte> buf, int bitOffset, int bitCount)
    {
        if (bitCount <= 0) return 0UL;
        int bytePos = bitOffset >> 3;
        int bitShift = bitOffset & 7;
        if (bytePos >= buf.Length) return 0UL;

        ulong lo = 0;
        int maxBytes = Math.Min(8, buf.Length - bytePos);
        for (int b = 0; b < maxBytes; b++)
        {
            lo |= (ulong)buf[bytePos + b] << (b * 8);
        }

        ulong result = lo >> bitShift;
        if (bitShift > 0 && bytePos + 8 < buf.Length && bitShift + bitCount > 64)
        {
            result |= (ulong)buf[bytePos + 8] << (64 - bitShift);
        }

        if (bitCount < 64)
        {
            result &= (1UL << bitCount) - 1UL;
        }

        return result;
    }

    private static Dictionary<int, uint> ParseRelationshipMap(ReadOnlySpan<byte> relSpan)
    {
        int count = BinaryPrimitives.ReadInt32LittleEndian(relSpan.Slice(0, 4));
        var map = new Dictionary<int, uint>(Math.Max(0, count));
        int pos = 12;
        for (int i = 0; i < count && pos + 8 <= relSpan.Length; i++)
        {
            uint foreignId = BinaryPrimitives.ReadUInt32LittleEndian(relSpan.Slice(pos, 4));
            int recordIndex = BinaryPrimitives.ReadInt32LittleEndian(relSpan.Slice(pos + 4, 4));
            map[recordIndex] = foreignId;
            pos += 8;
        }
        return map;
    }

    private static void ApplyCopyTable(
        ReadOnlySpan<byte> copySpan,
        int idColOutputIdx,
        ExtractedTableData tableData)
    {
        for (int pos = 0; pos + 8 <= copySpan.Length; pos += 8)
        {
            int newId = BinaryPrimitives.ReadInt32LittleEndian(copySpan.Slice(pos, 4));
            int srcId = BinaryPrimitives.ReadInt32LittleEndian(copySpan.Slice(pos + 4, 4));
            if (newId == srcId || !tableData.RowsById.TryGetValue(srcId, out var srcRow))
            {
                continue;
            }

            var clonedVals = (object?[])srcRow.Values.Clone();
            if (idColOutputIdx >= 0 && idColOutputIdx < clonedVals.Length)
            {
                clonedVals[idColOutputIdx] = newId;
            }

            tableData.RowsById[newId] = new ExtractedRow
            {
                Id = newId,
                Values = clonedVals,
                Source = RowSourceKind.Db2
            };
        }
    }

    private static bool IsLockedZeroSection(ReadOnlySpan<byte> db2Bytes, SectionHeader sec, int recordSize, bool hasOffsetMap)
    {
        int checkStart = sec.FileOffset;
        int checkLen = hasOffsetMap
            ? Math.Max(0, sec.OffsetRecordsEnd - sec.FileOffset)
            : sec.RecordCount * recordSize + sec.IdListSize;

        if (checkLen <= 0 || checkStart < 0 || checkStart >= db2Bytes.Length)
        {
            return true;
        }

        var slice = db2Bytes.Slice(checkStart, Math.Min(checkLen, db2Bytes.Length - checkStart));
        foreach (byte b in slice)
        {
            if (b != 0) return false;
        }
        return true;
    }

    private static List<DbdColumnLayout> ResolveEffectiveColumns(
        DbdTableSchema? dbd,
        FieldStorageInfo[] fsi,
        bool hasOffsetMap,
        bool hasNonInlineId,
        ushort idIndex,
        bool hasRelationshipTable)
    {
        if (dbd != null)
        {
            int inlineCount = dbd.Columns.Count(c => !c.IsNonInline);
            if (inlineCount == fsi.Length || hasOffsetMap)
            {
                var cols = dbd.Columns.ToList();
                if (!cols.Any(c => c.IsId) && hasNonInlineId)
                {
                    cols.Insert(0, new DbdColumnLayout("ID", DbdBaseKind.Int, 32, false, 1, IsId: true, IsNonInline: true, IsRelation: false, null));
                }
                return cols;
            }
        }

        // Fallback synthetic schema from FSI metadata when DBD layout differs
        var synth = new List<DbdColumnLayout>(fsi.Length + 2);
        if (hasNonInlineId)
        {
            synth.Add(new DbdColumnLayout("ID", DbdBaseKind.Int, 32, false, 1, IsId: true, IsNonInline: true, IsRelation: false, null));
        }

        for (int i = 0; i < fsi.Length; i++)
        {
            bool isInlineId = !hasNonInlineId && i == idIndex;
            int arrLen = fsi[i].StorageType == 4 && fsi[i].Val3 > 1 ? (int)fsi[i].Val3 : 1;
            int width = Math.Clamp((int)fsi[i].FieldSizeBits, 8, 64);
            synth.Add(new DbdColumnLayout(
                Name: isInlineId ? "ID" : $"Field_{i:D2}",
                BaseKind: DbdBaseKind.Int,
                BitWidth: width,
                IsUnsigned: fsi[i].StorageType == 1,
                ArrayLength: arrLen,
                IsId: isInlineId,
                IsNonInline: false,
                IsRelation: false,
                ForeignTable: null));
        }

        if (hasRelationshipTable)
        {
            synth.Add(new DbdColumnLayout("RelationID", DbdBaseKind.Int, 32, false, 1, IsId: false, IsNonInline: true, IsRelation: true, null));
        }

        return synth;
    }
}
