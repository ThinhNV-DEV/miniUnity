// Camera MSAA: Unity's built-in pipeline renders a camera with allowMSAA through a multisampled intermediate when the quality
// level has antiAliasing > 1 and the camera renders to the screen or to a script-made texture through its HDR intermediate — so video frames
// and thumbnails captured with QualitySettings "Ultra" (2×) have partially covered edge pixels. MiniUnity rasterizes 2 samples
// per pixel (D3D standard 2× positions), shades once per pixel at its centre and resolves the samples after the camera.
// Supersampled render textures (antiAliasing ≥ 2 on the texture itself) keep their own path. MU_MSAA=0 turns it off.
using System;
using UnityEngine;

namespace MiniUnity
{
    internal static class Msaa
    {
        /// <summary>Sample offsets from the pixel centre in the surface's y-up rows: the D3D 2× pattern ((+4, +4) and (-4, -4) / 16) as it
        /// lands in a flipped render target. This diagonal matches the project's Unity captures (thumbnails 01 / 02 / 13 / 94: 56–59 dB;
        /// the other diagonal gives 44–58 dB).</summary>
        public static readonly float[] OX = { .25f, -.25f }, OY = { .25f, -.25f };

        public static bool Enabled = Environment.GetEnvironmentVariable("MU_MSAA") != "0";

        /// <summary>Samples the camera renders with (1 or 2) into this target.</summary>
        public static int CameraSamples(Camera cam, RenderTexture target)
        {
            if (!Enabled || target.m_SS > 1 || !cam.allowMSAA) return 1;
            if (QualitySettings.antiAliasing < 2) return 1;
            // observed in the project's Unity captures: the screen and render textures created by script (thumbnails, frame
            // capture) get the quality level's MSAA through the HDR intermediate; a camera rendering into a RenderTexture asset
            // (PixelFlow / BlockExit stage textures, antiAliasing 1) stays single-sampled
            var rt = cam.targetTexture;
            bool intermediate = rt == null || (rt.m_Asset == null && cam.allowHDR);
            return intermediate ? 2 : 1;
        }

        /// <summary>Coverage mask (bit per sample) of a pixel for a screen triangle given by its edge setup; top-left rule per sample.</summary>
        public static int Coverage(float sx0, float sy0, float sx1, float sy1, float sx2, float sy2, float ia, bool tl0, bool tl1, bool tl2, int x, int y, int samples)
        {
            int mask = 0;
            for (int s = 0; s < samples; s++)
            {
                float px = x + .5f + (samples > 1 ? OX[s] : 0), py = y + .5f + (samples > 1 ? OY[s] : 0);
                float w0 = ((sx2 - sx1) * (py - sy1) - (sy2 - sy1) * (px - sx1)) * ia;
                float w1 = ((sx0 - sx2) * (py - sy2) - (sy0 - sy2) * (px - sx2)) * ia;
                float w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                if ((w0 == 0 && !tl0) || (w1 == 0 && !tl1) || (w2 == 0 && !tl2)) continue;
                mask |= 1 << s;
            }
            return mask;
        }

        /// <summary>Same without the top-left rule (the 3D rasterizers test w ≥ 0, like their single-sample path).</summary>
        public static int CoverageInclusive(float sx0, float sy0, float sx1, float sy1, float sx2, float sy2, float ia, int x, int y, int samples)
        {
            int mask = 0;
            for (int s = 0; s < samples; s++)
            {
                float px = x + .5f + (samples > 1 ? OX[s] : 0), py = y + .5f + (samples > 1 ? OY[s] : 0);
                float w0 = ((sx2 - sx1) * (py - sy1) - (sy2 - sy1) * (px - sx1)) * ia;
                float w1 = ((sx0 - sx2) * (py - sy2) - (sy0 - sy2) * (px - sx2)) * ia;
                if (w0 < 0 || w1 < 0 || 1 - w0 - w1 < 0) continue;
                mask |= 1 << s;
            }
            return mask;
        }

        /// <summary>Depth (screen z) of the plane at a sample of the pixel.</summary>
        public static float DepthAt(float sx0, float sy0, float sx1, float sy1, float sx2, float sy2, float ia, float z0, float z1, float z2, int x, int y, int s, int samples)
        {
            float px = x + .5f + (samples > 1 ? OX[s] : 0), py = y + .5f + (samples > 1 ? OY[s] : 0);
            float w0 = ((sx2 - sx1) * (py - sy1) - (sy2 - sy1) * (px - sx1)) * ia;
            float w1 = ((sx0 - sx2) * (py - sy2) - (sy0 - sy2) * (px - sx2)) * ia;
            return w0 * z0 + w1 * z1 + (1 - w0 - w1) * z2;
        }
    }
}
