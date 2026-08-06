using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Mailo.Networking.Steam
{
    public class SteamLobbyDebugProbe : MonoBehaviour
    {
        [SerializeField] private TMP_InputField _lobbyIdInputField;
        [SerializeField] private TMP_Text _statusLabel;
        [SerializeField] private TMP_Text _playerCountLabel;
        [SerializeField] private SteamLobbyMemberRow[] _memberRows;
        [SerializeField] private Button _createLobbyButton;
        [SerializeField] private Button _joinByIdButton;
        [SerializeField] private Button _leaveLobbyButton;
        [SerializeField] private Button _inviteFriendButton;
        [SerializeField] private Button _startButton;

        private bool _operationInProgress;

        private void Awake()
        {
            // Forces early bootstrap so a friend's overlay invite (GameLobbyJoinRequested_t)
            // is heard even before any button here has been clicked.
            _ = SteamLobbyManager.IsInLobby;
        }

        private void Start()
        {
            // Ensures rows start hidden (no lobby yet) instead of showing whatever
            // placeholder state they were left in inside the Editor.
            RefreshMemberList();
            UpdateButtonStates();
        }

        private void OnEnable()
        {
            SteamLobbyManager.LobbyCreated += OnLobbyCreated;
            SteamLobbyManager.LobbyEntered += OnLobbyEntered;
            SteamLobbyManager.LobbyMembersChanged += OnLobbyMembersChanged;
            SteamLobbyManager.LobbyLeft += OnLobbyLeft;
            SteamLobbyManager.LobbyInviteAccepted += OnLobbyInviteAccepted;
            SteamLobbyManager.LobbyDataUpdated += OnLobbyDataUpdated;

            if (_createLobbyButton != null) _createLobbyButton.onClick.AddListener(OnCreateLobbyClicked);
            if (_joinByIdButton != null) _joinByIdButton.onClick.AddListener(OnJoinByIdClicked);
            if (_leaveLobbyButton != null) _leaveLobbyButton.onClick.AddListener(OnLeaveLobbyClicked);
            if (_inviteFriendButton != null) _inviteFriendButton.onClick.AddListener(OnInviteFriendClicked);
            if (_startButton != null) _startButton.onClick.AddListener(OnStartClicked);
        }

        private void OnDisable()
        {
            SteamLobbyManager.LobbyCreated -= OnLobbyCreated;
            SteamLobbyManager.LobbyEntered -= OnLobbyEntered;
            SteamLobbyManager.LobbyMembersChanged -= OnLobbyMembersChanged;
            SteamLobbyManager.LobbyLeft -= OnLobbyLeft;
            SteamLobbyManager.LobbyInviteAccepted -= OnLobbyInviteAccepted;
            SteamLobbyManager.LobbyDataUpdated -= OnLobbyDataUpdated;

            if (_createLobbyButton != null) _createLobbyButton.onClick.RemoveListener(OnCreateLobbyClicked);
            if (_joinByIdButton != null) _joinByIdButton.onClick.RemoveListener(OnJoinByIdClicked);
            if (_leaveLobbyButton != null) _leaveLobbyButton.onClick.RemoveListener(OnLeaveLobbyClicked);
            if (_inviteFriendButton != null) _inviteFriendButton.onClick.RemoveListener(OnInviteFriendClicked);
            if (_startButton != null) _startButton.onClick.RemoveListener(OnStartClicked);
        }

        private void OnCreateLobbyClicked()
        {
            SetOperationInProgress(true);
            // TEMP (2-player test): explicit maxMembers: 2 overrides the real 4-player default.
            // Revert to SteamLobbyManager.CreateLobby(OnCreateResult) to go back to 4.
            SteamLobbyManager.CreateLobby(OnCreateResult, maxMembers: 2);
        }

        private void OnJoinByIdClicked()
        {
            if (_lobbyIdInputField == null || !ulong.TryParse(_lobbyIdInputField.text, out ulong raw))
            {
                SetStatus("Enter a valid numeric Lobby ID first.");
                return;
            }

            SetOperationInProgress(true);
            SteamLobbyManager.JoinLobby(new CSteamID(raw), OnJoinResult);
        }

        private void OnLeaveLobbyClicked()
        {
            SteamLobbyManager.LeaveCurrentLobby();
        }

        private void OnInviteFriendClicked()
        {
            SteamLobbyManager.InviteFriendViaOverlay();
        }

        private void OnCreateResult(bool success, CSteamID lobbyId, EResult result)
        {
            SetOperationInProgress(false);
            SetStatus(success ? $"Lobby created: {lobbyId}" : $"Create lobby failed: {result}");
        }

        private void OnJoinResult(bool success, CSteamID lobbyId, EChatRoomEnterResponse response)
        {
            SetOperationInProgress(false);
            SetStatus(success ? $"Joined lobby: {lobbyId}" : $"Join lobby failed: {response}");
        }

        private void OnLobbyCreated(bool success, CSteamID lobbyId, EResult result)
        {
            Debug.Log($"[SteamLobbyDebugProbe] LobbyCreated success={success} lobbyId={lobbyId} result={result}");
            // Also cleared here (not just OnCreateResult) as a safety net for any future path
            // that creates a lobby without going through the tracked onComplete callback.
            SetOperationInProgress(false);
        }

        private void OnLobbyEntered(bool success, CSteamID lobbyId, SteamLobbyEnterKind kind, EChatRoomEnterResponse response)
        {
            Debug.Log($"[SteamLobbyDebugProbe] LobbyEntered success={success} lobbyId={lobbyId} kind={kind} response={response}");
            SetStatus(success ? $"In lobby: {lobbyId} ({kind})" : $"Enter failed: {response}");
            // Covers the invite-accepted auto-join path too, which has no onComplete callback.
            SetOperationInProgress(false);
        }

        private void OnLobbyMembersChanged(CSteamID lobbyId)
        {
            RefreshMemberList();
            // Member count affects Start's interactable state - must refresh here too, not
            // just after locally-initiated actions like Create/Join/Leave.
            UpdateButtonStates();
        }

        private void OnLobbyDataUpdated(CSteamID lobbyId)
        {
            // Doesn't tell us which key changed, so just refresh unconditionally - cheap at 4 rows.
            RefreshMemberList();
            UpdateButtonStates();
        }

        private void OnStartClicked()
        {
            // Doesn't load the scene directly - just signals via lobby metadata. Every member
            // (host included) reacts to that signal in Mailo.Networking.Fish.NetworkLobbyBridge:
            // connects over FishNet, then loads the Game scene locally once its own connection
            // succeeds, so everyone arrives together without any manual Steam ID entry.
            SteamLobbyManager.NotifyGameStarting();
        }

        private void OnLobbyLeft()
        {
            Debug.Log("[SteamLobbyDebugProbe] LobbyLeft");
            SetStatus("Left lobby.");
            RefreshMemberList();
            UpdateButtonStates();
        }

        private void OnLobbyInviteAccepted(CSteamID lobbyId, CSteamID friendId)
        {
            Debug.Log($"[SteamLobbyDebugProbe] Invite accepted, joining lobby {lobbyId} via friend {friendId}");
            SetStatus($"Joining {SteamIdentityManager.GetDisplayName(friendId)}'s lobby...");
            // An auto-join is about to happen; block manual Create/Join/Leave/Invite until it resolves.
            SetOperationInProgress(true);
        }

        private void RefreshMemberList()
        {
            var members = SteamLobbyManager.GetLobbyMembers();

            if (_memberRows == null || _memberRows.Length == 0)
            {
                Debug.LogWarning("[SteamLobbyDebugProbe] _memberRows is empty/unassigned in the Inspector - member list will not display.");
            }
            else
            {
                for (int i = 0; i < _memberRows.Length; i++)
                {
                    if (_memberRows[i] == null)
                    {
                        Debug.LogWarning($"[SteamLobbyDebugProbe] _memberRows[{i}] is unassigned in the Inspector.");
                        continue;
                    }

                    if (i < members.Count)
                    {
                        _memberRows[i].gameObject.SetActive(true);
                        _memberRows[i].Bind(members[i]);
                    }
                    else
                    {
                        _memberRows[i].gameObject.SetActive(false);
                    }
                }
            }

            if (_playerCountLabel != null)
            {
                string maxPlayers = SteamLobbyManager.GetLobbyMetadata(SteamLobbyMetadata.KeyMaxPlayers);
                if (string.IsNullOrEmpty(maxPlayers))
                    maxPlayers = "4";

                _playerCountLabel.text = $"Players: {members.Count}/{maxPlayers}";
            }
        }

        private void SetOperationInProgress(bool inProgress)
        {
            _operationInProgress = inProgress;
            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
            bool inLobby = SteamLobbyManager.IsInLobby;

            // Create/Join only make sense when idle and not already in a lobby.
            bool canStartNewAction = !_operationInProgress && !inLobby;
            // Leave/Invite only make sense once actually in a lobby, and not mid-operation.
            bool canManageCurrentLobby = !_operationInProgress && inLobby;

            if (_createLobbyButton != null) _createLobbyButton.interactable = canStartNewAction;
            if (_joinByIdButton != null) _joinByIdButton.interactable = canStartNewAction;
            if (_lobbyIdInputField != null) _lobbyIdInputField.interactable = canStartNewAction;
            if (_leaveLobbyButton != null) _leaveLobbyButton.interactable = canManageCurrentLobby;
            if (_inviteFriendButton != null) _inviteFriendButton.interactable = canManageCurrentLobby;

            if (_startButton != null)
            {
                // Non-hosts never see this button at all (not just disabled) - starting the
                // game is a host-only action, so showing it greyed-out to everyone else is
                // just confusing UI they can never use.
                bool isOwner = inLobby && SteamLobbyManager.IsLobbyOwner();
                _startButton.gameObject.SetActive(isOwner);

                if (isOwner)
                {
                    string maxPlayersRaw = SteamLobbyManager.GetLobbyMetadata(SteamLobbyMetadata.KeyMaxPlayers);
                    if (!int.TryParse(maxPlayersRaw, out int maxPlayers))
                        maxPlayers = 4;

                    _startButton.interactable = canManageCurrentLobby && SteamLobbyManager.GetLobbyMembers().Count == maxPlayers;
                }
            }
        }

        private void SetStatus(string message)
        {
            Debug.Log($"[SteamLobbyDebugProbe] {message}");

            if (_statusLabel != null)
                _statusLabel.text = message;
        }
    }
}
