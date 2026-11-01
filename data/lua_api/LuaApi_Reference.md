# WoW Classic Forever (`Camelot`) — Lua API & UI Surface Reference

- **Produkt / Build**: `wowf` — `1.60.1.70707` (`WOW-70707patch1.60.1_Retail`)
- **Projekt-Konstante**: `WOW_PROJECT_CAMELOT = 18` (`WOW_PROJECT_ID = 18`)
- **UI-Architektur**: `[Family] = Mainline` (`mainline`) | `[Game] = Camelot` (`camelot`)
- **Dokumentierte API-Systeme**: **612** (`283` `C_*` Namespaces)
- **Lua-Funktionen gesamt**: **6,609** (`4,397` Namespaced + `2,212` Global)
- **Events gesamt**: **1,816** | **Enums/Structs/Konstanten**: **1,758**
- **Blizzard UI-AddOns in Camelot aktiv**: **307** von `350` (`212` Camelot-spezifische Lua/XML-Overrides)

## 1. Camelot-Exklusive & Hervorgehobene Blizzard UI-Module

| Blizzard AddOn | AllowLoadGameType | LoadOnDemand | Camelot-Overrides |
| :--- | :--- | :---: | ---: |
| `Blizzard_UIPanels_Game` | `(alle / Mainline)` | Nein | 21 |
| `Blizzard_Minimap` | `(alle / Mainline)` | Nein | 14 |
| `Blizzard_UnitFrame` | `(alle / Mainline)` | Nein | 14 |
| `Blizzard_GlueXML` | `(alle / Mainline)` | Nein | 12 |
| `Blizzard_FrameXML` | `(alle / Mainline)` | Nein | 9 |
| `Blizzard_PlayerSpells` | `(alle / Mainline)` | Ja | 8 |
| `Blizzard_SettingsDefinitions_Frame` | `(alle / Mainline)` | Nein | 8 |
| `Blizzard_InspectUI` | `(alle / Mainline)` | Ja | 7 |
| `Blizzard_Collections` | `wrath, cata, mists, mainline` | Ja | 6 |
| `Blizzard_ActionBar` | `(alle / Mainline)` | Nein | 5 |
| `Blizzard_FrameXMLUtil` | `(alle / Mainline)` | Nein | 5 |
| `Blizzard_GamepadActionBars` | `camelot` | Nein | 5 |
| `Blizzard_PVPMatch` | `standard, camelot` | Nein | 5 |
| `Blizzard_CharacterCreate` | `(alle / Mainline)` | Nein | 4 |
| `Blizzard_EditMode` | `(alle / Mainline)` | Nein | 4 |
| `Blizzard_Game` | `(alle / Mainline)` | Nein | 4 |
| `Blizzard_NamePlates` | `(alle / Mainline)` | Nein | 4 |
| `Blizzard_NewPlayerExperience` | `(alle / Mainline)` | Ja | 4 |
| `Blizzard_Professions` | `mainline` | Ja | 4 |
| `Blizzard_SharedXML` | `(alle / Mainline)` | Nein | 4 |
| `Blizzard_WorldMap` | `(alle / Mainline)` | Nein | 4 |
| `Blizzard_CharacterCustomize` | `(alle / Mainline)` | Ja | 3 |
| `Blizzard_MicroMenu` | `(alle / Mainline)` | Nein | 3 |
| `Blizzard_ProfessionsTemplates` | `(alle / Mainline)` | Ja | 3 |
| `Blizzard_Settings_Shared` | `(alle / Mainline)` | Nein | 3 |
| `Blizzard_StatusTrackingBar` | `(alle / Mainline)` | Nein | 3 |
| `Blizzard_CharacterSelectNavBar` | `(alle / Mainline)` | Nein | 2 |
| `Blizzard_ChatFrameBase` | `(alle / Mainline)` | Nein | 2 |
| `Blizzard_DeclensionFrame` | `(alle / Mainline)` | Nein | 2 |
| `Blizzard_FriendsFrame` | `mainline` | Nein | 2 |
| `Blizzard_GlueParent` | `(alle / Mainline)` | Nein | 2 |
| `Blizzard_MainMenuBarBagButtons` | `(alle / Mainline)` | Nein | 2 |
| `Blizzard_ProfessionsBook` | `(alle / Mainline)` | Ja | 2 |
| `Blizzard_StableUI` | `standard, camelot` | Nein | 2 |
| `Blizzard_Statistics` | `camelot` | Nein | 2 |
| `Blizzard_TokenUI` | `(alle / Mainline)` | Nein | 2 |
| `Blizzard_DamageMeter` | `standard, camelot` | Nein | 1 |
| `Blizzard_CooldownViewer` | `standard, camelot` | Nein | 0 |
| `Blizzard_CovenantToasts` | `standard, camelot` | Nein | 0 |
| `Blizzard_DelvesCompanionConfiguration` | `standard, camelot` | Nein | 0 |
| `Blizzard_Gamepad` | `camelot` | Nein | 0 |
| `Blizzard_GamepadTargeting` | `camelot` | Nein | 0 |
| `Blizzard_GroupFinder_VanillaStyle` | `classic, camelot` | Ja | 0 |
| `Blizzard_GuildRename` | `standard, camelot` | Nein | 0 |
| `Blizzard_HardcoreUI` | `vanilla, camelot` | Nein | 0 |
| `Blizzard_IslandsPartyPoseUI` | `standard, camelot` | Ja | 0 |
| `Blizzard_IslandsQueueUI` | `standard, camelot` | Ja | 0 |
| `Blizzard_LegacyChallengeTracker` | `camelot` | Nein | 0 |
| `Blizzard_LegacySystem` | `camelot` | Ja | 0 |
| `Blizzard_MoneyReceipt` | `standard, camelot` | Nein | 0 |
| `Blizzard_PetBattleUI` | `standard, camelot, mists` | Nein | 0 |
| `Blizzard_QuestTimer` | `classic, camelot` | Nein | 0 |
| `Blizzard_RestrictedAddOnEnvironment` | `classic, standard, camelot` | Nein | 0 |
| `Blizzard_SwingTimer` | `camelot` | Nein | 0 |
| `Blizzard_Tutorials` | `standard, camelot` | Nein | 0 |

