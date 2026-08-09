using System.Collections.Generic;
using System.Linq;
using Steamworks;
using UnityEngine;

namespace Mailo.Networking.Steam
{
    internal static class SteamLobbyCharacterAssignment
    {
        public static readonly string[] Roster = { "Bruno", "Ranger", "Zara", "Pixel" };

        private const char EntrySeparator = ';';
        private const char KeyValueSeparator = '=';

        // Only the lobby owner's SetLobbyData calls succeed (Steam-enforced); this recompute
        // is safe to call from any client, it just no-ops for non-owners.
        public static void RecomputeAndPublish(CSteamID lobbyId)
        {
            if (SteamMatchmaking.GetLobbyOwner(lobbyId) != SteamUser.GetSteamID())
                return;

            var map = Parse(SteamMatchmaking.GetLobbyData(lobbyId, SteamLobbyMetadata.KeyCharacterAssignments));

            var memberIds = new HashSet<ulong>();
            int count = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
            for (int i = 0; i < count; i++)
                memberIds.Add(SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i).m_SteamID);

            bool changed = false;

            // Prune departed members only — never touches entries for members still present,
            // which is what keeps existing members' characters stable across roster changes.
            foreach (var key in map.Keys.ToList())
            {
                if (!memberIds.Contains(key))
                {
                    map.Remove(key);
                    changed = true;
                }
            }

            // Assign only to members lacking an entry — never reassigns an existing member's character.
            var available = Roster.Where(c => !map.ContainsValue(c)).ToList();
            foreach (var id in memberIds)
            {
                if (map.ContainsKey(id))
                    continue;

                if (available.Count == 0)
                {
                    Debug.LogWarning("[SteamLobbyCharacterAssignment] No characters left to assign - lobby has more members than roster characters.");
                    continue;
                }

                int pick = Random.Range(0, available.Count);
                map[id] = available[pick];
                available.RemoveAt(pick);
                changed = true;
            }

            if (!changed)
                return;

            SteamMatchmaking.SetLobbyData(lobbyId, SteamLobbyMetadata.KeyCharacterAssignments, Serialize(map));
        }

        public static string GetAssignedCharacter(CSteamID lobbyId, CSteamID memberId)
        {
            var map = Parse(SteamMatchmaking.GetLobbyData(lobbyId, SteamLobbyMetadata.KeyCharacterAssignments));
            return map.TryGetValue(memberId.m_SteamID, out var character) ? character : string.Empty;
        }

        private static Dictionary<ulong, string> Parse(string raw)
        {
            var map = new Dictionary<ulong, string>();

            if (string.IsNullOrEmpty(raw))
                return map;

            foreach (var entry in raw.Split(EntrySeparator))
            {
                if (string.IsNullOrEmpty(entry))
                    continue;

                var parts = entry.Split(KeyValueSeparator);
                if (parts.Length != 2)
                    continue;

                if (!ulong.TryParse(parts[0], out var id))
                    continue;

                map[id] = parts[1];
            }

            return map;
        }

        private static string Serialize(Dictionary<ulong, string> map)
        {
            return string.Join(EntrySeparator.ToString(), map.Select(kvp => $"{kvp.Key}{KeyValueSeparator}{kvp.Value}"));
        }
    }
}
