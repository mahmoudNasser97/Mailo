using Steamworks;
using UnityEngine;

namespace Mailo.Networking.Steam
{
    internal static class SteamLobbyMetadata
    {
        public const string KeyHostSteamId = "hostSteamId";
        public const string KeyBuildVersion = "buildVersion";
        public const string KeySessionState = "sessionState";
        public const string KeyCurrentPlayers = "currentPlayers";
        public const string KeyMaxPlayers = "maxPlayers";
        public const string KeyJoinable = "joinable";
        public const string KeyGameMode = "gameMode";
        public const string KeyIsPublic = "isPublic";

        public const string SessionStateWaitingForPlayers = "WaitingForPlayers";
        public const string GameModeCoop = "coop";

        // Only the lobby owner's SetLobbyData calls succeed; non-owner calls fail silently (no error/exception).
        public static void WriteInitialOwnerMetadata(CSteamID lobbyId, int maxMembers)
        {
            SteamMatchmaking.SetLobbyData(lobbyId, KeyHostSteamId, SteamUser.GetSteamID().m_SteamID.ToString());
            SteamMatchmaking.SetLobbyData(lobbyId, KeyBuildVersion, Application.version);
            SteamMatchmaking.SetLobbyData(lobbyId, KeySessionState, SessionStateWaitingForPlayers);
            SteamMatchmaking.SetLobbyData(lobbyId, KeyCurrentPlayers, SteamMatchmaking.GetNumLobbyMembers(lobbyId).ToString());
            SteamMatchmaking.SetLobbyData(lobbyId, KeyMaxPlayers, maxMembers.ToString());
            SteamMatchmaking.SetLobbyData(lobbyId, KeyJoinable, "true");
            SteamMatchmaking.SetLobbyData(lobbyId, KeyGameMode, GameModeCoop);
            SteamMatchmaking.SetLobbyData(lobbyId, KeyIsPublic, "false");
        }

        public static string Read(CSteamID lobbyId, string key)
        {
            return SteamMatchmaking.GetLobbyData(lobbyId, key);
        }
    }
}
