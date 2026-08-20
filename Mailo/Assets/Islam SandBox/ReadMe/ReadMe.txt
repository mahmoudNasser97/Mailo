================================================================================
 MAILO — STEAM MULTIPLAYER INTEGRATION
 Test & User Manual
================================================================================

This file is a living document. Every time a new networking step is built,
this file is updated with what was added and exactly how to test it in the
Unity Editor. Steps are listed in the order they were implemented. Do not
delete previous sections when adding a new one — this is meant to stay a
full test log for the whole multiplayer feature as it grows.

Location of all new networking code: Assets/Scripts/Networking/
Namespace: Mailo.Networking.Steam
Dev-only test scripts live under: Assets/Scripts/Networking/Steam/Debug/

--------------------------------------------------------------------------------
 GENERAL PREREQUISITES (apply to every step below)
--------------------------------------------------------------------------------

1. The Steam desktop client must be running and logged in for any Steam
   feature to work. Without it, SteamManager.Initialized will be false and
   every feature below falls back gracefully (see "Testing without Steam
   running" at the end of this file) instead of crashing.
2. steam_appid.txt (project root, value "480") must stay present when
   testing from the Unity Editor or a non-Steam-launched build.
3. Let Unity fully finish importing/compiling after pulling new files
   before entering Play mode.

================================================================================
 STEP 1 — STEAM IDENTITY (Display Name + Avatar)
================================================================================

WHAT IT DOES
------------
Retrieves the local Steam user's display name and profile picture (avatar),
and exposes the same lookup for any other Steam user (CSteamID) so it can
be reused later for lobby member lists, etc.

FILES
-----
- Assets/Scripts/Networking/Steam/SteamIdentityManager.cs   (main API)
- Assets/Scripts/Networking/Steam/SteamAvatarConverter.cs   (image handle -> Texture2D)
- Assets/Scripts/Networking/Steam/SteamAvatarSize.cs        (Small/Medium/Large enum)
- Assets/Scripts/Networking/Steam/Debug/SteamIdentityDebugProbe.cs  (test harness)

HOW TO TEST
-----------
1. Create/open a scratch scene (e.g. inside Islam SandBox).
2. Create an empty GameObject, add the "Steam Identity Debug Probe" component.
3. (Optional, to see the avatar visually) Create a Canvas > Raw Image, and a
   Canvas > Text - TextMeshPro under it for the username. Drag the Raw Image
   into the probe's "Preview" field, and the TMP Text into "Name Label".
4. Make sure Steam is running and logged in.
5. Enter Play mode.

EXPECTED RESULT
----------------
In the Console, in this order:
  [SteamIdentityDebugProbe] SteamManager.Initialized = True
  [SteamIdentityDebugProbe] Local display name: <your Steam name>
  [SteamIdentityDebugProbe] Avatar ready: 64x64
If a Raw Image/TMP Text were wired up, your Steam avatar and name appear
on screen shortly after Play starts (avatar defaults to Medium/64x64 size).

KNOWN EDGE CASE
----------------
If Steam is NOT running (or the user is not logged in), SteamManager.Initialized
stays false. This is expected and already handled everywhere, not a bug:
  - GetLocalDisplayName() returns the fallback string "Player" instead of throwing.
  - RequestLocalAvatar/RequestAvatar return null instead of throwing.
  - A red "[Steamworks.NET] SteamAPI_Init() failed..." line will appear in the
    Console — this comes from Steamworks.NET's own stock SteamManager.cs (a
    file we deliberately never modify) and is expected/documented Valve SDK
    behavior, not a crash. In real gameplay this case will not happen because
    the game will always be launched through Steam, but it matters for
    developing/testing without Steam open.

================================================================================
 STEP 2 — STEAM LOBBY (Create / Join / Leave)
================================================================================

