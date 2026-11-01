using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace GrunzForever.Core;

public sealed record TableSummaryRow(
    string TableName,
    string TableHash,
    string LayoutHash,
    int ColumnCount,
    int BaseDb2Rows,
    int HotfixUpserts,
    int HotfixDeletes,
    int FinalRowCount);

public sealed record TableSummaryDelta(
    string TableName,
    string OldLayoutHash,
    string NewLayoutHash,
    int OldBaseRows,
    int NewBaseRows,
    int OldHotfixUpserts,
    int NewHotfixUpserts,
    int OldHotfixDeletes,
    int NewHotfixDeletes,
    int OldFinalRows,
    int NewFinalRows)
{
    public bool LayoutChanged => !string.Equals(OldLayoutHash, NewLayoutHash, StringComparison.OrdinalIgnoreCase);
    public int RowDelta => NewFinalRows - OldFinalRows;
}

public sealed record HotfixPushWaveDelta(
    int PushId,
    int AddedValidCount,
    int AddedDeleteCount,
    IReadOnlyList<string> AffectedTables);

public sealed record ItemDeltaEntry(
    int Id,
    string Name,
    int Quality,
    int ItemLevel,
    int RequiredLevel,
    string DataSource,
    int? HotfixPushId,
    string? ChangeDetails = null);

public sealed record SpellDeltaEntry(
    int Id,
    string Name,
    string Subtext,
    string Description,
    string DataSource,
    int? HotfixPushId,
    string? ChangeDetails = null);

public sealed record CharTitleDeltaEntry(int Id, string Name);

public sealed class BuildDiffReport
{
    public required string OldVersion { get; init; }
    public required string NewVersion { get; init; }
    public required string OldBuildKey { get; init; }
    public required string NewBuildKey { get; init; }
    public required int OldTableCount { get; init; }
    public required int NewTableCount { get; init; }
    public required long OldTotalRows { get; init; }
    public required long NewTotalRows { get; init; }
    public required int OldValidHotfixes { get; init; }
    public required int NewValidHotfixes { get; init; }
    public required int OldDeleteHotfixes { get; init; }
    public required int NewDeleteHotfixes { get; init; }
    public required IReadOnlyList<TableSummaryRow> AddedTables { get; init; }
    public required IReadOnlyList<TableSummaryRow> RemovedTables { get; init; }
    public required IReadOnlyList<TableSummaryDelta> ChangedTables { get; init; }
    public required IReadOnlyList<HotfixPushWaveDelta> NewHotfixWaves { get; init; }
    public required IReadOnlyList<ItemDeltaEntry> AddedItems { get; init; }
    public required IReadOnlyList<ItemDeltaEntry> RemovedItems { get; init; }
    public required IReadOnlyList<ItemDeltaEntry> ModifiedItems { get; init; }
    public required IReadOnlyList<SpellDeltaEntry> AddedSpells { get; init; }
    public required IReadOnlyList<SpellDeltaEntry> RemovedSpells { get; init; }
    public required IReadOnlyList<SpellDeltaEntry> ModifiedSpells { get; init; }
    public required IReadOnlyList<CharTitleDeltaEntry> AddedTitles { get; init; }
}

