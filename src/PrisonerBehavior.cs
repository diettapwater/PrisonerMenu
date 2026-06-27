using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SandBox;
using SandBox.Conversation.MissionLogics;
using SandBox.GauntletUI.Missions;
using SandBox.Missions.MissionLogics;
using SandBox.View.Missions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.GauntletUI.Mission;
using TaleWorlds.MountAndBlade.GauntletUI.Mission.Singleplayer;
using TaleWorlds.MountAndBlade.Source.Missions;
using TaleWorlds.MountAndBlade.View.MissionViews;

namespace Imprisoned
{
    /// <summary>
    /// Core PrisonerMenu behavior.
    /// Press F7 on the campaign map while imprisoned to open the prisoner RP menu.
    ///
    /// Settlement prisoner  → "Explore the dungeon" opens the indoor dungeon scene.
    /// Mobile party prisoner → "Walk around the camp" opens a terrain-matched outdoor
    ///                         scene with captor heroes and fellow prisoners spawned in.
    ///                         Uses MissionState.OpenNew (no MissionAgentHandler) so the
    ///                         prison-roster player state doesn't crash agent setup.
    /// Any prisoner         → "Talk to captor" / "Talk to fellow prisoner" map conversations.
    /// </summary>
    public class PrisonerBehavior : CampaignBehaviorBase
    {
        public static bool IsInPrisonerScene { get; private set; }
        public static void OnEnterScene() { IsInPrisonerScene = true; }
        public static void OnExitScene()  { IsInPrisonerScene = false; }

        public override void RegisterEvents() { }
        public override void SyncData(IDataStore dataStore) { }

        // ── Menu registration ─────────────────────────────────────────────────

        public void InitMenus(CampaignGameStarter starter)
        {
            starter.AddGameMenu(
                "prisoner_rp_main",
                "{=prisoner_rp_main_desc}You are a prisoner. What will you do?",
                args => { });

            starter.AddGameMenuOption(
                "prisoner_rp_main",
                "prisoner_rp_dungeon",
                "Explore the dungeon",
                ConditionDungeon,
                ConsequenceDungeon,
                false, -1);

            starter.AddGameMenuOption(
                "prisoner_rp_main",
                "prisoner_rp_camp_scene",
                "Walk around the camp",
                ConditionCampScene,
                ConsequenceCampScene,
                false, -1);

            starter.AddGameMenuOption(
                "prisoner_rp_main",
                "prisoner_rp_talk_captor",
                "Request an audience with your captor",
                ConditionTalkCaptor,
                ConsequenceTalkCaptor,
                false, -1);

            starter.AddGameMenuOption(
                "prisoner_rp_main",
                "prisoner_rp_talk_fellow",
                "Speak with a fellow prisoner",
                ConditionTalkFellow,
                ConsequenceTalkFellow,
                false, -1);

            starter.AddGameMenuOption(
                "prisoner_rp_main",
                "prisoner_rp_leave",
                "Return to captivity.",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                _ => GameMenu.ExitToLast(),
                true, -1);
        }

        // ── Dungeon scene (settlement prisoners) ──────────────────────────────

        private static bool ConditionDungeon(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Mission;
            if (Campaign.Current == null || Hero.MainHero == null) return false;
            return Hero.MainHero.IsPrisoner
                && Hero.MainHero.PartyBelongedToAsPrisoner?.Settlement is Settlement s
                && s.IsFortification;
        }

