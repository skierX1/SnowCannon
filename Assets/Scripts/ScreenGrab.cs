using System;
using System.IO;
using UnityEngine;

namespace SnowCannon
{
    /// <summary>
    /// Grabs the fully composited Game view (including Screen Space Overlay canvases, which a
    /// camera render-texture capture can not see) and writes it as a PNG. Uses only runtime
    /// APIs, so it lives with the game code and can be called from any component. Wrapped in a
    /// try/catch so a capture failure can never break a run.
    /// </summary>
    public static class ScreenGrab
    {
        /// <summary>Discards one grab. Unity's very first CaptureScreenshotAsTexture in a
        /// session can return an unpresented (blank) backbuffer, so callers do a throwaway
        /// warm-up grab before the first shot they actually keep.</summary>
        public static void WarmUp()
        {
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
            catch { }
        }

        public static void Capture(string path)
        {
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null) { Debug.Log("[ScreenGrab] null texture for " + path); return; }
                byte[] png = ImageConversion.EncodeToPNG(tex);
                File.WriteAllBytes(path, png);
                UnityEngine.Object.Destroy(tex);
                Debug.Log("[ScreenGrab] wrote " + path + " (" + png.Length + " bytes)");
            }
            catch (Exception e)
            {
                Debug.Log("[ScreenGrab] failed: " + e.Message);
            }
        }
    }
}