/// <summary>
/// Compares two extracted GrunzForever SQLite databases and produces structured JSON and Markdown patch changelogs.
/// </summary>
public static class WowBuildDiffer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static BuildDiffReport Compare(string oldSqlitePath, string newSqlitePath)
    {
        if (!File.Exists(oldSqlitePath))
            throw new FileNotFoundException($"Alte SQLite-Datenbank nicht gefunden: {oldSqlitePath}", oldSqlitePath);
        if (!File.Exists(newSqlitePath))
            throw new FileNotFoundException($"Neue SQLite-Datenbank nicht gefunden: {newSqlitePath}", newSqlitePath);

        using var connOld = OpenReadOnly(oldSqlitePath);
        using var connNew = OpenReadOnly(newSqlitePath);

        var metaOld = ReadMetadata(connOld);
        var metaNew = ReadMetadata(connNew);

        var sumOld = ReadTableSummaries(connOld);
        var sumNew = ReadTableSummaries(connNew);

        var addedTables = sumNew.Values
            .Where(t => !sumOld.ContainsKey(t.TableName))
            .OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var removedTables = sumOld.Values
            .Where(t => !sumNew.ContainsKey(t.TableName))
            .OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var changedTables = new List<TableSummaryDelta>();
        foreach (var (name, n) in sumNew.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!sumOld.TryGetValue(name, out var o)) continue;
            if (!string.Equals(o.LayoutHash, n.LayoutHash, StringComparison.OrdinalIgnoreCase) ||
                o.BaseDb2Rows != n.BaseDb2Rows ||
                o.HotfixUpserts != n.HotfixUpserts ||
                o.HotfixDeletes != n.HotfixDeletes ||
                o.FinalRowCount != n.FinalRowCount)
            {
                changedTables.Add(new TableSummaryDelta(
                    name,
                    o.LayoutHash, n.LayoutHash,
                    o.BaseDb2Rows, n.BaseDb2Rows,
                    o.HotfixUpserts, n.HotfixUpserts,
                    o.HotfixDeletes, n.HotfixDeletes,
                    o.FinalRowCount, n.FinalRowCount));
            }
        }

        var (oldValidHf, oldDelHf, hfSetOld) = ReadActiveHotfixes(connOld);
        var (newValidHf, newDelHf, hfSetNew) = ReadActiveHotfixes(connNew);

        var newWaves = hfSetNew
            .Where(h => !hfSetOld.Contains(h) && h.PushId > 0)
            .GroupBy(h => h.PushId)
            .OrderBy(g => g.Key)
            .Select(g => new HotfixPushWaveDelta(
                PushId: g.Key,
                AddedValidCount: g.Count(x => x.Status == 1),
                AddedDeleteCount: g.Count(x => x.Status == 2),
                AffectedTables: g.Select(x => x.TableName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();

        var (addedItems, removedItems, modifiedItems) = CompareItems(connOld, connNew);
        var (addedSpells, removedSpells, modifiedSpells) = CompareSpells(connOld, connNew);
        var addedTitles = CompareCharTitles(connOld, connNew);

        return new BuildDiffReport
        {
            OldVersion = metaOld.GetValueOrDefault("Version", "unknown"),
            NewVersion = metaNew.GetValueOrDefault("Version", "unknown"),
            OldBuildKey = metaOld.GetValueOrDefault("BuildKey", "unknown"),
            NewBuildKey = metaNew.GetValueOrDefault("BuildKey", "unknown"),
            OldTableCount = sumOld.Count,
            NewTableCount = sumNew.Count,
            OldTotalRows = sumOld.Values.Sum(x => (long)x.FinalRowCount),
            NewTotalRows = sumNew.Values.Sum(x => (long)x.FinalRowCount),
            OldValidHotfixes = oldValidHf,
            NewValidHotfixes = newValidHf,
            OldDeleteHotfixes = oldDelHf,
            NewDeleteHotfixes = newDelHf,
            AddedTables = addedTables,
            RemovedTables = removedTables,
            ChangedTables = changedTables,
            NewHotfixWaves = newWaves,
            AddedItems = addedItems,
            RemovedItems = removedItems,
            ModifiedItems = modifiedItems,
            AddedSpells = addedSpells,
            RemovedSpells = removedSpells,
            ModifiedSpells = modifiedSpells,
            AddedTitles = addedTitles
        };
    }

    public static void WriteJson(BuildDiffReport report, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        string json = JsonSerializer.Serialize(report, JsonOptions);
        File.WriteAllText(outputPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static string FormatMarkdown(BuildDiffReport r, int maxSampleRows = 100)
    {
        var sb = new StringBuilder(32_768);
        var ic = CultureInfo.InvariantCulture;

        sb.AppendLine(string.Create(ic, $"# WoW Classic Forever Build-Diff: `{r.OldVersion}` -> `{r.NewVersion}`"));
        sb.AppendLine();
        sb.AppendLine("| Metrik | Vorher | Nachher | Delta |");
        sb.AppendLine("| :--- | ---: | ---: | ---: |");
        sb.AppendLine(string.Create(ic, $"| **Version** | `{r.OldVersion}` | `{r.NewVersion}` | {(r.OldVersion == r.NewVersion ? "unveraendert" : "Update")} |"));
        sb.AppendLine(string.Create(ic, $"| **BuildKey** | `{ShortKey(r.OldBuildKey)}` | `{ShortKey(r.NewBuildKey)}` | {(r.OldBuildKey == r.NewBuildKey ? "identisch" : "neu")} |"));
        sb.AppendLine(string.Create(ic, $"| **Extrahierte Tabellen** | {r.OldTableCount:N0} | {r.NewTableCount:N0} | {FormatSigned(r.NewTableCount - r.OldTableCount)} |"));
        sb.AppendLine(string.Create(ic, $"| **Datensaetze gesamt** | {r.OldTotalRows:N0} | {r.NewTotalRows:N0} | {FormatSigned(r.NewTotalRows - r.OldTotalRows)} |"));
        sb.AppendLine(string.Create(ic, $"| **Aktive Hotfixes (Valid)** | {r.OldValidHotfixes:N0} | {r.NewValidHotfixes:N0} | {FormatSigned(r.NewValidHotfixes - r.OldValidHotfixes)} |"));
        sb.AppendLine(string.Create(ic, $"| **Geloeschte Eintraege (Delete)** | {r.OldDeleteHotfixes:N0} | {r.NewDeleteHotfixes:N0} | {FormatSigned(r.NewDeleteHotfixes - r.OldDeleteHotfixes)} |"));
        sb.AppendLine(string.Create(ic, $"| **Items (+ / ~ / -)** | - | - | +{r.AddedItems.Count:N0} / ~{r.ModifiedItems.Count:N0} / -{r.RemovedItems.Count:N0} |"));
        sb.AppendLine(string.Create(ic, $"| **Spells (+ / ~ / -)** | - | - | +{r.AddedSpells.Count:N0} / ~{r.ModifiedSpells.Count:N0} / -{r.RemovedSpells.Count:N0} |"));
        sb.AppendLine();

        if (r.AddedTables.Count > 0 || r.RemovedTables.Count > 0)
        {
            sb.AppendLine("## 1. Neue & entfernte DB2-Tabellen");
            sb.AppendLine();
            foreach (var t in r.AddedTables)
                sb.AppendLine(string.Create(ic, $"- **Neu:** `{t.TableName}` (`LayoutHash={t.LayoutHash}`, `{t.FinalRowCount:N0}` Zeilen)"));
            foreach (var t in r.RemovedTables)
                sb.AppendLine(string.Create(ic, $"- **Entfernt:** `{t.TableName}` (zuvor `{t.FinalRowCount:N0}` Zeilen)"));
            sb.AppendLine();
        }

        if (r.ChangedTables.Count > 0)
        {
            sb.AppendLine("## 2. Geaenderte Tabellen (Zeilen / Schema / Hotfixes)");
            sb.AppendLine();
            sb.AppendLine("| Tabelle | Layout | DB2 Basis | Hotfix + | Hotfix - | Final | Delta |");
            sb.AppendLine("| :--- | :---: | ---: | ---: | ---: | ---: | ---: |");
            foreach (var c in r.ChangedTables.OrderByDescending(x => Math.Abs(x.RowDelta)).ThenBy(x => x.TableName, StringComparer.OrdinalIgnoreCase))
            {
                string layoutInfo = c.LayoutChanged ? $"`{c.OldLayoutHash}` -> `{c.NewLayoutHash}`" : $"`{c.NewLayoutHash}`";
                sb.AppendLine(string.Create(ic, $"| `{c.TableName}` | {layoutInfo} | {c.OldBaseRows:N0} -> {c.NewBaseRows:N0} | {c.OldHotfixUpserts:N0} -> {c.NewHotfixUpserts:N0} | {c.OldHotfixDeletes:N0} -> {c.NewHotfixDeletes:N0} | {c.OldFinalRows:N0} -> {c.NewFinalRows:N0} | {FormatSigned(c.RowDelta)} |"));
            }
            sb.AppendLine();
        }

        if (r.NewHotfixWaves.Count > 0)
        {
            sb.AppendLine(string.Create(ic, $"## 3. Neue Server-Hotfix-Wellen (`{r.NewHotfixWaves.Count:N0}` PushIDs)"));
            sb.AppendLine();
            sb.AppendLine("| PushID | Valid (+) | Delete (-) | Betroffene Tabellen |");
            sb.AppendLine("| ---: | ---: | ---: | :--- |");
            foreach (var w in r.NewHotfixWaves.OrderByDescending(x => x.PushId).Take(maxSampleRows))
            {
                string tbls = string.Join(", ", w.AffectedTables.Select(t => $"`{t}`"));
                sb.AppendLine(string.Create(ic, $"| `{w.PushId}` | {w.AddedValidCount:N0} | {w.AddedDeleteCount:N0} | {tbls} |"));
            }
            sb.AppendLine();
        }

        if (r.AddedItems.Count > 0 || r.ModifiedItems.Count > 0 || r.RemovedItems.Count > 0)
        {
            sb.AppendLine("## 4. Item-Aenderungen");
            sb.AppendLine();
            if (r.AddedItems.Count > 0)
            {
                sb.AppendLine(string.Create(ic, $"### Neue Items (`{r.AddedItems.Count:N0}`, Anzeige bis {maxSampleRows})"));
                sb.AppendLine();
                sb.AppendLine("| ID | Name | Qualitaet | iLvl | ReqLvl | Quelle |");
                sb.AppendLine("| ---: | :--- | ---: | ---: | ---: | :--- |");
                foreach (var it in r.AddedItems.OrderByDescending(x => x.Id).Take(maxSampleRows))
                {
                    sb.AppendLine(string.Create(ic, $"| `{it.Id}` | {EscapeMd(it.Name)} | {it.Quality} | {it.ItemLevel} | {it.RequiredLevel} | `{it.DataSource}` |"));
                }
                sb.AppendLine();
            }

            if (r.ModifiedItems.Count > 0)
            {
                sb.AppendLine(string.Create(ic, $"### Modifizierte Items (`{r.ModifiedItems.Count:N0}`, Anzeige bis {maxSampleRows})"));
                sb.AppendLine();
                sb.AppendLine("| ID | Name | Aenderungen |");
                sb.AppendLine("| ---: | :--- | :--- |");
                foreach (var it in r.ModifiedItems.OrderByDescending(x => x.Id).Take(maxSampleRows))
                {
                    sb.AppendLine(string.Create(ic, $"| `{it.Id}` | {EscapeMd(it.Name)} | {EscapeMd(it.ChangeDetails ?? "")} |"));
                }
                sb.AppendLine();
            }
        }

        if (r.AddedSpells.Count > 0 || r.ModifiedSpells.Count > 0 || r.RemovedSpells.Count > 0)
        {
            sb.AppendLine("## 5. Spell-Aenderungen");
            sb.AppendLine();
            if (r.AddedSpells.Count > 0)
            {
                sb.AppendLine(string.Create(ic, $"### Neue Spells (`{r.AddedSpells.Count:N0}`, Anzeige bis {maxSampleRows})"));
                sb.AppendLine();
                sb.AppendLine("| ID | Name | Subtext | Quelle | Beschreibung |");
                sb.AppendLine("| ---: | :--- | :--- | :--- | :--- |");
                foreach (var sp in r.AddedSpells.OrderByDescending(x => x.Id).Take(maxSampleRows))
                {
                    string desc = TruncateOneLine(sp.Description, 90);
                    sb.AppendLine(string.Create(ic, $"| `{sp.Id}` | {EscapeMd(sp.Name)} | {EscapeMd(sp.Subtext)} | `{sp.DataSource}` | {EscapeMd(desc)} |"));
                }
                sb.AppendLine();
            }

            if (r.ModifiedSpells.Count > 0)
            {
                sb.AppendLine(string.Create(ic, $"### Modifizierte Spells (`{r.ModifiedSpells.Count:N0}`, Anzeige bis {maxSampleRows})"));
                sb.AppendLine();
                sb.AppendLine("| ID | Name | Aenderungen |");
                sb.AppendLine("| ---: | :--- | :--- |");
                foreach (var sp in r.ModifiedSpells.OrderByDescending(x => x.Id).Take(maxSampleRows))
                {
                    sb.AppendLine(string.Create(ic, $"| `{sp.Id}` | {EscapeMd(sp.Name)} | {EscapeMd(sp.ChangeDetails ?? "")} |"));
                }
                sb.AppendLine();
            }
        }

        if (r.AddedTitles.Count > 0)
        {
            sb.AppendLine(string.Create(ic, $"## 6. Neue Charakter-Titel (`{r.AddedTitles.Count:N0}`)"));
            sb.AppendLine();
            foreach (var ct in r.AddedTitles.OrderBy(x => x.Id))
                sb.AppendLine(string.Create(ic, $"- `{ct.Id}`: **{EscapeMd(ct.Name)}**"));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        var conn = new SqliteConnection(csb.ToString());
        conn.Open();
        return conn;
    }

    private static Dictionary<string, string> ReadMetadata(SqliteConnection conn)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!ObjectExists(conn, "_ExtractionMetadata")) return dict;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"Key\", \"Value\" FROM \"_ExtractionMetadata\";";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            dict[r.GetString(0)] = r.GetString(1);
        return dict;
    }

    private static Dictionary<string, TableSummaryRow> ReadTableSummaries(SqliteConnection conn)
    {
        var dict = new Dictionary<string, TableSummaryRow>(StringComparer.OrdinalIgnoreCase);
        if (!ObjectExists(conn, "_TableSummary")) return dict;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"TableName\", \"TableHash\", \"LayoutHash\", \"ColumnCount\", \"BaseDb2Rows\", \"HotfixUpserts\", \"HotfixDeletes\", \"FinalRowCount\" FROM \"_TableSummary\";";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var row = new TableSummaryRow(
                r.GetString(0),
                r.GetString(1),
                r.GetString(2),
                r.GetInt32(3),
                r.GetInt32(4),
                r.GetInt32(5),
                r.GetInt32(6),
                r.GetInt32(7));
            dict[row.TableName] = row;
        }
        return dict;
    }

    private static (int ValidCount, int DeleteCount, HashSet<(int PushId, string TableName, int RecordId, int Status)> ActiveSet) ReadActiveHotfixes(SqliteConnection conn)
    {
        var set = new HashSet<(int, string, int, int)>();
        int valid = 0, del = 0;
        if (!ObjectExists(conn, "_HotfixCacheEntries")) return (0, 0, set);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"PushId\", \"TableName\", \"RecordId\", \"Status\" FROM \"_HotfixCacheEntries\" WHERE \"Status\" IN (1, 2);";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int push = r.GetInt32(0);
            string tbl = r.GetString(1);
            int rec = r.GetInt32(2);
            int st = r.GetInt32(3);
            if (st == 1) valid++;
            else if (st == 2) del++;
            set.Add((push, tbl, rec, st));
        }
        return (valid, del, set);
    }

    private sealed record RawItemSnapshot(
        int Id,
        string Name,
        int Quality,
        int ItemLevel,
        int RequiredLevel,
        long SellPrice,
        string StatsJson,
        string StatValsJson,
        string DataSource,
        int? HotfixPushId);

    private static (List<ItemDeltaEntry> Added, List<ItemDeltaEntry> Removed, List<ItemDeltaEntry> Modified) CompareItems(
        SqliteConnection connOld,
        SqliteConnection connNew)
    {
        if (!ObjectExists(connOld, "v_Items") || !ObjectExists(connNew, "v_Items"))
            return ([], [], []);

        var oldMap = ReadItems(connOld);
        var newMap = ReadItems(connNew);

        var added = new List<ItemDeltaEntry>();
        var removed = new List<ItemDeltaEntry>();
        var modified = new List<ItemDeltaEntry>();

        foreach (var (id, n) in newMap)
        {
            if (!oldMap.TryGetValue(id, out var o))
            {
                added.Add(new ItemDeltaEntry(n.Id, n.Name, n.Quality, n.ItemLevel, n.RequiredLevel, n.DataSource, n.HotfixPushId));
                continue;
            }

            var diffs = new List<string>();
            if (o.Name != n.Name) diffs.Add($"Name: '{o.Name}' -> '{n.Name}'");
            if (o.Quality != n.Quality) diffs.Add($"Quality: {o.Quality} -> {n.Quality}");
            if (o.ItemLevel != n.ItemLevel) diffs.Add($"iLvl: {o.ItemLevel} -> {n.ItemLevel}");
            if (o.RequiredLevel != n.RequiredLevel) diffs.Add($"ReqLvl: {o.RequiredLevel} -> {n.RequiredLevel}");
            if (o.SellPrice != n.SellPrice) diffs.Add($"SellPrice: {o.SellPrice} -> {n.SellPrice}");
            if (o.StatsJson != n.StatsJson || o.StatValsJson != n.StatValsJson) diffs.Add("Stats geaendert");

            if (diffs.Count > 0)
            {
                modified.Add(new ItemDeltaEntry(n.Id, n.Name, n.Quality, n.ItemLevel, n.RequiredLevel, n.DataSource, n.HotfixPushId, string.Join("; ", diffs)));
            }
        }

        foreach (var (id, o) in oldMap)
        {
            if (!newMap.ContainsKey(id))
                removed.Add(new ItemDeltaEntry(o.Id, o.Name, o.Quality, o.ItemLevel, o.RequiredLevel, o.DataSource, o.HotfixPushId));
        }

        return (added, removed, modified);
    }

    private static Dictionary<int, RawItemSnapshot> ReadItems(SqliteConnection conn)
    {
        var map = new Dictionary<int, RawItemSnapshot>(24_000);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT "ID", COALESCE("Name", ''), COALESCE("Quality", 0), COALESCE("ItemLevel", 0),
                   COALESCE("RequiredLevel", 0), COALESCE("SellPrice", 0),
                   COALESCE("StatModifier_bonusStat", ''), COALESCE("StatPercentEditor", ''),
                   COALESCE("_DataSource", 'Db2'), "_HotfixPushID"
            FROM "v_Items";
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int id = r.GetInt32(0);
            map[id] = new RawItemSnapshot(
                id,
                r.GetString(1),
                r.GetInt32(2),
                r.GetInt32(3),
                r.GetInt32(4),
                r.GetInt64(5),
                r.GetString(6),
                r.GetString(7),
                r.GetString(8),
                r.IsDBNull(9) ? null : r.GetInt32(9));
        }
        return map;
    }

    private sealed record RawSpellSnapshot(
        int Id,
        string Name,
        string Subtext,
        string Description,
        string AuraDescription,
        string DataSource,
        int? HotfixPushId);

    private static (List<SpellDeltaEntry> Added, List<SpellDeltaEntry> Removed, List<SpellDeltaEntry> Modified) CompareSpells(
        SqliteConnection connOld,
        SqliteConnection connNew)
    {
        if (!ObjectExists(connOld, "v_Spells") || !ObjectExists(connNew, "v_Spells"))
            return ([], [], []);

        var oldMap = ReadSpells(connOld);
        var newMap = ReadSpells(connNew);

        var added = new List<SpellDeltaEntry>();
        var removed = new List<SpellDeltaEntry>();
        var modified = new List<SpellDeltaEntry>();

        foreach (var (id, n) in newMap)
        {
            if (!oldMap.TryGetValue(id, out var o))
            {
                added.Add(new SpellDeltaEntry(n.Id, n.Name, n.Subtext, n.Description, n.DataSource, n.HotfixPushId));
                continue;
            }

            var diffs = new List<string>();
            if (o.Name != n.Name) diffs.Add($"Name: '{o.Name}' -> '{n.Name}'");
            if (o.Subtext != n.Subtext) diffs.Add($"Subtext: '{o.Subtext}' -> '{n.Subtext}'");
            if (o.Description != n.Description) diffs.Add("Beschreibung geaendert");
            if (o.AuraDescription != n.AuraDescription) diffs.Add("Aura-Tooltip geaendert");

            if (diffs.Count > 0)
            {
                modified.Add(new SpellDeltaEntry(n.Id, n.Name, n.Subtext, n.Description, n.DataSource, n.HotfixPushId, string.Join("; ", diffs)));
            }
        }

        foreach (var (id, o) in oldMap)
        {
            if (!newMap.ContainsKey(id))
                removed.Add(new SpellDeltaEntry(o.Id, o.Name, o.Subtext, o.Description, o.DataSource, o.HotfixPushId));
        }

        return (added, removed, modified);
    }

    private static Dictionary<int, RawSpellSnapshot> ReadSpells(SqliteConnection conn)
    {
        var cols = GetColumnNames(conn, "v_Spells");
        string subExpr = cols.Contains("Subtext") ? "COALESCE(\"Subtext\", '')" : "''";
        string descExpr = cols.Contains("Description") ? "COALESCE(\"Description\", '')" : "''";
        string auraExpr = cols.Contains("AuraDescription") ? "COALESCE(\"AuraDescription\", '')" : "''";

        var map = new Dictionary<int, RawSpellSnapshot>(35_000);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT "ID", COALESCE("Name", ''), {subExpr},
                   {descExpr}, {auraExpr},
                   COALESCE("_DataSource", 'Db2'), "_HotfixPushID"
            FROM "v_Spells";
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int id = r.GetInt32(0);
            map[id] = new RawSpellSnapshot(
                id,
                r.GetString(1),
                r.GetString(2),
                r.GetString(3),
                r.GetString(4),
                r.GetString(5),
                r.IsDBNull(6) ? null : r.GetInt32(6));
        }
        return map;
    }

    private static HashSet<string> GetColumnNames(SqliteConnection conn, string tableOrView)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tableOrView.Replace("\"", "\"\"")}\");";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            set.Add(r.GetString(1));
        return set;
    }

    private static List<CharTitleDeltaEntry> CompareCharTitles(SqliteConnection connOld, SqliteConnection connNew)
    {
        if (!ObjectExists(connNew, "CharTitles")) return [];

        var oldIds = new HashSet<int>();
        if (ObjectExists(connOld, "CharTitles"))
        {
            using var co = connOld.CreateCommand();
            co.CommandText = "SELECT \"ID\" FROM \"CharTitles\";";
            using var ro = co.ExecuteReader();
            while (ro.Read()) oldIds.Add(ro.GetInt32(0));
        }

        var added = new List<CharTitleDeltaEntry>();
        using var cn = connNew.CreateCommand();
        cn.CommandText = "SELECT \"ID\", COALESCE(\"Name_lang\", '') FROM \"CharTitles\" ORDER BY \"ID\";";
        using var rn = cn.ExecuteReader();
        while (rn.Read())
        {
            int id = rn.GetInt32(0);
            if (!oldIds.Contains(id))
                added.Add(new CharTitleDeltaEntry(id, rn.GetString(1)));
        }
        return added;
    }

    private static bool ObjectExists(SqliteConnection conn, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE name = $n LIMIT 1;";
        cmd.Parameters.AddWithValue("$n", name);
        return cmd.ExecuteScalar() != null;
    }

    private static string ShortKey(string key) =>
        key.Length > 12 ? key[..12] + "..." : key;

    private static string FormatSigned(long v) =>
        v.ToString("+#,##0;-#,##0;0", CultureInfo.InvariantCulture);

    private static string TruncateOneLine(string s, int maxLen)
    {
        string flat = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return flat.Length <= maxLen ? flat : flat[..maxLen] + "...";
    }

    private static string EscapeMd(string s) =>
        s.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");
}
