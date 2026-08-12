# Scene Chunks Integration — Session Notes

_Saved 2026-08-04. Source brief: `C:\Users\pc\Downloads\scene-chunks-claude-code-brief.md`._
_Package: `Packages/com.nasser.scene-chunks` (embedded, bumped 1.0.0 → 1.1.0)._

Goal: grid-based streaming of **props only** in `Demo_Island.unity`; terrain stays resident.
Work through the brief's phases in order; stop for review after Phase 0 (done) and after
Phase 3's Preview (pending).

---

## Phase 0 — Recon (done, signed off)

| Item | Finding |
|---|---|
| Engine | Unity 6000.2.7f2, URP 17.2.0 |
| Map | `Assets/Toon Adventure Island/Scenes/Demo_Island.unity`, ~750 m × ~1200 m |
| Props | 13,834 prefab instances under 10 flat category roots (Grass 7,912 · Vegetation 2,020 · Props 1,788 · Rocks 914 · Trees 551 · Ruins 508 · Animals 73 · Ships 36 · FX 28 · Animal Groups 3), nesting depth 1. ~25–35k renderers |
| Terrain | 1 object, 513×513 heightmap, `TAI_Demo_Terrain.asset` ~3.2 MB — stays resident |
| Camera | Gameplay `Character Camera` far clip 5000 m; ~3.5 m third-person pull-back |
| Fog | Linear 130 → 1050 m (defeats streaming until pulled in) |
| Max speed | ~30 m/s sustained (grapple, `GrappleController._maxSwingSpeed:30`), ~60 m/s burst |
| Netcode | **None** (no Fusion/Mirror/FishNet/NGO). Steamworks.NET only, in the **sibling project** at repo root `G:\Work\Mailo\Assets\`. Demo_Island is effectively single-player today |

**Decision — Profile B (Balanced):** ChunkSize **125**, LoadRadius **2**, UnloadPadding **1**,
fog pulled to **250 m** via ChunkFogSync. ~25 active chunks, 625 m footprint, ~55% fewer
resident props/draw-calls. The real lever was view distance (fog), not the speed floor (~30 m).

---

## What was implemented

**Phase 1:** `Assets/Settings/Streaming/ChunkStreamingSettings.asset` (Profile B values).

**Phase 2** (all in the package, fully backward-compatible):
- 2a View transform + `StreamingAnchor {Target, View, Midpoint}`, `SetView()` — `ChunkStreamer.cs`, `ChunkWindow.cs`
- 2b Smoothed directional bias (`forwardBiasChunks`, `biasSmoothTime`); unload measured from the unbiased player — `ChunkStreamer.cs`
- 2c `ChunkFogSync.cs` — fog end = LoadRadius×ChunkSize, start 60%; auto-added to the rig
- 2d Far-clip / fog validation warning — `ChunkStreamerEditor.cs`
- 2e Edit-mode tests — `Tests/Editor/` (ChunkGrid round-trips, Chebyshev distance, hysteresis, LRU)
- Extra: `ChunkWindow.cs` (pure testable math), cached sort comparator (no per-refresh alloc),
  `SceneChunksFlattenTool.cs` (flatten multi-root map), README/CHANGELOG updated,
  `Packages/manifest.json` gained `testables`.

---

## Verification status — NOT yet run

Code is written and self-reviewed but **not compiled or tested** (editor was open/locked).
- Focus Unity → let it import/compile → check Console for errors.
- Window → General → Test Runner → EditMode → Run All (filter `Nasser.SceneChunks.Tests`).

---

## Remaining steps (editor checklist)

Do these top to bottom. Two report-back gates: after step 2 (test result) and at step 4 (Preview).

1. **Compile:** focus Unity so it imports the new files → open Console (Ctrl+Shift+C) → confirm
   0 errors. If red errors appear, stop and fix them first.
2. **Run tests:** Window → General → Test Runner → EditMode → Run All (filter
   `Nasser.SceneChunks.Tests`). Expect ~20 green (acceptance criterion #6). **Report result.**
3. **Create rig:** Tools → Scene Chunks → Streaming Setup (Ctrl+Shift+K) → Setup tab → drag
   `Assets/Settings/Streaming/ChunkStreamingSettings.asset` into the Settings field → Create
   Streaming Rig In Scene. (Adds ChunkFogSync → fog snaps 1050 → 250 m, intended.)
4. **Phase 3 — flatten + Preview (STOP):** in the Hierarchy select the static visual roots
   (Grass, Vegetation, Props, Rocks, Trees, Ruins; review Animals/FX/Ships/Animal Groups and
   exclude anything that moves or is interactable; keep Terrain/lights/navmesh/audio out) →
   Tools → Scene Chunks → **Flatten Selected Roots For Slicing** → Slice tab → Source Root =
   `Streamable Root`, Chunk Size 125 → **Preview Distribution**.
   **→ STOP and send the Preview distribution** (flag hotspots >3× median) before baking.
5. **Bake** (only after Preview review): Slice tab → Slice And Bake Prefabs (keep Disable
   Originals on).
6. **Phase 4 — wire player:** add `ChunkStreamTarget` to `ThirdPersonPuppet` (`bindOnEnable = true`
   is fine while single-player; guard with `Bind()` when FishNet lands). Assigning Character
   Camera as the streamer `view` is optional here.
7. **Acceptance:** Diagnostics grid shows zero load/unload after settling on a boundary; spinning
   360° releases nothing near the player; a full lap at max speed shows no pop-in inside fog;
   Profiler (on a build) — record peak memory + frame time before/after.

_Alternative to step 2: I can run the tests headless if you close the editor (I'll locate Unity 6000.2.7f2)._

## Gotchas
- ChunkFogSync overwrites RenderSettings fog whenever enabled — the single source of truth for view distance.
- The slicer clears the provider each run and groups one root's direct children → hence the flatten step.
- Don't put gameplay state in chunk prefabs (pickups/doors/spawns stay server-authoritative).
