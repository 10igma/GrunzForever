# Changelog

Alle relevanten Änderungen an **GrunzForever** und den dokumentierten **WoW: Classic Forever (`wowf` / `Camelot`)** Builds werden hier festgehalten.

## [0.7.0] - 2026-11-26

### Hinzugefügt
- **Daten-Update (`wowf` Build `1.60.1.71139` + Live-Hotfixes bis `PushId 114448`):**
  - Neuer Build-zu-Build-Diff [`docs/diffs/1.60.1.71037_to_1.60.1.71139.md`](docs/diffs/1.60.1.71037_to_1.60.1.71139.md) und Manifest [`data/manifests/_ExtractionManifest_1.60.1.71139.json`](data/manifests/_ExtractionManifest_1.60.1.71139.json) (`323` Tabellen / `+2` neu: `AnimKitConfig`, `AnimKitConfigBoneSet`, `1,502,345` Datensätze / `+690` Zeilen, `43,948` Valid- & `12,786` Delete-Hotfixes über `72` neue `PushId`-Wellen).
  - Neue Basis-DB2-Einträge in `CriteriaTree` (`+38`), `Criteria` (`+36`), `SpellLearnSpell` (`+7`: `Reagent Economy` lehrt alle 7 Berufs-Passiva `1262636..1262662`, `Goblin Mortar` lehrt `The Mortar: Reloaded`), `ModifierTree` (`+4`), `Cfg_GameRules` (`+2`: `ID 276` & `277`) und `ItemLimitCategory` (`+1`: `ID 721` `Gift of the Wild Gods`).
  - **Balance-Druiden / Moonkin-Rework (`PushId 114344`):** `Moonkin Form` (`24858`) erhöht nun zusätzlich Arkan- und Naturschaden; `Eclipse` (`408248`) erhöht zusätzlich die kritische Trefferchance von `Wrath`; `Dreamstate` (`408258`) gewährt beim Wirken von `Innervate` auf andere Ziele dem Druiden selbst `$s1%` des Effekts; `Improved Wrath` -> `Starlight Wrath` (`16814`, verkürzt Zauberzeit von `Wrath` und `Starfire`); `Improved Moonfire` -> `Moon and Stars` (`16821`, inkl. Pushback-Schutz für `Starfire`).
  - **Alteractal-Update (`PushId 114357`, `114386`, `114435`, `114437`, `+13` neue Spells):** Neue stapelbare Marshall-/Kriegsmeisters-Buffs `Might of the Alliance` (`1334174`) & `Might of the Horde` (`1334175`, je +15 % Leben, Angriffstempo & Schaden pro Stapel + `Remove Stack`), Boss-Fähigkeiten (`Avatar`, `Enrage`, `Whirlwind`, `Cleave`, `Thunderclap`, `Stormbolt`) und Ruf-Zauber (`Alliance/Horde Alterac Valley`).
  - **Weltbuffs & weitere Spells:** `Fengus' Ferocity` (`22817` / `1304759`, `PushId 114329`) gewährt zusätzlich zu `+200` Angriffskraft nun auch `+115` Zauberschaden und `+115` Heilung; alle 8 `Sayge's Dark Fortune`-Dunkelmond-Buffs überarbeitet; Updates für `Will of Shahram`/`Fist of Shahram` (`PushId 114378`), Paladin-/Hexenmeister-Epic-Mounts (`Summon Forsaken Charger`/`Charger`/`Teach Summon Dreadsteed`, `PushId 114349`), `Pauldrons of Shattered Summits`, `Legwraps of Gathering Flame`, `Stealth` & `Hurricane`.
  - `26` modifizierte Items (`SellPrice -> 0` auf allen `20` klassischen Schurken-Giften, `-50 %` `SellPrice` auf Uldaman-Custom-Items `Axe of Lingering Dread`, `Rock Sentinel Slicer`, `Dark Golem Breastplate`, `White Obsidian Wand` via `PushId 114345`, Lockbox-/Grab-Bag-Pass via `PushId 114332` sowie Umbenennung von `272432` `Totem of the Storm` -> `Totem of the Maelstrom`).
- **Build-Konfiguration (`global.json`):** `rollForward` auf `latestMajor` aktualisiert, damit das Projekt auch unter `.NET SDK 11.x` ohne Warnungen baut.

## [0.6.0] - 2026-11-21

