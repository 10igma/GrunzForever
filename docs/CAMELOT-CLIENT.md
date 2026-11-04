# Der WoW: Classic Forever Client (`wowf` / `Camelot`)

Dieses Dokument fasst alle durch Data-Mining verifizierten technischen Eigenheiten des **World of Warcraft: Classic Forever** Clients zusammen — als Referenz für AddOn-Entwickler, Theorycrafter und Daten-Analysten.

---

## 1. Projekt-Identifikatoren & Client-Architektur

Obwohl *Classic Forever* inhaltlich in der klassischen Welt von Azeroth (`Expansion 0`) angesiedelt ist, läuft der Client technisch auf einem modernen **12.x-Mainline-Codebase-Zweig** mit eigenem internen Projekt-Codenamen **`Camelot`**:

| Eigenschaft | Wert | Quelle im Client |
| :--- | :--- | :--- |
| **TACT-Produktcode** | `wowf` | `.flavor.info`, `.build.info`, `eu.patch.battle.net:1119/wowf` |
| **Installationsordner** | `_forever_` (`Wow.exe`) | Battle.net Desktop App |
| **Interner Codename** | `Camelot` | `Blizzard_SharedXML`, TOC `AllowLoadGameType: camelot` |
| **`WOW_PROJECT_ID`** | `18` (`WOW_PROJECT_CAMELOT`) | Globale Lua-Konstanten / `FrameXML` |
| **`LE_EXPANSION_LEVEL_CURRENT`** | `0` (`LE_EXPANSION_CLASSIC`) | Client-Globals |
| **TOC `## Interface`** | `16001` (entspricht Version `1.60.1`) | `Blizzard_*.toc` |
| **DB2-Binärformat** | `WDC5` | Identisch zu modernen Retail-/11.x-/12.x-Clients |
| **Hotfix-Cache-Format** | `XFTH` Version `9` (`DBCache.bin`) | `_forever_/Cache/ADB/<locale>/DBCache.bin` |

---

## 2. Die drei Update-Kanäle von Classic Forever

Bei der Analyse von Updates ist entscheidend, dass Blizzard Änderungen für *Classic Forever* über **drei unabhängige Kanäle** ausliefert:

### Kanal A: Voller Client-Build-Patch (TACT `versions`)
- Sichtbar in `.build.info` (`Version` und `Build Key` ändern sich, z. B. `1.60.1.70685` → `1.60.1.70707`) sowie am öffentlichen Endpoint `http://eu.patch.battle.net:1119/wowf/versions`.
- Bringt neue `Wow.exe`-Kompilate, geänderte DB2-Tabellenstrukturen (`LayoutHash`-Wechsel), neue Basis-DB2-Zeilen und aktualisierte `Blizzard_APIDocumentationGenerated`-Lua-Dateien.

### Kanal B: CASC-Nachstreaming / Launcher-Update bei identischem Build
- `Version` und `Build Key` bleiben **unverändert**, aber Battle.net lädt zusätzliche CASC-Blöcke in `Data/data/data.NNN` nach und schreibt neue `.idx`-Generationen.
- Dadurch werden FileDataIDs (darunter auch DB2-Tabellen wie `CharTitles`, `ItemXBonusTree` oder `SpellRange`), die zwar schon im Root-Manifest referenziert waren, aber lokal noch nicht im CASC-Cache lagen, plötzlich extrahierbar.

### Kanal C: Live Server-Hotfixes (`DBCache.bin` / `PushId`)
- Findet **ohne** Battle.net-Download direkt beim Login / Spielen statt.
- Der Realm-Server überträgt geänderte oder komplett neue Datensätze (Items, Spells, Kreaturen, GameRules, Titel) in `_forever_/Cache/ADB/enUS/DBCache.bin`.
- Jeder Hotfix-Schub besitzt eine eindeutige **`PushId`** (z. B. `PushId > 100000`), anhand derer `GrunzForever` Hotfix-Wellen chronologisch gruppiert und in Changelogs ausweist.
- **Praxis-Tipp:** Für den vollständigsten Datenstand vor einem `update-forever.ps1`-Lauf einmal kurz mit einem Charakter in Classic Forever einloggen, damit der Client die neuesten Server-Hotfixes in `DBCache.bin` persistiert!

---

## 3. AddOn-Umgebung, `AllowLoadGameType` & SecretValues

### 3.1 TOC-Steuerung für AddOns
Blizzards eigene UI-Module unter `Interface/AddOns/Blizzard_*` steuern ihre Aktivierung über den TOC-Header `## AllowLoadGameType`:
- Module mit `camelot`, `classic` oder `all` werden in Classic Forever geladen (`307` von `350` Blizzard-AddOns in Build `1.60.1.70790`).
- Für zahlreiche Module existieren eigene `*_Camelot.lua`- und `*_Camelot.xml`-Dateien (`213` Camelot-spezifische Datei-Overrides), die klassische Mechaniken (z. B. Talentbäume, Spellbook, Aktionsleisten-Rangprüfung, Charakterfenster, Gilden-UI) auf der modernen 12.x-Engine abbilden.
- Eigene AddOns für Classic Forever sollten im `.toc` angeben:
  ```ini
  ## Interface: 16001
  ```

### 3.2 `SecretValue`- & Restriktions-Modell (12.x-Engine)
Da `Camelot` auf der 12.x-Architektur basiert, enthält die Lua-API das moderne Restriktions- und SecretValue-System (`C_Secrets`, `C_RestrictedActions`, `issecretvalue`, `canaccessvalue`):
- Funktionen und Events in [`data/lua_api/LuaApi_CamelotSurface.json`](../data/lua_api/LuaApi_CamelotSurface.json) tragen explizite Annotationen wie `SecretArguments`, `SecretReturns` oder Restriktions-Flags.
- AddOn-Autoren können in [`data/lua_api/LuaApi_Reference.md`](../data/lua_api/LuaApi_Reference.md) vorab prüfen, welche `C_*`-Systeme in `Camelot` verfügbar sind und welche Parameter SecretValue-Regeln unterliegen.

---

## 4. Erkennung von Custom-Forever-Inhalten in den Daten

Classic-Forever-spezifische Inhalte lassen sich in der SQLite-Datenbank und in `data/forever/` über zwei komplementäre Kriterien identifizieren:

1. **Hotfix-Herkunft (`_DataSource != 'Db2'`)**:
   - Alle Datensätze mit `_DataSource IN ('HotfixInserted', 'HotfixModified')` stammen direkt aus den aktiven Forever-Server-Overlays (`CustomForever_Items.json`, `CustomForever_Spells.json`).
2. **Hohe ID-Bereiche in den Basis-DB2s**:
   - In einen neuen Build übernommene Forever-/Season-/Camelot-Inhalte wandern oft von `DBCache.bin` fest in die Basis-DB2-Dateien:
     - **Items (`ItemSparse` / `Item`)**: Klassische Era-Items liegen typischerweise unter `ID < 25000`; neuere Classic-/Season-/Forever-Items liegen überwiegend bei `ID >= 200000`.
     - **Spells (`SpellName` / `Spell`)**: Klassische Era-Spells liegen meist unter `ID < 35000`; neue Runen-, Klassen- und Forever-Spells liegen bei `ID >= 350000` bis `1250000+`.
     - **CharTitles (`CharTitles`)**: Klassische PvP-Ränge belegen `ID 1..28`; neue Forever-Titel (Handwerksmeister, Fraktions-Clans, Raid-Errungenschaften wie *Scourge of Naxxramas*, *Cryptbreaker*, *Harbinger of Darkness*) liegen ab `ID >= 29` bzw. in höheren ID-Blöcken.