        private static void ConsequenceDungeon(MenuCallbackArgs args)
        {
            var settlement = Hero.MainHero.PartyBelongedToAsPrisoner!.Settlement!;
            var locationComplex = settlement.LocationComplex;
            if (locationComplex == null) { Fail(); return; }

            var prisonLocation = locationComplex.GetLocationWithId("prison");
            if (prisonLocation == null) { Fail(); return; }

            int wallLevel  = settlement.Town?.GetWallLevel() ?? 1;
            string sceneName = prisonLocation.GetSceneName(wallLevel);
            if (string.IsNullOrEmpty(sceneName)) { Fail(); return; }

            // Scene level tag (e.g. "level_1", "level_2") so the right variant loads.
            string sceneLevels = Campaign.Current.Models.LocationModel
                .GetUpgradeLevelTag(wallLevel);

            // ── Who's in the dungeon? ────────────────────────────────────────
            var captorLords     = new List<Hero>();
            var fellowPrisoners = new List<Hero>();

            // Lords/heroes present in the settlement — spawn outside the cell.
            foreach (var h in settlement.HeroesWithoutParty)
            {
                if (h != Hero.MainHero && h.IsActive && !h.IsPrisoner)
                    captorLords.Add(h);
            }
            // Clan owner / governor if not already listed.
            if (settlement.OwnerClan?.Leader is Hero leader
                && leader != Hero.MainHero && !captorLords.Contains(leader))
                captorLords.Add(leader);

            // Visiting lord parties currently inside the settlement.
            foreach (var party in settlement.Parties)
            {
                if (party.LeaderHero is Hero ph
                    && ph != Hero.MainHero && !ph.IsPrisoner
                    && !captorLords.Contains(ph))
                    captorLords.Add(ph);
            }

            // Other hero prisoners in the same dungeon — spawn near player inside cells.
            foreach (var e in settlement.Party.PrisonRoster.GetTroopRoster())
            {
                if (e.Character.IsHero && e.Character.HeroObject is Hero ph
                    && ph != Hero.MainHero)
                    fellowPrisoners.Add(ph);
            }

            var atm = Campaign.Current.Models.MapWeatherModel
                .GetAtmosphereModel(settlement.GatePosition);
            var rec = new MissionInitializerRecord(sceneName)
            {
                PlayingInCampaignMode = Campaign.Current.GameMode == CampaignGameMode.Campaign,
                AtmosphereOnCampaign  = atm,
                SceneLevels           = sceneLevels,
                DecalAtlasGroup       = 3,
            };

            OnEnterScene();
            MissionState.OpenNew("PrisonerDungeon", rec,
                _ => GetDungeonBehaviors(captorLords, fellowPrisoners),
                true, true);
        }

        private static MissionBehavior[] GetDungeonBehaviors(
            List<Hero> captorLords, List<Hero> fellowPrisoners)
        {
            return new MissionBehavior[]
            {
                new MissionOptionsComponent(),
                new CampaignMissionComponent(),
                new MissionConversationLogic(),
                new MissionMainAgentController(),
                new PrisonerDungeonMissionController(captorLords, fellowPrisoners),
                new MissionGauntletSingleplayerEscapeMenu(false),
                new MissionGauntletOptionsUIHandler(),
                new MissionGauntletPhotoMode(),
                new MissionGauntletLeaveView(),
                new MissionSingleplayerViewHandler(),
                new MissionGauntletAgentStatus(),
                new MissionMainAgentEquipDropView(),
                new MissionGauntletMainAgentEquipmentControllerView(),
                new MissionGauntletConversationView(),
                new MissionGauntletNameMarkerView(),
                new MissionGauntletMainAgentControlModeView(),
            };
        }

        // ── Camp scene (mobile party prisoners) ───────────────────────────────

        private static bool ConditionCampScene(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Mission;
            if (Campaign.Current == null || Hero.MainHero == null) return false;
            return Hero.MainHero.IsPrisoner
                && Hero.MainHero.PartyBelongedToAsPrisoner?.Settlement == null
                && Hero.MainHero.PartyBelongedToAsPrisoner?.MobileParty != null;
        }

