using System;
using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using Mailo.Networking.Game;
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
    //
    // Also owns the Steam-identity handshake: the server needs to know which NetworkConnection
    // corresponds to which CSteamID (the transport doesn't expose that natively) in order to
    // spawn the correct character for each connection. The broadcast handler is registered here
    // - the moment the server starts, well before the Game scene even loads - rather than inside
    // the scene-bound spawner, specifically to avoid a race: a remote client's identity broadcast
    // can arrive before the server-side Game scene (and thus a scene-bound listener) exists.
    // Received identities are cached (ReceivedIdentities) so a listener that starts late (like the
    // spawner, once its scene loads) can catch up instead of missing anything sent before it existed.
    public class NetworkLobbyBridge : MonoBehaviour
    {
        public static NetworkLobbyBridge Instance { get; private set; }
        public static event Action<NetworkConnection, ulong> ClientIdentityReceived;

        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private string _gameSceneName = "Demo_Island";

        private readonly Dictionary<NetworkConnection, ulong> _receivedIdentities = new Dictionary<NetworkConnection, ulong>();

        private bool _networkStartTriggered;
        private bool _sceneLoadTriggered;
        private bool _identityBroadcastRegistered;

        public IReadOnlyDictionary<NetworkConnection, ulong> ReceivedIdentities => _receivedIdentities;

        private void Awake()
        {
            Instance = this;
        }

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

            UnregisterIdentityBroadcastIfNeeded();
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
            _receivedIdentities.Clear();
            UnregisterIdentityBroadcastIfNeeded();

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
                // Registered before StartConnection() so the handler is live the instant the
                // server starts accepting connections - no window where an early identity
                // broadcast could arrive with nobody listening.
                RegisterIdentityBroadcastIfNeeded();
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

        private void RegisterIdentityBroadcastIfNeeded()
        {
            if (_identityBroadcastRegistered || _networkManager == null)
                return;

            _identityBroadcastRegistered = true;
            _networkManager.ServerManager.RegisterBroadcast<ClientIdentityBroadcast>(OnClientIdentityBroadcastReceived);
        }

        private void UnregisterIdentityBroadcastIfNeeded()
        {
            if (!_identityBroadcastRegistered || _networkManager == null)
                return;

            _identityBroadcastRegistered = false;
            _networkManager.ServerManager.UnregisterBroadcast<ClientIdentityBroadcast>(OnClientIdentityBroadcastReceived);
        }

        private void OnClientIdentityBroadcastReceived(NetworkConnection conn, ClientIdentityBroadcast message, Channel channel)
        {
            _receivedIdentities[conn] = message.SteamId64;
            ClientIdentityReceived?.Invoke(conn, message.SteamId64);
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            Debug.Log($"[NetworkLobbyBridge] Server: {args.ConnectionState}");
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            Debug.Log($"[NetworkLobbyBridge] Client: {args.ConnectionState}");

            if (args.ConnectionState != LocalConnectionState.Started)
                return;

            // Tells the server which Steam identity this connection belongs to - sent
            // unconditionally, including for the host's own local client half.
            _networkManager.ClientManager.Broadcast(new ClientIdentityBroadcast { SteamId64 = SteamUser.GetSteamID().m_SteamID });

            if (!_sceneLoadTriggered)
            {
                _sceneLoadTriggered = true;
                SceneManager.LoadScene(_gameSceneName);
            }
        }
    }
}