WHAT IT DOES
------------
Creates and joins a Friends-only Steam lobby (max 4 members), tracks the
live member list, sets/reads basic lobby metadata (host id, build version,
session state, player counts, game mode), and supports joining a friend's
lobby either by pasting in a Lobby ID or by accepting a Steam overlay
invite. No FishNet/game networking yet — this is the Steam-side lobby layer
only.

Out of scope for this step (not built yet): public lobby browser, joining
via a Steam invite while the game isn't already running (+connect_lobby),
live player-count updates after creation, lobby chat, voice.

FILES
-----
- Assets/Scripts/Networking/Steam/SteamLobbyManager.cs      (main API)
- Assets/Scripts/Networking/Steam/SteamLobbyMetadata.cs     (metadata read/write)
- Assets/Scripts/Networking/Steam/SteamLobbyMemberInfo.cs   (member struct)
- Assets/Scripts/Networking/Steam/SteamLobbyEnterKind.cs    (CreatedByMe/JoinedExisting enum)
- Assets/Scripts/Networking/Steam/Debug/SteamLobbyDebugProbe.cs  (test harness)
- Assets/Scripts/Networking/Steam/Debug/SteamLobbyMemberRow.cs   (one member row: avatar + name)

SCENE SETUP (once)
-------------------
1. Create a member row prefab (or just 4 pre-placed instances, since the
   lobby max is 4): a GameObject with a RawImage (avatar) + a
   Text - TextMeshPro (name) child, and add the "Steam Lobby Member Row"
   component to it, dragging the RawImage into "_avatarImage" and the
   TMP Text into "_nameLabel". Make 4 of these under a Vertical Layout
   Group (or just stacked manually) — name them e.g. "MemberRow1".."4".
2. Create a Canvas with:
   - A TMP_InputField named e.g. "LobbyIdInput" (for pasting a Lobby ID)
   - A TMP_Text named e.g. "StatusLabel" (shows current status/result)
   - A TMP_Text named e.g. "PlayerCountLabel" (shows "Players: X/4")
   - The 4 member rows from step 1
   - Four Buttons: "Create Lobby", "Join by ID", "Leave Lobby", "Invite Friend"
3. Create an empty GameObject (e.g. "LobbyTest"), add the
   "Steam Lobby Debug Probe" component.
4. In the Inspector, drag each UI element into its matching field:
   _lobbyIdInputField, _statusLabel, _playerCountLabel,
   _memberRows (drag all 4 row GameObjects into this array, size 4),
   _createLobbyButton, _joinByIdButton, _leaveLobbyButton, _inviteFriendButton

HOW TO TEST — SOLO (confirms create/leave works)
--------------------------------------------------
1. Make sure Steam is running and logged in. Enter Play mode.
2. Click "Create Lobby".
   Expected Console: LobbyCreated success=True ... / LobbyEntered success=True
   kind=CreatedByMe. StatusLabel shows "In lobby: <id> (CreatedByMe)".
   PlayerCountLabel shows "Players: 1/4". MemberRow1 shows your own name
   right away, and your avatar shortly after (avatar fetch is async).
3. Click "Leave Lobby".
   Expected Console: LobbyLeft. PlayerCountLabel shows "Players: 0/4" and
   all member rows go inactive/empty.

HOW TO TEST — TWO TESTERS, PATH 1: Manual Lobby ID (no Steam friendship needed)
---------------------------------------------------------------------------------
1. Tester A clicks "Create Lobby", copies the number shown in StatusLabel
   after "In lobby:" (that is the Lobby ID).
2. Tester A sends that number to Tester B (Discord, chat, anything).
3. Tester B pastes it into the LobbyIdInput field and clicks "Join by ID".
4. Expected: both testers' member rows and PlayerCountLabel ("Players: 2/4")
   update to show both names + avatars shortly after (this comes through
   Steam's LobbyChatUpdate_t event, may take a short moment — not instant;
   avatars can lag a moment behind names since they're fetched separately).

HOW TO TEST — TWO TESTERS, PATH 2: Steam Overlay Invite (must be Steam friends)
---------------------------------------------------------------------------------
1. Tester A clicks "Create Lobby", then clicks "Invite Friend".
2. The Steam overlay invite dialog opens; pick Tester B and send the invite.
3. Tester B accepts the invite from Steam (friends list / notification).
4. Tester B's game auto-joins (no button press needed on B's side) —
   Console on B shows "Invite accepted, joining lobby..." then
   LobbyEntered success=True kind=JoinedExisting.
