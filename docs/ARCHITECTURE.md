# Architektur & Daten-Pipeline

Dieses Dokument beschreibt den technischen Aufbau von **GrunzForever**, die verarbeiteten Blizzard-Binärformate sowie das relationale Datenbank-Schema.

---

## 1. Systemkontext & Datenfluss

```text
┌──────────────────────────────────────────────────────────┐
│ Lokale WoW: Classic Forever Installation (_forever_)     │
│                                                          │
│  ├─ ../.build.info          (TACT BuildKey / CDNKey)     │
│  ├─ ../Data/config/*        (BuildConfig: root/encoding) │
│  ├─ ../Data/data/*.idx      (CASC v7 EKey -> Archiv+Off) │
│  ├─ ../Data/data/data.NNN   (BLTE-komprimierte Bloecke)  │
│  └─ Cache/ADB/enUS/DBCache.bin (XFTH v9 Server-Hotfixes) │
└───────────────────────────┬──────────────────────────────┘
                            │ Read-Only (FileShare.ReadWrite)
                            ▼
┌──────────────────────────────────────────────────────────┐
│ GrunzForever.Core (.NET 10, Zero Native Dependencies)    │
│                                                          │
│  1. CascStorage       -> Loest BuildKey -> Encoding ->   │
│                          EKey -> BLTE-Decompression auf  │
│  2. RootManifest      -> Parst TSFM v2 Root (FDID->CKey) │
│  3. DbdSchemaProvider -> Laedt/cacht WoWDBDefs (.dbd)    │
│                          passend zum WDC5 LayoutHash     │
│  4. Wdc5Reader        -> Dekodiert WDC5-DB2-Sektionen,   │
│                          Bitpacking, Pallet, CopyTable   │
│  5. HotfixCacheReader -> Parst XFTH v9 (PushId, Valid,   │
│                          Delete) & ueberschreibt Zeilen  │
│  6. LuaApiExporter    -> Extrahiert Blizzard_APIDoc +    │
│                          TOC GameType-Regeln (Camelot)   │
│  7. WowBuildDiffer    -> Vergleicht zwei SQLite-Stands   │
└──────────────┬───────────────────────────┬───────────────┘
               │                           │
               ▼                           ▼
┌─────────────────────────────┐ ┌──────────────────────────┐
│ Lokale Voll-Exporte         │ │ Versionierte Repo-Daten  │
│ (Exports/, gitignored)      │ │ (data/ & docs/diffs/)    │
│                             │ │                          │
│ - wow_wowf_<build>.sqlite   │ │ - CustomForever_*.json   │
│ - Exports/json/*.json       │ │ - LuaApi_*.json / .md    │
│ -> als .zip in GitHub       │ │ - _ExtractionManifest_*  │
│    Releases veroeffentlicht │ │ - <old>_to_<new>.md/json │
└─────────────────────────────┘ └──────────────────────────┘
```

---

## 2. Projekte & Abhängigkeitsregeln

```text
GrunzForever.Cli   ──▶  GrunzForever.Core
GrunzForever.Tests ──▶  GrunzForever.Core
```

| Projekt | Verantwortung | Externe Pakete |
| :--- | :--- | :--- |
| `src/GrunzForever.Core` | Komplette Binär-Decoder, Schema-Auflösung, Hotfix-Merger, SQLite/JSON-Exporter, Lua-API-Parser, Build-Differ und BLP2-Decoder | `Microsoft.Data.Sqlite` |
| `src/GrunzForever.Cli` | Kommandozeilen-Steuerung (`grunzforever`), Pfad-Auto-Erkennung, Repo-Sync (`--sync-repo-data`) und Ad-hoc-SQL-Abfragen | *(nur BCL + Core)* |
| `tests/GrunzForever.Tests` | Synthetische Binär- und Integrationstests für BLTE, Hotfix-Overlays, SQLite-Privacy und Build-Diffing | `xunit`, `Microsoft.NET.Test.Sdk` |

---

## 3. Verarbeitete Binärformate im Detail

### 3.1 CASC v7 & Encoding (`CascStorage.cs`, `BlteDecoder.cs`)
1. **`.flavor.info` & `.build.info`**: Ermittelt den aktiven Produktcode (`wowf`), die Build-Version (z. B. `1.60.1.70707`) sowie `Build Key` und `CDN Key`.
2. **Build-Config (`Data/config/XX/YY/<BuildKey>`)**: Liefert den Content-Key (`CKey`) und Encoding-Key (`EKey`) der `encoding`- und `root`-Systemdateien.
3. ** Lokale Index-Tabellen (`Data/data/*.idx`, Version 7)**: Gruppiert nach Bucket-Index; bildet die ersten 9 Bytes eines `EKey` auf `(ArchiveNumber, ArchiveOffset, EncodedSize)` in `data.NNN` ab. Pro Bucket wird stets nur die höchste `.idx`-Generation geladen.
4. **Encoding-Manifest**: Binäre CKey→EKey-Seitentabelle; übersetzt logische 16-Byte-MD5-`ContentKeys` in physische CASC-`EncodingKeys`.
5. **BLTE (`BlteDecoder.cs`)**: Unterstützt Multi-Chunk- und Single-Chunk-Frames:
   - Modus `'N'` (`0x4E`): Unkomprimierte Rohdaten.
   - Modus `'Z'` (`0x5A`): ZLib-komprimierte Blöcke (`ZLibStream`).
   - Modus `'E'` (`0x45`): Verschlüsselte TactKey-Blöcke — werden sauber übersprungen, ohne den Gesamtlauf abzubrechen.

