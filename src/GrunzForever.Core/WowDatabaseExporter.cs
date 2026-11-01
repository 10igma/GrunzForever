using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace GrunzForever.Core;

public sealed class ExportSummary
{
    public required string SqlitePath { get; init; }
    public required string JsonDirectory { get; init; }
    public required int ExportedTableCount { get; init; }
    public required long TotalExportedRows { get; init; }
    public required int TotalHotfixEntries { get; init; }
    public required TimeSpan Elapsed { get; init; }
}

/// <summary>
/// High-throughput SQLite and JSON export engine for extracted WoW CASC DB2 + Hotfix datasets.
/// </summary>
public static class WowDatabaseExporter
{
    private static readonly JsonWriterOptions IndentedJsonOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonWriterOptions CompactJsonOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static ExportSummary ExportAll(
        WowBuildMetadata build,
        IReadOnlyList<ExtractedTableData> tables,
        HotfixCacheDocument? hotfixDoc,
        string exportRootDirectory)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Directory.CreateDirectory(exportRootDirectory);

        string jsonDir = Path.Combine(exportRootDirectory, "json");
        string jsonTablesDir = Path.Combine(jsonDir, "tables");
        Directory.CreateDirectory(jsonDir);
        Directory.CreateDirectory(jsonTablesDir);

        string safeVer = build.Version.Replace(' ', '_');
        string sqlitePath = Path.Combine(exportRootDirectory, $"wow_{build.Product}_{safeVer}.sqlite");
        if (File.Exists(sqlitePath))
        {
            File.Delete(sqlitePath);
        }

        // 1. Export full SQLite relational database + indexes + convenience views
        ExportToSqlite(sqlitePath, build, tables, hotfixDoc);

        // 2. Export individual JSON table files
        var tableByName = tables.ToDictionary(t => t.TableName, StringComparer.OrdinalIgnoreCase);
        long totalRows = 0;

        foreach (var table in tables.OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
        {
            totalRows += table.RowsById.Count;
            string tableJsonPath = Path.Combine(jsonTablesDir, $"{table.TableName}.json");
            WriteTableJson(table, tableJsonPath, indented: table.RowsById.Count <= 25_000);
        }

        // 3. Export rich pre-joined JSON bundles for immediate web/telemetry consumption
        WriteCuratedJsonBundles(build, tables, tableByName, hotfixDoc, jsonDir);

        sw.Stop();
        return new ExportSummary
        {
            SqlitePath = sqlitePath,
            JsonDirectory = jsonDir,
            ExportedTableCount = tables.Count,
            TotalExportedRows = totalRows,
            TotalHotfixEntries = hotfixDoc?.Entries.Count ?? 0,
            Elapsed = sw.Elapsed
        };
    }

