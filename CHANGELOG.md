# Changelog

Alle relevanten Änderungen an **GrunzForever** und den dokumentierten **WoW: Classic Forever (`wowf` / `Camelot`)** Builds werden hier festgehalten.

## [0.4.0] - 2026-11-11

### Hinzugefügt
- **Daten-Update (`wowf` Build `1.60.1.70888` + Live-Hotfixes):**
  - Neuer Build-zu-Build-Diff [`docs/diffs/1.60.1.70862_to_1.60.1.70888.md`](docs/diffs/1.60.1.70862_to_1.60.1.70888.md) und Manifest [`data/manifests/_ExtractionManifest_1.60.1.70888.json`](data/manifests/_ExtractionManifest_1.60.1.70888.json) (`315` Tabellen / `+6` neu: `ContentTuning`, `SharedString`, `SoundAmbience`, `SoundAmbienceFlavor`, `TransformMatrix`, `WorldStateZoneSounds`, `1,499,238` Datensätze / `+5,291` Zeilen, `39,289` Valid- & `12,257` Delete-Hotfixes).
  - Hotfix-Bake-In von `CurvePoint`/`Curve`/`LightDataGlobalVolumeFog`/`LightData`, neue `Cfg_GameRules` (`ID 271` via `PushId 113763`, `ID 267` via `PushId 113904`), Zonen-Hotfixes für `Shen'dralas` & Subzonen (`AreaTable PushId 113843`) sowie `Stratholme` (`PushId 113801`).
  - `+1,308` neu gecachte Items (inkl. Custom-Forever-Epics/Rares wie `Ankle Slicers`, `High Clerist's Seal`, `Scarlet Forgemaster's Bindings`, `Shaper's Robe`, `Blackmaw Bindings`, `Krol'dok Warplate`), `125` modifizierte Items (Stat-Nerfs auf den Engineering-Trinkets `272437`–`272439`, Stat-Updates auf `284215`/`280604`/`280031`/`254013` sowie `SellPrice`-Wirtschafts-Pass auf `120` hergestellten Rüstungsteilen).
  - `+11` neue Spells (`Gloves of Holy Might`, `Poised`, `Terrifying Roar`, `Drink` u. a.), `14` modifizierte Spells (Hexenmeister-`Wrack`-Rework als kanalisierter DoT-Beschleuniger, `Soul Siphon`, Druiden-`Swiftmend` ohne HoT-Verbrauch, `Reduced Claw and Rake Cost`, `Holy Power`, Paladin-`Seals`), `SpellEffect`-Hotfixes (`Flametongue Totem`, `Nature's Swiftness`, `Frost Trap`, `Poison Shock`, `Escape Rope`, `Teach Summon Felsteed`, `Flamebreak Talisman`, `Frost Nova`) sowie neuer Lua-Event-Parameter `isCrossShard` in `PARTY_INVITE_REQUEST`.
- **Post-Launch-Daten-Update (`wowf` Build `1.60.1.70862`):**
  - Neuer Build-zu-Build-Diff [`docs/diffs/1.60.1.70790_to_1.60.1.70862.md`](docs/diffs/1.60.1.70790_to_1.60.1.70862.md) und Manifest [`data/manifests/_ExtractionManifest_1.60.1.70862.json`](data/manifests/_ExtractionManifest_1.60.1.70862.json) (`309` Tabellen / `+10` neu, `1,493,947` Datensätze / `+61,997` Zeilen, `27,257` Valid- & `11,916` Delete-Hotfixes).
  - Neue Forever-Zonen & Encounters in `AreaTable` / `DungeonEncounter` (`The Drowned City` mit `Min'loth the Serpent`, `Blackmaw Hold`, `The Shaper's Terrace`, `Krol'dok Stronghold`), neue Sprache `Shen'dorei` (`Languages ID 310`), `CharTitles`-Fix für `%s the Blacksmith` (`Mask_ID = 40`), `+3,072` neu gecachte Items (darunter `610` Custom-Forever-Items inkl. `22` Epics und `287` Rares), `9` Stadt-Portale (inkl. `Portal to Dalaran`/`Southshore`) sowie Klassen-Updates (`Shank`, `Gore Drinker`, `Ravage`, `Bear Form`, `Use Soulstone`).

## [0.3.0] - 2026-11-04

### Hinzugefügt
- **Pre-Launch-Daten-Update (`wowf` Build `1.60.1.70790`):**
  - Neuer Build-zu-Build-Diff [`docs/diffs/1.60.1.70755_to_1.60.1.70790.md`](docs/diffs/1.60.1.70755_to_1.60.1.70790.md) und Manifest [`data/manifests/_ExtractionManifest_1.60.1.70790.json`](data/manifests/_ExtractionManifest_1.60.1.70790.json) (`299` Tabellen / `+1` `ChatChannels`, `1,431,950` Datensätze, `16,919` Valid- & `11,518` Delete-Hotfixes über `18` neue `PushId`-Wellen).
  - Hotfix-Bake-In der `70755`-Klassen-/Wetter-Updates in die Basis-DB2s, `Ferocious Bite`-Buff (~25 % über alle 6 Ränge), Pet-Skalierungs- & `Cfg_GameRules`-Updates (`PushId 113223` / `113595`), `AuctionHouse`/`AvailableSuperDistrict`-Launch-Konfiguration (`PushId 113600`) sowie `Fortified Spaulders` (`Item 9817`).
  - Aktualisierte Camelot-Lua-API-Referenz (`6,611` Funktionen, `+1` `C_BattleNet.GetDefaultTitleWhisperTarget`).

