# Steam Multiplayer Design Spec
**Date:** 2026-07-27
**Status:** Approved
**Scope:** 4-player co-op, Steam P2P listen server, full physics sync, grapple sync, RayFire sync, Steam voice chat

---

## 1. Stack

```
Unity 6
  └── FishNet (networking)
       └── FishySteamworks (Steam transport for FishNet)
            └── Steamworks.NET (C# Steam SDK wrapper)
                 └── Steam Networking / Valve Relay
```

**Package versions to pin (record exact versions after install):**
- FishNet: install from Asset Store or OpenUPM
- FishySteamworks: https://github.com/FirstGearGames/FishySteamworks
- Steamworks.NET: https://github.com/rlabrecque/Steamworks.NET

**Rule:** Never install Facepunch.Steamworks or FishyFacepunch. These are incompatible with FishySteamworks.

**Steam App ID:**
- Dev/testing: `480` (Spacewar — shared test app)
- Production: real App ID from Steam partner dashboard

---

## 2. Architecture Overview

```
┌─────────────────────────────────────────────────────┐
│  Game Layer                                         │
│  PhysicsCharacterController · NPCBrain · GrappleCtrl│
│  ObjectGrabController · DestructibleObject          │
├─────────────────────────────────────────────────────┤
│  Sync Layer                                         │
│  NetworkedPlayer · CharacterNetSync · RagdollNetSync│
│  NPCServerAuthority · NPCStateSync                  │
│  NetworkedPickup · NetworkedGrapple                 │
│  NetworkedDestructible · SteamVoiceCapture          │
├─────────────────────────────────────────────────────┤
│  FishNet Layer                                      │
│  NetworkManager · FishySteamworks Transport         │
│  Tick system · NetworkTransform · NetworkObject     │
├─────────────────────────────────────────────────────┤
│  Steam Layer                                        │
│  SteamBootstrap · SteamIdentityManager              │
│  SteamLobbyManager · SteamAuthManager · Voice API  │
└─────────────────────────────────────────────────────┘
```

### Host Model
One player is simultaneously the FishNet server and a local playing client (ListenServer). The host runs all physics, all NPC AI, all ragdoll simulation, all demolition events. Remote clients send inputs only and display received state.

### Tick Rate
- Server tick: 30 Hz
- Ragdoll bone snapshots: 10 Hz (every 3 ticks)
- NPC snapshots: 15 Hz
- Voice capture chunks: ~20-40ms intervals

### Important: FishNet tick ≠ Unity FixedUpdate
Do NOT assume they are automatically aligned. All movement must go through the FishNet tick and prediction workflow, not FixedUpdate directly.

---

## 3. Authority Table

| System | Authority | Client behaviour |
|---|---|---|
| Player root Rigidbody (Balanced mode) | Host | Interpolate received snapshots |
| Player ragdoll bones (PhysicsCharacterController) | Host | Kinematic, driven by snapshot interpolation |
| PuppetMaster ragdoll muscles (PuppetRagdollController) | Host | Kinematic, driven by snapshot interpolation |
| NPCBrain + NPCPatroller + NPCChaser + NPCThrower | Host only | No AI runs on clients |
| NPCHitReaction + NPCHitVFX | Host triggers, result synced | Clients display synced state |
| ObjectGrabController grab/throw | Host validates, executes | Client sends request RPC |
| Pickupable / NetworkedPickup | Host | NetworkTransform interpolation |
| GrappleController rope/anchor | Owner client (local prediction) | All clients via NetworkTransform on anchor |
| DestructibleObject (RayFire) | Host triggers Demolish() | Clients receive reliable RPC → call Demolish() locally |
| HitReactor.TakeHit() | Host only | Ignored on clients |
| Voice packets | Each client captures locally | Routed through FishNet relay to others |

---

## 4. Existing Scripts — Required Changes

Only 4 existing files need modification. Everything else is new scripts.

### 4.1 PhysicsCharacterController.cs

**Problem:** `HandleMovement()` calls `Input.GetAxisRaw()` directly. The host cannot feed a remote client's input into this method.

**Fix:** Extract input reading from `HandleMovement()`. The method must accept an `InputPayload` parameter instead of reading from `Input`.

