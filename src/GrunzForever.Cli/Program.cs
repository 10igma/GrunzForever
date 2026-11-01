using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;
using GrunzForever.Core;
using Microsoft.Data.Sqlite;

Console.OutputEncoding = Encoding.UTF8;

string repoRoot = ResolveWorkspaceRoot();
string? wowFlavorDir = Environment.GetEnvironmentVariable("WOW_FOREVER_DIR");
string exportDir = Path.Combine(repoRoot, "Exports");
string cacheDir = Path.Combine(repoRoot, "Cache", "WoWDBDefs");
bool allDb2 = false;
bool exportLuaApiOnly = false;
bool syncRepoData = false;
bool showHelp = false;
string? inspectTable = null;
string? sqlQuery = null;
string? explicitDbFile = null;
string? diffOldDb = null;
string? diffNewDb = null;
string? diffOutMd = null;
string? diffOutJson = null;
int? exportBlpFdid = null;
string? exportBlpOut = null;
int? dumpCascFdid = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i].ToLowerInvariant())
    {
        case "-h" or "--help" or "help":
            showHelp = true;
            break;
        case "--wow-dir" when i + 1 < args.Length:
            wowFlavorDir = args[++i];
            break;
        case "--out-dir" when i + 1 < args.Length:
            exportDir = Path.GetFullPath(args[++i]);
            break;
        case "--cache-dir" when i + 1 < args.Length:
            cacheDir = Path.GetFullPath(args[++i]);
            break;
        case "--db" when i + 1 < args.Length:
            explicitDbFile = Path.GetFullPath(args[++i]);
            break;
        case "--inspect-db2" when i + 1 < args.Length:
            inspectTable = args[++i];
            break;
        case "--sql" when i + 1 < args.Length:
            sqlQuery = args[++i];
            break;
        case "--diff" when i + 2 < args.Length:
            diffOldDb = Path.GetFullPath(args[++i]);
            diffNewDb = Path.GetFullPath(args[++i]);
            break;
        case "--out-md" when i + 1 < args.Length:
            diffOutMd = Path.GetFullPath(args[++i]);
            break;
        case "--out-json" when i + 1 < args.Length:
            diffOutJson = Path.GetFullPath(args[++i]);
            break;
        case "--export-blp" when i + 2 < args.Length:
            exportBlpFdid = int.Parse(args[++i], CultureInfo.InvariantCulture);
            exportBlpOut = Path.GetFullPath(args[++i]);
            break;
        case "--dump-casc" when i + 1 < args.Length:
            dumpCascFdid = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--export-lua-api":
            exportLuaApiOnly = true;
            break;
        case "--sync-repo-data":
            syncRepoData = true;
            break;
        case "--all-db2":
            allDb2 = true;
            break;
    }
}

if (showHelp)
{
    PrintHelp();
    return 0;
}

if (!string.IsNullOrWhiteSpace(diffOldDb) && !string.IsNullOrWhiteSpace(diffNewDb))
{
    var diff = WowBuildDiffer.Compare(diffOldDb, diffNewDb);
    string md = WowBuildDiffer.FormatMarkdown(diff);

    if (!string.IsNullOrWhiteSpace(diffOutMd))
    {
        Directory.CreateDirectory(Path.GetDirectoryName(diffOutMd)!);
        File.WriteAllText(diffOutMd, md, new UTF8Encoding(false));
        Console.WriteLine($"[Diff] Markdown-Changelog gespeichert: {diffOutMd}");
    }
    else
    {
        Console.WriteLine(md);
    }

    if (!string.IsNullOrWhiteSpace(diffOutJson))
    {
        WowBuildDiffer.WriteJson(diff, diffOutJson);
        Console.WriteLine($"[Diff] JSON-Bericht gespeichert: {diffOutJson}");
    }

    return 0;
}

