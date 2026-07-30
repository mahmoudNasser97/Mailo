using Steamworks;

namespace Mailo.Networking.Steam
{
    public readonly struct SteamLobbyMemberInfo
    {
        public readonly CSteamID SteamId;
        public readonly string DisplayName;

        public SteamLobbyMemberInfo(CSteamID steamId, string displayName)
        {
            SteamId = steamId;
            DisplayName = displayName;
        }
    }
}
