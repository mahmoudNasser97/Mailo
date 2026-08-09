using FishNet.Managing;
using FishNet.Transporting;
using Mailo.Networking.Steam;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mailo.Networking.Fish
{
    // Bridges the Steam lobby layer to FishNet: reacts to SteamLobbyManager.NotifyGameStarting()
    // (a lobby-metadata flag) by connecting every member over FishNet automatically - the host
    // starts hosting, everyone else connects to the host's already-known Steam ID - then each
    // client loads the Game scene locally once its own connection succeeds. No manual Steam ID
    // entry, no separate Host/Join buttons - the lobby's existing Start button drives all of it.
    public class NetworkLobbyBridge : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;

        private bool _networkStartTriggered;
        private bool _sceneLoadTriggered;

        private void OnEnable()
        {
            SteamLobbyManager.LobbyDataUpdated += OnLobbyDataUpdated;
            SteamLobbyManager.LobbyEntered += OnLobbyEntered;
            SteamLobbyManager.LobbyLeft += OnLobbyLeft;

            if (_networkManager == null)
            {
                Debug.LogWarning("[NetworkLobbyBridge] _networkManager is unassigned in the Inspector.");
                return;
            }

            _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            _networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
        }

        private void OnDisable()
        {
            SteamLobbyManager.LobbyDataUpdated -= OnLobbyDataUpdated;
            SteamLobbyManager.LobbyEntered -= OnLobbyEntered;
            SteamLobbyManager.LobbyLeft -= OnLobbyLeft;

            if (_networkManager == null)
                return;

            _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            _networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
        }

        private void OnLobbyDataUpdated(CSteamID lobbyId) => TryAutoConnect();

        private void OnLobbyEntered(bool success, CSteamID lobbyId, SteamLobbyEnterKind kind, EChatRoomEnterResponse response)
        {
            // Covers joining a lobby where the host already clicked Start before we got here.
            if (success)
                TryAutoConnect();
        }

        private void OnLobbyLeft()
        {
            _networkStartTriggered = false;
            _sceneLoadTriggered = false;

            if (_networkManager == null)
                return;

            if (_networkManager.ServerManager.Started)
                _networkManager.ServerManager.StopConnection(true);
            if (_networkManager.ClientManager.Started)
                _networkManager.ClientManager.StopConnection();
        }

        private void TryAutoConnect()
        {
            if (_networkStartTriggered || _networkManager == null)
                return;

            if (SteamLobbyManager.GetLobbyMetadata(SteamLobbyMetadata.KeyNetworkStarted) != "true")
                return;

            if (SteamLobbyManager.IsLobbyOwner())
            {
                _networkStartTriggered = true;
                _networkManager.ServerManager.StartConnection();
                _networkManager.ClientManager.StartConnection(); // host's own local client half
                return;
            }

            string hostSteamId = SteamLobbyManager.GetLobbyMetadata(SteamLobbyMetadata.KeyHostSteamId);
            if (string.IsNullOrEmpty(hostSteamId))
            {
                // hostSteamId is set once, at lobby creation, so this is replication lag, not
                // a real absence - LobbyDataUpdated will fire again shortly and we'll retry then.
                Debug.LogWarning("[NetworkLobbyBridge] networkStarted is true but hostSteamId isn't available yet - will retry.");
                return;
            }

            _networkStartTriggered = true;
            _networkManager.ClientManager.StartConnection(hostSteamId);
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            Debug.Log($"[NetworkLobbyBridge] Server: {args.ConnectionState}");
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            Debug.Log($"[NetworkLobbyBridge] Client: {args.ConnectionState}");

            if (args.ConnectionState == LocalConnectionState.Started && !_sceneLoadTriggered)
            {
                _sceneLoadTriggered = true;
                SceneManager.LoadScene("Game");
            }
        }
    }
}
