using System;
using System.Collections.Generic;
using System.Linq;
using MiniUnity;

namespace UnityEngine
{
    public partial class Object
    {
        static int s_NextId = 2;
        internal int m_InstanceID;
        internal bool m_Destroyed;
        internal string m_Name = "";
        internal HideFlags m_HideFlags;
        /// <summary>Persistent identity (asset objects): guid + local fileID in that asset.</summary>
        internal AssetIdentity m_Asset;
        /// <summary>Native serialized state for engine (C++) classes: the YAML body Unity would write.</summary>
        internal MiniUnity.Yaml.YMap m_Native;
        /// <summary>Prefab instance bookkeeping: source object in the prefab asset.</summary>
        internal Object m_CorrespondingSource;
        internal PrefabInstanceData m_PrefabInstance;
        /// <summary>Unity "fake null" for components created with new().</summary>
        internal bool m_Constructed;

        public Object()
        {
            m_InstanceID = s_NextId; s_NextId += 2;
            ObjectRegistry.Register(this);
            m_Constructed = true;
            NativeInit();
        }

        /// <summary>Called on construction: native classes set up their default serialized body.</summary>
        internal virtual void NativeInit()
        {
            var tpl = NativeTemplates.For(GetType());
            if (tpl != null) m_Native = (MiniUnity.Yaml.YMap)tpl.Clone();
            // a native component without a template starts empty: written with only the fields set from code (Unity fills the
            // rest with its defaults when it loads the file)
            else if (this is Component && !(this is MonoBehaviour) && Serializer.TypeById.ContainsValue(GetType())) m_Native = new MiniUnity.Yaml.YMap();
        }

        public int GetInstanceID() => m_InstanceID;
        public override int GetHashCode() => m_InstanceID;
        public override bool Equals(object other) => other is Object o ? this == o : other == null && !IsAlive(this);

        internal static bool IsAlive(Object o) => !ReferenceEquals(o, null) && !o.m_Destroyed;

        public static implicit operator bool(Object exists) => IsAlive(exists);
        public static bool operator ==(Object x, Object y)
        {
            bool xn = !IsAlive(x), yn = !IsAlive(y);
            if (xn && yn) return true;
            if (xn || yn) return false;
            return ReferenceEquals(x, y);
        }
        public static bool operator !=(Object x, Object y) => !(x == y);

        public virtual string name
        {
            get { ThrowIfDestroyed(); return this is Component c ? c.gameObject.m_Name : m_Name; }
            set { ThrowIfDestroyed(); if (this is Component c) c.gameObject.m_Name = value ?? ""; else m_Name = value ?? ""; }
        }
        public HideFlags hideFlags { get => m_HideFlags; set => m_HideFlags = value; }

        internal void ThrowIfDestroyed()
        {
            if (m_Destroyed) throw new MissingReferenceException($"The object of type '{GetType().Name}' has been destroyed but you are still trying to access it.");
        }

        public override string ToString() => IsAlive(this) ? $"{m_Name} ({GetType().FullName})" : "null";

