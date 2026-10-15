// AnimationClip data for MiniUnity's animation runtime: reads the clip's serialized curves (position / rotation / euler /
// scale / float / PPtr curves, clip settings, events) or curves added with SetCurve, and binds them to a GameObject
// hierarchy (path + class + attribute, like Unity's GenericBinding). Pose = per-binding values blended by weight.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MiniUnity.Yaml;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiniUnity.Anim
{
    /// <summary>A float curve on one animated property (vector / quaternion curves are split into components).</summary>
    public sealed class FloatBinding
    {
        public string Path, Attribute; public int ClassId; public Type ScriptType;
        public AnimationCurve Curve;
        public string Key => $"{Path}|{ClassId}|{ScriptType?.FullName}|{Attribute}";
    }

    public sealed class PPtrBinding
    {
        public string Path, Attribute; public int ClassId; public Type ScriptType;
        public List<(float time, Object value)> Keys = new();
        public string Key => $"{Path}|{ClassId}|{ScriptType?.FullName}|{Attribute}";
    }

    public sealed class ClipData
    {
        public readonly List<FloatBinding> Floats = new();
        public readonly List<PPtrBinding> PPtrs = new();
        public readonly List<AnimationEvent> Events = new();
        public float Length, StartTime, StopTime, FrameRate = 60, CycleOffset;
        public bool Loop, LoopBlend, Legacy;
        public WrapMode WrapMode;
        public bool HasRootCurves;   // the root transform (path "") is animated
        // additive reference pose (AnimationClipSettings): another clip at a time, otherwise this clip's first frame
        public bool HasAdditiveReference; public AnimationClip AdditiveReferenceClip; public float AdditiveReferenceTime;

        static readonly Dictionary<AnimationClip, (int version, ClipData data)> cache = new();

        public static ClipData Of(AnimationClip clip)
        {
            if (clip == null) return null;
            if (cache.TryGetValue(clip, out var c) && c.version == clip.m_Version) return c.data;
            var d = Build(clip);
            cache[clip] = (clip.m_Version, d);
            return d;
        }

        static ClipData Build(AnimationClip clip)
        {
            var d = new ClipData();
            var n = clip.m_Native;
            d.Legacy = clip.NBool("m_Legacy");
            d.FrameRate = clip.NFloat("m_SampleRate", 60);
            d.WrapMode = (WrapMode)clip.NInt("m_WrapMode");
            if (n != null)
            {
                foreach (var e in Seq(n["m_PositionCurves"])) AddVector(d, e, "m_LocalPosition");
                foreach (var e in Seq(n["m_ScaleCurves"])) AddVector(d, e, "m_LocalScale");
                foreach (var e in Seq(n["m_EulerCurves"])) AddVector(d, e, "localEulerAnglesRaw");
                foreach (var e in Seq(n["m_RotationCurves"])) AddQuaternion(d, e);
                foreach (var e in Seq(n["m_FloatCurves"]))
                {
                    if (e is not YMap m) continue;
                    d.Floats.Add(new FloatBinding { Path = Str(m["path"]), Attribute = Str(m["attribute"]), ClassId = Int(m["classID"]), ScriptType = ScriptOf(m["script"]), Curve = Serializer.ReadCurve(m["curve"]) });
                }
                foreach (var e in Seq(n["m_PPtrCurves"]))
                {
                    if (e is not YMap m) continue;
                    var b = new PPtrBinding { Path = Str(m["path"]), Attribute = Str(m["attribute"]), ClassId = Int(m["classID"]), ScriptType = ScriptOf(m["script"]) };
                    foreach (var k in Seq(m["curve"]))
                        if (k is YMap km) b.Keys.Add((Serializer.F(km["time"]), RefOf(km["value"])));
                    b.Keys.Sort((x, y) => x.time.CompareTo(y.time));
                    d.PPtrs.Add(b);
                }
                // clips saved without runtime curves (or with editor curves only)
                if (d.Floats.Count == 0 && d.PPtrs.Count == 0)
                {
                    foreach (var e in Seq(n["m_EditorCurves"]).Concat(Seq(n["m_EulerEditorCurves"])))
                    {
                        if (e is not YMap m) continue;
                        var attr = Str(m["attribute"]);
                        if (attr.StartsWith("localEulerAngles")) attr = "localEulerAnglesRaw" + attr.Substring(attr.IndexOf('.'));
                        d.Floats.Add(new FloatBinding { Path = Str(m["path"]), Attribute = attr, ClassId = Int(m["classID"]), ScriptType = ScriptOf(m["script"]), Curve = Serializer.ReadCurve(m["curve"]) });
                    }
                }
                foreach (var e in Seq(n["m_Events"]))
                {
                    if (e is not YMap m) continue;
                    d.Events.Add(new AnimationEvent
                    {
                        m_Time = Serializer.F(m["time"]), m_FunctionName = Str(m["functionName"]), m_StringParameter = Str(m["data"]),
                        m_ObjectReferenceParameter = RefOf(m["objectReferenceParameter"]), m_FloatParameter = Serializer.F(m["floatParameter"]),
                        m_IntParameter = Int(m["intParameter"]), m_MessageOptions = Int(m["messageOptions"]),
                    });
                }
                if (n["m_AnimationClipSettings"] is YMap s)
                {
                    d.StartTime = Serializer.F(s["m_StartTime"]); d.StopTime = Serializer.F(s["m_StopTime"]);
                    d.Loop = Int(s["m_LoopTime"]) != 0; d.LoopBlend = Int(s["m_LoopBlend"]) != 0; d.CycleOffset = Serializer.F(s["m_CycleOffset"]);
                    d.HasAdditiveReference = Int(s["m_HasAdditiveReferencePose"]) != 0;
                    d.AdditiveReferenceClip = RefOf(s["m_AdditiveReferencePoseClip"]) as AnimationClip;
                    d.AdditiveReferenceTime = Serializer.F(s["m_AdditiveReferencePoseTime"]);
                }
            }
            // curves set from script (AnimationClip.SetCurve)
            foreach (var b in clip.m_ScriptCurves) d.Floats.Add(b);
            foreach (var ev in clip.m_ScriptEvents) d.Events.Add(ev);
            d.Events.Sort((a, b) => a.time.CompareTo(b.time));
            float maxKey = 0;
            foreach (var b in d.Floats) if (b.Curve.length > 0) maxKey = Math.Max(maxKey, b.Curve[b.Curve.length - 1].time);
            foreach (var b in d.PPtrs) if (b.Keys.Count > 0) maxKey = Math.Max(maxKey, b.Keys[^1].time);
            if (d.StopTime <= d.StartTime) { d.StartTime = 0; d.StopTime = maxKey; }
            d.Length = Math.Max(0, d.StopTime - d.StartTime);
            if (d.Legacy && d.WrapMode == WrapMode.Loop) d.Loop = true;
            d.HasRootCurves = d.Floats.Any(b => b.Path == "" && b.ClassId == 4);
            return d;
        }

        static IEnumerable<YNode> Seq(YNode n) => n is YSeq s ? s.Items : Enumerable.Empty<YNode>();
        static string Str(YNode n) => (n as YScalar)?.Value ?? "";
        static int Int(YNode n) => n is YScalar s && s.Value.Length > 0 ? (int)s.L : 0;
        static Object RefOf(YNode n) => n is YRef r ? (r.Raw ? AssetLoader.ResolveExternal(r) : r.Target) : null;
        static Type ScriptOf(YNode n)
        {
            if (n is not YRef r) return null;
            if (r.Raw && !string.IsNullOrEmpty(r.Guid)) return ScriptRegistry.TypeOf(r.Guid);
            return null;
        }

        static void AddVector(ClipData d, YNode e, string attr)
        {
            if (e is not YMap m || m["curve"] is not YMap cm) return;
            var path = Str(m["path"]);
            var curves = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            foreach (var k in Seq(cm["m_Curve"]))
            {
                if (k is not YMap km) continue;
                float t = Serializer.F(km["time"]);
                var v = Serializer.ReadV3(km["value"]); var i = Serializer.ReadV3(km["inSlope"]); var o = Serializer.ReadV3(km["outSlope"]);
                curves[0].m_Keys.Add(new Keyframe(t, v.x, i.x, o.x)); curves[1].m_Keys.Add(new Keyframe(t, v.y, i.y, o.y)); curves[2].m_Keys.Add(new Keyframe(t, v.z, i.z, o.z));
            }
            var pre = (WrapMode)InternalWrap(cm["m_PreInfinity"] == null ? 2 : Int(cm["m_PreInfinity"])); var post = (WrapMode)InternalWrap(cm["m_PostInfinity"] == null ? 2 : Int(cm["m_PostInfinity"]));
            string[] comp = { "x", "y", "z" };
            for (int c = 0; c < 3; c++)
            {
                curves[c].m_PreInfinity = pre; curves[c].m_PostInfinity = post;
                d.Floats.Add(new FloatBinding { Path = path, ClassId = 4, Attribute = attr + "." + comp[c], Curve = curves[c] });
            }
        }

        static void AddQuaternion(ClipData d, YNode e)
        {
            if (e is not YMap m || m["curve"] is not YMap cm) return;
            var path = Str(m["path"]);
            var curves = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            foreach (var k in Seq(cm["m_Curve"]))
            {
                if (k is not YMap km) continue;
                float t = Serializer.F(km["time"]);
                var v = Q(km["value"]); var i = Q(km["inSlope"]); var o = Q(km["outSlope"]);
                for (int c = 0; c < 4; c++) curves[c].m_Keys.Add(new Keyframe(t, v[c], i[c], o[c]));
            }
            var pre = (WrapMode)InternalWrap(cm["m_PreInfinity"] == null ? 2 : Int(cm["m_PreInfinity"])); var post = (WrapMode)InternalWrap(cm["m_PostInfinity"] == null ? 2 : Int(cm["m_PostInfinity"]));
            string[] comp = { "x", "y", "z", "w" };
            for (int c = 0; c < 4; c++)
            {
                curves[c].m_PreInfinity = pre; curves[c].m_PostInfinity = post;
                d.Floats.Add(new FloatBinding { Path = path, ClassId = 4, Attribute = "m_LocalRotation." + comp[c], Curve = curves[c] });
            }
        }

        static float[] Q(YNode n) { var m = n as YMap; return new[] { Serializer.F(m?["x"]), Serializer.F(m?["y"]), Serializer.F(m?["z"]), Serializer.F(m?["w"]) }; }
        // serialized m_PreInfinity / m_PostInfinity: 0 PingPong, 1 Loop, 2 Clamp (see Serializer.ReadCurve)
        static int InternalWrap(int v) => v switch { 0 => (int)WrapMode.PingPong, 1 => (int)WrapMode.Loop, _ => (int)WrapMode.ClampForever };

        /// <summary>Clip-local sample time for a state's normalized time (looping clips wrap, others clamp).</summary>
        public float TimeAt(float normalizedTime, bool loop)
        {
            if (Length <= 0) return StartTime;
            float nt = loop ? normalizedTime - MathF.Floor(normalizedTime) : Math.Clamp(normalizedTime, 0, 1);
            if (loop && normalizedTime > 0 && nt == 0 && normalizedTime == MathF.Floor(normalizedTime)) nt = 0;
            return StartTime + nt * Length;
        }
    }

    // ===================================================================================================== binding to a hierarchy
    /// <summary>Resolved property of the animated hierarchy: read / write accessors, default value for Write Defaults.</summary>
    public sealed class BoundProperty
    {
        public string Key; public Transform Target; public Component Component; public GameObject Go;
        public string Attribute; public bool IsPPtr;
        public Func<float> Get; public Action<float> Set;
        public Func<Object> GetRef; public Action<Object> SetRef;
        public float Default; public Object DefaultRef;
        // transform channels are written together (position / rotation / euler / scale component)
        public int TransformChannel = -1, Component4 = -1;
    }

    public static class Binder
    {
        public static Transform Find(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;
            var t = root;
            foreach (var part in path.Split('/'))
            {
                Transform next = null;
                foreach (var ch in t.m_Children) if (ch.gameObject.name == part) { next = ch; break; }
                if (next == null) return null;
                t = next;
            }
            return t;
        }

        static Type TypeOfClass(int classId, Type script)
        {
            if (classId == 114 && script != null) return script;
            if (classId == 1) return typeof(GameObject);
            if (classId == 4) return typeof(Transform);
            return Serializer.TypeById.TryGetValue(classId, out var t) ? t : null;
        }

        public static BoundProperty Bind(Transform root, string path, int classId, Type script, string attribute, bool pptr)
        {
            var t = Find(root, path);
            if (t == null) return null;
            var bp = new BoundProperty { Target = t, Go = t.gameObject, Attribute = attribute, IsPPtr = pptr };
            var type = TypeOfClass(classId, script);
            if (type == typeof(GameObject))
            {
                if (attribute != "m_IsActive") return null;
                bp.Get = () => t.gameObject.activeSelf ? 1 : 0; bp.Set = v => { bool a = v > 0.5f; if (t.gameObject.activeSelf != a) t.gameObject.SetActive(a); };
                return Finish(bp);
            }
            if (type == typeof(Transform) || (type == typeof(RectTransform) && attribute.StartsWith("m_Local")))
            {
                int ch = attribute.StartsWith("m_LocalPosition") ? 0 : attribute.StartsWith("m_LocalRotation") ? 1 : attribute.StartsWith("localEulerAngles") ? 2 : attribute.StartsWith("m_LocalScale") ? 3 : -1;
                if (ch < 0) return null;
                int c = "xyzw".IndexOf(attribute[^1]);
                bp.TransformChannel = ch; bp.Component4 = c; bp.Component = t;
                bp.Get = ch switch
                {
                    0 => () => t.localPosition[c],
                    1 => () => Quat(t.localRotation, c),
                    2 => () => t.localEulerAngles[c],
                    _ => () => t.localScale[c],
                };
                bp.Set = v => { };   // applied per channel by Pose.Apply
                return Finish(bp);
            }
            if (type == null) return null;
            var comp = t.gameObject.GetComponent(type);
            if (comp == null) return null;
            bp.Component = comp;
            if (pptr)
            {
                var (getR, setR) = RefAccessor(comp, attribute);
                if (getR == null) return null;
                bp.GetRef = getR; bp.SetRef = setR;
                return Finish(bp);
            }
            var (get, set) = FloatAccessor(comp, attribute);
            if (get == null) return null;
            bp.Get = get; bp.Set = set;
            return Finish(bp);
        }

        static BoundProperty Finish(BoundProperty bp)
        {
            if (bp.IsPPtr) bp.DefaultRef = bp.GetRef(); else bp.Default = bp.Get();
            return bp;
        }

        static float Quat(Quaternion q, int c) => c switch { 0 => q.x, 1 => q.y, 2 => q.z, _ => q.w };

        // ------------------------------------------------------------------ component properties
        static readonly Dictionary<(Type, string), string> propertyAlias = new()
        {
            [(typeof(RectTransform), "m_AnchoredPosition")] = "anchoredPosition", [(typeof(RectTransform), "m_SizeDelta")] = "sizeDelta",
            [(typeof(RectTransform), "m_AnchorMin")] = "anchorMin", [(typeof(RectTransform), "m_AnchorMax")] = "anchorMax", [(typeof(RectTransform), "m_Pivot")] = "pivot",
        };

        /// <summary>Accessors for "m_Field" / "m_Field.x" / "field.sub.r" on native (serialized YAML) or managed components.</summary>
        static (Func<float>, Action<float>) FloatAccessor(Component comp, string attribute)
        {
            if (attribute == "m_Enabled" && comp is Behaviour beh) return (() => beh.enabled ? 1 : 0, v => beh.enabled = v > 0.5f);
            if (attribute == "m_Enabled" && comp is Renderer ren) return (() => ren.enabled ? 1 : 0, v => ren.enabled = v > 0.5f);
            var parts = attribute.Split('.');
            // property alias (RectTransform C# state)
            if (propertyAlias.TryGetValue((comp.GetType(), parts[0]), out var prop) || propertyAlias.TryGetValue((comp.GetType().BaseType ?? typeof(object), parts[0]), out prop))
            {
                var pi = comp.GetType().GetProperty(prop);
                if (pi != null) return MemberAccessor(() => pi.GetValue(comp), v => pi.SetValue(comp, v), parts.Skip(1).ToArray());
            }
            if (comp is MonoBehaviour)
            {
                // managed field path (serialized names; m_Color → m_Color field or color property)
                var f = FindField(comp.GetType(), parts[0]);
                if (f != null) return MemberAccessor(() => f.GetValue(comp), v => { f.SetValue(comp, v); NotifyGraphic(comp); }, parts.Skip(1).ToArray());
                return (null, null);
            }
            // native component: serialized YAML node
            var node = comp.NGet(attribute);
            if (node is YScalar)
                return (() => comp.NFloat(attribute), v => comp.NSetF(attribute, v));
            // C# property named like the field without m_ (e.g. Light.intensity)
            var name = parts[0].StartsWith("m_") ? char.ToLowerInvariant(parts[0][2]) + parts[0].Substring(3) : parts[0];
            var p2 = comp.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (p2 != null && p2.CanRead && p2.CanWrite) return MemberAccessor(() => p2.GetValue(comp), v => p2.SetValue(comp, v), parts.Skip(1).ToArray());
            return (() => comp.NFloat(attribute), v => comp.NSetF(attribute, v));
        }

        static void NotifyGraphic(Component comp)
        {
            var m = comp.GetType().GetMethod("SetVerticesDirty", BindingFlags.Instance | BindingFlags.Public);
            try { m?.Invoke(comp, null); } catch { }
        }

        static FieldInfo FindField(Type t, string name)
        {
            for (var c = t; c != null && c != typeof(MonoBehaviour); c = c.BaseType)
            {
                var f = c.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>Float accessor into a (possibly nested struct) member value: bool / int / float / Vector / Color components.</summary>
        static (Func<float>, Action<float>) MemberAccessor(Func<object> get, Action<object> set, string[] sub)
        {
            float Read(object o, int i)
            {
                if (i >= sub.Length) return o switch { float f => f, int n => n, bool b => b ? 1 : 0, double d => (float)d, Enum e => Convert.ToInt32(e), _ => 0 };
                var fi = o?.GetType().GetField(sub[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return fi == null ? 0 : Read(fi.GetValue(o), i + 1);
            }
            object Write(object o, int i, float v)
            {
                if (i >= sub.Length)
                    return o switch { int => (object)(int)MathF.Round(v), bool => v > 0.5f, double => (double)v, Enum e => Enum.ToObject(e.GetType(), (int)MathF.Round(v)), _ => v };
                if (o == null) return null;
                var fi = o.GetType().GetField(sub[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi == null) return o;
                fi.SetValue(o, Write(fi.GetValue(o), i + 1, v));   // boxed struct copy is written back by the caller
                return o;
            }
            return (() => Read(get(), 0), v => set(Write(get(), 0, v)));
        }

        static (Func<Object>, Action<Object>) RefAccessor(Component comp, string attribute)
        {
            if (comp is MonoBehaviour)
            {
                var f = FindField(comp.GetType(), attribute);
                if (f == null || !typeof(Object).IsAssignableFrom(f.FieldType)) return (null, null);
                return (() => f.GetValue(comp) as Object, v => { f.SetValue(comp, v); NotifyGraphic(comp); });
            }
            var name = attribute.StartsWith("m_") ? char.ToLowerInvariant(attribute[2]) + attribute.Substring(3) : attribute;
            var p = comp.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (p != null && p.CanWrite && typeof(Object).IsAssignableFrom(p.PropertyType)) return (() => p.GetValue(comp) as Object, v => p.SetValue(comp, v));
            return (() => comp.NRef<Object>(attribute), v => comp.NSetRef(attribute, v));
        }
    }

    /// <summary>Weighted accumulation of sampled values for one Animator / Animation evaluation.</summary>
    public sealed class Pose
    {
        public readonly Dictionary<string, (float sum, float w)> Floats = new();
        public readonly Dictionary<string, (Object value, float w)> Refs = new();

        public void Add(ClipData clip, float time, float weight)
        {
            if (clip == null || weight <= 0) return;
            // quaternion components: a sample on the other hemisphere than what is accumulated is negated first (q and -q are the
            // same rotation; summing them would cancel out)
            Dictionary<string, (float x, float y, float z, float w, int n)> rot = null;
            foreach (var b in clip.Floats)
            {
                if (!IsRotation(b.Key, out var g, out int ci)) continue;
                rot ??= new();
                rot.TryGetValue(g, out var q);
                float v = b.Curve.Evaluate(time);
                if (ci == 0) q.x = v; else if (ci == 1) q.y = v; else if (ci == 2) q.z = v; else q.w = v;
                q.n++; rot[g] = q;
            }
            HashSet<string> flip = null;
            if (rot != null)
                foreach (var (g, q) in rot)
                {
                    if (q.n < 4) continue;
                    float dot = Sum(g + "x") * q.x + Sum(g + "y") * q.y + Sum(g + "z") * q.z + Sum(g + "w") * q.w;
                    if (dot < 0) (flip ??= new()).Add(g);
                }
            foreach (var b in clip.Floats)
            {
                float v = b.Curve.Evaluate(time);
                var k = b.Key;
                if (flip != null && IsRotation(k, out var g, out _) && flip.Contains(g)) v = -v;
                Floats.TryGetValue(k, out var acc);
                Floats[k] = (acc.sum + v * weight, acc.w + weight);
            }
            foreach (var b in clip.PPtrs)
            {
                if (b.Keys.Count == 0) continue;
                Object v = b.Keys[0].value;
                foreach (var key in b.Keys) { if (key.time <= time + 1e-6f) v = key.value; else break; }
                var k = b.Key;
                if (!Refs.TryGetValue(k, out var acc) || weight > acc.w) Refs[k] = (v, weight);   // discrete: heaviest wins
            }
        }

        public void Clear() { Floats.Clear(); Refs.Clear(); }

        float Sum(string k) => Floats.TryGetValue(k, out var a) ? a.sum : 0;

        /// <summary>"path|4||m_LocalRotation.x" → group "path|4||m_LocalRotation.", component 0..3 (x y z w).</summary>
        public static bool IsRotation(string key, out string group, out int component)
        {
            group = null; component = -1;
            if (key.Length < 17 || key[^2] != '.' || !key.EndsWith(".x", StringComparison.Ordinal) && !key.EndsWith(".y", StringComparison.Ordinal) && !key.EndsWith(".z", StringComparison.Ordinal) && !key.EndsWith(".w", StringComparison.Ordinal)) return false;
            if (key.IndexOf("|4||m_LocalRotation.", StringComparison.Ordinal) < 0) return false;
            group = key[..^1]; component = "xyzw".IndexOf(key[^1]);
            return true;
        }

        /// <summary>Negate this pose's quaternion sums that lie on the other hemisphere than <paramref name="reference"/>'s.</summary>
        public void AlignRotationsTo(Pose reference)
        {
            var groups = new HashSet<string>();
            foreach (var k in Floats.Keys) if (IsRotation(k, out var g, out _)) groups.Add(g);
            foreach (var g in groups)
            {
                if (!reference.Floats.ContainsKey(g + "w")) continue;
                float dot = 0;
                foreach (var c in "xyzw") dot += Sum(g + c) * reference.Sum(g + c);
                if (dot >= 0) continue;
                foreach (var c in "xyzw") if (Floats.TryGetValue(g + c, out var a)) Floats[g + c] = (-a.sum, a.w);
            }
        }
    }
}