```
Before:
  void HandleMovement()
  {
      float h = Input.GetAxisRaw("Horizontal");
      float v = Input.GetAxisRaw("Vertical");
      ...
  }

After:
  public void HandleMovement(InputPayload input)
  {
      float h = input.move.x;
      float v = input.move.y;
      ...
  }
```

The `FixedUpdate()` path for the local player still works — the local `NetworkedPlayer` reads real input into an `InputPayload` and calls `HandleMovement(payload)` each tick. For remote players, the host feeds received `InputPayload` into their `HandleMovement()`.

Remove the direct `Input.GetAxisRaw` calls from `HandleMovement`. Do not change any physics logic — only the input source.

### 4.2 NPCBrain.cs

**Problem:** `Update()` runs full AI on every client, causing NPCs to behave differently and desync.

**Fix:** Add server-only guard. NPCBrain must become a `NetworkBehaviour`. Add this as the first line of `Update()`:

```csharp
if (!IsServer) return;
```

`FindNearestPlayer()` on clients will return null (no Player-tagged objects on clients in multiplayer mode unless you place them), so the guard is critical.

### 4.3 ObjectGrabController.cs

**Problem:** Grabs and throws directly manipulate Rigidbody state (`isKinematic`, `linearVelocity`) on the client. This conflicts with host authority and breaks during host migration.

**Fix:** Replace the `TryPickup()` and `Throw()` method bodies with RPC calls to the host. The client sends a request; the host validates and executes; the host broadcasts the result to all clients.

The `LineRenderer` arc preview (`UpdateArc()`) remains local-only — it is purely visual and does not need networking.

### 4.4 HitReactor.cs

**Problem:** `TakeHit()` calls `_ragdoll.ReportImpact()` and `rb.AddForce()` on all clients, causing every client to independently trigger ragdoll.

**Fix:** Wrap the body of `TakeHit()` in a server-only check. `HitReactor` must become a `NetworkBehaviour`:

```csharp
public void TakeHit(float throwSpeed, Vector3 direction, Vector3 hitPoint = default)
{
    if (!IsServer) return;   // add this line
    ...existing body...
}
```

---

## 5. New Script Structure

All new networking code lives under:
```
Assets/Game/Multiplayer/
```

```
Assets/Game/Multiplayer/
├── Bootstrap/
│   ├── SteamBootstrap.cs
│   ├── NetworkBootstrapper.cs
│   └── BuildVersionManager.cs
├── Steam/
│   ├── SteamIdentityManager.cs
│   ├── SteamLobbyManager.cs
│   ├── SteamAuthManager.cs
│   └── SteamFriendsManager.cs
├── Players/
│   ├── NetworkedPlayer.cs
│   ├── PlayerInputReplicator.cs
│   ├── CharacterNetSync.cs
│   ├── RagdollNetSync.cs
│   └── PuppetNetSync.cs
├── NPC/
│   ├── NPCServerAuthority.cs
│   └── NPCStateSync.cs
├── World/
│   ├── NetworkedPickup.cs
│   ├── NetworkedGrapple.cs
│   └── NetworkedDestructible.cs
├── Voice/
│   ├── SteamVoiceCapture.cs
│   ├── NetworkVoiceSender.cs
│   ├── NetworkVoiceReceiver.cs
│   ├── VoicePlaybackSource.cs
│   └── VoiceTalkingIndicator.cs
└── Migration/
    ├── HostMigrationManager.cs
    └── MigrationCheckpoint.cs
```

---

## 6. Steam Initialization Flow

```
Game starts
→ SteamBootstrap.Awake() calls SteamClient.Init(480)
→ If Steam not running: show error dialog, disable multiplayer, quit or go offline
→ SteamIdentityManager reads LocalSteamId, DisplayName
→ FishNet NetworkManager initializes (does NOT connect yet)
→ Main menu opens
```

`SteamBootstrap` and `NetworkManager` live on a single `DontDestroyOnLoad` GameObject called `GameBootstrap` in the bootstrap scene (the first scene that loads).

---

## 7. Steam Lobby Flow

### Host creates lobby:
```
Host presses "Create Lobby"
→ SteamLobbyManager.CreateLobby()
→ Steam creates lobby (max 4, Friends-only)
→ Lobby metadata set: hostSteamId, buildVersion, protocolVersion, sessionState=WaitingForPlayers
→ NetworkBootstrapper.StartHost() → NetworkManager.StartServer() + StartClient()
→ Lobby marked joinable=true
```

