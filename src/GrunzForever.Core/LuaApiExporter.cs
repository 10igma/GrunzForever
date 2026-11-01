using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GrunzForever.Core;

public sealed record LuaParamDoc(string Name, string Type, bool Nilable, string? DefaultValue);

public sealed record LuaFunctionDoc(
    string Name,
    string FullName,
    string Namespace,
    string SourceFile,
    IReadOnlyList<LuaParamDoc> Arguments,
    IReadOnlyList<LuaParamDoc> Returns,
    IReadOnlyDictionary<string, string> Attributes,
    string? Documentation);

public sealed record LuaEventDoc(
    string Name,
    string LiteralName,
    string Namespace,
    string SourceFile,
    bool Synchronous,
    IReadOnlyList<LuaParamDoc> Payload,
    IReadOnlyDictionary<string, string> Attributes,
    string? Documentation);

public sealed record LuaTableFieldDoc(string Name, string Type, string? EnumOrConstValue, bool Nilable);

public sealed record LuaTableDoc(
    string Name,
    string Kind,
    string Namespace,
    string SourceFile,
    IReadOnlyList<LuaTableFieldDoc> Fields);

public sealed record LuaSystemDoc(
    string Name,
    string Namespace,
    string Environment,
    string SourceFile,
    int FileDataId,
    IReadOnlyList<LuaFunctionDoc> Functions,
    IReadOnlyList<LuaEventDoc> Events,
    IReadOnlyList<LuaTableDoc> Tables);

public sealed record BlizzardAddonManifestInfo(
    string AddonName,
    int TocFileDataId,
    string TocPath,
    string? AllowLoad,
    string? AllowLoadGameType,
    bool LoadsInCamelot,
    bool IsLoadOnDemand,
    int CamelotOverrideFileCount);

public sealed class LuaApiExportSummary
{
    public required string BuildVersion { get; init; }
    public required int ProjectId { get; init; }
    public required string ProjectCodeName { get; init; }
    public required int DocumentationFileCount { get; init; }
    public required int SystemCount { get; init; }
    public required int DistinctNamespaceCount { get; init; }
    public required int TotalFunctionCount { get; init; }
    public required int GlobalFunctionCount { get; init; }
    public required int ProtectedOrRestrictedFunctionCount { get; init; }
    public required int SecretGuardedFunctionCount { get; init; }
    public required int TotalEventCount { get; init; }
    public required int TotalTableCount { get; init; }
    public required int BlizzardAddonCount { get; init; }
    public required int ActiveCamelotAddonCount { get; init; }
    public required int CamelotSpecificFileCount { get; init; }
    public required string OutputDirectory { get; init; }
    public required IReadOnlyList<string> WrittenFiles { get; init; }
}

/// <summary>
/// Extracts and parses WoW Classic Forever ("Camelot", WOW_PROJECT_CAMELOT = 18) Lua API documentation,
/// C_* namespaces, global functions, events, enums/structures, restriction/secret predicates, and
/// Blizzard UI TOC/Camelot overrides directly from local CASC storage.
/// </summary>
public static class LuaApiExporter
{
    private const string InterfaceListfileUrl = "https://raw.githubusercontent.com/wowdev/wow-listfile/master/parts/interface.csv";

