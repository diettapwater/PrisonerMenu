using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace Imprisoned
{
    /// <summary>
    /// Mission logic for the prisoner camp scene.
    ///
    /// Spawns the player, captor heroes, troop guards, and fellow prisoners manually —
    /// no MissionAgentHandler, so the prison roster state doesn't crash agent setup.
    ///
    /// Player and fellow prisoners wear stripped prisoner rags (burlap/hemp tunic).
    /// Captors and guards wear their full battle gear.
    /// </summary>
    public class PrisonerCampMissionController : MissionLogic
    {
        private const BodyFlags GroundFlags = (BodyFlags)544321929;
        private const float HeroRadius  = 4.0f;
        private const float TroopRadius = 7.0f;

        private static readonly string[] RagItemIds =
        {
            "burlap_sack_dress",
            "hemp_tunic",
            "long_hemp_tunic",
            "peasant_costume",
        };

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

            Vec3 center = GetSceneCenter();
            SpawnAll(center);
        }

        public override InquiryData OnEndMissionRequest(out bool canPlayerLeave)
        {
            canPlayerLeave = true;
            PrisonerBehavior.OnExitScene();
            return null!;
        }

        // ── Spawning ──────────────────────────────────────────────────────────

        private void SpawnAll(Vec3 center)
        {
            var captorParty = Hero.MainHero.PartyBelongedToAsPrisoner ?? PartyBase.MainParty;

            // Player at the center — stripped to rags.
            SpawnHero(Hero.MainHero, center, Vec2.Forward,
                AgentControllerType.Player, PartyBase.MainParty, isPrisoner: true);

            // ── Captor heroes — front semicircle, full battle gear ────────────
            int captorCount = _captorHeroes.Count;
            for (int i = 0; i < captorCount; i++)
            {
                Vec3 pos = GetArcPos(center, i, captorCount, HeroRadius, 0f);
                SpawnHero(_captorHeroes[i], pos, FaceToward(pos, center),
                    AgentControllerType.AI, captorParty, isPrisoner: false);
            }

            // ── Fellow prisoners — back semicircle, also in rags ─────────────
            int prisonerCount = _fellowPrisoners.Count;
            for (int i = 0; i < prisonerCount; i++)
            {
                Vec3 pos = GetArcPos(center, i, prisonerCount, HeroRadius, MathF.PI);
                SpawnHero(_fellowPrisoners[i], pos, FaceToward(pos, center),
                    AgentControllerType.AI, captorParty, isPrisoner: true);
            }

            // ── Troop guards — wider ring, battle gear ────────────────────────
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
            AgentControllerType ctrl, PartyBase party, bool isPrisoner)
        {
            try
            {
                var bd = new AgentBuildData(hero.CharacterObject)
                    .InitialPosition(in pos)
                    .InitialDirection(in dir)
                    .NoHorses(true)
                    .TroopOrigin(new PartyAgentOrigin(
                        party, hero.CharacterObject, -1, default, false, false))
                    .Controller(ctrl);

                if (isPrisoner)
                {
                    var rags = BuildPrisonerEquipment();
                    if (rags != null)
                        bd = bd.Equipment(rags);
                    else
                        bd = bd.CivilianEquipment(true);
                }
                else
                {
                    bd = bd.CivilianEquipment(false); // battle gear
                }

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
                    .CivilianEquipment(false) // battle gear for guards
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

        // ── Prisoner equipment ────────────────────────────────────────────────

        /// <summary>
        /// Builds a minimal equipment set — just a ragged body piece, nothing else.
        /// Returns null if no suitable item is found (caller falls back to civilian gear).
        /// </summary>
        private static Equipment? BuildPrisonerEquipment()
        {
            try
            {
                foreach (var id in RagItemIds)
                {
                    var item = MBObjectManager.Instance.GetObject<ItemObject>(id);
                    if (item == null) continue;

                    var eq = new Equipment(Equipment.EquipmentType.Battle);
                    eq.AddEquipmentToSlotWithoutAgent(
                        EquipmentIndex.Body, new EquipmentElement(item));
                    return eq;
                }
            }
            catch { }
            return null;
        }

        // ── Geometry helpers ──────────────────────────────────────────────────

        private Vec3 GetSceneCenter()
        {
            Mission.Scene.GetBoundingBox(out Vec3 min, out Vec3 max);
            Vec3 c = (min + max) * 0.5f;
            c.z = Mission.Scene.GetGroundHeightAtPosition(c, GroundFlags);
            return c;
        }

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