### Client joins lobby:
```
Friend accepts Steam invite (or joins via friend list)
→ SteamLobbyManager.OnLobbyJoined callback fires
→ Validate lobby metadata (buildVersion, protocolVersion match)
→ If mismatch: show error "Version mismatch — ask host to update"
→ Read hostSteamId from lobby metadata
→ FishySteamworks.SetClientAddress(hostSteamId)
→ NetworkManager.StartClient()
→ SteamAuthManager: request fresh auth ticket, send to host via reliable RPC
→ Host validates ticket via SteamUser.BeginAuthSession()
→ Host accepts connection → FishNet spawns NetworkedPlayer prefab
```

### Lobby metadata keys (set on Steam lobby, not FishNet):
```
hostSteamId
buildVersion
protocolVersion
sessionState        (WaitingForPlayers / InGame / MigratingHost)
currentPlayers
maxPlayers          (4)
joinable            (true/false)
voiceEnabled        (true)
```

---

## 8. Player Identity Sync

Each `NetworkedPlayer` carries a `PlayerIdentityData` synced via `SyncVar`:
```
SteamId64         (ulong)
DisplayName       (string)
NetworkPlayerId   (int)
IsHost            (bool)
IsTalking         (bool)   ← toggled by push-to-talk
```

Avatars (Steam profile pictures) are loaded locally per-client using `SteamFriends.GetMediumFriendAvatar()` — never send avatar bytes over FishNet.

---

## 9. Input Model

### InputPayload struct (shared between client and host):
```csharp
public struct InputPayload : IReplicateData
{
    public uint   tick;
    public Vector2 move;           // WASD normalized
    public float  aimYaw;          // camera yaw for rotation
    public bool   isAiming;        // RMB held
    public bool   grabPressed;     // F key
    public bool   throwPressed;    // LMB while aiming
    public bool   grapplePressed;  // grapple fire key
    public bool   grappleRelease;  // grapple release key
}
```

### Delivery rules:
- Movement, aimYaw, isAiming → unreliable (loss acceptable, next tick overwrites)
- grabPressed, throwPressed, grapplePressed, grappleRelease → sent as reliable ServerRpc (must not be dropped)

### Prediction and reconciliation:
- Client runs `HandleMovement(localInput)` immediately (prediction)
- Host runs `HandleMovement(receivedInput)` authoritatively
- Host sends `PlayerStateSnapshot` back each tick
- Client compares predicted position vs authoritative snapshot
- If delta > 0.1m: reconcile (re-simulate buffered inputs from divergence tick)
- If delta > 1.0m: hard snap

---

## 10. Player State Sync

### Balanced mode snapshot (sent every tick, unreliable):
```csharp
public struct PlayerStateSnapshot : IReconcileData
{
    public uint   tick;
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 linearVelocity;
    public Vector3 angularVelocity;
    public CharacterPhysicsState physicsState;
    public PuppetPhysicsState    puppetState;
    public bool   isGrounded;
}
```

### Ragdoll snapshot (sent at 10 Hz, unreliable):
- For `PhysicsCharacterController.ragdollBodies[]`: send position + rotation per bone
- For `PuppetRagdollController.muscleBodies[]`: send position + rotation per muscle Rigidbody

### State transition events (sent once, reliable):
- `CharacterPhysicsState` changes: Balanced ↔ Ragdoll ↔ GettingUp
- `PuppetPhysicsState` changes: Balanced ↔ Ragdoll ↔ GettingUp

On receiving a state transition event, clients:
1. Set the correct `State` enum on the controller
2. Enable/disable kinematic on ragdoll bones
3. Enable/disable `Animator`

---

## 11. NPC Sync

`NPCBrain.Update()` runs only on host (server-only guard).

### NPCStateSync sends (per NPC, every 15 Hz, unreliable):
```csharp
public struct NPCStateSnapshot
{
    public Vector3  position;
    public Quaternion rotation;
    public Vector3  velocity;
    public NPCState state;      // Patrol / Chase / Throw / HitReact
    public int      animState;  // Animator hash
}
```

### State transition events (reliable):
- NPCState changes (e.g., Patrol → Chase, Chase → HitReact)
- Death / despawn events

