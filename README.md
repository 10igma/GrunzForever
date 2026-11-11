# GrunzForever

CASC-Data-Mining-Engine, Server-Hotfix-Merger (`DBCache.bin`), automatischer Build-Differ und vollständige **Lua-API-Referenz** für **World of Warcraft: Classic Forever** (`wowf`, interner Codename **`Camelot`**, `WOW_PROJECT_ID = 18`).

## Überblick

WoW: Classic Forever kombiniert klassische Inhalte mit der modernen **12.x-Mainline-Client-Architektur** (`WOW_PROJECT_CAMELOT = 18`, `## Interface: 16001`), einschliesslich WDC5-DB2-Tabellen, live vom Server gepushten XFTH-v9-Hotfixes (`DBCache.bin`) und dem neuen SecretValue-/Restriktions-Modell für AddOns.

**GrunzForever** stellt drei zentrale Bausteine bereit:

1. **Kuratierte Forever-Datensätze & Lua-API-Dokumentation ([`data/`](data/))**
   - **[`data/lua_api/LuaApi_Reference.md`](data/lua_api/LuaApi_Reference.md)**: Übersicht über alle `612` API-Systeme, `283` `C_*`-Namespaces, `6,611` Funktionen, `1,816` Events, geschützte/SecretValue-bewachte Aufrufe sowie aktive Blizzard-UI-AddOns und `AllowLoadGameType`-Regeln.
   - **[`data/lua_api/LuaApi_Complete.json`](data/lua_api/LuaApi_Complete.json)** & **[`LuaApi_CamelotSurface.json`](data/lua_api/LuaApi_CamelotSurface.json)**: Maschinenlesbare API-Spezifikation für IDE-Tooling, Linter und statische AddOn-Prüfungen.
   - **[`data/forever/CustomForever_Items.json`](data/forever/CustomForever_Items.json)** & **[`CustomForever_Spells.json`](data/forever/CustomForever_Spells.json)**: Alle per DB2-Delta oder Server-Hotfix (`DBCache.bin`) eingeführten bzw. modifizierten Classic-Forever-Items und -Spells.
   - **[`data/forever/CharTitles.json`](data/forever/CharTitles.json)**: Charakter-Titel inkl. der neuen Classic-Forever-Titel (Handwerksberufe, Fraktions-Clans wie *Magram*/*Gelkis*/*Nightclaw*, *Harbinger of Darkness* u. a.).
2. **Automatische Build-zu-Build-Changelogs ([`docs/diffs/`](docs/diffs/))**
   - Strukturierte Patch-Diffs zwischen Forever-Builds (z. B. [`1.60.1.70862` → `1.60.1.70888`](docs/diffs/1.60.1.70862_to_1.60.1.70888.md) und [`1.60.1.70790` → `1.60.1.70862`](docs/diffs/1.60.1.70790_to_1.60.1.70862.md)) auf Tabellen-, Schema-, Hotfix-Push-, Item-, Spell- und Titel-Ebene.
