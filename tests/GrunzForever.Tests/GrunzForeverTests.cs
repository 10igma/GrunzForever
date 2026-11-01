using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GrunzForever.Core;

namespace GrunzForever.Tests;

public sealed class GrunzForeverTests
{
    [Fact]
    public void BlteDecoder_DecodesMultiChunkPlainZlibAndLockedFrames()
    {
        byte[] chunk0Plain = Encoding.UTF8.GetBytes("WDC5-Header-Test!");
        byte[] chunk1Raw = Encoding.UTF8.GetBytes("Uncompressed Zlib Payload Data 1234567890");
        byte[] chunk1Zlib;
        using (var ms = new MemoryStream())
        {
            using (var zs = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                zs.Write(chunk1Raw);
            }
            chunk1Zlib = ms.ToArray();
        }

        int lockedDecompSize = 32;
        byte[] lockedPayload = [0x45, 0x08, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x04, 0xAA, 0xBB, 0xCC, 0xDD, 0x99];

        byte[] chunk0Block = [(byte)'N', .. chunk0Plain];
        byte[] chunk1Block = [(byte)'Z', .. chunk1Zlib];

        int chunkCount = 3;
        int headerSize = 12 + chunkCount * 24;
        byte[] blte = new byte[headerSize + chunk0Block.Length + chunk1Block.Length + lockedPayload.Length];

        Encoding.ASCII.GetBytes("BLTE").CopyTo(blte, 0);
        BinaryPrimitives.WriteUInt32BigEndian(blte.AsSpan(4, 4), (uint)headerSize);
        blte[8] = 0x0F;
        blte[11] = (byte)chunkCount;

        WriteChunkEntry(blte.AsSpan(12, 24), chunk0Block.Length, chunk0Plain.Length);
        WriteChunkEntry(blte.AsSpan(36, 24), chunk1Block.Length, chunk1Raw.Length);
        WriteChunkEntry(blte.AsSpan(60, 24), lockedPayload.Length, lockedDecompSize);

        int off = headerSize;
        chunk0Block.CopyTo(blte, off);
        off += chunk0Block.Length;
        chunk1Block.CopyTo(blte, off);
        off += chunk1Block.Length;
        lockedPayload.CopyTo(blte, off);

        byte[] decoded = BlteDecoder.Decode(blte);

        Assert.Equal(chunk0Plain.Length + chunk1Raw.Length + lockedDecompSize, decoded.Length);
        Assert.Equal("WDC5-Header-Test!", Encoding.UTF8.GetString(decoded, 0, chunk0Plain.Length));
        Assert.Equal("Uncompressed Zlib Payload Data 1234567890", Encoding.UTF8.GetString(decoded, chunk0Plain.Length, chunk1Raw.Length));
        Assert.All(decoded.Skip(chunk0Plain.Length + chunk1Raw.Length), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Wdc5SequentialUnpacker_And_HotfixMerger_WorkAccurately()
    {
        var cols = new List<DbdColumnLayout>
        {
            new("ID", DbdBaseKind.Int, 32, false, 1, IsId: true, IsNonInline: true, IsRelation: false, null),
            new("Display_lang", DbdBaseKind.String, 32, false, 1, IsId: false, IsNonInline: false, IsRelation: false, null),
            new("ItemLevel", DbdBaseKind.Int, 16, true, 1, IsId: false, IsNonInline: false, IsRelation: false, null),
            new("SpeedMod", DbdBaseKind.Float, 32, false, 1, IsId: false, IsNonInline: false, IsRelation: false, null),
            new("Stats", DbdBaseKind.Int, 32, false, 2, IsId: false, IsNonInline: false, IsRelation: false, null)
        };

        var table = new ExtractedTableData
        {
            TableName = "ItemSparse",
            TableHash = 0x919BE54E,
            LayoutHash = 0x6FCC3191,
            Columns = cols,
            BaseDb2RowCount = 2
        };

        table.RowsById[100] = new ExtractedRow
        {
            Id = 100,
            Values = [100, "Old Sword", (ushort)10, 1.0f, new object?[] { 1, 2 }],
            Source = RowSourceKind.Db2
        };
        table.RowsById[101] = new ExtractedRow
        {
            Id = 101,
            Values = [101, "Obsolete Item", (ushort)5, 1.0f, new object?[] { 0, 0 }],
            Source = RowSourceKind.Db2
        };

        byte[] hotfixPayload100 = BuildSequentialPayload("Reforged Forever Blade", 65, 1.25f, 15, 20);
        byte[] hotfixPayload999 = BuildSequentialPayload("Brand New Forever Relic", 80, 1.50f, 30, 45);

        var hotfixes = new[]
        {
            new HotfixEntry(0, 1, 113001, 1, 0x919BE54E, "ItemSparse", 100, Status: 1, hotfixPayload100),
            new HotfixEntry(1, 1, 113002, 1, 0x919BE54E, "ItemSparse", 999, Status: 1, hotfixPayload999),
            new HotfixEntry(2, 1, 113003, 1, 0x919BE54E, "ItemSparse", 101, Status: 2, [])
        };

        HotfixCacheReader.ApplyHotfixesToTable(table, hotfixes);

        Assert.Equal(2, table.HotfixUpsertCount);
        Assert.Equal(1, table.HotfixDeleteCount);
        Assert.False(table.RowsById.ContainsKey(101));

        var r100 = table.RowsById[100];
        Assert.Equal(RowSourceKind.Db2PlusHotfix, r100.Source);
        Assert.Equal("Reforged Forever Blade", r100.Values[1]);
        Assert.Equal((ushort)65, r100.Values[2]);

        var r999 = table.RowsById[999];
        Assert.Equal(RowSourceKind.HotfixOnly, r999.Source);
        Assert.Equal("Brand New Forever Relic", r999.Values[1]);
        Assert.Equal(113002, r999.HotfixPushId);
    }

    [Fact]
    public void BlpDecoder_DecodesPalettedTextureToValidPng()
    {
        byte[] blp = new byte[1172 + 16 + 16];
        Encoding.ASCII.GetBytes("BLP2").CopyTo(blp, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(blp.AsSpan(4, 4), 1);
        blp[8] = 1; // Paletted
        blp[9] = 8; // 8-bit alpha
        BinaryPrimitives.WriteInt32LittleEndian(blp.AsSpan(12, 4), 4);
        BinaryPrimitives.WriteInt32LittleEndian(blp.AsSpan(16, 4), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(blp.AsSpan(20, 4), 1172);
        BinaryPrimitives.WriteUInt32LittleEndian(blp.AsSpan(84, 4), 32);

        blp[148 + 4 + 0] = 238;
        blp[148 + 4 + 1] = 53;
        blp[148 + 4 + 2] = 163;
        blp[148 + 4 + 3] = 255;

        for (int i = 0; i < 16; i++)
        {
            blp[1172 + i] = 1;
            blp[1172 + 16 + i] = 255;
        }

        var (w, h, rgba) = BlpDecoder.DecodeToRgba32(blp);
        Assert.Equal(4, w);
        Assert.Equal(4, h);
        Assert.Equal(163, rgba[0]);
        Assert.Equal(53, rgba[1]);
        Assert.Equal(238, rgba[2]);
        Assert.Equal(255, rgba[3]);

        byte[] png = BlpDecoder.DecodeToPngBytes(blp);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
    }

    [Fact]
    public void LuaApiExporter_ParsesBlizzardApidocAndValidatesCamelotSurfaceExports()
    {
        const string sampleDocLua = """
            local GameRulesDoc =
            {
                Name = "GameRules",
                Type = "System",
                Namespace = "C_GameRules",
                Environment = "All",

                Functions =
                {
                    {
                        Name = "SetForeverExperiencePresetPostLogin",
                        Type = "Function",
                        HasRestrictions = true,
                        SecretArguments = "AllowedWhenUntainted",
                        Documentation = { "Switches Classic Forever preset after login." },

                        Arguments =
                        {
                            { Name = "preset", Type = "ForeverExperiencePreset", Nilable = false },
                        },

                        Returns =
                        {
                            { Name = "applied", Type = "bool", Nilable = false },
                        },
                    },
                },

                Events =
                {
                    {
                        Name = "ForeverExperiencePresetChanged",
                        Type = "Event",
                        LiteralName = "FOREVER_EXPERIENCE_PRESET_CHANGED",
                        SynchronousEvent = true,
                        Payload =
                        {
                            { Name = "newPreset", Type = "ForeverExperiencePreset", Nilable = false },
                        },
                    },
                },

                Tables =
                {
                    {
                        Name = "ForeverExperiencePreset",
                        Type = "Enumeration",
                        Fields =
                        {
                            { Name = "Classic", Type = "ForeverExperiencePreset", EnumValue = 0 },
                            { Name = "Enhanced", Type = "ForeverExperiencePreset", EnumValue = 1 },
                        },
                    },
                },
            };
            APIDocumentation:AddDocumentationTable(GameRulesDoc);
            """;

        var parsed = LuaApiExporter.ParseDocumentationLua(sampleDocLua, "GameRulesDocumentation.lua", 999999);
        Assert.Equal("GameRules", parsed.Name);
        Assert.Equal("C_GameRules", parsed.Namespace);
        Assert.Single(parsed.Functions);
        Assert.Equal("C_GameRules.SetForeverExperiencePresetPostLogin", parsed.Functions[0].FullName);
        Assert.Equal("true", parsed.Functions[0].Attributes["HasRestrictions"]);
        Assert.Equal("AllowedWhenUntainted", parsed.Functions[0].Attributes["SecretArguments"]);
        Assert.Single(parsed.Events);
        Assert.Equal("FOREVER_EXPERIENCE_PRESET_CHANGED", parsed.Events[0].LiteralName);
        Assert.True(parsed.Events[0].Synchronous);
        Assert.Single(parsed.Tables);
        Assert.Equal("Enumeration", parsed.Tables[0].Kind);
        Assert.Equal(2, parsed.Tables[0].Fields.Count);

        string repoRoot = FindRepoRoot();
        string luaApiDir = Path.Combine(repoRoot, "data", "lua_api");
        if (!Directory.Exists(luaApiDir))
        {
            return;
        }

        string completePath = Path.Combine(luaApiDir, "LuaApi_Complete.json");
        string camelotPath = Path.Combine(luaApiDir, "LuaApi_CamelotSurface.json");
        string mdPath = Path.Combine(luaApiDir, "LuaApi_Reference.md");

        foreach (string path in new[] { completePath, camelotPath, mdPath })
        {
            Assert.True(File.Exists(path), $"Lua-API-Exportdatei fehlt: {path}");
            byte[] raw = File.ReadAllBytes(path);
            Assert.False(raw is [0xEF, 0xBB, 0xBF, ..], $"UTF-8 BOM in {path} gefunden!");
        }

        using (var fs = File.OpenRead(completePath))
        using (var doc = JsonDocument.Parse(fs))
        {
            var root = doc.RootElement;
            Assert.Equal(18, root.GetProperty("ProjectId").GetInt32());
            Assert.Equal("Camelot", root.GetProperty("ProjectCodeName").GetString());
            Assert.True(root.GetProperty("TotalSystems").GetInt32() >= 600);
            Assert.True(root.GetProperty("TotalFunctions").GetInt32() >= 6_000);
            Assert.True(root.GetProperty("TotalEvents").GetInt32() >= 1_700);
        }

        using (var fs = File.OpenRead(camelotPath))
        using (var doc = JsonDocument.Parse(fs))
        {
            var root = doc.RootElement;
            Assert.Equal("Mainline", root.GetProperty("UiFamily").GetString());
            Assert.Equal("camelot", root.GetProperty("GameType").GetString());
            Assert.Equal(16001, root.GetProperty("RecommendedAddonTocInterface").GetInt32());
        }
    }

    [Fact]
    public void WowBuildDiffer_DetectsSpellsTitlesAndHotfixWavesAccurately()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"GrunzForever_DiffTest_{Guid.NewGuid():N}");
        string dirOld = Path.Combine(tempDir, "old");
        string dirNew = Path.Combine(tempDir, "new");
        Directory.CreateDirectory(dirOld);
        Directory.CreateDirectory(dirNew);

        try
        {
            var spellNameCols = new List<DbdColumnLayout>
            {
                new("ID", DbdBaseKind.Int, 32, false, 1, IsId: true, IsNonInline: true, IsRelation: false, null),
                new("Name_lang", DbdBaseKind.String, 32, false, 1, false, false, false, null)
            };

            var snOld = new ExtractedTableData
            {
                TableName = "SpellName",
                TableHash = 0x11223344,
                LayoutHash = 0xAABBCC01,
                Columns = spellNameCols,
                BaseDb2RowCount = 2
            };
            snOld.RowsById[10] = new ExtractedRow { Id = 10, Values = [10, "Fireball"], Source = RowSourceKind.Db2 };
            snOld.RowsById[20] = new ExtractedRow { Id = 20, Values = [20, "Old Custom Strike"], Source = RowSourceKind.HotfixOnly, HotfixPushId = 500 };

            var snNew = new ExtractedTableData
            {
                TableName = "SpellName",
                TableHash = 0x11223344,
                LayoutHash = 0xAABBCC01,
                Columns = spellNameCols,
                BaseDb2RowCount = 2,
                HotfixUpsertCount = 2
            };
            snNew.RowsById[10] = new ExtractedRow { Id = 10, Values = [10, "Fireball"], Source = RowSourceKind.Db2 };
            snNew.RowsById[20] = new ExtractedRow { Id = 20, Values = [20, "Reforged Custom Strike"], Source = RowSourceKind.HotfixOnly, HotfixPushId = 501 };
            snNew.RowsById[30] = new ExtractedRow { Id = 30, Values = [30, "Forever Nova"], Source = RowSourceKind.HotfixOnly, HotfixPushId = 501 };

            var titleCols = new List<DbdColumnLayout>
            {
                new("ID", DbdBaseKind.Int, 32, false, 1, IsId: true, IsNonInline: true, IsRelation: false, null),
                new("Name_lang", DbdBaseKind.String, 32, false, 1, false, false, false, null)
            };
            var titlesNew = new ExtractedTableData
            {
                TableName = "CharTitles",
                TableHash = 0x55667788,
                LayoutHash = 0xDDCCBBAA,
                Columns = titleCols,
                BaseDb2RowCount = 1
            };
            titlesNew.RowsById[1352] = new ExtractedRow { Id = 1352, Values = [1352, "%s the Harbinger of Darkness"], Source = RowSourceKind.Db2 };

            var buildOld = CreateDummyBuild("1.60.1.70685", "aaaa1111bbbb2222cccc3333dddd4444");
            var buildNew = CreateDummyBuild("1.60.1.70707", "7feb61f3c994bcb168526b8db521514a");

            var hfOld = new HotfixCacheDocument
            {
                FilePath = "DBCache.bin",
                Version = 9,
                Build = 70685,
                Entries = [new HotfixEntry(0, 1, 500, 1, 0x11223344, "SpellName", 20, 1, [1])]
            };
            var hfNew = new HotfixCacheDocument
            {
                FilePath = "DBCache.bin",
                Version = 9,
                Build = 70707,
                Entries =
                [
                    new HotfixEntry(0, 1, 501, 2, 0x11223344, "SpellName", 20, 1, [1]),
                    new HotfixEntry(1, 1, 501, 3, 0x11223344, "SpellName", 30, 1, [1])
                ]
            };

            var expOld = WowDatabaseExporter.ExportAll(buildOld, [snOld], hfOld, dirOld);
            var expNew = WowDatabaseExporter.ExportAll(buildNew, [snNew, titlesNew], hfNew, dirNew);

            var diff = WowBuildDiffer.Compare(expOld.SqlitePath, expNew.SqlitePath);

            Assert.Equal("1.60.1.70685", diff.OldVersion);
            Assert.Equal("1.60.1.70707", diff.NewVersion);
            Assert.Single(diff.AddedTables);
            Assert.Equal("CharTitles", diff.AddedTables[0].TableName);
            Assert.Single(diff.AddedSpells);
            Assert.Equal("Forever Nova", diff.AddedSpells[0].Name);
            Assert.Single(diff.ModifiedSpells);
            Assert.Equal(20, diff.ModifiedSpells[0].Id);
            Assert.Single(diff.AddedTitles);
            Assert.Equal("%s the Harbinger of Darkness", diff.AddedTitles[0].Name);
            Assert.Single(diff.NewHotfixWaves);
            Assert.Equal(501, diff.NewHotfixWaves[0].PushId);

            string markdown = WowBuildDiffer.FormatMarkdown(diff);
            Assert.Contains("1.60.1.70685", markdown);
            Assert.Contains("Forever Nova", markdown);
            Assert.Contains("%s the Harbinger of Darkness", markdown);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void CuratedRepositoryData_ParsesCleanlyWithoutBomOrPrivateLeaks()
    {
        string repoRoot = FindRepoRoot();
        string dataDir = Path.Combine(repoRoot, "data");
        if (!Directory.Exists(dataDir))
        {
            return;
        }

        foreach (string jsonFile in Directory.EnumerateFiles(dataDir, "*.json", SearchOption.AllDirectories))
        {
            byte[] raw = File.ReadAllBytes(jsonFile);
            Assert.False(raw is [0xEF, 0xBB, 0xBF, ..], $"UTF-8 BOM gefunden in {jsonFile}");

            using var doc = JsonDocument.Parse(raw);
            Assert.True(doc.RootElement.ValueKind is JsonValueKind.Array or JsonValueKind.Object);
        }

        string customItemsPath = Path.Combine(dataDir, "forever", "CustomForever_Items.json");
        if (File.Exists(customItemsPath))
        {
            using var fs = File.OpenRead(customItemsPath);
            using var doc = JsonDocument.Parse(fs);
            Assert.True(doc.RootElement.GetArrayLength() >= 700);
        }
    }

    private static WowBuildMetadata CreateDummyBuild(string ver, string buildKey) =>
        new()
        {
            FlavorDirectory = "/wow/_forever_",
            WowRootDirectory = "/wow",
            Product = "wowf",
            Version = ver,
            BuildName = $"WOW-patch_{ver}",
            BuildKey = buildKey,
            CdnKey = "00000000000000000000000000000000",
            RootCKey = new byte[16],
            EncodingCKey = new byte[16],
            EncodingEKey = new byte[16]
        };

    private static void WriteChunkEntry(Span<byte> dest24, int compSize, int decompSize)
    {
        BinaryPrimitives.WriteUInt32BigEndian(dest24.Slice(0, 4), (uint)compSize);
        BinaryPrimitives.WriteUInt32BigEndian(dest24.Slice(4, 4), (uint)decompSize);
    }

    private static byte[] BuildSequentialPayload(string name, ushort ilvl, float speedMod, int stat0, int stat1)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        bw.Write(Encoding.UTF8.GetBytes(name));
        bw.Write((byte)0);
        bw.Write(ilvl);
        bw.Write(speedMod);
        bw.Write(stat0);
        bw.Write(stat1);
        bw.Flush();
        return ms.ToArray();
    }

    private static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (File.Exists(Path.Combine(dir, "GrunzForever.slnx")) ||
                File.Exists(Path.Combine(dir, "GrunzForever.sln")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)!.FullName;
        }
        throw new DirectoryNotFoundException("GrunzForever.slnx nicht gefunden.");
    }
}
