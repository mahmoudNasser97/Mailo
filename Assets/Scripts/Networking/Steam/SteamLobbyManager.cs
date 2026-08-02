using System;
using System.Collections;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace Mailo.Networking.Steam
{
    public sealed class SteamLobbyManager : MonoBehaviour
    {
        private static SteamLobbyManager s_instance;
        private static bool s_warnedNotInitialized;

        public static event Action<bool, CSteamID, EResult> LobbyCreated;
        public static event Action<bool, CSteamID, SteamLobbyEnterKind, EChatRoomEnterResponse> LobbyEntered;
        public static event Action<CSteamID> LobbyMembersChanged;
        public static event Action LobbyLeft;
        public static event Action<CSteamID, CSteamID> LobbyInviteAccepted;
        public static event Action<CSteamID> LobbyDataUpdated;

        public static SteamLobbyManager Instance
        {
            get
            {
                if (s_instance == null)
                {
                    var go = new GameObject(nameof(SteamLobbyManager));
                    s_instance = go.AddComponent<SteamLobbyManager>();
                }
                return s_instance;
            }
        }

        public static bool IsInLobby
        {
            get
            {
                if (!WarnIfNotInitialized())
                    return false;

                return Instance._currentLobbyId.m_SteamID != 0;
            }
        }

        private const int MaxConnectionRetries = 3;
        private const float ConnectionRetryDelaySeconds = 2f;

        private CSteamID _currentLobbyId = CSteamID.Nil;
        private bool _lastActionWasCreate;
        private int _pendingMaxMembers;
        private ELobbyType _pendingLobbyType;
        private CSteamID _pendingJoinLobbyId;
        private bool _explicitJoinInProgress;
        private int _createRetryCount;
        private int _joinRetryCount;
        private Action<bool, CSteamID, EResult> _pendingCreateCompletion;
        private Action<bool, CSteamID, EChatRoomEnterResponse> _pendingJoinCompletion;

        // Must be fields: Steamworks.NET Callback<T> instances stop firing silently if garbage collected.
        private Callback<LobbyCreated_t> _lobbyCreatedCallback;
        private Callback<LobbyEnter_t> _lobbyEnterCallback;
        private Callback<LobbyChatUpdate_t> _lobbyChatUpdateCallback;
        private Callback<GameLobbyJoinRequested_t> _gameLobbyJoinRequestedCallback;
        private Callback<LobbyDataUpdate_t> _lobbyDataUpdateCallback;

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            DontDestroyOnLoad(gameObject);

            // Registered eagerly (not lazily per-call like SteamIdentityManager) so a friend's
            // overlay invite (GameLobbyJoinRequested_t) is caught even before any lobby action
            // has been explicitly requested. Safe here because every public static entry point
            // guards on SteamManager.Initialized before ever touching Instance.
            _lobbyCreatedCallback = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            _lobbyEnterCallback = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            _lobbyChatUpdateCallback = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
            _gameLobbyJoinRequestedCallback = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
            _lobbyDataUpdateCallback = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);
        }

        public static CSteamID GetCurrentLobbyId()
        {
            if (!WarnIfNotInitialized())
                return CSteamID.Nil;

            return Instance._currentLobbyId;
        }

        public static bool IsLobbyOwner()
        {
            if (!WarnIfNotInitialized())
                return false;

            var manager = Instance;
            if (manager._currentLobbyId.m_SteamID == 0)
                return false;

            return SteamMatchmaking.GetLobbyOwner(manager._currentLobbyId) == SteamUser.GetSteamID();
        }

        public static List<SteamLobbyMemberInfo> GetLobbyMembers()
        {
            var members = new List<SteamLobbyMemberInfo>();

            if (!WarnIfNotInitialized())
                return members;

            var manager = Instance;
            if (manager._currentLobbyId.m_SteamID == 0)
                return members;

            int count = SteamMatchmaking.GetNumLobbyMembers(manager._currentLobbyId);
            for (int i = 0; i < count; i++)
            {
                CSteamID memberId = SteamMatchmaking.GetLobbyMemberByIndex(manager._currentLobbyId, i);
                string assignedCharacter = SteamLobbyCharacterAssignment.GetAssignedCharacter(manager._currentLobbyId, memberId);
                members.Add(new SteamLobbyMemberInfo(memberId, SteamIdentityManager.GetDisplayName(memberId), assignedCharacter));
            }

            return members;
        }

        public static string GetLobbyMetadata(string key)
        {
            if (!WarnIfNotInitialized())
                return string.Empty;

            var manager = Instance;
            if (manager._currentLobbyId.m_SteamID == 0)
                return string.Empty;

            return SteamLobbyMetadata.Read(manager._currentLobbyId, key);
        }

        public static void CreateLobby(Action<bool, CSteamID, EResult> onComplete = null, int maxMembers = 4, ELobbyType lobbyType = ELobbyType.k_ELobbyTypeFriendsOnly)
        {
            if (!WarnIfNotInitialized())
            {
                onComplete?.Invoke(false, CSteamID.Nil, EResult.k_EResultFail);
                return;
            }

            var manager = Instance;
            manager._lastActionWasCreate = true;
            manager._pendingMaxMembers = maxMembers;
            manager._pendingLobbyType = lobbyType;
            manager._pendingCreateCompletion = onComplete;
            manager._createRetryCount = 0;

            SteamMatchmaking.CreateLobby(lobbyType, maxMembers);
        }

        public static void JoinLobby(CSteamID lobbyId, Action<bool, CSteamID, EChatRoomEnterResponse> onComplete = null)
        {
            if (!WarnIfNotInitialized())
            {
                onComplete?.Invoke(false, CSteamID.Nil, EChatRoomEnterResponse.k_EChatRoomEnterResponseError);
                return;
            }

            var manager = Instance;
            manager._lastActionWasCreate = false;
            manager._pendingJoinLobbyId = lobbyId;
            manager._pendingJoinCompletion = onComplete;
            manager._explicitJoinInProgress = true;
            manager._joinRetryCount = 0;

            SteamMatchmaking.JoinLobby(lobbyId);
        }

        public static void LeaveCurrentLobby()
        {
            if (!WarnIfNotInitialized())
                return;

            var manager = Instance;
            if (manager._currentLobbyId.m_SteamID == 0)
                return;

            // No LobbyLeft_t callback exists; LeaveLobby is fire-and-forget, so state is cleared optimistically.
            SteamMatchmaking.LeaveLobby(manager._currentLobbyId);
            manager._currentLobbyId = CSteamID.Nil;
            LobbyLeft?.Invoke();
        }

        public static void InviteFriendViaOverlay()
        {
            if (!WarnIfNotInitialized())
                return;

            var manager = Instance;
            if (manager._currentLobbyId.m_SteamID == 0)
            {
                Debug.LogWarning("[Mailo.Networking.Steam] InviteFriendViaOverlay called while not in a lobby.");
                return;
            }

            SteamFriends.ActivateGameOverlayInviteDialog(manager._currentLobbyId);
        }

        private void OnLobbyCreated(LobbyCreated_t callback)
        {
            bool success = callback.m_eResult == EResult.k_EResultOK;

            // k_EResultNoConnection/Timeout typically mean Steam hasn't finished establishing
            // its session with the backend matchmaking servers yet (common right after launch)
            // and clear up on their own within a couple of seconds - safe to retry a few times.
            if (!success && IsTransientResult(callback.m_eResult) && _createRetryCount < MaxConnectionRetries)
            {
                _createRetryCount++;
                Debug.LogWarning($"[Mailo.Networking.Steam] CreateLobby failed with {callback.m_eResult}, retrying ({_createRetryCount}/{MaxConnectionRetries})...");
                StartCoroutine(RetryCreateLobbyAfterDelay());
                return;
            }

            CSteamID lobbyId = success ? new CSteamID(callback.m_ulSteamIDLobby) : CSteamID.Nil;

            if (success)
                SteamLobbyMetadata.WriteInitialOwnerMetadata(lobbyId, _pendingMaxMembers);

            var completion = _pendingCreateCompletion;
            _pendingCreateCompletion = null;
            completion?.Invoke(success, lobbyId, callback.m_eResult);

            LobbyCreated?.Invoke(success, lobbyId, callback.m_eResult);
        }

        private void OnLobbyEnter(LobbyEnter_t callback)
        {
            var response = (EChatRoomEnterResponse)callback.m_EChatRoomEnterResponse;
            bool success = response == EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess;

            // Only our own explicit JoinLobby request is retryable (we know which lobby id to
            // retry against); auto-joins triggered by GameLobbyJoinRequested_t are not retried.
            if (!success && response == EChatRoomEnterResponse.k_EChatRoomEnterResponseError
                && _explicitJoinInProgress && _joinRetryCount < MaxConnectionRetries)
            {
                _joinRetryCount++;
                Debug.LogWarning($"[Mailo.Networking.Steam] JoinLobby failed with {response}, retrying ({_joinRetryCount}/{MaxConnectionRetries})...");
                StartCoroutine(RetryJoinLobbyAfterDelay());
                return;
            }

            CSteamID lobbyId = new CSteamID(callback.m_ulSteamIDLobby);

            if (success)
                _currentLobbyId = lobbyId;

            SteamLobbyEnterKind kind = _lastActionWasCreate ? SteamLobbyEnterKind.CreatedByMe : SteamLobbyEnterKind.JoinedExisting;

            _explicitJoinInProgress = false;
            var completion = _pendingJoinCompletion;
            _pendingJoinCompletion = null;
            completion?.Invoke(success, lobbyId, response);

            LobbyEntered?.Invoke(success, lobbyId, kind, response);

            if (success)
            {
                SteamLobbyCharacterAssignment.RecomputeAndPublish(lobbyId);
                LobbyMembersChanged?.Invoke(lobbyId);
            }
        }

        private void OnLobbyChatUpdate(LobbyChatUpdate_t callback)
        {
            if (callback.m_ulSteamIDLobby != _currentLobbyId.m_SteamID)
                return;

            var stateChange = (EChatMemberStateChange)callback.m_rgfChatMemberStateChange;
            var userChanged = new CSteamID(callback.m_ulSteamIDUserChanged);

            const EChatMemberStateChange leftMask =
                EChatMemberStateChange.k_EChatMemberStateChangeLeft |
                EChatMemberStateChange.k_EChatMemberStateChangeDisconnected |
                EChatMemberStateChange.k_EChatMemberStateChangeKicked |
                EChatMemberStateChange.k_EChatMemberStateChangeBanned;

            if (userChanged == SteamUser.GetSteamID() && (stateChange & leftMask) != 0)
            {
                _currentLobbyId = CSteamID.Nil;
                LobbyLeft?.Invoke();
                return;
            }

            SteamLobbyCharacterAssignment.RecomputeAndPublish(_currentLobbyId);
            LobbyMembersChanged?.Invoke(_currentLobbyId);
        }

        private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t callback)
        {
            LobbyInviteAccepted?.Invoke(callback.m_steamIDLobby, callback.m_steamIDFriend);

            // Steam does not auto-join; the game must call JoinLobby itself in response.
            _lastActionWasCreate = false;
            _pendingJoinCompletion = null;
            _explicitJoinInProgress = false;
            SteamMatchmaking.JoinLobby(callback.m_steamIDLobby);
        }

        private void OnLobbyDataUpdate(LobbyDataUpdate_t callback)
        {
            // Equal member/lobby id means lobby-level data changed; otherwise it's per-member data (out of scope).
            if (callback.m_ulSteamIDMember != callback.m_ulSteamIDLobby)
                return;

            LobbyDataUpdated?.Invoke(new CSteamID(callback.m_ulSteamIDLobby));
        }

        private IEnumerator RetryCreateLobbyAfterDelay()
        {
            yield return new WaitForSeconds(ConnectionRetryDelaySeconds);
            SteamMatchmaking.CreateLobby(_pendingLobbyType, _pendingMaxMembers);
        }

        private IEnumerator RetryJoinLobbyAfterDelay()
        {
            yield return new WaitForSeconds(ConnectionRetryDelaySeconds);
            SteamMatchmaking.JoinLobby(_pendingJoinLobbyId);
        }

        private static bool IsTransientResult(EResult result)
        {
            return result == EResult.k_EResultNoConnection
                || result == EResult.k_EResultTimeout
                || result == EResult.k_EResultServiceUnavailable;
        }

        private static bool WarnIfNotInitialized()
        {
            if (SteamManager.Initialized)
                return true;

            if (!s_warnedNotInitialized)
            {
                s_warnedNotInitialized = true;
                Debug.LogWarning("[Mailo.Networking.Steam] SteamManager is not initialized. Lobby calls will no-op.");
            }

            return false;
        }
    }
}