        private static void ConsequenceCampScene(MenuCallbackArgs args)
        {
            var captorParty = Hero.MainHero.PartyBelongedToAsPrisoner!;
            var captor = captorParty.MobileParty!;

            string sceneName = Campaign.Current.Models.SceneModel
                .GetConversationSceneForMapPosition(captor.Position);
            if (string.IsNullOrEmpty(sceneName)) { Fail(); return; }

            var captorHeroes    = new List<Hero>();
            var fellowPrisoners = new List<Hero>();
            var troopGuards     = new List<(CharacterObject Char, int Count)>();

            // ── 1. Direct captor party heroes ────────────────────────────────────
            foreach (var e in captorParty.MemberRoster.GetTroopRoster())
            {
                if (e.Character.IsHero && e.Character.HeroObject is Hero h
                    && h != Hero.MainHero)
                    captorHeroes.Add(h);
            }

            // ── 2. Fellow prisoners ──────────────────────────────────────────────
            foreach (var e in captorParty.PrisonRoster.GetTroopRoster())
            {
                if (e.Character.IsHero && e.Character.HeroObject is Hero h
                    && h != Hero.MainHero)
                    fellowPrisoners.Add(h);
            }

            // ── 3. Army parties — other lords escorting the captor ───────────────
            if (captor.Army != null)
            {
                foreach (var armyParty in captor.Army.Parties)
                {
                    if (armyParty == captor) continue;
                    foreach (var e in armyParty.MemberRoster.GetTroopRoster())
                    {
                        if (e.Character.IsHero && e.Character.HeroObject is Hero h
                            && h != Hero.MainHero && !captorHeroes.Contains(h))
                            captorHeroes.Add(h);
                    }
                }
            }

            // ── 4. FollowAll tracked heroes/parties (optional soft dependency) ───
            TryAddFollowAllHeroes(captorHeroes);

            // ── 5. Troop guards — up to 6 soldiers from captor party ─────────────
            const int MaxGuards = 6;
            int guardCount = 0;
            foreach (var e in captorParty.MemberRoster.GetTroopRoster())
            {
                if (e.Character.IsHero || guardCount >= MaxGuards) continue;
                int n = System.Math.Min(e.Number, MaxGuards - guardCount);
                troopGuards.Add((e.Character, n));
                guardCount += n;
            }

            var rec = new MissionInitializerRecord(sceneName)
            {
                PlayingInCampaignMode = Campaign.Current.GameMode == CampaignGameMode.Campaign,
                AtmosphereOnCampaign  = Campaign.Current.Models.MapWeatherModel
                    .GetAtmosphereModel(captor.Position),
                SceneLevels     = "",
                DecalAtlasGroup = 2,
            };

            OnEnterScene();
            MissionState.OpenNew("PrisonerCamp", rec,
                _ => GetCampBehaviors(captorHeroes, fellowPrisoners, troopGuards),
                true, true);
        }

        // Accesses FollowAll's static hero/party sets via reflection so PrisonerMenu
        // has no hard build dependency on FollowAll.dll.
        private static void TryAddFollowAllHeroes(List<Hero> captorHeroes)
        {
            try
            {
                var t = AccessTools.TypeByName("FollowAll.FollowPartiesCampaignBehavior");
                if (t == null) return;

                // FollowingHeroes — heroes tracked individually (companions, wanderers)
                var heroesVal = AccessTools.Field(t, "FollowingHeroes")?.GetValue(null);
                if (heroesVal is System.Collections.IEnumerable heroSet)
                {
                    foreach (var obj in heroSet)
                    {
                        if (obj is Hero h && h != Hero.MainHero
                            && h.IsActive && !captorHeroes.Contains(h))
                            captorHeroes.Add(h);
                    }
                }

                // FollowingParties — lord parties tracked on the world map
                var partiesVal = AccessTools.Field(t, "FollowingParties")?.GetValue(null);
                if (partiesVal is System.Collections.IEnumerable partySet)
                {
                    foreach (var obj in partySet)
                    {
                        if (obj is not MobileParty party) continue;
                        foreach (var e in party.MemberRoster.GetTroopRoster())
                        {
                            if (e.Character.IsHero && e.Character.HeroObject is Hero h
                                && h != Hero.MainHero && h.IsActive
                                && !captorHeroes.Contains(h))
                                captorHeroes.Add(h);
                        }
                    }
                }
            }
            catch { }
        }

