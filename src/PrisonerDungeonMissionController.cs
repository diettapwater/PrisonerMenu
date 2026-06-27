using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Imprisoned
{
    /// <summary>
    /// Mission logic for the prisoner dungeon scene.
    ///
    /// Tries to spawn the player at a cell-interior spawn point (any scene entity
    /// whose name contains "prisoner") so they appear behind bars.  Captor lords
    /// and fellow hero-prisoners are spawned in the general dungeon area at offset
    /// positions so they can be interacted with via ChatAI (Alt+H / G etc.).
    ///
    /// Uses MissionState.OpenNew (no MissionAgentHandler) — safe even when the
    /// player is in a prison roster.
    /// </summary>
    public class PrisonerDungeonMissionController : MissionLogic
    {
        private const BodyFlags GroundFlags = (BodyFlags)544321929;
        private const float OuterRadius = 4.0f;

        private readonly List<Hero> _captorLords;
        private readonly List<Hero> _fellowPrisoners;

        public PrisonerDungeonMissionController(
            List<Hero> captorLords, List<Hero> fellowPrisoners)
        {
            _captorLords     = captorLords;
            _fellowPrisoners = fellowPrisoners;
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

            // ── Player — try to land inside a cell ───────────────────────────
            var cellEntity = FindPrisonerSpawnEntity();
            if (cellEntity != null)
                SpawnHeroAtEntity(Hero.MainHero, cellEntity, AgentControllerType.Player, PartyBase.MainParty, civilian: true);
            else
                SpawnHero(Hero.MainHero, center, Vec2.Forward, AgentControllerType.Player, PartyBase.MainParty, civilian: true);

            // ── Captor lords — scatter around the dungeon center ─────────────
            int captorCount = _captorLords.Count;
            for (int i = 0; i < captorCount; i++)
            {
                Vec3 pos = GetArcPos(center, i, captorCount, OuterRadius, 0f);
                SpawnHero(_captorLords[i], pos, FaceToward(pos, center),
                    AgentControllerType.AI, captorParty, civilian: true);
            }

            // ── Fellow prisoners — near the player, slightly offset ──────────
            int prisonerCount = _fellowPrisoners.Count;
            for (int i = 0; i < prisonerCount; i++)
            {
                Vec3 pos = GetArcPos(center, i, prisonerCount, OuterRadius * 0.5f, MathF.PI);
                SpawnHero(_fellowPrisoners[i], pos, FaceToward(pos, center),
                    AgentControllerType.AI, captorParty, civilian: true);
            }
        }

        // ── Spawn helpers ─────────────────────────────────────────────────────

        private void SpawnHeroAtEntity(Hero hero, GameEntity entity,
            AgentControllerType ctrl, PartyBase party, bool civilian)
        {
            try
            {
                var bd = new AgentBuildData(hero.CharacterObject)
                    .InitialFrameFromSpawnPointEntity(entity)
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

        private void SpawnHero(Hero hero, Vec3 pos, Vec2 dir,
            AgentControllerType ctrl, PartyBase party, bool civilian)
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

        // ── Scene entity search ───────────────────────────────────────────────

        /// <summary>
        /// Scans all scene entities for one whose name suggests a prisoner spawn
        /// point (e.g. "sp_prisoner", "prisoner_spawn_point", etc.).
        /// Returns null if none found — caller falls back to scene center.
        /// </summary>
        private GameEntity? FindPrisonerSpawnEntity()
        {
            try
            {
                // Direct name lookups first (fast path)
                string[] knownNames = {
                    "sp_prisoner", "sp_prison_prisoner", "prisoner_spawn",
                    "sp_prisoner_side", "prisoner_spawn_point"
                };
                foreach (var name in knownNames)
                {
                    try
                    {
                        var e = Mission.Scene.FindEntityWithName(name);
                        if (e != null) return e;
                    }
                    catch { }
                }

                // Full scan — any entity whose name contains "prisoner"
                var all = new List<GameEntity>();
                Mission.Scene.GetEntities(ref all);
                foreach (var e in all)
                {
                    string eName = e.Name ?? "";
                    if (eName.IndexOf("prisoner", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return e;
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
