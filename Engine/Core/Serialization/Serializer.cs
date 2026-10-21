using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MiniUnity.Yaml;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiniUnity
{
    /// <summary>Object ⇄ YAML body (Unity serialization rules). Object references become YRef nodes pointing at live objects.</summary>
    public static class Serializer
    {
        // ------------------------------------------------------------------ problems
        /// <summary>Values that could not be read or written (a getter or ISerializationCallbackReceiver threw, a type has no usable
        /// constructor…). The field is written with its default as before; `ua` returns the list as `warnings`.</summary>
        public static readonly List<string> Warnings = new();
        /// <summary>`ua … --strict`: such a problem fails the command instead of being written as a default.</summary>
        public static bool Strict;

        static void Problem(string what, Exception e)
        {
            if (e is TargetInvocationException { InnerException: { } inner }) e = inner;
            var msg = $"{what}: {e.GetType().Name}: {e.Message}";
            if (Strict) throw new InvalidOperationException("serialization problem (--strict): " + msg, e);
            if (Warnings.Count < 200) Warnings.Add(msg);
        }

        // ------------------------------------------------------------------ class ids
        static readonly Dictionary<Type, (int id, string name)> nativeIds = new()
        {
            [typeof(GameObject)] = (1, "GameObject"),
            [typeof(Transform)] = (4, "Transform"),
            [typeof(RectTransform)] = (224, "RectTransform"),
            [typeof(Camera)] = (20, "Camera"),
            [typeof(Material)] = (21, "Material"),
            [typeof(MeshRenderer)] = (23, "MeshRenderer"),
            [typeof(Texture2D)] = (28, "Texture2D"),
            [typeof(MeshFilter)] = (33, "MeshFilter"),
            [typeof(Mesh)] = (43, "Mesh"),
            [typeof(Shader)] = (48, "Shader"),
            [typeof(TextAsset)] = (49, "TextAsset"),
            [typeof(AnimationClip)] = (74, "AnimationClip"),
            [typeof(AudioListener)] = (81, "AudioListener"),
            [typeof(AudioSource)] = (82, "AudioSource"),
            [typeof(AudioClip)] = (83, "AudioClip"),
            [typeof(Animator)] = (95, "Animator"),
            [typeof(Font)] = (128, "Font"),
            [typeof(Sprite)] = (213, "Sprite"),
            [typeof(CanvasRenderer)] = (222, "CanvasRenderer"),
            [typeof(Canvas)] = (223, "Canvas"),
            [typeof(CanvasGroup)] = (225, "CanvasGroup"),
            [typeof(UnityEngine.Playables.PlayableDirector)] = (320, "PlayableDirector"),
            [typeof(Light)] = (108, "Light"),
            [typeof(BoxCollider)] = (65, "BoxCollider"),
            [typeof(SphereCollider)] = (135, "SphereCollider"),
            [typeof(CapsuleCollider)] = (136, "CapsuleCollider"),
            [typeof(MeshCollider)] = (64, "MeshCollider"),
            [typeof(Rigidbody)] = (54, "Rigidbody"),
            [typeof(ArticulationBody)] = (171, "ArticulationBody"),
            [typeof(CharacterController)] = (143, "CharacterController"),
            [typeof(PhysicMaterial)] = (134, "PhysicMaterial"),
            [typeof(ConstantForce)] = (75, "ConstantForce"),
            [typeof(FixedJoint)] = (138, "FixedJoint"),
            [typeof(HingeJoint)] = (59, "HingeJoint"),
            [typeof(SpringJoint)] = (145, "SpringJoint"),
            [typeof(CharacterJoint)] = (144, "CharacterJoint"),
            [typeof(ConfigurableJoint)] = (153, "ConfigurableJoint"),
            [typeof(SpriteRenderer)] = (212, "SpriteRenderer"),
            [typeof(Animation)] = (111, "Animation"),
            [typeof(UnityEditor.Animations.AnimatorController)] = (91, "AnimatorController"),
            [typeof(AnimatorOverrideController)] = (221, "AnimatorOverrideController"),
            [typeof(UnityEditor.Animations.AnimatorState)] = (1102, "AnimatorState"),
            [typeof(UnityEditor.Animations.AnimatorStateTransition)] = (1101, "AnimatorStateTransition"),
            [typeof(UnityEditor.Animations.AnimatorStateMachine)] = (1107, "AnimatorStateMachine"),
            [typeof(UnityEditor.Animations.AnimatorTransition)] = (1109, "AnimatorTransition"),
            [typeof(UnityEditor.Animations.BlendTree)] = (206, "BlendTree"),
            [typeof(AvatarMask)] = (319, "AvatarMask"),
            [typeof(Rigidbody2D)] = (50, "Rigidbody2D"),
            [typeof(BoxCollider2D)] = (61, "BoxCollider2D"),
            [typeof(CircleCollider2D)] = (58, "CircleCollider2D"),
            [typeof(PolygonCollider2D)] = (60, "PolygonCollider2D"),
            [typeof(EdgeCollider2D)] = (68, "EdgeCollider2D"),
            [typeof(CapsuleCollider2D)] = (70, "CapsuleCollider2D"),
            [typeof(CompositeCollider2D)] = (66, "CompositeCollider2D"),
            [typeof(PhysicsMaterial2D)] = (62, "PhysicsMaterial2D"),
            [typeof(SpringJoint2D)] = (231, "SpringJoint2D"),
            [typeof(DistanceJoint2D)] = (232, "DistanceJoint2D"),
            [typeof(HingeJoint2D)] = (233, "HingeJoint2D"),
            [typeof(SliderJoint2D)] = (234, "SliderJoint2D"),
            [typeof(WheelJoint2D)] = (235, "WheelJoint2D"),
            [typeof(ConstantForce2D)] = (247, "ConstantForce2D"),
            [typeof(AreaEffector2D)] = (249, "AreaEffector2D"),
            [typeof(PointEffector2D)] = (250, "PointEffector2D"),
            [typeof(PlatformEffector2D)] = (251, "PlatformEffector2D"),
            [typeof(SurfaceEffector2D)] = (252, "SurfaceEffector2D"),
            [typeof(BuoyancyEffector2D)] = (253, "BuoyancyEffector2D"),
            [typeof(RelativeJoint2D)] = (254, "RelativeJoint2D"),
            [typeof(FixedJoint2D)] = (255, "FixedJoint2D"),
            [typeof(FrictionJoint2D)] = (256, "FrictionJoint2D"),
            [typeof(TargetJoint2D)] = (257, "TargetJoint2D"),
            [typeof(WheelCollider)] = (146, "WheelCollider"),
            [typeof(RenderTexture)] = (84, "RenderTexture"),
            [typeof(UnityEngine.Audio.AudioMixer)] = (241, "AudioMixerController"),
            [typeof(UnityEngine.Audio.AudioMixerGroup)] = (243, "AudioMixerGroupController"),
            [typeof(UnityEngine.Audio.AudioMixerSnapshot)] = (245, "AudioMixerSnapshotController"),
            // components MiniUnity keeps as serialized data (loaded, found by GetComponent, written back unchanged)
            [typeof(ParticleSystem)] = (198, "ParticleSystem"),
            [typeof(ParticleSystemRenderer)] = (199, "ParticleSystemRenderer"),
            [typeof(LineRenderer)] = (120, "LineRenderer"),
            [typeof(TrailRenderer)] = (96, "TrailRenderer"),
            [typeof(SkinnedMeshRenderer)] = (137, "SkinnedMeshRenderer"),
            [typeof(TextMesh)] = (102, "TextMesh"),
            [typeof(LODGroup)] = (205, "LODGroup"),
            [typeof(ReflectionProbe)] = (215, "ReflectionProbe"),
            [typeof(Projector)] = (119, "Projector"),
            [typeof(Skybox)] = (45, "Skybox"),
            [typeof(FlareLayer)] = (124, "FlareLayer"),
            [typeof(Grid)] = (156049354, "Grid"),
            [typeof(UnityEngine.Tilemaps.Tilemap)] = (1839735485, "Tilemap"),
            [typeof(UnityEngine.Tilemaps.TilemapRenderer)] = (483693784, "TilemapRenderer"),
            [typeof(UnityEngine.Tilemaps.TilemapCollider2D)] = (19719996, "TilemapCollider2D"),
            [typeof(UnityEngine.Rendering.SortingGroup)] = (210, "SortingGroup"),
            [typeof(AudioReverbZone)] = (167, "AudioReverbZone"),
            [typeof(AudioLowPassFilter)] = (169, "AudioLowPassFilter"),
            [typeof(AudioHighPassFilter)] = (165, "AudioHighPassFilter"),
            [typeof(AudioEchoFilter)] = (168, "AudioEchoFilter"),
            [typeof(AudioChorusFilter)] = (166, "AudioChorusFilter"),
            [typeof(AudioDistortionFilter)] = (170, "AudioDistortionFilter"),
            [typeof(AudioReverbFilter)] = (164, "AudioReverbFilter"),
        };
        public static readonly Dictionary<int, Type> TypeById = nativeIds.ToDictionary(k => k.Value.id, k => k.Key);

        public static (int id, string name) ClassOf(Object o) => ClassOf(o.GetType());
        public static (int id, string name) ClassOf(Type t)
        {
            if (typeof(MonoBehaviour).IsAssignableFrom(t) || typeof(ScriptableObject).IsAssignableFrom(t)) return (114, "MonoBehaviour");
            for (var c = t; c != null; c = c.BaseType) if (nativeIds.TryGetValue(c, out var r)) return r;
            throw new NotSupportedException("No class id for " + t.FullName);
        }
        public static bool IsManaged(Object o) => o is MonoBehaviour || o is ScriptableObject;

        static YMap Header(Object o)
        {
            var m = new YMap();
            m.Add("m_ObjectHideFlags", YScalar.Of((int)o.m_HideFlags));
            m.Add("m_CorrespondingSourceObject", new YRef(0, null, 0));
            m.Add("m_PrefabInstance", new YRef(0, null, 0));
            m.Add("m_PrefabAsset", new YRef(0, null, 0));
            return m;
        }

        // ------------------------------------------------------------------ serialize
        public static YMap Serialize(Object o)
        {
            switch (o)
            {
                case GameObject go: return SerializeGameObject(go);
                case RectTransform rt: return SerializeTransform(rt, true);
                case Transform t: return SerializeTransform(t, false);
            }
            if (IsManaged(o)) return SerializeManagedObject(o);
            return SerializeNative(o);
        }

        static YMap SerializeGameObject(GameObject go)
        {
            var m = Header(go);
            m.Add("serializedVersion", "6");
            var comps = new YSeq();
            foreach (var c in go.m_Components) if (!c.m_Destroyed) comps.Items.Add(new YMap().Add("component", new YRef(c)));
            m.Add("m_Component", comps);
            m.Add("m_Layer", YScalar.Of(go.m_Layer));
            m.Add("m_Name", go.m_Name);
            m.Add("m_TagString", go.m_Tag);
            m.Add("m_Icon", new YRef(0, null, 0));
            m.Add("m_NavMeshLayer", "0");
            m.Add("m_StaticEditorFlags", YScalar.Of((long)go.m_StaticEditorFlags));
            m.Add("m_IsActive", YScalar.Of(go.m_IsActive));
            return m;
        }

        static YMap SerializeTransform(Transform t, bool rect)
        {
            var m = Header(t);
            m.Add("m_GameObject", new YRef(t.m_GameObject));
            if (!rect) m.Add("serializedVersion", "2");
            m.Add("m_LocalRotation", Vec(t.m_LocalRotation));
            // Unity 2022 writes RectTransform m_LocalPosition.xy as 0 (derived from anchors at load); keep z
            m.Add("m_LocalPosition", Vec(rect ? new Vector3(0, 0, t.m_LocalPositionRaw.z) : t.localPosition));
            m.Add("m_LocalScale", Vec(t.m_LocalScale));
            m.Add("m_ConstrainProportionsScale", YScalar.Of(t.m_ConstrainProportionsScale));
            var ch = new YSeq();
            foreach (var c in t.m_Children) ch.Items.Add(new YRef(c));
            m.Add("m_Children", ch);
            m.Add("m_Father", new YRef(t.m_Parent));
            m.Add("m_LocalEulerAnglesHint", Vec(t.m_LocalEulerAnglesHint));
            if (rect)
            {
                var r = (RectTransform)t;
                m.Add("m_AnchorMin", Vec(r.m_AnchorMin));
                m.Add("m_AnchorMax", Vec(r.m_AnchorMax));
                m.Add("m_AnchoredPosition", Vec(r.m_AnchoredPosition));
                m.Add("m_SizeDelta", Vec(r.m_SizeDelta));
                m.Add("m_Pivot", Vec(r.m_Pivot));
            }
            return m;
        }

        static YMap SerializeNative(Object o)
        {
            if (o.m_Native == null) throw new NotSupportedException("Native object without data: " + o.GetType().Name);
            var m = Header(o);
            // Material (and other non-component natives) write serializedVersion right after m_ObjectHideFlags
            if (o is Material && o.m_Native.Has("serializedVersion")) m.Entries.Insert(0, new("serializedVersion", o.m_Native["serializedVersion"].Clone()));
            if (o is Component c) m.Add("m_GameObject", new YRef(c.m_GameObject));
            if (o is Behaviour b) m.Add("m_Enabled", YScalar.Of(b.m_Enabled));
            if (!(o is Component) && !o.m_Native.Has("m_Name")) m.Add("m_Name", o.m_Name);
            if (o is INativeSync ns) ns.SyncToNative();
            foreach (var e in o.m_Native.Entries)
            {
                if (m.Has(e.Key)) continue;
                if (e.Key == "m_Name" && !(o is Component)) { m.Add("m_Name", o.m_Name); continue; }
                m.Add(e.Key, e.Value?.Clone());
            }
            return m;
        }

        static YMap SerializeManagedObject(Object o)
        {
            if (o is ISerializationCallbackReceiver r) { try { r.OnBeforeSerialize(); } catch (Exception e) { Debug.LogWarning($"OnBeforeSerialize {o.GetType().Name}: {e.Message}"); } }
            var m = Header(o);
            if (o is MonoBehaviour mb)
            {
                m.Add("m_GameObject", new YRef(mb.m_GameObject));
                m.Add("m_Enabled", YScalar.Of(mb.m_Enabled));
            }
            else
            {
                m.Add("m_GameObject", new YRef(0, null, 0));
                m.Add("m_Enabled", "1");
            }
            m.Add("m_EditorHideFlags", "0");
            m.Add("m_Script", ScriptRegistry.ScriptRef(o.GetType()));
            m.Add("m_Name", o is MonoBehaviour ? "" : o.m_Name);
            m.Add("m_EditorClassIdentifier", "");
            WriteFields(m, o, o.GetType(), 0);
            return m;
        }

        // ------------------------------------------------------------------ managed fields
        static readonly Dictionary<Type, FieldInfo[]> fieldCache = new();

        public static FieldInfo[] SerializedFields(Type t)
        {
            if (fieldCache.TryGetValue(t, out var r)) return r;
            var chain = new List<Type>();
            for (var c = t; c != null && c != typeof(object) && c != typeof(MonoBehaviour) && c != typeof(ScriptableObject) && c != typeof(Behaviour)
                 && c != typeof(Component) && c != typeof(Object) && c != typeof(ValueType); c = c.BaseType) chain.Add(c);
            chain.Reverse();
            var list = new List<FieldInfo>();
            var names = new HashSet<string>();
            foreach (var c in chain)
                foreach (var f in c.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).OrderBy(f => f.MetadataToken))
                {
                    if (f.IsInitOnly || f.IsLiteral || f.IsStatic) continue;
                    if (f.IsNotSerialized || f.GetCustomAttribute<NonSerializedAttribute>() != null) continue;
                    bool sref = f.GetCustomAttribute<SerializeReference>() != null;
                    if (!f.IsPublic && f.GetCustomAttribute<SerializeField>() == null && !sref) continue;
                    if (f.Name.Contains('<')) continue; // backing fields w/o [field: SerializeField]
                    if (!sref && !IsSerializableFieldType(f.FieldType, 0)) continue;
                    if (!names.Add(f.Name)) continue;
                    list.Add(f);
                }
            r = list.ToArray();
            fieldCache[t] = r;
            return r;
        }

        static readonly HashSet<Type> builtinStructs = new()
        {
            typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Vector2Int), typeof(Vector3Int), typeof(Quaternion), typeof(Color), typeof(Color32),
            typeof(Rect), typeof(RectInt), typeof(Bounds), typeof(BoundsInt), typeof(Matrix4x4), typeof(LayerMask), typeof(AnimationCurve), typeof(Gradient),
            typeof(RectOffset), typeof(Hash128), typeof(GUIStyle), typeof(PropertyName),
        };

        public static bool IsSerializableFieldType(Type t, int depth)
        {
            if (depth > 10) return false;
            if (t.IsPrimitive && t != typeof(IntPtr) && t != typeof(UIntPtr)) return true;
            if (t == typeof(string)) return true;
            if (t.IsEnum) return true;
            if (typeof(Object).IsAssignableFrom(t)) return true;
            if (builtinStructs.Contains(t)) return true;
            if (typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(t)) return true;
            if (t.IsArray) { if (t.GetArrayRank() != 1) return false; var e = t.GetElementType(); return !e.IsArray && !IsList(e) && IsSerializableFieldType(e, depth + 1); }
            if (IsList(t)) { var e = t.GetGenericArguments()[0]; return !e.IsArray && !IsList(e) && IsSerializableFieldType(e, depth + 1); }
            if (t.IsAbstract || t.IsInterface) return false;
            if (typeof(Delegate).IsAssignableFrom(t)) return false;
            if (t.Namespace != null && t.Namespace.StartsWith("System")) return false;
            if (t.IsGenericTypeDefinition) return false;
            if (t.GetCustomAttribute<SerializableAttribute>(false) == null && !t.IsSerializable) return false;
            if (t.IsPointer) return false;
            return true;
        }
        static bool IsList(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>);

        static void WriteFields(YMap m, object o, Type t, int depth)
        {
            foreach (var f in SerializedFields(t))
            {
                object v;
                try { v = f.GetValue(o); } catch (Exception e) { Problem($"read {t.Name}.{f.Name}", e); v = null; }
                m.Add(f.Name, ValueToNode(v, f.FieldType, depth));
            }
        }

        public static YNode ValueToNode(object v, Type t, int depth)
        {
            if (typeof(Object).IsAssignableFrom(t)) return new YRef(v as Object);
            if (t == typeof(string)) return new YScalar((string)v ?? "");
            if (t == typeof(PropertyName)) return new YScalar(((PropertyName)v).ToString());
            if (t == typeof(bool)) return YScalar.Of((bool)v);
            if (t == typeof(float)) return YScalar.Of((float)v);
            if (t == typeof(double)) return YScalar.Of((double)v);
            if (t.IsEnum) return new YScalar(Convert.ToInt64(v).ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (t == typeof(char)) return YScalar.Of((int)(char)v);
            if (t.IsPrimitive) return new YScalar(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture));
            if (t.IsArray || IsList(t))
            {
                var et = t.IsArray ? t.GetElementType() : t.GetGenericArguments()[0];
                if (et.IsEnum)
                {
                    // Unity writes enum arrays as a hex blob of little-endian int32 values ("" when empty)
                    var sb = new System.Text.StringBuilder();
                    if (v is IList el) foreach (var item in el) { uint u = (uint)Convert.ToInt32(item); for (int k = 0; k < 4; k++) sb.Append(((u >> (8 * k)) & 255).ToString("x2")); }
                    return new YScalar(sb.ToString());
                }
                if (et == typeof(byte) || et == typeof(sbyte))
                {
                    // byte arrays are a hex blob too ("cells: 0404ff…")
                    var sb = new System.Text.StringBuilder();
                    if (v is IList bl) foreach (var item in bl) sb.Append((Convert.ToInt32(item) & 255).ToString("x2"));
                    return new YScalar(sb.ToString());
                }
                var seq = new YSeq();
                if (v is IList list) foreach (var item in list) seq.Items.Add(ValueToNode(item, et, depth + 1));
                return seq;
            }
            var b = BuiltinToNode(v, t);
            if (b != null) return b;
            if (typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(t)) return UnityEventNode(v);
            // [Serializable] class/struct
            if (v == null) { try { v = Activator.CreateInstance(t, true); } catch (Exception e) { Problem($"create {t.Name} (written as {{}})", e); return new YMap(); } }
            if (v is ISerializationCallbackReceiver r) { try { r.OnBeforeSerialize(); } catch (Exception e) { Problem($"{t.Name}.OnBeforeSerialize", e); } }
            var m = new YMap();
            if (depth < 10) WriteFields(m, v, t, depth + 1);
            return m;
        }

        static readonly ConditionalWeakTable<object, YNode> rawEvents = new();
        static YNode UnityEventNode(object v)
        {
            if (v != null && rawEvents.TryGetValue(v, out var raw)) return raw.Clone();
            return new YMap().Add("m_PersistentCalls", new YMap().Add("m_Calls", new YSeq()));
        }

        public static YMap Vec(Vector2 v) => new YMap { Flow = true }.Add("x", YScalar.Of(v.x)).Add("y", YScalar.Of(v.y));
        public static YMap Vec(Vector3 v) => new YMap { Flow = true }.Add("x", YScalar.Of(v.x)).Add("y", YScalar.Of(v.y)).Add("z", YScalar.Of(v.z));
        public static YMap Vec(Vector4 v) => new YMap { Flow = true }.Add("x", YScalar.Of(v.x)).Add("y", YScalar.Of(v.y)).Add("z", YScalar.Of(v.z)).Add("w", YScalar.Of(v.w));
        public static YMap Vec(Quaternion v) => new YMap { Flow = true }.Add("x", YScalar.Of(v.x)).Add("y", YScalar.Of(v.y)).Add("z", YScalar.Of(v.z)).Add("w", YScalar.Of(v.w));
        public static YMap Col(Color c) => new YMap { Flow = true }.Add("r", YScalar.Of(c.r)).Add("g", YScalar.Of(c.g)).Add("b", YScalar.Of(c.b)).Add("a", YScalar.Of(c.a));

        public static YMap CurveNode(AnimationCurve c)
        {
            var m = new YMap();
            m.Add("serializedVersion", "2");
            var keys = new YSeq();
            if (c != null)
                foreach (var k in c.m_Keys)
                {
                    var km = new YMap();
                    km.Add("serializedVersion", "3");
                    km.Add("time", YScalar.Of(k.time)); km.Add("value", YScalar.Of(k.value));
                    km.Add("inSlope", YScalar.Of(k.inTangent)); km.Add("outSlope", YScalar.Of(k.outTangent));
                    km.Add("tangentMode", YScalar.Of(k.m_TangentMode)); km.Add("weightedMode", YScalar.Of((int)k.weightedMode));
                    km.Add("inWeight", YScalar.Of(k.inWeight)); km.Add("outWeight", YScalar.Of(k.outWeight));
                    keys.Items.Add(km);
                }
            m.Add("m_Curve", keys);
            m.Add("m_PreInfinity", YScalar.Of(c == null ? 2 : WrapToInternal(c.m_PreInfinity)));
            m.Add("m_PostInfinity", YScalar.Of(c == null ? 2 : WrapToInternal(c.m_PostInfinity)));
            m.Add("m_RotationOrder", "4");
            return m;
        }

        // native curves store InternalWrapMode (PingPong=0, Repeat=1, Clamp=2)
        static int WrapToInternal(WrapMode w) => w switch { WrapMode.PingPong => 0, WrapMode.Loop => 1, _ => 2 };
        static WrapMode InternalToWrap(int i) => i switch { 0 => WrapMode.PingPong, 1 => WrapMode.Loop, _ => WrapMode.ClampForever };

        static YNode BuiltinToNode(object v, Type t)
        {
            if (t == typeof(Vector2)) return Vec((Vector2)v);
            if (t == typeof(Vector3)) return Vec((Vector3)v);
            if (t == typeof(Vector4)) return Vec((Vector4)v);
            if (t == typeof(Quaternion)) return Vec((Quaternion)v);
            if (t == typeof(Color)) return Col((Color)v);
            if (t == typeof(Vector2Int)) { var p = (Vector2Int)v; return new YMap { Flow = true }.Add("x", YScalar.Of(p.x)).Add("y", YScalar.Of(p.y)); }
            if (t == typeof(Vector3Int)) { var p = (Vector3Int)v; return new YMap { Flow = true }.Add("x", YScalar.Of(p.x)).Add("y", YScalar.Of(p.y)).Add("z", YScalar.Of(p.z)); }
            if (t == typeof(Color32)) { var c = (Color32)v; uint rgba = (uint)(c.r | (c.g << 8) | (c.b << 16) | (c.a << 24)); return new YMap().Add("serializedVersion", "2").Add("rgba", YScalar.Of((long)rgba)); }
            if (t == typeof(Rect)) { var r = (Rect)v; return new YMap().Add("serializedVersion", "2").Add("x", YScalar.Of(r.x)).Add("y", YScalar.Of(r.y)).Add("width", YScalar.Of(r.width)).Add("height", YScalar.Of(r.height)); }
            if (t == typeof(RectInt)) { var r = (RectInt)v; return new YMap().Add("serializedVersion", "2").Add("x", YScalar.Of(r.x)).Add("y", YScalar.Of(r.y)).Add("width", YScalar.Of(r.width)).Add("height", YScalar.Of(r.height)); }
            if (t == typeof(LayerMask)) { var l = (LayerMask)v; return new YMap().Add("serializedVersion", "2").Add("m_Bits", YScalar.Of((long)(uint)l.value)); }
            if (t == typeof(Bounds)) { var b = (Bounds)v; return new YMap().Add("m_Center", Vec(b.center)).Add("m_Extent", Vec(b.extents)); }
            if (t == typeof(AnimationCurve)) return CurveNode((AnimationCurve)v);
            if (t == typeof(Matrix4x4))
            {
                var mm = (Matrix4x4)v; var m = new YMap();
                for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) m.Add($"e{i}{j}", YScalar.Of(mm[i, j]));
                return m;
            }
            if (t == typeof(RectOffset))
            {
                var ro = v as RectOffset;
                return new YMap().Add("m_Left", YScalar.Of(ro?.m_LeftV ?? 0)).Add("m_Right", YScalar.Of(ro?.m_RightV ?? 0)).Add("m_Top", YScalar.Of(ro?.m_TopV ?? 0)).Add("m_Bottom", YScalar.Of(ro?.m_BottomV ?? 0));
            }
            if (t == typeof(Gradient)) return (v as Gradient ?? new Gradient()).ToYaml();
            if (t == typeof(GUIStyle) || t == typeof(Hash128) || t == typeof(BoundsInt))
                throw new NotImplementedException("serialize " + t.Name);
            return null;
        }

        /// <summary>Unity Editor fills unassigned serialized Object fields of MonoBehaviours with a "fake null" (MU_FAKE_NULL=0 turns it off):
        /// `== null` and `if (x)` are true / false as for null, but using it throws UnassignedReferenceException (never assigned) or
        /// MissingReferenceException (the referenced object is gone) with Unity's message instead of a NullReferenceException.
        /// Abstract field types get a real null.</summary>
        public static readonly bool FakeNull = Environment.GetEnvironmentVariable("MU_FAKE_NULL") != "0";

        internal static Object FakeNullFor(Type t, string field, string owner, bool missing)
        {
            if (!FakeNull || t.IsAbstract || t.ContainsGenericParameters || t.IsInterface) return null;
            Object o;
            try { o = (Object)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t); }
            catch { return null; }
            o.m_Destroyed = true; o.m_FakeNullMissing = missing; o.m_Name = "";
            o.m_FakeNull = missing
                ? $"The variable {field} of {owner} doesn't exist anymore.\nYou probably need to reassign the {field} variable of the '{owner}' script in the inspector."
                : $"The variable {field} of {owner} has not been assigned.\nYou probably need to assign the {field} variable of the {owner} script in the inspector.";
            return o;
        }

        /// <summary>Unity never leaves serialized collection / [Serializable] class fields null on new objects.</summary>
        public static void EnsureDefaults(object o, int depth = 0)
        {
            if (o == null || depth > 8) return;
            foreach (var f in SerializedFields(o.GetType()))
            {
                var t = f.FieldType;
                if (f.GetCustomAttribute<SerializeReference>() != null) continue;
                object v = f.GetValue(o);
                if (v != null)
                {
                    if (!t.IsValueType && !(v is string) && !(v is Object) && !(v is IList) && !typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(t) && !builtinStructs.Contains(t)) EnsureDefaults(v, depth + 1);
                    continue;
                }
                if (t == typeof(string)) f.SetValue(o, "");
                else if (t.IsArray) f.SetValue(o, Array.CreateInstance(t.GetElementType(), 0));
                else if (IsList(t)) f.SetValue(o, Activator.CreateInstance(t));
                else if (typeof(Object).IsAssignableFrom(t)) { if (depth == 0 && o is MonoBehaviour owner) f.SetValue(o, FakeNullFor(t, f.Name, owner.GetType().Name, false)); continue; }
                else if (t == typeof(AnimationCurve)) f.SetValue(o, new AnimationCurve());
                else if (!t.IsValueType && !t.IsAbstract)
                {
                    try { var inst = CreateSerializable(t); EnsureDefaults(inst, depth + 1); f.SetValue(o, inst); }
                    catch (Exception e) { Problem($"default for {o.GetType().Name}.{f.Name}", e); }
                }
            }
        }

        // ------------------------------------------------------------------ deserialize
        public delegate Object RefResolver(YRef r, Type expected);

        public static void Deserialize(Object o, YMap body, RefResolver resolve)
        {
            Object.SceneEdited(); o.m_NVersion++;
            switch (o)
            {
                case GameObject go: DeserializeGameObject(go, body, resolve); return;
                case Transform t: DeserializeTransform(t, body, resolve); return;
            }
            if (IsManaged(o))
            {
                if (o is MonoBehaviour mb) mb.m_Enabled = Int(body["m_Enabled"], 1) != 0;
                else o.m_Name = Str(body["m_Name"]);
                o.m_HideFlags = (HideFlags)Int(body["m_ObjectHideFlags"], 0);
                ReadFields(body, o, o.GetType(), resolve, 0);
                EnsureDefaults(o);
                if (o is ISerializationCallbackReceiver r) { try { r.OnAfterDeserialize(); } catch (Exception e) { Debug.LogWarning($"OnAfterDeserialize {o.GetType().Name}: {e.Message}"); } }
                return;
            }
            // native: keep the body (refs resolved later on access)
            o.m_HideFlags = (HideFlags)Int(body["m_ObjectHideFlags"], 0);
            if (o is Behaviour b) b.m_Enabled = Int(body["m_Enabled"], 1) != 0;
            if (!(o is Component)) o.m_Name = Str(body["m_Name"]);
            var nat = new YMap();
            foreach (var e in body.Entries)
            {
                if (e.Key is "m_ObjectHideFlags" or "m_CorrespondingSourceObject" or "m_PrefabInstance" or "m_PrefabAsset" or "m_GameObject") continue;
                // a Behaviour keeps m_Enabled in its field; Collider / Renderer (Components with their own enabled) read it from the body
                if (e.Key == "m_Enabled" && o is Behaviour) continue;
                nat.Add(e.Key, ResolveRefs(e.Value, resolve)); // m_Name kept as an ordering placeholder for assets
            }
            o.m_Native = nat;
            if (o is INativeSync ns) ns.SyncFromNative();
        }

        static YNode ResolveRefs(YNode n, RefResolver resolve)
        {
            switch (n)
            {
                case YRef r:
                    if (r.Raw && r.FileID == 0) return new YRef(null);
                    if (!r.Raw && r.Target == null) return new YRef(null);
                    var o = resolve(r, typeof(Object));
                    return o != null ? new YRef(o) : (r.Raw ? r.Clone() : new YRef(null));
                case YMap m:
                    var mm = new YMap { Flow = m.Flow };
                    foreach (var e in m.Entries) mm.Add(e.Key, ResolveRefs(e.Value, resolve));
                    return mm;
                case YSeq s:
                    var ss = new YSeq { Flow = s.Flow };
                    foreach (var i in s.Items) ss.Items.Add(ResolveRefs(i, resolve));
                    return ss;
                default: return n?.Clone();
            }
        }

        static void DeserializeGameObject(GameObject go, YMap body, RefResolver resolve)
        {
            go.m_Name = Str(body["m_Name"]);
            go.m_Layer = Int(body["m_Layer"], 0);
            go.m_Tag = Str(body["m_TagString"]);
            go.m_IsActive = Int(body["m_IsActive"], 1) != 0;
            go.m_StaticEditorFlags = (uint)Long(body["m_StaticEditorFlags"]);
            go.m_HideFlags = (HideFlags)Int(body["m_ObjectHideFlags"], 0);
            go.m_Components.Clear();
            if (body["m_Component"] is YSeq seq)
                foreach (var it in seq.Items)
                {
                    var r = (it as YMap)?["component"] as YRef;
                    if (r == null) continue;
                    if (resolve(r, typeof(Component)) is Component c)
                    {
                        c.m_GameObject = go; go.m_Components.Add(c);
                        if (c is Transform t) go.m_Transform = t;
                    }
                }
        }

        static void DeserializeTransform(Transform t, YMap body, RefResolver resolve)
        {
            t.m_HideFlags = (HideFlags)Int(body["m_ObjectHideFlags"], 0);
            Object.SceneEdited(); Hier.Version++;
            t.m_LocalRotation = ReadQuat(body["m_LocalRotation"]);
            t.m_LocalPositionRaw = ReadV3(body["m_LocalPosition"]);
            t.m_LocalScale = ReadV3(body["m_LocalScale"]);
            t.m_ConstrainProportionsScale = Int(body["m_ConstrainProportionsScale"], 0) != 0;
            t.m_LocalEulerAnglesHint = ReadV3(body["m_LocalEulerAnglesHint"]);
            t.m_Children.Clear();
            if (body["m_Children"] is YSeq ch)
                foreach (var it in ch.Items)
                    if (it is YRef r && resolve(r, typeof(Transform)) is Transform c) { t.m_Children.Add(c); c.m_Parent = t; }
            if (t is RectTransform rt)
            {
                rt.m_AnchorMin = ReadV2(body["m_AnchorMin"]);
                rt.m_AnchorMax = ReadV2(body["m_AnchorMax"]);
                rt.m_AnchoredPosition = ReadV2(body["m_AnchoredPosition"]);
                rt.m_SizeDelta = ReadV2(body["m_SizeDelta"]);
                rt.m_Pivot = ReadV2(body["m_Pivot"]);
            }
        }

        static void ReadFields(YMap m, object o, Type t, RefResolver resolve, int depth)
        {
            foreach (var f in SerializedFields(t))
            {
                var n = m[f.Name];
                if (n == null)
                {
                    foreach (var fa in f.GetCustomAttributes<UnityEngine.Serialization.FormerlySerializedAsAttribute>())
                        if ((n = m[fa.oldName]) != null) break;
                    if (n == null) continue;
                }
                try
                {
                    var cur = f.GetValue(o);
                    var v = NodeToValue(n, f.FieldType, cur, resolve, depth);
                    // a reference to something that no longer resolves: Unity's "missing" fake null
                    if (v == null && depth == 0 && n is YRef r && !r.IsNull && typeof(Object).IsAssignableFrom(f.FieldType) && o is MonoBehaviour)
                        v = FakeNullFor(f.FieldType, f.Name, o.GetType().Name, true);
                    f.SetValue(o, v);
                }
                catch (Exception e) when (!Strict) { Debug.LogWarning($"[deser] {t.Name}.{f.Name}: {e.Message}"); Problem($"read {t.Name}.{f.Name} from YAML", e); }
            }
        }

        public static object NodeToValue(YNode n, Type t, object cur, RefResolver resolve, int depth)
        {
            if (typeof(Object).IsAssignableFrom(t))
            {
                if (n is YRef r)
                {
                    if (!r.Raw && r.Target == null) return null;
                    if (r.Raw && r.FileID == 0) return null;
                    var o = resolve(r, t);
                    return o != null && t.IsInstanceOfType(o) ? o : null;
                }
                return null;
            }
            if (t == typeof(string)) return (n as YScalar)?.Value ?? "";
            if (t == typeof(PropertyName)) return new PropertyName((n as YScalar)?.Value ?? "");
            if (t == typeof(bool)) return Int(n, 0) != 0;
            if (t == typeof(float)) return n is YScalar s ? s.F : 0f;
            if (t == typeof(double)) return n is YScalar s2 ? s2.D : 0d;
            if (t.IsEnum) return Enum.ToObject(t, Long(n));
            if (t == typeof(char)) return (char)Int(n, 0);
            if (t.IsPrimitive) return Convert.ChangeType(n is YScalar s3 ? (s3.Value.Length == 0 ? "0" : s3.Value) : "0", t, System.Globalization.CultureInfo.InvariantCulture);
            if (t.IsArray || IsList(t))
            {
                var et = t.IsArray ? t.GetElementType() : t.GetGenericArguments()[0];
                var items = new List<object>();
                if (n is YSeq seq) foreach (var it in seq.Items) items.Add(NodeToValue(it, et, DefaultFor(et), resolve, depth + 1));
                else if (et.IsEnum && n is YScalar hx)
                    for (int k = 0; k + 8 <= hx.Value.Length; k += 8)
                    {
                        uint u = 0; for (int b2 = 0; b2 < 4; b2++) u |= Convert.ToUInt32(hx.Value.Substring(k + b2 * 2, 2), 16) << (8 * b2);
                        items.Add(Enum.ToObject(et, (int)u));
                    }
                else if ((et == typeof(byte) || et == typeof(sbyte)) && n is YScalar hb)
                    for (int k = 0; k + 2 <= hb.Value.Length; k += 2)
                    {
                        byte by = Convert.ToByte(hb.Value.Substring(k, 2), 16);
                        items.Add(et == typeof(byte) ? by : (object)unchecked((sbyte)by));
                    }
                if (t.IsArray) { var arr = Array.CreateInstance(et, items.Count); for (int i = 0; i < items.Count; i++) arr.SetValue(items[i], i); return arr; }
                var list = (IList)Activator.CreateInstance(t);
                foreach (var it in items) list.Add(it);
                return list;
            }
            var b = NodeToBuiltin(n, t, cur);
            if (b != null) return b;
            if (typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(t))
            {
                var ev = cur ?? Activator.CreateInstance(t, true);
                if (n != null) rawEvents.AddOrUpdate(ev, ResolveRefs(n, resolve));
                // persistent listeners (Inspector-wired calls) into the real PersistentCallGroup, so Invoke reaches them as in Unity
                if (n is YMap em && depth < 10)
                {
                    ReadFields(em, ev, t, resolve, depth + 1);
                    if (ev is ISerializationCallbackReceiver er) { try { er.OnAfterDeserialize(); } catch (Exception e) { Problem($"{t.Name}.OnAfterDeserialize", e); } }
                }
                return ev;
            }
            var obj = cur ?? CreateSerializable(t);
            if (n is YMap m && depth < 10) ReadFields(m, obj, t, resolve, depth + 1);
            if (obj is ISerializationCallbackReceiver r2) { try { r2.OnAfterDeserialize(); } catch (Exception e) { Problem($"{t.Name}.OnAfterDeserialize", e); } }
            return obj;
        }

        public static object CreateSerializable(Type t)
        {
            try { return Activator.CreateInstance(t, true); }
            catch (MissingMethodException) { return RuntimeHelpers.GetUninitializedObject(t); }
        }
        static object DefaultFor(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;

        static object NodeToBuiltin(YNode n, Type t, object cur)
        {
            if (t == typeof(Vector2)) return ReadV2(n);
            if (t == typeof(Vector3)) return ReadV3(n);
            if (t == typeof(Vector4)) { var m = n as YMap; return new Vector4(F(m?["x"]), F(m?["y"]), F(m?["z"]), F(m?["w"])); }
            if (t == typeof(Quaternion)) return ReadQuat(n);
            if (t == typeof(Color)) return ReadColor(n);
            if (t == typeof(Vector2Int)) { var m = n as YMap; return new Vector2Int(Int(m?["x"], 0), Int(m?["y"], 0)); }
            if (t == typeof(Vector3Int)) { var m = n as YMap; return new Vector3Int(Int(m?["x"], 0), Int(m?["y"], 0), Int(m?["z"], 0)); }
            if (t == typeof(Color32)) { uint v = (uint)Long((n as YMap)?["rgba"]); return new Color32((byte)(v & 255), (byte)((v >> 8) & 255), (byte)((v >> 16) & 255), (byte)(v >> 24)); }
            if (t == typeof(Rect)) { var m = n as YMap; return new Rect(F(m?["x"]), F(m?["y"]), F(m?["width"]), F(m?["height"])); }
            if (t == typeof(RectInt)) { var m = n as YMap; return new RectInt(Int(m?["x"], 0), Int(m?["y"], 0), Int(m?["width"], 0), Int(m?["height"], 0)); }
            if (t == typeof(LayerMask)) { LayerMask l = (int)(uint)Long((n as YMap)?["m_Bits"]); return l; }
            if (t == typeof(Bounds)) { var m = n as YMap; return new Bounds(ReadV3(m?["m_Center"]), ReadV3(m?["m_Extent"]) * 2); }
            if (t == typeof(AnimationCurve)) return ReadCurve(n);
            if (t == typeof(Matrix4x4)) { var m = n as YMap; var mm = new Matrix4x4(); for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) mm[i, j] = F(m?[$"e{i}{j}"]); return mm; }
            if (t == typeof(RectOffset)) { var m = n as YMap; return new RectOffset(Int(m?["m_Left"], 0), Int(m?["m_Right"], 0), Int(m?["m_Top"], 0), Int(m?["m_Bottom"], 0)); }
            if (t == typeof(Gradient)) return Gradient.FromYaml(n);
            if (t == typeof(GUIStyle) || t == typeof(Hash128) || t == typeof(BoundsInt)) return cur;
            return null;
        }

        public static AnimationCurve ReadCurve(YNode n)
        {
            var c = new AnimationCurve();
            if (n is not YMap m) return c;
            var keys = new List<Keyframe>();
            if (m["m_Curve"] is YSeq seq)
                foreach (var it in seq.Items)
                {
                    var k = it as YMap;
                    var kf = new Keyframe(F(k["time"]), F(k["value"]), F(k["inSlope"]), F(k["outSlope"]), F(k["inWeight"]), F(k["outWeight"]));
                    kf.m_TangentMode = Int(k["tangentMode"], 0);
                    kf.weightedMode = (WeightedMode)Int(k["weightedMode"], 0);
                    keys.Add(kf);
                }
            c.m_Keys = keys;
            c.m_PreInfinity = InternalToWrap(Int(m["m_PreInfinity"], 2));
            c.m_PostInfinity = InternalToWrap(Int(m["m_PostInfinity"], 2));
            return c;
        }

        // ------------------------------------------------------------------ helpers
        public static float F(YNode n) => n is YScalar s ? s.F : 0f;
        public static int Int(YNode n, int def) => n is YScalar s && s.Value.Length > 0 ? (int)s.L : def;
        public static long Long(YNode n) => n is YScalar s && s.Value.Length > 0 ? s.L : 0;
        public static string Str(YNode n) => (n as YScalar)?.Value ?? "";
        public static Vector2 ReadV2(YNode n) { var m = n as YMap; return new Vector2(F(m?["x"]), F(m?["y"])); }
        public static List<Vector2> ReadV2List(YNode n) { var l = new List<Vector2>(); if (n is YSeq s) foreach (var i in s.Items) l.Add(ReadV2(i)); return l; }
        public static YSeq V2List(IEnumerable<Vector2> pts) { var s = new YSeq(); foreach (var p in pts) s.Items.Add(Vec(p)); return s; }
        public static Vector3 ReadV3(YNode n) { var m = n as YMap; return new Vector3(F(m?["x"]), F(m?["y"]), F(m?["z"])); }
        public static Quaternion ReadQuat(YNode n) { var m = n as YMap; return m == null ? Quaternion.identity : new Quaternion(F(m["x"]), F(m["y"]), F(m["z"]), F(m["w"])); }
        public static Color ReadColor(YNode n) { var m = n as YMap; return new Color(F(m?["r"]), F(m?["g"]), F(m?["b"]), F(m?["a"])); }
    }

    /// <summary>Native classes that mirror some state in C# fields (sync before serialize / after deserialize).</summary>
    public interface INativeSync
    {
        void SyncToNative();
        void SyncFromNative();
    }
}