        private static MissionBehavior[] GetCampBehaviors(
            List<Hero> captorHeroes, List<Hero> fellowPrisoners,
            List<(CharacterObject Char, int Count)> troopGuards)
        {
            return new MissionBehavior[]
            {
                new MissionOptionsComponent(),
                new CampaignMissionComponent(),
                new MissionConversationLogic(),
                new MissionMainAgentController(),
                new PrisonerCampMissionController(captorHeroes, fellowPrisoners, troopGuards),
                new MissionGauntletSingleplayerEscapeMenu(false),
                new MissionGauntletOptionsUIHandler(),
                new MissionGauntletPhotoMode(),
                new MissionGauntletLeaveView(),
                new MissionSingleplayerViewHandler(),
                new MissionGauntletAgentStatus(),
                new MissionMainAgentEquipDropView(),
                new MissionGauntletMainAgentEquipmentControllerView(),
                new MissionGauntletConversationView(),
                new MissionGauntletNameMarkerView(),
                new MissionGauntletMainAgentControlModeView(),
            };
        }

        // ── Map conversations ─────────────────────────────────────────────────

        private static bool ConditionTalkCaptor(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Conversation;
            if (Campaign.Current == null || Hero.MainHero == null) return false;
            return Hero.MainHero.PartyBelongedToAsPrisoner?.MobileParty?.LeaderHero != null;
        }

        private static void ConsequenceTalkCaptor(MenuCallbackArgs args)
        {
            var captor = Hero.MainHero.PartyBelongedToAsPrisoner!;
            var leader = captor.MobileParty!.LeaderHero!;
            CampaignMapConversation.OpenConversation(
                new ConversationCharacterData(CharacterObject.PlayerCharacter, PartyBase.MainParty),
                new ConversationCharacterData(leader.CharacterObject, captor));
        }

        private static Hero? _selectedFellow;

        private static bool ConditionTalkFellow(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Conversation;
            if (Campaign.Current == null || Hero.MainHero == null) return false;
            _selectedFellow = null;
            var captor = Hero.MainHero.PartyBelongedToAsPrisoner;
            if (captor == null) return false;
            foreach (var e in captor.PrisonRoster.GetTroopRoster())
            {
                if (!e.Character.IsHero) continue;
                var h = e.Character.HeroObject;
                if (h == null || h == Hero.MainHero) continue;
                _selectedFellow = h;
                break;
            }
            return _selectedFellow != null;
        }

        private static void ConsequenceTalkFellow(MenuCallbackArgs args)
        {
            if (_selectedFellow == null) return;
            var captor = Hero.MainHero.PartyBelongedToAsPrisoner!;
            CampaignMapConversation.OpenConversation(
                new ConversationCharacterData(CharacterObject.PlayerCharacter, PartyBase.MainParty),
                new ConversationCharacterData(_selectedFellow.CharacterObject, captor));
            _selectedFellow = null;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        internal static LocationCharacter CreateLocationCharacter(
            Hero hero, LocationCharacter.CharacterRelations relation)
        {
            var monster = Game.Current.ObjectManager.GetObject<Monster>(
                hero.IsFemale ? "human_female" : "human_male")
                ?? Game.Current.ObjectManager.GetObject<Monster>("human");

            return new LocationCharacter(
                new AgentData(new SimpleAgentOrigin(hero.CharacterObject, -1, null, default))
                    .Monster(monster)
                    .NoHorses(true)
                    .CivilianEquipment(true),
                SandBoxManager.Instance.AgentBehaviorManager.AddWandererBehaviors,
                "npc_common",
                false,
                relation,
                null,
                true,
                false);
        }

        private static void Fail()
        {
            OnExitScene();
            InformationManager.DisplayMessage(
                new InformationMessage("The guards block your path."));
        }
    }
}