if (!string.IsNullOrWhiteSpace(sqlQuery))
{
    string dbFile = explicitDbFile ?? ResolveLatestSqlitePath(exportDir);
    using var conn = new SqliteConnection($"Data Source={dbFile};Mode=ReadOnly");
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = sqlQuery;
    using var reader = cmd.ExecuteReader();
    var colNames = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
    Console.WriteLine(string.Join(" | ", colNames));
    Console.WriteLine(new string('-', Math.Min(120, colNames.Length * 18)));
    while (reader.Read())
    {
        var vals = Enumerable.Range(0, reader.FieldCount)
            .Select(c => reader.IsDBNull(c) ? "NULL" : reader.GetValue(c)?.ToString() ?? "")
            .ToArray();
        Console.WriteLine(string.Join(" | ", vals));
    }
    return 0;
}

string resolvedWowDir = ResolveWowForeverDirectory(wowFlavorDir);

if (exportLuaApiOnly)
{
    string cacheRoot = Path.Combine(repoRoot, "Cache");
    var luaApiRes = await LuaApiExporter.ExportFromCascAsync(resolvedWowDir, cacheRoot, exportDir, Console.WriteLine);
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"[LuaAPI] {luaApiRes.ProjectCodeName} (WOW_PROJECT_ID={luaApiRes.ProjectId}, Build {luaApiRes.BuildVersion}): {luaApiRes.SystemCount:N0} Systeme | {luaApiRes.DistinctNamespaceCount:N0} C_* Namespaces | {luaApiRes.TotalFunctionCount:N0} Funktionen | {luaApiRes.TotalEventCount:N0} Events"));
    return 0;
}

if (dumpCascFdid.HasValue)
{
    using var casc = CascStorage.Open(resolvedWowDir);
    var root = RootManifest.Parse(casc.LoadRootManifestBytes());
    if (!root.TryGetContentKey(dumpCascFdid.Value, out var ck))
    {
        Console.WriteLine($"FDID {dumpCascFdid.Value} nicht im Root-Manifest.");
        return 1;
    }
    byte[] rawBytes = casc.ReadFileByCKey(ck) ?? [];
    Console.WriteLine(Encoding.UTF8.GetString(rawBytes));
    return 0;
}

if (exportBlpFdid.HasValue && !string.IsNullOrWhiteSpace(exportBlpOut))
{
    using var casc = CascStorage.Open(resolvedWowDir);
    var root = RootManifest.Parse(casc.LoadRootManifestBytes());
    if (!root.TryGetContentKey(exportBlpFdid.Value, out var ck))
    {
        Console.WriteLine($"FDID {exportBlpFdid.Value} nicht im Root-Manifest.");
        return 1;
    }
    byte[] blpBytes = casc.ReadFileByCKey(ck) ?? throw new InvalidOperationException("BLP konnte nicht aus CASC gelesen werden.");
    byte[] pngBytes = BlpDecoder.DecodeToPngBytes(blpBytes);
    Directory.CreateDirectory(Path.GetDirectoryName(exportBlpOut)!);
    File.WriteAllBytes(exportBlpOut, pngBytes);
    Console.WriteLine($"BLP {exportBlpFdid.Value} ({blpBytes.Length} B) -> PNG ({pngBytes.Length} B): {exportBlpOut}");
    return 0;
}

if (!string.IsNullOrWhiteSpace(inspectTable))
{
    using var casc = CascStorage.Open(resolvedWowDir);
    var root = RootManifest.Parse(casc.LoadRootManifestBytes());
    using var dbd = new DbdSchemaProvider(cacheDir);
    await dbd.InitializeManifestAsync();
    if (!dbd.TryGetByName(inspectTable, out var entry) || !entry.Db2FileDataId.HasValue)
    {
        Console.WriteLine($"Tabelle '{inspectTable}' nicht gefunden.");
        return 1;
    }
    if (!root.TryGetContentKey(entry.Db2FileDataId.Value, out var ck))
    {
        Console.WriteLine($"FDID {entry.Db2FileDataId} nicht im Root-Manifest.");
        return 1;
    }
    byte[] raw = casc.ReadFileByCKey(ck)!;
    int metaPos = 136;
    int recCount = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(metaPos + 0, 4));
    int minId = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(metaPos + 24, 4));
    int maxId = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(metaPos + 28, 4));
    ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(metaPos + 36, 2));
    int secCount = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(metaPos + 64, 4));
    Console.WriteLine($"{entry.TableName} FDID={entry.Db2FileDataId} RawLen={raw.Length} Recs={recCount} MinId={minId} MaxId={maxId} Flags=0x{flags:X} Sections={secCount}");
    return 0;
}

