// Small hand implementations of engine members hit at runtime (auto-excluded from the stubs).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.Experimental.Rendering
{
    public partial class GraphicsFormatUtility
    {
        public static bool IsCrunchFormat(TextureFormat format) => format == TextureFormat.DXT1Crunched || format == TextureFormat.DXT5Crunched || format == TextureFormat.ETC_RGB4Crunched || format == TextureFormat.ETC2_RGBA8Crunched;
    }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    public static partial class UnsafeUtility
    {
        public static unsafe void MemCpy(void* destination, void* source, long size) => System.Buffer.MemoryCopy(source, destination, size, size);
        public static unsafe void MemSet(void* destination, byte value, long size) => new System.Span<byte>(destination, (int)size).Fill(value);
        public static unsafe void MemClear(void* destination, long size) => new System.Span<byte>(destination, (int)size).Clear();
    }
}

namespace UnityEngine.Animations
{
    /// <summary>Clip curves (animated clip parameters) are not evaluated: no video uses them.</summary>
    public static partial class AnimationPlayableExtensions
    {
        public static void SetAnimatedProperties<U>(this U playable, AnimationClip clip) where U : struct, UnityEngine.Playables.IPlayable { }
    }
}

namespace UnityEngine.Sprites
{
    /// <summary>Sprite UV / border data (sprites here are never atlas-packed: textureRect == rect).</summary>
    public sealed partial class DataUtility
    {
        public static Vector4 GetOuterUV(Sprite sprite)
        {
            var t = sprite?.texture; if (t == null) return new Vector4(0, 0, 1, 1);
            var r = sprite.textureRect; float w = t.width, h = t.height;
            return new Vector4(r.xMin / w, r.yMin / h, r.xMax / w, r.yMax / h);
        }
        public static Vector4 GetInnerUV(Sprite sprite)
        {
            var t = sprite?.texture; if (t == null) return new Vector4(0, 0, 1, 1);
            var r = sprite.textureRect; var b = sprite.border; float w = t.width, h = t.height;
            return new Vector4((r.xMin + b.x) / w, (r.yMin + b.y) / h, (r.xMax - b.z) / w, (r.yMax - b.w) / h);
        }
        public static Vector4 GetPadding(Sprite sprite) => Vector4.zero;
        public static Vector2 GetMinSize(Sprite sprite) => sprite == null ? Vector2.zero : new Vector2(sprite.border.x + sprite.border.z, sprite.border.y + sprite.border.w);
    }
}

namespace UnityEditor
{
    public sealed partial class EditorSettings
    {
        public static SpritePackerMode spritePackerMode { get => SpritePackerMode.Disabled; set { } }
    }
}

namespace UnityEngine
{
    internal sealed partial class UnityString
    {
        public static string Format(string fmt, params object[] args) => string.Format(System.Globalization.CultureInfo.InvariantCulture.NumberFormat, fmt, args);
    }
}

namespace UnityEditor
{
    public sealed partial class MenuItem
    {
        public MenuItem(string itemName) : this(itemName, false) { }
        public MenuItem(string itemName, bool isValidateFunction) : this(itemName, isValidateFunction, isValidateFunction ? -1 : 1000) { }
        public MenuItem(string itemName, bool isValidateFunction, int priority) { menuItem = itemName; validate = isValidateFunction; this.priority = priority; }
        // internal in Unity; packages with access to the editor's internals use them
        public MenuItem(string itemName, bool isValidateFunction, int priority, bool internalMenu) : this(itemName, isValidateFunction, priority) { }
        public MenuItem(string itemName, bool isValidateFunction, int priority, bool internalMenu, string[] editorModes) : this(itemName, isValidateFunction, priority) { this.editorModes = editorModes; }
        internal MenuItem() { }
    }
}

namespace UnityEditor
{
    /// <summary>128-bit asset GUID, printed as 32 lowercase hex digits like Unity.</summary>
    public partial struct GUID
    {
        string m_Hex;
        public GUID(string hexRepresentation) { m_Hex = (hexRepresentation ?? "").ToLowerInvariant(); }
        string H => string.IsNullOrEmpty(m_Hex) ? "00000000000000000000000000000000" : m_Hex;
        public static bool operator ==(GUID x, GUID y) => x.H == y.H;
        public static bool operator !=(GUID x, GUID y) => x.H != y.H;
        public static bool operator <(GUID x, GUID y) => string.CompareOrdinal(x.H, y.H) < 0;
        public static bool operator >(GUID x, GUID y) => string.CompareOrdinal(x.H, y.H) > 0;
        public override bool Equals(object obj) => obj is GUID g && g == this;
        public bool Equals(GUID obj) => obj == this;
        public override int GetHashCode() => H.GetHashCode();
        public int CompareTo(object obj) => obj is GUID g ? CompareTo(g) : 1;
        public int CompareTo(GUID rhs) => string.CompareOrdinal(H, rhs.H);
        public bool Empty() => H == "00000000000000000000000000000000";
        public bool ParseExact(string hex) { if (hex == null || hex.Length != 32) return false; m_Hex = hex.ToLowerInvariant(); return true; }
        public static bool TryParse(string hex, out GUID result) { result = default; return result.ParseExact(hex); }
        public static GUID Generate() => new GUID(System.Guid.NewGuid().ToString("N"));
        public override string ToString() => H;
    }
}

namespace UnityEngine
{
    /// <summary>Interned string id (Timeline exposed references, PlayableBinding names). Serialized as its string.</summary>
    public partial struct PropertyName
    {
        internal int id;
        internal int conflictIndex;
        static readonly System.Collections.Generic.List<string> s_Names = new() { "" };
        static readonly System.Collections.Generic.Dictionary<string, int> s_Ids = new() { [""] = 0 };
        public PropertyName(string name) { name ??= ""; if (!s_Ids.TryGetValue(name, out id)) { id = s_Names.Count; s_Names.Add(name); s_Ids[name] = id; } conflictIndex = 0; }
        public PropertyName(PropertyName other) { id = other.id; conflictIndex = other.conflictIndex; }
        public PropertyName(int id) { this.id = id; conflictIndex = 0; }
        public static bool IsNullOrEmpty(PropertyName prop) => prop.id == 0;
        public static bool operator ==(PropertyName lhs, PropertyName rhs) => lhs.id == rhs.id;
        public static bool operator !=(PropertyName lhs, PropertyName rhs) => lhs.id != rhs.id;
        public override int GetHashCode() => id;
        public override bool Equals(object other) => other is PropertyName p && p.id == id;
        public bool Equals(PropertyName other) => other.id == id;
        public static implicit operator PropertyName(string name) => new PropertyName(name);
        public static implicit operator PropertyName(int id) => new PropertyName(id);
        public override string ToString() => id >= 0 && id < s_Names.Count ? s_Names[id] : "";
    }
}

namespace UnityEditor.Compilation
{
    public static partial class CompilationPipeline
    {
        internal static bool IsScriptDebugInfoEnabled() => true;
    }
}