    private static readonly JsonWriterOptions JsonOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task<LuaApiExportSummary> ExportFromCascAsync(
        string wowFlavorDirectory,
        string cacheRootDirectory,
        string exportRootDirectory,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        void Log(string msg) => log?.Invoke(msg);

        using var casc = CascStorage.Open(wowFlavorDirectory);
        var root = RootManifest.Parse(casc.LoadRootManifestBytes());

        string listfileCachePath = Path.Combine(cacheRootDirectory, "WoWListfile", "interface.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(listfileCachePath)!);

        string csvText;
        if (File.Exists(listfileCachePath) && new FileInfo(listfileCachePath).Length > 100_000)
        {
            csvText = await File.ReadAllTextAsync(listfileCachePath, ct);
        }
        else
        {
            Log("      Lade wow-listfile (parts/interface.csv) in lokalen Cache ...");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            csvText = await http.GetStringAsync(InterfaceListfileUrl, ct);
            await File.WriteAllTextAsync(listfileCachePath, csvText, Encoding.UTF8, ct);
        }

        var pathByFdid = new Dictionary<int, string>(160_000);
        foreach (string rawLine in csvText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int semi = rawLine.IndexOf(';');
            if (semi <= 0 || !int.TryParse(rawLine.AsSpan(0, semi), out int fdid)) continue;
            if (root.TryGetContentKey(fdid, out _))
            {
                pathByFdid[fdid] = rawLine[(semi + 1)..].Trim().Replace('\\', '/');
            }
        }

        // 1. Parse ProjectConstants (Camelot)
        int projectId = 18;
        string projectCodeName = "Camelot";
        var projConstEntry = pathByFdid.FirstOrDefault(x =>
            x.Value.EndsWith("Blizzard_ProjectConstants/Camelot/ProjectConstants.lua", StringComparison.OrdinalIgnoreCase));
        if (projConstEntry.Key != 0 && root.TryGetContentKey(projConstEntry.Key, out var projCk))
        {
            byte[]? pb = casc.ReadFileByCKey(projCk);
            if (pb != null)
            {
                string pt = Encoding.UTF8.GetString(pb);
                var m = Regex.Match(pt, @"WOW_PROJECT_([A-Z0-9_]+)\s*=\s*(\d+)");
                if (m.Success)
                {
                    projectCodeName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(m.Groups[1].Value.ToLowerInvariant());
                    projectId = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                }
            }
        }

        // 2. Parse all Blizzard_APIDocumentationGenerated/*.lua files
        var docEntries = pathByFdid
            .Where(kv => kv.Value.Contains("Blizzard_APIDocumentationGenerated/", StringComparison.OrdinalIgnoreCase) &&
                         kv.Value.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var systems = new List<LuaSystemDoc>(docEntries.Count);
        foreach (var (fdid, path) in docEntries)
        {
            ct.ThrowIfCancellationRequested();
            if (!root.TryGetContentKey(fdid, out var ck)) continue;
            byte[]? raw = casc.ReadFileByCKey(ck);
            if (raw == null || raw.Length == 0) continue;

            string luaText = Encoding.UTF8.GetString(raw);
            var parsed = ParseDocumentationLua(luaText, Path.GetFileName(path), fdid);
            if (parsed.Functions.Count > 0 || parsed.Events.Count > 0 || parsed.Tables.Count > 0)
            {
                systems.Add(parsed);
            }
        }

        // 3. Inventory Camelot-specific override files & Blizzard AddOn TOCs
        var camelotFiles = pathByFdid
            .Where(kv => (kv.Value.Contains("/Camelot/", StringComparison.OrdinalIgnoreCase) ||
                          kv.Value.Contains("_Camelot.", StringComparison.OrdinalIgnoreCase)) &&
                         (kv.Value.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ||
                          kv.Value.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var camelotCountByAddon = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, path) in camelotFiles)
        {
            string? addon = ExtractBlizzardAddonFolderName(path);
            if (addon != null)
            {
                camelotCountByAddon[addon] = camelotCountByAddon.GetValueOrDefault(addon) + 1;
            }
        }

        var addonManifests = new List<BlizzardAddonManifestInfo>();
        foreach (var (fdid, path) in pathByFdid
                     .Where(kv => kv.Value.StartsWith("Interface/AddOns/", StringComparison.OrdinalIgnoreCase) &&
                                  kv.Value.EndsWith(".toc", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase))
        {
            if (!root.TryGetContentKey(fdid, out var ck)) continue;
            byte[]? raw = casc.ReadFileByCKey(ck);
            if (raw == null) continue;

            string toc = Encoding.UTF8.GetString(raw);
            string addonName = ExtractBlizzardAddonFolderName(path) ?? Path.GetFileNameWithoutExtension(path);
            string? allowLoad = ExtractTocTag(toc, "AllowLoad");
            string? allowGameType = ExtractTocTag(toc, "AllowLoadGameType");
            string? lod = ExtractTocTag(toc, "LoadOnDemand");
            bool isLod = lod != null && (lod == "1" || lod.Equals("true", StringComparison.OrdinalIgnoreCase));

            // In Blizzard_Game.toc, Camelot has Family = Mainline (mainline) and Game = Camelot (camelot)
            bool loadsInCamelot = EvaluateCamelotGameTypeMatch(allowGameType);
            int camOverrides = camelotCountByAddon.GetValueOrDefault(addonName);

            addonManifests.Add(new BlizzardAddonManifestInfo(
                AddonName: addonName,
                TocFileDataId: fdid,
                TocPath: path,
                AllowLoad: allowLoad,
                AllowLoadGameType: allowGameType,
                LoadsInCamelot: loadsInCamelot,
                IsLoadOnDemand: isLod,
                CamelotOverrideFileCount: camOverrides));
        }

        // 4. Write JSON & Markdown outputs
        string outDir = Path.Combine(exportRootDirectory, "lua_api");
        Directory.CreateDirectory(outDir);

        string completeJsonPath = Path.Combine(outDir, "LuaApi_Complete.json");
        string camelotJsonPath = Path.Combine(outDir, "LuaApi_CamelotSurface.json");
        string referenceMdPath = Path.Combine(outDir, "LuaApi_Reference.md");

        WriteCompleteApiJson(completeJsonPath, casc.Build, projectId, projectCodeName, systems);
        WriteCamelotSurfaceJson(camelotJsonPath, casc.Build, projectId, projectCodeName, addonManifests, camelotFiles);
        WriteMarkdownReference(referenceMdPath, casc.Build, projectId, projectCodeName, systems, addonManifests, camelotFiles);

        var allFuncs = systems.SelectMany(s => s.Functions).ToList();
        var allEvents = systems.SelectMany(s => s.Events).ToList();
        var allTables = systems.SelectMany(s => s.Tables).ToList();
        int distinctNs = systems.Select(s => s.Namespace).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        int protectedOrRestricted = allFuncs.Count(f =>
            f.Attributes.ContainsKey("IsProtectedFunction") ||
            f.Attributes.ContainsKey("HasRestrictions") ||
            f.Attributes.Keys.Any(k => k.StartsWith("Requires", StringComparison.Ordinal)));

        int secretGuarded = allFuncs.Count(f =>
            f.Attributes.Keys.Any(k => k.StartsWith("Secret", StringComparison.Ordinal) && k != "SecretArguments"));

        return new LuaApiExportSummary
        {
            BuildVersion = casc.Build.Version,
            ProjectId = projectId,
            ProjectCodeName = projectCodeName,
            DocumentationFileCount = docEntries.Count,
            SystemCount = systems.Count,
            DistinctNamespaceCount = distinctNs,
            TotalFunctionCount = allFuncs.Count,
            GlobalFunctionCount = allFuncs.Count(f => string.IsNullOrEmpty(f.Namespace)),
            ProtectedOrRestrictedFunctionCount = protectedOrRestricted,
            SecretGuardedFunctionCount = secretGuarded,
            TotalEventCount = allEvents.Count,
            TotalTableCount = allTables.Count,
            BlizzardAddonCount = addonManifests.Count,
            ActiveCamelotAddonCount = addonManifests.Count(a => a.LoadsInCamelot),
            CamelotSpecificFileCount = camelotFiles.Count,
            OutputDirectory = outDir,
            WrittenFiles = [completeJsonPath, camelotJsonPath, referenceMdPath]
        };
    }

    public static LuaSystemDoc ParseDocumentationLua(string luaText, string sourceFileName, int fileDataId)
    {
        string sysName = Path.GetFileNameWithoutExtension(sourceFileName);
        string sysNamespace = string.Empty;
        string sysEnv = "All";

        var functions = new List<LuaFunctionDoc>();
        var events = new List<LuaEventDoc>();
        var tables = new List<LuaTableDoc>();

        int depth = 0;
        string currentSection = string.Empty; // "Functions", "Events", "Tables"
        string currentSubSection = string.Empty; // "Arguments", "Returns", "Payload", "Fields", "Documentation"

        // Entry-level state (depth 3)
        var entryProps = new Dictionary<string, string>(StringComparer.Ordinal);
        var entryArgs = new List<LuaParamDoc>();
        var entryReturns = new List<LuaParamDoc>();
        var entryFields = new List<LuaTableFieldDoc>();
        var entryDocs = new List<string>();

        void ResetEntry()
        {
            entryProps.Clear();
            entryArgs.Clear();
            entryReturns.Clear();
            entryFields.Clear();
            entryDocs.Clear();
            currentSubSection = string.Empty;
        }

        void FlushEntry()
        {
            if (!entryProps.TryGetValue("Name", out string? itemName) || string.IsNullOrEmpty(itemName))
            {
                return;
            }

            string itemType = entryProps.GetValueOrDefault("Type", "");
            string effectiveNs = entryProps.TryGetValue("Namespace", out string? overrideNs)
                ? overrideNs
                : sysNamespace;

            var attrs = entryProps
                .Where(kv => kv.Key is not ("Name" or "Type" or "Namespace" or "LiteralName" or "SynchronousEvent"))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

            string? docStr = entryDocs.Count > 0 ? string.Join(" ", entryDocs) : null;

            if (currentSection == "Functions" || itemType == "Function")
            {
                string fullName = string.IsNullOrEmpty(effectiveNs) ? itemName : $"{effectiveNs}.{itemName}";
                functions.Add(new LuaFunctionDoc(
                    Name: itemName,
                    FullName: fullName,
                    Namespace: effectiveNs,
                    SourceFile: sourceFileName,
                    Arguments: entryArgs.ToList(),
                    Returns: entryReturns.ToList(),
                    Attributes: attrs,
                    Documentation: docStr));
            }
            else if (currentSection == "Events" || itemType == "Event")
            {
                string lit = entryProps.GetValueOrDefault("LiteralName", itemName);
                bool sync = entryProps.GetValueOrDefault("SynchronousEvent", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
                events.Add(new LuaEventDoc(
                    Name: itemName,
                    LiteralName: lit,
                    Namespace: effectiveNs,
                    SourceFile: sourceFileName,
                    Synchronous: sync,
                    Payload: entryArgs.ToList(),
                    Attributes: attrs,
                    Documentation: docStr));
            }
            else if (currentSection == "Tables" || itemType is "Enumeration" or "Structure" or "Constants" or "CallbackType")
            {
                tables.Add(new LuaTableDoc(
                    Name: itemName,
                    Kind: string.IsNullOrEmpty(itemType) ? "Table" : itemType,
                    Namespace: effectiveNs,
                    SourceFile: sourceFileName,
                    Fields: entryFields.ToList()));
            }
        }

        foreach (string rawLine in luaText.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            int deltaBraces = CountBracesOutsideStrings(line);

            if (depth == 1)
            {
                if (TryParseKeyValue(line, out string k, out string v))
                {
                    if (k == "Name") sysName = Unquote(v);
                    else if (k == "Namespace") sysNamespace = Unquote(v);
                    else if (k == "Environment") sysEnv = Unquote(v);
                    else if (v.Length == 0 && k is "Functions" or "Events" or "Tables" or "Predicates")
                    {
                        currentSection = k;
                    }
                }
                else if (line.EndsWith('='))
                {
                    string sec = line[..^1].Trim();
                    if (sec is "Functions" or "Events" or "Tables" or "Predicates")
                    {
                        currentSection = sec;
                    }
                }
            }
            else if (depth == 2 && line == "{")
            {
                ResetEntry();
            }
            else if (depth == 3)
            {
                if (line.StartsWith("Documentation =", StringComparison.Ordinal) && line.Contains('{'))
                {
                    ExtractQuotedStringsInto(line, entryDocs);
                }
                else if (line.EndsWith('='))
                {
                    currentSubSection = line[..^1].Trim();
                }
                else if (TryParseKeyValue(line, out string k, out string v))
                {
                    if (v.Length == 0)
                    {
                        currentSubSection = k;
                    }
                    else
                    {
                        entryProps[k] = Unquote(v);
                    }
                }
            }
            else if (depth == 4)
            {
                if (currentSubSection == "Documentation")
                {
                    ExtractQuotedStringsInto(line, entryDocs);
                }
                else if (line.StartsWith('{') && line.Contains("Name =", StringComparison.Ordinal))
                {
                    var inlineMap = ParseInlineLuaRecord(line);
                    string pName = inlineMap.GetValueOrDefault("Name", "");
                    string pType = inlineMap.GetValueOrDefault("Type", "any");
                    bool nilable = inlineMap.GetValueOrDefault("Nilable", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
                    string? defVal = inlineMap.GetValueOrDefault("Default");

                    if (currentSubSection is "Arguments" or "Payload")
                    {
                        entryArgs.Add(new LuaParamDoc(pName, pType, nilable, defVal));
                    }
                    else if (currentSubSection == "Returns")
                    {
                        entryReturns.Add(new LuaParamDoc(pName, pType, nilable, defVal));
                    }
                    else if (currentSubSection == "Fields")
                    {
                        string? enumVal = inlineMap.GetValueOrDefault("EnumValue") ?? inlineMap.GetValueOrDefault("Value");
                        entryFields.Add(new LuaTableFieldDoc(pName, pType, enumVal, nilable));
                    }
                }
            }

            int nextDepth = depth + deltaBraces;
            if (depth == 3 && nextDepth == 2)
            {
                FlushEntry();
                ResetEntry();
            }
            else if (depth == 4 && nextDepth == 3)
            {
                currentSubSection = string.Empty;
            }

            depth = Math.Max(0, nextDepth);
        }

        return new LuaSystemDoc(
            Name: sysName,
            Namespace: sysNamespace,
            Environment: sysEnv,
            SourceFile: sourceFileName,
            FileDataId: fileDataId,
            Functions: functions,
            Events: events,
            Tables: tables);
    }

    private static bool EvaluateCamelotGameTypeMatch(string? allowLoadGameType)
    {
        if (string.IsNullOrWhiteSpace(allowLoadGameType))
        {
            return true;
        }

        foreach (string token in allowLoadGameType.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Equals("camelot", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("mainline", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ExtractBlizzardAddonFolderName(string normalizedPath)
    {
        const string prefix = "Interface/AddOns/";
        if (!normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string rem = normalizedPath[prefix.Length..];
        int slash = rem.IndexOf('/');
        return slash > 0 ? rem[..slash] : null;
    }

    private static string? ExtractTocTag(string tocContent, string tagName)
    {
        string prefix = $"## {tagName}:";
        foreach (string rawLine in tocContent.Split('\n'))
        {
            string l = rawLine.Trim();
            if (l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return l[prefix.Length..].Trim();
            }
        }
        return null;
    }

    private static int CountBracesOutsideStrings(string line)
    {
        int delta = 0;
        bool inStr = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (!inStr && c == '-' && i + 1 < line.Length && line[i + 1] == '-')
            {
                break;
            }
            if (c == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                inStr = !inStr;
                continue;
            }
            if (!inStr)
            {
                if (c == '{') delta++;
                else if (c == '}') delta--;
            }
        }
        return delta;
    }

    private static bool TryParseKeyValue(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;
        int eq = line.IndexOf('=');
        if (eq <= 0) return false;

        string k = line[..eq].Trim();
        if (k.Length == 0 || !k.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            return false;
        }

        string v = line[(eq + 1)..].Trim().TrimEnd(',').Trim();
        key = k;
        value = v;
        return true;
    }

    private static Dictionary<string, string> ParseInlineLuaRecord(string line)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(line, @"([A-Za-z0-9_]+)\s*=\s*(""[^""]*""|[^,}\s]+)"))
        {
            map[m.Groups[1].Value] = Unquote(m.Groups[2].Value);
        }
        return map;
    }

    private static void ExtractQuotedStringsInto(string line, List<string> target)
    {
        foreach (Match m in Regex.Matches(line, @"""((?:\\.|[^""\\])*)"""))
        {
            string val = m.Groups[1].Value.Trim();
            if (val.Length > 0)
            {
                target.Add(val);
            }
        }
    }

    private static string Unquote(string s) =>
        s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;

    private static void WriteCompleteApiJson(
        string outputPath,
        WowBuildMetadata build,
        int projectId,
        string projectCodeName,
        IReadOnlyList<LuaSystemDoc> systems)
    {
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536);
        using var w = new Utf8JsonWriter(fs, JsonOptions);

        w.WriteStartObject();
        w.WriteString("Product", build.Product);
        w.WriteString("Version", build.Version);
        w.WriteString("BuildName", build.BuildName);
        w.WriteNumber("ProjectId", projectId);
        w.WriteString("ProjectCodeName", projectCodeName);
        w.WriteNumber("TotalSystems", systems.Count);
        w.WriteNumber("TotalFunctions", systems.Sum(s => s.Functions.Count));
        w.WriteNumber("TotalEvents", systems.Sum(s => s.Events.Count));
        w.WriteNumber("TotalTables", systems.Sum(s => s.Tables.Count));

        w.WriteStartArray("Systems");
        foreach (var sys in systems)
        {
            w.WriteStartObject();
            w.WriteString("Name", sys.Name);
            w.WriteString("Namespace", sys.Namespace);
            w.WriteString("Environment", sys.Environment);
            w.WriteString("SourceFile", sys.SourceFile);
            w.WriteNumber("FileDataId", sys.FileDataId);

            w.WriteStartArray("Functions");
            foreach (var fn in sys.Functions)
            {
                w.WriteStartObject();
                w.WriteString("Name", fn.Name);
                w.WriteString("FullName", fn.FullName);
                if (fn.Documentation != null) w.WriteString("Documentation", fn.Documentation);
                if (fn.Attributes.Count > 0)
                {
                    w.WriteStartObject("Attributes");
                    foreach (var kv in fn.Attributes) w.WriteString(kv.Key, kv.Value);
                    w.WriteEndObject();
                }
                w.WriteStartArray("Arguments");
                foreach (var a in fn.Arguments)
                {
                    w.WriteStartObject();
                    w.WriteString("Name", a.Name);
                    w.WriteString("Type", a.Type);
                    w.WriteBoolean("Nilable", a.Nilable);
                    if (a.DefaultValue != null) w.WriteString("Default", a.DefaultValue);
                    w.WriteEndObject();
                }
                w.WriteEndArray();

                w.WriteStartArray("Returns");
                foreach (var r in fn.Returns)
                {
                    w.WriteStartObject();
                    w.WriteString("Name", r.Name);
                    w.WriteString("Type", r.Type);
                    w.WriteBoolean("Nilable", r.Nilable);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("Events");
            foreach (var ev in sys.Events)
            {
                w.WriteStartObject();
                w.WriteString("Name", ev.Name);
                w.WriteString("LiteralName", ev.LiteralName);
                w.WriteBoolean("Synchronous", ev.Synchronous);
                if (ev.Documentation != null) w.WriteString("Documentation", ev.Documentation);
                w.WriteStartArray("Payload");
                foreach (var p in ev.Payload)
                {
                    w.WriteStartObject();
                    w.WriteString("Name", p.Name);
                    w.WriteString("Type", p.Type);
                    w.WriteBoolean("Nilable", p.Nilable);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("Tables");
            foreach (var tbl in sys.Tables)
            {
                w.WriteStartObject();
                w.WriteString("Name", tbl.Name);
                w.WriteString("Kind", tbl.Kind);
                w.WriteStartArray("Fields");
                foreach (var f in tbl.Fields)
                {
                    w.WriteStartObject();
                    w.WriteString("Name", f.Name);
                    w.WriteString("Type", f.Type);
                    if (f.EnumOrConstValue != null) w.WriteString("Value", f.EnumOrConstValue);
                    w.WriteBoolean("Nilable", f.Nilable);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void WriteCamelotSurfaceJson(
        string outputPath,
        WowBuildMetadata build,
        int projectId,
        string projectCodeName,
        IReadOnlyList<BlizzardAddonManifestInfo> addons,
        IReadOnlyList<KeyValuePair<int, string>> camelotFiles)
    {
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536);
        using var w = new Utf8JsonWriter(fs, JsonOptions);

        w.WriteStartObject();
        w.WriteString("Product", build.Product);
        w.WriteString("Version", build.Version);
        w.WriteNumber("ProjectId", projectId);
        w.WriteString("ProjectCodeName", projectCodeName);
        w.WriteString("UiFamily", "Mainline");
        w.WriteString("GameType", "camelot");
        w.WriteNumber("RecommendedAddonTocInterface", 16001);

        w.WriteStartArray("BlizzardAddOns");
        foreach (var a in addons)
        {
            w.WriteStartObject();
            w.WriteString("AddonName", a.AddonName);
            w.WriteNumber("TocFileDataId", a.TocFileDataId);
            w.WriteString("TocPath", a.TocPath);
            w.WriteBoolean("LoadsInCamelot", a.LoadsInCamelot);
            w.WriteBoolean("LoadOnDemand", a.IsLoadOnDemand);
            if (a.AllowLoad != null) w.WriteString("AllowLoad", a.AllowLoad);
            if (a.AllowLoadGameType != null) w.WriteString("AllowLoadGameType", a.AllowLoadGameType);
            w.WriteNumber("CamelotOverrideFiles", a.CamelotOverrideFileCount);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("CamelotSpecificFiles");
        foreach (var kv in camelotFiles)
        {
            w.WriteStartObject();
            w.WriteNumber("FileDataId", kv.Key);
            w.WriteString("Path", kv.Value);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteEndObject();
    }

    private static void WriteMarkdownReference(
        string outputPath,
        WowBuildMetadata build,
        int projectId,
        string projectCodeName,
        IReadOnlyList<LuaSystemDoc> systems,
        IReadOnlyList<BlizzardAddonManifestInfo> addons,
        IReadOnlyList<KeyValuePair<int, string>> camelotFiles)
    {
        var sb = new StringBuilder(128_000);
        var allFuncs = systems.SelectMany(s => s.Functions).ToList();
        var allEvents = systems.SelectMany(s => s.Events).ToList();
        var allTables = systems.SelectMany(s => s.Tables).ToList();

        sb.AppendLine($"# WoW Classic Forever (`{projectCodeName}`) — Lua API & UI Surface Reference");
        sb.AppendLine();
        sb.AppendLine($"- **Produkt / Build**: `{build.Product}` — `{build.Version}` (`{build.BuildName}`)");
        sb.AppendLine($"- **Projekt-Konstante**: `WOW_PROJECT_{projectCodeName.ToUpperInvariant()} = {projectId}` (`WOW_PROJECT_ID = {projectId}`)");
        sb.AppendLine("- **UI-Architektur**: `[Family] = Mainline` (`mainline`) | `[Game] = Camelot` (`camelot`)");
        sb.AppendLine($"- **Dokumentierte API-Systeme**: **{systems.Count:N0}** (`{systems.Select(s => s.Namespace).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count():N0}` `C_*` Namespaces)");
        sb.AppendLine($"- **Lua-Funktionen gesamt**: **{allFuncs.Count:N0}** (`{allFuncs.Count(f => f.Namespace.Length > 0):N0}` Namespaced + `{allFuncs.Count(f => f.Namespace.Length == 0):N0}` Global)");
        sb.AppendLine($"- **Events gesamt**: **{allEvents.Count:N0}** | **Enums/Structs/Konstanten**: **{allTables.Count:N0}**");
        sb.AppendLine($"- **Blizzard UI-AddOns in Camelot aktiv**: **{addons.Count(a => a.LoadsInCamelot):N0}** von `{addons.Count:N0}` (`{camelotFiles.Count:N0}` Camelot-spezifische Lua/XML-Overrides)");
        sb.AppendLine();

        sb.AppendLine("## 1. Camelot-Exklusive & Hervorgehobene Blizzard UI-Module");
        sb.AppendLine();
        sb.AppendLine("| Blizzard AddOn | AllowLoadGameType | LoadOnDemand | Camelot-Overrides |");
        sb.AppendLine("| :--- | :--- | :---: | ---: |");
        foreach (var a in addons
                     .Where(a => (a.AllowLoadGameType != null && a.AllowLoadGameType.Contains("camelot", StringComparison.OrdinalIgnoreCase)) || a.CamelotOverrideFileCount >= 2)
                     .OrderByDescending(a => a.CamelotOverrideFileCount)
                     .ThenBy(a => a.AddonName))
        {
            sb.AppendLine($"| `{a.AddonName}` | `{a.AllowLoadGameType ?? "(alle / Mainline)"}` | {(a.IsLoadOnDemand ? "Ja" : "Nein")} | {a.CamelotOverrideFileCount} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 2. Sicherheits-, Hardware-Event- & Secret-Value-Modell (Engine 12.0)");
        sb.AppendLine();
        var attrCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in allFuncs)
        {
            foreach (var k in f.Attributes.Keys)
            {
                attrCounts[k] = attrCounts.GetValueOrDefault(k) + 1;
            }
        }
        sb.AppendLine("| Attribut / Guard | Anzahl Funktionen |");
        sb.AppendLine("| :--- | ---: |");
        foreach (var kv in attrCounts.OrderByDescending(x => x.Value).Take(25))
        {
            sb.AppendLine($"| `{kv.Key}` | {kv.Value:N0} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 3. Alle `C_*` Namespaces nach Funktionsumfang");
        sb.AppendLine();
        sb.AppendLine("| Namespace | System-Datei | Funktionen | Events | Enums/Structs |");
        sb.AppendLine("| :--- | :--- | ---: | ---: | ---: |");
        foreach (var grp in systems
                     .Where(s => !string.IsNullOrEmpty(s.Namespace))
                     .GroupBy(s => s.Namespace, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(g => g.Sum(x => x.Functions.Count))
                     .ThenBy(g => g.Key))
        {
            string files = string.Join(", ", grp.Select(x => x.SourceFile).Take(2));
            sb.AppendLine($"| `{grp.Key}` | `{files}` | {grp.Sum(x => x.Functions.Count)} | {grp.Sum(x => x.Events.Count)} | {grp.Sum(x => x.Tables.Count)} |");
        }

        File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