5. Both testers' member rows and PlayerCountLabel update to show both
   names + avatars.

EXPECTED / NORMAL BEHAVIOR TO NOT MISTAKE FOR BUGS
----------------------------------------------------
- Right after joining, reading lobby metadata (e.g. hostSteamId) may briefly
  return an empty string before Steam finishes replicating it — this is
  expected Steam lobby data-replication lag, not a bug.
- Clicking "Invite Friend" while not currently in a lobby logs a warning and
  does nothing (by design — nothing to invite people into).
- Buttons now enable/disable themselves automatically based on state (see
  "BUTTON STATES" section below) — you should no longer be able to click
  Create while already in a lobby, or Join while a request is pending, etc.
- If Create/Join fails with "k_EResultNoConnection" (or Timeout/ServiceUnavailable),
  this usually means Steam hasn't finished establishing its connection to the
  backend matchmaking servers yet (common right after Steam/the game just
  launched). SteamLobbyManager now retries automatically up to 3 times, 2
  seconds apart, before reporting a final failure — you may see a
  "retrying (1/3)..." warning in the Console, which is expected, not a bug.
  If it still fails after all retries, check your network/firewall/VPN and
  that Steam is fully online (not in Offline Mode).

BUTTON STATES
-------------
Buttons are automatically enabled/disabled based on two things: whether a
request is currently in flight, and whether you're currently in a lobby.
  - Not in a lobby, idle: Create + Join by ID + the Lobby ID input field are
    enabled; Leave + Invite Friend are disabled (nothing to leave/invite to).
  - Any request in flight (Create, Join, or an incoming friend invite
    auto-joining you): ALL four buttons + the input field are disabled until
    the final result arrives (this also covers the automatic connection
    retries above — buttons stay locked through all retry attempts, not just
    the first try).
  - In a lobby, idle: Leave + Invite Friend are enabled; Create + Join by ID
    + the input field are disabled (you can't create/join while already in
    one — leave first).
If a button looks stuck disabled with no request in flight, that's a bug
worth reporting (not expected behavior).

================================================================================
 STEP 3 — RANDOM CHARACTER ASSIGNMENT + START BUTTON + GAME SCENE SPAWNER
================================================================================

WHAT IT DOES
------------
Each of the 4 lobby slots gets randomly assigned one of 4 fixed characters
(Bruno, Ranger, Zara, Pixel), shown next to that player's name+avatar in the
lobby. Assignment is STABLE - once a member has a character, they keep it
until they leave; only newly-joined members get assigned from whichever
characters are still unused. A "Start" button is enabled ONLY when you are
the lobby owner AND the lobby is full (4/4); clicking it loads a new "Game"
scene and spawns one placeholder capsule per lobby member.

IMPORTANT LIMITATION: Start's scene load is LOCAL ONLY for now. Clicking it
only changes YOUR OWN screen - the other 3 players stay in the lobby scene.
Real synced scene transition needs FishNet, not installed yet. This is
expected for this step, not a bug.
>>> RESOLVED in STEP 4 below - Start now connects everyone over the network
>>> and every client follows automatically. See Step 4 for details.

FILES
-----
- Assets/Scripts/Networking/Steam/SteamLobbyCharacterAssignment.cs
  (roster, recompute/publish/read - host-only writes, mirrors the existing
  owner-gated SteamLobbyMetadata pattern)
- Assets/Scripts/Networking/Game/GameSceneCharacterSpawner.cs (new folder,
  namespace Mailo.Networking.Game - reads the lobby roster and spawns
  capsules; not Steam-specific itself, just consumes lobby data)