### Hinzugefügt
- **Daten-Update (`wowf` Build `1.60.1.71037` + Live-Hotfixes bis `PushId 114326`):**
  - Neuer Build-zu-Build-Diff [`docs/diffs/1.60.1.70959_to_1.60.1.71037.md`](docs/diffs/1.60.1.70959_to_1.60.1.71037.md) und Manifest [`data/manifests/_ExtractionManifest_1.60.1.71037.json`](data/manifests/_ExtractionManifest_1.60.1.71037.json) (`321` Tabellen / `+1` neu: `AreaTriggerSphere`, `1,501,655` Datensätze, `43,149` Valid- & `12,695` Delete-Hotfixes über `67` neue `PushId`-Wellen).
  - Hotfix-Bake-In zahlreicher `70959`-Einträge in die Basis-DB2s (`ItemSparse` `+74` Basiszeilen, `CurvePoint` `+504`, `Curve` `+252`, `LightDataGlobalVolumeFog` `+108`, `LightData` `+36`, `SpellEffect` `+14`, `Criteria` `+13`, `Spell`/`SpellName`/`SpellMisc` `+11`).
  - `+7` neu gecachte Items (`219326` `Seed of Renewal`, `18673` `Avalanchion's Stony Hide`, `11423` `Gnome Engineer's Renewal Gift`, `11422` `Goblin Engineer's Renewal Gift`, `291874` `Ruined Herb Seeds`, `286640` `High Priestess's Kilt`, `11603` `Vilerend Slicer`) sowie `73` modifizierte Items (`iLvl 61 -> 60` und Stat-Rebalancing auf `72` Klassen-Setteilen: *Spiritcaller*, *of Glory*, *Demonheart*, *Grimstitch*, *of Conviction*, *Justice*, *Manaflare*, *Wildstalker's*, *Grovekeeper* inkl. zweitem Feintuning-Pass via `PushId 114318`; `219343` `Filcher's Cowl` `RequiredLevel 50 -> 0` via `PushId 114312`; Koch-Rezepte *Undermine Clam Chowder*, *Goblin Deviled Clams*, *Clam Chowder*, *Boiled Clams* & *Murloc Fin Soup* via `PushId 114294`).
  - `+10` neue Spells (`Hate to Zero`, `Demoralizing Roar`, `Berserk`, `Swipe`, `Moonfire`, `Wrath`, `Umbral Pull`, `Eternal Night`, `Fixate`, `Thorns Aura`) und `8` modifizierte Spells (`Revitalize`, `Shank` Ränge 1 & 2, `Increased Feral Combat`, `Wrath`, `Wither`, `Bloom`, `Tranquility`) sowie `SpellMisc`/`SpellEffect`-Hotfixes für `Battle Shout` (`PushId 114326`), `Execute` Ränge 1–5 & `Judgement` (`PushId 114313`), `Mind Vision` (`PushId 114310`), `Chastise` Ränge 1–5 (`PushId 114309`), `Retaliate`/`Devastate`/`Raging Blow`/`Holy Fire`/`Flamebreak` (`PushId 114285`) und `Judgement of Justice/Wisdom/Light` (`PushId 114283`).
  - Aktualisierte Camelot-Lua-API-Referenz (`6,614` Funktionen, `+1` neu: `C_BattleNet.IsGameAccountOnSameRealm` sowie neues Feld `hasRealmInfo` in `BNetGameAccountInfo`).

## [0.5.0] - 2026-11-18

### Hinzugefügt
- **Daten-Update (`wowf` Build `1.60.1.70959` + Live-Hotfixes):**
  - Neuer Build-zu-Build-Diff [`docs/diffs/1.60.1.70888_to_1.60.1.70959.md`](docs/diffs/1.60.1.70888_to_1.60.1.70959.md) und Manifest [`data/manifests/_ExtractionManifest_1.60.1.70959.json`](data/manifests/_ExtractionManifest_1.60.1.70959.json) (`320` Tabellen / `+5` neu: `ClientSettings`, `CombatCondition`, `ContentTuningXExpected`, `ExpectedStatMod`, `SpellAuraVisibility`, `1,501,535` Datensätze / `+2,297` Zeilen, `42,302` Valid- & `12,536` Delete-Hotfixes).
  - Neue `Cfg_GameRules` (`ID 272..274` mit `RuleValue = 1` in DB2 sowie `ID 275` mit `RuleValue = 0` via `PushId 113983`), `AreaTable`-Hotfixes für `Dalaran`, `Winterspring` & `Darkwhisper Gorge` (`PushId 114125`) sowie `Warsong Lumber Mill` & `Silverwing Hold` (`PushId 114114` zusammen mit `Disable ALL Mounts`).
  - `+278` neu gecachte Items (u. a. `Camper's Lure`, `Predator's Shroud`, `Predaceous Band`, `Reforged/Shattered Atal'ai Spear`, `Cannibal's Cleaver`, `Silithid Queen's Husk`, `Unleashed Swarm`, `Scarlet Hearthstone`, `Star of Elune`, `Scales of the Devourer`, `Wit's Edge`, `Band of the Damned`, `Soulwoven Drape` sowie T0.5-Sets *The Five Thunders*/*Soulforge*/*Virtuous*) und `44` modifizierte Items (grosser Tank-Buff auf `Onyxia Blood Talisman`, Stat-Anpassungen auf `19` *Premier*-Mail-Teilen, `Everstaring Abomination Eye`, `Darkspear Signet`, `Souvenir Sea Shell`, `Blessed Band of Light`, `Old Guard Retaliator` auf Rare hochgestuft, `Darkmoon Cards` iLvl `66 -> 65`).
  - `+10` neue Spells (`Disable ALL Mounts`, `Camper's Lure`, `Fishing Skill +60`, `Mark of Flames`, `Smash`, `Howler's Cry`, `Lash of the Dark Rider` u. a.) und `29` modifizierte Spells (Priester-`Vampiric Embrace` mit +100 % Eigenheilung, Schamanen-`Windfury Weapon`/`Flametongue Weapon`/`Windfury Totem` & `Maelstrom Weapon`-Formel, Schurken-T1-5P-Bonus auf *Eviscerate & Death Mark* umgestellt, Jäger-T1-5P-Bonus, `Filcher's Cowl`, `Gloves of Holy Might`-Proc, neue Darkmoon-Card-Effekte für `Lightning Strike` gegen Aquatic und `Heroism` in Strongholds/Cities).
  - Aktualisierte Camelot-Lua-API-Referenz (`6,613` Funktionen, `+2` neu: `C_Mail.IsCODEnabled` und `C_TradeSkillUI.HasNoReagentCost`).

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
