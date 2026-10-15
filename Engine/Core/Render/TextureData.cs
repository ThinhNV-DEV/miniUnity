// Pixel storage for textures: imported image files decode lazily (PNG), YAML textures (font atlases) decode their
// "image data" stream, and code-made textures keep the Color[] they were given. The renderer samples a linear-space
// float copy (sRGB textures decoded per texel before filtering, like the GPU).
using System;
using System.Collections.Generic;
using System.Globalization;
using MiniUnity.Yaml;
using UnityEngine;

namespace MiniUnity
{
    public sealed class TexSampler
    {
        public int w, h;
        public float[] px;          // linear RGBA, row-major bottom-up
        public bool clamp = true;
        public bool point;

        /// <summary>Bilinear sample at uv (0..1, Unity convention: texel centres at (i + .5) / size).</summary>
        public void Sample(float u, float v, out float r, out float g, out float b, out float a)
        {
            if (w == 0) { r = g = b = a = 1; return; }
            float x = u * w - 0.5f, y = v * h - 0.5f;
            if (point)
            {
                int ix = Wrap((int)MathF.Floor(x + 0.5f), w), iy = Wrap((int)MathF.Floor(y + 0.5f), h);
                int o = (iy * w + ix) * 4; r = px[o]; g = px[o + 1]; b = px[o + 2]; a = px[o + 3]; return;
            }
            int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
            float fx = x - x0, fy = y - y0;
            int xa = Wrap(x0, w), xb = Wrap(x0 + 1, w), ya = Wrap(y0, h), yb = Wrap(y0 + 1, h);
            int o00 = (ya * w + xa) * 4, o10 = (ya * w + xb) * 4, o01 = (yb * w + xa) * 4, o11 = (yb * w + xb) * 4;
            float w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
            r = px[o00] * w00 + px[o10] * w10 + px[o01] * w01 + px[o11] * w11;
            g = px[o00 + 1] * w00 + px[o10 + 1] * w10 + px[o01 + 1] * w01 + px[o11 + 1] * w11;
            b = px[o00 + 2] * w00 + px[o10 + 2] * w10 + px[o01 + 2] * w01 + px[o11 + 2] * w11;
            a = px[o00 + 3] * w00 + px[o10 + 3] * w10 + px[o01 + 3] * w01 + px[o11 + 3] * w11;
        }

        /// <summary>Alpha only (SDF atlases).</summary>
        public float SampleA(float u, float v)
        {
            if (w == 0) return 1;
            float x = u * w - 0.5f, y = v * h - 0.5f;
            int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
            float fx = x - x0, fy = y - y0;
            int xa = Wrap(x0, w), xb = Wrap(x0 + 1, w), ya = Wrap(y0, h), yb = Wrap(y0 + 1, h);
            return px[(ya * w + xa) * 4 + 3] * (1 - fx) * (1 - fy) + px[(ya * w + xb) * 4 + 3] * fx * (1 - fy)
                 + px[(yb * w + xa) * 4 + 3] * (1 - fx) * fy + px[(yb * w + xb) * 4 + 3] * fx * fy;
        }

        int Wrap(int i, int n) => clamp ? (i < 0 ? 0 : i >= n ? n - 1 : i) : ((i % n) + n) % n;
    }

    public static class TextureData
    {
        public static readonly float[] SrgbToLinear = new float[256];
        static readonly byte[] s_LinToSrgb = new byte[4097];
        static TextureData()
        {
            for (int i = 0; i < 256; i++) SrgbToLinear[i] = Mathf.GammaToLinearSpace(i / 255f);
            for (int i = 0; i <= 4096; i++) s_LinToSrgb[i] = (byte)Math.Clamp((int)MathF.Round(Mathf.LinearToGammaSpace(i / 4096f) * 255f), 0, 255);
        }
        /// <summary>Linear 0..1 → sRGB byte (exact curve, rounded).</summary>
        public static byte LinearToSrgbByte(float v)
        {
            if (v <= 0) return 0;
            if (v >= 1) return 255;
            // exact for dark values, LUT elsewhere
            if (v < 0.01f) return DarkByte(v);
            return s_LinToSrgb[(int)MathF.Round(v * 4096f)];
        }

        internal static byte ExactDarkByte(float v) => (byte)Math.Clamp((int)MathF.Round(Mathf.LinearToGammaSpace(v) * 255f), 0, 255);

        // ExactDarkByte is a non-decreasing step function of v on (0, 0.01): s_DarkSteps[k] is the bit pattern of the first float
        // where it reaches k + 1 (found by bisection over the float bit patterns, which are ordered like positive floats).
        // Within DarkGuard patterns of a step the exact curve is evaluated, so a pow result a few ulps off near a step cannot
        // change the byte; everywhere else the step count is the byte.
        static int[] s_DarkSteps;
        const int DarkGuard = 64;
        static int[] DarkSteps()
        {
            int lo = 1, hi = BitConverter.SingleToInt32Bits(0.01f) - 1;
            int top = ExactDarkByte(BitConverter.Int32BitsToSingle(hi));
            var steps = new int[top];
            for (int k = 1; k <= top; k++)
            {
                int a = lo, b = hi;
                while (a < b) { int m = a + (b - a) / 2; if (ExactDarkByte(BitConverter.Int32BitsToSingle(m)) >= k) b = m; else a = m + 1; }
                steps[k - 1] = a;
            }
            return steps;
        }
        static byte DarkByte(float v)
        {
            var steps = s_DarkSteps ??= DarkSteps();
            int bits = BitConverter.SingleToInt32Bits(v);
            int k = 0;
            while (k < steps.Length && steps[k] <= bits) k++;
            if ((k > 0 && bits - steps[k - 1] < DarkGuard) || (k < steps.Length && steps[k] - bits <= DarkGuard)) return ExactDarkByte(v);
            return (byte)k;
        }

