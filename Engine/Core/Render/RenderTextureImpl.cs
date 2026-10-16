// RenderTexture: an 8-bit sRGB colour buffer (stored encoded, like ARGB32 sRGB targets: blending decodes, blends in
// linear and re-encodes per draw) plus an 8-bit stencil buffer. Camera.Render draws into it; ReadPixels copies out.
using System;
using MiniUnity;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine
{
    public partial class RenderTexture
    {
        internal int m_RW = 256, m_RH = 256, m_Depth = 24;
        internal bool m_SRGB = true;
        internal byte[] m_Color;     // RGBA8, rows bottom-up; RGB sRGB-encoded when m_SRGB
        internal byte[] m_Stencil;
        // render surface: m_SS× the resolution when antiAliasing > 1 (MSAA approximated by supersampling, resolved in linear space)
        internal int m_AA = 1, m_SS = 1;
        internal byte[] m_SurfColor, m_SurfStencil;
        internal float[] m_SurfDepth;
        // camera MSAA (2 samples, Msaa.cs): while a camera renders through a multisampled intermediate, m_SurfColor / Stencil /
        // Depth hold sample 0 and these sample 1; EndMultisample resolves the two into the surface
        internal int m_MS = 1;
        internal byte[] m_MsColor, m_MsStencil;
        internal float[] m_MsDepth;
        internal byte[] SampleColor(int s) => s == 0 ? m_SurfColor : m_MsColor;
        internal byte[] SampleStencil(int s) => s == 0 ? m_SurfStencil : m_MsStencil;
        internal float[] SampleDepth(int s) => s == 0 ? m_SurfDepth : m_MsDepth;
        internal void BeginMultisample()
        {
            int n = SurfW * SurfH;
            if (m_MsColor == null || m_MsColor.Length != n * 4) { m_MsColor = new byte[n * 4]; m_MsStencil = new byte[n]; m_MsDepth = new float[n]; }
            Buffer.BlockCopy(m_SurfColor, 0, m_MsColor, 0, n * 4);
            Buffer.BlockCopy(m_SurfStencil, 0, m_MsStencil, 0, n);
            Buffer.BlockCopy(m_SurfDepth, 0, m_MsDepth, 0, n * 4);
            m_MS = 2;
        }
        /// <summary>Box resolve of the two samples in linear space (an sRGB / HDR intermediate resolves linear values).</summary>
        internal void EndMultisample()
        {
            if (m_MS == 1) return;
            m_MS = 1;
            var a = m_SurfColor; var b = m_MsColor; bool srgb = m_SRGB;
            System.Threading.Tasks.Parallel.For(0, SurfH, y =>
            {
                int o0 = y * SurfW * 4, o1 = o0 + SurfW * 4;
                for (int o = o0; o < o1; o += 4)
                {
                    if (a[o] == b[o] && a[o + 1] == b[o + 1] && a[o + 2] == b[o + 2] && a[o + 3] == b[o + 3]) continue;
                    for (int k = 0; k < 3; k++)
                        a[o + k] = srgb ? TextureData.LinearToSrgbByte((TextureData.SrgbToLinear[a[o + k]] + TextureData.SrgbToLinear[b[o + k]]) * .5f)
                                        : (byte)((a[o + k] + b[o + k] + 1) >> 1);
                    a[o + 3] = (byte)((a[o + 3] + b[o + 3] + 1) >> 1);
                }
            });
        }
        internal int SurfW => m_RW * m_SS;
        internal int SurfH => m_RH * m_SS;
        internal int m_RVersion;
        static RenderTexture s_Active;

        protected internal RenderTexture() { }
        public RenderTexture(RenderTextureDescriptor desc) { }
        public RenderTexture(RenderTexture textureToCopy) { if (textureToCopy != null) Init(textureToCopy.m_RW, textureToCopy.m_RH, textureToCopy.m_Depth, textureToCopy.m_SRGB); }
        public RenderTexture(int width, int height, int depth, DefaultFormat format) { Init(width, height, depth, format != DefaultFormat.HDR); }
        public RenderTexture(int width, int height, int depth, GraphicsFormat format) { Init(width, height, depth, format.ToString().Contains("SRGB")); }
        public RenderTexture(int width, int height, int depth, GraphicsFormat format, int mipCount) : this(width, height, depth, format) { }
        public RenderTexture(int width, int height, GraphicsFormat colorFormat, GraphicsFormat depthStencilFormat, int mipCount) { Init(width, height, 24, colorFormat.ToString().Contains("SRGB")); }
        public RenderTexture(int width, int height, GraphicsFormat colorFormat, GraphicsFormat depthStencilFormat) { Init(width, height, 24, colorFormat.ToString().Contains("SRGB")); }
        public RenderTexture(int width, int height, int depth, RenderTextureFormat format, RenderTextureReadWrite readWrite) { Init(width, height, depth, readWrite != RenderTextureReadWrite.Linear); }
        public RenderTexture(int width, int height, int depth, RenderTextureFormat format) { Init(width, height, depth, true); }
        public RenderTexture(int width, int height, int depth) { Init(width, height, depth, true); }
        public RenderTexture(int width, int height, int depth, RenderTextureFormat format, int mipCount) { Init(width, height, depth, true); }

        /// <summary>Serialized fields of a .renderTexture asset (RenderTexture 84), as Unity writes them.</summary>
        void WriteNative()
        {
            if (NGet("m_ImageContentsHash") == null)
            {
                NSet("m_ImageContentsHash", new MiniUnity.Yaml.YMap().Add("serializedVersion", "2").Add("Hash", "00000000000000000000000000000000"));
                NSetI("m_ForcedFallbackFormat", 4); NSetI("m_DownscaleFallback", 0); NSetI("m_IsAlphaChannelOptional", 0); NSetI("serializedVersion", 5);
            }
            NSetI("m_Width", m_RW); NSetI("m_Height", m_RH); NSetI("m_AntiAliasing", m_AA); NSetI("m_MipCount", -1);
            NSetI("m_DepthStencilFormat", m_Depth >= 24 ? 94 : m_Depth > 0 ? 90 : 0); NSetI("m_ColorFormat", m_SRGB ? 4 : 8);
            NSetI("m_MipMap", 0); NSetI("m_GenerateMips", 1); NSetB("m_SRGB", m_SRGB); NSetI("m_UseDynamicScale", 0); NSetI("m_BindMS", 0);
            NSetI("m_EnableCompatibleFormat", 1); NSetI("m_EnableRandomWrite", 0);
            if (NGet("m_TextureSettings") == null)
                NSet("m_TextureSettings", new MiniUnity.Yaml.YMap().Add("serializedVersion", "2").Add("m_FilterMode", "1").Add("m_Aniso", "0").Add("m_MipBias", "0")
                    .Add("m_WrapU", "1").Add("m_WrapV", "1").Add("m_WrapW", "1"));
            NSetI("m_Dimension", 2); NSetI("m_VolumeDepth", 1); NSetI("m_ShadowSamplingMode", 2);
        }

        /// <summary>A RenderTexture loaded from an asset gets its size and format from the serialized fields.</summary>
        internal void FromNative()
        {
            if (m_Color != null || NGet("m_Width") is not MiniUnity.Yaml.YScalar) return;
            m_AA = Math.Max(1, NInt("m_AntiAliasing", 1));
            Init(NInt("m_Width", 256), NInt("m_Height", 256), NInt("m_DepthStencilFormat", 94) == 0 ? 0 : 24, NBool("m_SRGB", true));
        }

        void Init(int w, int h, int depth, bool srgb)
        {
            m_RW = Math.Max(1, w); m_RH = Math.Max(1, h); m_Depth = depth; m_SRGB = srgb;
            WriteNative();
            m_Color = new byte[m_RW * m_RH * 4]; m_Stencil = new byte[m_RW * m_RH];
            m_SS = m_AA >= 8 ? 3 : m_AA >= 2 ? 2 : 1;
            if (m_SS == 1) { m_SurfColor = m_Color; m_SurfStencil = m_Stencil; }
            else { m_SurfColor = new byte[SurfW * SurfH * 4]; m_SurfStencil = new byte[SurfW * SurfH]; }
            m_SurfDepth = new float[SurfW * SurfH];
            Array.Fill(m_SurfDepth, 1f);
        }
        internal void EnsureBuffers() { FromNative(); if (m_Color == null || m_Color.Length != m_RW * m_RH * 4) Init(m_RW, m_RH, m_Depth, m_SRGB); }

        public override int width { get { FromNative(); return m_RW; } set { m_RW = value; m_Color = null; } }
        public override int height { get { FromNative(); return m_RH; } set { m_RH = value; m_Color = null; } }
        public int depth { get => m_Depth; set => m_Depth = value; }
        public bool sRGB => m_SRGB;
        public int antiAliasing { get => m_AA; set { m_AA = Math.Max(1, value); m_Color = null; } }
        /// <summary>Average the supersampled surface into the colour buffer (no-op without antialiasing).</summary>
        internal void Resolve()
        {
            if (m_SS == 1) return;
            int s = m_SS, SW = SurfW; float inv = 1f / (s * s);
            for (int y = 0; y < m_RH; y++)
                for (int x = 0; x < m_RW; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int j = 0; j < s; j++)
                        for (int i = 0; i < s; i++)
                        {
                            int o = ((y * s + j) * SW + x * s + i) * 4;
                            if (m_SRGB) { r += TextureData.SrgbToLinear[m_SurfColor[o]]; g += TextureData.SrgbToLinear[m_SurfColor[o + 1]]; b += TextureData.SrgbToLinear[m_SurfColor[o + 2]]; }
                            else { r += m_SurfColor[o] / 255f; g += m_SurfColor[o + 1] / 255f; b += m_SurfColor[o + 2] / 255f; }
                            a += m_SurfColor[o + 3] / 255f;
                        }
                    int d = (y * m_RW + x) * 4;
                    m_Color[d] = m_SRGB ? TextureData.LinearToSrgbByte(r * inv) : (byte)Math.Round(r * inv * 255);
                    m_Color[d + 1] = m_SRGB ? TextureData.LinearToSrgbByte(g * inv) : (byte)Math.Round(g * inv * 255);
                    m_Color[d + 2] = m_SRGB ? TextureData.LinearToSrgbByte(b * inv) : (byte)Math.Round(b * inv * 255);
                    m_Color[d + 3] = (byte)Math.Round(a * inv * 255);
                }
        }
        public bool useMipMap { get => false; set { } }
        public bool autoGenerateMips { get => false; set { } }
        public RenderTextureFormat format { get => RenderTextureFormat.ARGB32; set { } }
        public GraphicsFormat graphicsFormat { get => m_SRGB ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm; set => m_SRGB = value.ToString().Contains("SRGB"); }
        public static RenderTexture active { get => s_Active; set => s_Active = value; }
        public bool Create() { EnsureBuffers(); return true; }
        public void Release() { m_Color = null; m_Stencil = null; }
        public bool IsCreated() => m_Color != null;
        public void DiscardContents() { }
        public void DiscardContents(bool discardColor, bool discardDepth) { }
        public void MarkRestoreExpected() { }
        public static RenderTexture GetTemporary(int width, int height) => new RenderTexture(width, height, 0);
        public static RenderTexture GetTemporary(int width, int height, int depthBuffer) => new RenderTexture(width, height, depthBuffer);
        public static RenderTexture GetTemporary(int width, int height, int depthBuffer, RenderTextureFormat format) => new RenderTexture(width, height, depthBuffer, format);
        public static RenderTexture GetTemporary(int width, int height, int depthBuffer, RenderTextureFormat format, RenderTextureReadWrite readWrite) => new RenderTexture(width, height, depthBuffer, format, readWrite);
        public static void ReleaseTemporary(RenderTexture temp) { }

        /// <summary>Copy a block into a Texture2D (values as stored: sRGB-encoded for sRGB targets – what ReadPixels returns).</summary>
        internal void ReadInto(Texture2D dst, int sx, int sy, int w, int h, int dx, int dy)
        {
            EnsureBuffers();
            for (int y = 0; y < h; y++)
            {
                int yy = sy + y, ty = dy + y;
                if (yy < 0 || yy >= m_RH || ty < 0 || ty >= dst.height) continue;
                for (int x = 0; x < w; x++)
                {
                    int xx = sx + x, tx = dx + x;
                    if (xx < 0 || xx >= m_RW || tx < 0 || tx >= dst.width) continue;
                    int o = (yy * m_RW + xx) * 4;
                    dst.m_Pixels[ty * dst.width + tx] = new Color32(m_Color[o], m_Color[o + 1], m_Color[o + 2], m_Color[o + 3]);
                }
            }
        }

        TexSampler m_Sampler; int m_SamplerVersion = -1;
        internal TexSampler AsSampler()
        {
            EnsureBuffers();
            if (m_Sampler != null && m_SamplerVersion == m_RVersion) return m_Sampler;
            // the previous frame's sampler is refilled in place (draws build their sampler right before rasterizing, none keeps it)
            var s = m_Sampler != null && m_Sampler.w == m_RW && m_Sampler.h == m_RH ? m_Sampler : new TexSampler { w = m_RW, h = m_RH, px = new float[m_RW * m_RH * 4] };
            var px = s.px; var col = m_Color; bool srgb = m_SRGB; int n = m_RW * m_RH;
            System.Threading.Tasks.Parallel.For(0, (n + 65535) / 65536, chunk =>
            {
                for (int i = chunk * 65536, end = Math.Min(n, i + 65536); i < end; i++)
                {
                    for (int c = 0; c < 3; c++) px[i * 4 + c] = srgb ? TextureData.SrgbToLinear[col[i * 4 + c]] : col[i * 4 + c] / 255f;
                    px[i * 4 + 3] = col[i * 4 + 3] / 255f;
                }
            });
            m_Sampler = s; m_SamplerVersion = m_RVersion;
            return s;
        }
    }
}
