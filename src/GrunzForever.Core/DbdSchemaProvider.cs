using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace GrunzForever.Core;

public enum DbdBaseKind
{
    Int,
    Float,
    String
}

public sealed record DbdManifestEntry(
    string TableName,
    uint TableHash,
    int? Db2FileDataId);

public sealed record DbdColumnLayout(
    string Name,
    DbdBaseKind BaseKind,
    int BitWidth,
    bool IsUnsigned,
    int ArrayLength,
    bool IsId,
    bool IsNonInline,
    bool IsRelation,
    string? ForeignTable);

public sealed class DbdTableSchema
{
    public required string TableName { get; init; }
    public required uint TableHash { get; init; }
    public required int? FileDataId { get; init; }
    public required uint MatchedLayoutHash { get; init; }
    public required IReadOnlyList<DbdColumnLayout> Columns { get; init; }

    public IReadOnlyList<DbdColumnLayout> InlineColumns =>
        Columns.Where(c => !c.IsNonInline).ToList();

    public DbdColumnLayout? NonInlineRelationColumn =>
        Columns.FirstOrDefault(c => c.IsNonInline && c.IsRelation);
}

/// <summary>
/// Loads and caches official WoWDBDefs manifest.json and .dbd definitions,
/// matching exact WDC5 LayoutHash or WoW build numbers (e.g. 1.60.1.70685).
/// </summary>
public sealed class DbdSchemaProvider : IDisposable
{
    private const string BaseRawUrl = "https://raw.githubusercontent.com/wowdev/WoWDBDefs/master";
    private readonly string _cacheDir;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly Dictionary<string, DbdManifestEntry> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, DbdManifestEntry> _byHash = [];

    public IReadOnlyCollection<DbdManifestEntry> AllTables => _byName.Values;

    public DbdSchemaProvider(string cacheDirectory)
    {
        _cacheDir = cacheDirectory;
        Directory.CreateDirectory(_cacheDir);
        Directory.CreateDirectory(Path.Combine(_cacheDir, "definitions"));
    }

    public async Task InitializeManifestAsync(CancellationToken ct = default)
    {
        string manifestCachePath = Path.Combine(_cacheDir, "manifest.json");
        string json;

        if (File.Exists(manifestCachePath) && new FileInfo(manifestCachePath).Length > 1024)
        {
            json = await File.ReadAllTextAsync(manifestCachePath, ct);
        }
        else
        {
            json = await _http.GetStringAsync($"{BaseRawUrl}/manifest.json", ct);
            await File.WriteAllTextAsync(manifestCachePath, json, ct);
        }

        using var doc = JsonDocument.Parse(json);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            string? tableName = el.TryGetProperty("tableName", out var tn) ? tn.GetString() : null;
            string? tableHashHex = el.TryGetProperty("tableHash", out var th) ? th.GetString() : null;
            if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(tableHashHex))
            {
                continue;
            }

            if (!uint.TryParse(tableHashHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hash))
            {
                continue;
            }

            int? fdid = null;
            if (el.TryGetProperty("db2FileDataID", out var fdidProp) && fdidProp.TryGetInt32(out int parsedFdid))
            {
                fdid = parsedFdid;
            }

