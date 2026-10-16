// Graphics.Blit and image effects: a full-screen pass from a texture into a RenderTexture (null = the screen buffer), bilinear,
// in linear space. With a material whose shader is registered in ImageEffects.Registry (C#, by shader name) every destination
// pixel runs that effect with the source bound as _MainTex; other materials copy (and say so once). Camera OnRenderImage chains
// (Play mode, when a camera is rasterised: screenshots, record, Camera.Render) hand their scripts the camera image this way.
using System;
using System.Collections.Generic;
using System.Reflection;
using MiniUnity;
using UnityEngine;

namespace MiniUnity
{
    /// <summary>One destination pixel of an image effect: (u, v) in the destination, the source as a linear sampler, the material
    /// (properties) and pass; returns linear RGBA.</summary>
    public delegate Vector4 ImageEffect(TexSampler source, Material material, int pass, float u, float v);

    public static class ImageEffects
    {
        public static readonly Dictionary<string, ImageEffect> Registry = new(StringComparer.Ordinal)
        {
            ["Hidden/BlitCopy"] = (s, m, p, u, v) => Sample(s, u, v),
            ["Unlit/Texture"] = (s, m, p, u, v) => Sample(s, u, v),
            ["Unlit/Color"] = (s, m, p, u, v) => { var c = m.color.linear; return new Vector4(c.r, c.g, c.b, c.a); },
        };
        static readonly HashSet<string> warned = new();

        public static Vector4 Sample(TexSampler s, float u, float v)
        {
            if (s == null) return new Vector4(0, 0, 0, 1);
            s.Sample(u, v, out float r, out float g, out float b, out float a);
            return new Vector4(r, g, b, a);
        }

        public static void Blit(Texture source, RenderTexture dest, Material mat, int pass, Vector2 scale, Vector2 offset)
        {
            dest ??= UIRenderer.ScreenBuffer;
            dest.EnsureBuffers();
            if (mat != null && source != null) mat.mainTexture = source;
            var src = TextureData.Sampler(source);
            ImageEffect fx = null;
            if (mat != null)
            {
                var name = UIRenderer.ShaderName(mat.shader) ?? "";
                if (!Registry.TryGetValue(name, out fx) && warned.Add(name))
                    UnityEngine.Debug.LogWarning($"[MiniUnity] Graphics.Blit: shader '{name}' has no C# image effect (ImageEffects.Registry); copying the source");
            }
            int W = dest.m_RW, H = dest.m_RH; var col = dest.m_Color; bool srgb = dest.m_SRGB;
            System.Threading.Tasks.Parallel.For(0, H, y =>
            {
                float v = (y + 0.5f) / H * scale.y + offset.y;
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W * scale.x + offset.x;
                    var c = fx != null ? fx(src, mat, pass, u, v) : Sample(src, u, v);
                    int o = (y * W + x) * 4;
                    col[o] = srgb ? TextureData.LinearToSrgbByte(c.x) : Byte(c.x);
                    col[o + 1] = srgb ? TextureData.LinearToSrgbByte(c.y) : Byte(c.y);
                    col[o + 2] = srgb ? TextureData.LinearToSrgbByte(c.z) : Byte(c.z);
                    col[o + 3] = Byte(c.w);
                }
            });
            if (dest.m_SS > 1)
            {
                // keep the supersampled surface consistent with the resolved image
                int s = dest.m_SS, SW = dest.SurfW;
                for (int y = 0; y < dest.SurfH; y++)
                    for (int x = 0; x < SW; x++)
                        Buffer.BlockCopy(col, ((y / s) * W + x / s) * 4, dest.m_SurfColor, (y * SW + x) * 4, 4);
            }
            dest.m_RVersion++;
        }

        static byte Byte(float f) => (byte)Math.Clamp((int)MathF.Round(f * 255f), 0, 255);

        static readonly Dictionary<Type, MethodInfo> renderImage = new();

        /// <summary>The camera's OnRenderImage scripts, in component order, each fed the previous one's output.</summary>
        internal static void RunOnRenderImage(Camera cam, RenderTexture target)
        {
            if (!Play.IsPlaying || !Play.RenderCallbacks) return;
            var chain = new List<(MonoBehaviour mb, MethodInfo m)>();
            foreach (var c in cam.gameObject.m_Components)
            {
                if (c is not MonoBehaviour mb || mb.m_Destroyed || !mb.m_Enabled) continue;
                var t = mb.GetType();
                if (!renderImage.TryGetValue(t, out var m))
                {
                    for (var k = t; k != null && k != typeof(MonoBehaviour) && m == null; k = k.BaseType)
                        m = k.GetMethod("OnRenderImage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, new[] { typeof(RenderTexture), typeof(RenderTexture) }, null);
                    renderImage[t] = m;
                }
                if (m != null) chain.Add((mb, m));
            }
            if (chain.Count == 0) return;
            var source = new RenderTexture(target.m_RW, target.m_RH, 0);
            source.EnsureBuffers();
            Buffer.BlockCopy(target.m_Color, 0, source.m_Color, 0, Math.Min(target.m_Color.Length, source.m_Color.Length));
            source.m_RVersion++;
            for (int i = 0; i < chain.Count; i++)
            {
                var dst = i == chain.Count - 1 ? target : new RenderTexture(target.m_RW, target.m_RH, 0);
                try { chain[i].m.Invoke(chain[i].mb, new object[] { source, dst }); }
                catch (TargetInvocationException e) { Play.Report(e.InnerException ?? e, chain[i].mb.GetType().Name + ".OnRenderImage"); }
                source = dst;
            }
            target.m_RVersion++;
        }
    }
}

namespace UnityEngine
{
    public partial class Graphics
    {
        public static void Blit(Texture source, RenderTexture dest) => ImageEffects.Blit(source, dest, null, -1, Vector2.one, Vector2.zero);
        public static void Blit(Texture source, RenderTexture dest, int sourceDepthSlice, int destDepthSlice) => Blit(source, dest);
        public static void Blit(Texture source, RenderTexture dest, Vector2 scale, Vector2 offset) => ImageEffects.Blit(source, dest, null, -1, scale, offset);
        public static void Blit(Texture source, RenderTexture dest, Vector2 scale, Vector2 offset, int sourceDepthSlice, int destDepthSlice) => Blit(source, dest, scale, offset);
        public static void Blit(Texture source, RenderTexture dest, Material mat, int pass) => ImageEffects.Blit(source, dest, mat, pass, Vector2.one, Vector2.zero);
        public static void Blit(Texture source, RenderTexture dest, Material mat, int pass, int destDepthSlice) => Blit(source, dest, mat, pass);
        public static void Blit(Texture source, RenderTexture dest, Material mat) => Blit(source, dest, mat, -1);
        public static void Blit(Texture source, Material mat, int pass) => Blit(source, RenderTexture.active, mat, pass);
        public static void Blit(Texture source, Material mat, int pass, int destDepthSlice) => Blit(source, mat, pass);
        public static void Blit(Texture source, Material mat) => Blit(source, mat, -1);
        public static void BlitMultiTap(Texture source, RenderTexture dest, Material mat, params Vector2[] offsets) => Blit(source, dest, mat, -1);
        public static void BlitMultiTap(Texture source, RenderTexture dest, Material mat, int destDepthSlice, params Vector2[] offsets) => Blit(source, dest, mat, -1);
    }
}
