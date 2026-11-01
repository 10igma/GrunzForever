# GrunzForever – Arbeitsregeln für Claude

Öffentliches Open-Source-Repository [`10igma/GrunzForever`](https://github.com/10igma/GrunzForever) (MIT-Lizenz): CASC-Data-Mining-Engine, `DBCache.bin`-Hotfix-Merger, Build-Differ und Camelot (`WOW_PROJECT_ID = 18`) Lua-API-Referenz für **World of Warcraft: Classic Forever** (`_forever_`, Produkt `wowf`).

Alle verbindlichen Projektregeln, Roten Linien, Architektur-Entscheide und Update-Abläufe sind zentral dokumentiert in:

- **[`AGENTS.md`](AGENTS.md)** – Rote Linien (Read-Only, Privacy/Scope, keine privaten AddOn- oder Pfad-Leaks, keine Binär-Assets), Stack-Konventionen und Standard-Workflows.
- **[`.agents/skills/forever-update/SKILL.md`](.agents/skills/forever-update/SKILL.md)** – Schritt-für-Schritt-Runbook für die forensische Analyse neuer WoW-Forever-Updates, Diff-Erstellung und GitHub-Releases.
- **[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)** – Binärformate (`CASC v7`, `BLTE`, `TSFM v2`, `WDC5`, `XFTH v9`) & SQLite-Schema.
- **[`docs/CAMELOT-CLIENT.md`](docs/CAMELOT-CLIENT.md)** – Technische Referenz zum Classic-Forever-Client (`Camelot`, `WOW_PROJECT_ID = 18`).
- **[`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md)** & **[`docs/RELEASE.md`](docs/RELEASE.md)** – CLI-Referenz, Entwicklung & Release-Checkliste.

## Schnellbefehle

```powershell
# Bauen & Testen
dotnet build GrunzForever.slnx -c Release
dotnet test GrunzForever.slnx -c Release

# Vollautomatisches Client-Update pruefen, extrahieren & diffen
powershell -ExecutionPolicy Bypass -File tools/update-forever.ps1

# Release-ZIPs fuer GitHub Release bauen
powershell -ExecutionPolicy Bypass -File tools/update-forever.ps1 -PackageRelease
```
