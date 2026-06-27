# Imprisoned — Bannerlord RP Mod

A Mount & Blade II Bannerlord mod that gives captured heroes an active role instead of a passive wait screen. When imprisoned — whether in a castle dungeon or a mobile lord's camp — press **F7** on the campaign map to enter a live 3D scene with your captors, their soldiers, and fellow prisoners.

Built primarily to support **ChatAI** group conversations: once inside the scene you can open ChatAI's group menu (Alt+H / G) and have the captor lord, their companions, guards, and other prisoners all participate in the same AI-driven conversation.

---

## Features

### F7 — Enter the Scene (campaign map only, while prisoner)

| Situation | What opens |
|---|---|
| Held in a castle or town | **Dungeon scene** — the actual settlement prison interior. The player spawns inside a cell; captor lords and visiting heroes spawn in the hallway outside. |
| Held by a mobile party on the map | **Camp scene** — an outdoor terrain-matched scene. Captor heroes, army lords, troop guards (up to 6), and fellow prisoners all spawn around you in battle gear. |

### Who appears in the camp scene
- Every hero in the captor's direct party
- Every hero in any **army** the captor belongs to
- Heroes/parties tracked in **FollowAll** (soft dependency — gracefully skipped if FollowAll isn't loaded)
- Up to 6 troop soldiers from the captor's roster as camp guards
- Other hero prisoners

### Dungeon scene
- Player placed at a cell-interior spawn point (`sp_prisoner*` entity scan); falls back to scene center if none found in that particular dungeon layout
- Settlement owner/governor + visiting lords spawn outside the cell for interaction
- Other imprisoned heroes appear nearby

### Ransom suppression
Vanilla's automatic ransom system is fully disabled for the player so RP arcs can run as long as needed:

| What's blocked | How |
|---|---|
| AI deciding to ransom you | `ConsiderRansomPrisoner` prefix |
| The ransom offer panel appearing | `OnRansomOffered` prefix |
| Any code path actually freeing you via ransom | `EndCaptivityAction.ApplyByRansom` prefix |
| "Propose ransom" option in captivity menu | Postfix disables + tooltips it |

---

## Requirements

- Mount & Blade II Bannerlord (tested on e1.2.x / 1.x)
- **Bannerlord.Harmony** — must be enabled in the launcher above this mod

## Optional

- **ChatAI** — the main use case; group conversations in the spawned scene
- **FollowAll** — tracked heroes automatically appear in the camp scene

---

## Installation

1. Download and extract so that `PrisonerMenu/` is a folder inside your `Modules/` directory.
2. Enable **Bannerlord.Harmony** in the launcher (above PrisonerMenu in load order).
3. Enable **Prisoner Menu** in the launcher.
4. Load a save, get captured, open the campaign map, press **F7**.

---

## Building from Source

```powershell
# Set game directory (or let the .csproj default kick in)
$env:BANNERLORD_GAME_DIR = "F:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord"

cd src
dotnet build PrisonerMenu.csproj -c Release -p:Platform=x64 -f net472
# DLL outputs to ../bin/Win64_Shipping_Client/PrisonerMenu.dll
```

Close Bannerlord / BLSE before building — the launcher holds a lock on the DLL.

### Dependencies (all resolved from your game install, nothing to download)
- `TaleWorlds.MountAndBlade`
- `TaleWorlds.CampaignSystem`
- `TaleWorlds.Core`, `TaleWorlds.Library`, `TaleWorlds.Engine`
- `SandBox`, `SandBox.View`, `SandBox.GauntletUI`
- `TaleWorlds.MountAndBlade.GauntletUI`, `.View`
- `0Harmony` (from Bannerlord.Harmony module)

---

## How It Works

The mod uses `MissionState.OpenNew` with a custom `MissionLogic` controller for both the camp and dungeon scenes — bypassing `MissionAgentHandler` entirely. This is necessary because the player's prison-roster state makes the standard agent handler crash during `EarlyStart()`. All agents (player, lords, troops, prisoners) are spawned manually via `AgentBuildData` + `PartyAgentOrigin`.

Ransom suppression uses **Harmony prefix patches** applied through a `TryPatch` helper that silently skips any method that can't be found — no startup crash if a game update renames an internal method.

FollowAll integration is a **soft reflection dependency**: the mod reads `FollowAll.FollowPartiesCampaignBehavior.FollowingHeroes` and `FollowingParties` at runtime via `AccessTools`. If FollowAll is absent, the try/catch swallows the miss and the scene opens normally with captor/army heroes only.

---

## Compatibility

- No XML data changes — no load-order conflicts with other mods
- Does not patch `MissionAgentHandler` or any combat system
- Ransom patches are specific to `Hero.MainHero` — NPC ransom between other lords is unaffected
- Safe to add/remove mid-campaign (no saved data)

---

## License

MIT
