# Scene Chunks

Grid-based scene streaming for Unity. The world is split into chunks and only the
ones near the player are resident, so memory stays flat regardless of how large the
map gets.

Built for mobile multiplayer, where the budget is tight and every client is looking
at a different part of the world.

---

## Install

**Local package (recommended while iterating)**

Copy `com.nasser.scene-chunks` into your project's `Packages/` folder. Unity picks
it up automatically.

**From git**

Window → Package Manager → `+` → *Add package from git URL*:

```
https://github.com/<you>/scene-chunks.git?path=/com.nasser.scene-chunks
```

**From disk**

Package Manager → `+` → *Add package from disk* → select `package.json`.

---

## Quick start

Open **Tools → Scene Chunks → Streaming Setup** (`Ctrl/Cmd + Shift + K`).

1. **Setup tab** — set your chunk size and load radius, click *Create Settings Asset*,
   then *Create Streaming Rig In Scene*. That adds a `Chunk Streaming` object with a
   `ChunkStreamer`, a `PrefabChunkProvider`, and a debug HUD.
2. **Slice tab** — drag your existing hand-built map root into *Source Root* and click
   *Slice And Bake Prefabs*. Every child is assigned to a chunk by its renderer bounds,
   baked into a prefab at its chunk origin, and wired into the provider.
3. **Player** — add `ChunkStreamTarget` to your player prefab.
4. Press play. The **Diagnostics tab** shows a live grid of what is active, loading,
   and pooled.

Already have chunk prefabs? Skip step 2 and fill in the provider's entry list directly.

---

## How it works

```
Queued  ->  Loading  ->  Active  ->  Pooled
                            ^           |
                            +-----------+
                          player returns
```

Each refresh tick the streamer converts the target's position to a chunk coord, then:

- **Requests** every missing chunk within `LoadRadius`, nearest first, capped by
  `MaxConcurrentLoads`. A chunk already in the pool is reactivated instantly.
- **Releases** chunks past `UnloadRadius` (= `LoadRadius + UnloadPadding`), capped by
  `MaxReleasesPerTick`.

Released chunks are deactivated rather than destroyed and held in an LRU pool, so
walking back the way you came costs nothing.

### The three settings that matter

| Setting | What it buys you | What it costs |
|---|---|---|
| `LoadRadius` | Distance before the player can see the edge of the loaded world | Memory and draw calls, quadratically |
| `UnloadPadding` | Immunity to boundary thrash when the player paces back and forth | A ring of chunks kept resident that you cannot see |
| `MaxConcurrentLoads` | Flat frame time — loads don't pile up in one frame | Slower to catch up, so needs a larger radius |

`UnloadPadding = 0` is almost always a bug. With load and unload on the same boundary,
a player stepping over an edge repeatedly will load and destroy the same chunk every
few seconds. The custom inspector warns about this.

**Sizing the radius:** it has to cover more ground than the player crosses in the time
a chunk takes to load. Sprint speed × worst-case load time, rounded up, is the floor.

---

## Streaming around the camera (view transform, bias, fog)

By default the window follows the `target` (player). A third-person or pulled-back camera
looks at chunks the player's position never requests, so the streamer can also take a
separate **view transform**:

```csharp
streamer.SetView(gameplayCamera.transform);
streamer.Anchor = StreamingAnchor.View; // or Midpoint, or Target (default)
```

**Directional bias** streams more of what you are looking at by shifting the window along
the view's forward vector, in chunks (`forwardBiasChunks`, typically 0.5–1.5). It is damped
over `biasSmoothTime` so a fast turn ramps in instead of triggering a load storm. Unloading
is always measured from the *unbiased* player, so spinning the camera never releases the
chunk you are standing on. With bias at 0 and `anchor = Target`, behaviour is unchanged.

**`ChunkFogSync`** ties the fog end distance to `LoadRadius × ChunkSize` (fog start at a
fraction of that), so the player can never see past the loaded world. Drop it on the
streaming rig — the Setup tab adds it for you — and it keeps `RenderSettings` fog in sync.
Use *Override Distance* for a manual value. The `ChunkStreamer` inspector warns loudly if
the load reach is shorter than the fog end (or camera far clip when fog is off).

## Multiplayer

Streaming here is **local and cosmetic**. Point the streamer at the local player only —
call `ChunkStreamTarget.Bind()` from your networked spawn callback when the object is
locally owned, and leave remote players unbound.

Nothing about game state may depend on what a client has loaded. A player standing in
an unloaded chunk still exists; the server is still authoritative. If a chunk holds
gameplay-relevant objects (pickups, doors, spawn points), their state must live on the
server, with the chunk prefab holding only the visual representation.

```csharp
// Photon Fusion example
public override void Spawned()
{
    if (Object.HasInputAuthority)
        GetComponent<ChunkStreamTarget>().Bind();
}
```

---

## Custom providers

Subclass `ChunkProvider` to stream from Addressables, asset bundles, or a procedural
generator:

```csharp
public class MyProvider : ChunkProvider
{
    public override bool Exists(ChunkCoord coord) => true; // infinite world

    public override IEnumerator Load(ChunkCoord coord, Transform parent, Action<GameObject> onLoaded)
    {
        var handle = Addressables.InstantiateAsync("chunk_" + coord, parent);
        yield return handle;
        onLoaded(handle.Result);
    }

    public override void Unload(ChunkCoord coord, GameObject root)
    {
        Addressables.ReleaseInstance(root);
    }
}
```

Return `false` from `Exists` for coords outside your authored world so the streamer
stops asking.

---

## API

| Member | Purpose |
|---|---|
| `ChunkStreamer.SetTarget(Transform)` | Bind or rebind the transform the window follows |
| `ChunkStreamer.ChunkActivated` | Event, fired when a chunk becomes visible |
| `ChunkStreamer.ChunkReleased` | Event, fired when a chunk leaves the active set |
| `ChunkStreamer.FlushAll()` | Drop everything — call before teleporting across the map |
| `ChunkStreamer.GetState(ChunkCoord)` | Current state of a specific chunk |
| `ChunkGrid.WorldToCoord(Vector3, float)` | World position to chunk coord |

---

## Notes and limits

- The grid is 2D on XZ. Vertical worlds would need a third axis on `ChunkCoord`.
- Baked chunk prefabs are static geometry. Anything that moves between chunks should
  live outside the streamed hierarchy.
- Lightmaps and light probes bake per scene, so the additive-scene provider handles
  baked lighting better than the prefab provider.
- Occlusion culling data does not follow instantiated prefabs.
