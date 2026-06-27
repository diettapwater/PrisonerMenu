using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace PrisonerMenu
{
    /// <summary>
    /// Mission logic for the prisoner camp scene.
    /// Spawns the player, captor heroes, troop guards, and fellow prisoners manually —
    /// no MissionAgentHandler, so the prison roster state doesn't crash agent setup.
    /// Modeled after CouncilOfWar's CouncilMissionController.
    /// </summary>
    public class PrisonerCampMissionController : MissionLogic
    {
        private const BodyFlags GroundFlags = (BodyFlags)544321929;
        private const float HeroRadius  = 3.5f;
        private const float TroopRadius = 6.5f;

        private readonly List<Hero> _captorHeroes;
        private readonly List<Hero> _fellowPrisoners;
        private readonly List<(CharacterObject Char, int Count)> _troopGuards;

        public PrisonerCampMissionController(
            List<Hero> captorHeroes,
            List<Hero> fellowPrisoners,
            List<(CharacterObject Char, int Count)> troopGuards)
        {
            _captorHeroes    = captorHeroes;
            _fellowPrisoners = fellowPrisoners;
            _troopGuards     = troopGuards;
        }

        public override void AfterStart()
        {
            base.AfterStart();
            Mission.SetMissionMode(MissionMode.Stealth, true);
            SpawnAll();
        }

        public override InquiryData OnEndMissionRequest(out bool canPlayerLeave)
        {
            canPlayerLeave = true;
            PrisonerBehavior.OnExitScene();
            return null!;
        }

        // ── Spawning ──────────────────────────────────────────────────────────

        private void SpawnAll()
        {
            var captorParty = Hero.MainHero.PartyBelongedToAsPrisoner ?? PartyBase.MainParty;
            Vec3 center = GetSceneCenter();

            // Player at the center.
            SpawnHero(Hero.MainHero, center, Vec2.Forward,
                AgentControllerType.Player, PartyBase.MainParty);

            // ── Captor heroes — front semicircle ─────────────────────────────
            int captorCount = _captorHeroes.Count;
            for (int i = 0; i < captorCount; i++)
            {
                Vec3 pos = GetArcPos(center, i, captorCount, HeroRadius, 0f);
                SpawnHero(_captorHeroes[i], pos, FaceToward(pos, center),
                    AgentControllerType.AI, captorParty);
            }

            // ── Fellow prisoners — back semicircle ───────────────────────────
            int prisonerCount = _fellowPrisoners.Count;
            for (int i = 0; i < prisonerCount; i++)
            {
                Vec3 pos = GetArcPos(center, i, prisonerCount, HeroRadius, MathF.PI);
                SpawnHero(_fellowPrisoners[i], pos, FaceToward(pos, center),
                    AgentControllerType.AI, captorParty);
            }

            // ── Troop guards — wider ring ────────────────────────────────────
            int troopTotal = _troopGuards.Sum(g => g.Count);
            if (troopTotal > 0)
            {
                int idx = 0;
                foreach (var (charObj, count) in _troopGuards)
                {
                    for (int i = 0; i < count; i++)
                    {
                        Vec3 pos = GetArcPos(center, idx++, troopTotal, TroopRadius, 0f);
                        SpawnTroop(charObj, pos, FaceToward(pos, center), captorParty);
                    }
                }
            }
        }

        private void SpawnHero(Hero hero, Vec3 pos, Vec2 dir,
            AgentControllerType ctrl, PartyBase party, bool civilian = false)
        {
            try
            {
                var bd = new AgentBuildData(hero.CharacterObject)
                    .InitialPosition(in pos)
                    .InitialDirection(in dir)
                    .NoHorses(true)
                    .CivilianEquipment(civilian)
                    .TroopOrigin(new PartyAgentOrigin(
                        party, hero.CharacterObject, -1, default, false, false))
                    .Controller(ctrl);

                var agent = Mission.SpawnAgent(bd, false);

                if (agent != null && ctrl == AgentControllerType.AI)
                {
                    agent.AddComponent(new CommonAIComponent(agent));
                    agent.AddComponent(new HumanAIComponent(agent));
                }
            }
            catch { }
        }

        private void SpawnTroop(CharacterObject character, Vec3 pos, Vec2 dir, PartyBase party)
        {
            try
            {
                var bd = new AgentBuildData(character)
                    .InitialPosition(in pos)
                    .InitialDirection(in dir)
                    .NoHorses(true)
                    .CivilianEquipment(false)
                    .TroopOrigin(new PartyAgentOrigin(
                        party, character, -1, default, false, false))
                    .Controller(AgentControllerType.AI);

                var agent = Mission.SpawnAgent(bd, false);

                if (agent != null)
                {
                    agent.AddComponent(new CommonAIComponent(agent));
                    agent.AddComponent(new HumanAIComponent(agent));
                }
            }
            catch { }
        }

        // ── Geometry helpers ──────────────────────────────────────────────────

        private Vec3 GetSceneCenter()
        {
            Mission.Scene.GetBoundingBox(out Vec3 min, out Vec3 max);
            Vec3 c = (min + max) * 0.5f;
            c.z = Mission.Scene.GetGroundHeightAtPosition(c, GroundFlags);
            return c;
        }

        /// <summary>
        /// Places <paramref name="total"/> slots in a semicircle of <paramref name="radius"/>
        /// centred on <paramref name="baseAngle"/>.
        /// </summary>
        private Vec3 GetArcPos(Vec3 center, int index, int total,
            float radius, float baseAngle)
        {
            float step  = total > 1 ? MathF.PI / (total - 1) : 0f;
            float angle = baseAngle + index * step - MathF.PI * 0.5f;

            Vec3 pos = center;
            pos.x += MathF.Cos(angle) * radius;
            pos.y += MathF.Sin(angle) * radius;
            pos.z  = Mission.Scene.GetGroundHeightAtPosition(pos, GroundFlags);
            return pos;
        }

        private static Vec2 FaceToward(Vec3 from, Vec3 to)
        {
            Vec2 dir = new Vec2(to.x - from.x, to.y - from.y);
            if (dir.LengthSquared > 0.01f) dir.Normalize();
            else dir = Vec2.Forward;
            return dir;
        }
    }
}
