# Entwicklungs- & CLI-Handbuch

## 1. Voraussetzungen

| Komponente | Anforderung |
| :--- | :--- |
| **.NET SDK** | `.NET 10.0.100` oder neuer (gepinnt in `global.json`, `rollForward: latestFeature`) |
| **Betriebssystem** | Windows 10/11, Linux oder macOS (CI testet `ubuntu-latest` & `windows-latest`) |
| **PowerShell** | Windows PowerShell 5.1 oder PowerShell 7+ (für `tools/update-forever.ps1`) |
| **GitHub CLI (`gh`)** | Optional — nur für automatisierte GitHub-Releases (`-PublishRelease`) |
| **WoW: Classic Forever** | Lokale Installation (`_forever_`) nur für neue CASC-/DBCache-Extraktionen nötig; Build, Tests, SQL-Abfragen und Diffs funktionieren auch ohne installiertes Spiel |

---

## 2. Bauen & Testen

```powershell
dotnet build GrunzForever.slnx -c Release
dotnet test GrunzForever.slnx -c Release
```

Die Test-Suite (`tests/GrunzForever.Tests`) erzeugt synthetische BLTE-, XFTH-v9- und SQLite-Testdaten im Arbeitsspeicher/Temp-Ordner und benötigt **keine** lokale WoW-Installation (läuft daher vollständig in GitHub Actions).

---

## 3. Das Automatik-Skript (`tools/update-forever.ps1`)

Für den Alltag nach einem Battle.net-Patch oder In-Game-Hotfix übernimmt `tools/update-forever.ps1` alle Schritte in einem Durchlauf:

```powershell
# Standard-Lauf: Forensik + EU-TACT-Pruefung + Build/Test + Extraktion + Repo-Sync + Diff
powershell -ExecutionPolicy Bypass -File tools/update-forever.ps1

# Expliziten WoW-Pfad angeben und TACT-Online-Check ueberspringen
powershell -ExecutionPolicy Bypass -File tools/update-forever.ps1 -WowDir "D:\Games\World of Warcraft\_forever_" -SkipTactCheck

# Zusaetzlich komprimierte Release-ZIPs unter artifacts/ erzeugen
powershell -ExecutionPolicy Bypass -File tools/update-forever.ps1 -PackageRelease
```

**Besonderheit bei Intra-Build-Updates:**
Wenn die Build-Version in `.build.info` gleich geblieben ist (z. B. reiner Server-Hotfix in `DBCache.bin` oder CASC-Nachstreaming), sichert `update-forever.ps1` vor der Extraktion automatisch die bisherige `Exports/wow_wowf_<build>.sqlite` als temporären Snapshot und erzeugt nur dann einen Diff-Bericht (`docs/diffs/<build>_hotfix_<zeit>.md`), wenn sich tatsächlich Datensätze geändert haben.

---

## 4. Vollständige CLI-Referenz (`grunzforever`)

Alle Befehle können über `dotnet run --project src/GrunzForever.Cli -c Release -- <Optionen>` ausgeführt werden:

| Option | Beschreibung |
| :--- | :--- |
| `--wow-dir <pfad>` | Pfad zum `_forever_`-Ordner (Fallback: Umgebungsvariable `WOW_FOREVER_DIR` oder Auto-Suche). |
| `--out-dir <pfad>` | Zielordner für SQLite- und Voll-JSON-Exporte (Standard: `Exports/`). |
| `--cache-dir <pfad>` | Lokaler Cache für `WoWDBDefs` `.dbd`-Dateien (Standard: `Cache/WoWDBDefs/`). |
| `--sync-repo-data` | Kopiert nach der Extraktion alle kuratierten Forever-Deltas, Manifeste und Lua-API-Dateien direkt nach `data/`. |
| `--all-db2` | Versucht zusätzlich alle aus `WoWDBDefs` bekannten Tabellennamen im Root-Manifest aufzulösen. |
| `--export-lua-api` | Extrahiert ausschliesslich die Camelot-Lua-API und TOC-Metadaten (ohne DB2-Lauf). |
| `--diff <alt.sqlite> <neu.sqlite>` | Vergleicht zwei SQLite-Datenbanken; kombinierbar mit `--out-md <datei>` und `--out-json <datei>`. |
| `--sql "<query>"` | Führt eine Read-Only-SQL-Abfrage auf der neuesten Datenbank in `Exports/` (oder `--db <datei>`) aus. |
| `--db <datei.sqlite>` | Wählt eine spezifische SQLite-Datei für `--sql`. |
| `--inspect-db2 <tabelle>` | Zeigt WDC5-Header, `TableHash`, `LayoutHash`, Sektionen und `.dbd`-Spaltenlayout einer DB2-Tabelle. |
| `--dump-casc <fdid>` | Gibt den Textinhalt einer CASC-Datei anhand ihrer `FileDataID` auf der Konsole aus. |
| `--export-blp <fdid> <ziel.png>` | Dekodiert eine BLP2-Textur (`DXT1`/`DXT3`/`DXT5`/`BGRA8`/`Palette`) per `FileDataID` als PNG. |
| `-h` / `--help` | Zeigt die integrierte Befehlsübersicht. |

---

## 5. Nützliche SQL-Rezepte (`--sql`)

```powershell
# 1. Alle Tabellen mit den meisten aktiven Server-Hotfixes anzeigen
dotnet run --project src/GrunzForever.Cli -c Release -- --sql "SELECT TableName, BaseDb2Rows, HotfixUpserts, HotfixDeletes, FinalRowCount FROM _TableSummary WHERE HotfixUpserts > 0 OR HotfixDeletes > 0 ORDER BY HotfixUpserts DESC LIMIT 20"

# 2. Neue epische/legendaere Forever-Items (Quality >= 4) nach ItemLevel sortiert
dotnet run --project src/GrunzForever.Cli -c Release -- --sql "SELECT ID, Name, Quality, ItemLevel, RequiredLevel, _HotfixPushID FROM v_CustomForeverItems WHERE Quality >= 4 ORDER BY ItemLevel DESC, ID DESC LIMIT 30"

# 3. Charakter-Titel in Classic Forever auflisten
dotnet run --project src/GrunzForever.Cli -c Release -- --sql "SELECT ID, Name_lang, Name1_lang, Mask_ID, _DataSource FROM CharTitles ORDER BY ID"

# 4. Aktive GameRules des Forever-Clients pruefen
dotnet run --project src/GrunzForever.Cli -c Release -- --sql "SELECT * FROM Cfg_GameRules ORDER BY ID LIMIT 40"
```

---

## 6. Code- & Privacy-Konventionen

- **C# 14 / .NET 10**: Dateibezogene Namespaces (`namespace GrunzForever.Core;`), `Nullable=enable`, `InvariantGlobalization=true` (immer `CultureInfo.InvariantCulture` bei Zahlenformatierung verwenden).
- **Privacy by Design**: `WowDatabaseExporter` speichert niemals lokale Dateipfade (`FlavorDirectory` o. ä.) in `_ExtractionMetadata` oder JSON-Manifesten. Dieser Schutz wird durch den Unit-Test `Exporter_ProducesCleanMetadataWithoutLocalPaths` abgesichert.
- **Zeilenenden**: `.gitattributes` erzwingt `LF` für Quellcode/JSON/Markdown und `CRLF` für `.ps1`-Skripte.
