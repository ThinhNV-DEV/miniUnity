// Unity 6 API the hand-written engine classes did not have in 2022.3. Each of these classes is hand-written (config/stubs.manual.cfg
// or a member of the same name in Engine/Core), so the generated stub does not carry the new overloads either.
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine
{
    public partial class SystemInfo
    {
        // FormatUsage became GraphicsFormatUsage; same answers as the FormatUsage overloads in Render/GraphicsCaps.cs
        public static bool IsFormatSupported(GraphicsFormat format, GraphicsFormatUsage usage) => format != GraphicsFormat.None;
        public static GraphicsFormat GetCompatibleFormat(GraphicsFormat format, GraphicsFormatUsage usage) => format;
    }

    public partial class RenderTexture
    {
        // what RenderTextureDescriptor (decompiled) asks when a color or depth format is set
        internal static GraphicsFormat GetDepthStencilFormatLegacy(int depthBits, ShadowSamplingMode shadowSamplingMode) =>
            GetDepthStencilFormatLegacy(depthBits, shadowSamplingMode != ShadowSamplingMode.None);
        internal static ShadowSamplingMode GetShadowSamplingModeForFormat(RenderTextureFormat format) =>
            format == RenderTextureFormat.Shadowmap ? ShadowSamplingMode.CompareDepths : ShadowSamplingMode.None;
        internal static ShadowSamplingMode GetShadowSamplingModeForFormat(DefaultFormat format) =>
            format == DefaultFormat.Shadow ? ShadowSamplingMode.CompareDepths : ShadowSamplingMode.None;
    }

    public partial class RectTransform
    {
        /// <summary>Unity 6: whether a change of this rect sends OnRectTransformDimensionsChange to its children (LayoutGroup sets it).</summary>
        public bool sendChildDimensionsChange { get; set; }
    }
}

namespace UnityEngine.TextCore.LowLevel
{
    public partial class FontEngine
    {
        // Unity 6 point sizes are float (FaceInfo.pointSize). MiniUnity samples at whole sizes, as every TMP font asset does.
        public static FontEngineError LoadFontFace(string filePath, float pointSize, int faceIndex) => LoadFontFace(filePath, Mathf.RoundToInt(pointSize));
        public static FontEngineError LoadFontFace(byte[] sourceFontFile, float pointSize, int faceIndex) => FontEngineError.Invalid_File;
        public static FontEngineError LoadFontFace(Font font, float pointSize, int faceIndex) => LoadFontFace(font, Mathf.RoundToInt(pointSize));
        public static FontEngineError LoadFontFace(string familyName, string styleName, float pointSize) => FontEngineError.Invalid_File;
    }
}

namespace UnityEngine.Rendering
{
    public partial class GraphicsSettings
    {
        // Unity 6: settings a render pipeline registers per type (URP's shader and texture resources). MiniUnity draws with its own
        // software renderer and never runs a pipeline's render loop, so none is registered - Unity's answer for a pipeline without them.
        public static T GetRenderPipelineSettings<T>() where T : class, IRenderPipelineGraphicsSettings => null;
        public static bool TryGetRenderPipelineSettings<T>(out T settings) where T : class, IRenderPipelineGraphicsSettings { settings = null; return false; }
    }
}

namespace UnityEngine.InputForUI
{
    internal static partial class EventProvider
    {
        // the Input System package installs itself as the UI event source from a [RuntimeInitializeOnLoadMethod]; MiniUnity feeds
        // uGUI from its own input simulation, so the provider is kept, not run
        static IEventProviderImpl s_Provider;
        internal static void SetInputSystemProvider(IEventProviderImpl impl) => s_Provider = impl;
        public static IEventProviderImpl provider => s_Provider;
    }
}

namespace UnityEditor
{
    public partial class EditorUtility
    {
        // Unity 6.3 replaces InstanceIDToObject; an EntityId is the instance id here (Versioned/6000/EntityId.cs)
        public static UnityEngine.Object EntityIdToObject(UnityEngine.EntityId entityId) => UnityEngine.Object.FindObjectFromInstanceID(entityId);
    }
}