3. **Cross-Platform .NET 10 Extractor & Diff-CLI (`grunzforever`)**
   - Liest lokale CASC-v7-Archive (`.build.info` → `encoding` → `TSFM v2 root` → `BLTE` → `WDC5`) direkt im Read-Only-Modus und führt sie verlustfrei mit Blizzards `XFTH v9`-Hotfix-Cache (`DBCache.bin`) in einer indizierten SQLite-Datenbank + JSON-Paketen zusammen.
   - Vollständige `.sqlite`-Datenbanken (`315` Tabellen, `1.50M+` Zeilen) stehen komprimiert unter [**GitHub Releases**](https://github.com/10igma/GrunzForever/releases) bereit.

---

## Aktueller Datenstand (`wowf` Build `1.60.1.70888`)

| Kennzahl | Wert |
| :--- | ---: |
| **Produkt / Build** | `wowf` — `1.60.1.70888` (`0597e74b01a3...`) |
| **Projekt-Konstante / TOC** | `WOW_PROJECT_CAMELOT = 18` / `## Interface: 16001` |
| **Extrahierte DB2 + Hotfix-Tabellen** | `315` (`1,499,238` Datensätze) |
| **Aktive Server-Hotfixes (`DBCache.bin`)** | `39,289` Valid + `12,257` Delete |
| **Dokumentierte Lua-API-Systeme** | `612` (`283` `C_*`-Namespaces, `6,611` Funktionen, `1,816` Events) |
| **Blizzard UI-Module in Camelot aktiv** | `307` / `350` (`213` Camelot-spezifische Datei-Overrides) |

---

## Bauen & Testen

Voraussetzung: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet build -c Release
dotnet test -c Release
```

---

## Vollautomatisches Update-Skript (`tools/update-forever.ps1`)

Nach einem Battle.net-Patch oder einem neuen `DBCache.bin`-Hotfix-Schub genügt ein einziger Befehl, um den lokalen Client gegen Blizzards Live-EU-TACT-Endpoint zu prüfen, alle Daten zu extrahieren, `data/` zu synchronisieren und automatisch den Build- bzw. Intra-Build-Hotfix-Diff zu generieren:

```powershell
powershell -ExecutionPolicy Bypass -File tools/update-forever.ps1
```

Optional mit direktem Erzeugen der komprimierten Release-ZIP-Archive (`artifacts/`):

```powershell
powershell -ExecutionPolicy Bypass -File tools/update-forever.ps1 -PackageRelease
```

---

## CLI-Verwendung (`grunzforever`)

### 1. Komplett-Extraktion (CASC DB2 + `DBCache.bin` + Lua-API)

Erkennt lokale WoW-Forever-Installationen (`_forever_`) unter Windows/macOS automatisch oder nutzt `--wow-dir` bzw. `WOW_FOREVER_DIR`:

```bash
dotnet run --project src/GrunzForever.Cli -c Release -- --wow-dir "D:\Games\World of Warcraft\_forever_"
```

Mit `--sync-repo-data` werden die kuratierten JSON-Deltas und Lua-API-Exporte unter `data/` direkt aktualisiert:

```bash
dotnet run --project src/GrunzForever.Cli -c Release -- --sync-repo-data
```

### 2. Patch-Diff & Markdown-Changelog zwischen zwei Builds erzeugen

```bash
dotnet run --project src/GrunzForever.Cli -c Release -- \
  --diff Exports/wow_wowf_1.60.1.70862.sqlite Exports/wow_wowf_1.60.1.70888.sqlite \
  --out-md docs/diffs/1.60.1.70862_to_1.60.1.70888.md \
  --out-json docs/diffs/1.60.1.70862_to_1.60.1.70888.json
```

### 3. Ad-hoc SQL-Abfragen auf der extrahierten SQLite-Datenbank

```bash
dotnet run --project src/GrunzForever.Cli -c Release -- \
  --sql "SELECT ID, Name, Quality, ItemLevel, _HotfixPushID FROM v_CustomForeverItems ORDER BY ID DESC LIMIT 20"
```

### 4. Einzel-Werkzeuge (Lua-API, DB2-Header-Inspektion, CASC-Dump, BLP-zu-PNG)

```bash
# Nur Camelot-Lua-API & TOC-Analyse exportieren
dotnet run --project src/GrunzForever.Cli -c Release -- --export-lua-api

# WDC5-Sektionen und LayoutHash einer DB2-Tabelle inspizieren
dotnet run --project src/GrunzForever.Cli -c Release -- --inspect-db2 ItemSparse

# Textdatei per FileDataID aus lokalem CASC ausgeben
dotnet run --project src/GrunzForever.Cli -c Release -- --dump-casc 8518863

# BLP2-Textur per FileDataID als PNG dekodieren
dotnet run --project src/GrunzForever.Cli -c Release -- --export-blp 135877 icon.png
```

---

## Dokumentation

| Dokument | Inhalt |
| :--- | :--- |
| **[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)** | Technische Pipeline (`CASC v7` → `BLTE` → `TSFM v2` → `WDC5` → `XFTH v9` → SQLite/JSON) & DB-Views |
| **[`docs/CAMELOT-CLIENT.md`](docs/CAMELOT-CLIENT.md)** | Spezifika des Classic-Forever-Clients (`wowf`, `Camelot`, `WOW_PROJECT_ID = 18`, 3 Update-Kanäle, SecretValues) |
| **[`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md)** | Vollständige CLI-Referenz, SQL-Rezepte, `update-forever.ps1`-Optionen & Code-Konventionen |
| **[`docs/RELEASE.md`](docs/RELEASE.md)** | SemVer-Schema, Release-Artefakte & Checkliste |
| **[`AGENTS.md`](AGENTS.md)** | Verbindliche Arbeitsregeln & Kontext für KI-Agenten (Antigravity / Claude) |

---

## Repository-Struktur

```text
GrunzForever/
├── .agents/skills/       # Workspace-Skills (u. a. forever-update Forensik- & Release-Runbook)
├── data/
│   ├── forever/          # CustomForever_Items.json, CustomForever_Spells.json, CharTitles.json, Cfg_*.json
│   ├── lua_api/          # LuaApi_Reference.md, LuaApi_CamelotSurface.json, LuaApi_Complete.json
│   └── manifests/        # Tabellen- & LayoutHash-Manifeste je Build
├── docs/
│   ├── diffs/            # Automatisch generierte Build-zu-Build- & Hotfix-Changelogs (.md & .json)
│   ├── ARCHITECTURE.md   # Binärformate, Merge-Pipeline & SQLite-Schema
│   ├── CAMELOT-CLIENT.md # Technische Referenz zu wowf / WOW_PROJECT_ID = 18
│   ├── DEVELOPMENT.md    # CLI-Handbuch & SQL-Abfrage-Rezepte
│   └── RELEASE.md        # Release-Ablauf & ZIP-Packaging
├── src/
│   ├── GrunzForever.Core/# CASC v7, BLTE, Root v2, WDC5, XFTH v9, SQLite/JSON Exporter, BuildDiffer, LuaApiExporter
│   └── GrunzForever.Cli/ # Kommandozeilen-Werkzeug (grunzforever)
├── tests/
│   └── GrunzForever.Tests/# xUnit-Tests für Decoder, Merger, Differ, API-Parser und Daten-Integrität
└── tools/
    └── update-forever.ps1# Vollautomatisches Forensik-, Extraktions-, Diff- & Release-Skript
```

---

## Lizenz & Hinweis

- Der Quellcode dieses Projekts steht unter der [MIT-Lizenz](LICENSE) (`Copyright (c) 2026 10igma`).
- Dieses Projekt enthält **keine** ausführbaren Spiel-Binärdateien, Texturen, Modelle oder Audiodateien. *World of Warcraft* und *Blizzard Entertainment* sind Marken oder eingetragene Marken von Blizzard Entertainment, Inc. Dieses Projekt steht in keiner Verbindung zu Blizzard Entertainment.
