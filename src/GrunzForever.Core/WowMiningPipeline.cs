using System.Diagnostics;
using System.IO;
using System.Text;

namespace GrunzForever.Core;

public sealed class PipelineOptions
{
    public required string WowFlavorDirectory { get; init; }
    public required string ExportDirectory { get; init; }
    public required string DbdCacheDirectory { get; init; }
    public bool ExtractAllAvailableDb2s { get; init; }
}

public sealed class PipelineResult
{
    public required WowBuildMetadata Build { get; init; }
    public required ExportSummary ExportSummary { get; init; }
    public required IReadOnlyList<ExtractedTableData> Tables { get; init; }
    public required HotfixCacheDocument? HotfixCache { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// End-to-end CASC + WDC5 + XFTH DBCache.bin extraction and export pipeline for World of Warcraft.
/// </summary>
public static class WowMiningPipeline
{
    public static readonly string[] CuratedGameplayTables =
    [
        // Items, Stats, Loot & Economy
        "Item", "ItemSparse", "ItemSearchName", "ItemEffect", "ItemXItemEffect",
        "ItemClass", "ItemSubClass", "ItemSubClassMask", "ItemSet", "ItemSetSpell",
        "ItemAppearance", "ItemModifiedAppearance", "ItemDisplayInfo",
        "ItemBonus", "ItemBonusList", "ItemBonusTree", "ItemBonusTreeNode", "ItemXBonusTree",
        "ItemDisenchantLoot", "ItemExtendedCost", "ItemLimitCategory", "ItemNameDescription",
        "ItemPetFood", "ItemPriceBase", "ItemBagFamily",
        "ItemArmorQuality", "ItemArmorShield", "ItemArmorTotal",
        "ItemDamageOneHand", "ItemDamageOneHandCaster", "ItemDamageTwoHand", "ItemDamageTwoHandCaster",
        "ItemDamageRanged", "ItemDamageThrown", "ItemDamageWand", "ItemDamageAmmo",
        "ItemReforge", "ItemSalvage", "ItemSalvageLoot",
        "SpellItemEnchantment", "SpellItemEnchantmentCondition", "GemProperties",
        "RandPropPoints", "ScalingStatDistribution", "ScalingStatValues",
        "CurrencyTypes", "CurrencyCategory",

        // Spells, Auras, Talents, Traits & Professions
        "Spell", "SpellName", "SpellMisc", "SpellEffect", "SpellLevels", "SpellCooldowns",
        "SpellAuraOptions", "SpellAuraRestrictions", "SpellCategories", "SpellCategory",
        "SpellCastTimes", "SpellDuration", "SpellRange", "SpellRadius", "SpellPower",
        "SpellReagents", "SpellReagentsCurrency", "SpellCastingRequirements",
        "SpellClassOptions", "SpellEquippedItems", "SpellInterrupts", "SpellLabel",
        "SpellLearnSpell", "SpellMechanic", "SpellDispelType", "SpellScaling",
        "SpellShapeshift", "SpellShapeshiftForm", "SpellTargetRestrictions", "SpellTotems",
        "SpellFocusObject", "SpellDescriptionVariables", "SpellXDescriptionVariables",
        "SpellActionBarPref", "SpellVisual",
        "Talent", "TalentTab", "GlyphProperties",
        "TraitTree", "TraitNode", "TraitNodeEntry", "TraitNodeGroup", "TraitDefinition",
        "TraitCond", "TraitCost", "TraitCurrency", "TraitCurrencySource",
        "SkillLine", "SkillLineAbility", "SkillLineCategory", "SkillRaceClassInfo",
        "TradeSkillCategory", "TradeSkillItem", "Profession",

        // Quests, Dialogue & Strings
        "QuestV2", "QuestV2CliTask", "QuestObjective", "QuestInfo", "QuestSort",
        "QuestXP", "QuestMoneyReward", "QuestFactionReward", "QuestFeedbackEffect",
        "QuestLine", "QuestLineXQuest", "QuestPOIBlob", "QuestPOIPoint", "QuestPackageItem",
        "BroadcastText", "ConversationLine", "PageTextMaterial", "MailTemplate", "GlobalStrings",

        // Creatures, NPCs, GameObjects, World, Dungeons, Factions & Character
        "Creature", "CreatureDifficulty", "CreatureDifficultyTreasure",
        "CreatureDisplayInfo", "CreatureDisplayInfoExtra", "CreatureFamily", "CreatureType",
        "CreatureModelData", "CreatureSoundData", "CreatureSpellData", "CreatureImmunities",
        "CreatureLabel", "CreatureXDisplayInfo", "GossipNPCOption",
        "NPCSounds", "GameObjects", "GameObjectDisplayInfo", "Lock", "LockType",
        "TaxiNodes", "TaxiPath", "TaxiPathNode",
        "CollectableSourceInfo", "CollectableSourceVendor", "CollectableSourceVendorSparse",
        "CollectableSourceEncounter", "CollectableSourceEncounterSparse",
        "CollectableSourceQuest", "CollectableSourceQuestSparse",
        "DungeonEncounter", "JournalEncounter", "JournalEncounterCreature",
        "JournalEncounterItem", "JournalEncounterSection", "JournalInstance", "JournalTier",
        "AdventureJournal", "LFGDungeons", "BattlemasterList",
        "AreaTable", "AreaPOI", "WMOAreaTable", "WorldMapArea", "UiMap", "Map", "MapDifficulty",
        "Faction", "FactionGroup", "FactionTemplate",
        "Mount", "MountCapability", "MountType", "BattlePetSpecies",
        "Achievement", "Achievement_Category", "Criteria", "CriteriaTree", "ModifierTree",
        "ChrClasses", "ChrRaces", "ChrRaceRacialAbility", "ChrSpecialization", "CharTitles",
        "PlayerCondition", "HolidayNames", "Holidays", "ManifestInterfaceData"
    ];

    public static async Task<PipelineResult> RunAsync(
        PipelineOptions options,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        var totalSw = Stopwatch.StartNew();
        var warnings = new List<string>();
        void Log(string msg) => log?.Invoke(msg);

        // 1. Open CASC storage
        Log($"[1/6] Öffne lokale CASC-Archive unter: {options.WowFlavorDirectory}");
        using var casc = CascStorage.Open(options.WowFlavorDirectory);
        Log($"      Produkt: {casc.Build.Product} | Version: {casc.Build.Version} | Build: {casc.Build.BuildName}");
        Log($"      Indizierte lokale CASC-Blöcke: {casc.IndexedEntryCount:N0}");

        // 2. Parse Root manifest
        Log("[2/6] Dekodiere TSFM v2 Root-Manifest ...");
        var root = RootManifest.Parse(casc.LoadRootManifestBytes());
        Log($"      Aufgelöste FileDataIDs: {root.UniqueFileCount:N0} ({root.TotalBlocksParsed:N0} Blöcke)");

        // 3. Initialize WoWDBDefs manifest
        Log($"[3/6] Initialisiere WoWDBDefs Schema-Katalog ({options.DbdCacheDirectory}) ...");
        using var dbd = new DbdSchemaProvider(options.DbdCacheDirectory);
        await dbd.InitializeManifestAsync(ct);
        Log($"      Bekannte DB2-Tabellendefinitionen: {dbd.AllTables.Count:N0}");

        // 4. Locate & parse DBCache.bin server hotfixes
        HotfixCacheDocument? hotfixDoc = null;
        string? dbCachePath = ResolveDbCachePath(options.WowFlavorDirectory);
        if (dbCachePath != null)
        {
            Log($"[4/6] Lese Server-Hotfix-Cache: {dbCachePath}");
            hotfixDoc = HotfixCacheReader.Read(dbCachePath, dbd);
            int validCount = hotfixDoc.Entries.Count(e => e.Status == 1);
            int deleteCount = hotfixDoc.Entries.Count(e => e.Status == 2);
            int distinctTables = hotfixDoc.Entries.Where(e => e.Status is 1 or 2).Select(e => e.TableHash).Distinct().Count();
            Log($"      Hotfix-Einträge gesamt: {hotfixDoc.Entries.Count:N0} (Valid={validCount:N0}, Delete={deleteCount:N0} über {distinctTables} Tabellen)");
        }
        else
        {
            Log("[4/6] Kein lokaler DBCache.bin gefunden – extrahiere reine CASC-DB2-Daten.");
        }

        // 5. Assemble target table set (curated gameplay tables + 100% of hotfixed tables)
        var targetEntries = new Dictionary<string, DbdManifestEntry>(StringComparer.OrdinalIgnoreCase);

        if (options.ExtractAllAvailableDb2s)
        {
            foreach (var m in dbd.AllTables.Where(t => t.Db2FileDataId.HasValue))
            {
                targetEntries[m.TableName] = m;
            }
        }
        else
        {
            foreach (string name in CuratedGameplayTables)
            {
                if (dbd.TryGetByName(name, out var entry))
                {
                    targetEntries[entry.TableName] = entry;
                }
            }
        }

        // Ensure EVERY known table referenced by Valid/Delete hotfixes in DBCache.bin is included
        if (hotfixDoc != null)
        {
            foreach (uint tableHash in hotfixDoc.Entries
                         .Where(e => e.Status is 1 or 2)
                         .Select(e => e.TableHash)
                         .Distinct())
            {
                if (dbd.TryGetByHash(tableHash, out var hfEntry))
                {
                    targetEntries[hfEntry.TableName] = hfEntry;
                }
            }
        }

        var orderedTargets = targetEntries.Values
            .OrderBy(e => e.TableName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Pre-warm .dbd files concurrently so HTTP caching finishes in a couple seconds
        Log($"[5/6] Lade Schemata & extrahiere {orderedTargets.Count} Ziel-Tabellen (DB2 + Hotfix-Merge) ...");
        using (var gate = new SemaphoreSlim(12))
        {
            var warmTasks = orderedTargets.Select(async entry =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    await dbd.LoadSchemaAsync(entry.TableName, exactLayoutHash: null, casc.Build.Version, ct);
                }
                finally
                {
                    gate.Release();
                }
            });
            await Task.WhenAll(warmTasks);
        }

        var hotfixByHash = hotfixDoc?.EntriesByTableHash;
        var extractedTables = new List<ExtractedTableData>(orderedTargets.Count);

        foreach (var entry in orderedTargets)
        {
            ct.ThrowIfCancellationRequested();

            byte[]? rawDb2 = null;
            if (entry.Db2FileDataId.HasValue && root.TryGetContentKey(entry.Db2FileDataId.Value, out var cKey))
            {
                try
                {
                    rawDb2 = casc.ReadFileByCKey(cKey);
                }
                catch (Exception ex)
                {
                    warnings.Add($"{entry.TableName}: CASC-Lesefehler ({ex.Message})");
                }
            }

            bool hasActiveHotfixes = hotfixByHash != null &&
                                     hotfixByHash[entry.TableHash].Any(h => h.Status is 1 or 2);

            ExtractedTableData? tableData = null;

            if (rawDb2 is { Length: >= 72 } &&
                Encoding.ASCII.GetString(rawDb2, 0, 4) is "WDC5" or "WDC4" or "WDC3")
            {
                try
                {
                    uint layoutHash = Wdc5Reader.PeekLayoutHash(rawDb2);
                    var schema = await dbd.LoadSchemaAsync(entry.TableName, layoutHash, casc.Build.Version, ct);
                    tableData = Wdc5Reader.Read(rawDb2, entry.TableName, schema);
                }
                catch (Exception ex)
                {
                    warnings.Add($"{entry.TableName}: WDC5-Parserfehler ({ex.Message})");
                }
            }

            // Fallback for server-only / hotfix-only tables without local DB2
            if (tableData == null && hasActiveHotfixes)
            {
                var fallbackSchema = await dbd.LoadSchemaAsync(entry.TableName, exactLayoutHash: null, casc.Build.Version, ct);
                if (fallbackSchema != null && fallbackSchema.Columns.Count > 0)
                {
                    tableData = new ExtractedTableData
                    {
                        TableName = entry.TableName,
                        TableHash = entry.TableHash,
                        LayoutHash = fallbackSchema.MatchedLayoutHash,
                        Columns = fallbackSchema.Columns,
                        BaseDb2RowCount = 0
                    };
                }
            }

            if (tableData == null)
            {
                continue;
            }

            if (hotfixByHash != null)
            {
                var tableHotfixes = hotfixByHash[entry.TableHash];
                if (tableHotfixes.Any())
                {
                    try
                    {
                        HotfixCacheReader.ApplyHotfixesToTable(tableData, tableHotfixes);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"{entry.TableName}: Hotfix-Merge-Warnung ({ex.Message})");
                    }
                }
            }

            if (tableData.RowsById.Count > 0 || tableData.HotfixDeleteCount > 0 || rawDb2 != null)
            {
                extractedTables.Add(tableData);
            }
        }

        Log($"      Erfolgreich extrahierte Tabellen: {extractedTables.Count:N0} | Zeilen gesamt: {extractedTables.Sum(t => (long)t.RowsById.Count):N0}");

        // 6. Export SQLite + JSON
        Log($"[6/6] Exportiere SQLite-Datenbank & JSON-Pakete nach: {options.ExportDirectory} ...");
        var exportSummary = WowDatabaseExporter.ExportAll(casc.Build, extractedTables, hotfixDoc, options.ExportDirectory);

        totalSw.Stop();
        Log($"      Export abgeschlossen in {exportSummary.Elapsed.TotalSeconds:F2}s (Gesamtzeit: {totalSw.Elapsed.TotalSeconds:F2}s).");

        return new PipelineResult
        {
            Build = casc.Build,
            ExportSummary = exportSummary,
            Tables = extractedTables,
            HotfixCache = hotfixDoc,
            Warnings = warnings
        };
    }

    private static string? ResolveDbCachePath(string flavorDir)
    {
        string preferred = Path.Combine(flavorDir, "Cache", "ADB", "enUS", "DBCache.bin");
        if (File.Exists(preferred))
        {
            return preferred;
        }

        string adbRoot = Path.Combine(flavorDir, "Cache", "ADB");
        if (Directory.Exists(adbRoot))
        {
            return Directory.EnumerateFiles(adbRoot, "DBCache.bin", SearchOption.AllDirectories)
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault();
        }

        return null;
    }
}