        // ---------------- Instantiate ----------------
        public static Object Instantiate(Object original) => ObjectCloner.Instantiate(original, null, false);
        public static Object Instantiate(Object original, Transform parent) => ObjectCloner.Instantiate(original, parent, false);
        public static Object Instantiate(Object original, Transform parent, bool instantiateInWorldSpace) => ObjectCloner.Instantiate(original, parent, instantiateInWorldSpace);
        public static Object Instantiate(Object original, Vector3 position, Quaternion rotation)
        {
            var o = ObjectCloner.Instantiate(original, null, false);
            var t = ObjectCloner.TransformOf(o); if (t != null) { t.position = position; t.rotation = rotation; }
            return o;
        }
        public static Object Instantiate(Object original, Vector3 position, Quaternion rotation, Transform parent)
        {
            var o = ObjectCloner.Instantiate(original, parent, false);
            var t = ObjectCloner.TransformOf(o); if (t != null) { t.position = position; t.rotation = rotation; }
            return o;
        }
        public static T Instantiate<T>(T original) where T : Object => (T)Instantiate((Object)original);
        public static T Instantiate<T>(T original, Transform parent) where T : Object => (T)Instantiate((Object)original, parent);
        public static T Instantiate<T>(T original, Transform parent, bool worldPositionStays) where T : Object => (T)Instantiate((Object)original, parent, worldPositionStays);
        public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation) where T : Object => (T)Instantiate((Object)original, position, rotation);
        public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation, Transform parent) where T : Object => (T)Instantiate((Object)original, position, rotation, parent);

        // ---------------- Destroy ----------------
        public static void Destroy(Object obj) => DestroyImmediate(obj);
        public static void Destroy(Object obj, float t)
        {
            // delayed destroy in play mode (scaled time, processed at the end of the frame like Unity)
            if (t > 0 && MiniUnity.Play.IsPlaying) { MiniUnity.Play.DestroyLater(obj, t); return; }
            DestroyImmediate(obj);
        }
        public static void DestroyImmediate(Object obj) => DestroyImmediate(obj, false);
        public static void DestroyImmediate(Object obj, bool allowDestroyingAssets)
        {
            if (!IsAlive(obj)) return;
            Lifecycle.Destroy(obj);
        }
        public static void DontDestroyOnLoad(Object target) { }

        // ---------------- Find ----------------
        public static T[] FindObjectsOfType<T>() where T : Object => FindObjectsOfType<T>(false);
        public static T[] FindObjectsOfType<T>(bool includeInactive) where T : Object => ObjectRegistry.SceneObjects(typeof(T), includeInactive).Cast<T>().ToArray();
        public static Object[] FindObjectsOfType(Type type) => ObjectRegistry.SceneObjects(type, false).ToArray();
        public static Object[] FindObjectsOfType(Type type, bool includeInactive) => ObjectRegistry.SceneObjects(type, includeInactive).ToArray();
        public static T FindObjectOfType<T>() where T : Object => FindObjectsOfType<T>().FirstOrDefault();
        public static T FindObjectOfType<T>(bool includeInactive) where T : Object => FindObjectsOfType<T>(includeInactive).FirstOrDefault();
        public static Object FindObjectOfType(Type type) => FindObjectsOfType(type).FirstOrDefault();
        public static Object FindObjectOfType(Type type, bool includeInactive) => FindObjectsOfType(type, includeInactive).FirstOrDefault();
        public static T[] FindObjectsByType<T>(FindObjectsSortMode sortMode) where T : Object => FindObjectsOfType<T>();
        public static T[] FindObjectsByType<T>(FindObjectsInactive findObjectsInactive, FindObjectsSortMode sortMode) where T : Object => FindObjectsOfType<T>(findObjectsInactive == FindObjectsInactive.Include);
        public static Object[] FindObjectsByType(Type type, FindObjectsSortMode sortMode) => FindObjectsOfType(type);
        public static Object[] FindObjectsByType(Type type, FindObjectsInactive findObjectsInactive, FindObjectsSortMode sortMode) => FindObjectsOfType(type, findObjectsInactive == FindObjectsInactive.Include);
        public static T FindFirstObjectByType<T>() where T : Object => FindObjectOfType<T>();
        public static T FindAnyObjectByType<T>() where T : Object => FindObjectOfType<T>();
        public static T FindFirstObjectByType<T>(FindObjectsInactive i) where T : Object => FindObjectOfType<T>(i == FindObjectsInactive.Include);
        public static T FindAnyObjectByType<T>(FindObjectsInactive i) where T : Object => FindObjectOfType<T>(i == FindObjectsInactive.Include);
        public static Object FindFirstObjectByType(Type type) => FindObjectOfType(type);
        public static Object FindAnyObjectByType(Type type) => FindObjectOfType(type);
        public static Object[] FindObjectsOfTypeAll(Type type) => ObjectRegistry.All(type).ToArray();

        internal static bool IsPersistent(Object obj) => obj.m_Asset != null;
        internal static Object FindObjectFromInstanceID(int instanceID) => ObjectRegistry.ById(instanceID);
        internal static bool DoesObjectWithInstanceIDExist(int instanceID) => ObjectRegistry.ById(instanceID) != null;
        internal void MarkDirty() { }
    }

}

namespace MiniUnity
{
    public sealed class AssetIdentity
    {
        public string Path;
        public string Guid;
        public long FileID;
        public int RefType; // 2 = native asset (.asset/.playable/.prefab? no → 3), 3 = imported/meta-based
        public override string ToString() => $"{Path}#{FileID}";
    }