Console.WriteLine("==============================================================================");
Console.WriteLine("  GrunzForever — WoW Classic Forever (Camelot) CASC DB2, Hotfix & API Miner");
Console.WriteLine("==============================================================================");

var result = await WowMiningPipeline.RunAsync(
    new PipelineOptions
    {
        WowFlavorDirectory = resolvedWowDir,
        ExportDirectory = exportDir,
        DbdCacheDirectory = cacheDir,
        ExtractAllAvailableDb2s = allDb2
    },
    Console.WriteLine);

var luaApiSummary = await LuaApiExporter.ExportFromCascAsync(
    resolvedWowDir,
    Path.Combine(repoRoot, "Cache"),
    exportDir,
    Console.WriteLine);

if (syncRepoData)
{
    SyncCuratedRepositoryData(repoRoot, exportDir, result.Build.Version);
}

long sqliteBytes = File.Exists(result.ExportSummary.SqlitePath)
    ? new FileInfo(result.ExportSummary.SqlitePath).Length
    : 0;

Console.WriteLine();
Console.WriteLine("==============================================================================");
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  Build      : {result.Build.Product} {result.Build.Version} ({result.Build.BuildKey})"));
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  SQLite-DB  : {result.ExportSummary.SqlitePath} ({sqliteBytes / (1024.0 * 1024.0):F2} MB)"));
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  JSON-Pfad  : {result.ExportSummary.JsonDirectory}"));
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  Lua-API    : {luaApiSummary.SystemCount:N0} Systeme | {luaApiSummary.DistinctNamespaceCount:N0} C_* | {luaApiSummary.TotalFunctionCount:N0} Funktionen | {luaApiSummary.TotalEventCount:N0} Events"));
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  Tabellen   : {result.ExportSummary.ExportedTableCount:N0} | Datensaetze gesamt: {result.ExportSummary.TotalExportedRows:N0}"));
Console.WriteLine("==============================================================================");

return 0;

static void PrintHelp()
{
    Console.WriteLine("""
        GrunzForever — WoW Classic Forever (Camelot) Data-Mining & Diff CLI

        Verwendung:
          grunzforever [--wow-dir <Pfad>] [--out-dir <Pfad>] [--all-db2] [--sync-repo-data]
          grunzforever --export-lua-api [--wow-dir <Pfad>] [--out-dir <Pfad>]
          grunzforever --diff <alt.sqlite> <neu.sqlite> [--out-md <bericht.md>] [--out-json <bericht.json>]
          grunzforever --sql "<SQL-Abfrage>" [--db <pfad.sqlite>]
          grunzforever --inspect-db2 <Tabellenname> [--wow-dir <Pfad>]
          grunzforever --export-blp <FileDataID> <ziel.png> [--wow-dir <Pfad>]
          grunzforever --dump-casc <FileDataID> [--wow-dir <Pfad>]

        Umgebungsvariablen:
          WOW_FOREVER_DIR   Pfad zum lokalen '_forever_'-Verzeichnis von World of Warcraft.
        """);
}

