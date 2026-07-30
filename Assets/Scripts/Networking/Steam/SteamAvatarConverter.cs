using Steamworks;
using UnityEngine;

namespace Mailo.Networking.Steam
{
    internal static class SteamAvatarConverter
    {
        public static bool TryConvertImageHandleToTexture(int imageHandle, out Texture2D texture)
        {
            texture = null;

            if (imageHandle <= 0)
                return false;

            if (!SteamUtils.GetImageSize(imageHandle, out uint width, out uint height) || width == 0 || height == 0)
                return false;

            byte[] buffer = new byte[width * height * 4];
            if (!SteamUtils.GetImageRGBA(imageHandle, buffer, buffer.Length))
                return false;

            // Steam returns RGBA rows top-down; Texture2D.LoadRawTextureData expects bottom-up.
            FlipRowsVertically(buffer, (int)width, (int)height);

            texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false, false);
            texture.LoadRawTextureData(buffer);
            texture.Apply();
            return true;
        }

        private static void FlipRowsVertically(byte[] buffer, int width, int height)
        {
            int stride = width * 4;
            byte[] row = new byte[stride];

            for (int y = 0; y < height / 2; y++)
            {
                int top = y * stride;
                int bottom = (height - 1 - y) * stride;

                System.Buffer.BlockCopy(buffer, top, row, 0, stride);
                System.Buffer.BlockCopy(buffer, bottom, buffer, top, stride);
                System.Buffer.BlockCopy(row, 0, buffer, bottom, stride);
            }
        }
    }
}