## 2. Sicherheits-, Hardware-Event- & Secret-Value-Modell (Engine 12.0)

| Attribut / Guard | Anzahl Funktionen |
| :--- | ---: |
| `SecretArguments` | 3,940 |
| `MayReturnNothing` | 669 |
| `HasRestrictions` | 280 |
| `SecretReturnsForAspect` | 93 |
| `RequiresClubsInitialized` | 84 |
| `IsProtectedFunction` | 74 |
| `SecretWhenUnitStatsRestricted` | 63 |
| `ChecksForbiddenAspects` | 59 |
| `SecretArgumentsAddAspect` | 56 |
| `RequiresActiveCommentator` | 53 |
| `ConstSecretAccessor` | 46 |
| `SecretInChatMessagingLockdown` | 38 |
| `RequiresFriendList` | 32 |
| `SecretWhenUnitIdentityRestricted` | 32 |
| `SecureHooksAllowed` | 26 |
| `RequiresValidActionSlot` | 25 |
| `RequiresUnitAuraAccess` | 24 |
| `SecretWhenAnchoringSecret` | 23 |
| `SecretWhenUnitAuraRestricted` | 23 |
| `CheckAllowChangeParent` | 20 |
| `SecretReturns` | 19 |
| `ReturnsNeverSecret` | 16 |
| `SecretWhenCooldownsRestricted` | 15 |
| `RequiresValidTimelineEvent` | 12 |
| `RequiresRecentAllies` | 12 |

## 3. Alle `C_*` Namespaces nach Funktionsumfang

