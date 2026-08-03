# Changelog

## [1.1.0]

### Added
- **View transform + anchor.** `ChunkStreamer` can stream around a separate view (camera)
  transform distinct from the target (player), centred via `StreamingAnchor` (`Target`,
  `View`, `Midpoint`). See `SetView`.
- **Directional bias.** Optional forward-biased loading shifts the window along the view's
  forward vector (`forwardBiasChunks`), damped over `biasSmoothTime` so spinning does not
  cause a load storm. Unloading is still measured from the unbiased player, so a chunk you
  stand on is never released by turning.
- **`ChunkFogSync`.** Matches `RenderSettings` linear fog to `LoadRadius x ChunkSize` (start
  at a configurable fraction), enforcing "you can never see past the loaded region" in one
  place. Added automatically to the rig created from the Setup tab.
- **Far-clip / fog validation.** The `ChunkStreamer` inspector warns when
  `LoadRadius x ChunkSize` is smaller than the fog end (or the largest camera far clip when
  fog is off), and names the minimum radius that fixes it.
- **Edit-mode tests** under `Tests/Editor/` covering `ChunkGrid.WorldToCoord` round-trips,
  `ChunkCoord.Distance` (Chebyshev), the load/unload hysteresis predicates, and LRU pool
  eviction.

### Changed
- Extracted the pure window maths (anchor, bias, hysteresis, LRU pick) into `ChunkWindow`
  so the load/unload decisions are unit-testable.
- The nearest-first request sort now uses a cached comparator instead of allocating a
  closure each refresh.

### Compatibility
- Fully backward compatible: with no view transform, `anchor = Target`, and
  `forwardBiasChunks = 0` the behaviour is identical to 1.0.0.

## [1.0.0]

### Added
- `ChunkStreamer` — moving window of chunks around a target transform.
- Separate load and unload radii (hysteresis) to stop boundary thrash.
- Chunk pooling with LRU eviction, so backtracking costs nothing.
- Load budgeting: nearest-first priority with a concurrency cap.
- `PrefabChunkProvider` and `AdditiveSceneChunkProvider`; `ChunkProvider` base for custom sources.
- `ChunkStreamTarget` for binding the local player at spawn.
- Editor window: one-click setup, scene slicing into chunk prefabs, live diagnostics grid.
- Scene view gizmos for the load and unload rings.
- On-screen debug HUD for on-device tuning.