### 3.2 Root-Manifest (`RootManifest.cs`)
- Parst Blizzards **`TSFM` v2** Root-Datei.
- Filtert Blöcke nach `LocaleFlags` (bevorzugt `enUS = 0x2` bzw. globale Blöcke) und erstellt eine schnelle `FileDataID -> ContentKey` Lookup-Map für alle DB2-, Lua-, XML- und TOC-Dateien.

### 3.3 WDC5-DB2 & WoWDBDefs (`Wdc5Reader.cs`, `DbdSchemaProvider.cs`)
- **Header-Validierung**: Liest Magic `WDC5`, `TableHash`, `LayoutHash`, Feld-Metadaten, Bitpacking-Informationen (`StorageType`: `None`, `Bitpacked`, `CommonData`, `BitpackedIndexed`, `BitpackedIndexedArray`, `BitpackedSigned`), `PalletData`, `CommonData`, `CopyTable` und `RelationshipData` (Foreign Keys am Zeilenende).
- **Schema-Matching**: Lädt `.dbd`-Definitionen von `WowDev/WoWDBDefs` (mit lokalem Dateicache in `Cache/WoWDBDefs/`). Das Feldlayout wird primär exakt über den 8-stelligen Hex-`LAYOUT`-Hash aufgelöst und sekundär über die `BUILD`-Range verifiziert.

### 3.4 Server-Hotfix-Overlay (`HotfixCacheReader.cs`, `WowMiningPipeline.cs`)
- Parst `_forever_/Cache/ADB/enUS/DBCache.bin` im **`XFTH` v9**-Format.
- Jeder Hotfix-Frame besitzt `PushId`, `UniqueId`, `TableHash`, `RecordId`, `DataSize` und `Status`:
  - `Status = 1 (Valid)`: Inline-serialisierter Rekord (C-Strings + skalare Felder gemäss `.dbd`-Spaltenbreiten). Aktualisiert eine bestehende DB2-Zeile (`HotfixModified`) oder fügt eine rein serverseitige Zeile neu ein (`HotfixInserted`).
  - `Status = 2 (Delete)`: Entfernt den entsprechenden `RecordId`-Eintrag aktiv aus der finalen Tabelle.
  - `Status = 3 / 4 (Invalid / NotPublic)`: Wird im Audit-Table `_HotfixCacheEntries` protokolliert, verändert aber nicht den aktiven Datenbestand.
- **Chronologische Deterministik**: Einträge werden streng in Dateireihenfolge angewendet, sodass spätere Hotfix-Pushes ältere Stände exakt wie im Live-Client überschreiben.

---

## 4. SQLite-Datenbankschema & Komfort-Views

Jede erzeugte Datenbank (`Exports/wow_wowf_<build>.sqlite`) ist zu 100 % frei von lokalen Dateisystempfaden und enthält:

### 4.1 System- & Audit-Tabellen
| Tabelle | Beschreibung |
| :--- | :--- |
| `_ExtractionMetadata` | Schlüssel/Wert-Metadaten (`Product`, `Version`, `BuildName`, `BuildKey`, `ExtractedAtUtc`, `TotalTables`, `HotfixEntryCount`). |
| `_TableSummary` | Eine Zeile pro DB2-Tabelle mit `TableHash`, `LayoutHash`, `ColumnCount`, `BaseDb2Rows`, `HotfixUpserts`, `HotfixDeletes` und `FinalRowCount`. |
| `_HotfixCacheEntries` | Vollständiges Audit-Log aller Einträge aus `DBCache.bin` (`SeqIndex`, `PushId`, `TableHash`, `TableName`, `RecordId`, `Status`, `PayloadSize`). |

### 4.2 Spieltabellen & Herkunftsspalten
Jede der ~287 extrahierten DB2-Tabellen erhält automatisch zwei zusätzliche Tracking-Spalten:
- `_DataSource` (`TEXT`): `'Db2'`, `'HotfixModified'` oder `'HotfixInserted'`.
- `_HotfixPushID` (`INTEGER NULL`): Die Server-`PushId`, falls der Datensatz durch `DBCache.bin` angelegt oder verändert wurde.

Array-Spalten (z. B. `StatModifier_bonusStat` in `ItemSparse` oder `Effect` in `SpellEffect`) werden als valides JSON-Array (`TEXT`) gespeichert und lassen sich direkt mit SQLite `json_each(...)` abfragen.

### 4.3 Vorab verknüpfte Komfort-Views
| View | Verknüpfte Tabellen & Zweck |
| :--- | :--- |
| `v_Items` | `ItemSparse` + `Item` + `ItemClass` + `ManifestInterfaceData` (inkl. aufgelöster Icon-Pfade, Qualität, ItemLevel, Stats und `_DataSource`). |
| `v_CustomForeverItems` | Gefilterte Sicht auf `v_Items` (`WHERE _DataSource != 'Db2'`) — zeigt sofort alle per Hotfix eingefügten/geänderten Forever-Items. |
| `v_Spells` | `SpellName` + `Spell` + `SpellMisc` + `ManifestInterfaceData` (inkl. Beschreibung, Aura-Text, Zauberschule, Icon und `_DataSource`). |
| `v_CustomForeverSpells` | Gefilterte Sicht auf `v_Spells` (`WHERE _DataSource != 'Db2'`) — alle serverseitig modifizierten oder neuen Forever-Spells. |
| `v_Creatures` | `Creature` + `CreatureType` + `CreatureFamily` inkl. `_DataSource`. |
