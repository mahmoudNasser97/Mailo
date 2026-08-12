using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Mailo.Networking.Steam
{
    public class SteamLobbyMemberRow : MonoBehaviour
    {
        [SerializeField] private RawImage _avatarImage;
        [SerializeField] private TMP_Text _nameLabel;
        [SerializeField] private TMP_Text _characterLabel;

        public void Bind(SteamLobbyMemberInfo member)
        {
            if (_nameLabel != null)
            {
                _nameLabel.text = member.DisplayName;
            }
            else
            {
                Debug.LogWarning($"[SteamLobbyMemberRow] '{name}' has no _nameLabel assigned in the Inspector.", this);
            }

            if (_characterLabel != null)
            {
                // May legitimately be empty for a brief moment (host's assignment write hasn't
                // propagated to this client yet) - not a wiring problem, so no warning here.
                _characterLabel.text = member.AssignedCharacter;
            }
            else
            {
                Debug.LogWarning($"[SteamLobbyMemberRow] '{name}' has no _characterLabel assigned in the Inspector.", this);
            }

            if (_avatarImage != null)
            {
                _avatarImage.texture = null;
                SteamIdentityManager.RequestAvatar(member.SteamId, OnAvatarReady);
            }
            else
            {
                Debug.LogWarning($"[SteamLobbyMemberRow] '{name}' has no _avatarImage assigned in the Inspector.", this);
            }
        }

        private void OnAvatarReady(CSteamID steamId, Texture2D avatar)
        {
            if (_avatarImage != null)
                _avatarImage.texture = avatar;
        }
    }
}