    /// <summary>Bookkeeping for a prefab instance root (written as PrefabInstance in scenes).</summary>
    public sealed class PrefabInstanceData
    {
        public UnityEngine.GameObject PrefabAsset;   // root of the prefab asset
        public string PrefabGuid;
        public UnityEngine.GameObject InstanceRoot;
        public List<UnityEngine.Object> Objects = new(); // all instance objects that correspond to prefab objects
        public UnityEngine.Object Handle;                // PrefabUtility.GetPrefabInstanceHandle
    }

    /// <summary>Stand-in for Unity's PrefabInstance object (PrefabUtility.GetPrefabInstanceHandle).</summary>
    public sealed class PrefabInstanceHandle : UnityEngine.Object { }

    public static class ObjectRegistry
    {
        static readonly Dictionary<int, WeakReference<UnityEngine.Object>> byId = new();
        static readonly List<WeakReference<UnityEngine.Object>> all = new();
        public static void Register(UnityEngine.Object o) { var w = new WeakReference<UnityEngine.Object>(o); byId[o.m_InstanceID] = w; all.Add(w); }
        public static UnityEngine.Object ById(int id) => byId.TryGetValue(id, out var w) && w.TryGetTarget(out var o) && !o.m_Destroyed ? o : null;
        public static IEnumerable<UnityEngine.Object> All(Type t)
        {
            foreach (var w in all) if (w.TryGetTarget(out var o) && !o.m_Destroyed && t.IsInstanceOfType(o)) yield return o;
        }
        public static IEnumerable<UnityEngine.Object> SceneObjects(Type t, bool includeInactive)
        {
            var scene = SceneData.Active;
            if (scene == null) yield break;
            foreach (var root in scene.Roots.ToList())
            {
                foreach (var go in Hier.Walk(root))
                {
                    if (!includeInactive && !go.activeInHierarchy) continue;
                    if (t.IsInstanceOfType(go)) { yield return go; continue; }
                    foreach (var c in go.m_Components) if (t.IsInstanceOfType(c) && (includeInactive || !(c is UnityEngine.Behaviour b) || b.enabled)) yield return c;
                }
            }
        }
    }

    public static class Hier
    {
        /// <summary>Pre-order walk. Each object's children are taken when the walk reaches them (after the object itself was
        /// handed out), so objects reparented or added under a not yet visited object are seen, as with a recursive walk.</summary>
        public static IEnumerable<UnityEngine.GameObject> Walk(UnityEngine.GameObject root)
        {
            yield return root;
            var stack = new Stack<UnityEngine.Transform>();
            Push(stack, root);
            while (stack.Count > 0)
            {
                var g = stack.Pop().gameObject;
                yield return g;
                Push(stack, g);
            }
        }
        static void Push(Stack<UnityEngine.Transform> stack, UnityEngine.GameObject g)
        {
            var ch = g.transform.m_Children;
            for (int i = ch.Count - 1; i >= 0; i--) stack.Push(ch[i]);
        }

        /// <summary>Counts hierarchy changes (parenting, sibling order, scene roots, destroy, deserialization).</summary>
        internal static int Version;
        static List<UnityEngine.GameObject> sceneList;
        static SceneData sceneListScene;
        static int sceneListVersion = -1;

        /// <summary>Every object of the scene in hierarchy order (roots in order, each subtree pre-order). The list is shared and
        /// rebuilt (as a new list) after a hierarchy change: read it, do not modify it.</summary>
        public static IReadOnlyList<UnityEngine.GameObject> SceneObjects(SceneData s)
        {
            if (s == null) return Array.Empty<UnityEngine.GameObject>();
            if (sceneList != null && sceneListScene == s && sceneListVersion == Version) return sceneList;
            var list = new List<UnityEngine.GameObject>(sceneList?.Count ?? 16);
            var stack = new Stack<UnityEngine.Transform>();
            foreach (var r in s.Roots)
            {
                list.Add(r); Push(stack, r);
                while (stack.Count > 0) { var g = stack.Pop().gameObject; list.Add(g); Push(stack, g); }
            }
            sceneList = list; sceneListScene = s; sceneListVersion = Version;
            return list;
        }
        public static string Path(UnityEngine.Transform t)
        {
            var parts = new List<string>();
            for (var c = t; c != null; c = c.m_Parent) parts.Add(c.gameObject.m_Name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
