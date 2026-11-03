# Changelog

Alle relevanten Änderungen an **GrunzForever** und den dokumentierten **WoW: Classic Forever (`wowf` / `Camelot`)** Builds werden hier festgehalten.

## [Unreleased]

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