        /// <summary>Checks DarkByte against the exact curve for every float in [from, to) bit patterns (0 = all of (0, 0.01)); returns mismatches.</summary>
        internal static long VerifyDarkBytes()
        {
            int hi = BitConverter.SingleToInt32Bits(0.01f);
            long bad = 0;
            System.Threading.Tasks.Parallel.For(0, 1024, chunk =>
            {
                long local = 0;
                int a = 1 + (int)((long)(hi - 1) * chunk / 1024), b = 1 + (int)((long)(hi - 1) * (chunk + 1) / 1024);
                for (int i = a; i < b; i++) { float f = BitConverter.Int32BitsToSingle(i); if (DarkByte(f) != ExactDarkByte(f)) local++; }
                System.Threading.Interlocked.Add(ref bad, local);
            });
            return bad;
        }

        /// <summary>RGBA8 pixels of a texture (rows bottom-up) or null.</summary>
        public static byte[] RGBA8(Texture2D t, out int w, out int h)
        {
            w = t.width; h = t.height;
            if (t.m_Pixels != null)
            {
                var b = new byte[t.m_Pixels.Length * 4];
                for (int i = 0; i < t.m_Pixels.Length; i++) { Color32 c = t.m_Pixels[i]; b[i * 4] = c.r; b[i * 4 + 1] = c.g; b[i * 4 + 2] = c.b; b[i * 4 + 3] = c.a; }
                return b;
            }
            if (t.m_SourceFile != null && Png.TryDecodeFile(t.m_SourceFile, out w, out h, out var rgba)) return rgba;
            if (t.m_Native != null && DecodeYaml(t, out w, out h) is byte[] y) return y;
            return null;
        }

        /// <summary>Unity YAML Texture2D: "image data" + _typelessdata (hex). Alpha8 / RGB24 / RGBA32 / ARGB32 / R8.</summary>
        static byte[] DecodeYaml(Texture2D t, out int w, out int h)
        {
            w = Serializer.Int(t.m_Native["m_Width"], 0); h = Serializer.Int(t.m_Native["m_Height"], 0);
            if (t.m_Native["_typelessdata"] is not YScalar hex || w <= 0 || h <= 0) return null;
            var s = hex.Value;
            var raw = new byte[s.Length / 2];
            for (int i = 0; i < raw.Length; i++) raw[i] = (byte)((Hex(s[2 * i]) << 4) | Hex(s[2 * i + 1]));
            int fmt = Serializer.Int(t.m_Native["m_TextureFormat"], 4);
            var o = new byte[w * h * 4];
            int n = w * h;
            for (int i = 0; i < n; i++)
            {
                switch (fmt)
                {
                    case 1: o[i * 4] = o[i * 4 + 1] = o[i * 4 + 2] = 255; o[i * 4 + 3] = i < raw.Length ? raw[i] : (byte)0; break;           // Alpha8
                    case 63: o[i * 4] = o[i * 4 + 1] = o[i * 4 + 2] = i < raw.Length ? raw[i] : (byte)0; o[i * 4 + 3] = 255; break;         // R8
                    case 3: o[i * 4] = raw[i * 3]; o[i * 4 + 1] = raw[i * 3 + 1]; o[i * 4 + 2] = raw[i * 3 + 2]; o[i * 4 + 3] = 255; break;   // RGB24
                    case 5: o[i * 4 + 3] = raw[i * 4]; o[i * 4] = raw[i * 4 + 1]; o[i * 4 + 1] = raw[i * 4 + 2]; o[i * 4 + 2] = raw[i * 4 + 3]; break; // ARGB32
                    default: Array.Copy(raw, i * 4, o, i * 4, 4); break;                                                                   // RGBA32
                }
            }
            return o;
        }
        static int Hex(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

        static readonly Dictionary<Texture, (int version, TexSampler s)> s_Cache = new(ReferenceEqualityComparer.Instance);

        public static TexSampler Sampler(Texture t)
        {
            if (t == null) return null;
            if (t is RenderTexture rt) return rt.AsSampler();
            if (t is not Texture2D t2) return null;
            if (s_Cache.TryGetValue(t, out var c) && c.version == t2.m_Version) return c.s;
            var rgba = RGBA8(t2, out int w, out int h);
            var s = new TexSampler { w = rgba != null ? w : 0, h = rgba != null ? h : 0, clamp = t.wrapMode != TextureWrapMode.Repeat, point = t.filterMode == FilterMode.Point };
            if (rgba != null)
            {
                bool srgb = !t2.m_Linear;
                s.px = new float[w * h * 4];
                for (int i = 0; i < w * h; i++)
                {
                    if (srgb) { s.px[i * 4] = SrgbToLinear[rgba[i * 4]]; s.px[i * 4 + 1] = SrgbToLinear[rgba[i * 4 + 1]]; s.px[i * 4 + 2] = SrgbToLinear[rgba[i * 4 + 2]]; }
                    else { s.px[i * 4] = rgba[i * 4] / 255f; s.px[i * 4 + 1] = rgba[i * 4 + 1] / 255f; s.px[i * 4 + 2] = rgba[i * 4 + 2] / 255f; }
                    s.px[i * 4 + 3] = rgba[i * 4 + 3] / 255f;
                }
            }
            s_Cache[t] = (t2.m_Version, s);
            return s;
        }
    }
}