### On clients:
- `NPCBrain.Update()` returns immediately (IsServer guard)
- `NPCStateSync` drives the `CharacterController` position and `Animator` state from received snapshots
- `NPCHitReaction`, `NPCHitVFX` fire locally on clients only when receiving a reliable hit-event RPC from host

---

## 12. Pickup / Grab / Throw Sync

### NetworkedPickup component:
- Added to every `Pickupable` prefab alongside `NetworkObject` + `NetworkTransform`
- `NetworkTransform` handles position/rotation sync while object is in the world

### Grab flow:
```
Client presses F near object
→ PlayerInputReplicator sends CmdRequestGrab(networkObjectId) to host (reliable)
→ Host validates: distance ≤ pickupRange, object is Pickupable, object not already held
→ Host sets object kinematic, attaches to host-side hold point
→ Host sends RpcGrabConfirmed(networkObjectId, holderConnectionId) to all clients
→ All clients: disable NetworkTransform updates, attach object visually to holder's hold point
```

### Throw flow:
```
Client presses LMB while aiming
→ PlayerInputReplicator sends CmdRequestThrow(throwVelocity) to host (reliable)
→ Host validates and clamps velocity magnitude
→ Host detaches object, applies ThrowMath.TryCalculateVelocity result as linearVelocity
→ Host re-enables NetworkTransform
→ NetworkTransform propagates object flight to all clients
```

### Conflict handling:
- Two players grab same object: host grants to first valid request, rejects second with RpcGrabDenied
- Disconnect while holding: host releases object on OnClientDisconnect
- Ragdoll while holding: server-side PhysicsCharacterController state change triggers auto-drop RPC

---

## 13. Grappling Hook Sync

`GrappleController` fires a raycast and creates a `GrappleAnchor` at the hit point.

### NetworkedGrapple component (added to player prefab):
- A `NetworkObject` + `NetworkTransform` child called `GrappleAnchorNet` tracks the anchor world position
- When the owner fires the grapple: activate `GrappleAnchorNet`, set its position to anchor hit point
- `NetworkTransform` propagates anchor position to all clients at 20 Hz
- A `LineRenderer` on each client draws the rope from the player's hand bone to the received anchor position
- When the owner releases: deactivate `GrappleAnchorNet` → reliable RPC → all clients hide rope

Rope physics simulation (spring joint) runs only on the owner. Other clients see the rope as a static line from hand to anchor — cosmetic only.

---

## 14. RayFire Destructible Sync

`DestructibleObject` wraps `RayfireRigid` (Mesh Root type). Currently triggered by proximity prompt (T key) calling `RayfireRigid.Demolish()`.

### NetworkedDestructible component:
- Added alongside `RayfireRigid` on every destructible prefab
- Also needs `NetworkObject`
- The `NetworkObject` does NOT need `NetworkTransform` (destructibles are static until demolished)

### Demolition flow:
```
Any client presses T near destructible
→ NetworkedDestructible sends CmdRequestDemolish() to host (reliable)
→ Host validates: player within range, object not already demolished
→ Host calls RayfireRigid.Demolish() locally
→ Host sends RpcDemolishConfirmed() to all clients (reliable ObserversRpc)
→ All clients call RayfireRigid.Demolish() on their local copy
→ Collectible item drop (CollectibleShard) spawned by host via NetworkObject.Spawn()
```

**Key rule:** Only the host calls `Demolish()` first. Clients call it only after receiving `RpcDemolishConfirmed`. This guarantees all clients demolish the same object in sync.

Fragment physics after demolition run locally on each client (cosmetic). Only the resulting pickup items are network-spawned.

---

## 15. Steam Voice Chat

### SteamVoiceCapture (on GameBootstrap, persistent):
- Push-to-talk key: `V` (configurable in settings)
- On key down: `SteamUser.StartVoiceRecording()`
- Each frame while held: `SteamUser.GetAvailableVoice()` → `SteamUser.GetVoice()` → send `VoicePacket` via `NetworkVoiceSender`
- On key up: `SteamUser.StopVoiceRecording()`

### VoicePacket struct:
```csharp
public struct VoicePacket
{
    public int    senderConnectionId;
    public ushort sequence;
    public uint   captureTimestamp;
    public byte[] compressedVoiceData;
}
```

