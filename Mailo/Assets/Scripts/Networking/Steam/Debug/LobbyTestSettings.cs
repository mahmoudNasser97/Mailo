using UnityEngine;

namespace Mailo.Networking.Steam
{
    // Dev-only convenience: lets testing with fewer real people happen without editing code
    // every time. Add this once anywhere in the lobby scene and flip the checkbox as needed.
    // Absent from the scene (or left disabled), lobbies use the real 4-player default.
    public class LobbyTestSettings : MonoBehaviour
    {
        [Tooltip("When enabled, lobbies are created with a 2-player max instead of the real " +
                 "4-player default. Turn this off for normal testing/real play.")]
        [SerializeField] private bool _testModeTwoPlayers = false;

        public static bool TestModeTwoPlayers { get; private set; }

        private void Awake()
        {
            TestModeTwoPlayers = _testModeTwoPlayers;
        }

        // Keeps the static value in sync if the checkbox is toggled while already in Play mode.
        private void OnValidate()
        {
            TestModeTwoPlayers = _testModeTwoPlayers;
        }
    }
}