            var entry = new DbdManifestEntry(tableName, hash, fdid);
            _byName[tableName] = entry;
            _byHash[hash] = entry;
        }
    }

    public bool TryGetByHash(uint tableHash, out DbdManifestEntry entry) =>
        _byHash.TryGetValue(tableHash, out entry!);

    public bool TryGetByName(string tableName, out DbdManifestEntry entry) =>
        _byName.TryGetValue(tableName, out entry!);

    public async Task<DbdTableSchema?> LoadSchemaAsync(
        string tableName,
        uint? exactLayoutHash,
        string targetBuildVersion,
        CancellationToken ct = default)
    {
        if (!_byName.TryGetValue(tableName, out var entry))
        {
            return null;
        }

        string dbdText = await FetchDbdTextAsync(entry.TableName, ct);
        if (string.IsNullOrWhiteSpace(dbdText))
        {
            return null;
        }

        return ParseDbdForLayoutOrBuild(entry, dbdText, exactLayoutHash, targetBuildVersion);
    }

    private async Task<string> FetchDbdTextAsync(string canonicalTableName, CancellationToken ct)
    {
        string localPath = Path.Combine(_cacheDir, "definitions", $"{canonicalTableName}.dbd");
        if (File.Exists(localPath) && new FileInfo(localPath).Length > 10)
        {
            return await File.ReadAllTextAsync(localPath, ct);
        }

        try
        {
            string content = await _http.GetStringAsync($"{BaseRawUrl}/definitions/{canonicalTableName}.dbd", ct);
            await File.WriteAllTextAsync(localPath, content, ct);
            return content;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static DbdTableSchema? ParseDbdForLayoutOrBuild(
        DbdManifestEntry entry,
        string dbdText,
        uint? exactLayoutHash,
        string targetBuildVersion)
    {
        string[] rawLines = dbdText.Split('\n');
        var columnBaseTypes = new Dictionary<string, (DbdBaseKind Kind, string? ForeignTable)>(StringComparer.OrdinalIgnoreCase);

        int lineIdx = 0;
        bool inColumns = false;

        // 1. Parse COLUMNS section
        for (; lineIdx < rawLines.Length; lineIdx++)
        {
            string line = StripComment(rawLines[lineIdx]);
            if (line.Length == 0)
            {
                if (inColumns && columnBaseTypes.Count > 0)
                {
                    lineIdx++;
                    break;
                }
                continue;
            }

            if (line.Equals("COLUMNS", StringComparison.OrdinalIgnoreCase))
            {
                inColumns = true;
                continue;
            }

            if (line.StartsWith("LAYOUT ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("BUILD ", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (!inColumns) continue;

            // Example: int<ItemClass::ClassID> ClassID?
            string[] parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            string rawType = parts[0];
            string colName = parts[^1].TrimEnd('?');
            string? foreignTable = null;

            int ltIdx = rawType.IndexOf('<');
            if (ltIdx > 0)
            {
                int gtIdx = rawType.IndexOf('>', ltIdx);
                if (gtIdx > ltIdx)
                {
                    string fkSpec = rawType[(ltIdx + 1)..gtIdx];
                    int sep = fkSpec.IndexOf("::", StringComparison.Ordinal);
                    foreignTable = sep > 0 ? fkSpec[..sep] : fkSpec;
                }
                rawType = rawType[..ltIdx];
            }

            DbdBaseKind baseKind = rawType.ToLowerInvariant() switch
            {
                "float" => DbdBaseKind.Float,
                "string" or "locstring" => DbdBaseKind.String,
                _ => DbdBaseKind.Int
            };

            columnBaseTypes[colName] = (baseKind, foreignTable);
        }

        // 2. Parse LAYOUT / BUILD blocks
        var blocks = new List<(HashSet<uint> Layouts, List<string> BuildSpecs, List<string> FieldLines)>();
        HashSet<uint> curLayouts = [];
        List<string> curBuilds = [];
        List<string> curFields = [];

        void FlushBlock()
        {
            if (curFields.Count > 0)
            {
                blocks.Add((curLayouts, curBuilds, curFields));
            }
            curLayouts = [];
            curBuilds = [];
            curFields = [];
        }

        for (; lineIdx < rawLines.Length; lineIdx++)
        {
            string line = StripComment(rawLines[lineIdx]);
            if (line.Length == 0)
            {
                FlushBlock();
                continue;
            }

            if (line.StartsWith("COMMENT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith("LAYOUT ", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string tok in line[7..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (uint.TryParse(tok, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint lh))
                    {
                        curLayouts.Add(lh);
                    }
                }
                continue;
            }

            if (line.StartsWith("BUILD ", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string tok in line[6..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    curBuilds.Add(tok);
                }
                continue;
            }

            curFields.Add(line);
        }
        FlushBlock();

        if (blocks.Count == 0)
        {
            return null;
        }

        // Priority 1: Exact LayoutHash match
        (HashSet<uint> Layouts, List<string> BuildSpecs, List<string> FieldLines)? chosen = null;
        uint matchedHash = exactLayoutHash ?? 0;

        if (exactLayoutHash.HasValue)
        {
            var found = blocks.FirstOrDefault(b => b.Layouts.Contains(exactLayoutHash.Value));
            if (found.FieldLines != null)
            {
                chosen = found;
            }
        }

        // Priority 2: Exact or range Build match (e.g. 1.60.1.70685)
        if (chosen == null && TryParseWowVersion(targetBuildVersion, out var targetVer))
        {
            foreach (var b in blocks)
            {
                if (b.BuildSpecs.Any(spec => MatchesBuildSpec(spec, targetVer)))
                {
                    chosen = b;
                    matchedHash = b.Layouts.FirstOrDefault();
                    break;
                }
            }

            // Priority 3: Same branch (1.60.* or closest build number)
            if (chosen == null)
            {
                chosen = blocks
                    .Select(b => (Block: b, BestDiff: GetMinBuildNumberDistance(b.BuildSpecs, targetVer)))
                    .Where(x => x.BestDiff < int.MaxValue)
                    .OrderBy(x => x.BestDiff)
                    .Select(x => ((HashSet<uint>, List<string>, List<string>)? )x.Block)
                    .FirstOrDefault();

                if (chosen.HasValue)
                {
                    matchedHash = chosen.Value.Item1.FirstOrDefault();
                }
            }
        }

        chosen ??= blocks[^1];

        var parsedColumns = new List<DbdColumnLayout>(chosen.Value.FieldLines.Count);
        foreach (string fieldLine in chosen.Value.FieldLines)
        {
            var col = ParseFieldLine(fieldLine, columnBaseTypes);
            if (col != null)
            {
                parsedColumns.Add(col);
            }
        }

        // Ensure an ID column metadata exists if implicit
        return new DbdTableSchema
        {
            TableName = entry.TableName,
            TableHash = entry.TableHash,
            FileDataId = entry.Db2FileDataId,
            MatchedLayoutHash = matchedHash,
            Columns = parsedColumns
        };
    }

    private static DbdColumnLayout? ParseFieldLine(
        string line,
        Dictionary<string, (DbdBaseKind Kind, string? ForeignTable)> columnBaseTypes)
    {
        // Format: [$annotations$]ColumnName[<typeBits>][[arrayLen]]
        string rest = line.Trim();
        bool isId = false;
        bool isNonInline = false;
        bool isRelation = false;

        if (rest.StartsWith('$'))
        {
            int secondDollar = rest.IndexOf('$', 1);
            if (secondDollar > 0)
            {
                string annotations = rest[1..secondDollar];
                foreach (string ann in annotations.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (ann.Equals("id", StringComparison.OrdinalIgnoreCase)) isId = true;
                    else if (ann.Equals("noninline", StringComparison.OrdinalIgnoreCase)) isNonInline = true;
                    else if (ann.Equals("relation", StringComparison.OrdinalIgnoreCase)) isRelation = true;
                }
                rest = rest[(secondDollar + 1)..].Trim();
            }
        }

        int arrayLen = 1;
        int brOpen = rest.IndexOf('[');
        if (brOpen > 0)
        {
            int brClose = rest.IndexOf(']', brOpen);
            if (brClose > brOpen && int.TryParse(rest[(brOpen + 1)..brClose], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedArr) && parsedArr > 0)
            {
                arrayLen = parsedArr;
            }
            rest = rest[..brOpen];
        }

        bool isUnsigned = false;
        int bitWidth = 32;
        bool hasExplicitWidth = false;

        int lt = rest.IndexOf('<');
        if (lt > 0)
        {
            int gt = rest.IndexOf('>', lt);
            if (gt > lt)
            {
                string widthSpec = rest[(lt + 1)..gt].Trim();
                if (widthSpec.StartsWith('u') || widthSpec.StartsWith('U'))
                {
                    isUnsigned = true;
                    widthSpec = widthSpec[1..];
                }
                if (int.TryParse(widthSpec, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedBits))
                {
                    bitWidth = parsedBits;
                    hasExplicitWidth = true;
                }
            }
            rest = rest[..lt];
        }

        string colName = rest.Trim();
        if (colName.Length == 0)
        {
            return null;
        }

        var (baseKind, foreignTable) = columnBaseTypes.TryGetValue(colName, out var found)
            ? found
            : (hasExplicitWidth ? DbdBaseKind.Int : DbdBaseKind.String, null);

        if (baseKind == DbdBaseKind.Float)
        {
            bitWidth = 32;
        }

        return new DbdColumnLayout(
            Name: colName,
            BaseKind: baseKind,
            BitWidth: bitWidth,
            IsUnsigned: isUnsigned,
            ArrayLength: arrayLen,
            IsId: isId,
            IsNonInline: isNonInline,
            IsRelation: isRelation,
            ForeignTable: foreignTable);
    }

    private static string StripComment(string raw)
    {
        int commentIdx = raw.IndexOf("//", StringComparison.Ordinal);
        if (commentIdx >= 0)
        {
            raw = raw[..commentIdx];
        }
        return raw.Trim();
    }

    private readonly record struct WowVer(int Major, int Minor, int Patch, int Build);

    private static bool TryParseWowVersion(string text, out WowVer ver)
    {
        ver = default;
        string[] p = text.Trim().Split('.');
        if (p.Length != 4) return false;
        if (!int.TryParse(p[0], out int maj) ||
            !int.TryParse(p[1], out int min) ||
            !int.TryParse(p[2], out int pat) ||
            !int.TryParse(p[3], out int bld))
        {
            return false;
        }
        ver = new WowVer(maj, min, pat, bld);
        return true;
    }

    private static bool MatchesBuildSpec(string spec, WowVer target)
    {
        int dash = spec.IndexOf('-');
        if (dash > 0)
        {
            if (TryParseWowVersion(spec[..dash], out var start) &&
                TryParseWowVersion(spec[(dash + 1)..], out var end))
            {
                return start.Major == target.Major &&
                       start.Minor == target.Minor &&
                       target.Build >= start.Build &&
                       target.Build <= end.Build;
            }
            return false;
        }

        return TryParseWowVersion(spec, out var single) &&
               single.Major == target.Major &&
               single.Minor == target.Minor &&
               single.Patch == target.Patch &&
               single.Build == target.Build;
    }

    private static int GetMinBuildNumberDistance(List<string> specs, WowVer target)
    {
        int best = int.MaxValue;
        foreach (string spec in specs)
        {
            string firstPart = spec.Contains('-') ? spec[..spec.IndexOf('-')] : spec;
            if (TryParseWowVersion(firstPart, out var v))
            {
                int branchPenalty = (v.Major == target.Major && v.Minor == target.Minor) ? 0 : 1_000_000;
                int dist = branchPenalty + Math.Abs(v.Build - target.Build);
                if (dist < best) best = dist;
            }
        }
        return best;
    }

    public void Dispose() => _http.Dispose();
}
