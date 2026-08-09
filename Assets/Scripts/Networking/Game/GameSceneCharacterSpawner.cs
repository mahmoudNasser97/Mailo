using Mailo.Networking.Steam;
using UnityEngine;

namespace Mailo.Networking.Game
{
    public class GameSceneCharacterSpawner : MonoBehaviour
    {
        private const int MaxSlots = 4;

        [SerializeField] private Transform[] _spawnPoints;
        [SerializeField] private float _autoSpreadSpacing = 3f;

        private void Start()
        {
            var members = SteamLobbyManager.GetLobbyMembers();

            for (int i = 0; i < members.Count && i < MaxSlots; i++)
            {
                var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                capsule.transform.position = GetSpawnPosition(i);

                string character = string.IsNullOrEmpty(members[i].AssignedCharacter) ? "Unassigned" : members[i].AssignedCharacter;
                capsule.name = $"Player_{members[i].DisplayName}_{character}";
            }
        }

        private Vector3 GetSpawnPosition(int index)
        {
            if (_spawnPoints != null && index < _spawnPoints.Length && _spawnPoints[index] != null)
                return _spawnPoints[index].position;

            return new Vector3(index * _autoSpreadSpacing, 0f, 0f);
        }
    }
}