| Namespace | System-Datei | Funktionen | Events | Enums/Structs |
| :--- | :--- | ---: | ---: | ---: |
| `C_Commentator` | `CommentatorFrameDocumentation.lua` | 144 | 10 | 12 |
| `C_Item` | `ItemDocumentation.lua` | 121 | 19 | 3 |
| `C_PvP` | `PvpInfoDocumentation.lua` | 114 | 43 | 28 |
| `C_QuestLog` | `QuestLogDocumentation.lua` | 93 | 17 | 10 |
| `C_Calendar` | `CalendarDocumentation.lua` | 90 | 13 | 12 |
| `C_TradeSkillUI` | `TradeSkillUIDocumentation.lua` | 89 | 20 | 0 |
| `C_AuctionHouse` | `AuctionHouseDocumentation.lua` | 86 | 47 | 16 |
| `C_Club` | `ClubDocumentation.lua` | 84 | 30 | 24 |
| `C_TransmogCollection` | `TransmogItemsDocumentation.lua` | 84 | 0 | 8 |
| `C_TooltipInfo` | `TooltipInfoDocumentation.lua` | 82 | 3 | 0 |
| `C_VoiceChat` | `VoiceChatDocumentation.lua` | 82 | 51 | 10 |
| `C_ActionBar` | `ActionBarFrameDocumentation.lua` | 72 | 15 | 1 |
| `C_Spell` | `SpellDocumentation.lua` | 71 | 6 | 3 |
| `C_TransmogOutfitInfo` | `TransmogOutfitInfoDocumentation.lua` | 67 | 8 | 12 |
| `C_ArtifactUI` | `ArtifactUIDocumentation.lua` | 62 | 10 | 7 |
| `C_Housing` | `HousingUIDocumentation.lua` | 62 | 39 | 2 |
| `C_ClubFinder` | `ClubFinderDocumentation.lua` | 55 | 15 | 10 |
| `C_PartyInfo` | `PartyInfoDocumentation.lua, PartyInfoSystemStatusDocumentation.lua` | 55 | 33 | 1 |
| `C_Traits` | `SharedTraitsDocumentation.lua` | 53 | 15 | 16 |
| `C_LFGList` | `LFGListInfoDocumentation.lua` | 52 | 17 | 11 |
| `C_SpellBook` | `SpellBookDocumentation.lua` | 51 | 19 | 2 |
| `C_MountJournal` | `MountJournalDocumentation.lua` | 50 | 4 | 3 |
| `C_Container` | `ContainerDocumentation.lua` | 47 | 17 | 5 |
| `C_ChatInfo` | `ChatInfoDocumentation.lua` | 45 | 102 | 2 |
| `C_CurrencyInfo` | `CurrencyInfoDocumentation.lua` | 43 | 8 | 6 |
| `C_TransmogSets` | `TransmogSetsDocumentation.lua` | 42 | 0 | 2 |
| `C_UIWidgetManager` | `UIWidgetManagerDocumentation.lua` | 42 | 2 | 91 |
| `C_HousingLayout` | `HousingLayoutUIDocumentation.lua` | 41 | 18 | 0 |
| `C_DelvesUI` | `DelvesUIDocumentation.lua` | 40 | 6 | 5 |
| `C_Garrison` | `GarrisonInfoDocumentation.lua` | 40 | 65 | 20 |
| `C_Map` | `MapDocumentation.lua` | 40 | 7 | 6 |
| `C_PlayerInfo` | `PlayerInfoDocumentation.lua, PlayerLocationDocumentation.lua` | 40 | 0 | 2 |
| `C_GuildInfo` | `GuildInfoDocumentation.lua` | 39 | 33 | 2 |
| `C_Soulbinds` | `SoulbindsDocumentation.lua` | 39 | 13 | 6 |
| `C_CatalogShop` | `CatalogShopDocumentation.lua` | 38 | 18 | 13 |
| `C_UnitAuras` | `UnitAuraDocumentation.lua` | 38 | 6 | 0 |
| `C_CraftingOrders` | `CraftingOrderUIDocumentation.lua` | 36 | 24 | 2 |
| `C_BattleNet` | `BattleNetDocumentation.lua` | 35 | 0 | 4 |
| `C_HousingDecor` | `HousingDecorUIDocumentation.lua` | 35 | 11 | 2 |
| `C_EncounterTimeline` | `EncounterTimelineDocumentation.lua` | 34 | 11 | 4 |
| `C_HousingCatalog` | `HousingCatalogUIDocumentation.lua` | 34 | 8 | 10 |
| `C_FriendList` | `FriendListDocumentation.lua` | 32 | 27 | 4 |
| `C_HousingCustomizeMode` | `HousingCustomizeModeUIDocumentation.lua` | 32 | 6 | 4 |
| `C_ClassTalents` | `ClassTalentsDocumentation.lua` | 31 | 8 | 2 |
| `C_SpecializationInfo` | `SpecializationInfoDocumentation.lua` | 31 | 10 | 6 |
| `C_GameRules` | `GameRulesDocumentation.lua` | 30 | 7 | 3 |
| `C_HousingNeighborhood` | `HousingNeighborhoodUIDocumentation.lua` | 30 | 13 | 1 |
| `C_AddOns` | `AddOnsDocumentation.lua` | 29 | 3 | 2 |
| `C_BarberShop` | `BarberShopDocumentation.lua` | 29 | 7 | 0 |
| `C_ChallengeMode` | `ChallengeModeInfoDocumentation.lua` | 29 | 14 | 7 |
| `C_Intl` | `IntlDocumentation.lua` | 27 | 0 | 0 |
| `C_ProfSpecs` | `ProfessionSpecUIDocumentation.lua` | 27 | 2 | 0 |
| `C_Reputation` | `ReputationInfoDocumentation.lua` | 27 | 1 | 2 |
| `C_Secrets` | `SecretPredicateAPIDocumentation.lua` | 27 | 0 | 0 |
| `C_Bank` | `BankDocumentation.lua` | 26 | 7 | 3 |
| `C_GamePad` | `GamePadDocumentation.lua` | 25 | 6 | 9 |
| `C_PetJournal` | `PetJournalInfoDocumentation.lua` | 25 | 15 | 3 |
| `C_PerksProgram` | `PerksProgramDocumentation.lua` | 24 | 15 | 6 |
| `C_EquipmentSet` | `EquipmentManagerDocumentation.lua` | 23 | 4 | 0 |
| `C_LFGInfo` | `LFGInfoDocumentation.lua` | 23 | 31 | 2 |
| `C_Minimap` | `MinimapDocumentation.lua` | 23 | 4 | 2 |
| `C_EncounterJournal` | `EncounterJournalDocumentation.lua` | 22 | 3 | 5 |
| `C_HousingBasicMode` | `HousingBasicModeUIDocumentation.lua` | 22 | 6 | 1 |
| `C_AzeriteEssence` | `AzeriteEssenceDocumentation.lua` | 21 | 8 | 2 |
| `C_Discord` | `DiscordDocumentation.lua` | 21 | 6 | 0 |
| `C_MythicPlus` | `MythicPlusInfoDocumentation.lua` | 21 | 2 | 6 |
| `C_PaperDollInfo` | `PaperDollInfoDocumentation.lua` | 21 | 23 | 4 |
| `C_WeeklyRewards` | `WeeklyRewardsDocumentation.lua` | 21 | 2 | 6 |
| `C_GossipInfo` | `GossipInfoDocumentation.lua` | 20 | 7 | 10 |
| `C_HouseExterior` | `HouseExteriorUIDocumentation.lua` | 20 | 12 | 0 |
| `C_LegendaryCrafting` | `LegendaryCraftingDocumentation.lua` | 20 | 4 | 0 |
| `C_NeighborhoodInitiative` | `NeighborhoodInitiativeDocumentation.lua` | 20 | 6 | 7 |
| `C_SuperTrack` | `SuperTrackManagerDocumentation.lua` | 20 | 2 | 0 |
| `C_HousingBlueprint` | `HousingBlueprintUIDocumentation.lua` | 19 | 14 | 0 |
| `C_PingSecure` | `PingManagerSecureDocumentation.lua` | 19 | 2 | 11 |
| `C_TTSSettings` | `TTSSettingsDocumentation.lua` | 19 | 0 | 0 |
| `C_AzeriteEmpoweredItem` | `AzeriteEmpoweredItemDocumentation.lua` | 18 | 2 | 5 |
| `C_ContentTracking` | `ContentTrackingDocumentation.lua` | 18 | 5 | 0 |
| `C_ContributionCollector` | `ContributionCollectorDocumentation.lua` | 18 | 4 | 3 |
| `C_HousingExpertMode` | `HousingExpertModeUIDocumentation.lua` | 18 | 6 | 4 |
| `C_StableInfo` | `StableInfoDocumentation.lua` | 17 | 5 | 2 |
| `C_CombatAudioAlert` | `CombatAudioAlertDocumentation.lua` | 16 | 0 | 0 |
| `C_CovenantSanctumUI` | `CovenantSanctumDocumentation.lua` | 16 | 2 | 3 |
| `C_ItemSocketInfo` | `ItemSocketInfoDocumentation.lua` | 16 | 8 | 3 |
| `C_ItemUpgrade` | `ItemUpgradeDocumentation.lua` | 16 | 3 | 7 |
| `C_RecruitAFriend` | `RecruitAFriendDocumentation.lua` | 16 | 5 | 17 |
| `C_RecentAllies` | `RecentAlliesDocumentation.lua` | 15 | 5 | 7 |
| `C_MajorFactions` | `MajorFactionsDocumentation.lua` | 14 | 4 | 6 |
| `C_PetInfo` | `PetInfoDocumentation.lua` | 14 | 12 | 1 |
| `C_QuestSession` | `QuestSessionDocumentation.lua` | 14 | 8 | 1 |
| `C_SecureTransfer` | `SecureTransferDocumentation.lua` | 14 | 6 | 2 |
| `C_CVar` | `CVarDocumentation.lua` | 12 | 0 | 1 |
| `C_EditMode` | `EditModeManagerDocumentation.lua` | 12 | 1 | 5 |
| `C_ReportSystem` | `ReportSystemDocumentation.lua` | 12 | 2 | 0 |
| `C_SocialRestrictions` | `SocialRestrictionsDocumentation.lua` | 12 | 4 | 0 |
| `C_TaskQuest` | `QuestTaskInfoDocumentation.lua` | 12 | 0 | 0 |
| `table` | `LuaTableExtensionsDocumentation.lua` | 12 | 0 | 0 |
| `C_AccountStore` | `AccountStoreDocumentation.lua` | 11 | 5 | 3 |
| `C_CombatLog` | `CombatLogDocumentation.lua` | 11 | 4 | 0 |
| `C_DateAndTime` | `DateAndTimeDocumentation.lua` | 11 | 1 | 1 |
| `C_EventScheduler` | `EventSchedulerUIDocumentation.lua` | 11 | 1 | 3 |
| `C_KeyBindings` | `KeyBindingsDocumentation.lua` | 11 | 4 | 5 |
| `C_PhotoSharing` | `PhotoSharingUIDocumentation.lua` | 11 | 5 | 0 |
| `C_ScrappingMachineUI` | `ScrappingMachineUIDocumentation.lua` | 11 | 4 | 0 |
| `C_Texture` | `TextureUtilsDocumentation.lua` | 11 | 1 | 4 |
| `C_VideoOptions` | `VideoDocumentation.lua` | 11 | 9 | 2 |
| `math` | `LuaMathExtensionsDocumentation.lua` | 11 | 0 | 0 |
| `C_AddOnProfiler` | `AddOnProfilerDocumentation.lua` | 10 | 0 | 4 |
| `C_AuraContainerUtil` | `AuraContainerUtilDocumentation.lua` | 10 | 0 | 15 |
| `C_AzeriteItem` | `AzeriteItemDocumentation.lua` | 10 | 3 | 1 |
| `C_CampaignInfo` | `WarCampaignDocumentation.lua` | 10 | 0 | 4 |
| `C_EncodingUtil` | `EncodingUtilDocumentation.lua` | 10 | 0 | 5 |
| `C_EncounterWarnings` | `EncounterWarningsDocumentation.lua` | 10 | 1 | 1 |
| `C_HouseEditor` | `HouseEditorUIDocumentation.lua` | 10 | 3 | 1 |
| `C_ItemInteraction` | `ItemInteractionUIDocumentation.lua` | 10 | 2 | 3 |
| `C_MacOptions` | `MacOptionsDocumentation.lua` | 10 | 0 | 0 |
| `C_PerksActivities` | `PerksActivitiesDocumentation.lua` | 10 | 4 | 7 |
| `C_ScenarioInfo` | `ScenarioInfoDocumentation.lua` | 10 | 9 | 7 |
| `C_SocialQueue` | `SocialQueueDocumentation.lua, SocialQueueSystemStatusDocumentation.lua` | 10 | 3 | 4 |
| `C_ClickBindings` | `ClickBindingsDocumentation.lua` | 9 | 1 | 0 |
| `C_CombatLogSecure` | `CombatLogSecureDocumentation.lua` | 9 | 3 | 0 |
| `C_QuestInfoSystem` | `QuestInfoSystemDocumentation.lua` | 9 | 0 | 0 |
| `C_AreaPoiInfo` | `AreaPoiInfoDocumentation.lua` | 8 | 1 | 1 |
| `C_CreatureInfo` | `CreatureInfoDocumentation.lua` | 8 | 0 | 5 |
| `C_DamageMeter` | `DamageMeterDocumentation.lua` | 8 | 3 | 6 |
| `C_DyeColor` | `DyeColorInfoDocumentation.lua` | 8 | 2 | 0 |
| `C_EncounterEvents` | `EncounterEventsDocumentation.lua` | 8 | 0 | 0 |
| `C_HeirloomInfo` | `HeirloomInfoDocumentation.lua` | 8 | 2 | 0 |
| `C_LobbyMatchmakerInfo` | `LobbyMatchmakerInfoDocumentation.lua` | 8 | 5 | 2 |
| `C_ModelInfo` | `UIModelInfoDocumentation.lua` | 8 | 1 | 3 |
| `C_PlayerInteractionManager` | `PlayerInteractionManagerDocumentation.lua` | 8 | 2 | 0 |
| `C_RemixArtifactUI` | `RemixArtifactUIDocumentation.lua` | 8 | 2 | 3 |
| `C_SkillInfo` | `SkillInfoDocumentation.lua` | 8 | 1 | 1 |
| `C_WorldLootObject` | `WorldLootObjectDocumentation.lua` | 8 | 1 | 1 |
| `C_AnimaDiversion` | `AnimaDiversionUIDocumentation.lua` | 7 | 3 | 4 |
| `C_ColorUtil` | `ColorUtilDocumentation.lua` | 7 | 0 | 0 |
| `C_CooldownViewer` | `CooldownViewerDocumentation.lua` | 7 | 3 | 2 |
| `C_MerchantFrame` | `MerchantFrameDocumentation.lua` | 7 | 4 | 1 |
| `C_Navigation` | `InGameNavigationDocumentation.lua` | 7 | 3 | 1 |
| `C_PlayerChoice` | `PlayerChoiceDocumentation.lua` | 7 | 2 | 8 |
| `C_QuestLine` | `QuestLineInfoDocumentation.lua` | 7 | 0 | 2 |
| `C_SummonInfo` | `SummonInfoDocumentation.lua` | 7 | 0 | 0 |
| `C_Transmog` | `TransmogDocumentation.lua` | 7 | 15 | 5 |
| `C_UI` | `UIManagerDocumentation.lua` | 7 | 2 | 0 |
| `C_WarbandScene` | `WarbandSceneInfoDocumentation.lua` | 7 | 2 | 2 |
| `C_AchievementInfo` | `AchievementInfoDocumentation.lua` | 6 | 11 | 0 |
| `C_ColorOverrides` | `ColorOverridesDocumentation.lua` | 6 | 2 | 1 |
| `C_Flyout` | `FlyoutDocumentation.lua` | 6 | 0 | 2 |
| `C_IslandsQueue` | `IslandsQueueUIDocumentation.lua` | 6 | 2 | 1 |
| `C_Ping` | `PingManagerDocumentation.lua` | 6 | 1 | 1 |
| `C_Sound` | `SoundDocumentation.lua` | 6 | 2 | 2 |
| `C_ToyBoxInfo` | `ToyBoxInfoDocumentation.lua` | 6 | 2 | 0 |
| `C_VignetteInfo` | `VignetteInfoDocumentation.lua` | 6 | 2 | 1 |
| `string` | `LuaStringExtensionsDocumentation.lua` | 6 | 0 | 0 |
| `C_AdventureMap` | `AdventureMapDocumentation.lua` | 5 | 5 | 1 |
| `C_CurveUtil` | `CurveUtilDocumentation.lua` | 5 | 0 | 0 |
| `C_DeathInfo` | `DeathInfoDocumentation.lua` | 5 | 14 | 3 |
| `C_GamepadUI` | `GamepadUIDocumentation.lua` | 5 | 2 | 3 |
| `C_HousingInspectMode` | `HousingInspectModeUIDocumentation.lua` | 5 | 2 | 0 |
| `C_LootHistory` | `LootHistoryDocumentation.lua` | 5 | 5 | 5 |
| `C_LossOfControl` | `LossOfControlDocumentation.lua` | 5 | 6 | 1 |
| `C_Mail` | `MailInfoDocumentation.lua` | 5 | 14 | 0 |
| `C_PetBattles` | `PetBattlesDocumentation.lua` | 5 | 0 | 0 |
| `C_PlayerMentorship` | `PlayerMentorshipDocumentation.lua` | 5 | 2 | 0 |
| `C_SplashScreen` | `SplashScreenDocumentation.lua` | 5 | 1 | 1 |
| `C_AssistedCombat` | `AssistedCombatDocumentation.lua` | 4 | 1 | 0 |
| `C_AutoComplete` | `AutoCompleteDocumentation.lua` | 4 | 0 | 1 |
| `C_BlizzCon2026` | `BlizzCon2026Documentation.lua` | 4 | 2 | 1 |
| `C_ChromieTime` | `ChromieTimeUIDocumentation.lua` | 4 | 0 | 1 |
| `C_ConfigurationWarnings` | `ConfigurationWarningsDocumentation.lua` | 4 | 0 | 0 |
| `C_ConsoleScriptCollection` | `ConsoleScriptCollectionDocumentation.lua` | 4 | 0 | 4 |
| `C_DeathRecap` | `DeathRecapDocumentation.lua` | 4 | 0 | 1 |
| `C_ExternalEventURL` | `ExternalEventURLDocumentation.lua` | 4 | 2 | 0 |
| `C_InstanceEncounter` | `InstanceEncounterDocumentation.lua` | 4 | 6 | 0 |
| `C_InvasionInfo` | `InvasionInfoDocumentation.lua` | 4 | 0 | 1 |
| `C_Log` | `LogDocumentation.lua` | 4 | 0 | 0 |
| `C_Macro` | `UIMacrosDocumentation.lua` | 4 | 1 | 1 |
| `C_NamePlate` | `NamePlateDocumentation.lua` | 4 | 0 | 0 |
| `C_NamePlateManager` | `NamePlateManagerDocumentation.lua` | 4 | 7 | 0 |
| `C_PartyPose` | `PartyPoseUIDocumentation.lua` | 4 | 1 | 1 |
| `C_QuestOffer` | `QuestOfferDocumentation.lua` | 4 | 5 | 1 |
| `C_RaidLocks` | `RaidLocksDocumentation.lua` | 4 | 0 | 0 |
| `C_RestrictedActions` | `RestrictedActionsDocumentation.lua` | 4 | 5 | 0 |
| `C_SpellDiminish` | `SpellDiminishUIDocumentation.lua` | 4 | 1 | 2 |
| `C_StringUtil` | `StringUtilDocumentation.lua` | 4 | 0 | 0 |
| `C_Timer` | `UITimerDocumentation.lua` | 4 | 0 | 2 |
| `C_TradeInfo` | `TradeInfoDocumentation.lua` | 4 | 15 | 0 |
| `C_Tutorial` | `TutorialDocumentation.lua` | 4 | 6 | 1 |
| `C_AccountInfo` | `AccountInfoDocumentation.lua` | 3 | 0 | 0 |
| `C_AchievementTelemetry` | `AchievementTelemetryDocumentation.lua` | 3 | 0 | 0 |
| `C_CombatText` | `CombatTextDocumentation.lua` | 3 | 1 | 0 |
| `C_Covenants` | `CovenantsDocumentation.lua` | 3 | 1 | 1 |
| `C_DurationUtil` | `DurationUtilDocumentation.lua` | 3 | 0 | 0 |
| `C_EventToastManager` | `UIEventToastManagerDocumentation.lua` | 3 | 2 | 1 |
| `C_GamepadTargeting` | `GamepadTargetingDocumentation.lua` | 3 | 0 | 1 |
| `C_HousingCleanupMode` | `HousingCleanupModeUIDocumentation.lua` | 3 | 2 | 1 |
| `C_LFGListRoles` | `LFGListRolesDocumentation.lua` | 3 | 1 | 0 |
| `C_NewItems` | `NewItemsDocumentation.lua` | 3 | 0 | 0 |
| `C_Roleset` | `RolesetSystemDocumentation.lua` | 3 | 0 | 0 |
| `C_SeasonInfo` | `SeasonInfoDocumentation.lua` | 3 | 0 | 0 |
| `C_StorePublic` | `StorePublicUIDocumentation.lua` | 3 | 0 | 0 |
| `C_TaxiMap` | `TaxiMapDocumentation.lua` | 3 | 3 | 4 |
| `C_TooltipComparison` | `TooltipComparisonDocumentation.lua` | 3 | 1 | 2 |
| `C_Trainer` | `TrainerDocumentation.lua` | 3 | 5 | 0 |
| `C_UIFileAsset` | `UIFileAssetAPIDocumentation.lua` | 3 | 0 | 0 |
| `C_AlliedRaces` | `AlliedRacesFrameDocumentation.lua` | 2 | 2 | 2 |
| `C_ArdenwealdGardening` | `ArdenwealdGardeningDocumentation.lua` | 2 | 0 | 1 |
| `C_CovenantCallings` | `CovenantCallingsDocumentation.lua` | 2 | 1 | 0 |
| `C_CovenantPreview` | `CovenantPreviewDocumentation.lua` | 2 | 2 | 4 |
| `C_EventUtils` | `EventUtilsDocumentation.lua` | 2 | 0 | 0 |
| `C_ExpansionTrial` | `ExpansionTrialDocumentation.lua` | 2 | 0 | 0 |
| `C_FogOfWar` | `FogOfWarDocumentation.lua` | 2 | 1 | 1 |
| `C_GenericWidgetDisplay` | `UIGenericWidgetDisplayDocumentation.lua` | 2 | 1 | 1 |
| `C_Glue` | `GlueDocumentation.lua` | 2 | 1 | 0 |
| `C_IncomingSummon` | `IncomingSummonDocumentation.lua` | 2 | 0 | 1 |
| `C_LevelLink` | `LevelLinkDocumentation.lua` | 2 | 0 | 0 |
| `C_LevelSquish` | `LevelSquishDocumentation.lua` | 2 | 0 | 0 |
| `C_LiveEvent` | `LiveEventDocumentation.lua` | 2 | 0 | 0 |
| `C_Loot` | `LootDocumentation.lua` | 2 | 37 | 0 |
| `C_LootJournal` | `LootJournalDocumentation.lua` | 2 | 1 | 2 |
| `C_MapExplorationInfo` | `MapExplorationDocumentation.lua` | 2 | 1 | 2 |
| `C_QuestHub` | `QuestHubInfoDocumentation.lua` | 2 | 0 | 0 |
| `C_SettingsUtil` | `SettingsUtilDocumentation.lua` | 2 | 2 | 0 |
| `C_SwingTimer` | `SwingTimerDocumentation.lua` | 2 | 2 | 1 |
| `C_UserFeedback` | `UserFeedbackDocumentation.lua` | 2 | 0 | 0 |
| `C_WowSurvey` | `WowSurveyDocumentation.lua` | 2 | 1 | 0 |
| `C_WowTokenUI` | `WowTokenUIDocumentation.lua` | 2 | 15 | 0 |
| `C_XMLUtil` | `XMLUtilDocumentation.lua` | 2 | 0 | 3 |
| `C_ZoneAbility` | `ZoneAbilityDocumentation.lua` | 2 | 0 | 1 |
| `C_AutoLoot` | `AutoLootDocumentation.lua` | 1 | 0 | 0 |
| `C_BarberShopInternal` | `BarberShopInternalDocumentation.lua` | 1 | 0 | 0 |
| `C_BehavioralMessaging` | `BehavioralMessagingDocumentation.lua` | 1 | 1 | 0 |
| `C_Browser` | `BrowserDocumentation.lua` | 1 | 7 | 0 |
| `C_ChatBubbles` | `ChatBubblesDocumentation.lua` | 1 | 0 | 0 |
| `C_CinematicList` | `CinematicListDocumentation.lua` | 1 | 0 | 1 |
| `C_ClassColor` | `ClassColorDocumentation.lua` | 1 | 0 | 0 |
| `C_ClientScene` | `ClientSceneDocumentation.lua` | 1 | 2 | 1 |
| `C_CombatLogInternal` | `CombatLogInternalDocumentation.lua` | 1 | 1 | 0 |
| `C_Cursor` | `CursorDocumentation.lua` | 1 | 4 | 0 |
| `C_EndOfMatchUI` | `EndOfMatchUIDocumentation.lua` | 1 | 1 | 4 |
| `C_FrameManager` | `UIFrameManagerDocumentation.lua` | 1 | 2 | 1 |
| `C_GuildBank` | `GuildBankDocumentation.lua` | 1 | 10 | 0 |
| `C_ImmersiveInteraction` | `ImmersiveInteractionManagerDocumentation.lua` | 1 | 2 | 0 |
| `C_InputInterfaceStyle` | `InputInterfaceStyleDocumentation.lua` | 1 | 0 | 0 |
| `C_InstanceLeaver` | `InstanceLeaverInfoDocumentation.lua` | 1 | 1 | 0 |
| `C_InterfaceFileManifest` | `InterfaceFileManifestDocumentation.lua` | 1 | 0 | 0 |
| `C_LimitedInput` | `LimitedInputDocumentation.lua` | 1 | 0 | 1 |
| `C_LootFrame` | `LootFrameDocumentation.lua` | 1 | 0 | 0 |
| `C_LoreText` | `LoreTextDocumentation.lua` | 1 | 1 | 1 |
| `C_ModifiedInstance` | `UIModifiedInstanceDocumentation.lua` | 1 | 0 | 1 |
| `C_NameUtil` | `NameUtilDocumentation.lua` | 1 | 0 | 0 |
| `C_QuestItemUse` | `QuestItemUseDocumentation.lua` | 1 | 0 | 0 |
| `C_ResearchInfo` | `ResearchInfoDocumentation.lua` | 1 | 8 | 1 |
| `C_ScriptedAnimations` | `ScriptedAnimationsDocumentation.lua` | 1 | 1 | 4 |
| `C_SocialUI` | `SocialUIDocumentation.lua` | 1 | 1 | 3 |
| `C_SpellActivationOverlay` | `SpellActivationOverlayDocumentation.lua` | 1 | 4 | 0 |
| `C_System` | `SystemDocumentation.lua` | 1 | 31 | 0 |
| `C_SystemVisibilityManager` | `UISystemVisibilityManagerDocumentation.lua` | 1 | 1 | 1 |
| `C_TimerunningUI` | `TimerunningUIDocumentation.lua` | 1 | 1 | 0 |
| `C_UIColor` | `UIColorDocumentation.lua` | 1 | 0 | 0 |
| `C_Weather` | `WeatherScriptDocumentation.lua` | 1 | 1 | 1 |
| `C_WorldSafeLocsUIInternal` | `WorldSafeLocsUIInternalDocumentation.lua` | 1 | 0 | 1 |
| `C_AdventureJournal` | `AdventureJournalDocumentation.lua` | 0 | 14 | 0 |
| `C_BattlePet` | `BattlePetDocumentation.lua` | 0 | 26 | 0 |
| `C_BlackMarketInfo` | `BlackMarketInfoDocumentation.lua` | 0 | 7 | 0 |
| `C_ClassTrial` | `ClassTrialDocumentation.lua` | 0 | 2 | 0 |
| `C_CompactUnitFrames` | `CompactUnitFramesDocumentation.lua` | 0 | 1 | 0 |
| `C_CursorUtil` | `CursorUtilDocumentation.lua` | 0 | 0 | 1 |
| `C_DeathAlert` | `DeathAlertDocumentation.lua` | 0 | 1 | 0 |
| `C_DuelInfo` | `DuelInfoDocumentation.lua` | 0 | 5 | 0 |
| `C_EncounterInfo` | `EncounterInfoDocumentation.lua` | 0 | 10 | 1 |
| `C_GMTicketInfo` | `GMTicketInfoDocumentation.lua` | 0 | 8 | 0 |
| `C_GlyphInfo` | `GlyphInfoDocumentation.lua` | 0 | 3 | 0 |
| `C_IslandsInfo` | `IslandsInfoDocumentation.lua` | 0 | 1 | 0 |
| `C_ItemText` | `ItemTextDocumentation.lua` | 0 | 4 | 0 |
| `C_LoadingScreen` | `LoadingScreenDocumentation.lua` | 0 | 2 | 0 |
| `C_Platform` | `PlatformDocumentation.lua` | 0 | 0 | 1 |
| `C_ScriptWarnings` | `ScriptWarningsDocumentation.lua` | 0 | 1 | 0 |
| `C_TalkingHead` | `TalkingHeadDocumentation.lua` | 0 | 2 | 0 |
| `C_UIActionHandler` | `UIActionDocumentation.lua` | 0 | 1 | 0 |
| `C_Vehicle` | `VehicleDocumentation.lua` | 0 | 10 | 0 |
| `C_WorldStateInfo` | `WorldStateInfoDocumentation.lua` | 0 | 6 | 0 |
| `C_WowEntitlementInfo` | `WowEntitlementInfoDocumentation.lua` | 0 | 2 | 1 |
