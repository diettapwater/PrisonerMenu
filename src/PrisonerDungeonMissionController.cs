using System.Collections.Generic;
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
    /// Mission logic for the prisoner dungeon scene.
    ///
    /// Player and fellow prisoners spawn in rags (burlap/hemp tunic, no armor).
    /// Captor lords and visiting nobles spawn in their civilian clothes.
    ///
    /// Player is placed at a cell-interior spawn point (any entity whose name
    /// contains "prisoner") so they appear behind bars.  Captor lords are scattered
    /// in the hallway outside.
    ///
    /// Uses MissionState.OpenNew (no MissionAgentHandler) — safe even when the
    /// player is in a prison roster.
    /// </summary>
    public class PrisonerDungeonMissionController : MissionLogic
    {
        private const BodyFlags GroundFlags = (BodyFlags)544321929;
        private const float OuterRadius = 4.0f;

        private static readonly string[] RagItemIds =
        {
            "burlap_sack_dress",
            "hemp_tunic",
            "long_hemp_tunic",
            "peasant_costume",
        };

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

            // ── Player — try to land inside a cell, stripped to rags ─────────
            var cellEntity = FindPrisonerSpawnEntity();
            if (cellEntity != null)
                SpawnHeroAtEntity(Hero.MainHero, cellEntity,
                    AgentControllerType.Player, PartyBase.MainParty, isPrisoner: true);
            else
                SpawnHero(Hero.MainHero, center, Vec2.Forward,
                    AgentControllerType.Player, PartyBase.MainParty, isPrisoner: true);

            // ── Captor lords — scatter in hallway outside cell, civilian clothes
            int captorCount = _captorLords.Count;
            for (int i = 0; i < captorCount; i++)
            {
                Vec3 pos = GetArcPos(center, i, captorCount, OuterRadius, 0f);
                SpawnHero(_captorLords[i], pos, FaceToward(pos, center),
                    AgentControllerType.AI, captorParty, isPrisoner: false);
            }

            // ── Fellow prisoners — near the player, also in rags ─────────────
            int prisonerCount = _fellowPrisoners.Count;
            for (int i = 0; i < prisonerCount; i++)
            {
                Vec3 pos = GetArcPos(center, i, prisonerCount, OuterRadius * 0.5f, MathF.PI);
                SpawnHero(_fellowPrisoners[i], pos, FaceToward(pos, center),
                    AgentControllerType.AI, captorParty, isPrisoner: true);
            }
        }

        // ── Spawn helpers ─────────────────────────────────────────────────────

        private void SpawnHeroAtEntity(Hero hero, GameEntity entity,
            AgentControllerType ctrl, PartyBase party, bool isPrisoner)
        {
            try
            {
                var bd = new AgentBuildData(hero.CharacterObject)
                    .InitialFrameFromSpawnPointEntity(entity)
                    .NoHorses(true)
                    .TroopOrigin(new PartyAgentOrigin(
                        party, hero.CharacterObject, -1, default, false, false))
                    .Controller(ctrl);

                ApplyEquipment(ref bd, isPrisoner);

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

                ApplyEquipment(ref bd, isPrisoner);

                var agent = Mission.SpawnAgent(bd, false);
                if (agent != null && ctrl == AgentControllerType.AI)
                {
                    agent.AddComponent(new CommonAIComponent(agent));
                    agent.AddComponent(new HumanAIComponent(agent));
                }
            }
            catch { }
        }

        private static void ApplyEquipment(ref AgentBuildData bd, bool isPrisoner)
        {
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
                bd = bd.CivilianEquipment(true); // lords in dungeon = visiting clothes
            }
        }

        // ── Prisoner equipment ────────────────────────────────────────────────

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

        // ── Scene entity search ───────────────────────────────────────────────

        private GameEntity? FindPrisonerSpawnEntity()
        {
            try
            {
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
