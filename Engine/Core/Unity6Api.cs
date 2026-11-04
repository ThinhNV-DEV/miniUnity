// Unity 6 additions to hand-written engine types, used by the Unity 6 packages (uGUI 2.x with TextMeshPro, Timeline,
// Cinemachine) and the decompiled managed types. Compiled only against Unity 6 stubs (UNITY_6000_0_OR_NEWER, from
// Engine/UnityVersion.props); the 2022.3 build is unchanged.
#if UNITY_6000_0_OR_NEWER
using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine
{
    public partial class SystemInfo
    {
        // Unity 6 takes GraphicsFormatUsage where 2022.3 took FormatUsage
        public static bool IsFormatSupported(GraphicsFormat format, GraphicsFormatUsage usage) => format != GraphicsFormat.None;
        public static GraphicsFormat GetCompatibleFormat(GraphicsFormat format, GraphicsFormatUsage usage) => format;
    }

    public partial class RenderTexture
    {
        internal static GraphicsFormat GetDepthStencilFormatLegacy(int depthBits, ShadowSamplingMode shadowSamplingMode) =>
            GetDepthStencilFormatLegacy(depthBits, shadowSamplingMode != ShadowSamplingMode.None);
    }

    public sealed partial class RectTransform
    {
        /// <summary>Unity 6 native flag: LayoutGroup sets it to receive OnRectTransformDimensionsChange from its children.</summary>
        internal bool sendChildDimensionsChange { get; set; }
    }

#if UNITY_6000_2_OR_NEWER
    // EntityId (Unity 6.2+) replaces the int instance ID in the API; MiniUnity's ids stay ints, wrapped here.
    public partial struct EntityId
    {
        int m_Data;
        public static EntityId None => default;
        public bool IsValid() => m_Data != 0;
        public override bool Equals(object obj) => obj is EntityId e && e.m_Data == m_Data;
        public bool Equals(EntityId other) => other.m_Data == m_Data;
        public bool Equals(int other) => other == m_Data;
        public int CompareTo(EntityId other) => m_Data.CompareTo(other.m_Data);
        public override int GetHashCode() => m_Data;
        public override string ToString() => m_Data.ToString();
        public string ToString(string format) => m_Data.ToString(format);
        internal static EntityId From(int input) => new EntityId { m_Data = input };
        internal static EntityId From(ulong input) => new EntityId { m_Data = unchecked((int)input) };
        internal static EntityId Parse(string input) => From(int.Parse(input));
        internal int GetRawData() => m_Data;
        public static bool operator ==(EntityId left, EntityId right) => left.m_Data == right.m_Data;
        public static bool operator !=(EntityId left, EntityId right) => left.m_Data != right.m_Data;
        public static bool operator <(EntityId left, EntityId right) => left.m_Data < right.m_Data;
        public static bool operator >(EntityId left, EntityId right) => left.m_Data > right.m_Data;
        public static bool operator <=(EntityId left, EntityId right) => left.m_Data <= right.m_Data;
        public static bool operator >=(EntityId left, EntityId right) => left.m_Data >= right.m_Data;
        public static implicit operator int(EntityId entityId) => entityId.m_Data;
        public static implicit operator EntityId(int intValue) => From(intValue);
        // InstanceID is Unity's older wrapper; MiniUnity does not model it
        public static implicit operator EntityId(InstanceID entityId) => throw new NotImplementedException("UnityEngine.EntityId.op_Implicit(InstanceID)");
        public static implicit operator InstanceID(EntityId entityId) => throw new NotImplementedException("UnityEngine.EntityId.op_Implicit(EntityId)");
    }

    public partial class Object
    {
        public EntityId GetEntityId() => EntityId.From(m_InstanceID);
    }
#endif
}

namespace UnityEngine.TextCore.LowLevel
{
    public partial class FontEngine
    {
        // Unity 6 point sizes are floats (FaceInfo.pointSize); MiniUnity rasterizes at whole point sizes
        static int Pt(float pointSize) => (int)Math.Round(pointSize);
        public static FontEngineError LoadFontFace(string filePath, float pointSize) => LoadFontFace(filePath, Pt(pointSize));
        public static FontEngineError LoadFontFace(string filePath, float pointSize, int faceIndex) => LoadFontFace(filePath, Pt(pointSize), faceIndex);
        public static FontEngineError LoadFontFace(byte[] sourceFontFile, float pointSize) => LoadFontFace(sourceFontFile, Pt(pointSize));
        public static FontEngineError LoadFontFace(byte[] sourceFontFile, float pointSize, int faceIndex) => LoadFontFace(sourceFontFile, Pt(pointSize), faceIndex);
        public static FontEngineError LoadFontFace(Font font, float pointSize) => LoadFontFace(font, Pt(pointSize));
        public static FontEngineError LoadFontFace(Font font, float pointSize, int faceIndex) => LoadFontFace(font, Pt(pointSize), faceIndex);
        public static FontEngineError LoadFontFace(string familyName, string styleName, float pointSize) => LoadFontFace(familyName, styleName, Pt(pointSize));
        public static FontEngineError SetFaceSize(float pointSize) => SetFaceSize(Pt(pointSize));
    }
}
#endif
