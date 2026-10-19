// What the "graphics device" reports to scripts that pick a rendering path from it (PostProcessing v2, image effects): no GPU
// (GraphicsDeviceType.Null, no compute shaders), the formats the software renderer can hold, no stereo / XR, no dynamic
// resolution, and the camera state those paths read and write (depthTextureMode, jittered projection flags).
using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine
{
    public partial class SystemInfo
    {
        public static GraphicsDeviceType graphicsDeviceType => GraphicsDeviceType.Null;
        public static bool supportsComputeShaders => false;
        public static bool supportsMotionVectors => true;
        public static bool supports3DRenderTextures => true;
        public static bool usesLoadStoreActions => false;
        public static bool usesReversedZBuffer => false;
        public static bool graphicsUVStartsAtTop => false;
        public static int graphicsShaderLevel => 45;
        public static int supportedRenderTargetCount => 8;
        public static CopyTextureSupport copyTextureSupport => CopyTextureSupport.Basic;
        public static OperatingSystemFamily operatingSystemFamily =>
            OperatingSystem.IsWindows() ? OperatingSystemFamily.Windows : OperatingSystem.IsMacOS() ? OperatingSystemFamily.MacOSX : OperatingSystemFamily.Linux;
        public static bool SupportsRenderTextureFormat(RenderTextureFormat format) => true;
        public static bool SupportsTextureFormat(TextureFormat format) => true;
        public static bool IsFormatSupported(GraphicsFormat format, FormatUsage usage) => format != GraphicsFormat.None;
        public static GraphicsFormat GetCompatibleFormat(GraphicsFormat format, FormatUsage usage) => format;
        public static GraphicsFormat GetGraphicsFormat(DefaultFormat format) => format switch
        {
            DefaultFormat.HDR => GraphicsFormat.R16G16B16A16_SFloat,
            DefaultFormat.DepthStencil => GraphicsFormat.D24_UNorm_S8_UInt,
            _ => QualitySettings.activeColorSpace == ColorSpace.Linear ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm,
        };
    }

    public partial class Camera
    {
        DepthTextureMode m_DepthTextureMode;
        bool m_UseJitteredForTransparent = true, m_AllowDynamicResolution;
        public DepthTextureMode depthTextureMode { get => m_DepthTextureMode; set => m_DepthTextureMode = value; }
        public bool useJitteredProjectionMatrixForTransparentRendering { get => m_UseJitteredForTransparent; set => m_UseJitteredForTransparent = value; }
        /// <summary>The projection is always derived from the camera's settings (no jitter), so the non-jittered one is the same.</summary>
        public Matrix4x4 nonJitteredProjectionMatrix { get => projectionMatrix; set { } }
        public void ResetProjectionMatrix() { }
        public bool allowDynamicResolution { get => m_AllowDynamicResolution; set => m_AllowDynamicResolution = value; }
        public int scaledPixelWidth => pixelWidth;
        public int scaledPixelHeight => pixelHeight;
        public bool stereoEnabled => false;
        public bool stereoActive => false;
        public MonoOrStereoscopicEye stereoActiveEye => MonoOrStereoscopicEye.Mono;
        public StereoTargetEyeMask stereoTargetEye { get => StereoTargetEyeMask.Both; set { } }
        public RenderingPath renderingPath { get => RenderingPath.UsePlayerSettings; set { } }
        /// <summary>Forward: the software renderer has no deferred path.</summary>
        public RenderingPath actualRenderingPath => RenderingPath.Forward;
    }

    public partial class RenderTexture
    {
        // settings scripts read back (a 2D colour buffer without random write, mips or dynamic scaling)
        bool m_RandomWrite, m_DynamicScale, m_GenerateMips;
        int m_VolumeDepth = 1;
        public override TextureDimension dimension { get => TextureDimension.Tex2D; set { } }
        public VRTextureUsage vrUsage { get => VRTextureUsage.None; set { } }
        public RenderTextureMemoryless memorylessMode { get => RenderTextureMemoryless.None; set { } }
        public GraphicsFormat stencilFormat { get => GraphicsFormat.None; set { } }
        public GraphicsFormat depthStencilFormat { get => GetDepthStencilFormatLegacy(m_Depth, false); set { } }
        public int volumeDepth { get => m_VolumeDepth; set => m_VolumeDepth = Math.Max(1, value); }
        public bool bindTextureMS { get => false; set { } }
        public bool enableRandomWrite { get => m_RandomWrite; set => m_RandomWrite = value; }
        public bool useDynamicScale { get => m_DynamicScale; set => m_DynamicScale = value; }
        public bool generateMips { get => m_GenerateMips; set => m_GenerateMips = value; }
        public bool isPowerOfTwo { get => (m_RW & (m_RW - 1)) == 0 && (m_RH & (m_RH - 1)) == 0; set { } }
        public bool isCubemap { get => false; set { } }
        public bool isVolume { get => false; set { } }
        public RenderBuffer colorBuffer => default;
        public RenderBuffer depthBuffer => default;
        public void GenerateMips() { }
        public void ResolveAntiAliasedSurface() { }
        public void ResolveAntiAliasedSurface(RenderTexture target) { }
        public void SetGlobalShaderProperty(string propertyName) => Shader.SetGlobalTexture(Shader.PropertyToID(propertyName), this);
        public Vector2 GetTexelOffset() => Vector2.zero;
        public static bool SupportsStencil(RenderTexture rt) => rt == null || rt.m_Depth >= 24;

        internal static GraphicsFormat GetDepthStencilFormatLegacy(int depthBits, GraphicsFormat colorFormat) => GetDepthStencilFormatLegacy(depthBits, false);
        internal static GraphicsFormat GetDepthStencilFormatLegacy(int depthBits, RenderTextureFormat format) => GetDepthStencilFormatLegacy(depthBits, format == RenderTextureFormat.Shadowmap);
        internal static GraphicsFormat GetDepthStencilFormatLegacy(int depthBits, DefaultFormat format) => GetDepthStencilFormatLegacy(depthBits, format == DefaultFormat.Shadow);
        internal static GraphicsFormat GetDepthStencilFormatLegacy(int depthBits, bool requestedShadowMap) =>
            depthBits <= 0 ? GraphicsFormat.None : depthBits <= 16 ? GraphicsFormat.D16_UNorm : depthBits <= 24 ? GraphicsFormat.D24_UNorm_S8_UInt : GraphicsFormat.D32_SFloat_S8_UInt;
    }

    public static partial class ScalableBufferManager
    {
        public static float widthScaleFactor => 1;
        public static float heightScaleFactor => 1;
        public static void ResizeBuffers(float widthScale, float heightScale) { }
    }

    public partial class GL
    {
        /// <summary>The software renderer uses OpenGL conventions (z in -1..1, no flip): the GPU matrix is the projection.</summary>
        public static Matrix4x4 GetGPUProjectionMatrix(Matrix4x4 proj, bool renderIntoTexture) => proj;
    }
}