SCENE SETUP (once)
-------------------
1. File > Build Settings: add Assets/Islam SandBox/Game.unity to Scenes In
   Build (checked/enabled), alongside the existing lobby scene. Required -
   SceneManager.LoadScene("Game") does nothing (silently logs an error) if
   the scene isn't listed here, even inside the Editor's Play mode.
2. In the lobby scene's Canvas, add a "Start" Button, wire it into the
   SteamLobbyDebugProbe's new "_startButton" field.
3. On each of the 4 SteamLobbyMemberRow instances, add a new TMP Text child
   for the character name, wire it into the new "_characterLabel" field.
4. In Game.unity, add an empty GameObject (e.g. "CharacterSpawner") with the
   new "Game Scene Character Spawner" component attached. Leave
   "_spawnPoints" unassigned to use automatic spread-out positions, or
   create 4 child Transforms and assign them for manual placement.

HOW TO TEST
-----------
1. Two testers join the same lobby (either path from Step 2).
2. Both members' rows should show a character name (e.g. A="Bruno",
   B="Ranger") - different characters, never duplicates.
3. Have Tester A leave and rejoin (or a 3rd tester join) - confirm existing
   members' characters do NOT change, and the new/rejoining member gets one
   of the remaining unused characters.
4. With 1-3 members, confirm Start is disabled for everyone, including the
   owner.
5. Get to exactly 4 members - Start becomes clickable ONLY for the lobby
   owner, stays disabled for the other 3.
6. Owner clicks Start - Game.unity loads on the owner's client only (the
   other 3 stay in the lobby scene, expected per the limitation above). In
   the Hierarchy, confirm 4 capsules named "Player_<name>_<character>"
   appear, spread out and distinct.

================================================================================
 STEP 4 — FISHNET + FISHYSTEAMWORKS: AUTOMATIC CONNECT + SCENE LOAD
================================================================================

WHAT IT DOES
------------
Installs FishNet (networking framework) and FishySteamworks (Steam transport
for it), and wires them into the existing lobby flow so clicking "Start"
(from Step 3) now actually connects every lobby member over the network and
sends each of them into the Game scene automatically - no manual Steam ID
typing, no extra buttons for real players. This directly resolves Step 3's
"local only" limitation - the client now follows the host into the Game
scene. Each client independently loads the Game scene the moment ITS OWN
connection succeeds (not a server-authoritative synced scene load via
FishNet's own networked SceneManager - that's a possible future refinement),
which in practice happens within a fraction of a second of each other.

PACKAGES INSTALLED
-------------------
- FishNet: Networking Evolved (v4.7.2), installed via Package Manager git
  URL: https://github.com/FirstGearGames/FishNet.git?path=Assets/FishNet
  (added to Packages/manifest.json - Unity resolves/downloads it via git,
  requires Git 2.14.0+ installed and on PATH).
- FishySteamworks, installed manually: latest .unitypackage from
  https://github.com/FirstGearGames/FishySteamworks/releases, imported via
  Assets > Import Package > Custom Package.

IMPORTANT: FishySteamworks' repo also ships a separate "SteamManager.unitypackage"
alongside the transport script, for projects that don't already have a Steam
bootstrap. We do NOT import that one - this project already has its own
Assets/Scripts/Steamworks.NET/SteamManager.cs, and every step above depends
on it. Importing a second one would create a duplicate SteamManager and
throw "Tried to Initialize the SteamAPI twice in one session!" (that file's
own guard). Only FishySteamworks.cs and its Core/ support files get imported.

FishySteamworks has NO App ID field - don't look for one. It uses whatever
Steam session is already active via steam_appid.txt + our own SteamManager.
The field that DOES matter is "Peer To Peer" on the FishySteamworks
component - it MUST be enabled (ticked), or connecting via Steam ID (instead
of a raw IP) will not work.

