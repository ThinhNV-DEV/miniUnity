using System;
using MiniUnity;
using MiniUnity.Yaml;

namespace UnityEngine
{
    public partial class Object
    {
        /// <summary>Counts edits that can change what the physics scene sees (native properties, transforms, hierarchy,
        /// components, active / enabled, layers). Physics queries skip re-syncing the physics scene while it is unchanged.</summary>
        internal static int s_SceneEdits;
        internal static void SceneEdited() => s_SceneEdits++;
        /// <summary>Counts this object's native property sets (and re-deserializations): caches derived from them key on it.</summary>
        internal int m_NVersion;

        internal YNode NGet(string path)
        {
            if (m_Native == null) { ThrowIfFakeNull(); return null; }
            if (path.IndexOf('.') < 0) return m_Native[path];
            YNode cur = m_Native;
            foreach (var part in path.Split('.'))
            {
                if (cur is not YMap m) return null;
                cur = m[part];
                if (cur == null) return null;
            }
            return cur;
        }
        internal void NSet(string path, YNode v)
        {
            ThrowIfFakeNull();
            s_SceneEdits++; m_NVersion++;
            m_Native ??= new YMap();
            var parts = path.Split('.');
            YMap cur = m_Native;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (cur[parts[i]] is not YMap next) { next = new YMap(); cur[parts[i]] = next; }
                cur = next;
            }
            var old = cur[parts[^1]];
            if (old is YMap om && v is YMap nm) nm.Flow = om.Flow;
            cur[parts[^1]] = v;
        }
        internal float NFloat(string p, float def = 0) => NGet(p) is YScalar s && s.Value.Length > 0 ? s.F : def;
        internal int NInt(string p, int def = 0) => NGet(p) is YScalar s && s.Value.Length > 0 ? (int)s.L : def;
        internal long NLong(string p, long def = 0) => NGet(p) is YScalar s && s.Value.Length > 0 ? s.L : def;
        internal bool NBool(string p, bool def = false) => NGet(p) is YScalar s && s.Value.Length > 0 ? s.L != 0 : def;
        internal string NStr(string p) => (NGet(p) as YScalar)?.Value ?? "";
        internal void NSetF(string p, float v) => NSet(p, YScalar.Of(v));
        internal void NSetD(string p, double v) => NSet(p, YScalar.Of(v));
        internal void NSetI(string p, long v) => NSet(p, YScalar.Of(v));
        internal void NSetB(string p, bool v) => NSet(p, YScalar.Of(v));
        internal void NSetS(string p, string v) => NSet(p, new YScalar(v));
        internal Color NColor(string p) => Serializer.ReadColor(NGet(p));
        internal void NSetColor(string p, Color c) => NSet(p, Serializer.Col(c));
        internal Vector2 NVec2(string p) => Serializer.ReadV2(NGet(p));
        internal void NSetVec2(string p, Vector2 v) => NSet(p, Serializer.Vec(v));
        internal Vector3 NVec3(string p) => Serializer.ReadV3(NGet(p));
        internal void NSetVec3(string p, Vector3 v) => NSet(p, Serializer.Vec(v));
        internal T NRef<T>(string p) where T : Object
        {
            var n = NGet(p) as YRef;
            if (n == null) return null;
            if (n.Raw) { var o = AssetLoader.ResolveExternal(n); return o as T; }
            return n.Target as T;
        }
        internal void NSetRef(string p, Object o) => NSet(p, new YRef(o));
    }
}