### Network delivery:
- Sent as `ObserversRpc` on the sender's `NetworkedPlayer` (unreliable, sequenced)
- Dedicated FishNet channel (Channel 1) separate from gameplay traffic
- Drop packets with sequence older than current jitter window (discard out-of-order)
- Payload size limit: 4 KB per packet
- Rate limit: max 50 packets/sec per sender

### Playback:
- Each `NetworkedPlayer` prefab has a `VoicePlaybackSource` component with a `AudioSource` (3D spatial, minDistance=1, maxDistance=20, logarithmic rolloff)
- On receive: `SteamUser.DecompressVoice()` → raw PCM → write into ring buffer AudioClip → `AudioSource.Play()`
- Ring buffer size: 200ms of audio
- Drop received packets that are too old (> 300ms behind ring buffer write head)

### Talking indicator:
- `VoiceTalkingIndicator` component on player prefab
- World-space Canvas parented to head bone, shows mic icon
- Toggled by a reliable `ObserversRpc` on key press/release (not voice data — a separate 2-byte event)
- `PlayerIdentityData.IsTalking` SyncVar also updated for lobby UI

### Per-player mute:
- Stored locally (not synced) — muted players still send voice, receiving client just doesn't play it

---

## 16. Host Migration

### The problem
If the host disconnects, all game state is lost. A new host must be elected and game state restored from a checkpoint.

### MigrationCheckpoint (serialized and cached):
```
SessionId
MigrationEpoch       (increments each migration)
ServerTick
SceneId
MissionState
PlayerStates[]       (position, health, inventory per player)
NPCStates[]          (position, NPCState per NPC)
PickupStates[]       (position, held-by info per pickup)
DestructibleStates[] (demolished or not per destructible)
GrappleStates[]      (active or not per player)
```

### Checkpoint policy:
- Host sends checkpoint to all connected clients every 1 second via reliable fragmented RPC
- Each client caches the last 3 checkpoints
- Checkpoints are compressed before sending

### Migration flow:
```
Host disconnects
→ FishNet fires OnClientDisconnect on all remaining clients
→ HostMigrationManager detects host SteamId gone
→ Steam lobby ownership automatically transfers to oldest remaining member
→ New Steam lobby owner becomes the new host
→ New host: NetworkManager.StopClient() → NetworkManager.StartServer() + StartClient()
→ New host increments MigrationEpoch, updates hostSteamId in lobby metadata
→ Remaining clients: read new hostSteamId from lobby, reconnect via FishySteamworks
→ New host restores world state from latest cached MigrationCheckpoint
→ New host sends full authoritative snapshot to all reconnected clients
→ Gameplay resumes
```

### Host election order:
1. New Steam lobby owner (automatic Steam mechanic)
2. Client with newest MigrationEpoch checkpoint
3. Deterministic join-order fallback

### Failure fallback:
If migration fails (< 2 clients remain, checkpoint corrupted): return all players to main menu with a clear error message.

---

## 17. Reconnection

If a client temporarily disconnects (network hiccup):
- Host retains the player slot for 60 seconds
- Reconnecting client sends: SteamId64, SessionId, MigrationEpoch, fresh auth ticket, previous player slot
- Host validates all fields and restores: position, health, inventory, held object state
- If slot expired: treated as a new join

---

## 18. Scene Architecture

### Bootstrap scene (loads first, persistent):
```
GameBootstrap (DontDestroyOnLoad)
├── SteamBootstrap
├── SteamIdentityManager
├── SteamLobbyManager
├── SteamAuthManager
├── SteamFriendsManager
├── NetworkManager          ← FishNet
├── FishySteamworks         ← transport component on NetworkManager
├── NetworkBootstrapper
├── BuildVersionManager
├── SteamVoiceCapture
├── NetworkVoiceSender
├── NetworkVoiceReceiver
└── HostMigrationManager
```

### Scene flow:
```
Bootstrap → Main Menu → Lobby → Loading → Gameplay → Results → Lobby
```

Only the host initiates scene transitions via FishNet scene management.

---

## 19. Network Message Classification

See section 26 for the full updated table including chat messages.

---

## 20. Bandwidth Budget

