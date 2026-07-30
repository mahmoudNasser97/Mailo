using System.Text;
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
        [SerializeField] private TMP_Text _memberListLabel;
        [SerializeField] private Button _createLobbyButton;
        [SerializeField] private Button _joinByIdButton;
        [SerializeField] private Button _leaveLobbyButton;
        [SerializeField] private Button _inviteFriendButton;

        private void Awake()
        {
            // Forces early bootstrap so a friend's overlay invite (GameLobbyJoinRequested_t)
            // is heard even before any button here has been clicked.
            _ = SteamLobbyManager.IsInLobby;
        }

        private void OnEnable()
        {
            SteamLobbyManager.LobbyCreated += OnLobbyCreated;
            SteamLobbyManager.LobbyEntered += OnLobbyEntered;
            SteamLobbyManager.LobbyMembersChanged += OnLobbyMembersChanged;
            SteamLobbyManager.LobbyLeft += OnLobbyLeft;
            SteamLobbyManager.LobbyInviteAccepted += OnLobbyInviteAccepted;

            if (_createLobbyButton != null) _createLobbyButton.onClick.AddListener(OnCreateLobbyClicked);
            if (_joinByIdButton != null) _joinByIdButton.onClick.AddListener(OnJoinByIdClicked);
            if (_leaveLobbyButton != null) _leaveLobbyButton.onClick.AddListener(OnLeaveLobbyClicked);
            if (_inviteFriendButton != null) _inviteFriendButton.onClick.AddListener(OnInviteFriendClicked);
        }

        private void OnDisable()
        {
            SteamLobbyManager.LobbyCreated -= OnLobbyCreated;
            SteamLobbyManager.LobbyEntered -= OnLobbyEntered;
            SteamLobbyManager.LobbyMembersChanged -= OnLobbyMembersChanged;
            SteamLobbyManager.LobbyLeft -= OnLobbyLeft;
            SteamLobbyManager.LobbyInviteAccepted -= OnLobbyInviteAccepted;

            if (_createLobbyButton != null) _createLobbyButton.onClick.RemoveListener(OnCreateLobbyClicked);
            if (_joinByIdButton != null) _joinByIdButton.onClick.RemoveListener(OnJoinByIdClicked);
            if (_leaveLobbyButton != null) _leaveLobbyButton.onClick.RemoveListener(OnLeaveLobbyClicked);
            if (_inviteFriendButton != null) _inviteFriendButton.onClick.RemoveListener(OnInviteFriendClicked);
        }

        private void OnCreateLobbyClicked()
        {
            SteamLobbyManager.CreateLobby(OnCreateResult);
        }

        private void OnJoinByIdClicked()
        {
            if (_lobbyIdInputField == null || !ulong.TryParse(_lobbyIdInputField.text, out ulong raw))
            {
                SetStatus("Enter a valid numeric Lobby ID first.");
                return;
            }

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
            SetStatus(success ? $"Lobby created: {lobbyId}" : $"Create lobby failed: {result}");
        }

        private void OnJoinResult(bool success, CSteamID lobbyId, EChatRoomEnterResponse response)
        {
            SetStatus(success ? $"Joined lobby: {lobbyId}" : $"Join lobby failed: {response}");
        }

        private void OnLobbyCreated(bool success, CSteamID lobbyId, EResult result)
        {
            Debug.Log($"[SteamLobbyDebugProbe] LobbyCreated success={success} lobbyId={lobbyId} result={result}");
        }

        private void OnLobbyEntered(bool success, CSteamID lobbyId, SteamLobbyEnterKind kind, EChatRoomEnterResponse response)
        {
            Debug.Log($"[SteamLobbyDebugProbe] LobbyEntered success={success} lobbyId={lobbyId} kind={kind} response={response}");
            SetStatus(success ? $"In lobby: {lobbyId} ({kind})" : $"Enter failed: {response}");
        }

        private void OnLobbyMembersChanged(CSteamID lobbyId)
        {
            RefreshMemberList();
        }

        private void OnLobbyLeft()
        {
            Debug.Log("[SteamLobbyDebugProbe] LobbyLeft");
            SetStatus("Left lobby.");

            if (_memberListLabel != null)
                _memberListLabel.text = string.Empty;
        }

        private void OnLobbyInviteAccepted(CSteamID lobbyId, CSteamID friendId)
        {
            Debug.Log($"[SteamLobbyDebugProbe] Invite accepted, joining lobby {lobbyId} via friend {friendId}");
            SetStatus($"Joining {SteamIdentityManager.GetDisplayName(friendId)}'s lobby...");
        }

        private void RefreshMemberList()
        {
            if (_memberListLabel == null)
                return;

            var members = SteamLobbyManager.GetLobbyMembers();
            var sb = new StringBuilder();
            foreach (var member in members)
                sb.AppendLine(member.DisplayName);

            _memberListLabel.text = sb.ToString();
        }

        private void SetStatus(string message)
        {
            Debug.Log($"[SteamLobbyDebugProbe] {message}");

            if (_statusLabel != null)
                _statusLabel.text = message;
        }
    }
}
