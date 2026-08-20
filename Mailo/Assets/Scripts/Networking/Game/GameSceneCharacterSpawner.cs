using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using Mailo.Networking.Fish;
using Mailo.Networking.Steam;
using Steamworks;
using UnityEngine;

namespace Mailo.Networking.Game
{
    public class GameSceneCharacterSpawner : MonoBehaviour
    {
        [Serializable]
        private struct CharacterPrefabEntry
        {
            public string DisplayName;
            public NetworkObject NetworkedPrefab;
        }

        private const int MaxSlots = 4;

        [SerializeField] private CharacterPrefabEntry[] _characterPrefabs;
        [SerializeField] private Transform[] _spawnPoints;
        [SerializeField] private float _autoSpreadSpacing = 3f;

        private readonly HashSet<NetworkConnection> _spawnedConnections = new HashSet<NetworkConnection>();
        private int _nextSpawnIndex;

        private void OnEnable()
        {
            NetworkLobbyBridge.ClientIdentityReceived += OnClientIdentityReceived;

            // Catch up on any identities that arrived before this scene (and thus this
            // component) existed - see NetworkLobbyBridge for why that's a real possibility.
            if (NetworkLobbyBridge.Instance != null)
            {
                foreach (var entry in NetworkLobbyBridge.Instance.ReceivedIdentities)
                    OnClientIdentityReceived(entry.Key, entry.Value);
            }
        }

        private void OnDisable()
        {
            NetworkLobbyBridge.ClientIdentityReceived -= OnClientIdentityReceived;
        }

        private void OnClientIdentityReceived(NetworkConnection connection, ulong steamId64)
        {
            // Every client runs this (it's how identities get sent in the first place via
            // NetworkLobbyBridge), but only the server actually spawns anything.
            if (!InstanceFinder.IsServerStarted)
                return;

            if (!_spawnedConnections.Add(connection))
                return; // already spawned for this connection

            if (_nextSpawnIndex >= MaxSlots)
            {
                Debug.LogWarning($"[GameSceneCharacterSpawner] Already spawned {MaxSlots} characters - ignoring identity for Steam ID {steamId64}.");
                return;
            }

            var steamId = new CSteamID(steamId64);
            string assignedCharacter = null;

            foreach (var member in SteamLobbyManager.GetLobbyMembers())
            {
                if (member.SteamId.m_SteamID == steamId.m_SteamID)
                {
                    assignedCharacter = member.AssignedCharacter;
                    break;
                }
            }

            if (string.IsNullOrEmpty(assignedCharacter))
            {
                Debug.LogWarning($"[GameSceneCharacterSpawner] No lobby member/assigned character found for Steam ID {steamId64} - not spawning.");
                return;
            }

            NetworkObject prefab = FindPrefab(assignedCharacter);
            if (prefab == null)
            {
                Debug.LogWarning($"[GameSceneCharacterSpawner] No prefab mapped for character \"{assignedCharacter}\" - not spawning.");
                return;
            }

            Vector3 position = GetSpawnPosition(_nextSpawnIndex);
            _nextSpawnIndex++;

            NetworkObject instance = Instantiate(prefab, position, Quaternion.identity);
            InstanceFinder.ServerManager.Spawn(instance.gameObject, connection);
        }

        private NetworkObject FindPrefab(string characterName)
        {
            if (_characterPrefabs == null)
                return null;

            foreach (var entry in _characterPrefabs)
            {
                if (entry.DisplayName == characterName)
                    return entry.NetworkedPrefab;
            }

            return null;
        }

        private Vector3 GetSpawnPosition(int index)
        {
            if (_spawnPoints != null && index < _spawnPoints.Length && _spawnPoints[index] != null)
                return _spawnPoints[index].position;

            // Relative to this GameObject's own position, not world (0,0,0) - the spawner
            // sits wherever it was placed on the island (e.g. -190, 14.5, 74.5 here), and
            // absolute-world spawn points would land players far off the map/underground.
            return transform.position + new Vector3(index * _autoSpreadSpacing, 0f, 0f);
        }
    }
}
