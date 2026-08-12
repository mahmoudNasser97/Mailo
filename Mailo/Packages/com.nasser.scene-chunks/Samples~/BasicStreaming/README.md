# Basic Streaming sample

A minimal setup you can copy:

1. Create an empty GameObject, add `ChunkStreamer` and `PrefabChunkProvider`.
2. Create a `ChunkStreamingSettings` asset (Create > Scene Chunks > Streaming Settings)
   and assign it to the streamer.
3. Make a couple of chunk prefabs — a 50x50 ground plane with some props, pivot at the
   corner — and add them to the provider's entry list at coords (0,0), (1,0), (0,1)...
4. Add a capsule with `ChunkStreamTarget` and any movement script.
5. Press play and walk. Watch the debug HUD.

The `SimpleOrbitTarget` script below moves a transform in a loop if you just want to
see the system run without writing a controller.
