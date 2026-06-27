using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.Localization;

namespace PrisonerMenu
{
    // Patch implementations — applied manually in SubModule so a missing method
    // is caught and skipped rather than crashing PatchAll() at startup.

    public static class PrisonerExitRedirectPatch
    {
        /// <summary>
        /// Intercepts GameMenu.SwitchToMenu to redirect exit-from-scene
        /// back to the prisoner menus instead of the normal settlement menus.
        /// </summary>
        public static void Prefix(ref string menuId)
        {
            if (!PrisonerBehavior.IsInPrisonerScene) return;

            // Exiting the dungeon normally sends you to "castle" or "town".
            if (menuId is "castle" or "town")
            {
                PrisonerBehavior.OnExitScene();
                menuId = "prisoner_wait";
                return;
            }

            // Exiting the village camp scene sends you to "village".
            if (menuId is "village")
            {
                PrisonerBehavior.OnExitScene();
                menuId = "prisoner_camp";
            }
        }
    }

    // ── Ransom suppression ────────────────────────────────────────────────────
    // Three layers so no code path can silently ransom the player out of an RP.

    /// <summary>
    /// Stops the AI from ever considering the player for ransom.
    /// ConsiderRansomPrisoner fires inside the daily-tick evaluation that decides
    /// whether to queue a ransom offer for a captured hero.
    /// </summary>
    public static class RansomConsiderBlockPatch
    {
        public static bool Prefix(Hero hero) => hero != Hero.MainHero;
    }

    /// <summary>
    /// Prevents the "your captor offers to release you for X gold" panel from
    /// appearing even if another mod or the game logic somehow bypasses the daily tick.
    /// </summary>
    public static class RansomOfferBlockPatch
    {
        public static bool Prefix(Hero captiveHero) => captiveHero != Hero.MainHero;
    }

    /// <summary>
    /// Last-resort block on the actual release action so the player cannot be
    /// freed via ransom regardless of how the offer was triggered.
    /// </summary>
    public static class RansomApplyBlockPatch
    {
        public static bool Prefix(Hero character) => character != Hero.MainHero;
    }

    /// <summary>
    /// Disables the "Propose ransom" option in the vanilla captivity game menu
    /// so the player cannot voluntarily pay their way out mid-RP.
    /// </summary>
    public static class RansomMenuInitBlockPatch
    {
        public static void Postfix(MenuCallbackArgs args)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("Ransom is suspended for immersive RP.");
        }
    }

    public static class PrisonerDungeonAccessPatch
    {
        /// <summary>
        /// Bypasses the dungeon bribe check when the player is a prisoner.
        /// A prisoner is already in the dungeon — the bribe gate doesn't apply.
        /// </summary>
        public static bool Prefix(Location location, ref bool __result)
        {
            if (location.StringId != "prison") return true;

            // Guard: Campaign.Current may be null before a save is loaded.
            if (Campaign.Current == null) return true;
            if (!Hero.MainHero.IsPrisoner) return true;

            __result = true;
            return false;
        }
    }
}
