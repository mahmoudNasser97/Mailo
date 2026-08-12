# Steam Multiplayer Design Spec
**Date:** 2026-07-20
**Status:** Approved
**Scope:** 4-player co-op, Steam P2P listen server with host migration, full physics sync, Steam voice chat

---

## Summary

Add 4-player co-op multiplayer to the game using FishNet (networking), FishySteamworks (Steam relay transport), and Facepunch.Steamworks (Steam SDK wrapper). All traffic routes through Valve's relay servers. One player acts as the host/listen server running full physics simulation. The three joining clients send inputs only; the host simulates and broadcasts state snapshots back. Steam handles identity, lobbies, voice, and relay.

**Stack:**
- [FishNet](https://github.com/FirstGearGames/FishNet) — tick-based networking (free, MIT)
- [FishySteamworks](https://github.com/Chykary/FishySteamworks) — Steam transport for FishNet (free)
- [Facepunch.Steamworks](https://github.com/Facepunch/Facepunch.Steamworks) — C# Steamworks SDK wrapper (free, MIT)
- Steam App ID: `480` (Spacewar) during dev/testing phase

---

## 1. Architecture Overview

```
┌─────────────────────────────────────────────┐
│           Game Layer                        │
│  PlayerController · NPCBrain · Ragdoll      │
├─────────────────────────────────────────────┤
│           Sync Layer                        │
│  NetworkedPlayer · NetworkedNPC             │
│  RagdollNetSync · CharacterNetSync          │
├─────────────────────────────────────────────┤
│           FishNet Layer                     │
│  NetworkManager · FishySteamworks Transport │
│  Tick system · NetworkTransform/Rigidbody   │
├─────────────────────────────────────────────┤
│           Steam Layer                       │
│  SteamManager · Lobby · Auth · Voice        │
└─────────────────────────────────────────────┘
```

**Host model:** ListenServer — the host is simultaneously server and a playing client. The host has full authority: runs all physics, all NPC AI state machines, and all ragdoll simulations. Clients send inputs only; the host simulates and broadcasts state.

**Tick rate:** 30 ticks/sec (server tick), aligned with `FixedUpdate`. Ragdoll bone snapshots sent at 10/sec (every 3 ticks) to reduce bandwidth.

**Scope:** Co-op only (players vs NPCs). No PvP. Max 4 players per session.

---

## 2. Steam Integration Layer

### 2.1 Initialization
A `SteamManager` MonoBehaviour on a `DontDestroyOnLoad` GameObject initializes on startup:
- Calls `SteamClient.Init(480)` (dev) or the real App ID (ship)
- If Steam is not running, logs an error and quits
- Exposes the local player's `SteamId` and display name to other systems

### 2.2 Authentication
- On join, the client requests a **Steam Auth Ticket** via `SteamUser.GetAuthSessionTicket()`
- The ticket bytes are sent to the host over FishNet (reliable RPC)
- The host verifies via `SteamUser.BeginAuthSession()` — rejects connections that fail validation
- This prevents SteamId spoofing

### 2.3 Lobbies
- Host creates a Steam Lobby (max 4 members) set to **Friends-only** or **Invite-only**
- Lobby metadata stores: host SteamId, current player count, game version
- Clients join via Steam overlay invite or `SteamMatchmaking` friend lobby browser
- Once in the lobby, clients connect to the host's SteamId as their FishNet address — no IP exchange needed
- A `LobbyManager` script wraps `SteamMatchmaking` callbacks and drives the pre-game lobby UI

### 2.4 Host Migration
When the host disconnects:
1. FishNet fires `OnClientDisconnect` on all clients
2. `SteamManager` detects host SteamId gone, elects the next client by join order
3. Elected client calls `NetworkManager.StartServer()` and recreates the Steam Lobby
4. Remaining clients reconnect to the new host's SteamId
5. NPC state and player positions re-sync on reconnect via a full state dump RPC
6. Expected downtime: ~2–3 seconds (acceptable for co-op)

---

## 3. Networking Layer (FishNet)

### 3.1 NetworkManager Setup
- Single `NetworkManager` GameObject, `DontDestroyOnLoad`
- Transport: `FishySteamworks`
- Server tick: 30/sec
- `PlayerSpawner` component instantiates the correct `NetworkedPlayer` prefab on client connect

### 3.2 NetworkObjects
| Object | Owner | Spawn |
|---|---|---|
| `NetworkedPlayer` | Respective client | Host spawns on join |
| `NetworkedNPC` | Host | Host spawns at scene load |
| `NetworkedPickup` | Host (transfers on grab) | Host spawns at scene load |

### 3.3 Input Flow (Client → Host)
Every tick each client packages and sends an `InputPayload`:

```csharp
struct InputPayload {
    int     tick;
    Vector2 move;         // WASD normalized
    bool    jumpPressed;
    bool    attackPressed;
    bool    grabPressed;
    bool    throwPressed;
    Vector3 aimDirection;
}
```

Sent **unreliably** every tick (loss is acceptable — next tick overwrites). The host feeds the payload into `PhysicsCharacterController` on behalf of that client's `NetworkedPlayer`.

### 3.4 State Sync (Host → Clients)
Every tick the host sends a `StateSnapshot` per entity:

**Player snapshot (Balanced mode):**
- Root Rigidbody: position, rotation, linear velocity, angular velocity
- Current `CharacterPhysicsState` enum

**Player snapshot (Ragdoll mode):**
- Per-bone: position + rotation for every entry in `ragdollBodies[]`
- Sent at 10/sec (every 3 ticks) — interpolated on clients

**NPC snapshot:**
- Root position + rotation (every tick, unreliable)
- `NPCState` enum (reliable RPC on change only)
- Ragdoll bones when in HitReact state (10/sec)

**Pickup snapshot:**
- Transform (position + rotation) via `NetworkTransform`, every tick

### 3.5 Client-Side Prediction & Reconciliation
- Clients run `HandleMovement()` locally on input immediately (prediction)
- When host authoritative state arrives, compare against predicted state
- If delta > threshold, snap to host state and re-simulate buffered inputs (reconciliation)
- Prevents input lag while maintaining host authority

### 3.6 Bandwidth Estimate
| Source | Size/tick | Count | Total |
|---|---|---|---|
| Player root RB | ~48 bytes | 4 | 192 bytes |
| Player ragdoll bones (10/sec) | ~720 bytes | 4 | 2,880 bytes (amortized ~960/tick) |
| NPC root | ~28 bytes | up to 8 NPCs | 224 bytes |
| NPC ragdoll (10/sec) | ~720 bytes | up to 8 | amortized ~1,920/tick |
| Inputs (client→host) | ~24 bytes | 3 clients | 72 bytes |
| **Peak total** | | | **~3.4 KB/tick → ~100 KB/sec** |

Well within Steam relay limits (~1 MB/sec per connection).

---

## 4. Physics State Sync

### 4.1 Player — Balanced Mode
- `NetworkRigidbody` on root syncs position, rotation, velocity
- Clients interpolate between last two received snapshots (1–2 tick buffer)
- Local prediction covers the round-trip latency gap

### 4.2 Player — Ragdoll Mode
- `RagdollNetSync` component iterates `ragdollBodies[]`, packages `RagdollSnapshot`
- Client bones set **kinematic** during ragdoll sync — driven by snapshot interpolation only
- Host still runs full PhysX ragdoll simulation; clients display the result

### 4.3 State Transitions
- `CharacterPhysicsState` changes (Balanced ↔ Ragdoll ↔ GettingUp) sent as **reliable RPCs**
- Ensures the get-up animation triggers correctly on all clients regardless of packet loss
- Client switches bone kinematic mode on transition receipt

### 4.4 NPC Sync
- Host runs all NPC AI (`NPCBrain`, `NPCPatroller`, `NPCChaser`, `NPCThrower`, etc.) unchanged
- `NetworkedNPCSync` component added to each NPC prefab — sends state to clients
- Clients run **no NPC AI** — purely display received positions/states
- NPC hit reactions triggered on host by existing `HitReactor.OnImpact` event; result synced

### 4.5 Pickup Objects
- `Pickupable` objects get `NetworkObject` + `NetworkTransform`
- On grab: client sends grab request RPC → host validates → transfers `NetworkObject` ownership to grabbing client
- While held: owner (the client) is authoritative for the object transform
- On throw: ownership returns to host; host simulates arc physics via `ThrowMath.TryCalculateVelocity`; result synced to all clients

---

## 5. Steam Voice Chat

### 5.1 SteamVoiceManager
A `SteamVoiceManager` component on a persistent GameObject handles all voice:
- Push-to-talk key: `V` (configurable)
- Uses `SteamUser.StartVoiceRecording()` / `StopVoiceRecording()` / `GetVoice()`
- Sends compressed voice buffer as **unreliable** FishNet RPC to all other clients (piggybacks on existing Steam relay connection)

### 5.2 Playback
- Each `NetworkedPlayer` prefab has a dedicated `AudioSource` (3D spatial, blend = 1.0)
- On receive: `SteamUser.DecompressVoice()` → raw PCM → ring buffer AudioClip (~200ms) → `AudioSource.Play()`
- Volume falls off naturally with distance via Unity's audio rolloff curve

### 5.3 Push-to-Talk Only
- Push-to-talk is the default and only mode for this phase
- Voice Activity Detection deferred to a future iteration

### 5.4 HUD Indicator
- When transmitting, a world-space icon appears above the local player's head (visible to all clients)
- Toggled by a broadcast RPC on push-to-talk key press/release
- Simple `Canvas` in world-space mode, parented to the player head bone

---

## 6. New Scripts Summary

| Script | Purpose |
|---|---|
| `SteamManager` | Init Steamworks SDK, expose SteamId, handle shutdown |
| `LobbyManager` | Create/join/leave Steam Lobbies, metadata, invites |
| `SteamAuthHandler` | Auth ticket request, send to host, host-side verification |
| `NetworkBootstrapper` | Start host or client based on lobby role, scene management |
| `NetworkedPlayer` | NetworkObject + input collection + prediction on client |
| `CharacterNetSync` | Sync root Rigidbody state for Balanced mode |
| `RagdollNetSync` | Sync all ragdoll bone transforms at 10/sec |
| `NetworkedNPCSync` | Sync NPC root position + NPCState to clients |
| `NetworkedPickup` | NetworkTransform + ownership transfer on grab/throw |
| `HostMigrationHandler` | Detect host disconnect, elect new host, reconnect clients |
| `SteamVoiceManager` | Record, send, receive, decompress, play back voice |
| `VoiceHUDIndicator` | World-space talking icon above player head |
| `PlayerSpawner` | Spawn correct player prefab on client connect |

---

## 7. Key Modifications to Existing Code

These existing scripts must be changed to support multiplayer — they are not new files:

| Script | Required Change |
|---|---|
| `PhysicsCharacterController` | Extract `HandleMovement()` to accept `InputPayload` parameter instead of reading `Input.GetAxisRaw()` directly. On clients, the local player still feeds real input; for remote players the host feeds their received `InputPayload`. |
| `NPCBrain` | Add a server-only guard (`if (!IsServer) return;`) so AI update loop only runs on host. |
| `ObjectGrabController` | Replace direct physics manipulation with RPC requests to host for grab/throw validation and ownership transfer. |
| `HitReactor` | `OnImpact` event handlers that mutate physics state must only execute on host; clients receive results via `RagdollNetSync`. |

---

## 8. Out of Scope (This Phase)

- Dedicated server hosting
- Matchmaking with strangers (public lobbies)
- Anti-cheat
- Voice Activity Detection
- Cross-platform (PC only via Steam)
- More than 4 players
- PvP mode