## [0.2.0] - 2026-11-03

### Hinzugefügt
- **Daten-Update (`wowf` Build `1.60.1.70755` + Live-Hotfixes bis `PushId 16791390`):**
  - Neuer Build-zu-Build-Diff [`docs/diffs/1.60.1.70707_to_1.60.1.70755.md`](docs/diffs/1.60.1.70707_to_1.60.1.70755.md) und Manifest [`data/manifests/_ExtractionManifest_1.60.1.70755.json`](data/manifests/_ExtractionManifest_1.60.1.70755.json) (`298` Tabellen / `+11` neu, `1,431,926` Datensätze / `+13,948` Zeilen, `16,683` Valid- & `11,512` Delete-Hotfixes über `62` neue `PushId`-Wellen).
  - Neue DB2-Tabellen im Export, u. a. `GameMode` (inkl. `wfhc` Hardcore `SeasonID 140` & `wf` `SeasonID 136..139`), `CatalogShopGameTitleInfo`, `TimeEventData`, `ScheduledInterval`, `Weather` und `LightDataGlobalVolumeFog`.
  - `2` neue Items (`289439` `Pouch of Violet Dust` Launch-Celebration-Toy, `14174` `Buccaneer's Boots`) und `37` neue sowie `51` geänderte Spells — darunter grosses Schurken-Rework (`Shank`, `Venom`, `Boot Knife`, `Flash of Steel`, `Quick Getaway`, `Composure`, `Blood Splatter`, `Poised`, `Coup de Grace`, `Efficient Strikes`), Krieger (`Reckless Swings`, `Improved Whirlwind`), Schamane (`Totemic Call`, `Lightning Mastery`) und Feral-Druide (`Poised`).
  - Aktualisierte Camelot-Lua-API-Referenz (`6,610` Funktionen, `+1` `C_ActionBar.IsHighestSpellRank`, `subIcon`-Erweiterungen in `C_Texture` / `BNetGameAccountInfo`, `213` Camelot-Overrides inkl. `ActionButtonUtilOverrides.lua`).
- **Automatisierung (`tools/update-forever.ps1`):** End-to-End-Pipeline für Client-Forensik, Live-EU-TACT-Abgleich, Intra-Build-Snapshot-Sicherung, Extraktion, Build-/Hotfix-Diffing und Release-ZIP-Packaging.
- **Technische Dokumentation (`docs/`):** Vollständige Referenzdokumente [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md), [`docs/CAMELOT-CLIENT.md`](docs/CAMELOT-CLIENT.md), [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md) und [`docs/RELEASE.md`](docs/RELEASE.md).
- **Agenten-Kontext & Skills:** [`AGENTS.md`](AGENTS.md), [`CLAUDE.md`](CLAUDE.md) und Workspace-Skill [`.agents/skills/forever-update/SKILL.md`](.agents/skills/forever-update/SKILL.md) für den eigenständigen Projektbetrieb.

## [0.1.0] - 2026-11-01

### Hinzugefügt
- **`GrunzForever.Core` (.NET 10):**
  - Read-Only CASC-v7-Archivleser (`CascStorage`), Multi-Chunk-`BLTE`-Decoder (`N`/`Z`/`E`), `TSFM v2` Root-Manifest-Parser und `BLP2`-zu-PNG-Decoder.
  - Automatischer Schema-Abgleich gegen `WoWDBDefs` (`DbdSchemaProvider`) inkl. Bitpack-/Pallet-/Common-/Sparse-`WDC5`-Parser (`Wdc5Reader`).
  - `XFTH v9` Server-Hotfix-Parser & Merger (`HotfixCacheReader`) für `DBCache.bin` (`Valid`-Upserts und `Delete`-Tombstones).
  - Relationaler SQLite- & JSON-Exporter (`WowDatabaseExporter`) mit Komfort-Views (`v_Items`, `v_CustomForeverItems`, `v_Spells`, `v_CustomForeverSpells`, `v_Creatures`).
  - `WOW_PROJECT_CAMELOT = 18` Lua-API- & Blizzard-TOC-Oberflächen-Exporter (`LuaApiExporter`).
  - Automatischer Build-zu-Build-Differ (`WowBuildDiffer`) mit Markdown- und JSON-Changelog-Generierung.
- **`GrunzForever.Cli` (`grunzforever`):**
  - Kommandozeilen-Interface für Voll-Extraktion, `--sync-repo-data`, `--diff`, `--export-lua-api`, `--sql`, `--inspect-db2`, `--export-blp` und `--dump-casc`.
- **Datensätze & Build-Diffs (`wowf` Build `1.60.1.70707`):**
  - Kuratierte Forever-Deltas unter `data/forever/` (`CustomForever_Items.json`, `CustomForever_Spells.json`, `CharTitles.json`, `Cfg_GameRules.json` u. a.).
  - Vollständige Camelot-Lua-API-Referenz (`612` Systeme, `6,609` Funktionen, `1,816` Events) unter `data/lua_api/`.
  - Build-Diff-Bericht [`docs/diffs/1.60.1.70685_to_1.60.1.70707.md`](docs/diffs/1.60.1.70685_to_1.60.1.70707.md).
