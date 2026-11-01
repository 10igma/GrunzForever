# GrunzForever – Arbeitsregeln & Projekt-Kontext (AGENTS)

Öffentliches Open-Source-Repository [`10igma/GrunzForever`](https://github.com/10igma/GrunzForever) (MIT-Lizenz).
Data-Mining-Engine, Server-Hotfix-Merger (`DBCache.bin`), automatischer Build-zu-Build-Differ und vollständige **Camelot (`WOW_PROJECT_ID = 18`) Lua-API-Referenz** für **World of Warcraft: Classic Forever** (`_forever_`, Produkt `wowf`).

## Rote Linien (nicht verhandelbar)

1. **Rein passives Datei-Lesen (Read-Only):**
   - `GrunzForever` liest ausschliesslich statische Client-Dateien auf der Festplatte (`.build.info`, `.flavor.info`, CASC-Archive unter `Data/`, `Cache/ADB/*/DBCache.bin`) im Read-Only-Modus (`FileAccess.Read`, `FileShare.ReadWrite`).
   - **Niemals** Memory Reading, Prozess-Handles auf `Wow.exe`, Hooks, Injection, Netzwerk-Interception oder UI-Automation einbauen.
2. **Strikte Scope- & Privacy-Trennung:**
   - Dieses Repo ist **öffentlich** (`https://github.com/10igma/GrunzForever`).
   - **Keine** privaten AddOns, Simulatoren oder Verweise auf private Nachbar-Repositories, **keine** Account-IDs, Charakter-/Gildennamen, `SavedVariables` oder lokalen Benutzerpfade (`C:\Users\...`, `FlavorDirectory`) committen oder in `.sqlite`/`.json`-Artefakte schreiben.
   - **Keine** urheberrechtlich geschützten Spiel-Binärdateien (`Wow.exe`), Texturen (`.blp`/`.png`), Modelle (`.m2`/`.wmo`) oder Audiodateien ins Git-Repository committen.
   - Beobachtungen aus privaten/geschlossenen Beta-Clients (`_classic_beta_`, `WowB.exe`) gehören **nicht** in dieses öffentliche Repository — nur Daten des öffentlichen `_forever_`-Clients (`wowf`).
3. **GitHub-Dateigrössen & Artefakte:**
   - Grosse Voll-Exporte (`Exports/*.sqlite` ~95 MB, `Exports/json/`, `Cache/`, `artifacts/*.zip`) bleiben strikt in `.gitignore` ausgeschlossen und werden ausschliesslich komprimiert als GitHub-Release-Assets (`wow_wowf_<build>_sqlite.zip`, `wow_wowf_<build>_json_bundle.zip`) veröffentlicht.

## Verbindliche Dokumentation

- [`README.md`](README.md) – Projektübersicht, aktuelle Build-Kennzahlen & Schnellstart.
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) – Technische Pipeline (CASC v7 → BLTE → Root v2 → WDC5 → XFTH v9 → SQLite/JSON → Differ) & Datenbank-Schema.
- [`docs/CAMELOT-CLIENT.md`](docs/CAMELOT-CLIENT.md) – Spezifika des Classic-Forever-Clients (`wowf`, `Camelot`, `WOW_PROJECT_ID = 18`, Update-Mechanismen, SecretValues, Custom-ID-Bereiche).
- [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md) – Build, Tests, alle CLI-Kommandos & Erweiterungs-Guide.
- [`docs/RELEASE.md`](docs/RELEASE.md) – Versionierung, Release-Checkliste & Asset-Packaging.
- [`CHANGELOG.md`](CHANGELOG.md) – Versionshistorie je Release.

## Repository-Aufbau

- `src/GrunzForever.Core` – Reine Klassenbibliothek ohne externe Binär-Abhängigkeiten (nur `Microsoft.Data.Sqlite`):
  `CascStorage`, `BlteDecoder`, `RootManifest`, `Wdc5Reader`, `DbdSchemaProvider`, `HotfixCacheReader`, `WowDatabaseExporter`, `WowMiningPipeline`, `LuaApiExporter`, `WowBuildDiffer`, `BlpDecoder`.
- `src/GrunzForever.Cli` – Konsolenwerkzeug (`grunzforever`), steuert Extraktion, `--sync-repo-data`, `--diff`, `--sql`, `--export-lua-api`, `--inspect-db2`, `--dump-casc` und `--export-blp`.
- `tests/GrunzForever.Tests` – xUnit-Tests für Binär-Decoder, Hotfix-Merge, SQLite-Export (inkl. Privacy-Check ohne Pfad-Leaks) und `WowBuildDiffer`.
- `tools/update-forever.ps1` – Vollautomatisches Forensik-, Update-, Diff- und Release-Skript.
- `data/forever/` – Versionierte Forever-spezifische JSON-Exporte (`CustomForever_Items.json`, `CustomForever_Spells.json`, `CharTitles.json`, `Creatures_Complete.json`, `Cfg_*.json`, `ChrClasses.json`, `ChrRaces.json`, `SkillLine.json`, `SpellRange.json`).
- `data/lua_api/` – Camelot-Lua-API-Referenz (`LuaApi_Reference.md`, `LuaApi_CamelotSurface.json`, `LuaApi_Complete.json`).
- `data/manifests/` – Tabellen- & `LayoutHash`-Manifeste pro Build (`_ExtractionManifest_<build>.json`).
- `docs/diffs/` – Automatisch generierte Build-zu-Build- und Hotfix-Changelogs (`.md` & `.json`).

## Stack & Konventionen

- `.NET 10` (`net10.0`), C# 14, `GrunzForever.slnx`, `Nullable=enable`, `ImplicitUsings=enable`, `InvariantGlobalization=true`.
- Zeilenenden gemäss `.gitattributes`: `LF` für `.cs`, `.md`, `.json`, `.yml`, `.slnx`; `CRLF` für `.ps1` und `.sln`.
- PowerShell-Skripte unter `tools/` müssen sowohl in Windows PowerShell 5.1 als auch PowerShell 7 fehlerfrei laufen (keine rohen Umlaute ohne Encoding-Schutz in `.ps1`).
- Kommunikation mit dem Nutzer auf **Deutsch** (prägnant, mit klickbaren Markdown-Links); Quellcode-Bezeichner auf Englisch.
- Vor jedem Commit müssen `dotnet build -c Release` (0 Warnungen, 0 Fehler) und `dotnet test -c Release` grün sein.

## Standard-Workflow bei WoW-Forever-Updates

Wenn der Nutzer meldet, dass WoW: Classic Forever ein Update erhalten hat, nutze den Workspace-Skill `forever-update` (`.agents/skills/forever-update/SKILL.md`) bzw. führe das Update-Skript aus:

```powershell
powershell -ExecutionPolicy Bypass -File tools/update-forever.ps1
```

Beachte dabei die **drei Update-Arten** von WoW: Classic Forever:
1. **Neuer Client-Build** (`.build.info` `Version` / `Build Key` geändert, z. B. `1.60.1.70685` → `1.60.1.70707`).
2. **CASC-Streaming / Launcher-Update bei gleichem Build** (`Version` unverändert, aber neue EKeys/Archive in `Data/data/*.idx` freigeschaltet → zusätzliche DB2-Tabellen oder Lokalisierungs-FDIDs werden lesbar).
3. **Live Server-Hotfix-Welle** (`Cache/ADB/enUS/DBCache.bin` aktualisiert → neue `PushId`-Einträge für Items, Spells, Kreaturen oder Titel ohne Client-Patch).