namespace UnityEngine.Experimental.Rendering
{
    /// <summary>Format conversions for the common colour / depth formats (Unity's table, restricted to what render
    /// textures and textures here use; anything else maps to None / the RGBA8 fallback).</summary>
    public partial class GraphicsFormatUtility
    {
        public static GraphicsFormat GetGraphicsFormat(RenderTextureFormat format, RenderTextureReadWrite readWrite) =>
            GetGraphicsFormat(format, readWrite == RenderTextureReadWrite.sRGB || (readWrite == RenderTextureReadWrite.Default && QualitySettings.activeColorSpace == ColorSpace.Linear));
        public static GraphicsFormat GetGraphicsFormat(RenderTextureFormat format, bool isSRGB) => format switch
        {
            RenderTextureFormat.ARGB32 or RenderTextureFormat.Default or RenderTextureFormat.BGRA32 => isSRGB ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm,
            RenderTextureFormat.ARGBHalf or RenderTextureFormat.DefaultHDR => GraphicsFormat.R16G16B16A16_SFloat,
            RenderTextureFormat.ARGBFloat => GraphicsFormat.R32G32B32A32_SFloat,
            RenderTextureFormat.RGB111110Float => GraphicsFormat.B10G11R11_UFloatPack32,
            RenderTextureFormat.RGHalf => GraphicsFormat.R16G16_SFloat,
            RenderTextureFormat.RGFloat => GraphicsFormat.R32G32_SFloat,
            RenderTextureFormat.RHalf => GraphicsFormat.R16_SFloat,
            RenderTextureFormat.RFloat => GraphicsFormat.R32_SFloat,
            RenderTextureFormat.R8 => GraphicsFormat.R8_UNorm,
            RenderTextureFormat.RG16 => GraphicsFormat.R8G8_UNorm,
            RenderTextureFormat.R16 => GraphicsFormat.R16_UNorm,
            RenderTextureFormat.ARGB2101010 => GraphicsFormat.A2B10G10R10_UNormPack32,
            RenderTextureFormat.ARGB64 => GraphicsFormat.R16G16B16A16_UNorm,
            RenderTextureFormat.Depth => GraphicsFormat.None,
            RenderTextureFormat.Shadowmap => GraphicsFormat.None,
            _ => isSRGB ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm,
        };
        public static GraphicsFormat GetGraphicsFormat(TextureFormat format, bool isSRGB) => format switch
        {
            TextureFormat.Alpha8 => GraphicsFormat.R8_UNorm,
            TextureFormat.R8 => GraphicsFormat.R8_UNorm,
            TextureFormat.RGB24 => isSRGB ? GraphicsFormat.R8G8B8_SRGB : GraphicsFormat.R8G8B8_UNorm,
            TextureFormat.RGBAHalf => GraphicsFormat.R16G16B16A16_SFloat,
            TextureFormat.RGBAFloat => GraphicsFormat.R32G32B32A32_SFloat,
            TextureFormat.RHalf => GraphicsFormat.R16_SFloat,
            TextureFormat.RFloat => GraphicsFormat.R32_SFloat,
            _ => isSRGB ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm,
        };
        public static RenderTextureFormat GetRenderTextureFormat(GraphicsFormat format) => format switch
        {
            GraphicsFormat.R16G16B16A16_SFloat => RenderTextureFormat.ARGBHalf,
            GraphicsFormat.R32G32B32A32_SFloat => RenderTextureFormat.ARGBFloat,
            GraphicsFormat.B10G11R11_UFloatPack32 => RenderTextureFormat.RGB111110Float,
            GraphicsFormat.R16G16_SFloat => RenderTextureFormat.RGHalf,
            GraphicsFormat.R32G32_SFloat => RenderTextureFormat.RGFloat,
            GraphicsFormat.R16_SFloat => RenderTextureFormat.RHalf,
            GraphicsFormat.R32_SFloat => RenderTextureFormat.RFloat,
            GraphicsFormat.R8_UNorm => RenderTextureFormat.R8,
            GraphicsFormat.R8G8_UNorm => RenderTextureFormat.RG16,
            GraphicsFormat.None => RenderTextureFormat.Depth,
            _ => RenderTextureFormat.ARGB32,
        };
        public static TextureFormat GetTextureFormat(GraphicsFormat format) => format switch
        {
            GraphicsFormat.R8_UNorm => TextureFormat.R8,
            GraphicsFormat.R8G8B8_SRGB or GraphicsFormat.R8G8B8_UNorm => TextureFormat.RGB24,
            GraphicsFormat.R16G16B16A16_SFloat => TextureFormat.RGBAHalf,
            GraphicsFormat.R32G32B32A32_SFloat => TextureFormat.RGBAFloat,
            GraphicsFormat.R16_SFloat => TextureFormat.RHalf,
            GraphicsFormat.R32_SFloat => TextureFormat.RFloat,
            _ => TextureFormat.RGBA32,
        };
        public static bool IsSRGBFormat(GraphicsFormat format) => format.ToString().Contains("SRGB");
        public static GraphicsFormat GetSRGBFormat(GraphicsFormat format) => Enum.TryParse<GraphicsFormat>(format.ToString().Replace("_UNorm", "_SRGB"), out var f) ? f : format;
        public static GraphicsFormat GetLinearFormat(GraphicsFormat format) => Enum.TryParse<GraphicsFormat>(format.ToString().Replace("_SRGB", "_UNorm"), out var f) ? f : format;
        public static bool HasAlphaChannel(GraphicsFormat format) { var n = format.ToString(); return n.Contains("A") && (n.StartsWith("R8G8B8A8") || n.StartsWith("B8G8R8A8") || n.StartsWith("R16G16B16A16") || n.StartsWith("R32G32B32A32") || n.StartsWith("A2") || n.Contains("RGBA")); }
        public static bool HasAlphaChannel(TextureFormat format) => format is TextureFormat.Alpha8 or TextureFormat.ARGB32 or TextureFormat.RGBA32 or TextureFormat.BGRA32 or TextureFormat.RGBAHalf or TextureFormat.RGBAFloat or TextureFormat.ARGB4444 or TextureFormat.RGBA4444 or TextureFormat.DXT5 or TextureFormat.ETC2_RGBA8 or TextureFormat.ASTC_4x4;
        public static bool IsCompressedFormat(GraphicsFormat format) { var n = format.ToString(); return n.Contains("DXT") || n.Contains("BC") || n.Contains("ETC") || n.Contains("ASTC") || n.Contains("PVRTC") || n.Contains("EAC"); }
        public static bool IsCompressedFormat(TextureFormat format) { var n = format.ToString(); return n.Contains("DXT") || n.Contains("BC") || n.Contains("ETC") || n.Contains("ASTC") || n.Contains("PVRTC") || n.Contains("EAC"); }
        public static int GetDepthBits(GraphicsFormat format) { var n = format.ToString(); return n.StartsWith("D16") ? 16 : n.StartsWith("D24") ? 24 : n.StartsWith("D32") ? 32 : 0; }
        public static bool IsDepthFormat(GraphicsFormat format) { var n = format.ToString(); return n.StartsWith("D16") || n.StartsWith("D24") || n.StartsWith("D32"); }
        public static GraphicsFormat GetDepthStencilFormat(int minimumDepthBits, int minimumStencilBits) =>
            minimumDepthBits == 0 && minimumStencilBits == 0 ? GraphicsFormat.None : minimumStencilBits > 0 || minimumDepthBits > 16 ? GraphicsFormat.D24_UNorm_S8_UInt : GraphicsFormat.D16_UNorm;
    }
}
