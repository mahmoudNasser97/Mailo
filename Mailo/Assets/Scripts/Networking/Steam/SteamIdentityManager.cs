using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace Mailo.Networking.Steam
{
    public sealed class SteamIdentityManager : MonoBehaviour
    {
        private static SteamIdentityManager s_instance;
        private static bool s_warnedNotInitialized;

        public static event Action<CSteamID, Texture2D> AvatarReady;

        public static SteamIdentityManager Instance
        {
            get
            {
                if (s_instance == null)
                {
                    var go = new GameObject(nameof(SteamIdentityManager));
                    s_instance = go.AddComponent<SteamIdentityManager>();
                }
                return s_instance;
            }
        }

        [SerializeField] private SteamAvatarSize _defaultAvatarSize = SteamAvatarSize.Medium;

        private readonly Dictionary<(CSteamID, SteamAvatarSize), Texture2D> _avatarCache = new Dictionary<(CSteamID, SteamAvatarSize), Texture2D>();
        private readonly HashSet<(CSteamID, SteamAvatarSize)> _resolvedNoAvatar = new HashSet<(CSteamID, SteamAvatarSize)>();
        private readonly Dictionary<CSteamID, List<PendingAvatarRequest>> _pending = new Dictionary<CSteamID, List<PendingAvatarRequest>>();

        // Must be a field: Steamworks.NET Callback<T> instances stop firing silently if garbage collected.
        private Callback<AvatarImageLoaded_t> _avatarImageLoadedCallback;

        private struct PendingAvatarRequest
        {
            public SteamAvatarSize Size;
            public Action<CSteamID, Texture2D> Callback;
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public static string GetLocalDisplayName()
        {
            if (!WarnIfNotInitialized())
                return "Player";

            return SteamFriends.GetPersonaName();
        }

        public static string GetDisplayName(CSteamID steamId)
        {
            if (!WarnIfNotInitialized())
                return "Player";

            return SteamFriends.GetFriendPersonaName(steamId);
        }

        public static bool TryGetLocalAvatar(out Texture2D avatar, SteamAvatarSize? size = null)
        {
            if (!WarnIfNotInitialized())
            {
                avatar = null;
                return false;
            }

            return TryGetCachedAvatar(SteamUser.GetSteamID(), out avatar, size);
        }

        public static bool TryGetCachedAvatar(CSteamID steamId, out Texture2D avatar, SteamAvatarSize? size = null)
        {
            if (!WarnIfNotInitialized())
            {
                avatar = null;
                return false;
            }

            var manager = Instance;
            var key = (steamId, size ?? manager._defaultAvatarSize);
            return manager._avatarCache.TryGetValue(key, out avatar);
        }

        public static void RequestLocalAvatar(Action<Texture2D> onReady, SteamAvatarSize? size = null)
        {
            if (onReady == null)
                return;

            if (!WarnIfNotInitialized())
            {
                onReady(null);
                return;
            }

            RequestAvatar(SteamUser.GetSteamID(), (id, tex) => onReady(tex), size);
        }

        public static void RequestAvatar(CSteamID steamId, Action<CSteamID, Texture2D> onReady, SteamAvatarSize? size = null)
        {
            if (!WarnIfNotInitialized())
            {
                onReady?.Invoke(steamId, null);
                return;
            }

            var manager = Instance;
            if (manager._avatarImageLoadedCallback == null)
                manager._avatarImageLoadedCallback = Callback<AvatarImageLoaded_t>.Create(manager.OnAvatarImageLoaded);

            var resolvedSize = size ?? manager._defaultAvatarSize;
            var key = (steamId, resolvedSize);

            if (manager._avatarCache.TryGetValue(key, out var cached))
            {
                onReady?.Invoke(steamId, cached);
                return;
            }

            if (manager._resolvedNoAvatar.Contains(key))
            {
                onReady?.Invoke(steamId, null);
                return;
            }

            int handle = GetAvatarHandle(steamId, resolvedSize);

            if (handle == 0)
            {
                manager._resolvedNoAvatar.Add(key);
                onReady?.Invoke(steamId, null);
                return;
            }

            if (handle == -1)
            {
                if (!manager._pending.TryGetValue(steamId, out var list))
                {
                    list = new List<PendingAvatarRequest>();
                    manager._pending[steamId] = list;
                }
                list.Add(new PendingAvatarRequest { Size = resolvedSize, Callback = onReady });
                return;
            }

            if (SteamAvatarConverter.TryConvertImageHandleToTexture(handle, out var texture))
            {
                manager._avatarCache[key] = texture;
                onReady?.Invoke(steamId, texture);
                AvatarReady?.Invoke(steamId, texture);
            }
            else
            {
                manager._resolvedNoAvatar.Add(key);
                onReady?.Invoke(steamId, null);
            }
        }

        private void OnAvatarImageLoaded(AvatarImageLoaded_t callback)
        {
            CSteamID steamId = callback.m_steamID;

            if (!_pending.TryGetValue(steamId, out var requests))
                return;

            _pending.Remove(steamId);

            List<PendingAvatarRequest> stillPending = null;

            foreach (var request in requests)
            {
                var key = (steamId, request.Size);
                int handle = GetAvatarHandle(steamId, request.Size);

                if (handle == -1)
                {
                    stillPending ??= new List<PendingAvatarRequest>();
                    stillPending.Add(request);
                    continue;
                }

                if (handle > 0 && SteamAvatarConverter.TryConvertImageHandleToTexture(handle, out var texture))
                {
                    _avatarCache[key] = texture;
                    request.Callback?.Invoke(steamId, texture);
                    AvatarReady?.Invoke(steamId, texture);
                }
                else
                {
                    _resolvedNoAvatar.Add(key);
                    request.Callback?.Invoke(steamId, null);
                }
            }

            if (stillPending != null)
                _pending[steamId] = stillPending;
        }

        private static int GetAvatarHandle(CSteamID steamId, SteamAvatarSize size)
        {
            switch (size)
            {
                case SteamAvatarSize.Small:
                    return SteamFriends.GetSmallFriendAvatar(steamId);
                case SteamAvatarSize.Large:
                    return SteamFriends.GetLargeFriendAvatar(steamId);
                default:
                    return SteamFriends.GetMediumFriendAvatar(steamId);
            }
        }

        private static bool WarnIfNotInitialized()
        {
            if (SteamManager.Initialized)
                return true;

            if (!s_warnedNotInitialized)
            {
                s_warnedNotInitialized = true;
                Debug.LogWarning("[Mailo.Networking.Steam] SteamManager is not initialized. Steam identity calls will return fallback values.");
            }

            return false;
        }
    }
}
