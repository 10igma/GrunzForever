---
name: forever-update
description: >-
  Forensisches Analyse-, Extraktions-, Diff- und Release-Runbook fuer World of Warcraft: Classic Forever (wowf / Camelot)
  Updates in GrunzForever. Immer aktivieren, wenn der Nutzer meldet, dass es ein WoW-Forever-Update / einen Patch /
  einen Hotfix gab, oder wenn ein neues GrunzForever-Release gebaut werden soll.
---

# WoW: Classic Forever – Update-Forensik & Release-Runbook

Nutze dieses Runbook jedes Mal, wenn der Nutzer nach einer Analyse eines neuen **WoW: Classic Forever** (`_forever_`, Produkt `wowf`) Updates fragt oder die Repository-Daten aktualisieren möchte.

---

## 1. Verständnis der drei WoW-Forever-Update-Arten

Bevor du Schlüsse ziehst, prüfe immer, **welche** der drei Update-Arten vorliegt (oft treten mehrere kombiniert auf):

1. **Echter Client-Build-Wechsel (TACT / `.build.info`)**
   - `Version` (z. B. `1.60.1.70707` → `1.60.1.708xx`) und `Build Key` in `..\.build.info` (Zeile `wowf`) sowie `Wow.exe` ändern sich.
   - Neue DB2-Schemas (`LayoutHash`), neue `Interface\AddOns\Blizzard_APIDocumentationGenerated`-Dateien (Lua-API-Änderungen) und neue Basis-DB2-Zeilen.
2. **CASC-Streaming / Launcher-Update ohne Build-Wechsel**
   - `Version` und `Build Key` bleiben identisch, aber Battle.net aktualisiert `World of Warcraft Launcher.exe` oder lädt fehlende CASC-Blöcke in `Data\data\data.*` und `.idx` nach.
   - Dadurch werden DB2-Tabellen oder `enUS`-FileDataIDs im lokalen CASC entsperrt, die im vorherigen Lauf noch nicht lokal gecacht waren (erkennbar an neuen Tabellen in `_TableSummary` trotz identischem `BuildKey`).
3. **Live Server-Hotfix-Push (`DBCache.bin` / `XFTH v9`)**
   - Entsteht beim Einloggen ins Spiel: Der Server pusht Datensätze in `_forever_\Cache\ADB\enUS\DBCache.bin` mit neuen `PushId`-Werten (`Status = 1 Valid` für Upserts, `Status = 2 Delete` für Löschungen).
   - Ändert Items (`ItemSparse` / `Item`), Spells (`SpellName` / `Spell` / `SpellMisc`), Kreaturen oder Titel zur Laufzeit ohne Client-Patch.

---

## 2. Automatisierte Ausführung via `tools/update-forever.ps1`

Führe als ersten Schritt immer das Update-Skript im Repository-Root aus:

```powershell
powershell -ExecutionPolicy Bypass -File tools/update-forever.ps1
```

Was das Skript automatisch erledigt:
- Vergleicht `.build.info` (`wowf`) mit Blizzards Live-EU-TACT-Server (`http://eu.patch.battle.net:1119/wowf/versions`).
  - **Wichtig:** Falls EU-TACT bereits einen neueren Build meldet als lokal in `.build.info` steht, weise den Nutzer sofort darauf hin, kurz den Battle.net-Launcher patchen zu lassen!
- Legt bei **gleicher Build-Nummer** automatisch einen temporären Snapshot (`Exports/_baseline_<ver>_<timestamp>.sqlite`) an, bevor `Exports/wow_wowf_<ver>.sqlite` überschrieben wird — dadurch gehen Intra-Build-/Hotfix-Deltas nie verloren!
- Kompiliert und testet `GrunzForever.slnx` (`Release`), extrahiert alle DB2s + `DBCache.bin` + Camelot-Lua-API und synchronisiert `data/forever/`, `data/lua_api/` und `data/manifests/`.
- Erzeugt den strukturierten Diff unter `docs/diffs/<alt>_to_<neu>.md` (bzw. `docs/diffs/<build>_hotfix_<zeit>.md` bei Intra-Build-Änderungen).

---

## 3. Vertiefte Forensik & Qualitätsprüfung

Nach dem Skript-Lauf:

1. **Git-Diff in `data/` und `docs/diffs/` prüfen:**
   - Prüfe `git status --short` und `git diff --stat data/`.
   - Hat sich `data/lua_api/LuaApi_CamelotSurface.json` oder `LuaApi_Reference.md` geändert? (Neue/entfernte `C_*`-Namespaces, geänderte `SecretArguments`- oder `Restricted`-Flags, geänderte `AllowLoadGameType`-AddOns!)
2. **Gezielte SQL-Abfragen für den Bericht an den Nutzer (`--sql`):**
   ```powershell
   # Neueste Hotfix-Push-Wellen pruefen
   dotnet run --project src/GrunzForever.Cli -c Release --no-build -- --sql "SELECT PushId, Status, COUNT(*) AS Entries FROM _HotfixCacheEntries WHERE PushId > 0 GROUP BY PushId, Status ORDER BY PushId DESC LIMIT 15"

   # Neueste Forever-Items pruefen
   dotnet run --project src/GrunzForever.Cli -c Release --no-build -- --sql "SELECT ID, Name, Quality, ItemLevel, RequiredLevel, _DataSource, _HotfixPushID FROM v_CustomForeverItems ORDER BY COALESCE(_HotfixPushID, 0) DESC, ID DESC LIMIT 25"

   # Neueste Forever-Spells pruefen
   dotnet run --project src/GrunzForever.Cli -c Release --no-build -- --sql "SELECT ID, Name, Subtext, _DataSource, _HotfixPushID FROM v_CustomForeverSpells ORDER BY COALESCE(_HotfixPushID, 0) DESC, ID DESC LIMIT 25"
   ```
3. **Dokumentations-Kennzahlen aktualisieren (bei neuem Build oder geänderten Zahlen):**
   - Aktualisiere die Kennzahlen-Tabelle in `README.md` (Abschnitt *Aktueller Datenstand*) mit den exakten Werten aus `_ExtractionManifest_Latest.json` und `LuaApi_Reference.md`.
   - Ergänze die Änderungen unter `CHANGELOG.md`.
4. **Privacy-Check vor jedem Commit:**
   - Verifiziere, dass `git status` keine `.sqlite`, `.zip`, `Exports/` oder `Cache/`-Dateien enthält und keine lokalen Pfade geleakt sind.

---

## 4. Commit & GitHub-Release (auf Wunsch des Nutzers)

Wenn ein neuer Meilenstein oder Build als GitHub-Release veröffentlicht werden soll:

1. Version in `Directory.Build.props` (`<Version>`) und `CHANGELOG.md` aktualisieren.
2. Alle getrackten Änderungen committen (`git add . && git commit -m "feat(data): update to wowf build <build>"`).
3. Release inkl. komprimierter SQLite- & JSON-ZIP-Bundles veröffentlichen:
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/update-forever.ps1 -SkipExtract -PublishRelease v0.2.0
   ```
4. `artifacts/` danach aufräumen (`Remove-Item artifacts -Recurse -Force`) und GitHub-Actions-CI prüfen (`gh run list`).