FILES
-----
- Packages/manifest.json (FishNet git dependency added)
- Assets/Scripts/Networking/Mailo.Networking.asmdef (added a reference to
  FishNet's own Runtime asmdef so our isolated assembly can see FishNet
  types - same pattern already used for Steamworks.NET and TextMeshPro)
- Assets/FishNet/Plugins/FishySteamworks/ (imported package, not ours -
  don't hand-edit this folder)
- Assets/Scripts/Networking/Fish/NetworkLobbyBridge.cs (NEW - the actual
  integration: listens for the Start signal, connects over FishNet
  automatically for host and client alike, loads the Game scene locally
  once each machine's own connection succeeds)
- Assets/Scripts/Networking/Steam/SteamLobbyMetadata.cs (added
  KeyNetworkStarted = "networkStarted", a new lobby-level metadata flag)
- Assets/Scripts/Networking/Steam/SteamLobbyManager.cs (added
  NotifyGameStarting() - owner-only write of the networkStarted flag,
  mirrors every other owner-gated metadata write already in this file)
- Assets/Scripts/Networking/Steam/Debug/SteamLobbyDebugProbe.cs (Start
  button's click handler now calls SteamLobbyManager.NotifyGameStarting()
  instead of loading the scene directly - the actual connect+load now
  happens in NetworkLobbyBridge, reacting to that flag)
- Assets/Scripts/Networking/Fish/Debug/NetworkConnectionDebugProbe.cs -
  DELETED. This was a throwaway raw-connectivity test harness (manual
  Host/Join buttons + manual Steam ID entry) used only to prove FishNet +
  FishySteamworks could connect two Steam clients before wiring the real
  integration above. No longer needed now that Start does this for real.

HOW IT WORKS (no manual Steam ID typing needed anymore)
----------------------------------------------------------
1. Host clicks "Start" -> NotifyGameStarting() writes networkStarted="true"
   to lobby metadata (owner-only write, same mechanism as every other
   lobby metadata field).
2. Every member's NetworkLobbyBridge (subscribed to LobbyDataUpdated) sees
   the flag flip to "true":
     - Host: starts the FishNet server (ServerManager.StartConnection()),
       then starts its own local client half
       (ClientManager.StartConnection() with no address - FishySteamworks
       detects the server is already running and routes this through its
       internal host-loopback socket automatically, not a real network hop).
     - Everyone else: reads hostSteamId (already known since Step 2, set
       at lobby creation) and connects directly
       (ClientManager.StartConnection(hostSteamId)).
3. The moment a member's OWN ClientManager reports
   LocalConnectionState.Started, THAT machine loads the Game scene
   (SceneManager.LoadScene("Game")) - independently, not waiting for
   anyone else. In practice all members arrive within a fraction of a
   second of each other since Steam's P2P handshake is fast.
4. Leaving the lobby (LobbyLeft) stops both the server and client
   connections and resets the trigger flags, so a fresh lobby session can
   go through this flow again cleanly.

SCENE SETUP (once)
-------------------
1. In the lobby scene, create an empty GameObject named "NetworkManager".
   Add Component > search "NetworkManager" (listed under FishNet > Manager)
   and add it. NOTE: right after importing, Add Component's search index
   can be stale and not find it - try browsing the menu tree manually
   (FishNet > Manager > NetworkManager) instead of searching, or fully
   restart the Unity Editor (not just refocus) if it's still missing.
2. On the same GameObject, Add Component > "FishySteamworks", and enable
   (tick) its "Peer To Peer" field. Leave everything else default.
   "Dont Destroy On Load" on the NetworkManager component itself should
   already be true by default - leave it, it's what keeps the connection
   alive across the Lobby -> Game scene transition.
3. Add Component > "Network Lobby Bridge" (same GameObject is fine), and
   drag the same "NetworkManager" GameObject into its one field,
   "Network Manager".
4. That's it - no new UI needed. The existing "Start" button (Step 3)
   drives all of this now.

HOW TO TEST
-----------
1. Two testers join the same lobby (any path from Step 2/3).
2. The lobby owner clicks "Start".
3. Expected in BOTH testers' Consoles shortly after: "[NetworkLobbyBridge]
   Server: Started" (host only) and "[NetworkLobbyBridge] Client: Started"
   (both).
4. Expected: BOTH testers' screens switch to the Game scene automatically,
   without the non-host tester clicking anything. In the Hierarchy, capsules
   should appear for each joined member, same as Step 3's test.

================================================================================
 TESTING WITHOUT STEAM RUNNING (applies to all steps above)
================================================================================
Every manager (SteamIdentityManager, SteamLobbyManager) checks
SteamManager.Initialized before touching any Steam API. If Steam isn't
running/logged in:
  - Identity calls fall back to "Player" / no avatar.
  - Lobby calls (Create/Join/etc.) report failure through their callback
    instead of throwing, and do nothing.
  - A one-time warning is logged per session, not spammed.
This will not happen for real players (the game will be launched through
Steam), but is expected and safe when a teammate tests without Steam open.

================================================================================
 STEP 5 — NETWORKED CHARACTER SPAWNING (real prefabs replace capsules)
================================================================================

WHAT IT DOES
------------
Replaces Step 4's placeholder capsules with the 4 real character prefabs
(Bruno, Ranger, Zara, Pixel) - fully networked over FishNet, so every
connected player sees everyone else's character move/animate live.

IMPORTANT CONSTRAINT: zero modification to teammate-owned files. The base
character prefabs and every script they use (CameraController,
GrappleController, ObjectGrabController, GatePullController,
SeesawParticipant, PlayerStuckRecovery, UserControlThirdPerson,
CharacterPuppet) are never edited. Networking is added entirely via new
Prefab Variants + one new "bridge" script that toggles existing components
on/off from the outside - never touches their source.

NAMING NOTE: the base prefab used to be named "Wolf.prefab" while the lobby
already displayed "Ranger" for that character - the teammate authorized
renaming the asset to match, so it's now Assets/Mahmoud SandBox/Characters
Prefabs/Ranger.prefab (renamed via git mv, GUID/references unaffected). No
more Wolf/Ranger mismatch anywhere.

FILES
-----
- Assets/Scripts/CharacterNetworking/NetworkedCharacterOwnership.cs (NEW,
  loose file, no .asmdef/namespace - deliberately compiles in the same tier
  as the Mahmoud SandBox/RootMotion scripts it references, so it can wire to
  them directly without moving or editing a single one of those files). On
  OnStartClient(), enables the full control/camera/interaction stack ONLY
  for the owning client; on every other client's copy it disables
  UserControlThirdPerson + CharacterPuppet + CharacterAnimationThirdPerson +
  the interaction scripts and makes the Rigidbody kinematic, handing full
  movement authority to NetworkTransform instead.
- Assets/Scripts/Networking/Game/ClientIdentityBroadcast.cs (NEW) +
  additions to NetworkLobbyBridge.cs - a small handshake so the server can
  tell which FishNet connection belongs to which Steam lobby member (the
  transport doesn't expose that natively). Registered as soon as the server
  starts (not when the Game scene loads) specifically to avoid a race where
  a remote client's identity could arrive before anything is listening;
  received identities are cached so a late subscriber (the spawner) can
  catch up instead of missing anything.
- Assets/Scripts/Networking/Game/GameSceneCharacterSpawner.cs (REWRITTEN) -
  capsule code fully removed. Server-only: for each identity handshake
  received, looks up that player's AssignedCharacter, spawns the matching
  networked prefab variant via FishNet's ServerManager.Spawn(), owned by
  that connection.
- Assets/Scripts/Networking/Game/NetworkedCharacters/{Bruno,Ranger,Zara,
  Pixel}_Networked.prefab (NEW) - Prefab Variants of the 4 base characters.
  Each has NetworkObject + NetworkedCharacterOwnership on the root,
  NetworkTransform on the "Character Controller" child, NetworkAnimator on
  the "Animation Controller" child.

SCENE SETUP (once)
-------------------
1. The 4 _Networked prefab variants must exist (see FILES above) with all
   4 new components added and NetworkedCharacterOwnership's 10 reference
   fields wired (Camera Controller, Character Camera Object, User Control,
   Character Puppet, Character Animation, Character Rigidbody, Grapple
   Controller, Object Grab Controller, Gate Pull Controller, Seesaw
   Participant - all pulled from the same prefab's own hierarchy; Character
   Animation is dragged from the "Animation Controller" child).
2. FishNet auto-registers any NetworkObject prefab into
   Assets/DefaultPrefabObjects.asset on save (via its Editor
   PrefabCollectionGenerator) - no manual registration step needed.
3. In Demo_Island.unity, add an empty GameObject ("CharacterSpawner") with
   Game Scene Character Spawner attached. Wire its Character Prefabs list
   (4 entries: Bruno/Ranger/Zara/Pixel -> matching _Networked prefab).
   Leave Spawn Points empty to use the auto-spread fallback, which is
   relative to wherever this GameObject itself is placed in the scene (so
   place it ON the island, not at the scene origin).
4. The static Bruno character instance that used to sit pre-placed in
   Demo_Island (from the teammate's original "Character Prefabs" commit)
   has been removed from the scene with the teammate's authorization -
   confirmed clean, no leftover ragdoll/component fragments.

KNOWN HARMLESS WARNING
------------------------
While adding NetworkObject to multiple prefabs in the same editing session,
you may see a Console error like: "Assets X and Y have the same assetPath
hash of 0. Please modify the prefab name of either to resolve." This is
FishNet's auto-generator catching prefabs mid-save, before it's finished
computing each one's unique hash (which briefly defaults to 0, not a real
name collision - the 4 character names are obviously distinct). It does
not reappear once all prefabs are fully saved. Confirmed via FishNet's own
source (DefaultPrefabObjects.cs) - safe to ignore.

HOW TO TEST
-----------
1. Full existing flow: lobby -> character assignment -> host clicks Start
   -> FishNet auto-connects -> Demo_Island loads for everyone (Step 4).
2. Each client should see their OWN assigned character, fully playable
   (movement, camera, grapple, object grab all working exactly as before -
   this is the single-player behavior, untouched).
3. Each client should also see EVERY OTHER player's character, moving
   smoothly as that player actually moves - no jitter, no fighting-physics
   symptoms (confirms the non-owner Rigidbody.isKinematic + disabled
   CharacterPuppet combination is working).
4. Only the local player's camera/AudioListener should be active per
   client.
5. A remote player's Animator should visibly reflect their real movement,
   not a frozen/default pose (NetworkAnimator working).

POST-TESTING FIXES (found during first real 2-player test, now resolved)
----------------------------------------------------------------------------
Real multiplayer testing with a teammate surfaced two bugs. Both are now
fixed; a third, related issue was found and fixed along the way.

BUG 1 - Local camera didn't follow the player
  Root cause: CameraController._camera was left unassigned on the base
  prefab, so it fell back to Camera.main in Awake() - unreliable the moment
  more than one camera exists in the scene at once (exactly the case once 4
  networked characters are spawned). This is a scene-wiring issue, not a
  code bug - CameraController.cs itself was never touched.
  Fix (manual, Inspector only, done per _Networked variant): drag that
  variant's own "Character Camera" GameObject's Camera component into
  CameraController's "Camera" field on the root.
  Also found + fixed along the way: "Character Camera" was overridden to
  Active=true on some variants (from earlier experimentation) - this would
  have left every spawned instance's camera/AudioListener active
  simultaneously, regardless of ownership. Fixed by reverting/unchecking
  Active on "Character Camera" so it stays at its correct prefab-default
  inactive state (NetworkedCharacterOwnership is the only thing that should
  ever activate it, and only for the owner). Verified via direct .prefab
  inspection: all 4 variants now correctly wired and inactive-by-default.

BUG 2 - Remote players' characters moved but never animated
  Root cause: CharacterAnimationThirdPerson runs its own independent
  Update() that reads CharacterPuppet.animState and writes Animator
  parameters every frame, completely regardless of any other script's
  enabled state. It was never being disabled on non-owner instances, so it
  kept re-applying frozen/stale local animState data every tick, fighting
  NetworkAnimator's correct incoming values from the network.
  Fix (code): added a Character Animation field to
  NetworkedCharacterOwnership.cs, disabled for non-owners exactly like
  UserControlThirdPerson/CharacterPuppet already were. Requires the new
  field to be wired on all 4 _Networked variants (see SCENE SETUP above).

OUT OF SCOPE FOR THIS STEP
-----------------------------
Ragdoll/knockdown sync (stays local-only per client), grapple/object-grab/
gate-pull/seesaw interaction EFFECTS sync (stay functional for the owner
locally, but aren't replicated to others yet), true movement
prediction/reconciliation (this is straightforward client-authoritative
NetworkTransform replication, not FishNet's fuller predict+reconcile
architecture - a deliberate scope choice driven by the zero-teammate-file-
modification constraint).

================================================================================
 CHANGELOG
================================================================================
- Step 1 added: Steam Identity (display name + avatar).
- Step 2 added: Steam Lobby (create/join/leave, member list, metadata).
- Step 2 fix: automatic retry (up to 3x, 2s apart) on transient CreateLobby/
  JoinLobby connection failures (k_EResultNoConnection/Timeout/ServiceUnavailable).
- Step 2 update: member list now shows each of the 4 lobby members' avatar
  + username (SteamLobbyMemberRow) plus a "Players: X/4" count, instead of
  a plain name-only text list.
- Step 2 fix: member rows now start hidden until someone actually joins.
- Step 2 update: Create/Join/Leave/Invite buttons now enable/disable
  automatically based on request-in-flight and in-lobby state (see
  "BUTTON STATES" section).
- Step 3 added: random stable character assignment (Bruno/Ranger/Zara/Pixel)
  shown per lobby member, plus a host-only "Start" button (enabled only at
  4/4 players) that loads the Game scene and spawns placeholder capsules.
- Step 4 added: FishNet + FishySteamworks installed. Start now automatically
  connects every lobby member over the network (host starts server, others
  auto-connect to the already-known hostSteamId) and each client loads the
  Game scene locally once its own connection succeeds - resolving Step 3's
  "local only" limitation. NetworkConnectionDebugProbe.cs (the raw
  connectivity test harness) removed, superseded by NetworkLobbyBridge.cs.
- Step 5 added: real character prefabs (Bruno/Ranger/Zara/Pixel) replace
  the placeholder capsules, fully networked (NetworkObject/NetworkTransform/
  NetworkAnimator on new Prefab Variants + a new NetworkedCharacterOwnership
  bridge script) - every player now sees every other player's character
  move and animate live, with zero modification to any teammate-owned
  script or the base prefabs. Wolf.prefab renamed to Ranger.prefab
  (teammate-authorized) to match the name already used everywhere else.
  GameSceneCharacterSpawner.cs rewritten from scratch (capsule code fully
  removed) to spawn server-side via a new Steam-identity <-> FishNet-
  connection handshake (ClientIdentityBroadcast).
- Step 5 fix: after the first real 2-player test, fixed remote players not
  animating (CharacterAnimationThirdPerson now disabled for non-owners too,
  via a new Character Animation field on NetworkedCharacterOwnership.cs)
  and the local camera not following the player (CameraController.Camera
  field wiring, plus reverting a stray Active=true override on "Character
  Camera" found on some variants during the same pass). See the
  "POST-TESTING FIXES" section under Step 5 above for full details.
