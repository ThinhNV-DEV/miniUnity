// Unity 6 bindings pass strings to and from native code as UTF-16 spans (UnityEngine.Bindings.ManagedSpanWrapper) instead of as
// marshalled strings, so the decompiled managed side of e.g. LayerMask.NameToLayer / LayerToName goes through these helpers and
// two *_Injected externs. MiniUnity's "native" side keeps a string as an HGlobal UTF-16 buffer, which GetStringAndDispose frees.
using System;
using System.Runtime.InteropServices;

namespace UnityEngine.Bindings
{
    internal readonly unsafe ref partial struct ManagedSpanWrapper
    {
        public ManagedSpanWrapper(void* begin, int length) { this.begin = begin; this.length = length; }

        internal static ManagedSpanWrapper Allocate(string s)
        {
            if (s == null) return default;
            var p = (char*)Marshal.AllocHGlobal(Math.Max(1, s.Length) * sizeof(char));
            s.AsSpan().CopyTo(new Span<char>(p, s.Length));
            return new ManagedSpanWrapper(p, s.Length);
        }

        internal string ToManagedString() => begin == null ? null : new string((char*)begin, 0, length);
    }

    internal unsafe partial class StringMarshaller
    {
        /// <summary>Unity: null → no span; "" → a non-null span of length 0; anything else is pinned by the caller.</summary>
        public static bool TryMarshalEmptyOrNullString(string s, ref ManagedSpanWrapper managedSpanWrapper)
        {
            if (s == null) { managedSpanWrapper = default; return true; }
            if (s.Length == 0) { managedSpanWrapper = new ManagedSpanWrapper((void*)1, 0); return true; }
            return false;
        }
    }

    internal unsafe ref partial struct OutStringMarshaller
    {
        public static string GetStringAndDispose(ManagedSpanWrapper managedSpan)
        {
            if (managedSpan.length == 0) return managedSpan.begin == null ? null : "";
            var s = managedSpan.ToManagedString();
            Marshal.FreeHGlobal((IntPtr)managedSpan.begin);
            return s;
        }
    }
}

namespace MiniUnity
{
    public static unsafe partial class Native
    {
        internal static int LayerMask_NameToLayer_Injected(ref UnityEngine.Bindings.ManagedSpanWrapper layerName) => Layers.NameToLayer(layerName.ToManagedString());
        internal static void LayerMask_LayerToName_Injected(int layer, out UnityEngine.Bindings.ManagedSpanWrapper ret) => ret = UnityEngine.Bindings.ManagedSpanWrapper.Allocate(Layers.LayerToName(layer));
    }
}