    private static void ExportToSqlite(
        string sqlitePath,
        WowBuildMetadata build,
        IReadOnlyList<ExtractedTableData> tables,
        HotfixCacheDocument? hotfixDoc)
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = sqlitePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        };

        using var conn = new SqliteConnection(csb.ToString());
        conn.Open();

        ExecuteNonQuery(conn, "PRAGMA journal_mode = OFF;");
        ExecuteNonQuery(conn, "PRAGMA synchronous = OFF;");
        ExecuteNonQuery(conn, "PRAGMA temp_store = MEMORY;");
        ExecuteNonQuery(conn, "PRAGMA cache_size = -131072;"); // 128 MB page cache

        // Metadata & Summary tables
        using (var metaTx = conn.BeginTransaction())
        {
            ExecuteNonQuery(conn, """
                CREATE TABLE "_ExtractionMetadata" (
                    "Key" TEXT PRIMARY KEY,
                    "Value" TEXT NOT NULL
                );
                CREATE TABLE "_TableSummary" (
                    "TableName" TEXT PRIMARY KEY,
                    "TableHash" TEXT NOT NULL,
                    "LayoutHash" TEXT NOT NULL,
                    "ColumnCount" INTEGER NOT NULL,
                    "BaseDb2Rows" INTEGER NOT NULL,
                    "HotfixUpserts" INTEGER NOT NULL,
                    "HotfixDeletes" INTEGER NOT NULL,
                    "FinalRowCount" INTEGER NOT NULL
                );
                """, metaTx);

            InsertMeta(conn, metaTx, "Product", build.Product);
            InsertMeta(conn, metaTx, "Version", build.Version);
            InsertMeta(conn, metaTx, "BuildName", build.BuildName);
            InsertMeta(conn, metaTx, "BuildKey", build.BuildKey);
            InsertMeta(conn, metaTx, "ExtractedAtUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            InsertMeta(conn, metaTx, "TotalTables", tables.Count.ToString(CultureInfo.InvariantCulture));
            InsertMeta(conn, metaTx, "HotfixEntryCount", (hotfixDoc?.Entries.Count ?? 0).ToString(CultureInfo.InvariantCulture));

            using var sumCmd = conn.CreateCommand();
            sumCmd.Transaction = metaTx;
            sumCmd.CommandText = """
                INSERT INTO "_TableSummary"
                ("TableName", "TableHash", "LayoutHash", "ColumnCount", "BaseDb2Rows", "HotfixUpserts", "HotfixDeletes", "FinalRowCount")
                VALUES ($n, $th, $lh, $cc, $base, $up, $del, $fin);
                """;
            var pN = sumCmd.Parameters.Add("$n", SqliteType.Text);
            var pTh = sumCmd.Parameters.Add("$th", SqliteType.Text);
            var pLh = sumCmd.Parameters.Add("$lh", SqliteType.Text);
            var pCc = sumCmd.Parameters.Add("$cc", SqliteType.Integer);
            var pBase = sumCmd.Parameters.Add("$base", SqliteType.Integer);
            var pUp = sumCmd.Parameters.Add("$up", SqliteType.Integer);
            var pDel = sumCmd.Parameters.Add("$del", SqliteType.Integer);
            var pFin = sumCmd.Parameters.Add("$fin", SqliteType.Integer);
            sumCmd.Prepare();

            foreach (var t in tables.OrderBy(x => x.TableName, StringComparer.OrdinalIgnoreCase))
            {
                pN.Value = t.TableName;
                pTh.Value = $"{t.TableHash:X8}";
                pLh.Value = $"{t.LayoutHash:X8}";
                pCc.Value = t.Columns.Count;
                pBase.Value = t.BaseDb2RowCount;
                pUp.Value = t.HotfixUpsertCount;
                pDel.Value = t.HotfixDeleteCount;
                pFin.Value = t.RowsById.Count;
                sumCmd.ExecuteNonQuery();
            }

            metaTx.Commit();
        }

        // Hotfix cache audit log table
        if (hotfixDoc != null)
        {
            using var hfTx = conn.BeginTransaction();
            ExecuteNonQuery(conn, """
                CREATE TABLE "_HotfixCacheEntries" (
                    "EntryIndex" INTEGER PRIMARY KEY,
                    "RegionId" INTEGER NOT NULL,
                    "PushId" INTEGER NOT NULL,
                    "UniqueId" INTEGER NOT NULL,
                    "TableHash" TEXT NOT NULL,
                    "TableName" TEXT NOT NULL,
                    "RecordId" INTEGER NOT NULL,
                    "Status" INTEGER NOT NULL,
                    "StatusLabel" TEXT NOT NULL,
                    "PayloadSize" INTEGER NOT NULL
                );
                """, hfTx);

            using var hfCmd = conn.CreateCommand();
            hfCmd.Transaction = hfTx;
            hfCmd.CommandText = """
                INSERT INTO "_HotfixCacheEntries"
                ("EntryIndex", "RegionId", "PushId", "UniqueId", "TableHash", "TableName", "RecordId", "Status", "StatusLabel", "PayloadSize")
                VALUES ($i, $reg, $push, $uid, $th, $tn, $rid, $st, $stl, $sz);
                """;
            var pI = hfCmd.Parameters.Add("$i", SqliteType.Integer);
            var pReg = hfCmd.Parameters.Add("$reg", SqliteType.Integer);
            var pPush = hfCmd.Parameters.Add("$push", SqliteType.Integer);
            var pUid = hfCmd.Parameters.Add("$uid", SqliteType.Integer);
            var pTh = hfCmd.Parameters.Add("$th", SqliteType.Text);
            var pTn = hfCmd.Parameters.Add("$tn", SqliteType.Text);
            var pRid = hfCmd.Parameters.Add("$rid", SqliteType.Integer);
            var pSt = hfCmd.Parameters.Add("$st", SqliteType.Integer);
            var pStl = hfCmd.Parameters.Add("$stl", SqliteType.Text);
            var pSz = hfCmd.Parameters.Add("$sz", SqliteType.Integer);
            hfCmd.Prepare();

            foreach (var e in hotfixDoc.Entries)
            {
                pI.Value = e.Index;
                pReg.Value = e.RegionId;
                pPush.Value = e.PushId;
                pUid.Value = (long)e.UniqueId;
                pTh.Value = $"{e.TableHash:X8}";
                pTn.Value = e.TableName;
                pRid.Value = e.RecordId;
                pSt.Value = (int)e.Status;
                pStl.Value = e.StatusLabel;
                pSz.Value = e.Payload.Length;
                hfCmd.ExecuteNonQuery();
            }

            ExecuteNonQuery(conn, """
                CREATE INDEX "IX_Hotfix_Table_Record" ON "_HotfixCacheEntries" ("TableName", "RecordId");
                CREATE INDEX "IX_Hotfix_Status" ON "_HotfixCacheEntries" ("Status");
                """, hfTx);

            hfTx.Commit();
        }

        // Game tables
        foreach (var table in tables.OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
        {
            WriteSqliteGameTable(conn, table);
        }

        // Pre-joined convenience views
        CreateConvenienceViews(conn, tables.ToDictionary(t => t.TableName, StringComparer.OrdinalIgnoreCase));

        ExecuteNonQuery(conn, "PRAGMA journal_mode = DELETE;");
        ExecuteNonQuery(conn, "PRAGMA synchronous = NORMAL;");
    }

    private static void WriteSqliteGameTable(SqliteConnection conn, ExtractedTableData table)
    {
        using var tx = conn.BeginTransaction();

        string safeTable = QuoteIdent(table.TableName);
        var uniqueColNames = MakeUniqueColumnNames(table.Columns);
        int idColIdx = table.Columns.ToList().FindIndex(c => c.IsId);

        var colDefs = new List<string>(table.Columns.Count + 3);
        if (idColIdx < 0)
        {
            colDefs.Add("\"_RecordID\" INTEGER PRIMARY KEY");
        }

        for (int c = 0; c < table.Columns.Count; c++)
        {
            var col = table.Columns[c];
            string sqlType = GetSqliteColumnType(col);
            string pk = c == idColIdx ? " PRIMARY KEY" : string.Empty;
            colDefs.Add($"{QuoteIdent(uniqueColNames[c])} {sqlType}{pk}");
        }

        colDefs.Add("\"_DataSource\" TEXT NOT NULL");
        colDefs.Add("\"_HotfixPushID\" INTEGER");

        ExecuteNonQuery(conn, $"CREATE TABLE {safeTable} ({string.Join(", ", colDefs)});", tx);

        var insertColIdents = new List<string>(table.Columns.Count + 3);
        var paramPlaceholders = new List<string>(table.Columns.Count + 3);

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;

        SqliteParameter? implicitIdParam = null;
        if (idColIdx < 0)
        {
            insertColIdents.Add("\"_RecordID\"");
            paramPlaceholders.Add("$recId");
            implicitIdParam = cmd.Parameters.Add("$recId", SqliteType.Integer);
        }

        var colParams = new SqliteParameter[table.Columns.Count];
        for (int c = 0; c < table.Columns.Count; c++)
        {
            insertColIdents.Add(QuoteIdent(uniqueColNames[c]));
            string pName = $"$c{c}";
            paramPlaceholders.Add(pName);
            colParams[c] = cmd.Parameters.Add(pName, GetSqliteParamType(table.Columns[c]));
        }

        insertColIdents.Add("\"_DataSource\"");
        paramPlaceholders.Add("$src");
        var srcParam = cmd.Parameters.Add("$src", SqliteType.Text);

        insertColIdents.Add("\"_HotfixPushID\"");
        paramPlaceholders.Add("$pushId");
        var pushParam = cmd.Parameters.Add("$pushId", SqliteType.Integer);

        cmd.CommandText = $"INSERT INTO {safeTable} ({string.Join(", ", insertColIdents)}) VALUES ({string.Join(", ", paramPlaceholders)});";
        cmd.Prepare();

        foreach (var row in table.RowsById.Values.OrderBy(r => r.Id))
        {
            if (implicitIdParam != null)
            {
                implicitIdParam.Value = row.Id;
            }

            for (int c = 0; c < table.Columns.Count; c++)
            {
                object? val = c < row.Values.Length ? row.Values[c] : null;
                if (c == idColIdx)
                {
                    colParams[c].Value = row.Id;
                }
                else
                {
                    colParams[c].Value = ConvertToSqliteParameterValue(val, table.Columns[c]);
                }
            }

            srcParam.Value = row.Source.ToString();
            pushParam.Value = row.HotfixPushId.HasValue ? row.HotfixPushId.Value : DBNull.Value;
            cmd.ExecuteNonQuery();
        }

        // Add fast indexes on common foreign-key and hotfix columns
        ExecuteNonQuery(conn, $"CREATE INDEX {QuoteIdent($"IX_{table.TableName}_DataSource")} ON {safeTable} (\"_DataSource\");", tx);

        string[] indexableFkNames = ["SpellID", "ItemID", "CreatureID", "QuestID", "ParentItemID", "ItemEffectID", "SkillLine"];
        for (int c = 0; c < uniqueColNames.Count; c++)
        {
            if (c != idColIdx && indexableFkNames.Contains(uniqueColNames[c], StringComparer.OrdinalIgnoreCase))
            {
                string idxName = QuoteIdent($"IX_{table.TableName}_{uniqueColNames[c]}");
                ExecuteNonQuery(conn, $"CREATE INDEX {idxName} ON {safeTable} ({QuoteIdent(uniqueColNames[c])});", tx);
            }
        }

        tx.Commit();
    }

    private static void CreateConvenienceViews(
        SqliteConnection conn,
        Dictionary<string, ExtractedTableData> tables)
    {
        // 1. v_Items & v_CustomForeverItems
        if (tables.ContainsKey("ItemSparse") && tables.ContainsKey("Item"))
        {
            bool hasClass = tables.ContainsKey("ItemClass");
            bool hasIcons = tables.ContainsKey("ManifestInterfaceData");

            string iconSelect = hasIcons
                ? ", mid.\"FilePath\" AS \"IconPath\", mid.\"FileName\" AS \"IconFileName\""
                : "";
            string iconJoin = hasIcons
                ? "LEFT JOIN \"ManifestInterfaceData\" mid ON mid.\"ID\" = i.\"IconFileDataID\""
                : "";
            string classSelect = hasClass
                ? ", ic.\"ClassName_lang\" AS \"ClassName\""
                : "";
            string classJoin = hasClass
                ? "LEFT JOIN \"ItemClass\" ic ON ic.\"ClassID\" = i.\"ClassID\""
                : "";

            TryExecuteView(conn, $"""
                CREATE VIEW "v_Items" AS
                SELECT
                    s."ID",
                    s."Display_lang" AS "Name",
                    s."Description_lang" AS "Description",
                    s."OverallQualityID" AS "Quality",
                    s."ItemLevel",
                    s."RequiredLevel",
                    i."ClassID"{classSelect},
                    i."SubclassID",
                    s."InventoryType",
                    s."ContainerSlots",
                    s."BuyPrice",
                    s."SellPrice",
                    s."Bonding",
                    s."MaxCount",
                    s."Stackable",
                    s."ItemDelay",
                    s."StatModifier_bonusStat",
                    s."StatPercentEditor",
                    s."Flags",
                    i."IconFileDataID"{iconSelect},
                    CASE
                        WHEN s."_DataSource" != 'Db2' THEN s."_DataSource"
                        ELSE COALESCE(i."_DataSource", 'Db2')
                    END AS "_DataSource",
                    COALESCE(s."_HotfixPushID", i."_HotfixPushID") AS "_HotfixPushID"
                FROM "ItemSparse" s
                LEFT JOIN "Item" i ON i."ID" = s."ID"
                {classJoin}
                {iconJoin};
                """);

            TryExecuteView(conn, """
                CREATE VIEW "v_CustomForeverItems" AS
                SELECT * FROM "v_Items"
                WHERE "_DataSource" != 'Db2'
                ORDER BY "ID";
                """);
        }

        // 2. v_Spells & v_CustomForeverSpells
        if (tables.ContainsKey("SpellName"))
        {
            bool hasSpell = tables.ContainsKey("Spell");
            bool hasMisc = tables.ContainsKey("SpellMisc");
            bool hasIcons = tables.ContainsKey("ManifestInterfaceData") && hasMisc;

            string spellCols = hasSpell
                ? ", sp.\"NameSubtext_lang\" AS \"Subtext\", sp.\"Description_lang\" AS \"Description\", sp.\"AuraDescription_lang\" AS \"AuraDescription\""
                : "";
            string spellJoin = hasSpell
                ? "LEFT JOIN \"Spell\" sp ON sp.\"ID\" = sn.\"ID\""
                : "";

            string miscCols = hasMisc
                ? ", sm.\"SchoolMask\", sm.\"CastingTimeIndex\", sm.\"DurationIndex\", sm.\"RangeIndex\", sm.\"SpellIconFileDataID\""
                : "";
            string miscJoin = hasMisc
                ? """
                  LEFT JOIN (
                      SELECT * FROM "SpellMisc" WHERE "DifficultyID" = 0 GROUP BY "SpellID"
                  ) sm ON sm."SpellID" = sn."ID"
                  """
                : "";

            string iconCols = hasIcons
                ? ", mid.\"FilePath\" AS \"IconPath\", mid.\"FileName\" AS \"IconFileName\""
                : "";
            string iconJoin = hasIcons
                ? "LEFT JOIN \"ManifestInterfaceData\" mid ON mid.\"ID\" = sm.\"SpellIconFileDataID\""
                : "";

            string spellSourceExpr = hasSpell && hasMisc
                ? """
                  CASE
                      WHEN sn."_DataSource" != 'Db2' THEN sn."_DataSource"
                      WHEN sp."_DataSource" IS NOT NULL AND sp."_DataSource" != 'Db2' THEN sp."_DataSource"
                      WHEN sm."_DataSource" IS NOT NULL AND sm."_DataSource" != 'Db2' THEN sm."_DataSource"
                      ELSE 'Db2'
                  END
                  """
                : hasSpell
                    ? """
                      CASE
                          WHEN sn."_DataSource" != 'Db2' THEN sn."_DataSource"
                          ELSE COALESCE(sp."_DataSource", 'Db2')
                      END
                      """
                    : "sn.\"_DataSource\"";

            string spellPushExpr = hasSpell && hasMisc
                ? "COALESCE(sn.\"_HotfixPushID\", sp.\"_HotfixPushID\", sm.\"_HotfixPushID\")"
                : hasSpell
                    ? "COALESCE(sn.\"_HotfixPushID\", sp.\"_HotfixPushID\")"
                    : "sn.\"_HotfixPushID\"";

            TryExecuteView(conn, $"""
                CREATE VIEW "v_Spells" AS
                SELECT
                    sn."ID",
                    sn."Name_lang" AS "Name"
                    {spellCols}
                    {miscCols}
                    {iconCols},
                    {spellSourceExpr} AS "_DataSource",
                    {spellPushExpr} AS "_HotfixPushID"
                FROM "SpellName" sn
                {spellJoin}
                {miscJoin}
                {iconJoin};
                """);

            TryExecuteView(conn, """
                CREATE VIEW "v_CustomForeverSpells" AS
                SELECT * FROM "v_Spells"
                WHERE "_DataSource" != 'Db2'
                ORDER BY "ID";
                """);
        }

        // 3. v_Creatures
        if (tables.ContainsKey("Creature"))
        {
            bool hasType = tables.ContainsKey("CreatureType");
            bool hasFamily = tables.ContainsKey("CreatureFamily");

            TryExecuteView(conn, $"""
                CREATE VIEW "v_Creatures" AS
                SELECT
                    c."ID",
                    c."Name_lang" AS "Name",
                    c."Title_lang" AS "Title",
                    c."Classification",
                    c."CreatureType" AS "CreatureTypeID"
                    {(hasType ? ", ct.\"Name_lang\" AS \"CreatureTypeName\"" : "")},
                    c."CreatureFamily" AS "CreatureFamilyID"
                    {(hasFamily ? ", cf.\"Name_lang\" AS \"CreatureFamilyName\"" : "")},
                    c."DisplayID",
                    c."_DataSource"
                FROM "Creature" c
                {(hasType ? "LEFT JOIN \"CreatureType\" ct ON ct.\"ID\" = c.\"CreatureType\"" : "")}
                {(hasFamily ? "LEFT JOIN \"CreatureFamily\" cf ON cf.\"ID\" = c.\"CreatureFamily\"" : "")};
                """);
        }
    }

    private static void WriteTableJson(ExtractedTableData table, string outputPath, bool indented)
    {
        var uniqueCols = MakeUniqueColumnNames(table.Columns);
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536);
        using var writer = new Utf8JsonWriter(fs, indented ? IndentedJsonOptions : CompactJsonOptions);

        writer.WriteStartArray();
        foreach (var row in table.RowsById.Values.OrderBy(r => r.Id))
        {
            writer.WriteStartObject();
            bool wroteId = false;

            for (int c = 0; c < table.Columns.Count; c++)
            {
                var col = table.Columns[c];
                string propName = uniqueCols[c];
                if (col.IsId)
                {
                    writer.WriteNumber(propName, row.Id);
                    wroteId = true;
                }
                else
                {
                    object? val = c < row.Values.Length ? row.Values[c] : null;
                    WriteJsonProperty(writer, propName, val);
                }
            }

            if (!wroteId)
            {
                writer.WriteNumber("_RecordID", row.Id);
            }

            writer.WriteString("_DataSource", row.Source.ToString());
            if (row.HotfixPushId.HasValue)
            {
                writer.WriteNumber("_HotfixPushID", row.HotfixPushId.Value);
            }

            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteCuratedJsonBundles(
        WowBuildMetadata build,
        IReadOnlyList<ExtractedTableData> tables,
        Dictionary<string, ExtractedTableData> byName,
        HotfixCacheDocument? hotfixDoc,
        string jsonDir)
    {
        // Build Icon lookup map if ManifestInterfaceData was extracted
        var iconMap = new Dictionary<int, string>();
        if (byName.TryGetValue("ManifestInterfaceData", out var mid))
        {
            int idxPath = FindCol(mid, "FilePath");
            int idxName = FindCol(mid, "FileName");
            foreach (var r in mid.RowsById.Values)
            {
                string p = idxPath >= 0 ? Convert.ToString(r.Values[idxPath]) ?? "" : "";
                string n = idxName >= 0 ? Convert.ToString(r.Values[idxName]) ?? "" : "";
                iconMap[r.Id] = string.IsNullOrEmpty(p) ? n : $"{p}{n}";
            }
        }

        // 1. Items_Complete.json & CustomForever_Items.json
        if (byName.TryGetValue("ItemSparse", out var itemSparse))
        {
            byName.TryGetValue("Item", out var itemTbl);
            byName.TryGetValue("ItemClass", out var itemClassTbl);

            var classNames = new Dictionary<int, string>();
            if (itemClassTbl != null)
            {
                int idxCid = FindCol(itemClassTbl, "ClassID");
                int idxCname = FindCol(itemClassTbl, "ClassName_lang");
                foreach (var r in itemClassTbl.RowsById.Values)
                {
                    int cid = idxCid >= 0 ? Convert.ToInt32(r.Values[idxCid]) : r.Id;
                    string cname = idxCname >= 0 ? Convert.ToString(r.Values[idxCname]) ?? "" : "";
                    classNames[cid] = cname;
                }
            }

            WriteJoinedItemsJson(Path.Combine(jsonDir, "Items_Complete.json"), itemSparse, itemTbl, classNames, iconMap, customOnly: false);
            WriteJoinedItemsJson(Path.Combine(jsonDir, "CustomForever_Items.json"), itemSparse, itemTbl, classNames, iconMap, customOnly: true);
        }

        // 2. Spells_Complete.json & CustomForever_Spells.json
        if (byName.TryGetValue("SpellName", out var spellName))
        {
            byName.TryGetValue("Spell", out var spellTbl);
            byName.TryGetValue("SpellMisc", out var spellMiscTbl);
            WriteJoinedSpellsJson(Path.Combine(jsonDir, "Spells_Complete.json"), spellName, spellTbl, spellMiscTbl, iconMap, customOnly: false);
            WriteJoinedSpellsJson(Path.Combine(jsonDir, "CustomForever_Spells.json"), spellName, spellTbl, spellMiscTbl, iconMap, customOnly: true);
        }

        // 3. Creatures_Complete.json & CustomForever_Creatures.json
        if (byName.TryGetValue("Creature", out var creatureTbl))
        {
            byName.TryGetValue("CreatureType", out var creatureTypeTbl);
            byName.TryGetValue("CreatureFamily", out var creatureFamilyTbl);
            WriteJoinedCreaturesJson(Path.Combine(jsonDir, "Creatures_Complete.json"), creatureTbl, creatureTypeTbl, creatureFamilyTbl, customOnly: false);
            WriteJoinedCreaturesJson(Path.Combine(jsonDir, "CustomForever_Creatures.json"), creatureTbl, creatureTypeTbl, creatureFamilyTbl, customOnly: true);
        }

        // 4. _ExtractionManifest.json
        string manifestPath = Path.Combine(jsonDir, "_ExtractionManifest.json");
        using var mFs = new FileStream(manifestPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var mWriter = new Utf8JsonWriter(mFs, IndentedJsonOptions);
        mWriter.WriteStartObject();
        mWriter.WriteString("Product", build.Product);
        mWriter.WriteString("Version", build.Version);
        mWriter.WriteString("BuildName", build.BuildName);
        mWriter.WriteString("BuildKey", build.BuildKey);
        mWriter.WriteString("ExtractedAtUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        mWriter.WriteNumber("TotalTables", tables.Count);
        mWriter.WriteNumber("TotalRows", tables.Sum(t => (long)t.RowsById.Count));
        mWriter.WriteNumber("HotfixCacheTotalEntries", hotfixDoc?.Entries.Count ?? 0);

        mWriter.WriteStartArray("Tables");
        foreach (var t in tables.OrderBy(x => x.TableName, StringComparer.OrdinalIgnoreCase))
        {
            mWriter.WriteStartObject();
            mWriter.WriteString("TableName", t.TableName);
            mWriter.WriteString("TableHash", $"{t.TableHash:X8}");
            mWriter.WriteString("LayoutHash", $"{t.LayoutHash:X8}");
            mWriter.WriteNumber("BaseDb2Rows", t.BaseDb2RowCount);
            mWriter.WriteNumber("HotfixUpserts", t.HotfixUpsertCount);
            mWriter.WriteNumber("HotfixDeletes", t.HotfixDeleteCount);
            mWriter.WriteNumber("FinalRowCount", t.RowsById.Count);
            mWriter.WriteEndObject();
        }
        mWriter.WriteEndArray();
        mWriter.WriteEndObject();
    }

    private static void WriteJoinedItemsJson(
        string outputPath,
        ExtractedTableData sparse,
        ExtractedTableData? itemTbl,
        Dictionary<int, string> classNames,
        Dictionary<int, string> iconMap,
        bool customOnly)
    {
        int sName = FindCol(sparse, "Display_lang");
        int sDesc = FindCol(sparse, "Description_lang");
        int sQual = FindCol(sparse, "OverallQualityID");
        int sIlvl = FindCol(sparse, "ItemLevel");
        int sRlvl = FindCol(sparse, "RequiredLevel");
        int sInv = FindCol(sparse, "InventoryType");
        int sBuy = FindCol(sparse, "BuyPrice");
        int sSell = FindCol(sparse, "SellPrice");
        int sBond = FindCol(sparse, "Bonding");
        int sDelay = FindCol(sparse, "ItemDelay");
        int sBags = FindCol(sparse, "ContainerSlots");
        int sStatId = FindCol(sparse, "StatModifier_bonusStat");
        int sStatVal = FindCol(sparse, "StatPercentEditor");

        int iClass = itemTbl != null ? FindCol(itemTbl, "ClassID") : -1;
        int iSub = itemTbl != null ? FindCol(itemTbl, "SubclassID") : -1;
        int iIcon = itemTbl != null ? FindCol(itemTbl, "IconFileDataID") : -1;

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536);
        using var w = new Utf8JsonWriter(fs, IndentedJsonOptions);

        w.WriteStartArray();
        foreach (var row in sparse.RowsById.Values.OrderBy(r => r.Id))
        {
            ExtractedRow? iRow = null;
            itemTbl?.RowsById.TryGetValue(row.Id, out iRow);

            var effSource = row.Source != RowSourceKind.Db2
                ? row.Source
                : (iRow?.Source ?? RowSourceKind.Db2);

            if (customOnly && effSource == RowSourceKind.Db2)
            {
                continue;
            }

            w.WriteStartObject();
            w.WriteNumber("ID", row.Id);
            w.WriteString("Name", GetStr(row, sName));
            string desc = GetStr(row, sDesc);
            if (desc.Length > 0) w.WriteString("Description", desc);

            w.WriteNumber("Quality", GetInt(row, sQual));
            w.WriteNumber("ItemLevel", GetInt(row, sIlvl));
            w.WriteNumber("RequiredLevel", GetInt(row, sRlvl));

            if (iRow != null)
            {
                int cid = GetInt(iRow, iClass);
                w.WriteNumber("ClassID", cid);
                if (classNames.TryGetValue(cid, out string? cn) && cn.Length > 0)
                {
                    w.WriteString("ClassName", cn);
                }
                w.WriteNumber("SubclassID", GetInt(iRow, iSub));

                int iconFdid = GetInt(iRow, iIcon);
                w.WriteNumber("IconFileDataID", iconFdid);
                if (iconMap.TryGetValue(iconFdid, out string? iconPath) && iconPath.Length > 0)
                {
                    w.WriteString("Icon", iconPath);
                }
            }

            w.WriteNumber("InventoryType", GetInt(row, sInv));
            w.WriteNumber("BuyPrice", GetLong(row, sBuy));
            w.WriteNumber("SellPrice", GetLong(row, sSell));
            w.WriteNumber("Bonding", GetInt(row, sBond));
            w.WriteNumber("ItemDelayMs", GetInt(row, sDelay));

            int slots = GetInt(row, sBags);
            if (slots > 0) w.WriteNumber("ContainerSlots", slots);

            if (sStatId >= 0) WriteJsonProperty(w, "BonusStatIDs", row.Values[sStatId]);
            if (sStatVal >= 0) WriteJsonProperty(w, "BonusStatValues", row.Values[sStatVal]);

            w.WriteString("DataSource", effSource.ToString());
            int? pushId = row.HotfixPushId ?? iRow?.HotfixPushId;
            if (pushId.HasValue) w.WriteNumber("HotfixPushID", pushId.Value);

            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void WriteJoinedSpellsJson(
        string outputPath,
        ExtractedTableData spellName,
        ExtractedTableData? spellTbl,
        ExtractedTableData? spellMiscTbl,
        Dictionary<int, string> iconMap,
        bool customOnly)
    {
        int snName = FindCol(spellName, "Name_lang");
        int spSub = spellTbl != null ? FindCol(spellTbl, "NameSubtext_lang") : -1;
        int spDesc = spellTbl != null ? FindCol(spellTbl, "Description_lang") : -1;
        int spAura = spellTbl != null ? FindCol(spellTbl, "AuraDescription_lang") : -1;

        var miscBySpellId = new Dictionary<int, ExtractedRow>();
        int smSpellId = -1;
        int smIcon = -1;
        int smSchool = -1;
        int smDiff = -1;

        if (spellMiscTbl != null)
        {
            smSpellId = FindCol(spellMiscTbl, "SpellID");
            smIcon = FindCol(spellMiscTbl, "SpellIconFileDataID");
            smSchool = FindCol(spellMiscTbl, "SchoolMask");
            smDiff = FindCol(spellMiscTbl, "DifficultyID");

            if (smSpellId >= 0)
            {
                foreach (var mr in spellMiscTbl.RowsById.Values)
                {
                    int sid = GetInt(mr, smSpellId);
                    int diff = smDiff >= 0 ? GetInt(mr, smDiff) : 0;
                    if (sid > 0 && (diff == 0 || !miscBySpellId.ContainsKey(sid)))
                    {
                        miscBySpellId[sid] = mr;
                    }
                }
            }
        }

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536);
        using var w = new Utf8JsonWriter(fs, IndentedJsonOptions);

        w.WriteStartArray();
        foreach (var row in spellName.RowsById.Values.OrderBy(r => r.Id))
        {
            ExtractedRow? spRow = null;
            spellTbl?.RowsById.TryGetValue(row.Id, out spRow);
            miscBySpellId.TryGetValue(row.Id, out var mRow);

            var effSource = row.Source != RowSourceKind.Db2
                ? row.Source
                : spRow?.Source is RowSourceKind.Db2PlusHotfix or RowSourceKind.HotfixOnly
                    ? spRow.Source
                    : mRow?.Source is RowSourceKind.Db2PlusHotfix or RowSourceKind.HotfixOnly
                        ? mRow.Source
                        : RowSourceKind.Db2;

            if (customOnly && effSource == RowSourceKind.Db2)
            {
                continue;
            }

            w.WriteStartObject();
            w.WriteNumber("ID", row.Id);
            w.WriteString("Name", GetStr(row, snName));

            if (spRow != null)
            {
                string sub = GetStr(spRow, spSub);
                string desc = GetStr(spRow, spDesc);
                string aura = GetStr(spRow, spAura);
                if (sub.Length > 0) w.WriteString("Subtext", sub);
                if (desc.Length > 0) w.WriteString("Description", desc);
                if (aura.Length > 0) w.WriteString("AuraDescription", aura);
            }

            if (mRow != null)
            {
                if (smSchool >= 0) w.WriteNumber("SchoolMask", GetInt(mRow, smSchool));
                if (smIcon >= 0)
                {
                    int iconId = GetInt(mRow, smIcon);
                    w.WriteNumber("SpellIconFileDataID", iconId);
                    if (iconMap.TryGetValue(iconId, out string? iconPath) && iconPath.Length > 0)
                    {
                        w.WriteString("Icon", iconPath);
                    }
                }
            }

            w.WriteString("DataSource", effSource.ToString());
            int? pushId = row.HotfixPushId ?? spRow?.HotfixPushId ?? mRow?.HotfixPushId;
            if (pushId.HasValue) w.WriteNumber("HotfixPushID", pushId.Value);

            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void WriteJoinedCreaturesJson(
        string outputPath,
        ExtractedTableData creatureTbl,
        ExtractedTableData? creatureTypeTbl,
        ExtractedTableData? creatureFamilyTbl,
        bool customOnly)
    {
        int cName = FindCol(creatureTbl, "Name_lang");
        int cTitle = FindCol(creatureTbl, "Title_lang");
        int cClass = FindCol(creatureTbl, "Classification");
        int cType = FindCol(creatureTbl, "CreatureType");
        int cFam = FindCol(creatureTbl, "CreatureFamily");
        int cDisp = FindCol(creatureTbl, "DisplayID");

        var typeNames = new Dictionary<int, string>();
        if (creatureTypeTbl != null)
        {
            int ctName = FindCol(creatureTypeTbl, "Name_lang");
            foreach (var r in creatureTypeTbl.RowsById.Values)
            {
                typeNames[r.Id] = GetStr(r, ctName);
            }
        }

        var famNames = new Dictionary<int, string>();
        if (creatureFamilyTbl != null)
        {
            int cfName = FindCol(creatureFamilyTbl, "Name_lang");
            foreach (var r in creatureFamilyTbl.RowsById.Values)
            {
                famNames[r.Id] = GetStr(r, cfName);
            }
        }

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536);
        using var w = new Utf8JsonWriter(fs, IndentedJsonOptions);

        w.WriteStartArray();
        foreach (var row in creatureTbl.RowsById.Values.OrderBy(r => r.Id))
        {
            if (customOnly && row.Source == RowSourceKind.Db2)
            {
                continue;
            }

            w.WriteStartObject();
            w.WriteNumber("ID", row.Id);
            w.WriteString("Name", GetStr(row, cName));
            string title = GetStr(row, cTitle);
            if (title.Length > 0) w.WriteString("Title", title);

            w.WriteNumber("Classification", GetInt(row, cClass));

            int typeId = GetInt(row, cType);
            w.WriteNumber("CreatureTypeID", typeId);
            if (typeNames.TryGetValue(typeId, out string? tn) && tn.Length > 0)
            {
                w.WriteString("CreatureTypeName", tn);
            }

            int famId = GetInt(row, cFam);
            if (famId > 0)
            {
                w.WriteNumber("CreatureFamilyID", famId);
                if (famNames.TryGetValue(famId, out string? fn) && fn.Length > 0)
                {
                    w.WriteString("CreatureFamilyName", fn);
                }
            }

            if (cDisp >= 0)
            {
                WriteJsonProperty(w, "DisplayIDs", row.Values[cDisp]);
            }

            w.WriteString("DataSource", row.Source.ToString());
            if (row.HotfixPushId.HasValue)
            {
                w.WriteNumber("HotfixPushID", row.HotfixPushId.Value);
            }

            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void WriteJsonProperty(Utf8JsonWriter writer, string propName, object? val)
    {
        if (val == null)
        {
            writer.WriteNull(propName);
            return;
        }

        if (val is object?[] arr)
        {
            writer.WriteStartArray(propName);
            foreach (var elem in arr)
            {
                WriteJsonScalarValue(writer, elem);
            }
            writer.WriteEndArray();
            return;
        }

        writer.WritePropertyName(propName);
        WriteJsonScalarValue(writer, val);
    }

    private static void WriteJsonScalarValue(Utf8JsonWriter writer, object? val)
    {
        switch (val)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case byte b:
                writer.WriteNumberValue(b);
                break;
            case sbyte sb:
                writer.WriteNumberValue(sb);
                break;
            case short s16:
                writer.WriteNumberValue(s16);
                break;
            case ushort u16:
                writer.WriteNumberValue(u16);
                break;
            case int i32:
                writer.WriteNumberValue(i32);
                break;
            case uint u32:
                writer.WriteNumberValue(u32);
                break;
            case long i64:
                writer.WriteNumberValue(i64);
                break;
            case ulong u64:
                writer.WriteNumberValue(u64);
                break;
            case float f:
                if (float.IsFinite(f))
                    writer.WriteNumberValue(Math.Round(f, 6));
                else
                    writer.WriteNullValue();
                break;
            case double d:
                if (double.IsFinite(d))
                    writer.WriteNumberValue(d);
                else
                    writer.WriteNullValue();
                break;
            default:
                writer.WriteStringValue(val.ToString());
                break;
        }
    }

    private static object ConvertToSqliteParameterValue(object? val, DbdColumnLayout col)
    {
        if (val == null) return DBNull.Value;

        if (val is object?[] arr)
        {
            return JsonSerializer.Serialize(arr);
        }

        return val switch
        {
            string s => s,
            byte b => (long)b,
            sbyte sb => (long)sb,
            short s16 => (long)s16,
            ushort u16 => (long)u16,
            int i32 => (long)i32,
            uint u32 => (long)u32,
            long i64 => i64,
            ulong u64 => unchecked((long)u64),
            float f => float.IsFinite(f) ? (double)f : DBNull.Value,
            double d => double.IsFinite(d) ? d : DBNull.Value,
            _ => val.ToString() ?? string.Empty
        };
    }

    private static string GetSqliteColumnType(DbdColumnLayout col)
    {
        if (col.ArrayLength > 1 || col.BaseKind == DbdBaseKind.String)
        {
            return "TEXT";
        }

        return col.BaseKind == DbdBaseKind.Float ? "REAL" : "INTEGER";
    }

    private static SqliteType GetSqliteParamType(DbdColumnLayout col)
    {
        if (col.ArrayLength > 1 || col.BaseKind == DbdBaseKind.String)
        {
            return SqliteType.Text;
        }

        return col.BaseKind == DbdBaseKind.Float ? SqliteType.Real : SqliteType.Integer;
    }

    private static List<string> MakeUniqueColumnNames(IReadOnlyList<DbdColumnLayout> cols)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "_RecordID", "_DataSource", "_HotfixPushID" };
        var result = new List<string>(cols.Count);

        foreach (var c in cols)
        {
            string baseName = string.IsNullOrWhiteSpace(c.Name) ? "Col" : c.Name;
            string candidate = baseName;
            int suffix = 2;
            while (!seen.Add(candidate))
            {
                candidate = $"{baseName}_{suffix++}";
            }
            result.Add(candidate);
        }

        return result;
    }

    private static int FindCol(ExtractedTableData tbl, string name)
    {
        for (int i = 0; i < tbl.Columns.Count; i++)
        {
            if (tbl.Columns[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private static string GetStr(ExtractedRow row, int idx) =>
        idx >= 0 && idx < row.Values.Length ? Convert.ToString(row.Values[idx]) ?? string.Empty : string.Empty;

    private static int GetInt(ExtractedRow row, int idx) =>
        idx >= 0 && idx < row.Values.Length && row.Values[idx] != null ? Convert.ToInt32(row.Values[idx]) : 0;

    private static long GetLong(ExtractedRow row, int idx) =>
        idx >= 0 && idx < row.Values.Length && row.Values[idx] != null ? Convert.ToInt64(row.Values[idx]) : 0L;

    private static string QuoteIdent(string name) => $"\"{name.Replace("\"", "\"\"")}\"";

    private static void InsertMeta(SqliteConnection conn, SqliteTransaction tx, string key, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO \"_ExtractionMetadata\" (\"Key\", \"Value\") VALUES ($k, $v);";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    private static void ExecuteNonQuery(SqliteConnection conn, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void TryExecuteView(SqliteConnection conn, string sql)
    {
        try
        {
            ExecuteNonQuery(conn, sql);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Hinweis] SQL-View übersprungen: {ex.Message}");
        }
    }
}
