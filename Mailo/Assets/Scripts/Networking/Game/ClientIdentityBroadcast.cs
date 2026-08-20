using FishNet.Broadcast;

namespace Mailo.Networking.Game
{
    // Sent by every client (host's own local half included) right after its own FishNet
    // connection succeeds, so the server can pair a NetworkConnection to the Steam lobby
    // member it belongs to - the transport doesn't expose that correlation natively.
    internal struct ClientIdentityBroadcast : IBroadcast
    {
        public ulong SteamId64;
    }
}