| Source | Approx size/tick | Count | Total |
|---|---|---|---|
| Player root RB snapshot | ~48 bytes | 4 | 192 bytes |
| Player ragdoll bones @ 10 Hz | ~720 bytes | 4 | ~960 bytes amortized |
| Puppet muscle bones @ 10 Hz | ~720 bytes | 4 | ~960 bytes amortized |
| NPC root snapshot @ 15 Hz | ~32 bytes | 8 NPCs | ~128 bytes |
| NPC ragdoll when hit @ 10 Hz | ~720 bytes | up to 2 | ~480 bytes amortized |
| Inputs (clients → host) | ~28 bytes | 3 clients | 84 bytes |
| Voice (variable) | ~1–4 KB/packet | 4 speakers | ~16 KB spike |
| **Peak total (with voice)** | | | **~19 KB/tick → ~570 KB/sec** |

Within Steam relay limits (~1 MB/sec per connection). Voice spikes are short and on a separate channel.

---

## 21. Security

Co-op only — no full anti-cheat needed. Required protections:
- Steam auth ticket validation on every connection
- Lobby-member check before FishNet auth succeeds
- Build version + protocol version mismatch rejection
- Host validates all grab/throw/demolish requests (distance, state, cooldown)
- Voice packet size limit (4 KB) and rate limit (50/sec)
- RPC rate limiting on sensitive RPCs (grab, throw, demolish)

---

## 22. Public Matchmaking (Steam Lobby Browser)

Public matchmaking uses Steam's built-in lobby browser. No backend required.

### Lobby types (host selects before creating):
- **Friends-only** — only Steam friends see the lobby (existing design)
- **Invite-only** — hidden, join by invite only (existing design)
- **Public** — visible in Steam lobby browser to anyone playing the game

### Lobby browser flow:
```
Client opens "Find Game" panel
→ SteamMatchmaking.RequestLobbyList() with filters
→ Filters: gameMode, maxPlayers=4, joinable=true, buildVersion matches
→ Results returned as list of SteamLobby objects
→ UI shows each lobby: host name, player count (2/4), map name
→ Client clicks Join → follows standard client join flow (section 7)
```

### New lobby metadata keys added for browser:
```
gameMode        (string: "coop")
mapName         (string)
isPublic        (bool)
```

### New scripts:
- `LobbBrowserPanel.cs` — UI panel showing list of public lobbies, refresh button, join button
- `LobbyListEntry.cs` — single row in the lobby browser list

### Filter rules:
- Only show lobbies with `buildVersion` matching the local client
- Only show lobbies where `joinable=true` and `currentPlayers < 4`
- Sort by player count descending (fuller lobbies first)

---

## 23. Server-Side Validation (Anti-Cheat)

All game-changing actions are validated on the host before executing. Clients cannot directly set authoritative state.

### Validated actions and their checks:

| Action | Host checks |
|---|---|
| Grab request | Distance ≤ pickupRange, object exists and not held, player state = Balanced |
| Throw request | Player is holding the object, velocity magnitude ≤ maxThrowSpeed (clamp if over) |
| Demolish request | Distance ≤ interactRange, object not already demolished |
| Grapple fire | Player state = Balanced, grapple not already active |
| Hit (TakeHit) | Server-only, ignored from clients |
| State transitions | Only host drives CharacterPhysicsState and PuppetPhysicsState changes |
| NPC targeting | Only host runs FindNearestPlayer(), clients receive result |

### Rate limiting:
- Grab requests: max 2 per second per client
- Throw requests: max 2 per second per client
- Demolish requests: max 1 per second per client
- Any RPC exceeding rate limit: log warning, ignore silently (do not disconnect)

### Duplicate protection:
- Host tracks last-processed `CommandSequence` per client per action type
- Duplicate sequence number → silently ignored

### Note on listen-server trust:
The host player can still manipulate their own game state. This is accepted for co-op. This is not competitive anti-cheat — it protects against accidental bugs and network exploits, not a malicious host.

---

## 24. In-Session Text Chat

Text chat between the 4 players in the current game session. Sent through FishNet. No backend required.

### ChatMessage struct:
```csharp
public struct ChatMessage
{
    public ulong  senderSteamId;
    public string senderDisplayName;
    public string text;
    public long   timestamp;          // Unix ms
    public ChatChannel channel;       // Session or Global
}

public enum ChatChannel { Session, Global }
```

