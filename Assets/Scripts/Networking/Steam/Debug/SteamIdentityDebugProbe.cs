using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Mailo.Networking.Steam
{
    public class SteamIdentityDebugProbe : MonoBehaviour
    {
        [SerializeField] private RawImage _preview;
        [SerializeField] private TMP_Text _nameLabel;

        private void Start()
        {
            Debug.Log($"[SteamIdentityDebugProbe] SteamManager.Initialized = {SteamManager.Initialized}");

            string displayName = SteamIdentityManager.GetLocalDisplayName();
            Debug.Log($"[SteamIdentityDebugProbe] Local display name: {displayName}");

            if (_nameLabel != null)
                _nameLabel.text = displayName;

            SteamIdentityManager.RequestLocalAvatar(OnLocalAvatarReady);
        }

        private void OnLocalAvatarReady(Texture2D avatar)
        {
            if (avatar == null)
            {
                Debug.Log("[SteamIdentityDebugProbe] Avatar not available.");
                return;
            }

            Debug.Log($"[SteamIdentityDebugProbe] Avatar ready: {avatar.width}x{avatar.height}");

            if (_preview != null)
                _preview.texture = avatar;
        }
    }
}
