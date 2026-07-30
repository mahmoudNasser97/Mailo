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