### Flow:
```
Local player types in chat input field, presses Enter
→ SessionChatManager.SendMessage(text)
→ Validates: text not empty, length ≤ 256 chars, rate ≤ 2 messages/5 seconds
→ Sends ServerRpc CmdSendChat(text) to host (reliable)
→ Host sanitizes text (strip HTML/rich text tags)
→ Host broadcasts ObserversRpc RpcReceiveChat(ChatMessage) to all clients
→ All clients: add message to ChatPanel UI
```

### ChatPanel UI:
- Scrollable message log (last 100 messages kept in memory)
- Input field at bottom, toggle with Enter or dedicated key (e.g., T)
- Shows sender Steam display name + message text
- Chat window fades out after 8 seconds of inactivity, reappears on new message or input focus
- Cannot type while chat input is focused AND block game input simultaneously (unfocus game input when chat is open)

### New scripts:
- `SessionChatManager.cs` — NetworkBehaviour, handles send/receive RPCs for session chat
- `ChatPanel.cs` — UI component, displays messages, manages input field
- `ChatMessage.cs` — shared struct (used by both session and global chat)

---

## 25. Global Text Chat (Cloudflare Backend)

Cross-session global chat visible to all online players regardless of which session they are in. Uses Cloudflare Workers + Durable Objects with WebSockets.

### Architecture:
```
Unity Client (WebSocket)
  → Cloudflare Worker (auth + routing)
       → Durable Object: GlobalChatRoom
            → Broadcasts to all connected WebSocket clients
```

### Cloudflare backend components:
- **Worker** (`chat-worker`): handles WebSocket upgrade, validates Steam auth token, routes to Durable Object
- **Durable Object** (`GlobalChatRoom`): maintains list of connected WebSocket sessions, broadcasts messages, stores last 50 messages for late joiners

### Auth flow:
```
Client connects to global chat
→ Unity: SteamUser.GetAuthSessionTicket() → get ticket bytes
→ Unity: WebSocket connect to wss://chat.yourdomain.workers.dev
→ Send JSON: { "type": "auth", "steamId": "...", "ticket": "base64..." }
→ Worker: validate ticket with Steam Web API (/ISteamUserAuth/AuthenticateUserTicket)
→ If valid: Worker upgrades to Durable Object WebSocket session
→ Durable Object sends last 50 messages to new joiner
```

### Message format (JSON over WebSocket):
```json
{
  "type": "chat",
  "senderSteamId": "76561198000000000",
  "senderName": "PlayerName",
  "text": "Hello world",
  "timestamp": 1753574400000,
  "channel": "global"
}
```

### Unity-side global chat:
- `GlobalChatClient.cs` — manages WebSocket lifecycle (connect on game start, reconnect on drop, disconnect on quit)
- Uses `System.Net.WebSockets.ClientWebSocket` (built into .NET, no extra package needed)
- Runs receive loop on background thread, marshals messages to main thread via `ConcurrentQueue`
- On receive: add to `ChatPanel` UI with `[Global]` prefix

### Rate limiting (enforced in Durable Object):
- Max 2 messages per 5 seconds per Steam ID
- Max message length: 256 characters
- Profanity/spam filtering: optional, can add later

### New scripts:
- `GlobalChatClient.cs` — WebSocket client, connects to Cloudflare, sends/receives global messages
- `GlobalChatMessage.cs` — JSON serialization model

### Cloudflare files (separate repository or `cloudflare/` folder in project root):
- `chat-worker/index.ts` — Worker entry point, WebSocket upgrade, Steam auth validation
- `chat-worker/GlobalChatRoom.ts` — Durable Object, connection management, broadcast, message history

### ChatPanel shared UI:
The same `ChatPanel.cs` handles both session and global messages. A tab switcher (Session / Global) at the top of the panel switches which channel messages are shown from and which channel new messages are sent to.

---

## 26. Network Message Classification (Updated)

