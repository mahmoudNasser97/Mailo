using Steamworks;

namespace Mailo.Networking.Steam
{
    public readonly struct SteamLobbyMemberInfo
    {
        public readonly CSteamID SteamId;
        public readonly string DisplayName;
        public readonly string AssignedCharacter;

        public SteamLobbyMemberInfo(CSteamID steamId, string displayName, string assignedCharacter)
        {
            SteamId = steamId;
            DisplayName = displayName;
            AssignedCharacter = assignedCharacter;
        }
    }
}
