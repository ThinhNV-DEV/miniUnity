using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiniUnity
{
    /// <summary>Edit-mode lifecycle emulation: AddComponent (RequireComponent), Awake/OnEnable/OnDisable/OnDestroy for
    /// [ExecuteAlways]/[ExecuteInEditMode] behaviours, ScriptableObject callbacks, hierarchy messages.</summary>
    public static class Lifecycle
    {
        public static bool Trace = Environment.GetEnvironmentVariable("MINIUNITY_TRACE") == "1";
        static readonly Dictionary<Type, bool> execCache = new();

        public static bool ExecutesInEditMode(Type t)
        {
            if (Play.IsPlaying) return true;   // play mode: every behaviour gets its messages
            if (execCache.TryGetValue(t, out var b)) return b;
            b = t.GetCustomAttributes(typeof(ExecuteAlways), true).Length > 0 || t.GetCustomAttributes(typeof(ExecuteInEditMode), true).Length > 0;
            execCache[t] = b;
            return b;
        }

        static MethodInfo FindMethod(Type t, string name, Type[] args = null)
        {
            for (var c = t; c != null && c != typeof(MonoBehaviour) && c != typeof(ScriptableObject) && c != typeof(Object); c = c.BaseType)
            {
                var m = c.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, args ?? Type.EmptyTypes, null);
                if (m != null) return m;
            }
            return null;
        }

        public static void Invoke(Object o, string name)
        {
            if (o is MonoBehaviour mb && !EnableState(mb, name)) return;
            var m = FindMethod(o.GetType(), name);
            if (m == null) return;
            if (Trace) Console.Error.WriteLine($"[life] {name} {o.GetType().Name} '{SafeName(o)}'");
            try { m.Invoke(o, null); }
            catch (TargetInvocationException e) { throw new Exception($"{o.GetType().Name}.{name} threw: {e.InnerException?.Message}", e.InnerException); }
        }
        /// <summary>OnDisable only follows an OnEnable, as in Unity: a behaviour that disables itself in Awake gets neither.</summary>
        internal static bool EnableState(MonoBehaviour mb, string name)
        {
            if (name == "OnEnable") mb.m_EnableSent = true;
            else if (name == "OnDisable") { if (!mb.m_EnableSent) return false; mb.m_EnableSent = false; }
            return true;
        }
        static string SafeName(Object o) { try { return o is Component c ? c.m_GameObject?.m_Name : o.m_Name; } catch { return "?"; } }

        public static void Send(GameObject go, string name, object arg)
        {
            foreach (var c in go.m_Components.ToList())
            {
                if (c is not MonoBehaviour mb) continue;
                var m = arg == null ? FindMethod(c.GetType(), name) : FindMethod(c.GetType(), name, new[] { arg.GetType() }) ?? FindMethod(c.GetType(), name);
                if (m == null) continue;
                m.Invoke(c, m.GetParameters().Length == 0 ? null : new[] { arg });
            }
        }

        /// <summary>Create the component object and attach it (no callbacks).</summary>
        public static Component CreateComponentRaw(GameObject go, Type t)
        {
            var c = (Component)Activator.CreateInstance(t, true);
            if (c is MonoBehaviour) Serializer.EnsureDefaults(c);
            c.m_GameObject = go;
            go.m_Components.Add(c);
            return c;
        }

        public static Component AddComponent(GameObject go, Type t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            UnityEngine.Object.SceneEdited();
            if (t.IsAbstract || t.IsInterface) { Debug.LogError($"Cannot add abstract component {t}"); return null; }
            if ((t == typeof(Transform) || t == typeof(RectTransform)))
            {
                if (t == typeof(RectTransform) && !(go.m_Transform is RectTransform)) return ReplaceWithRect(go);
                return go.m_Transform;
            }
            if (t.GetCustomAttributes(typeof(DisallowMultipleComponent), true).Length > 0 && go.GetComponent(t) != null)
            {
                Debug.LogWarning($"Can't add component '{t.Name}' to {go.m_Name} because such a component is already added");
                return null;
            }
            // RequireComponent (native components declare theirs in C++: Canvas needs a RectTransform)
            if (t == typeof(Canvas) && !(go.m_Transform is RectTransform)) ReplaceWithRect(go);
            foreach (RequireComponent rc in t.GetCustomAttributes(typeof(RequireComponent), true))
                foreach (var req in new[] { rc.m_Type0, rc.m_Type1, rc.m_Type2 })
                {
                    if (req == null) continue;
                    if (req == typeof(RectTransform)) { if (!(go.m_Transform is RectTransform)) ReplaceWithRect(go); continue; }
                    if (req == typeof(Transform)) continue;
                    if (go.GetComponent(req) == null)
                    {
                        var concrete = req;
                        if (concrete.IsAbstract) continue;
                        AddComponent(go, concrete);
                    }
                }
            var c = CreateComponentRaw(go, t);
            // native requirement: a ParticleSystem brings its ParticleSystemRenderer (added right after it, as in Unity)
            if (t == typeof(ParticleSystem) && go.GetComponent<ParticleSystemRenderer>() == null) AddComponent(go, typeof(ParticleSystemRenderer));
            if (Trace) Console.Error.WriteLine($"[life] AddComponent {t.Name} → '{go.m_Name}'");
            if (c is MonoBehaviour mb)
            {
                bool exec = ExecutesInEditMode(t);
                if (exec && go.activeInHierarchy)
                {
                    mb.m_Awoken = true;
                    Invoke(mb, "Awake");
                    if (mb.m_Enabled && !mb.m_Destroyed) Invoke(mb, "OnEnable");
                }
                if (!mb.m_Destroyed) Invoke(mb, "Reset");
                // Cinemachine's editor assembly builds vcam pipelines through Undo, which round-trips the vcam through
                // serialization: OnAfterDeserialize stamps m_StreamingVersion (seen in every Unity-saved vcam)
                if (!mb.m_Destroyed && mb is ISerializationCallbackReceiver scr && t.Namespace == "Cinemachine") scr.OnAfterDeserialize();
                if (!mb.m_Destroyed && EditorCallbacks.CallOnValidateOnAdd) Invoke(mb, "OnValidate");
            }
            return c;
        }

        static RectTransform ReplaceWithRect(GameObject go)
        {
            var old = go.m_Transform;
            UnityEngine.Object.SceneEdited(); Hier.Version++;
            var rt = (RectTransform)Activator.CreateInstance(typeof(RectTransform), true);
            rt.m_GameObject = go;
            int idx = go.m_Components.IndexOf(old);
            go.m_Components[idx] = rt;
            rt.m_Parent = old.m_Parent;
            if (old.m_Parent != null) { int ci = old.m_Parent.m_Children.IndexOf(old); old.m_Parent.m_Children[ci] = rt; }
            foreach (var ch in old.m_Children) { ch.m_Parent = rt; rt.m_Children.Add(ch); }
            rt.m_LocalRotation = old.m_LocalRotation; rt.m_LocalScale = old.m_LocalScale; rt.m_LocalEulerAnglesHint = old.m_LocalEulerAnglesHint;
            var lp = old.m_LocalPositionRaw;
            go.m_Transform = rt;
            rt.localPosition = lp;
            old.m_Destroyed = true;
            return rt;
        }

        public static ScriptableObject CreateScriptableObject(Type t, bool callbacks)
        {
            var so = (ScriptableObject)Activator.CreateInstance(t, true);
            Serializer.EnsureDefaults(so);
            so.m_Name = "";
            if (callbacks)
            {
                Invoke(so, "Awake");
                Invoke(so, "OnEnable");
            }
            return so;
        }

        public static void ActiveChanged(GameObject go, bool nowActive)
        {
            void Rec(GameObject g, bool isRoot)
            {
                if (!isRoot && !g.m_IsActive) return; // subtree stays inactive
                foreach (var c in g.m_Components.ToList())
                {
                    if (c is not MonoBehaviour mb || !ExecutesInEditMode(mb.GetType()) || mb.m_Destroyed) continue;
                    if (nowActive)
                    {
                        if (!mb.m_Awoken) { mb.m_Awoken = true; Invoke(mb, "Awake"); }
                        if (mb.m_Enabled) Invoke(mb, "OnEnable");
                    }
                    else if (mb.m_Enabled && mb.m_Awoken) Invoke(mb, "OnDisable");
                }
                foreach (var ch in g.m_Transform.m_Children.ToList()) Rec(ch.gameObject, false);
            }
            Rec(go, true);
        }

        static bool SubtreeSkip(GameObject root, GameObject g) => false;

        public static void EnabledChanged(Behaviour b, bool was, bool now)
        {
            if (was == now || b is not MonoBehaviour mb || !ExecutesInEditMode(mb.GetType())) return;
            if (now) { if (!mb.m_Awoken) { mb.m_Awoken = true; Invoke(mb, "Awake"); } Invoke(mb, "OnEnable"); }
            else if (mb.m_Awoken) Invoke(mb, "OnDisable");
        }

        /// <summary>Called after a deserialized (instantiated) hierarchy enters an active scene.</summary>
        public static void AwakeHierarchy(GameObject root)
        {
            if (!root.activeInHierarchy) return;
            foreach (var g in Hier.Walk(root).ToList())
            {
                if (!g.activeInHierarchy) continue;
                foreach (var c in g.m_Components.ToList())
                {
                    if (c is not MonoBehaviour mb || mb.m_Destroyed || !ExecutesInEditMode(mb.GetType())) continue;
                    if (!mb.m_Awoken) { mb.m_Awoken = true; Invoke(mb, "Awake"); }
                }
            }
            foreach (var g in Hier.Walk(root).ToList())
            {
                if (!g.activeInHierarchy) continue;
                foreach (var c in g.m_Components.ToList())
                    if (c is MonoBehaviour mb && !mb.m_Destroyed && mb.m_Enabled && ExecutesInEditMode(mb.GetType())) Invoke(mb, "OnEnable");
            }
        }

        public static void Destroy(Object obj)
        {
            UnityEngine.Object.SceneEdited(); Hier.Version++;
            switch (obj)
            {
                case GameObject go:
                    foreach (var ch in go.m_Transform.m_Children.ToList()) Destroy(ch.gameObject);
                    bool active = go.activeInHierarchy;
                    foreach (var c in go.m_Components.ToList())
                    {
                        if (c is MonoBehaviour mb && ExecutesInEditMode(mb.GetType()))
                        {
                            if (active && mb.m_Enabled && mb.m_Awoken) Invoke(mb, "OnDisable");
                            if (mb.m_Awoken) Invoke(mb, "OnDestroy");
                        }
                    }
                    var t = go.m_Transform;
                    if (t.m_Parent != null) t.m_Parent.m_Children.Remove(t);
                    else go.m_Scene?.RemoveRoot(go);
                    foreach (var c in go.m_Components) c.m_Destroyed = true;
                    go.m_Destroyed = true;
                    break;
                case Transform:
                    Debug.LogError("Can't destroy Transform component. Destroy the GameObject instead.");
                    break;
                case Component comp:
                    if (comp is MonoBehaviour m2 && ExecutesInEditMode(m2.GetType()))
                    {
                        if (m2.isActiveAndEnabled && m2.m_Awoken) Invoke(m2, "OnDisable");
                        if (m2.m_Awoken) Invoke(m2, "OnDestroy");
                    }
                    comp.m_GameObject?.m_Components.Remove(comp);
                    comp.m_Destroyed = true;
                    break;
                case ScriptableObject so:
                    Invoke(so, "OnDisable");
                    Invoke(so, "OnDestroy");
                    so.m_Destroyed = true;
                    break;
                default:
                    obj.m_Destroyed = true;
                    break;
            }
        }

        public static void ParentChanged(Transform t)
        {
            foreach (var g in Hier.Walk(t.gameObject).ToList())
                foreach (var c in g.m_Components.ToList())
                    if (c is MonoBehaviour mb && mb.m_Awoken && ExecutesInEditMode(mb.GetType()) && mb.isActiveAndEnabled)
                    {
                        if (g == t.gameObject) Invoke(mb, "OnTransformParentChanged");
                        Invoke(mb, "OnCanvasHierarchyChanged");
                    }
            if (t.m_Parent != null)
                foreach (var c in t.m_Parent.gameObject.m_Components.ToList())
                    if (c is MonoBehaviour mb && mb.m_Awoken && ExecutesInEditMode(mb.GetType()) && mb.isActiveAndEnabled) Invoke(mb, "OnTransformChildrenChanged");
        }

        public static void SiblingChanged(Transform t)
        {
            if (t.m_Parent != null)
                foreach (var c in t.m_Parent.gameObject.m_Components.ToList())
                    if (c is MonoBehaviour mb && mb.m_Awoken && ExecutesInEditMode(mb.GetType()) && mb.isActiveAndEnabled) Invoke(mb, "OnTransformChildrenChanged");
        }

        public static void RectDimensionsChanged(RectTransform rt)
        {
            if (rt.m_GameObject == null) return;
            foreach (var c in rt.m_GameObject.m_Components.ToList())
                if (c is MonoBehaviour mb && mb.m_Awoken && ExecutesInEditMode(mb.GetType()) && mb.isActiveAndEnabled) Invoke(mb, "OnRectTransformDimensionsChange");
        }
    }

    public static class EditorCallbacks
    {
        public static bool CallOnValidateOnAdd = false;
    }

    public static class TypeCache
    {
        static Dictionary<string, Type> byName;
        /// <summary>Project assemblies loaded by the agent runtime (ua play / ua test); their types win over MiniUnity's own copies.</summary>
        public static readonly List<Assembly> ProjectAssemblies = new();
        /// <summary>Assemblies of a host process that compiles a project's code next to the engine (e.g. MiniUnity.Video): looked up
        /// like the engine's own types, after them, and not ranked as compiled project scripts.</summary>
        public static readonly List<Assembly> HostAssemblies = new();
        public static void AddHostAssembly(Assembly a) { if (!HostAssemblies.Contains(a)) HostAssemblies.Add(a); byName = null; }
        static IEnumerable<Type> EngineAndHostTypes => typeof(TypeCache).Assembly.GetTypes().Concat(HostAssemblies.SelectMany(SafeTypes));
        public static void AddProjectAssembly(Assembly a) { ProjectAssemblies.Add(a); byName = null; }
        static IEnumerable<Type> SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
        }
        public static Type FindType(string name)
        {
            if (byName == null)
            {
                byName = new();
                foreach (var t in ProjectAssemblies.SelectMany(SafeTypes).Concat(EngineAndHostTypes))
                {
                    byName.TryAdd(t.Name, t);
                    if (t.FullName != null) byName.TryAdd(t.FullName, t);
                }
            }
            return byName.TryGetValue(name, out var r) ? r : null;
        }
        public static IEnumerable<Type> AllTypes => ProjectAssemblies.SelectMany(SafeTypes).Concat(EngineAndHostTypes);
        public static bool IsProjectType(Type t) => ProjectAssemblies.Contains(t.Assembly);
    }
}