| Message | Direction | Delivery |
|---|---|---|
| InputPayload (move/aim) | Client → Host | Unreliable |
| InputPayload (grab/throw/grapple edge) | Client → Host | Reliable |
| PlayerStateSnapshot | Host → Clients | Unreliable sequenced |
| PlayerStateTransition | Host → Clients | Reliable |
| RagdollSnapshot (PhysicsCharacterController) | Host → Clients | Unreliable sequenced |
| RagdollSnapshot (PuppetRagdollController) | Host → Clients | Unreliable sequenced |
| NPCStateSnapshot | Host → Clients | Unreliable sequenced |
| NPCStateTransition | Host → Clients | Reliable |
| CmdRequestGrab | Client → Host | Reliable |
| RpcGrabConfirmed/Denied | Host → Clients | Reliable |
| CmdRequestThrow | Client → Host | Reliable |
| CmdRequestDemolish | Client → Host | Reliable |
| RpcDemolishConfirmed | Host → All clients | Reliable |
| VoicePacket | Client → Other clients | Unreliable sequenced (Channel 1) |
| VoiceTalkingIndicator toggle | Client → All clients | Reliable |
| Auth ticket | Client → Host | Reliable |
| MigrationCheckpoint | Host → All clients | Reliable fragmented |
| Migration control event | Host/Clients | Reliable |
| CmdSendChat (session) | Client → Host | Reliable |
| RpcReceiveChat (session) | Host → All clients | Reliable |
| GlobalChat message | Client ↔ Cloudflare WebSocket | WebSocket (JSON) |

---

## 27. Updated New Script Structure

```
Assets/Game/Multiplayer/
├── Bootstrap/
│   ├── SteamBootstrap.cs
│   ├── NetworkBootstrapper.cs
│   └── BuildVersionManager.cs
├── Steam/
│   ├── SteamIdentityManager.cs
│   ├── SteamLobbyManager.cs
│   ├── SteamAuthManager.cs
│   └── SteamFriendsManager.cs
├── Players/
│   ├── NetworkedPlayer.cs
│   ├── PlayerInputReplicator.cs
│   ├── CharacterNetSync.cs
│   ├── RagdollNetSync.cs
│   └── PuppetNetSync.cs
├── NPC/
│   ├── NPCServerAuthority.cs
│   └── NPCStateSync.cs
├── World/
│   ├── NetworkedPickup.cs
│   ├── NetworkedGrapple.cs
│   └── NetworkedDestructible.cs
├── Voice/
│   ├── SteamVoiceCapture.cs
│   ├── NetworkVoiceSender.cs
│   ├── NetworkVoiceReceiver.cs
│   ├── VoicePlaybackSource.cs
│   └── VoiceTalkingIndicator.cs
├── Chat/
│   ├── SessionChatManager.cs
│   ├── GlobalChatClient.cs
│   ├── ChatPanel.cs
│   ├── ChatMessage.cs
│   └── GlobalChatMessage.cs
├── Matchmaking/
│   ├── LobbyBrowserPanel.cs
│   └── LobbyListEntry.cs
└── Migration/
    ├── HostMigrationManager.cs
    └── MigrationCheckpoint.cs

cloudflare/
├── chat-worker/
│   ├── index.ts
│   └── GlobalChatRoom.ts
└── wrangler.toml
```

---

## 28. Updated Phase Order

| Phase | What gets built | Exit criteria |
|---|---|---|
| 0 | Packages + Steam startup + two clients connecting | Two Steam accounts connect, exchange test messages |
| 1 | Steam identity, lobby (friends-only + invite-only + public), lobby browser | 4 players in one lobby, names visible, public lobby appears in browser |
| 2 | FishNet session, auth, player spawning, server-side validation hooks | 4 players in gameplay scene, invalid RPCs rejected |
| 3 | Input model + player movement sync + prediction | Movement responsive at 150ms simulated latency |
| 4 | Both ragdoll systems synced | Knockdown/get-up correct on all clients |
| 5 | NPC sync (Brain + all sub-components) | 8 NPCs active, AI only on host |
| 6 | Pickup/grab/throw sync + grapple sync + RayFire sync | No double-ownership, demolition seen by all |
| 7 | Steam voice (push-to-talk, 3D, mute) | 4 players speak simultaneously, no gameplay impact |
| 8 | In-session text chat | All 4 players send/receive session messages |
| 9 | Global chat (Cloudflare backend) | Players in different sessions see same global messages |
| 10 | Host migration + reconnect + hardening | Forced host disconnect recovers in < 10s |

---

## 29. Out of Scope (This Phase)

- Dedicated servers
- Skill-based matchmaking (ELO/rating)
- VAC / EAC anti-cheat
- Voice activity detection
- Cross-platform
- More than 4 players
- PvP
- Friends chat (Steam overlay handles this)
- Steam Workshop
- Steam Inventory