static void SyncCuratedRepositoryData(string repoRoot, string exportDir, string buildVersion)
{
    string dataRoot = Path.Combine(repoRoot, "data");
    string manifestsDir = Path.Combine(dataRoot, "manifests");
    string foreverDir = Path.Combine(dataRoot, "forever");
    string luaApiDstDir = Path.Combine(dataRoot, "lua_api");

    Directory.CreateDirectory(manifestsDir);
    Directory.CreateDirectory(foreverDir);
    Directory.CreateDirectory(luaApiDstDir);

    string srcJsonDir = Path.Combine(exportDir, "json");
    string srcTablesDir = Path.Combine(srcJsonDir, "tables");
    string srcLuaApiDir = Path.Combine(exportDir, "lua_api");

    CopyIfExists(Path.Combine(srcJsonDir, "_ExtractionManifest.json"), Path.Combine(manifestsDir, $"_ExtractionManifest_{buildVersion}.json"));
    CopyIfExists(Path.Combine(srcJsonDir, "_ExtractionManifest.json"), Path.Combine(manifestsDir, "_ExtractionManifest_Latest.json"));

    foreach (string bundle in new[] { "CustomForever_Items.json", "CustomForever_Spells.json", "Creatures_Complete.json" })
    {
        CopyIfExists(Path.Combine(srcJsonDir, bundle), Path.Combine(foreverDir, bundle));
    }

    foreach (string tbl in new[] { "CharTitles.json", "Cfg_GameRules.json", "Cfg_Categories.json", "Cfg_Regions.json", "ChrClasses.json", "ChrRaces.json", "SkillLine.json", "SpellRange.json" })
    {
        CopyIfExists(Path.Combine(srcTablesDir, tbl), Path.Combine(foreverDir, tbl));
    }

    foreach (string apiFile in new[] { "LuaApi_Reference.md", "LuaApi_CamelotSurface.json", "LuaApi_Complete.json" })
    {
        CopyIfExists(Path.Combine(srcLuaApiDir, apiFile), Path.Combine(luaApiDstDir, apiFile));
    }

    Console.WriteLine($"[Sync] Kuratierte Forever- & Lua-API-Datensaetze aktualisiert unter: {dataRoot}");
}

static void CopyIfExists(string src, string dst)
{
    if (File.Exists(src))
    {
        File.Copy(src, dst, overwrite: true);
    }
}

static string ResolveWowForeverDirectory(string? configuredPath)
{
    if (!string.IsNullOrWhiteSpace(configuredPath) && Directory.Exists(configuredPath))
    {
        return Path.GetFullPath(configuredPath);
    }

    if (OperatingSystem.IsWindows())
    {
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            string root = drive.RootDirectory.FullName;
            string[] candidates =
            [
                Path.Combine(root, "Program Files (x86)", "World of Warcraft", "_forever_"),
                Path.Combine(root, "Program Files", "World of Warcraft", "_forever_"),
                Path.Combine(root, "Games", "World of Warcraft", "_forever_"),
                Path.Combine(root, "World of Warcraft", "_forever_")
            ];

            foreach (string candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
    }
    else if (OperatingSystem.IsMacOS())
    {
        const string macCandidate = "/Applications/World of Warcraft/_forever_";
        if (Directory.Exists(macCandidate))
        {
            return macCandidate;
        }
    }

    throw new DirectoryNotFoundException(
        "WoW Classic Forever ('_forever_') Verzeichnis nicht gefunden. Bitte '--wow-dir <Pfad>' oder 'WOW_FOREVER_DIR' angeben.");
}

static string ResolveLatestSqlitePath(string exportDir) =>
    Directory.Exists(exportDir)
        ? Directory.EnumerateFiles(exportDir, "*.sqlite")
            .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
            .FirstOrDefault()
          ?? throw new FileNotFoundException($"Keine .sqlite Datei in {exportDir} gefunden.")
        : throw new DirectoryNotFoundException($"Export-Verzeichnis existiert nicht: {exportDir}");

static string ResolveWorkspaceRoot()
{
    string dir = Directory.GetCurrentDirectory();
    for (int depth = 0; depth < 6; depth++)
    {
        if (File.Exists(Path.Combine(dir, "GrunzForever.slnx")) ||
            File.Exists(Path.Combine(dir, "GrunzForever.sln")))
        {
            return dir;
        }

        string? parent = Directory.GetParent(dir)?.FullName;
        if (string.IsNullOrEmpty(parent))
        {
            break;
        }
        dir = parent;
    }

    return Directory.GetCurrentDirectory();
}
