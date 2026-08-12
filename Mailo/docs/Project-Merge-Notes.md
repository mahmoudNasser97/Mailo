# Project Merge Notes — Networking → Gameplay

- **Date:** 2026-08-09
- **Branch:** `merge-networking-into-gameplay` (baseline commit `965eb89`)

## Background
Two separate Unity projects (same editor `6000.2.7f2`) lived inside one git repo:

| | ROOT `G:\Work\Mailo\` | INNER `G:\Work\Mailo\Mailo\` |
|---|---|---|
| Role | Networking / Steam | Gameplay (the one you open) |
| Had | FishNet + FishySteamworks, Steamworks.NET, `Scripts/Networking`, `Islam SandBox` | Toon Adventure Island map, RayFire, FImpossible, URP, SceneChunks |

Decision: **keep the INNER gameplay project as the single survivor** and copy the networking
content into it. The merge is purely additive — no inner file was modified or overwritten.

## Copied into `Mailo\Assets` (unique networking content, 0 collisions)
- `FishNet/` (FishySteamworks transport) — 21 files
- `com.rlabrecque.steamworks.net/` — 282 files
- `Scripts/` (`Mailo.Networking`, `Mailo.Steamworks.Bootstrap`) — 39 files
- `Islam SandBox/` (Game.unity, lobby debug scenes) — 9 files
- `DefaultPrefabObjects.asset` (FishNet networked-prefab registry)

## Copied outer-only overlapping data (approved; 0 GUID collisions)
- `Plugins/CodeStage/` — CodeStage AntiCheatToolkit, 589 files (not referenced by networking code)
- `_Recovery/0.unity` — stray Unity auto-recovery scene

## Config changes
- `Mailo/Packages/manifest.json` — added `com.firstgeargames.fishnet` git package (core FishNet).
- `Mailo/steam_appid.txt` — added (`480`, Steam test app id).
- **ProjectSettings — no merge needed.** Tags, Layers, and Scripting Define Symbols are
  byte-identical between the two projects.

## NOT copied (inner's versions kept — they differ and drive the actual game)
- `InputSystem_Actions.inputactions`
- `CharacterAnimaiton.controller`
- `Readme.asset`

## Unity verification checklist — DO THIS BEFORE CLEANUP
1. Open `G:\Work\Mailo\Mailo` in Unity `6000.2.7f2`. Let it resolve the FishNet git package
   (needs internet) and recompile.
2. Confirm **0 compile errors** in the console.
3. Add the networking scenes (`Islam SandBox/Game.unity`, etc.) to **Build Settings** —
   the Start flow loads the `Game` scene by name.
4. TMP text in the Islam SandBox debug scenes may need font reassignment (TextMesh Pro
   GUIDs differed between the old projects). Cosmetic, not data loss.
5. Test the lobby → Start → connect flow.

## Cleanup — ONLY AFTER verification passes
Remove the now-stale outer project from the repo root:
`Assets/`, `Packages/`, `ProjectSettings/`, `steam_appid.txt`, and the loose root-Assets files.
Leaves one clean Unity project at `Mailo\`.

## Rollback
- `git reset --hard 965eb89` (or `git checkout .`), or just delete this branch.
- Config zip backup: `scratchpad\survivor-config-backup.zip`.
- The outer project stays intact on disk until the cleanup step, as a live reference.
