using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.MountAndBlade;

namespace Imprisoned
{
    public class SubModule : MBSubModuleBase
    {
        private static readonly Harmony _harmony = new Harmony("com.imprisoned.mod");
        private bool _wasF7Pressed;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            ApplyPatchesSafely();
        }

        // Tick fires every frame at the application level — safe for input polling,
        // same pattern FollowAll uses for F8.
        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);

            if (Campaign.Current == null) return;
            if (Mission.Current != null) return;  // campaign map only

            bool pressed = Input.IsKeyDown(InputKey.F7);
            if (pressed && !_wasF7Pressed)
                OnF7Pressed();
            _wasF7Pressed = pressed;
        }

        private static void OnF7Pressed()
        {
            if (Campaign.Current == null) return;
            if (Hero.MainHero == null || !Hero.MainHero.IsPrisoner) return;

            try { GameMenu.ActivateGameMenu("prisoner_rp_main"); }
            catch { }
        }

        private static void ApplyPatchesSafely()
        {
            // Prisoner scene plumbing
            TryPatch(typeof(GameMenu), "SwitchToMenu",
                typeof(PrisonerExitRedirectPatch), nameof(PrisonerExitRedirectPatch.Prefix), isPrefix: true);
            TryPatch(typeof(LocationComplex), "CanIfPriceIsPaid",
                typeof(PrisonerDungeonAccessPatch), nameof(PrisonerDungeonAccessPatch.Prefix), isPrefix: true);

            // Ransom suppression — three layers + menu disable
            TryPatch("TaleWorlds.CampaignSystem.CampaignBehaviors.RansomOfferCampaignBehavior",
                "ConsiderRansomPrisoner",
                typeof(RansomConsiderBlockPatch), nameof(RansomConsiderBlockPatch.Prefix), isPrefix: true);
            TryPatch("TaleWorlds.CampaignSystem.CampaignBehaviors.RansomOfferCampaignBehavior",
                "OnRansomOffered",
                typeof(RansomOfferBlockPatch), nameof(RansomOfferBlockPatch.Prefix), isPrefix: true);
            TryPatch("TaleWorlds.CampaignSystem.Actions.EndCaptivityAction",
                "ApplyByRansom",
                typeof(RansomApplyBlockPatch), nameof(RansomApplyBlockPatch.Prefix), isPrefix: true);
            TryPatch("TaleWorlds.CampaignSystem.CampaignBehaviors.PlayerCaptivityCampaignBehavior",
                "menu_captivity_end_propose_ransom_on_init",
                typeof(RansomMenuInitBlockPatch), nameof(RansomMenuInitBlockPatch.Postfix), isPrefix: false);
        }

        private static void TryPatch(
            System.Type targetType, string targetMethod,
            System.Type patchType,  string patchMethod,
            bool isPrefix)
        {
            try
            {
                if (targetType == null) return;
                var target = AccessTools.Method(targetType, targetMethod);
                if (target == null) return;

                var patch = new HarmonyMethod(AccessTools.Method(patchType, patchMethod));
                if (isPrefix)
                    _harmony.Patch(target, prefix: patch);
                else
                    _harmony.Patch(target, postfix: patch);
            }
            catch { }
        }

        private static void TryPatch(
            string targetTypeName, string targetMethod,
            System.Type patchType,  string patchMethod,
            bool isPrefix)
        {
            TryPatch(AccessTools.TypeByName(targetTypeName), targetMethod,
                patchType, patchMethod, isPrefix);
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            if (gameStarterObject is CampaignGameStarter starter)
            {
                var behavior = new PrisonerBehavior();
                starter.AddBehavior(behavior);
                behavior.InitMenus(starter);
            }
        }
    }
}
