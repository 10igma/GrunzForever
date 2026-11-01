# Release-Prozess

## 1. Versionierung

- **Semantic Versioning (`v0.Y.Z`)**:
  - `0.Y.0` (Minor): Neuer WoW-Forever-Client-Build (z. B. `1.60.1.70707` → `1.60.1.708xx`) oder neue CLI-/Core-Features.
  - `0.Y.Z` (Patch): Grössere unterjährige `DBCache.bin`-Hotfix-Wellen, CASC-Nachstreaming-Updates oder Bugfixes am Exporter/Differ.
- Die zentrale Versionsnummer steht in [`Directory.Build.props`](../Directory.Build.props) (`<Version>`).

---

## 2. Release-Artefakte

Da die vollständige SQLite-Datenbank (~95 MB unkomprimiert) und der komplette 287-Tabellen-JSON-Dump nicht direkt in Git eingecheckt werden, gehören zu jedem GitHub-Release zwei komprimierte Archive:

| Asset | Inhalt | Erzeugt durch |
| :--- | :--- | :--- |
| `wow_wowf_<build>_sqlite.zip` | `wow_wowf_<build>.sqlite` inkl. aller DB2-Tabellen, Hotfixes, Indizes und Komfort-Views (`v_Items`, `v_Spells`, `v_Creatures`) | `tools/update-forever.ps1 -PackageRelease` |
| `wow_wowf_<build>_json_bundle.zip` | Sämtliche Einzel-JSON-Tabellen aus `Exports/json/`, Lua-API-Exporte und `_ExtractionManifest.json` | `tools/update-forever.ps1 -PackageRelease` |

---

## 3. Schritt-für-Schritt-Checkliste

1. **Client & Hotfix-Cache aktualisieren**:
   - Sicherstellen, dass Battle.net das neueste `_forever_`-Update geladen hat (optional kurz ins Spiel einloggen, damit `DBCache.bin` die neuesten Server-Hotfixes enthält).
2. **Update-Pipeline ausführen**:
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/update-forever.ps1
   ```
3. **Änderungen prüfen & Doku nachziehen**:
   - Generierten Bericht unter `docs/diffs/` prüfen.
   - Kennzahlen in [`README.md`](../README.md) aktualisieren, falls sich Build-Nummer, Tabellen-, Zeilen-, Hotfix- oder API-Zahlen geändert haben.
   - Neuen Abschnitt in [`CHANGELOG.md`](../CHANGELOG.md) eintragen und `<Version>` in [`Directory.Build.props`](../Directory.Build.props) erhöhen.
4. **Privacy- & Clean-Tree-Prüfung**:
   - `git status` prüfen (keine `.sqlite`-, `.zip`- oder persönlichen Dateien getrackt).
   - `dotnet test GrunzForever.slnx -c Release` grün.
5. **Commit, Tag & GitHub-Release veröffentlichen**:
   ```powershell
   git add .
   git commit -m "chore(release): v0.2.0 (wowf build <build>)"
   powershell -ExecutionPolicy Bypass -File tools/update-forever.ps1 -PublishRelease v0.2.0
   Remove-Item artifacts -Recurse -Force -ErrorAction SilentlyContinue
   ```
6. **Nachkontrolle**:
   - GitHub-Actions-Lauf prüfen: `gh run list --limit 3`
   - Release-Seite & ZIP-Assets prüfen: `gh release view v0.2.0`
